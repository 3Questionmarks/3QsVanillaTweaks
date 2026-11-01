using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class NetworkedSuffering
    {
        static LegendaryConfig config;
        internal static void Init() => LegendaryConfigSupport.Register("SharedSuffering", Configure);
        static void Configure(LegendaryConfig c)
        {
            config = c;
            c.Pair("Maximum Infected Enemies", 4, 2, "Maximum simultaneously infected enemies.");
            c.Number("Shared Damage (%)", 45, "Damage copied into the shared damage pool.");
            c.Number("Damage Interval", 3, "Seconds between shared damage pulses.");
            c.Number("Proc Coefficient", 0, "Proc coefficient of shared damage pulses.");
            c.Describe(() => "Infect up to " + c.Values.V("Maximum Infected Enemies", "", "cIsUtility") + " enemies with a computer virus that duplicates <style=cIsDamage>" + c.Values.F("Shared Damage (%)").ToString("0.##") + "% of all damage taken</style> and sends it to a global damage pool. Every " + c.Values.F("Damage Interval").ToString("0.##") + " seconds, ALL infected enemies suffer <style=cIsDamage>100% pooled damage</style>.");
            if (!c.Values.Enabled.Value) return;
            U.Harmony.Patch(AccessTools.PropertyGetter(typeof(RoR2.Items.SharedSufferingItemBehaviour), "currentMax"), postfix: new HarmonyMethod(typeof(NetworkedSuffering), nameof(Targets)));
            c.FieldValue("RoR2.SharedSufferingManager", "_damageSentToPool", "Shared Damage (%)", 100, "SendToDamagePool");
            c.FieldValue("RoR2.SharedSufferingManager", "_damageInterval", "Damage Interval", 1, "get_isApproachingDamageTime", "FixedUpdate");
            U.Harmony.Patch(A.Method(typeof(SharedSufferingManager), "DealDamage"), transpiler: new HarmonyMethod(typeof(NetworkedSuffering), nameof(Proc)));
        }
        static float Coefficient() => config.Values.Active ? config.Values.F("Proc Coefficient") : 0f;
        static System.Collections.Generic.IEnumerable<CodeInstruction> Proc(System.Collections.Generic.IEnumerable<CodeInstruction> instructions)
        {
            var code = new System.Collections.Generic.List<CodeInstruction>(instructions);
            for (int i = 0; i < code.Count; i++)
            {
                if (i + 1 < code.Count && code[i].opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && Equals(code[i].operand, 0f) && code[i + 1].StoresField(AccessTools.Field(typeof(DamageInfo), "procCoefficient")))
                { code[i].opcode = System.Reflection.Emit.OpCodes.Call; code[i].operand = AccessTools.Method(typeof(NetworkedSuffering), nameof(Coefficient)); }
                yield return code[i];
            }
        }
        static void Targets(RoR2.Items.SharedSufferingItemBehaviour __instance, ref int __result)
        {
            if (config.Values.Active) __result = Mathf.Max(0, Mathf.RoundToInt(config.Values.Total("Maximum Infected Enemies", __instance.stack)));
        }
    }
}
