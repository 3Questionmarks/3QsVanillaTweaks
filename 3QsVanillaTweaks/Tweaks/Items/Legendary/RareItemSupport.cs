using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class RareItemSupport
    {
        internal static string Damage(string text) => "<style=cIsDamage>" + text + "</style>";
        internal static string Utility(string text) => "<style=cIsUtility>" + text + "</style>";
        internal static R Config(string name, string item, bool enabled = true) => new R(name, item, enabled, "03 Legendary");
        internal static void KillBudget(R c, string type, string method, string anchor, int native, string key)
        {
            c.Literal(type, method, native, () => U.Roll(c.Total("Trigger Chance (%)", c.Count(N.Source)), N.Source) ? c.Total(key, c.Count(N.Source)) : 0, anchor, 0, 60);
        }

        static R frost, nectar, hook, drops, temp;
        sealed class DecayState
        {
            internal float Value = -1;
        }

        static readonly ConditionalWeakTable<Inventory, DecayState> decayStates = new ConditionalWeakTable<Inventory, DecayState>();
        internal static void Icicles(R c)
        {
            frost = c;
            if (c.Enabled.Value)
                U.Patch(typeof(IcicleAuraController), "FixedUpdate", typeof(RareItemSupport), prefix: nameof(Icicle));
            if (c.Enabled.Value)
                U.Patch(typeof(IcicleAuraController), "OnOwnerKillOther", typeof(RareItemSupport), prefix: nameof(IcicleKill));
        }

        static bool IcicleKill(IcicleAuraController __instance)
        {
            var b = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            int n = frost.Count(b);
            if (n == 0)
                return true;
            if (!U.Roll(frost.Total("Trigger Chance (%)", n), b))
                return false;
            var list = (List<float>)A.Field(typeof(IcicleAuraController), "icicleLifetimes").GetValue(__instance);
            for (int i = 0; i < Mathf.Max(0, Mathf.RoundToInt(frost.Total("Icicles Per Kill", n))); i++)
                list.Add(frost.Total("Icicle Duration", n));
            return false;
        }

        static void Icicle(IcicleAuraController __instance)
        {
            var b = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            int n = frost.Count(b);
            if (n == 0)
                return;
            __instance.icicleDamageCoefficientPerTick = frost.Total("Damage (%)", n) / 100f;
            __instance.icicleDamageCoefficientPerTickPerIcicle = frost.Total("Damage Per Icicle (%)", n) / 100f;
            __instance.icicleProcCoefficientPerTick = frost.Total("Proc Coefficient", n);
            __instance.baseIcicleAttackInterval = Mathf.Max(.01f, frost.Total("Damage Interval", n));
            __instance.icicleDuration = frost.Total("Icicle Duration", n);
            __instance.icicleBaseRadius = frost.Total("Base Radius", n);
            __instance.icicleRadiusPerIcicle = frost.Total("Radius Per Icicle", n);
            __instance.icicleCountOnFirstKill = Mathf.RoundToInt(frost.Total("Initial Icicles", n));
            __instance.baseIcicleMax = Mathf.RoundToInt(frost.Total("Maximum Icicles", n));
            __instance.icicleMaxPerStack = 0;
        }

        internal static void Nectar(R c)
        {
            nectar = c;
            if (c.Enabled.Value)
            {
                var behaviour = typeof(CharacterBody).Assembly.GetType("RoR2.Items.BoostAllStatsBehavior")
                    ?? typeof(CharacterBody).Assembly.GetType("RoR2.Items.BoostAllStatsBodyBehavior");
                if (behaviour == null)
                    throw new MissingMemberException("Growth Nectar behaviour was not found in the loaded RoR2 assembly.");
                U.Patch(behaviour, "RecalculateBuffCount", typeof(RareItemSupport), prefix: nameof(NectarRecount));
                U.Patch(behaviour, "OnBuffFirstStackGained", typeof(RareItemSupport), prefix: nameof(NectarGained));
            }
        }

        static bool NectarGained(RoR2.Items.BaseItemBodyBehavior __instance, CharacterBody body)
        {
            if (!nectar.Active)
                return true;
            if (body == __instance.body)
                NectarRecount(__instance);
            return false;
        }

        static bool NectarRecount(RoR2.Items.BaseItemBodyBehavior __instance)
        {
            int n = nectar.Count(__instance.body);
            if (n == 0)
                return true;
            int cap = Mathf.Max(0, Mathf.RoundToInt(nectar.Total("Maximum Buffs", n)));
            int count = BuffCatalog.nonHiddenBuffIndices.Count(i => i != DLC2Content.Buffs.BoostAllStatsBuff.buffIndex && !BuffCatalog.ignoreGrowthNectarIndices.Contains(i) && __instance.body.HasBuff(i));
            A.Method(__instance.GetType(), "UpdateBuffCount").Invoke(__instance, new object[] { Math.Min(cap, count) });
            return false;
        }

        internal static void HookTargets(R c)
        {
            hook = c;
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 5, () => c.F("Maximum Targets") - c.F("Maximum Targets Per Stack"), "BounceNearby", 0, 50);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 5, () => c.F("Maximum Targets Per Stack"), "BounceNearby", 1, 50);
        }

        internal static void BossDrop(R c)
        {
            drops = c;
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "OnCharacterDeath"), transpiler: new HarmonyMethod(typeof(RareItemSupport), nameof(BossIL)));
        }

        static IEnumerable<CodeInstruction> BossIL(IEnumerable<CodeInstruction> input)
        {
            bool area = false, done = false;
            foreach (var i in input)
            {
                yield return i;
                if (i.operand is FieldInfo f && f.Name == "ItemDropChanceOnKill")
                    area = true;
                if (area && !done && i.operand is FieldInfo champion && champion.Name == "isChampion")
                {
                    done = true;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_1);
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, A.Method(typeof(RareItemSupport), nameof(BossChance)));
                }
            }

            if (!done)
                throw new InvalidOperationException("Sonorous Whispers champion branch missing.");
        }

        static bool BossChance(bool champion, DamageReport report) => champion && (!drops.Active || U.Roll(drops.Total("Boss Drop Chance (%)", drops.Count(report.attackerBody)), report.attackerBody));
        internal static void TempDuration(R c)
        {
            temp = c;
            if (c.Enabled.Value)
            {
                U.Patch(typeof(RoR2.Items.DuplicatorBehavior), "CalculateNewItemDecayDuration", typeof(RareItemSupport), prefix: nameof(DuplicatorDuration));
                U.Tick += b =>
                {
                    int n = c.Count(b);
                    if (n > 0 && b.inventory)
                    {
                        float value = 80 + c.Total("Additional Temporary Item Duration", n);
                        var state = decayStates.GetOrCreateValue(b.inventory);
                        if (state.Value != value)
                        {
                            state.Value = value;
                            b.inventory.SetItemDecayDurationServer(value);
                        }
                    }
                };
            }
        }

        internal static float TemporaryBase() => Tweaks.Others.TemporaryItems.EffectiveDuration;
        static bool DuplicatorDuration(RoR2.Items.DuplicatorBehavior __instance, int stack)
        {
            if (!temp.Active)
                return true;
            if (NetworkServer.active && __instance.body && __instance.body.inventory)
                __instance.body.inventory.SetItemDecayDurationServer(80 + (stack > 0 ? temp.Total("Additional Temporary Item Duration", stack) : 0));
            return false;
        }

        internal static GameObject StaticPrefab(Type type, string name)
        {
            object value = A.Field(type, name)?.GetValue(null);
            if (value is GameObject go)
                return go;
            if (value == null)
                return null;
            var conversion = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(GameObject) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(value));
            return conversion?.Invoke(null, new[] { value }) as GameObject;
        }

        internal static CharacterBody Owner(EntityStates.EntityState state) => A.Property(state.GetType(), "ownerBody")?.GetValue(state, null) as CharacterBody;
    }
}
