using System;
using RoR2;
using Tweaks.Stages;
using UnityEngine.Networking;
using Woods = Tweaks.Interactables.ShrineOfTheWoods;

namespace Tweaks.Bazaar
{
    internal static class BazaarShrineOfTheWoods
    {
        private const string SecondSlot = BazaarSpawnHelper.ExtraSection + "Shrine of the Woods 2";
        private const string PreviousSecondPose = "-105.5, -26.9977055, -50.19, 0, 205.537445, 0";
        private const string SecondPose = "-105.09, -26.66, -50.35, 0, 205.537445, 0";
        private static BazaarSpawnHelper.Settings settings, second;
        private static Stage lastStage;

        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - Shrine of the Woods",
                "Shrine of the Woods", null, 60f, -108.72f, -26.08f, -45.99f, .43f, 0f, .90f,
                exactPose: "-109.5, -26.9977055, -47.69, 0, 210, 0");
            second = BazaarSpawnHelper.BindFormerUnlockOnly("Shrine of the Woods 2", null,
                -105.09f, -26.66f, -50.35f, -.431101f, 0f, -.902304f, exactPose: SecondPose);
            if (second.ExactPose.Value.Trim() == PreviousSecondPose)
                second.ExactPose.Value = SecondPose;
            RoR2Application.onLoad += () => DebugStuff.InteractablePlacementEditor.UpdateSavedPose(
                "bazaar", SecondSlot, PreviousSecondPose, SecondPose);
            BazaarSpawnHelper.Register(typeof(BazaarShrineOfTheWoods), "Woods", nameof(AfterStageStart));
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || lastStage == __instance) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, Spawn));
        }

        private static void Spawn()
        {
            bool spawnFirst = BazaarSpawnHelper.Roll(settings);
            bool spawnSecond = BazaarSpawnHelper.Roll(second);
            if (!spawnFirst && !spawnSecond) return;
            int firstVariant = Woods.LoadoutVariantsEnabled ? Run.instance.stageRng.RangeInt(0, Woods.VariantCount) : -1;
            if (spawnFirst) Spawn(settings, firstVariant);
            if (!spawnSecond) return;
            int secondVariant = firstVariant < 0 ? -1 :
                (firstVariant + Run.instance.stageRng.RangeInt(1, Woods.VariantCount)) % Woods.VariantCount;
            Spawn(second, secondVariant);
        }

        private static void Spawn(BazaarSpawnHelper.Settings settings, int variant)
        {
            try
            {
                var prefab = StageInteractableAssets.Interactable("ShrineHealing", typeof(ShrineHealingBehavior).FullName);
                BazaarSpawnHelper.Spawn(settings, prefab, settings.Name, settings.Position, settings.Forward, instance =>
                {
                    var shrine = instance.GetComponentInChildren<ShrineHealingBehavior>(true);
                    var purchase = shrine ? shrine.GetComponent<PurchaseInteraction>() : null;
                    if (!purchase) throw new InvalidOperationException("The Woods shrine has no purchase interaction.");
                    purchase.automaticallyScaleCostWithDifficulty = false;
                    purchase.NetworkcostType = CostTypeIndex.LunarCoin;
                    purchase.Networkcost = 1;
                    shrine.costMultiplierPerPurchase = 1f;
                    if (variant >= 0) Woods.SetVariantTokens(purchase, variant);
                });
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Bazaar Woods shrine could not spawn: " + e.Message);
            }
        }
    }
}
