using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class MULT
    {
        private const string Root = "Survivors - 04 MUL-T", StatsSection = "Survivor-Stats - 04 MUL-T";
        private static readonly string[] Names = { "Auto-Nailgun", "Rebar Puncher", "Scrap Launcher", "Power-Saw", "Blast Canister", "Transport Mode", "Retool", "Power Mode" };
        private static readonly string[] States = { "FireNailgun", "FireSpear", "FireGrenadeLauncher", "FireBuzzsaw", "AimStunDrone", "ToolbotDash", "StartToolbotStanceSwap", "ToolbotDualWieldStart" };
        private static readonly string[] Activations = { "FireNailgun", "ChargeSpear", "FireGrenadeLauncher", "FireBuzzsaw", "AimStunDrone", "ToolbotDash", "StartToolbotStanceSwap", "ToolbotDualWieldStart" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal float NativeDamage, NativeProc;
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly List<Profile> allProfiles = new List<Profile>();
        private static readonly string[] PrimaryTokens = { "TOOLBOT_PRIMARY_NAME", "TOOLBOT_PRIMARY_ALT1_NAME", "TOOLBOT_PRIMARY_ALT2_NAME", "TOOLBOT_PRIMARY_ALT3_NAME" };
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<RuntimeMethodHandle> patchedEntries = new HashSet<RuntimeMethodHandle>();
        private static ConfigEntry<bool> enabled, statsEnabled, rapid, icbm, jump, reload;
        private static ConfigEntry<int> scrapSpeed, barrierConversion, bombletDamage, bombletCount, transportArmour, transportSpeed, ramBonus, powerArmour, powerSlow, powerDamage;
        private static ConfigEntry<float> swapTime;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static BuffDef powerPenalty, powerBonus;
        private static float nativeSlow;
        private static MethodInfo jumpVelocity;
        [ThreadStatic] private static Profile active;
        [ThreadStatic] private static CharacterBody activeBody;
        [ThreadStatic] private static bool extraRocket;
        [ThreadStatic] private static int activeIndex;
        private sealed class Scope { internal Profile Previous; internal CharacterBody Body; internal int Index; }
        private sealed class MeleeMarker { internal Profile Profile; internal CharacterBody Body; internal float Damage; internal int Index; }
        private sealed class MeleeScope { internal float Damage, Proc; }
        private static readonly ConditionalWeakTable<OverlapAttack, MeleeMarker> melee = new ConditionalWeakTable<OverlapAttack, MeleeMarker>();
        private sealed class RocketVolley { internal FireProjectileInfo Info; }
        private sealed class ProjectileMarker : MonoBehaviour
        {
            internal Profile Profile;
            internal Vector3 Origin;
            internal float BaseDamage;
        }
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Toolbot." + name);
        private static bool IsMULT(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool Authority(EntityState state) => S.Read(state, "isAuthority") is bool value && value;
        private static bool HasState(CharacterBody body, string name) => body && body.GetComponents<EntityStateMachine>().Any(machine => machine.state?.GetType().Name == name);
        private static bool InPowerMode(CharacterBody body) => profiles.ContainsKey(7) && IsMULT(body) && body.GetComponents<EntityStateMachine>().Any(machine => State("ToolbotDualWieldBase").IsInstanceOfType(machine.state));
        private static float PrimaryMultiplier(CharacterBody body) => InPowerMode(body) ? 1f + C.Get(powerDamage) / 100f : 1f;
        private static Profile ForState(EntityState state)
        {
            string name = state?.GetType().Name;
            if (name == "NailgunFinalBurst" || name == "NailgunSpinDown") return profiles.TryGetValue(0, out Profile nail) ? nail : null;
            if (name == "ToolbotStanceSwap") return profiles.TryGetValue(6, out Profile swap) ? swap : null;
            int index = Array.IndexOf(States, name);
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (method == "OnEnter" && !patchedEntries.Add(target.MethodHandle)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(MULT), prefix), postfix == null ? null : new HarmonyMethod(typeof(MULT), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(MULT), finalizer));
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable MUL-T changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsSection, "Changes Enabled", true, "Enable MUL-T stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Toolbot/ToolbotBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.MULT");
                if (statsEnabled.Value)
                {
                    // Bind the changed default first; Stats reuses the existing entry and its vanilla description.
                    S.Float(StatsSection, "Armour", 20f, prefabBody.baseArmor, "Base armour.", -10000f);
                    stats = new S.Stats(StatsSection, prefabBody); stats.Apply(prefabBody);
                    Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(slot => slot.skillFamily)
                    .SelectMany(slot => slot.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(States[i]);
                    SkillDef[] variants = defs.Where(d => d.activationState.stateType?.Name == Activations[i]
                        || d.activationState.stateType?.Name == States[i]).ToArray();
                    if (variants.Length == 0 || type == null) { ThreeQsVanillaTweaks.Log.Warning("MUL-T skill unavailable: " + Names[i]); continue; }
                    foreach (SkillDef def in variants)
                    {
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true;
                        if (!profiles.ContainsKey(i)) profiles[i] = p;
                        allProfiles.Add(p); definitions[def] = p;
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p),
                            () => p.Skill.Proc == null ? 0f : C.Get(p.Skill.Proc), i < 4 ? PrimaryTokens[i] : null);
                    }
                    Patch(type, "OnEnter", nameof(BeforeEnter), nameof(AfterEnter), nameof(EndScope));
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                }
                if (profiles.ContainsKey(0) || profiles.ContainsKey(7))
                {
                    Patch(State("BaseNailgunState"), "FireBullet", nameof(BeforeEnter), finalizer: nameof(EndScope));
                    Patch(State("NailgunFinalBurst"), "OnEnter", nameof(BeforeEnter), finalizer: nameof(EndScope));
                    if (profiles.ContainsKey(0)) PatchSprint(State("FireNailgun"));
                }
                if (profiles.ContainsKey(7))
                    for (int i = 0; i < 4; i++)
                        if (!profiles.ContainsKey(i)) Patch(State(States[i]), "OnEnter", nameof(BeforeEnter), nameof(AfterEnter), nameof(EndScope));
                if (profiles.ContainsKey(3) || profiles.ContainsKey(5) || profiles.ContainsKey(7))
                    Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap), finalizer: nameof(AfterOverlap));
                if (profiles.ContainsKey(0) || profiles.ContainsKey(1) || profiles.ContainsKey(7))
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), args: Type.EmptyTypes);
                if (profiles.ContainsKey(2) || profiles.ContainsKey(4) || profiles.ContainsKey(7))
                {
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), nameof(AfterProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeProjectileDamage));
                }
                if (profiles.ContainsKey(4)) Patch(typeof(ProjectileExplosion), "InitializeChild", postfix: nameof(AfterChild));
                if (profiles.ContainsKey(3))
                {
                    harmony.Patch(AccessTools.Method(State("FireBuzzsaw"), "FixedUpdate"), transpiler: new HarmonyMethod(typeof(MULT), nameof(SawFrequencyIL)));
                    GlobalEventManager.onServerDamageDealt += OnDamageDealt;
                }
                if (profiles.ContainsKey(5))
                {
                    Patch(State("ToolbotDashImpact"), "OnEnter", nameof(BeforeImpact), finalizer: nameof(EndScope));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeImpactDamage));
                    Patch(State("ToolbotDash"), "FixedUpdate", postfix: nameof(AfterTransportTick));
                    jumpVelocity = AccessTools.Method(typeof(GenericCharacterMain), "ApplyJumpVelocity");
                }
                if (profiles.ContainsKey(6)) Patch(State("ToolbotStanceSwap"), "OnEnter", nameof(BeforeEnter), nameof(AfterEnter), nameof(EndScope));
                if (profiles.ContainsKey(5) || profiles.ContainsKey(7))
                    harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(MULT), nameof(StatsIL)));
                if (profiles.ContainsKey(7)) PatchSprint(State("ToolbotDualWieldBase"));
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("MUL-T changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index;
            s.Bind(i == 2 ? 2f : (float?)null, changedAgile: i == 3 || i == 5 || i == 6 ? true : (bool?)null,
                damage: false, durationField: i == 5 ? "baseDuration" : null);
            if (i <= 5)
            {
                float frequency = i == 3 ? Math.Max(0.001f, S.Value(s.Sample, "baseFireFrequency", 5f)) : 1f;
                p.NativeDamage = S.Value(s.Sample, i == 3 ? "damageCoefficientPerSecond" : i == 5 ? "chargeDamageCoefficient" : "damageCoefficient", 1f) / frequency;
                p.NativeProc = S.Value(s.Sample, i == 3 ? "procCoefficientPerSecond" : "procCoefficient", 1f) / frequency;
                if (i == 2 || i == 4)
                {
                    p.Projectile = S.Read(s.Sample, "projectilePrefab") as GameObject;
                    if (!p.Projectile) throw new MissingMemberException("Projectile unavailable for " + Names[i]);
                    p.NativeProc = p.Projectile.GetComponent<ProjectileController>().procCoefficient;
                }
                if (i == 0) s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff over distance. Vanilla: Default.");
                s.BindDamage(p.NativeDamage * 100f, i == 1 ? 700f : i == 2 ? 400f : (float?)null, p.NativeProc, i == 3);
                if (i == 3) s.Interval = S.Float(s.Section, "Damage Interval", 1f / frequency, 1f / frequency, "Seconds between damage ticks before attack speed is applied.", 0.001f);
            }
            if (i == 2)
            {
                float speed = ProjectileSpeed(p.Projectile);
                scrapSpeed = S.Int(s.Section, "Projectile Speed (m/s)", 120, (int)speed, "Initial projectile speed in metres per second.");
                rapid = S.Bool(s.Section, "Cooldown Scales With Attack Speed", false, S.Read(s.Definition, "attackSpeedBuffsRestockSpeed") is bool value && value, "Recharge stocks faster as attack speed increases.");
                icbm = S.Bool(s.Section, "ICBM Synergy", true, false, "Pocket I.C.B.M. adds two rockets to each shot and increases their damage with additional stacks.");
                BindArea(p);
            }
            if (i == 3) barrierConversion = S.Int(s.Section, "Damage To Barrier Conversion (%)", 20, 0, "Damage dealt by Power-Saw gained as barrier.");
            if (i == 4)
            {
                s.BindProjectileSpeed(p.Projectile);
                ProjectileExplosion explosion = p.Projectile.GetComponent<ProjectileExplosion>();
                if (!explosion) throw new MissingMemberException("Blast Canister explosion unavailable.");
                bombletDamage = S.Int(s.Section, "Bomblet Damage (%)", 44, (int)Math.Round(p.NativeDamage * explosion.childrenDamageCoefficient * 100f), "Damage per bomblet as a percentage of base damage.");
                bombletCount = S.Int(s.Section, "Bomblet Count", 6, explosion.childrenCount, "Number of bomblets released by the canister.");
                BindArea(p);
            }
            if (i == 5)
            {
                transportArmour = S.Int(s.Section, "Armour", 200, 200, "Armour gained during Transport Mode.");
                int nativeSpeed = (int)Math.Round((S.Value(s.Sample, "speedMultiplier", 3.2f) - 1f) * 100f);
                transportSpeed = S.Int(s.Section, "Speed Bonus (%)", 220, nativeSpeed, "Movement speed gained during Transport Mode.");
                ramBonus = S.Int(s.Section, "Large Enemy Ram Damage Bonus", 4, (int)Math.Round(S.Value(s.Sample, "knockbackDamageCoefficient", p.NativeDamage * 4f) / Math.Max(0.001f, p.NativeDamage)), "Damage multiplier when ramming a large enemy and ending Transport Mode.");
                jump = S.Bool(s.Section, "Allow Jumping", false, false, "Allow jumping during Transport Mode.");
            }
            if (i == 6)
            {
                object swap = EntityStateCatalog.InstantiateState(State("ToolbotStanceSwap"));
                swapTime = S.Float(s.Section, "Swap Time", 0.4f, S.Value(swap, "baseDuration", 0.5f), "Time to swap primaries in seconds, before attack speed is applied.");
                reload = S.Bool(s.Section, "Reload Primaries", true, false, "Fully reload both primary weapons when swapping.");
            }
            if (i == 7)
            {
                powerPenalty = S.Read(s.Sample, "penaltyBuff") as BuffDef;
                powerBonus = S.Read(s.Sample, "bonusBuff") as BuffDef;
                nativeSlow = BuffSlow(powerPenalty);
                powerArmour = S.Int(s.Section, "Armour", 100, 100, "Armour gained during Power Mode.");
                powerSlow = S.Int(s.Section, "Slow Coefficient (%)", 50, (int)Math.Round(nativeSlow * 100f), "Movement slow coefficient during Power Mode. 50 gives 66.7% movement speed.");
                powerDamage = S.Int(s.Section, "Damage Bonus (%)", 50, 0, "Extra primary weapon damage during Power Mode.");
            }
        }
        private static float BuffSlow(BuffDef buff)
        {
            if (!buff) return 0f;
            if (buff == RoR2Content.Buffs.Slow50) return 0.5f;
            if (buff == RoR2Content.Buffs.Slow60) return 0.6f;
            if (buff == RoR2Content.Buffs.Slow80) return 0.8f;
            return 0f;
        }
        private static float ProjectileSpeed(GameObject prefab)
        {
            foreach (Component c in prefab.GetComponents<Component>()) if (S.Read(c, "desiredForwardSpeed") is float speed) return speed;
            throw new MissingMemberException("Projectile speed unavailable for " + prefab.name);
        }
        private static void BindArea(Profile p)
        {
            ProjectileExplosion explosion = p.Projectile.GetComponent<ProjectileExplosion>();
            if (!explosion) return;
            p.Skill.Radius = S.Float(p.Skill.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
            S.AreaFalloff native = Enum.TryParse(explosion.falloffModel.ToString(), out S.AreaFalloff parsed) ? parsed : S.AreaFalloff.None;
            p.Skill.Area = C.Bind(p.Skill.Section, "AOE Damage Falloff", native, "Damage falloff from the centre of the explosion. Vanilla: " + native + ".");
        }
        private static void Apply()
        {
            if (stats != null && prefabBody) stats.Apply(prefabBody);
            foreach (Profile p in allProfiles)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 2)
                {
                    S.Write(p.Skill.Definition, "attackSpeedBuffsRestockSpeed", C.Get(rapid));
                    p.Skill.SetKeyword("KEYWORD_RAPID", C.Get(rapid));
                }
                if (p.Index == 4) p.Skill.SetKeyword("KEYWORD_STUNNING", true);
                S.SetDescription(p.Skill, "3QS_MULT_" + p.Index + "_DESCRIPTION", () => Description(p));
            }
            // Supply a keyword even on game versions without a translated Rapid tooltip.
            if (profiles.ContainsKey(2)) S.SetText("KEYWORD_RAPID", () => "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
        }
        private static void AfterBodyAwake(CharacterBody __instance) { if (IsMULT(__instance) && stats != null) stats.Apply(__instance); }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill;
            string d = s.Damage == null ? "" : S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), s.Periodic);
            string text;
            switch (p.Index)
            {
                case 0: text = "Rapidly fire nails for " + d + ". Releasing the button fires a final burst."; break;
                case 1: text = "Fire a piercing rebar for " + d + "."; break;
                case 2: text = (C.Get(rapid) ? S.Utility("Rapid. ") : "") + "Fire an explosive rocket for " + d + "."; break;
                case 3: text = "Saw through nearby enemies for " + d + ", gaining " + S.Utility(S.Number(C.Get(barrierConversion)) + "% of damage dealt as barrier") + "."; break;
                case 4: text = S.Utility("Stunning. ") + "Launch a canister for " + d + ". Releases " + S.Damage(C.Get(bombletCount) + " bomblets") + " for " + S.Damage(C.Get(bombletDamage) + "% damage each") + "."; break;
                case 5: text = "Zoom forward, gaining " + S.Utility(C.Get(transportArmour) + " armour") + " and " + S.Utility(C.Get(transportSpeed) + "% movement speed") + ". Ram enemies for " + d + ". Large enemies take " + S.Damage(C.Get(ramBonus) + "× ram damage") + " and end the charge."; break;
                case 6: text = "Swap your primary weapon and active equipment." + (C.Get(reload) ? " " + S.Utility("Fully reload both primary weapons.") : ""); break;
                default: text = "Enter a heavy stance, firing both primary weapons at once. Gain " + S.Utility(C.Get(powerArmour) + " armour") + " and " + S.Damage(C.Get(powerDamage) + "% primary damage") + ", with " + S.Utility(S.Number(100f / (1f + C.Get(powerSlow) / 100f)) + "% movement speed") + "."; break;
            }
            return s.AgileText + text;
        }
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (key, value) => b.Append("\n").Append(key).Append(": ").Append(S.Utility(value));
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Index == 2) add("Projectile Speed", C.Get(scrapSpeed) + "m/s");
            if (p.Skill.Interval != null) add("Damage Interval", S.Number(C.Get(p.Skill.Interval)) + "s");
            if (p.Skill.Radius != null) add("AOE Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) add("Duration", S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.Index == 6) add("Swap Time", S.Number(C.Get(swapTime)) + "s");
            return b.ToString();
        }
        private static void BeforeEnter(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = active, Body = activeBody, Index = activeIndex };
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            active = IsMULT(body) ? p : null; activeBody = IsMULT(body) ? body : null;
            activeIndex = Array.IndexOf(States, __instance.GetType().Name);
            if (__instance.GetType().Name == "NailgunFinalBurst") activeIndex = 0;
            if (active == null) return;
            S.Skill s = p.Skill;
            if (p.Index == 1 || p.Index == 2 || p.Index == 4) S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f);
            if (p.Index == 5)
            {
                S.Write(__instance, "baseDuration", C.Get(s.Duration));
                S.Write(__instance, "speedMultiplier", 1f + C.Get(transportSpeed) / 100f);
            }
            if (p.Index == 6 && __instance.GetType().Name == "ToolbotStanceSwap") S.Write(__instance, "baseDuration", C.Get(swapTime));
        }
        private static Exception EndScope(Scope __state, Exception __exception) { active = __state.Previous; activeBody = __state.Body; activeIndex = __state.Index; return __exception; }
        private static void AfterEnter(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (!IsMULT(body)) return;
            int index = Array.IndexOf(States, __instance.GetType().Name);
            if ((index == 3 || index == 5) && S.Read(__instance, "attack") is OverlapAttack attack)
            {
                melee.Remove(attack);
                melee.Add(attack, new MeleeMarker { Profile = p, Body = body, Index = index,
                    Damage = p == null ? attack.damage : S.Value(__instance, "damageStat", body.damage) * C.Get(p.Skill.Damage) / 100f });
            }
            if (p == null) return;
            if (p.Index == 4) S.Write(__instance, "projectileBaseSpeed", C.Get(p.Skill.ProjectileSpeed));
            if (p.Index == 6 && __instance.GetType().Name == "ToolbotStanceSwap" && C.Get(reload) && Authority(__instance))
                foreach (GenericSkill slot in body.GetComponents<GenericSkill>())
                    if (slot.skillFamily && (((ScriptableObject)slot.skillFamily).name == "ToolbotBodyPrimary1" || ((ScriptableObject)slot.skillFamily).name == "ToolbotBodyPrimary2")) { slot.stock = slot.maxStock; slot.rechargeStopwatch = 0f; }
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            if (!activeBody || __instance.owner != activeBody.gameObject || activeIndex < 0 || activeIndex > 1) return;
            if (__instance.damageType.damageSource != DamageSource.Primary && __instance.damageType.damageSource != DamageSource.Secondary) return;
            __instance.damage *= PrimaryMultiplier(activeBody);
            if (active == null) return;
            S.Skill s = active.Skill;
            if (active.Index == 0) __instance.damage *= C.Get(s.Damage) / Math.Max(0.001f, active.NativeDamage * 100f);
            __instance.procCoefficient = C.Get(s.Proc);
            __instance.falloffModel = C.Get(s.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(s.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        }
        private static void BeforeOverlap(OverlapAttack __instance, out MeleeScope __state)
        {
            __state = null;
            if (!melee.TryGetValue(__instance, out MeleeMarker marker)) return;
            __state = new MeleeScope { Damage = __instance.damage, Proc = __instance.procCoefficient };
            if (marker.Index == 3) __instance.damage = marker.Damage * PrimaryMultiplier(marker.Body);
            else if (marker.Profile != null) __instance.damage *= C.Get(marker.Profile.Skill.Damage) / Math.Max(0.001f, marker.Profile.NativeDamage * 100f);
            if (marker.Profile != null) __instance.procCoefficient = C.Get(marker.Profile.Skill.Proc);
        }
        private static Exception AfterOverlap(OverlapAttack __instance, MeleeScope __state, Exception __exception)
        {
            if (__state != null) { __instance.damage = __state.Damage; __instance.procCoefficient = __state.Proc; }
            return __exception;
        }
        private static Profile ForProjectile(GameObject prefab, CharacterBody body) => IsMULT(body) ? profiles.Values.FirstOrDefault(p => p.Projectile && p.Projectile == prefab) : null;
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo, out RocketVolley __state)
        {
            __state = null;
            CharacterBody body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            Profile p = ForProjectile(fireProjectileInfo.projectilePrefab, body);
            if (p == null)
            {
                if (!extraRocket && IsMULT(body) && activeBody == body && activeIndex == 2) fireProjectileInfo.damage *= PrimaryMultiplier(body);
                return;
            }
            if (p.Index == 4) { fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride); return; }
            fireProjectileInfo.speedOverride = C.Get(scrapSpeed);
            if (extraRocket) return;
            fireProjectileInfo.damage *= PrimaryMultiplier(body);
            int count = C.Get(icbm) && body.inventory ? body.inventory.GetItemCountEffective(DLC1Content.Items.MoreMissile) : 0;
            if (count <= 0) return;
            fireProjectileInfo.damage *= MissileUtils.GetMoreMissileDamageMultiplier(count);
            __state = new RocketVolley { Info = fireProjectileInfo };
        }
        private static void AfterProjectile(RocketVolley __state)
        {
            if (__state == null) return;
            bool previous = extraRocket; extraRocket = true;
            try
            {
                foreach (float angle in new[] { -5f, 5f })
                {
                    FireProjectileInfo info = __state.Info;
                    info.rotation = Quaternion.AngleAxis(angle, Vector3.up) * info.rotation;
                    ProjectileManager.instance.FireProjectile(info);
                }
            }
            finally { extraRocket = previous; }
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            if (!IsMULT(body)) return;
            Profile p = profiles.Values.FirstOrDefault(profile => profile.Projectile && profile.Projectile.GetComponent<ProjectileController>().catalogIndex == __instance.catalogIndex);
            if (p == null) return;
            var marker = __instance.GetComponent<ProjectileMarker>() ?? __instance.gameObject.AddComponent<ProjectileMarker>();
            marker.Profile = p; marker.Origin = __instance.transform.position;
            ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
            marker.BaseDamage = damage && C.Get(p.Skill.Damage) > 0f ? damage.damage / (C.Get(p.Skill.Damage) / 100f) : body.damage;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            ProjectileExplosion explosion = __instance.GetComponent<ProjectileExplosion>();
            if (!explosion) return;
            if (p.Skill.Radius != null) explosion.blastRadius = C.Get(p.Skill.Radius);
            if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area));
            if (p.Index == 4) explosion.childrenCount = C.Get(bombletCount);
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static void AfterChild(ProjectileExplosion __instance, GameObject childProjectileInstance)
        {
            ProjectileMarker parent = __instance.GetComponent<ProjectileMarker>();
            if (!parent || parent.Profile.Index != 4) return;
            ProjectileDamage damage = childProjectileInstance.GetComponent<ProjectileDamage>();
            if (damage) damage.damage = parent.BaseDamage * C.Get(bombletDamage) / 100f;
            var marker = childProjectileInstance.GetComponent<ProjectileMarker>() ?? childProjectileInstance.AddComponent<ProjectileMarker>();
            marker.Profile = parent.Profile; marker.Origin = parent.Origin; marker.BaseDamage = parent.BaseDamage;
        }
        private static void BeforeProjectileDamage(DamageInfo damageInfo)
        {
            if (!damageInfo.inflictor || damageInfo.dotIndex != DotController.DotIndex.None) return;
            ProjectileMarker marker = damageInfo.inflictor.GetComponent<ProjectileMarker>();
            if (!marker || !damageInfo.attacker) return;
            float distance = Vector3.Distance(marker.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(marker.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f;
            else if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.25f + Mathf.Clamp01(Mathf.InverseLerp(25f, 7f, distance)) * 0.75f;
        }
        private static void OnDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || !profiles.ContainsKey(3) || !IsMULT(report.attackerBody) || report.damageDealt <= 0f) return;
            DamageInfo info = report.damageInfo;
            if (info.dotIndex != DotController.DotIndex.None || info.inflictor != info.attacker) return;
            CharacterBody body = report.attackerBody;
            bool saw = body.GetComponents<EntityStateMachine>().Any(machine => machine.state?.GetType().Name == "FireBuzzsaw" &&
                S.Read(machine.state, "activatorSkillSlot") is GenericSkill slot && body.skillLocator &&
                (slot == body.skillLocator.primary ? info.damageType.damageSource == DamageSource.Primary : slot == body.skillLocator.secondary && info.damageType.damageSource == DamageSource.Secondary));
            if (saw && body.healthComponent) body.healthComponent.AddBarrier(report.damageDealt * C.Get(barrierConversion) / 100f);
        }
        private static float SawFrequency(float original, EntityState state)
        {
            return profiles.TryGetValue(3, out Profile p) && IsMULT(Body(state)) ? 1f / C.Get(p.Skill.Interval) : original;
        }
        private static IEnumerable<CodeInstruction> SawFrequencyIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo f && f.Name == "baseFireFrequency")
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MULT), nameof(SawFrequency))); }
            }
        }
        private static void BeforeImpact(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = active, Body = activeBody, Index = activeIndex };
            active = profiles.TryGetValue(5, out Profile p) && IsMULT(Body(__instance)) ? p : null;
            activeBody = active == null ? null : Body(__instance);
        }
        private static void BeforeImpactDamage(DamageInfo damageInfo)
        {
            if (active?.Index != 5 || !activeBody || damageInfo.attacker != activeBody.gameObject || damageInfo.damageType.damageSource != DamageSource.Utility) return;
            float native = S.Value(active.Skill.Sample, "knockbackDamageCoefficient", 1f);
            damageInfo.damage *= C.Get(active.Skill.Damage) / 100f * C.Get(ramBonus) / Math.Max(0.001f, native);
            damageInfo.procCoefficient = C.Get(active.Skill.Proc);
        }
        private static void AfterTransportTick(EntityState __instance)
        {
            CharacterBody body = Body(__instance);
            if (!IsMULT(body) || !C.Get(jump) || !Authority(__instance) || !body.inputBank || !body.inputBank.jump.justPressed || body.inputBank.jump.hasPressBeenClaimed) return;
            Component motor = (object)body.characterMotor as Component;
            if (!motor || S.Value(motor, "jumpCount") >= body.maxJumpCount || jumpVelocity == null) return;
            float bonus = S.Value(motor, "jumpCount") >= body.baseJumpCount ? 1.5f : 1f;
            jumpVelocity.Invoke(null, new object[] { motor, body, bonus, bonus, false });
            S.Write(motor, "jumpCount", (int)S.Value(motor, "jumpCount") + 1);
            AccessTools.Method(typeof(CharacterBody), "TriggerJumpEventGlobally")?.Invoke(body, null);
        }
        private static float BuffStat(float original, CharacterBody body, string name)
        {
            if (!IsMULT(body)) return original;
            if (name == "ArmorBoost" && profiles.ContainsKey(5) && HasState(body, "ToolbotDash")) return C.Get(transportArmour);
            if (!InPowerMode(body)) return original;
            FieldInfo field = AccessTools.Field(typeof(RoR2Content.Buffs), name);
            BuffDef buff = field?.GetValue(null) as BuffDef;
            if (buff && buff == powerBonus) return C.Get(powerArmour);
            if (buff && buff == powerPenalty) return C.Get(powerSlow) / 100f;
            return original;
        }
        private static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> instructions)
        {
            string pending = null;
            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo f && f.DeclaringType == typeof(RoR2Content.Buffs))
                    pending = new[] { "ArmorBoost", "SmallArmorBoost", "Slow50", "Slow60", "Slow80" }.Contains(f.Name) ? f.Name : null;
                yield return code;
                if (pending == null || code.opcode != OpCodes.Ldc_R4) continue;
                string name = pending; pending = null;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldstr, name);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MULT), nameof(BuffStat)));
            }
        }
        private static void PatchSprint(Type type) => harmony.Patch(AccessTools.Method(type, "FixedUpdate"), transpiler: new HarmonyMethod(typeof(MULT), nameof(SprintIL)));
        private static bool CancelSprint(CharacterBody body, EntityState state)
        {
            if (!IsMULT(body)) return body.isSprinting;
            Profile p = ForState(state);
            if (p == null && State("ToolbotDualWieldBase").IsInstanceOfType(state)) profiles.TryGetValue(7, out p);
            return body.isSprinting && (p == null || p.Skill.Definition.canceledFromSprinting || p.Skill.Definition.cancelSprintingOnActivation);
        }
        private static IEnumerable<CodeInstruction> SprintIL(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.Property(typeof(CharacterBody), "isSprinting").GetMethod;
            foreach (CodeInstruction code in instructions)
            {
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && Equals(code.operand, getter))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(code.labels); load.blocks.AddRange(code.blocks);
                    yield return load; yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MULT), nameof(CancelSprint)));
                }
                else yield return code;
            }
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (__result && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll) __result = skillSlot.stock >= skillSlot.maxStock; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out int? __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = __instance.stockToConsume; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, int? __state, Exception __exception)
        { if (__state.HasValue) __instance.stockToConsume = __state.Value; return __exception; }
    }
}
