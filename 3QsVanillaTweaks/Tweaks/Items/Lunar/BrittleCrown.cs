using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RoR2;
using HarmonyLib;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Lunar
{
    internal static class BrittleCrown
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Brittle Crown", "GoldOnHit", true, "Lunar");
            c.Pair("Trigger Chance (%)", 30, 0, "Base on-hit gold chance before incoming proc scaling. Vanilla: 30%.");
            c.Pair("Gold Gained", 2, 2, "Gold per proc before difficulty scaling. Vanilla: 2 +2 per stack.");
            c.Pair("Gold Loss Multiplier", 1, 1, "Gold loss on damage, multiplied by the fraction of maximum combined health lost. Vanilla: 1 per item.");
            c.B("Chance Scales With Attack Damage", true, "Increase gold proc chance for attacks dealing more than 100% damage. Vanilla: false.");
            c.N("Chance Gained Per Damage Interval (%)", 1, "Extra percentage points of chance for each full interval over 100% attack damage. Vanilla: 0%.");
            c.N("Attack Damage Interval (%)", 10, "Attack damage above 100% needed for each extra chance interval.", .01f);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 30, Chance, "GoldOnHit", 0, 60);
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 2, "Gold Gained");
            if (c.Enabled.Value) U.Harmony.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(BrittleCrown), nameof(LossIL)));
            c.Text(() => c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance on hit to gain " + c.V("Gold Gained", " gold", "cIsUtility") + ". <style=cIsUtility>Scales over time.</style>" + (c.B("Chance Scales With Attack Damage") ? " Chance increases by <style=cIsUtility>" + c.F("Chance Gained Per Damage Interval (%)").ToString("0.##") + "%</style> for every <style=cIsDamage>" + c.F("Attack Damage Interval (%)").ToString("0.##") + "%</style> attack damage above 100%." : "") + " <style=cIsHealth>Lose gold</style> on taking damage equal to " + LossText() + " of the <style=cIsHealth>maximum health percentage you lost.</style>", () => "Gain gold on hit... <style=cIsHealth>but lose gold when taking damage.</style>");
        }
        static string LossText() => "<style=cIsHealth>" + (c.F("Gold Loss Multiplier") * 100).ToString("0.##") + "%</style>" + (c.F("Gold Loss Multiplier Per Stack") == 0 ? "" : " <style=cStack>(+" + (c.F("Gold Loss Multiplier Per Stack") * 100).ToString("0.##") + "% per stack)</style>");
        static float Chance()
        {
            int n = c.Count(N.Body); float result = c.Total("Trigger Chance (%)", n);
            if (c.B("Chance Scales With Attack Damage") && N.Body && N.Body.damage > 0 && N.Info != null)
                result += Mathf.Floor(Mathf.Max(0, N.Info.damage / N.Body.damage * 100 - 100) / c.F("Attack Damage Interval (%)")) * c.F("Chance Gained Per Damage Interval (%)");
            return Mathf.Clamp(result, 0, 100);
        }
        static float Loss(float count) => c.Active ? c.Total("Gold Loss Multiplier", Mathf.RoundToInt(count)) : count;
        static IEnumerable<CodeInstruction> LossIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int found = 0;
            for (int i = code.Count - 2; i >= 0; i--)
                if (code[i].operand is FieldInfo f && f.Name == "goldOnHit" && code[i + 1].opcode == OpCodes.Conv_R4)
                { code.Insert(i + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(BrittleCrown), nameof(Loss)))); found++; }
            if (found != 1) throw new InvalidOperationException("Brittle Crown gold loss anchor changed: " + found);
            return code;
        }
    }
}
