using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Lunar
{
    internal static class Transcendence
    {
        static R c;
        static readonly FieldInfo danger = A.Field(typeof(CharacterBody), "outOfDangerStopwatch");
        internal static void Init()
        {
            U.Ensure(); c = new R("Transcendence", "ShieldOnly", true, "Lunar");
            c.Pair("Maximum Health Increase (%)", 50, 50, "Maximum-health bonus converted to shield. Vanilla: 50% +25% per stack.");
            c.Pair("Extra Shield Recharge Delay", 0, 0, "Additional seconds added to the general shield regeneration delay while holding Transcendence. Vanilla: 0 extra seconds.");
            c.Pair("Extra Shield Replenish Time", 0, 1, "Seconds added to the normal full-shield recharge duration. Uses the normal shield replenishment duration as its base. Vanilla: 0.");
            c.N("Minimum Shield Replenish Time", .05f, "Minimum shield replenishment duration.", .01f);
            c.Literal("RoR2.CharacterBody", "RecalculateStats", 1.5f, () => 1 + c.F("Maximum Health Increase (%)") / 100, "ShieldOnly");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .25f, () => c.F("Maximum Health Increase (%) Per Stack") / 100, "ShieldOnly");
            if (c.Enabled.Value) RoR2Application.onLoad += () => U.Harmony.Patch(A.Method(typeof(HealthComponent), "ServerFixedUpdate"), transpiler: new HarmonyMethod(typeof(Transcendence), nameof(RechargeIL)));
            c.Text(() => "Increase <style=cIsHealing>maximum health</style> by " + c.V("Maximum Health Increase (%)", "%", "cIsHealing") + ". Convert <style=cIsHealth>all but 1 health</style> into <style=cIsHealing>regenerating shields</style>. Shields take " + ReplenishText() + " <style=cIsHealth>longer</style> to fully regenerate." + (c.F("Extra Shield Recharge Delay") == 0 && c.F("Extra Shield Recharge Delay Per Stack") == 0 ? "" : " Shield recharge is delayed by an additional " + c.V("Extra Shield Recharge Delay", " seconds", "cIsHealth") + ".") + (Tweaks.Others.ShieldMechanics.TranscendenceDisablesOSP ? " <style=cIsHealth>Lose normal one-shot protection</style>." : ""), () => "Increase maximum health... <style=cIsHealth>but replace your health with shields.</style>");
        }
        static string ReplenishText()
        {
            float first = c.F("Extra Shield Replenish Time"), extra = c.F("Extra Shield Replenish Time Per Stack");
            return "<style=cIsHealth>" + first.ToString("0.##") + (first == 1 ? " second" : " seconds") + "</style>" + (extra == 0 ? "" : " <style=cStack>(+" + extra.ToString("0.##") + (extra == 1 ? " second" : " seconds") + " per stack)</style>");
        }
        internal static float ReplenishRate(float native, float extra, float minimum) => native <= 0 ? native : 1 / Mathf.Max(minimum, 1 / native + extra);
        static int Count(HealthComponent h) => h ? c.Count(h.body) : 0;
        static bool Ready(bool native, HealthComponent h)
        { int n = Count(h); return n == 0 ? native : danger != null && (float)danger.GetValue(h.body) >= Tweaks.Others.ShieldMechanics.RechargeDelay + c.Total("Extra Shield Recharge Delay", n); }
        static bool Broke(bool native, HealthComponent h) => Count(h) > 0 ? false : native;
        static float Rate(float native, HealthComponent h)
        {
            int n = Count(h); if (n == 0 || native <= 0) return native;
            return ReplenishRate(native, c.Total("Extra Shield Replenish Time", n), c.F("Minimum Shield Replenish Time"));
        }
        static IEnumerable<CodeInstruction> RechargeIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int ready = 0, rate = 0, broke = 0;
            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i]; yield return instruction;
                string callback = null;
                if (instruction.operand is MethodInfo m && (m.Name == "get_outOfDanger" || m.DeclaringType.FullName == "Tweaks.Others.ShieldMechanics" && m.Name == "ShieldReady")) { callback = nameof(Ready); ready++; }
                if (instruction.operand is FieldInfo f && f.Name == "shieldBrokeSinceDangerCheck") { callback = nameof(Broke); broke++; }
                if (instruction.operand is MethodInfo mr && mr.DeclaringType.FullName == "Tweaks.Others.ShieldMechanics" && mr.Name == "Rate" || instruction.LoadsConstant(.5f) && i > 0 && code[i - 1].operand is MethodInfo getter && getter.Name == "get_maxShield") { callback = nameof(Rate); rate++; }
                if (callback != null) { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(Transcendence), callback)); }
            }
            if (ready != 1 || rate != 1 || broke != 1) throw new InvalidOperationException("Transcendence shield recharge anchors changed: " + ready + "/" + rate + "/" + broke);
        }
    }
}
