using RoR2;
using UnityEngine.Networking;
using R = Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Void
{
    internal static class PluripotentLarvaConsumed
    {
        static R c;
        internal static bool RestoreEnabled => PluripotentLarva.Enabled && c != null && c.Active && c.B("Regenerate Each Stage");
        internal static void Init()
        {
            c = new R("Pluripotent Larva (Consumed)", "ExtraLifeVoidConsumed", true, "Void");
            c.B("Regenerate Each Stage", true, "Restore consumed stacks at the start of the next stage. Requires Pluripotent Larva changes to be enabled. Vanilla: false.");
            c.Text(() => RestoreEnabled ? "At the start of each stage, it transforms into <style=cIsHealing>Pluripotent Larva</style>." : "A spent Pluripotent Larva.",
                () => RestoreEnabled ? "<style=cIsUtility>Regenerates at the start of the next stage.</style>" : "A spent Pluripotent Larva.");
            Stage.onStageStartGlobal += OnStageStart;
        }
        private static void OnStageStart(Stage stage)
        {
            // Inventory changes and transform notifications must originate on the server.
            if (!RestoreEnabled || !NetworkServer.active || !Run.instance) return;

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
