using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class ItemTransformNotifications
    {
        internal static void Init()
        {
            if (!C.BindStartupBool("Others - Item Transformation Notifications", "Enabled", true,
                "Prevent invalid transformation popups when an item is removed without a replacement. Restart to apply.").Value)
                return;
            new Harmony("com.3questionmarks.vanillatweaks.itemtransformnotifications").Patch(
                AccessTools.Method(typeof(CharacterMasterNotificationQueue), "SendTransformNotificationInternal"),
                prefix: new HarmonyMethod(typeof(ItemTransformNotifications), nameof(BeforeSend)));
        }

        static bool BeforeSend(CharacterMaster characterMaster, PickupIndex oldIndex, PickupIndex newIndex)
        {
            // Removal-only events have no destination pickup. Keep the inventory
            // operation and its other callbacks; omit only the invalid popup packet.
            if (!characterMaster) return false;
            var oldPickup = PickupCatalog.GetPickupDef(oldIndex);
            var newPickup = PickupCatalog.GetPickupDef(newIndex);
            return oldPickup != null && newPickup != null;
        }
    }
}
