using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using S = Tweaks.Items.Common.CommonItemSupport;
namespace Tweaks.Items.Lunar
{
    internal static class EffigyOfGrief
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.RegisterEquipment("CrippleWard", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/CrippleWard"));
            c.Literal("RoR2.EquipmentSlot", "FireCrippleWard", 1000f, "Placement Range");
            c.Number("Maximum Effigies", 5, "Maximum placed effigies per player. The oldest is replaced at the limit.");
            c.Number("Movement Speed Reduction (%)", 50, "Movement-speed reduction from Cripple. Also affects other sources of this same debuff.");
            c.Number("Armour Reduction", 20, "Armour removed by Cripple. Also affects other sources of this same debuff.");
            c.EquipmentDescription = () => "ALL characters within the effigy are <style=cIsHealth>slowed by " + c.Values.F("Movement Speed Reduction (%)").ToString("0.##") + "%</style> and have their <style=cIsHealth>armour reduced by " + c.Values.F("Armour Reduction").ToString("0.##") + "</style>. Can place up to <style=cIsUtility>" + c.Values.F("Maximum Effigies").ToString("0") + "</style>.";
            if (!c.Values.Enabled.Value) return;
            U.Harmony.Patch(A.Method(typeof(CharacterMaster), "GetDeployableSameSlotLimit"), postfix: new HarmonyMethod(typeof(EffigyOfGrief), nameof(Limit)));
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(EffigyOfGrief), nameof(Stats)));
        }
        static void Limit(DeployableSlot slot, ref int __result)
        {
            if (c.Values.Active && slot == DeployableSlot.CrippleWard) __result = Mathf.Max(0, Mathf.RoundToInt(c.Values.F("Maximum Effigies")));
        }
        static float Slow(float native) => c.Values.Active ? 1 / Mathf.Max(.0001f, 1 - c.Values.F("Movement Speed Reduction (%)") / 100f) - 1 : native;
        static float Armour(float native) => c.Values.Active ? c.Values.F("Armour Reduction") : native;
        static IEnumerable<CodeInstruction> Stats(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            bool cripple = false;
            for (int i = 0; i < code.Count; i++)
            {
                var ins = code[i];
                if (ins.operand is FieldInfo f && f.Name == "Cripple") cripple = true;
                yield return ins;
                if (cripple && ins.LoadsConstant(1f)) { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EffigyOfGrief), nameof(Slow))); cripple = false; }
                else if (cripple && ins.LoadsConstant(20f)) { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EffigyOfGrief), nameof(Armour))); cripple = false; }
            }
        }
    }
}
