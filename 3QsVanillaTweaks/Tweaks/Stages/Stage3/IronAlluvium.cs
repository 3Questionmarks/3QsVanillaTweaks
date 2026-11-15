using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using HarmonyLib;
using RoR2;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage3
{
    internal static class IronAlluvium
    {
        private static ConfigEntry<bool> registeredChest;
        private static readonly List<BasicPickupDropTable> dropTables = new List<BasicPickupDropTable>();
        private static readonly string[] rarities = { "Common", "Uncommon", "Legendary", "Boss", "Lunar", "Void Common", "Void Uncommon", "Void Legendary", "Void Boss" };
        private static readonly string[] weightFields = { "tier1Weight", "tier2Weight", "tier3Weight", "bossWeight", "lunarItemWeight", "voidTier1Weight", "voidTier2Weight", "voidTier3Weight", "voidBossWeight" };
        private static readonly string[] dropLists = { "availableTier1DropList", "availableTier2DropList", "availableTier3DropList", "availableBossDropList", "availableLunarItemDropList", "availableVoidTier1DropList", "availableVoidTier2DropList", "availableVoidTier3DropList", "availableVoidBossDropList" };
        private const string RegistrationPlugin = "Viliger.RegisterCommandChest";

        internal static void Init()
        {
            if (ModConfig.ModuleEnabled(typeof(Tweaks.ModCompatibility.LookingGlass)))
                registeredChest = ModConfig.BindBool("Mod Compatibility - Interactables - Register Command Chest", "Enabled", true,
                    "Use RegisterCommandChest's prefab for guaranteed Command Chests in Iron Alluvium, Iron Auroras and Broadcast Perch when installed. Keeps the stage cost and drop settings. Without it, use a large chest.");
            Run.onRunDestroyGlobal += CleanupDropTables;
            Register("ironalluvium", "Iron Alluvium");
            Register("ironalluvium2", "Iron Auroras");
        }

        private static void Register(string scene, string title) =>
            RegisterChest("Stages - Stage 3 - " + title + " - Command Chest", scene, title,
                new Vector3(-69.37f, 63.67f, 68.14f), 0.75f, -0.65f, requiresDlc3: true, exactPose: false);

        internal static void RegisterChest(string section, string scene, string title, Vector3 position,
            float forwardX, float forwardZ, bool requiresDlc3, bool exactPose)
        {
            var settings = StageFeatureSpawnHelper.Bind(section, scene, title + " Command Chest",
                position.x, position.y, position.z, forwardX, forwardZ, snapToGround: !exactPose,
                alignToGround: !exactPose);
            ConfigEntry<float> multiplier = ModConfig.Bind(section, "Cost Multiplier", 3f,
                new ConfigDescription("Price compared with a normal large chest.", new AcceptableValueRange<float>(0f, 100f)));
            ConfigEntry<int> count = ModConfig.Bind(section, "Drops With Command", 3,
                new ConfigDescription("Number of choice droplets when the Artifact of Command is enabled.", new AcceptableValueRange<int>(1, 100)));
            var snapBase = ModConfig.BindBool(section, "Snap Chest Base To Ground", true,
                "When Snap To Ground is enabled, align the bottom of the chest model instead of its root pivot. Height Offset is still applied.");
            ConfigEntry<float>[] weights = new ConfigEntry<float>[rarities.Length];
            for (int i = 0; i < weights.Length; i++)
                weights[i] = ModConfig.Bind(section + " - Rarity Weights", rarities[i] + " Weight", i == 0 ? 30f : i == 1 ? 60f : i == 2 ? 10f : 0f,
                    new ConfigDescription("Relative chance of this rarity. Zero excludes it. Available nonzero weights are normalized; 30/60/10 gives 30% common, 60% uncommon and 10% legendary. All excluded or unavailable means no chest spawns.", new AcceptableValueRange<float>(0f, 1000000f)));
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
            {
                if (requiresDlc3 && !SpawnRequirements.ExpansionEnabled("DLC3")) return;
                GameObject largeChest = StageInteractableAssets.Prefab("Chest2");
                bool useRegistered = (registeredChest == null ? !requiresDlc3 : ModConfig.Get(registeredChest)) &&
                    BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(RegistrationPlugin);
                // Reuse the exact registered prefab; do not clone or register another network asset.
                GameObject prefab = useRegistered ? StageInteractableAssets.Prefab("CommandChest") : largeChest;
                PurchaseInteraction referenceCost = largeChest.GetComponent<PurchaseInteraction>();
                if (!referenceCost) throw new InvalidOperationException("Large chest purchase component was not found.");
                BasicPickupDropTable table = CreateDropTable(weights, title);
                if (!table) return;
                GameObject spawned = StageFeatureSpawnHelper.Spawn(spawn, prefab, instance =>
                {
                    PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                    if (!purchase) throw new InvalidOperationException("Command chest purchase component was not found.");
                    // The registered prefab can have its own base price. Preserve our configured large-chest pricing.
                    purchase.costType = referenceCost.costType;
                    purchase.cost = referenceCost.cost;
                    Stage3SpawnHelper.ScaleCost(instance, ModConfig.Get(multiplier));
                    ChestBehavior chest = instance.GetComponent<ChestBehavior>();
                    if (!chest) throw new InvalidOperationException("Command chest behavior was not found.");
                    chest.dropTable = table;
                    if (ModConfig.Get(spawn.SnapToGround) && ModConfig.Get(snapBase)) SnapModelBase(instance);
                    Stage3SpawnHelper.SetMember(chest, "NetworkisCommandChest", true);
                    int drops = SpawnRequirements.CommandActive ? ModConfig.Get(count) : 1;
                    Stage3SpawnHelper.SetMember(chest, "minDropCount", drops);
                    Stage3SpawnHelper.SetMember(chest, "maxDropCount", drops);
                });
                if (!spawned) { dropTables.Remove(table); UnityEngine.Object.Destroy(table); }
            });
        }
        internal static float PerItemWeight(float rarityWeight, int availableItems) => availableItems <= 0 ? 0f : Math.Max(0f, rarityWeight) / availableItems;
        private static BasicPickupDropTable CreateDropTable(ConfigEntry<float>[] weights, string title)
        {
            if (!Run.instance) return null;
            var table = ScriptableObject.CreateInstance<BasicPickupDropTable>();
            table.name = "3Qs_" + title.Replace(" ", "") + "_CommandChestDrops";
            // Start with every non-item and special-tier weight disabled, including future known fields.
            foreach (FieldInfo field in typeof(BasicPickupDropTable).GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (field.FieldType == typeof(float) && field.Name.EndsWith("Weight", StringComparison.Ordinal)) field.SetValue(table, 0f);
            bool available = false;
            for (int i = 0; i < weights.Length; i++)
            {
                var list = AccessTools.Field(typeof(Run), dropLists[i])?.GetValue(Run.instance) as ICollection;
                int count = list?.Count ?? 0;
                float weight = PerItemWeight(ModConfig.Get(weights[i]), count);
                AccessTools.Field(typeof(BasicPickupDropTable), weightFields[i])?.SetValue(table, weight);
                available |= weight > 0f;
            }
            if (!available)
            {
                UnityEngine.Object.Destroy(table);
                ThreeQsVanillaTweaks.Log.Warning(title + " command chest skipped: all configured rarities are excluded or unavailable.");
                return null;
            }
            table.RegenerateDropTable(Run.instance);
            dropTables.Add(table);
            return table;
        }
        private static void CleanupDropTables(Run run)
        {
            foreach (BasicPickupDropTable table in dropTables) if (table) UnityEngine.Object.Destroy(table);
            dropTables.Clear();
        }
        private static void SnapModelBase(GameObject instance)
        {
            Vector3 contact = instance.transform.position;
            Vector3 up = instance.transform.up.normalized;
            float lowest = float.PositiveInfinity;
            foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = filter.sharedMesh;
                Renderer renderer = filter.GetComponent<Renderer>();
                if (!mesh || !renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
                Bounds bounds = mesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    float height = Vector3.Dot(filter.transform.TransformPoint(local) - contact, up);
                    lowest = Math.Min(lowest, height);
                }
            }
            if (!float.IsInfinity(lowest) && !float.IsNaN(lowest))
            {
                instance.transform.position -= up * lowest;
                Physics.SyncTransforms();
            }
        }
    }
}
