using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class SoldiersSyringe
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Soldiers Syringe");
            v0 = settings.Number("Attack Speed (%)", 15f, "First-stack bonus. Vanilla: 15.");
            v1 = settings.Number("Attack Speed Per Stack (%)", 15f, "Bonus per additional stack. Vanilla: 15.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "Syringe", 0.15f, v0, v1, 100f);
            settings.Describe("ITEM_SYRINGE_DESC", () => "Increase <style=cIsDamage>attack speed</style> by <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style>.");
        }
    }
}
