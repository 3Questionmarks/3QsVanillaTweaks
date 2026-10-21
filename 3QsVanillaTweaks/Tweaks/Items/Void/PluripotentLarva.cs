using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine.Networking;
using ModConfig = ThreeQsVanillaTweaks.Config.ConfigManager;

namespace ThreeQsVanillaTweaks.Tweaks.Items.Void
{
    internal static class PluripotentLarva
    {
        private const string Section = "Items - 06 Void - Pluripotent Larva";
        private static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true,
                "Restore consumed Pluripotent Larva stacks at the beginning of each new stage.");

            if (enabled.Value)
            {
                LanguageAPI.Add("ITEM_EXTRALIFEVOID_PICKUP",
                    "Revive on death and <style=cIsVoid>corrupt items</style>. <style=cIsHealing>Regenerates each stage.</style>");

                LanguageAPI.Add("ITEM_EXTRALIFEVOID_DESC",
                    "Upon death, this item will be <style=cIsUtility>consumed</style> and you will return to life with <style=cIsUtility>3 seconds of invulnerability</style>, and all of your items that can be corrupted will be. " +
                    "<style=cIsHealing>Regenerates at the start of each stage.</style>");

                // Like Regenerating Scrap (Consumed), the depleted item explains
                // that it will turn back into its usable counterpart.
                LanguageAPI.Add("ITEM_EXTRALIFEVOIDCONSUMED_PICKUP",
                    "<style=cIsUtility>Regenerates at the start of the next stage.</style>");

                LanguageAPI.Add("ITEM_EXTRALIFEVOIDCONSUMED_DESC",
                    "At the start of each stage, it transforms into <style=cIsHealing>Pluripotent Larva</style>.");
            }

            Stage.onStageStartGlobal += OnStageStart;
        }

        private static void OnStageStart(Stage stage)
        {
            // Inventory changes and transform notifications must originate on the server.
            if (!enabled.Value || !NetworkServer.active || !Run.instance) return;

            ItemDef original = DLC1Content.Items.ExtraLifeVoid;
            ItemDef consumed = DLC1Content.Items.ExtraLifeVoidConsumed;
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
