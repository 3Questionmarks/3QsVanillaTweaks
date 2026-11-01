using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class PersonalShieldGenerator
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Personal Shield Generator");
            v0 = settings.Number("Shield (%)", 8f, "First-stack bonus. Vanilla: 8.");
            v1 = settings.Number("Shield Per Stack (%)", 8f, "Bonus per additional stack. Vanilla: 8.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "PersonalShield", 0.08f, v0, v1, 100f);
            settings.Describe("ITEM_PERSONALSHIELD_DESC", () => "Gain a <style=cIsHealing>shield</style> equal to <style=cIsHealing>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> maximum health.");
        }
    }
}
