using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class BisonSteak
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Bison Steak");
            v0 = settings.Number("Maximum Health", 25f, "First-stack bonus. Vanilla: 25.");
            v1 = settings.Number("Maximum Health Per Stack", 25f, "Bonus per additional stack. Vanilla: 25.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "FlatHealth", 25f, v0, v1, 1f);
            settings.Describe("ITEM_FLATHEALTH_DESC", () => "Increase <style=cIsHealing>maximum health</style> by <style=cIsHealing>" + C.Get(v0) + "</style> <style=cStack>(+" + C.Get(v1) + " per stack)</style>.");
        }
    }
}
