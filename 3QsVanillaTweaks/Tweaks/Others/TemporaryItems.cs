using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class TemporaryItems
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> duration;
        internal static float EffectiveDuration => enabled != null && C.Get(enabled) ? C.Get(duration) : 80f;

        internal static void Init()
        {
            const string section = "Other - Temporary Items";
            enabled = C.BindStartupBool(section, "Enabled", true, "Enable temporary item duration changes. Restart to apply.");
            duration = C.Bind(section, "Duration Of Temporary Items", 90f, new ConfigDescription("Seconds of lifetime per temporary item stack. Existing slots retain their cached duration; start a new run after changing this value. Vanilla: 80 seconds.", new AcceptableValueRange<float>(0.01f, 1000000f)));
            if (!enabled.Value)
                return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.TemporaryItems");
            var storage = typeof(Inventory).GetNestedType("TempItemsStorage", System.Reflection.BindingFlags.NonPublic);
            if (storage != null)
                harmony.Patch(AccessTools.Method(storage, "SetDecayDurationServer"), prefix: new HarmonyMethod(typeof(TemporaryItems), nameof(BeforeStorageDuration)));
            harmony.Patch(AccessTools.Method(typeof(TempItemContainer), "CalcDecayRateForItem"), postfix: new HarmonyMethod(typeof(TemporaryItems), nameof(AfterDuration)));
        }

        private static void BeforeStorageDuration(ref float newDecayDuration)
        {
            if (C.Get(enabled))
                newDecayDuration = Math.Max(0.01f, newDecayDuration + EffectiveDuration - 80f);
        }

        private static void AfterDuration(ref float __result)
        {
            if (C.Get(enabled))
                __result = C.Get(duration);
        }
    }
}
