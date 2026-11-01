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
    internal static class SpinelTonic
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.RegisterEquipment("Tonic", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Number("Duration", 20, "Duration of the Tonic buff.");
            c.FieldValue("RoR2.EquipmentSlot", "tonicBuffDuration", "Duration", 1, "FireTonic");
            c.Number("Affliction Chance (%)", 20, "Chance to gain a Tonic Affliction after use, affected by luck.");
            c.Values.Literal("RoR2.EquipmentSlot", "FireTonic", 80, () => 100 - Mathf.Clamp(c.Values.F("Affliction Chance (%)"), 0, 100));
            c.Number("Maximum Health Increase (%)", 50, "Maximum health and shield increase during Tonic.");
            c.Number("Attack Speed Increase (%)", 70, "Attack-speed increase during Tonic.");
            c.Number("Movement Speed Increase (%)", 30, "Movement-speed increase during Tonic.");
            c.Number("Armour Increase", 20, "Additional armour during Tonic.");
            c.Number("Damage Increase (%)", 100, "Damage increase during Tonic.");
            c.Number("Regeneration Increase (%)", 300, "Regeneration increase during Tonic.");
            c.Number("Affliction Stat Reduction Per Stack (%)", 5, "Attack speed, movement speed, damage and regeneration lost per Affliction while Tonic is inactive.");
            c.Number("Affliction Curse Per Stack (%)", 10, "Additional health curse per Affliction while Tonic is inactive.");
            c.EquipmentDescription = () => "Drink the Tonic, gaining a boost for <style=cIsUtility>" + c.Values.F("Duration").ToString("0.##") + " seconds</style>. Increases <style=cIsDamage>damage</style> by " + c.Values.F("Damage Increase (%)").ToString("0.##") + "%, <style=cIsDamage>attack speed</style> by " + c.Values.F("Attack Speed Increase (%)").ToString("0.##") + "%, <style=cIsHealing>armour</style> by " + c.Values.F("Armour Increase").ToString("0.##") + ", <style=cIsHealing>maximum health</style> by " + c.Values.F("Maximum Health Increase (%)").ToString("0.##") + "%, <style=cIsHealing>regeneration</style> by " + c.Values.F("Regeneration Increase (%)").ToString("0.##") + "% and <style=cIsUtility>movement speed</style> by " + c.Values.F("Movement Speed Increase (%)").ToString("0.##") + "%. When the Tonic wears off, you have a <style=cIsHealth>" + c.Values.F("Affliction Chance (%)").ToString("0.##") + "% chance</style> to gain a <style=cIsHealth>Tonic Affliction</style>, reducing attack speed, movement speed, damage and regeneration by <style=cIsHealth>" + c.Values.F("Affliction Stat Reduction Per Stack (%)").ToString("0.##") + "%</style> and adding <style=cIsHealth>" + c.Values.F("Affliction Curse Per Stack (%)").ToString("0.##") + "% health curse</style> per Affliction while not under the effect of the Tonic.";
            if (c.Values.Enabled.Value) U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(SpinelTonic), nameof(Stats)));
        }
        static float Stat(float native, int stat)
        {
            if (!c.Values.Active) return native;
            string[] keys = { "Maximum Health Increase (%)", "Attack Speed Increase (%)", "Movement Speed Increase (%)", "Armour Increase", "Damage Increase (%)", "Regeneration Increase (%)", "Affliction Stat Reduction Per Stack (%)", "Affliction Curse Per Stack (%)" };
            float value = c.Values.F(keys[stat]);
            return stat == 3 ? value : stat == 6 ? Mathf.Clamp01(1 - value / 100f) : stat == 7 ? value / 100f : 1 + value / 100f;
        }
        static IEnumerable<CodeInstruction> Stats(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            int tonic = LunarConfigSupport.ItemLocal(code, "TonicBuff");
            int affliction = LunarConfigSupport.ItemLocal(code, "TonicAffliction");
            int begin = -1, end = -1;
            for (int i = 0; tonic >= 0 && i + 1 < code.Count; i++)
                if (LunarConfigSupport.LocalIndex(code[i], false) == tonic && (code[i + 1].opcode == OpCodes.Brfalse || code[i + 1].opcode == OpCodes.Brfalse_S) && code[i + 1].operand is Label label)
                { begin = i + 2; end = code.FindIndex(begin, ins => ins.labels.Contains(label)); break; }
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i]; int stat = -1;
                if (i >= begin && i < end && begin >= 0 && code[i].opcode == OpCodes.Ldc_R4)
                {
                    float f = (float)code[i].operand;
                    stat = f == 1.5f ? 0 : f == 1.7f ? 1 : f == 1.3f ? 2 : f == 20f ? 3 : f == 2f ? 4 : f == 4f ? 5 : -1;
                }
                else if (affliction >= 0 && i + 1 < code.Count && LunarConfigSupport.LocalIndex(code[i + 1], false) == affliction)
                    stat = code[i].LoadsConstant(.95f) ? 6 : code[i].LoadsConstant(.1f) ? 7 : -1;
                if (stat >= 0) { yield return new CodeInstruction(OpCodes.Ldc_I4, stat); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SpinelTonic), nameof(Stat))); }
            }
        }
    }
}
