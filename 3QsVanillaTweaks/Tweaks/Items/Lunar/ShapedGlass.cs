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
    internal static class ShapedGlass
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.Register("LunarDagger", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Number("Damage Increase Per Stack (%)", 100, "Damage multiplier added by each stack; each copy multiplies damage separately.");
            c.Number("Maximum Health Reduction Per Stack (%)", 50, "Maximum health lost to each copy, multiplied separately. Values are capped below 100%.");
            c.ColouredText(@"Reduce maximum health by\s+[\d.,]+%|halve (?:your )?(?:maximum )?health", () => "Increase base damage by <style=cIsDamage>" + c.Values.F("Damage Increase Per Stack (%)").ToString("0.##") + "%</style> <style=cStack>(+" + c.Values.F("Damage Increase Per Stack (%)").ToString("0.##") + "% per stack)</style>. Reduce maximum health by " + c.Values.F("Maximum Health Reduction Per Stack (%)").ToString("0.##") + "% <style=cStack>(+" + c.Values.F("Maximum Health Reduction Per Stack (%)").ToString("0.##") + "% per stack)</style>.");
            if (c.Values.Enabled.Value) U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(ShapedGlass), nameof(IL)));
        }
        static float DamageBase() => c.Values.Active ? 1 + c.Values.F("Damage Increase Per Stack (%)") / 100f : 2f;
        static float CurseBase() => c.Values.Active ? 1 / Mathf.Max(.0001f, 1 - c.Values.F("Maximum Health Reduction Per Stack (%)") / 100f) : 2f;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            int local = LunarConfigSupport.ItemLocal(code, "LunarDagger"); int matched = 0;
            for (int i = 0; i + 3 < code.Count; i++)
                if (local >= 0 && code[i].LoadsConstant(2f) && LunarConfigSupport.LocalIndex(code[i + 1], false) == local && code[i + 2].opcode == OpCodes.Conv_R4 && code[i + 3].Calls(AccessTools.Method(typeof(Mathf), "Pow")))
                {
                    code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(ShapedGlass), matched++ == 0 ? nameof(DamageBase) : nameof(CurseBase));
                }
            if (matched != 2) ThreeQsVanillaTweaks.Log.Warning("[Shaped Glass] Expected two stat multipliers; found " + matched + ".");
            return code;
        }
    }
}
