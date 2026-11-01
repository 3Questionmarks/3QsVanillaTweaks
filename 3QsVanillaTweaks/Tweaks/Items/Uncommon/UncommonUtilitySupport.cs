using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Uncommon
{
    internal static class AmmoPackSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Patch(typeof(AmmoPickup), "OnTriggerStay", typeof(AmmoPackSupport), prefix: nameof(Pickup));
        }

        static bool Pickup(AmmoPickup __instance, Collider other)
        {
            if (!c.Active || c.F("Cooldown Restored (%)") == 100)
                return true;
            if (!NetworkServer.active || !(bool)A.Field(typeof(AmmoPickup), "alive").GetValue(__instance) || !other || TeamComponent.GetObjectTeam(other.gameObject) != __instance.teamFilter.teamIndex)
                return false;
            var b = other.GetComponent<CharacterBody>();
            if (!b || !b.skillLocator)
                return false;
            A.Field(typeof(AmmoPickup), "alive").SetValue(__instance, false);
            foreach (var skill in b.skillLocator.allSkills)
                if (skill)
                    skill.RunRecharge(skill.finalRechargeInterval * c.F("Cooldown Restored (%)") / 100);
            b.OnPickup(CharacterBody.PickupClass.Minor);
            EffectManager.SimpleEffect(__instance.pickupEffect, __instance.transform.position, Quaternion.identity, true);
            UnityEngine.Object.Destroy(__instance.baseObject);
            return false;
        }
    }

    internal static class MoneyPackSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Patch(typeof(MoneyPickup), "Start", typeof(MoneyPackSupport), prefix: nameof(Start));
        }

        static void Start(MoneyPickup __instance)
        {
            if (c.Active && __instance.transform.root.name.StartsWith("BonusMoneyPack", StringComparison.Ordinal))
                __instance.baseGoldReward = Mathf.RoundToInt(c.F("Gold"));
        }
    }

    internal static class ShurikenStockSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(PrimarySkillShurikenBehavior), "FixedUpdate"), transpiler: new HarmonyMethod(typeof(ShurikenStockSupport), nameof(IL)));
        }

        static int Extra(int native) => c.Active ? Mathf.RoundToInt(c.F("Maximum Stocks Per Stack")) : native;
        static int Base(int native) => c.Active ? Mathf.RoundToInt(c.F("Maximum Stocks") - c.F("Maximum Stocks Per Stack")) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            foreach (var i in input)
            {
                yield return i;
                if (i.operand is FieldInfo f && (f.Name == "numShurikensBase" || f.Name == "numShurikensPerStack"))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(ShurikenStockSupport), f.Name == "numShurikensBase" ? nameof(Base) : nameof(Extra)));
            }
        }
    }

    internal static class FaradaySupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (!c.Enabled.Value)
                return;
            var t = typeof(CharacterBody).Assembly.GetType("RoR2.Items.JumpDamageStrikeBodyBehavior", true);
            U.Patch(t, "GetRadius", typeof(FaradaySupport), prefix: nameof(Radius));
            foreach (var name in new[]
            {
                "DischargeEffects",
                "UpdateCharge",
                "GetRadius"
            }

            )
                U.Harmony.Patch(A.Method(t, name), transpiler: new HarmonyMethod(typeof(FaradaySupport), nameof(IL)));
        }

        static bool Radius(int charge, int stacks, ref float __result)
        {
            if (!c.Active)
                return true;
            __result = charge < c.F("Minimum Charge") ? 0 : Mathf.Max(0, c.Total("Radius", stacks) + c.F("Radius Per Charge") * (charge - 1));
            return false;
        }

        static int Minimum(int native) => c.Active ? Mathf.RoundToInt(c.F("Minimum Charge")) : native;
        static int Maximum(int native) => c.Active ? Mathf.RoundToInt(c.F("Maximum Charge")) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            foreach (var i in input)
            {
                yield return i;
                if (i.LoadsConstant(25))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(FaradaySupport), nameof(Minimum)));
                else if (i.LoadsConstant(100))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(FaradaySupport), nameof(Maximum)));
            }
        }
    }

    internal static class BreachingFinSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(BreachingFinSupport), nameof(IL)));
        }

        static int Base(int native) => c.Active ? Mathf.RoundToInt(c.F("Maximum Juggles")) : native;
        static int Extra(int native) => c.Active ? Mathf.RoundToInt(c.F("Maximum Juggles Per Stack")) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            foreach (var i in input)
            {
                yield return i;
                if (i.operand is FieldInfo f && (f.Name == "baseJuggles" || f.Name == "jugglesPerStackBeyond1"))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BreachingFinSupport), f.Name == "baseJuggles" ? nameof(Base) : nameof(Extra)));
            }
        }
    }

    internal static class NoxiousSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(CharacterBody), "TriggerEnemyDebuffs"), transpiler: new HarmonyMethod(typeof(NoxiousSupport), nameof(IL)));
        }

        static int Targets(int native) => c.Active ? Mathf.RoundToInt(c.Total("Targets", native)) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var list = new List<CodeInstruction>(input);
            int item = -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].operand is FieldInfo f && f.Name == "TriggerEnemyDebuffs")
                {
                    for (int j = i + 1; j < Math.Min(i + 10, list.Count); j++)
                    {
                        int local = Local(list[j], true);
                        if (local >= 0)
                        {
                            item = local;
                            break;
                        }
                    }

                    break;
                }
            }

            // The final load of the item-count local is copied into the remaining
            // target budget, after collecting debuff data and finding candidates.
            for (int i = list.Count - 1; i >= 0; i--)
                if (item >= 0 && Local(list[i], false) == item)
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(NoxiousSupport), nameof(Targets))));
                    return list;
                }

            throw new InvalidOperationException("Noxious Thorn target budget missing.");
        }

        static int Local(CodeInstruction i, bool store)
        {
            if (i.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0))
                return 0;
            if (i.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1))
                return 1;
            if (i.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2))
                return 2;
            if (i.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3))
                return 3;
            if (i.opcode != (store ? OpCodes.Stloc : OpCodes.Ldloc) && i.opcode != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S))
                return -1;
            return i.operand is LocalBuilder b ? b.LocalIndex : i.operand is LocalVariableInfo v ? v.LocalIndex : Convert.ToInt32(i.operand);
        }
    }
}

