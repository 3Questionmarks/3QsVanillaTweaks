using BepInEx.Bootstrap;
using RoR2;

namespace Tweaks.Items.Equipment
{
    /// <summary>
    /// Registers native elite aspect equipment in the equipment config. ZetAspects
    /// owns these equipment definitions when installed, so this module yields to it.
    /// </summary>
    internal static class Aspects
    {
        // Native game aspects only. ZetAspects' additional equipment is deliberately
        // not enumerated here, and the whole module is skipped while ZetAspects loads.
        static readonly string[] NativeAspects =
        {
            "AffixBlue", "AffixHaunted", "AffixLunar", "AffixPoison", "AffixRed", "AffixWhite",
            "EliteAurelioniteEquipment", "EliteBeadEquipment", "EliteCollectiveEquipment",
            "EliteDeathEquipment", "EliteEarthEquipment", "EliteFireEquipment", "EliteHauntedEquipment",
            "EliteIceEquipment", "EliteLightningEquipment", "EliteLunarEquipment", "ElitePoisonEquipment",
            "EliteVoidEquipment"
        };

        internal static void Init()
        {
            EquipmentConfig.Defer(Register);
        }

        static void Register()
        {
            if (Chainloader.PluginInfos.ContainsKey("com.TPDespair.ZetAspects"))
                return;

            foreach (string id in NativeAspects)
            {
                EquipmentDef def = EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(id));
                if (!def)
                    continue;

                // Existing unchanged equipment configs are opt-in; preserve that
                // default for aspects and retain their game-provided descriptions.
                _ = new EquipmentConfig(Language.GetString(def.nameToken), id, false);
            }
        }
    }
}
