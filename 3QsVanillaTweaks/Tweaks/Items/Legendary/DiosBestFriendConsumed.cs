using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Legendary
{
    internal static class DiosBestFriendConsumed
    {
        static ConfigEntry<bool> enabled, scrap;
        internal static void Init()
        {
            const string section = "Items - 03 Legendary - Dios Best Friend (Consumed)";
            enabled = C.BindStartupBool(section, "Enabled", true, "Enable consumed Dio's Best Friend changes. Restart to apply.");
            scrap = C.BindBool(section, "Can Scrap", true, "Allow consumed Dio's Best Friend to produce Legendary Scrap. Does not add it to loot pools.");
            if (!enabled.Value)
                return;
            S.Ensure();
            S.ScrapAllowed["ExtraLifeConsumed"] = () => C.Get(enabled) && C.Get(scrap);
            S.ScrapTiers["ExtraLifeConsumed"] = ItemTier.Tier3;
        }
    }
}
