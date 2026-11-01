using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class ArmorPiercingRounds
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Armor-Piercing Rounds");
            v0 = settings.Number("Damage Bonus (%)", 20f, "First-stack damage bonus against bosses. Vanilla: 20%.");
            v1 = settings.Number("Damage Bonus Per Stack (%)", 20f, "Additional boss damage per stack. Vanilla: 20%.");
            settings.Count("RoR2.HealthComponent", "TakeDamageProcess", "BossDamageBonus", .2f, v0, v1, 100);
            settings.Describe("ITEM_BOSSDAMAGEBONUS_DESC", () => "Deal <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> additional <style=cIsDamage>damage</style> to bosses.");
        }
    }
}