namespace Tweaks.Items.Uncommon
{
    internal static class BandSupport
    {
        static readonly List<UncommonConfig> configs = new List<UncommonConfig>();
        static bool installed;
        internal static void Register(UncommonConfig c)
        {
            if (!c.Enabled.Value)
                return;
            configs.Add(c);
            c.Suppress("RoR2.GlobalEventManager", "ProcessHitEnemy", () => UncommonNative.Info != null && UncommonNative.Body && UncommonNative.Info.damage < UncommonNative.Body.damage * c.F("Trigger Damage (%)") / 100);
            if (installed)
                return;
            installed = true;
            U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy"), transpiler: new HarmonyMethod(typeof(BandSupport), nameof(IL)));
        }

        static float Threshold(float native)
        {
            var body = UncommonNative.Body;
            if (!body)
                return native;
            float threshold = float.MaxValue;
            foreach (var c in configs)
                if (c.Count(body) > 0)
                    threshold = Mathf.Min(threshold, c.F("Trigger Damage (%)") / 100);
            if (U.Count(body, "FireRing") > 0 && !configs.Any(c => c.Active && c.ItemName == "FireRing") || U.Count(body, "IceRing") > 0 && !configs.Any(c => c.Active && c.ItemName == "IceRing"))
                threshold = Mathf.Min(threshold, native);
            return threshold == float.MaxValue ? native : threshold;
        }

        static float Cooldown(float native)
        {
            var b = UncommonNative.Body;
            float cooldown = 0;
            bool any = false;
            foreach (var c in configs)
                if (c.Count(b) > 0 && UncommonNative.Info != null && UncommonNative.Info.damage >= b.damage * c.F("Trigger Damage (%)") / 100)
                {
                    any = true;
                    cooldown = Mathf.Max(cooldown, c.F("Cooldown"));
                }

            if (b && UncommonNative.Info != null && UncommonNative.Info.damage >= b.damage * 4 && (U.Count(b, "FireRing") > 0 && !configs.Any(c => c.Active && c.ItemName == "FireRing") || U.Count(b, "IceRing") > 0 && !configs.Any(c => c.Active && c.ItemName == "IceRing")))
            {
                any = true;
                cooldown = Mathf.Max(cooldown, native);
            }

            return any ? cooldown : native;
        }

