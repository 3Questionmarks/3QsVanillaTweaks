using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Items.Legendary
{
    internal static class DiosBestFriend
    {
        private const string Section = "Items - 03 Legendary - Dios Best Friend";
        private static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true,
                "Restore consumed Dio's Best Friend stacks at the beginning of each new stage.");

            if (ModConfig.Get(enabled))
            {
                LanguageAPI.Add("ITEM_EXTRALIFE_PICKUP",
                    "Revive on death with <style=cIsUtility>temporary invulnerability</style>. <style=cIsHealing>Regenerates each stage.</style>");

                LanguageAPI.Add("ITEM_EXTRALIFE_DESC",
                    "Upon death, this item will be <style=cIsUtility>consumed</style> and you will return to life with <style=cIsUtility>3 seconds of invulnerability</style>. " +
                    "<style=cIsHealing>Regenerates at the start of each stage.</style>");

                // Like Regenerating Scrap (Consumed), the depleted item explains
                // that it will turn back into its usable counterpart.
                LanguageAPI.Add("ITEM_EXTRALIFECONSUMED_PICKUP",
                    "<style=cIsUtility>Regenerates at the start of the next stage.</style>");

                LanguageAPI.Add("ITEM_EXTRALIFECONSUMED_DESC",
                    "At the start of each stage, it transforms into <style=cIsHealing>Dio's Best Friend</style>.");
            }

            Stage.onStageStartGlobal += OnStageStart;
        }

        private static void OnStageStart(Stage stage)
        {
            // Inventory changes and transform notifications must originate on the server.
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !Run.instance) return;

            ItemDef original = RoR2Content.Items.ExtraLife;
            ItemDef consumed = RoR2Content.Items.ExtraLifeConsumed;
            if (!original || !consumed) return;

            foreach (CharacterMaster master in CharacterMaster.readOnlyInstancesList)
            {
                if (!master || !master.inventory) continue;

                Inventory inventory = master.inventory;
                int count = inventory.GetItemCountPermanent(consumed.itemIndex);
                if (count <= 0) continue;

                inventory.RemoveItemPermanent(consumed.itemIndex, count);
                inventory.GiveItemPermanent(original.itemIndex, count);

                // Use the same transformation popup system as Regenerating Scrap.
                // One popup per item type avoids flooding the screen for stacked items.
                CharacterMasterNotificationQueue.SendTransformNotification(
                    master,
                    consumed.itemIndex,
                    original.itemIndex,
                    CharacterMasterNotificationQueue.TransformationType.RegeneratingScrapRegen);
            }
        }
    }
}
