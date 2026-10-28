using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage3
{
    internal static class SulfurPools
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> chance, voidChance;
        private static StageFeatureSpawnHelper.Settings[] locations;
        private static StageFeatureSpawnHelper.Settings printer;

        internal static void Init()
        {
            const string section = "Stages - Stage 3 - Sulfur Pools - Lockboxes";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Add lockboxes at the configured locations.");
            chance = StageFeatureSpawnHelper.Percent(section, "Chance Per Lockbox (%)", 100f, "Chance for each of the three lockboxes to spawn. Each rolls separately.");
            voidChance = StageFeatureSpawnHelper.Percent(section, "Void Replacement Chance (%)", 15, "Chance for each spawned lockbox to become a void lockbox.");
            locations = new[] {
                Stage3SpawnHelper.Location(section + " - 1", "sulfurpools", "Lockbox 1", new Vector3(0.82f, -11.44f, -61.15f)),
                Stage3SpawnHelper.Location(section + " - 2", "sulfurpools", "Lockbox 2", new Vector3(-11.63f, -15.98f, -66.88f)),
                Stage3SpawnHelper.Location(section + " - 3", "sulfurpools", "Lockbox 3", new Vector3(12.59f, -8.99f, -54.68f)) };
            StageFeatureSpawnHelper.RegisterScene("sulfurpools", "Sulfur Pools lockboxes", SpawnLockboxes);
            printer = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 3 - Sulfur Pools - Rusted Key Printer", "sulfurpools", "Rusted Key Printer", 32f, -36.1f, 86.62f, -0.02f, -0.99f);
            StageFeatureSpawnHelper.Register(printer, (settings, rng) => SpawnPrinter());
        }

        private static void SpawnLockboxes(Xoroshiro128Plus rng)
        {
            if (!ModConfig.Get(enabled)) return;
            foreach (var location in locations)
            {
                if (!StageFeatureSpawnHelper.Roll(ModConfig.Get(chance), rng)) continue;
                bool isVoid = SpawnRequirements.ExpansionEnabled("DLC1") && StageFeatureSpawnHelper.Roll(ModConfig.Get(voidChance), rng);
                string address = isVoid ? "RoR2/DLC1/TreasureCacheVoid/LockboxVoid.prefab" : "RoR2/Base/TreasureCache/Lockbox.prefab";
                StageFeatureSpawnHelper.Spawn(location, StageFeatureSpawnHelper.LoadPrefab(address));
            }
        }

        private static void SpawnPrinter()
        {
            if (SpawnRequirements.CommandActive && !ModConfig.Get(printer.SpawnWithCommand)) return;
            ItemDef key = Stage3SpawnHelper.Item("TreasureCache");
            if (!key || !SpawnRequirements.ItemAllowed(key) || !Run.instance.IsItemAvailable(key.itemIndex)) return;
            StageFeatureSpawnHelper.Spawn(printer, StageFeatureSpawnHelper.LoadPrefab("RoR2/Base/Duplicator/Duplicator.prefab"), instance =>
            {
                ShopTerminalBehavior terminal = instance.GetComponent<ShopTerminalBehavior>();
                terminal.selfGeneratePickup = false; terminal.dropTable = null; terminal.itemTier = ItemTier.Tier1;
                Stage3SpawnHelper.SetMember(terminal, "Networkpickup", new UniquePickup(PickupCatalog.FindPickupIndex(key.itemIndex)));
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                purchase.costType = CostTypeIndex.WhiteItem; purchase.Networkcost = 1;
            });
        }
    }
}
