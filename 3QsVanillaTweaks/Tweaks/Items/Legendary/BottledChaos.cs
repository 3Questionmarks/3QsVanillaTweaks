using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class BottledChaos
    {
        static LegendaryConfig config;
        internal static void Init() => LegendaryConfigSupport.Register("RandomEquipmentTrigger", Configure);
        static void Configure(LegendaryConfig c)
        {
            config = c;
            c.Pair("Random Equipment Activations", 1, 1, "Random equipment effects triggered on equipment use.");
            c.Describe(() => "Trigger " + c.Values.V("Random Equipment Activations", "", "cIsUtility") + " random equipment effect(s) when activating an equipment.");
            if (c.Values.Enabled.Value) {
                InstallExecutionHook();
                U.Harmony.Patch(AccessTools.Method(typeof(Inventory), "GetItemCountEffective", new[] { typeof(ItemDef) }), postfix: new HarmonyMethod(typeof(BottledChaos), nameof(Count)));
            }
        }
        static void Count(ItemDef itemDef, ref int __result)
        {
            // Only alter the count while executing equipment; inventory displays and other systems keep the real count.
            if (__result <= 0 || itemDef != DLC1Content.Items.RandomEquipmentTrigger || !config.Values.Active || !executing) return;
            __result = Mathf.Max(0, Mathf.RoundToInt(config.Values.Total("Random Equipment Activations", __result)));
        }
        [ThreadStatic] static bool executing;
        static void Begin(out bool __state) { __state = executing; executing = true; }
        static Exception End(bool __state, Exception __exception) { executing = __state; return __exception; }
        internal static void InstallExecutionHook()
        {
            // EquipmentLocation is passed by reference in current game versions.
            foreach (var method in typeof(EquipmentSlot).GetMethods())
                if (method.Name == "OnEquipmentExecuted" && method.GetParameters().Length == 2)
                    U.Harmony.Patch(method, prefix: new HarmonyMethod(typeof(BottledChaos), nameof(Begin)), finalizer: new HarmonyMethod(typeof(BottledChaos), nameof(End)));
        }
    }
}
