using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class Razorwire
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Razorwire", "Thorns", true);
            c.Pair("Damage (%)", 160, 40, "Damage per retaliatory razor. Vanilla: 160%, no damage stacking.");
            c.Pair("Targets", 5, 2, "Maximum targets per trigger. Vanilla: 5 +2 per stack.");
            c.Pair("Radius", 25, 10, "Search radius in metres. Vanilla: 25 +10 per stack.");
            c.Pair("Proc Coefficient", 0, 0, "Outgoing razor proc coefficient. Vanilla: 0.5.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance after taking damage. Vanilla: 100%.");
            // Keep native target selection and visual orbs; change their values.
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", 1.6f, () => c.Total("Damage (%)", c.Count(UncommonNative.Body)) / 100, "thorns", 0, 100);
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", .5f, () => c.Total("Proc Coefficient", c.Count(UncommonNative.Body)), "thorns", 0, 350);
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", 25, () => c.F("Radius"), "thorns", 0, 100);
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", 10, () => c.F("Radius Per Stack"), "thorns", 0, 100);
            // Target counts are replaced by a dedicated integer IL patch.
            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyLib.HarmonyMethod(typeof(Razorwire), nameof(Targets)));
            c.Text(() => "Taking damage fires <style=cIsDamage>razors</style> at up to " + c.V("Targets", "", "cIsUtility") + " enemies within " + c.V("Radius", "m", "cIsUtility") + " for " + c.V("Damage (%)", "%") + " damage.", () => "Taking damage fires <style=cIsDamage>razors</style> at nearby enemies.");
        }

        static int TargetCount(int native) => c.Active ? (U.Roll(c.Total("Trigger Chance (%)", c.Count(UncommonNative.Body)), UncommonNative.Body) ? Mathf.FloorToInt(c.Total("Targets", c.Count(UncommonNative.Body))) : 0) : native;
        static IEnumerable<HarmonyLib.CodeInstruction> Targets(IEnumerable<HarmonyLib.CodeInstruction> input)
        {
            var list = new List<HarmonyLib.CodeInstruction>(input);
            for (int i = 0; i < list.Count; i++)
                if (list[i].operand is System.Reflection.FieldInfo f && f.Name == "thorns")
                {
                    for (int j = i; j < Math.Min(i + 12, list.Count); j++)
                        if (list[j].opcode == System.Reflection.Emit.OpCodes.Add)
                        {
                            list.Insert(j + 1, new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call, A.Method(typeof(Razorwire), nameof(TargetCount))));
                            return list;
                        }

                    break;
                }

            throw new InvalidOperationException("Razorwire target-count anchor missing.");
        }
    }
}
