using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class RepulsionArmorPlate
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Repulsion Armor Plate");
            v0 = settings.Number("Damage Reduction", 5f, "Flat damage reduction from one plate. Vanilla: 5.");
            v1 = settings.Number("Damage Reduction Per Stack", 5f, "Additional reduction per stack. Vanilla: 5.");
            v2 = settings.Number("Minimum Damage", 1f, "Minimum incoming damage after plate reduction. Vanilla: 1.");
            settings.Count("RoR2.HealthComponent", "TakeDamageProcess", "armorPlate", 5f, v0, v1);
            settings.Literal("RoR2.HealthComponent", "TakeDamageProcess", 1f, () => C.Get(v2), "armorPlate", window: 35);
            settings.Describe("ITEM_ARMORPLATE_DESC", () => "<style=cIsHealing>Reduce incoming damage</style> by <style=cIsHealing>" + C.Get(v0) + "</style> <style=cStack>(+" + C.Get(v1) + " per stack)</style>, to a minimum of <style=cIsUtility>" + C.Get(v2) + " damage</style>.");
        }
    }
}