        static float IceProc(float native)
        {
            var c = configs.FirstOrDefault(c => c.Active && c.ItemName == "IceRing");
            return c != null && c.Count(UncommonNative.Body) > 0 ? c.Total("Proc Coefficient", c.Count(UncommonNative.Body)) : native;
        }

        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var list = new List<CodeInstruction>(input);
            int ready = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "ElementalRingsReady");
            int threshold = -1;
            for (int i = ready - 1; i >= Math.Max(0, ready - 40); i--)
                if (list[i].LoadsConstant(4f))
                {
                    threshold = i;
                    break;
                }

            if (threshold < 0)
                throw new InvalidOperationException("Band threshold missing.");
            list.Insert(threshold + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(BandSupport), nameof(Threshold))));
            int fire = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "FireRing");
            for (int i = fire; i < Math.Min(list.Count, fire + 30); i++)
                if (list[i].LoadsConstant(10f))
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(BandSupport), nameof(Cooldown))));
                    break;
                }

            for (int i = fire; i < list.Count; i++)
                if (list[i].opcode == OpCodes.Stfld && list[i].operand is FieldInfo f && f.DeclaringType == typeof(DamageInfo) && f.Name == "procCoefficient")
                {
                    var load = new CodeInstruction(OpCodes.Call, A.Method(typeof(BandSupport), nameof(IceProc)));
                    load.labels.AddRange(list[i].labels);
                    list[i].labels.Clear();
                    list.Insert(i, load);
                    break;
                }

            return list;
        }
    }

    internal static class LuminousSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            c.Pair("Maximum Charges", 5, 1, "Maximum stored secondary charges. Vanilla: 5 +1 per additional item stack.");
            if (!c.Enabled.Value)
                return;
            U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy"), transpiler: new HarmonyMethod(typeof(LuminousSupport), nameof(IL)));
            U.Patch(typeof(CharacterBody), "AddIncreasePrimaryDamageStack", typeof(LuminousSupport), prefix: nameof(Stack));
        }

        static bool Stack(CharacterBody __instance)
        {
            int n = c.Count(__instance);
            if (n == 0)
                return true;
            int cap = Mathf.Max(0, Mathf.FloorToInt(c.Total("Maximum Charges", n)));
            if (__instance.GetBuffCount(DLC2Content.Buffs.IncreasePrimaryDamageBuff) < cap)
                __instance.AddBuff(DLC2Content.Buffs.IncreasePrimaryDamageBuff);
            __instance.TransmitItemBehavior(new CharacterBody.NetworkItemBehaviorData(c.Item.itemIndex, __instance.GetBuffCount(DLC2Content.Buffs.IncreasePrimaryDamageBuff)));
            return false;
        }

        static int Required(int native) => c.Active ? Mathf.Max(1, Mathf.CeilToInt(c.F("Charges Required"))) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var list = new List<CodeInstruction>(input);
            int at = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "IncreasePrimaryDamageBuff");
            for (int i = at; i < Math.Min(list.Count, at + 40); i++)
                if (list[i].LoadsConstant(3))
                {
                    list.Insert(i + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(LuminousSupport), nameof(Required))));
                    return list;
                }

            throw new InvalidOperationException("Luminous Shot charge threshold missing.");
        }
    }

    internal static class KineticSupport
    {
        static UncommonConfig c;
        internal static void Register(UncommonConfig config)
        {
            c = config;
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(KineticSupport), nameof(IL)));
        }

        static float Radius(float native) => c.Active ? c.Total("Explosion Radius", c.Count(UncommonNative.Body)) : native;
        static float Proc(float native) => c.Active ? c.Total("Proc Coefficient", c.Count(UncommonNative.Body)) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var list = new List<CodeInstruction>(input);
            int at = list.FindIndex(i => i.operand is MethodInfo m && m.Name == "GetShieldBoosterDamage");
            if (at < 0)
                throw new InvalidOperationException("Kinetic shield blast missing.");
            for (int i = at; i < Math.Min(list.Count, at + 80); i++)
                if (list[i].opcode == OpCodes.Stfld && list[i].operand is FieldInfo f && f.DeclaringType == typeof(BlastAttack) && (f.Name == "radius" || f.Name == "procCoefficient"))
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Call, A.Method(typeof(KineticSupport), f.Name == "radius" ? nameof(Radius) : nameof(Proc))));
                    i++;
                }

            return list;
        }
    }
}
