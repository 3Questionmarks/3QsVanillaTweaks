using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class CautiousSlug
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> delay;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Cautious Slug");
            delay = settings.Number("Activation Delay", 7, "Seconds without damage before regeneration activates. Vanilla: 7.");
            v0 = settings.Number("Regeneration", 3f, "First-stack bonus. Vanilla: 3.");
            v1 = settings.Number("Regeneration Per Stack", 3f, "Bonus per additional stack. Vanilla: 3.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "HealWhileSafe", 3f, v0, v1, 1f);
            if (settings.Enabled.Value)
                S.Patch(S.Harmony("CautiousSlug"), typeof(CharacterBody), "RecalculateStats", typeof(CautiousSlug), transpiler: nameof(IL));
            settings.Describe("ITEM_HEALWHILESAFE_DESC", () => "After <style=cIsUtility>" + C.Get(delay) + " seconds</style> without taking damage, <style=cIsHealing>regenerate</style> an additional <style=cIsHealing>" + C.Get(v0) + "</style> <style=cStack>(+" + C.Get(v1) + " per stack)</style> health per second.");
        }
    }
}
