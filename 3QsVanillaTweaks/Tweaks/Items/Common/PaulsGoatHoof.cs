using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class PaulsGoatHoof
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Pauls Goat Hoof");
            v0 = settings.Number("Movement Speed (%)", 14f, "First-stack bonus. Vanilla: 14.");
            v1 = settings.Number("Movement Speed Per Stack (%)", 14f, "Bonus per additional stack. Vanilla: 14.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "Hoof", 0.14f, v0, v1, 100f);
            settings.Describe("ITEM_HOOF_DESC", () => "Increase <style=cIsUtility>movement speed</style> by <style=cIsUtility>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style>.");
        }
    }
}
