using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class FocusCrystal
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Focus Crystal");
            v0 = settings.Number("Damage Bonus (%)", 20f, "First-stack close-range damage bonus. Vanilla: 20%.");
            v1 = settings.Number("Damage Bonus Per Stack (%)", 20f, "Additional damage per stack. Vanilla: 20%.");
            v2 = settings.Number("Radius", 13f, "Range in metres. Vanilla: 13.");
            settings.Count("RoR2.HealthComponent", "TakeDamageProcess", "NearbyDamageBonus", .2f, v0, v1, 100);
            settings.Literal("RoR2.HealthComponent", "TakeDamageProcess", 169f, () => C.Get(v2) * C.Get(v2), "NearbyDamageBonus", window: 45);
            settings.Describe("ITEM_NEARBYDAMAGEBONUS_DESC", () => "Deal <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> additional <style=cIsDamage>damage</style> to enemies within <style=cIsUtility>" + C.Get(v2) + "m</style>.");
        }
    }
}
