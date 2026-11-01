using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class EnergyDrink
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Energy Drink");
            v0 = settings.Number("Sprint Speed (%)", 25f, "First-stack bonus. Vanilla: 25.");
            v1 = settings.Number("Sprint Speed Per Stack (%)", 25f, "Bonus per additional stack. Vanilla: 25.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "SprintBonus", 0.25f, v0, v1, 100f);
            settings.Describe("ITEM_SPRINTBONUS_DESC", () => "Increase <style=cIsUtility>sprint speed</style> by <style=cIsUtility>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style>.");
        }
    }
}
