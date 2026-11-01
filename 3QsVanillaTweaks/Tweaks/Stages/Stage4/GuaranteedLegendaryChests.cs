using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage4
{
    internal static class GuaranteedLegendaryChests
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<int> itemsPerPlayer;
        private static readonly HashSet<ChestBehavior> FixedChests = new HashSet<ChestBehavior>();
        private static readonly Dictionary<int, List<ChestBehavior>> SceneChests = new Dictionary<int, List<ChestBehavior>>();
        private static PickupDropTable legendaryTable;

        internal static void Init()
        {
            const string section = "Stages - Stage 4 - General - Guaranteed Legendary Chests";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Guaranteed legendary chests drop an item for each player. Random legendary chests are unaffected.");
            itemsPerPlayer = ModConfig.Bind(section, "Items Per Player", 1,
                new ConfigDescription("Items per player from a guaranteed legendary chest. Sale Star adds its usual bonus drops.", new AcceptableValueRange<int>(1, 100)));
            SceneManager.sceneLoaded += (scene, mode) => CaptureSceneChests(scene);
            SceneDirector.onPostPopulateSceneServer += ClassifySceneChests;
            SceneManager.sceneUnloaded += scene =>
            {
                SceneChests.Remove(scene.handle);
                FixedChests.RemoveWhere(chest => !chest || chest.gameObject.scene == scene);
            };
            Run.onRunDestroyGlobal += run => { FixedChests.Clear(); SceneChests.Clear(); };
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.GuaranteedLegendaryChests");
            harmony.Patch(AccessTools.Method(typeof(ChestBehavior), "ItemDrop"),
                prefix: new HarmonyMethod(typeof(GuaranteedLegendaryChests), nameof(BeforeDrop)),
                finalizer: new HarmonyMethod(typeof(GuaranteedLegendaryChests), nameof(AfterDrop)));
        }

        private static void CaptureSceneChests(Scene scene)
        {
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !Run.instance || SceneChests.ContainsKey(scene.handle)) return;
            SceneDef definition = SceneCatalog.FindSceneDef(scene.name);
            if (!definition || definition.stageOrder != 4 || !SpawnRequirements.SceneAllowed(definition)) return;
            // Capture authored objects before Start methods generate random chests.
            // Include inactive locations; the game's own selection still chooses which appear.
            var authored = new List<ChestBehavior>();
            foreach (ChestBehavior chest in UnityEngine.Object.FindObjectsOfType<ChestBehavior>(true))
                if (chest.gameObject.scene == scene) authored.Add(chest);
            SceneChests.Add(scene.handle, authored);
        }

        private static void ClassifySceneChests(SceneDirector director)
        {
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !Run.instance || !director || !SceneChests.TryGetValue(director.gameObject.scene.handle, out var authored)) return;
            // A boss-reward stage has no guaranteed chest to classify.
            // Avoid loading chest assets just because the stage has order four.
            if (!authored.Exists(IsLegendaryCandidate)) return;
            try
            {
                // Resolve the asset after scene loading completes, using only the snapshot.
                if (!legendaryTable)
                {
                    ChestBehavior prefab = LegendaryChestPrefab();
                    if (!prefab || !prefab.dropTable) throw new InvalidOperationException("Legendary chest drop table is unavailable.");
                    legendaryTable = prefab.dropTable;
                }
                int count = 0;
                foreach (ChestBehavior chest in authored)
                    if (chest && chest.dropTable == legendaryTable && FixedChests.Add(chest)) ++count;
                Log.Info("Stage 4 legendary chests: recognised " + count + " fixed chest location(s) on " + director.gameObject.scene.name + ".");
            }
            catch (Exception e) { Log.Warning("Stage 4 legendary chest detection failed: " + e.Message); }
        }

        private static bool IsLegendaryCandidate(ChestBehavior chest)
        {
            if (!chest) return false;
            string name = chest.gameObject.name;
            if (name.StartsWith("GoldChest", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Chest3", StringComparison.OrdinalIgnoreCase)) return true;
            string table = chest.dropTable ? chest.dropTable.name : "";
            return string.Equals(table, "dtTier3", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(table, "dtGoldChest", StringComparison.OrdinalIgnoreCase);
        }

        private static ChestBehavior LegendaryChestPrefab()
        {
            foreach (string name in new[] { "GoldChest", "Chest3" })
            {
                try
                {
                    GameObject asset = StageInteractableAssets.Prefab(name);
                    var chest = asset ? asset.GetComponent<ChestBehavior>() : null;
                    if (chest && chest.dropTable) return chest;
                }
                catch (InvalidOperationException) { }
            }
            // Director spawn cards remain usable when prefab address keys change.
            try
            {
                GameObject asset = StageInteractableAssets.Card("GoldChest", "RoR2.ChestBehavior").prefab;
                var chest = asset ? asset.GetComponent<ChestBehavior>() : null;
                if (chest && chest.dropTable) return chest;
            }
            catch (InvalidOperationException) { }
            throw new InvalidOperationException("Legendary chest prefab/drop table is unavailable (GoldChest/Chest3).");
        }

        private static void BeforeDrop(ChestBehavior __instance, out int? __state)
        {
            __state = null;
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !FixedChests.Contains(__instance) || __instance.dropCount < 1) return;
            int players = Mathf.Max(1, Run.instance.participatingPlayerCount);
            __state = __instance.dropCount;
            // Vanilla has already added Sale Star's drops to dropCount at purchase.
            // Replace only the original single base drop with the player allowance.
            __instance.dropCount += players * ModConfig.Get(itemsPerPlayer) - 1;
        }

        private static void AfterDrop(ChestBehavior __instance, int? __state)
        {
            if (__state.HasValue && __instance) __instance.dropCount = __state.Value;
        }
    }
}
