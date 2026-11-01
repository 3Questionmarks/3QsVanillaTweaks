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
    internal static class EulogyZero
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.Register("RandomlyLunar", Configure);
        static void Configure(LunarConfig config)
        {
            c = config; c.Pair("Lunar Replacement Chance (%)", 5, 5, "Chance to replace an eligible pickup with a lunar pickup, based on the team's total stacks.");
            c.Describe(() => "Items have a " + c.Values.V("Lunar Replacement Chance (%)", "%", "cIsUtility") + " chance to become a <style=cIsLunar>lunar item</style> instead.");
            if (!c.Values.Enabled.Value) return;
            var type = typeof(CharacterBody).Assembly.GetType("RoR2.Items.RandomlyLunarUtils");
            foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "CheckForLunarReplacement" && method.Name != "CheckForLunarReplacementUniqueArray") continue;
                if (method.IsGenericMethodDefinition)
                {
                    U.Harmony.Patch(method.MakeGenericMethod(typeof(UniquePickup[])), transpiler: new HarmonyMethod(typeof(EulogyZero), nameof(IL)));
                    U.Harmony.Patch(method.MakeGenericMethod(typeof(List<UniquePickup>)), transpiler: new HarmonyMethod(typeof(EulogyZero), nameof(IL)));
                }
                else U.Harmony.Patch(method, transpiler: new HarmonyMethod(typeof(EulogyZero), nameof(IL)));
            }
        }
        static float Chance(float native) => c.Values.Active ? c.Values.Total("Lunar Replacement Chance (%)", Mathf.RoundToInt(native / .05f)) / 100f : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            bool chance = false;
            foreach (var ins in input)
            {
                yield return ins;
                if (ins.LoadsConstant(.05f)) chance = true;
                else if (chance && ins.opcode == OpCodes.Mul) { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(EulogyZero), nameof(Chance))); chance = false; }
            }
        }
    }
}
