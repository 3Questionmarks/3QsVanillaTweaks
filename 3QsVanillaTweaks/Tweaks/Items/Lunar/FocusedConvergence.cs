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
    internal static class FocusedConvergence
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.Register("FocusConvergence", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Pair("Charge Speed Increase (%)", 30, 30, "Additional holdout-zone charge speed.");
            c.Pair("Radius Divisor", 2, 2, "Divide the zone radius by this amount. Vanilla: 2 +2 per stack.");
            c.Number("Maximum Effective Stacks", 3, "Maximum item stacks used per team.");
            c.Number("Startup Delay", 3, "Seconds before the effect activates.");
            c.Describe(() => "Teleporters charge " + c.Values.V("Charge Speed Increase (%)", "%", "cIsUtility") + " faster, but the size of the Teleporter zone is <style=cIsHealth>" + (100 - 100 / Mathf.Max(.001f, c.Values.F("Radius Divisor"))).ToString("0.##") + "%</style>" + (c.Values.F("Radius Divisor Per Stack") == 0 ? "" : " <style=cStack>(-" + (100 - 100 / Mathf.Max(.001f, c.Values.F("Radius Divisor Per Stack"))).ToString("0.##") + "% per stack)</style>") + " smaller.");
            foreach (string outer in new[] { "RoR2.HoldoutZoneController", "RoR2.KillZoneController" })
            {
                string nested = outer + "+FocusConvergenceController";
                c.FieldValue(nested, "cap", "Maximum Effective Stacks", 1, "DoUpdate");
                c.FieldValue(nested, "startupDelay", "Startup Delay", 1, "DoUpdate");
                var type = typeof(CharacterBody).Assembly.GetType(nested);
                if (type == null || !c.Values.Enabled.Value) continue;
                var radius = A.Method(type, "ApplyRadius"); var rate = A.Method(type, "ApplyRate");
                if (radius != null) U.Harmony.Patch(radius, prefix: new HarmonyMethod(typeof(FocusedConvergence), nameof(Radius)));
                if (rate != null) U.Harmony.Patch(rate, prefix: new HarmonyMethod(typeof(FocusedConvergence), nameof(Rate)));
            }
        }
        static int Count(object o) => (int)A.Field(o.GetType(), "currentFocusConvergenceCount").GetValue(o);
        static bool Radius(object __instance, ref float radius)
        {
            if (!c.Values.Active) return true;
            int n = Count(__instance); if (n > 0) radius /= Mathf.Max(.001f, c.Values.Total("Radius Divisor", n)); return false;
        }
        static bool Rate(object __instance, ref float rate)
        {
            if (!c.Values.Active) return true;
            int n = Count(__instance); if (n > 0) rate *= 1 + c.Values.Total("Charge Speed Increase (%)", n) / 100f; return false;
        }
    }
}
