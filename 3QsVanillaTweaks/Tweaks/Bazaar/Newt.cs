using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// Host-authoritative Newt stat configuration and optional kick suppression.
    /// Bazaar expulsion karma is moved to Newt's defeat when configured.
    /// </summary>
    internal static class Newt
    {
        private const string Section = "Bazaar - 01 Newt";
        private const string HarmonyId = "3Questionmarks.3QsVanillaTweaks.Bazaar.Newt";

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> preventKick;
        private static ConfigEntry<float> health;
        private static ConfigEntry<float> healthPerLevel;
        private static ConfigEntry<float> regenerationPercent;
        private static ConfigEntry<float> armor;
        private static ConfigEntry<float> regenerationDelay;
        private static ConfigEntry<bool> karmaOnKill;
        private static ConfigEntry<bool> karmaOncePerVisit;
        private static Stage karmaVisitStage;
        private static bool karmaAwardedThisVisit;
        private static bool grantingKarma;
        private static bool loggedMissingTrigger;
        // Karma types are resolved at runtime: some project RoR2 references predate the karma system.
        private static Type karmaManagerType;
        private static Type karmaTriggerType;
        private static FieldInfo runTriggersField;
        private static MethodInfo karmaEnabledMethod;
        private static MethodInfo karmaCompleteMethod;
        private static PropertyInfo karmaInstanceProperty;
        private static PropertyInfo karmaActiveProperty;
        private static FieldInfo karmaDefField;
        private static FieldInfo karmaIdentifierField;
        private sealed class DamageTracker
        {
            internal float lastDamageTime = float.NegativeInfinity;
        }
        private static readonly ConditionalWeakTable<HealthComponent, DamageTracker> damageTrackers =
            new ConditionalWeakTable<HealthComponent, DamageTracker>();
        private static Harmony harmony;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true, "Enable the Newt/Bazaar tweaks.");
            preventKick = ModConfig.BindBool(Section, "Newt Never Kicks Players Out", true,
                "Prevents Newt from expelling players after taking damage.");
            karmaOnKill = ModConfig.BindBool(Section, "Award Bazaar Karma When Newt Dies", true,
                "Relocates the Bazaar expulsion karma trigger from angering Newt to defeating him.");
            karmaOncePerVisit = ModConfig.BindBool(Section, "Karma Once Per Bazaar Visit", true,
                "When enabled, defeating Newt can award the Bazaar karma trigger only once per Bazaar visit.");

            health = ModConfig.Bind(Section, "Base Health", 2000000f,
                "Maximum health at level 1. Default: 2000000 (4x vanilla). Vanilla: 500000.");
            healthPerLevel = ModConfig.Bind(Section, "Health Per Level", 500000f,
                "Additional maximum health per level. Default: 500000. Vanilla: 150000.");
            regenerationPercent = ModConfig.Bind(Section, "Max Health Regeneration Per Second (%)", 10f,
                "Percentage of Newt's calculated maximum health restored each second. Default: 10. Set to 0 to disable regeneration.");
            regenerationDelay = ModConfig.Bind(Section, "Regeneration Delay After Damage (Seconds)", 3f,
                "Newt only regenerates after this many seconds without taking damage. Further hits reset the delay. Default: 3. Set to 0 for immediate regeneration.");
            armor = ModConfig.Bind(Section, "Armor", 0f,
                "Newt's base armour. Default: 0. Vanilla: 100.");

            harmony = new Harmony(HarmonyId);
            MethodInfo recalc = AccessTools.Method(typeof(CharacterBody), "RecalculateStats");
            if (recalc == null)
                throw new MissingMethodException("CharacterBody.RecalculateStats");
            harmony.Patch(recalc,
                prefix: new HarmonyMethod(typeof(Newt), nameof(BeforeRecalculateStats)),
                postfix: new HarmonyMethod(typeof(Newt), nameof(AfterRecalculateStats)));

            // Damage detection and per-tick regeneration suppression are server-side only.
            // Do not permanently overwrite CharacterBody.regen: other mods may recalculate it.
            MethodInfo damageMethod = AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess");
            MethodInfo healthUpdate = AccessTools.Method(typeof(HealthComponent), "ServerFixedUpdate");
            if (damageMethod == null || healthUpdate == null)
                throw new MissingMethodException("Newt regen delay requires HealthComponent.TakeDamageProcess and ServerFixedUpdate.");
            harmony.Patch(damageMethod,
                prefix: new HarmonyMethod(typeof(Newt), nameof(BeforeDamage)),
                postfix: new HarmonyMethod(typeof(Newt), nameof(AfterDamage)));
            harmony.Patch(healthUpdate,
                prefix: new HarmonyMethod(typeof(Newt), nameof(BeforeHealthUpdate)),
                postfix: new HarmonyMethod(typeof(Newt), nameof(AfterHealthUpdate)));

            // Karma is managed by the game's own trigger registry. Block its usual
            // completion only when we have positively identified the Bazaar trigger.
            InstallKarmaHook();

            // The name can differ between RoR2 builds. Discover the actual shopkeeper kick state
            // rather than adding a hard dependency on any one EntityStates assembly/class name.
            int patched = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (Type type in types)
                {
                    if (type == null || !type.Name.Equals("KickFromShop", StringComparison.Ordinal)) continue;
                    if (!typeof(EntityStates.EntityState).IsAssignableFrom(type)) continue;
                    MethodInfo enter = AccessTools.DeclaredMethod(type, "OnEnter");
                    if (enter == null) continue;
                    harmony.Patch(enter, prefix: new HarmonyMethod(typeof(Newt), nameof(BeforeKickFromShop)));
                    patched++;
                }
            }
            if (patched == 0)
                ThreeQsVanillaTweaks.Log.Warning("Newt: KickFromShop state not found; kick prevention is not active on this build.");
            else
                ThreeQsVanillaTweaks.Log.Info("Newt: hooked " + patched + " KickFromShop state(s).");
        }

        private static bool IsNewt(CharacterBody body)
        {
            if (body == null) return false;
            string name = body.name;
            return name != null && name.StartsWith("ShopkeeperBody", StringComparison.OrdinalIgnoreCase);
        }

        // Set base values before vanilla calculates Newt's level-scaled maximum health.
        private static void BeforeRecalculateStats(CharacterBody __instance)
        {
            if (!ModConfig.Get(enabled) || !IsNewt(__instance)) return;

            __instance.baseMaxHealth = Mathf.Max(1f, ModConfig.Get(health));
            __instance.levelMaxHealth = Mathf.Max(0f, ModConfig.Get(healthPerLevel));
            __instance.baseArmor = ModConfig.Get(armor);
        }

        // Calculate regeneration after vanilla has established maxHealth, so it
        // automatically tracks level scaling and any maximum-health modifiers.
        private static void AfterRecalculateStats(CharacterBody __instance)
        {
            if (!ModConfig.Get(enabled) || !IsNewt(__instance)) return;

            float percent = Mathf.Max(0f, ModConfig.Get(regenerationPercent));
            __instance.regen = Mathf.Max(0f, __instance.maxHealth) * (percent / 100f);
        }

        // Compare combined health, shield and barrier before and after damage resolution.
        // This avoids resetting the timer for blocked or fully mitigated hits.
        private static float RemainingDurability(HealthComponent hc)
        {
            return hc.health + hc.shield + hc.barrier;
        }

        private static void BeforeDamage(HealthComponent __instance, out float __state)
        {
            __state = (NetworkServer.active && ModConfig.Get(enabled) && IsNewt(__instance.body))
                ? RemainingDurability(__instance)
                : -1f;
        }

        private static void AfterDamage(HealthComponent __instance, float __state)
        {
            if (__state < 0f || !NetworkServer.active || !ModConfig.Get(enabled)) return;
            if (RemainingDurability(__instance) < __state - 0.001f)
                damageTrackers.GetOrCreateValue(__instance).lastDamageTime = Time.fixedTime;
            // Newt is effectively defeated at zero health, even if the game's
            // shopkeeper death animation/despawn differs between builds.
            if (__instance.health <= 0f)
                TryAwardKarmaOnDeath();
        }

        private static void BeforeHealthUpdate(HealthComponent __instance, out float __state)
        {
            __state = float.NaN;
            if (!NetworkServer.active || !ModConfig.Get(enabled) || !IsNewt(__instance.body)) return;
            float delay = Mathf.Max(0f, ModConfig.Get(regenerationDelay));
            if (delay <= 0f) return;
            DamageTracker tracker = damageTrackers.GetOrCreateValue(__instance);
            if (Time.fixedTime - tracker.lastDamageTime >= delay) return;

            CharacterBody body = __instance.body;
            __state = body.regen;
            body.regen = 0f;
        }

        private static void AfterHealthUpdate(HealthComponent __instance, float __state)
        {
            if (!float.IsNaN(__state) && __instance.body != null)
                __instance.body.regen = __state;
        }

        private static bool BeforeKickFromShop(EntityStates.EntityState __instance)
        {
            if (!NetworkServer.active || !ModConfig.Get(enabled) || !ModConfig.Get(preventKick))
                return true;
            // Exit the kick state without running its OnEnter (which expels the players).
            // The normal shopkeeper entity and shop interactables are left untouched.
            if (__instance.outer != null)
                __instance.outer.SetNextStateToMain();
            return false;
        }

        private static void RefreshVisit()
        {
            Stage current = Stage.instance;
            if (current == karmaVisitStage) return;
            karmaVisitStage = current;
            karmaAwardedThisVisit = false;
            loggedMissingTrigger = false;
        }

        private static void InstallKarmaHook()
        {
            karmaManagerType = AccessTools.TypeByName("RoR2.KarmaManager");
            karmaTriggerType = AccessTools.TypeByName("RoR2.Karma.BaseKarmaTrigger");
            Type defType = AccessTools.TypeByName("RoR2.KarmaTriggerDef");
            if (karmaManagerType == null || karmaTriggerType == null || defType == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt: karma types not present at runtime; karma relocation disabled (other Newt tweaks remain active).");
                return;
            }

            runTriggersField = AccessTools.Field(karmaManagerType, "runTriggers");
            karmaInstanceProperty = AccessTools.Property(karmaManagerType, "instance");
            karmaEnabledMethod = AccessTools.Method(karmaManagerType, "IsKarmaEnabled");
            karmaCompleteMethod = AccessTools.Method(karmaTriggerType, "MarkAsCompleted");
            karmaActiveProperty = AccessTools.Property(karmaTriggerType, "active");
            karmaDefField = AccessTools.Field(karmaTriggerType, "karmaTriggerDef");
            karmaIdentifierField = AccessTools.Field(defType, "identifier");
            MethodInfo flipped = AccessTools.Method(karmaManagerType, "OnKarmaTriggerFlipped");
            if (flipped == null || runTriggersField == null || karmaCompleteMethod == null ||
                karmaInstanceProperty == null || karmaDefField == null || karmaIdentifierField == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt: some karma members are unavailable; karma relocation disabled.");
                return;
            }
            harmony.Patch(flipped, prefix: new HarmonyMethod(typeof(Newt), nameof(BeforeKarmaFlipped)));
            ThreeQsVanillaTweaks.Log.Info("Newt: karma trigger interception installed.");
        }

        private static string KarmaId(object trigger)
        {
            if (trigger == null || karmaDefField == null || karmaIdentifierField == null) return null;
            object def = karmaDefField.GetValue(trigger);
            return def == null ? null : karmaIdentifierField.GetValue(def) as string;
        }

        private static bool IsBazaarKarmaTrigger(object trigger)
        {
            if (trigger == null) return false;
            return HasBazaarName(KarmaId(trigger)) || HasBazaarName(trigger.GetType().Name);
        }

        private static bool HasBazaarName(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf("bazaar", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("newt", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("shopkeeper", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // __0 is the original first argument, without referencing BaseKarmaTrigger at compile time.
        private static bool BeforeKarmaFlipped(object __0)
        {
            if (!NetworkServer.active || !ModConfig.Get(enabled) || !ModConfig.Get(karmaOnKill)) return true;
            if (!IsBazaarKarmaTrigger(__0)) return true;
            RefreshVisit();
            if (grantingKarma) return !ModConfig.Get(karmaOncePerVisit) || !karmaAwardedThisVisit;
            return false;
        }

        private static void TryAwardKarmaOnDeath()
        {
            if (!NetworkServer.active || !ModConfig.Get(enabled) || !ModConfig.Get(karmaOnKill)) return;
            RefreshVisit();
            if (ModConfig.Get(karmaOncePerVisit) && karmaAwardedThisVisit) return;
            if (karmaInstanceProperty == null || runTriggersField == null || karmaCompleteMethod == null) return;
            try
            {
                object manager = karmaInstanceProperty.GetValue(null, null);
                if (manager == null) return;
                if (karmaEnabledMethod != null && !(bool)karmaEnabledMethod.Invoke(manager, null)) return;
                IEnumerable triggers = runTriggersField.GetValue(manager) as IEnumerable;
                if (triggers == null) return;
                object selected = null;
                int matches = 0;
                var names = new List<string>();
                foreach (object candidate in triggers)
                {
                    if (candidate == null) continue;
                    names.Add((KarmaId(candidate) ?? "(none)") + " [" + candidate.GetType().Name + "]");
                    if (!IsBazaarKarmaTrigger(candidate)) continue;
                    selected = candidate;
                    matches++;
                }
                if (matches != 1)
                {
                    if (!loggedMissingTrigger)
                    {
                        loggedMissingTrigger = true;
                        ThreeQsVanillaTweaks.Log.Warning("Newt: expected exactly one Bazaar karma trigger, found " + matches
                            + ". No karma awarded. Available triggers: " + string.Join(", ", names.ToArray()));
                    }
                    return;
                }
                if (karmaActiveProperty != null && !(bool)karmaActiveProperty.GetValue(selected, null)) return;
                grantingKarma = true;
                try
                {
                    karmaCompleteMethod.Invoke(selected, null);
                    karmaAwardedThisVisit = true;
                    ThreeQsVanillaTweaks.Log.Info("Newt: completed the existing Bazaar karma trigger on defeat.");
                }
                finally { grantingKarma = false; }
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt: karma completion failed: " + e);
            }
        }
    }
}
