using System;
using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    internal static class BazaarPrinter
    {
        private static readonly string[] Names = { "Common", "Uncommon", "Legendary", "Boss", "Lunar",
            "Void Common", "Void Uncommon", "Void Legendary", "Void Boss" };
        private static readonly ItemTier[] Tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3,
            ItemTier.Boss, ItemTier.Lunar, ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss };
        private static readonly float[] Defaults = { 60f, 30f, 0f, 0f, 10f, 0f, 0f, 0f, 0f };
        private static readonly string[] Paths = {
            "RoR2/Base/Duplicator/Duplicator.prefab",
            "RoR2/Base/DuplicatorLarge/DuplicatorLarge.prefab",
            "RoR2/Base/DuplicatorMilitary/DuplicatorMilitary.prefab",
            "RoR2/Base/DuplicatorWild/DuplicatorWild.prefab"
        };
        private static BazaarSpawnHelper.Settings settings;
        private static ConfigEntry<float>[] weights;
        private static ConfigEntry<bool> spawnWithCommand;
        private static Stage lastStage;

        internal static void Init()
        {
            string section = "Bazaar - 05 Extra Interactables - 02 3D Printer";
            settings = BazaarSpawnHelper.Bind(section, "02 3D Printer", Paths[0],
                75f, -69.6f, -24.4f, -30f, -0.9f, 0f, 0.2f);
            spawnWithCommand = ModConfig.BindBool(section, "Spawn With Command", false,
                "Allow the extra Bazaar printer to spawn while the Artifact of Command is enabled.");
            weights = new ConfigEntry<float>[Tiers.Length];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = ModConfig.Bind(section, Names[i] + " Weight (%)", Defaults[i],
                    "Relative spawn weight for this printer rarity; 0 disables the tier.");
            BazaarSpawnHelper.Register(typeof(BazaarPrinter), "BazaarPrinter", nameof(AfterStageStart));
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || __instance == lastStage) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, SpawnPrinter));
        }

        private static void SpawnPrinter()
        {
            if (SpawnRequirements.CommandActive && !ModConfig.Get(spawnWithCommand)) return;
            if (!BazaarSpawnHelper.Roll(settings)) return;
            var eligibleWeights = new float[weights.Length];
            for (int i = 0; i < weights.Length; i++)
                if (SpawnRequirements.PrinterTierAvailable(Tiers[i]))
                {
                    float weight = ModConfig.Get(weights[i]);
                    eligibleWeights[i] = float.IsNaN(weight) || float.IsInfinity(weight) ? 0f : Mathf.Max(0f, weight);
                }
            float total = 0f;
            for (int i = 0; i < weights.Length; i++) total += eligibleWeights[i];
            if (total <= 0f) { Log.Warning("Bazaar 3D Printer: no enabled rarity has a positive weight and available items for this run."); return; }
            float roll = UnityEngine.Random.value * total;
            int choice = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                if (eligibleWeights[i] <= 0f) continue;
                choice = i;
                roll -= eligibleWeights[i];
                if (roll < 0f) { choice = i; break; }
            }
            if (choice < 0) return;
            ItemTier tier = Tiers[choice];
            string name = Names[choice];
            string path = choice < 4 ? Paths[choice] : Paths[0];
            BazaarSpawnHelper.Spawn(settings, path, "3D Printer (" + name + ")", settings.Position, settings.Forward, instance =>
            {
                ShopTerminalBehavior terminal = instance.GetComponent<ShopTerminalBehavior>();
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                if (terminal == null || purchase == null)
                    throw new InvalidOperationException("Printer prefab lacks shop/purchase components.");
                terminal.dropTable = null;
                terminal.itemTier = tier;
                purchase.cost = 1;
                purchase.costType = PrinterCostType(tier);
            });
        }

        private static CostTypeIndex PrinterCostType(ItemTier tier)
        {
            switch (tier)
            {
                case ItemTier.Tier2:
                case ItemTier.VoidTier2: return CostTypeIndex.GreenItem;
                case ItemTier.Tier3:
                case ItemTier.VoidTier3: return CostTypeIndex.RedItem;
                case ItemTier.Boss:
                case ItemTier.VoidBoss: return CostTypeIndex.BossItem;
                case ItemTier.Lunar: return CostTypeIndex.LunarItemOrEquipment;
                default: return CostTypeIndex.WhiteItem;
            }
        }
    }
}
