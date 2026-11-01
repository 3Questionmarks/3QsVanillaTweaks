using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class LensMakersGlasses
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Lens-Makers Glasses");
            v0 = settings.Number("Critical Chance (%)", 10f, "First-stack bonus. Vanilla: 10.");
            v1 = settings.Number("Critical Chance Per Stack (%)", 10f, "Bonus per additional stack. Vanilla: 10.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "CritGlasses", 10f, v0, v1, 1f);
            settings.Describe("ITEM_CRITGLASSES_DESC", () => "Gain <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> <style=cIsDamage>critical strike chance</style>.");
        }
    }
}
