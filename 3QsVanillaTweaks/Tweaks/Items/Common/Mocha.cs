using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class Mocha
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        static ConfigEntry<float> v3;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Mocha");
            v0 = settings.Number("Attack Speed (%)", 7.5f, "First-stack attack-speed bonus. Vanilla: 7.5%.");
            v1 = settings.Number("Attack Speed Per Stack (%)", 7.5f, "Bonus per additional stack. Vanilla: 7.5%.");
            v2 = settings.Number("Movement Speed (%)", 7f, "First-stack movement-speed bonus. Vanilla: 7%.");
            v3 = settings.Number("Movement Speed Per Stack (%)", 7f, "Bonus per additional stack. Vanilla: 7%.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "AttackSpeedAndMoveSpeed", .075f, v0, v1, 100);
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "AttackSpeedAndMoveSpeed", .07f, v2, v3, 100);
            settings.Describe("ITEM_ATTACKSPEEDANDMOVESPEED_DESC", () => "Increase <style=cIsDamage>attack speed</style> by <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> and <style=cIsUtility>movement speed</style> by <style=cIsUtility>" + C.Get(v2) + "%</style> <style=cStack>(+" + C.Get(v3) + "% per stack)</style>.");
        }
    }
}
