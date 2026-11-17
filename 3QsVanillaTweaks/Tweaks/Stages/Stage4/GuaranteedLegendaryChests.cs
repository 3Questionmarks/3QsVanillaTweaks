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
        private static ConfigEntry<bool> enabled, reopensPerPlayer;
        private static ConfigEntry<int> itemsPerPlayer;
        private static readonly HashSet<ChestBehavior> FixedChests = new HashSet<ChestBehavior>();
        private static readonly HashSet<ChestBehavior> DirectorChests = new HashSet<ChestBehavior>();
        private static readonly HashSet<ChestBehavior> ReportedChests = new HashSet<ChestBehavior>();
        // Chests that have already dropped once; later drops are reopens (e.g. RetroactiveMacro's Sale Star).
        private static readonly HashSet<ChestBehavior> OpenedChests = new HashSet<ChestBehavior>();
        private static readonly Dictionary<int, List<ChestBehavior>> SceneChests = new Dictionary<int, List<ChestBehavior>>();

        internal static void Init()
        {
            const string section = "Stages - Stage 4 - General - Guaranteed Legendary Chests";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Guaranteed legendary chests drop an item for each player. Random legendary chests are unaffected.");
            itemsPerPlayer = ModConfig.Bind(section, "Items Per Player", 1,
                new ConfigDescription("Items per player from a guaranteed legendary chest. Sale Star adds its usual bonus drops.", new AcceptableValueRange<int>(1, 100)));
            reopensPerPlayer = ModConfig.BindBool(section, "Reopens Drop Per Player", false,
                "Also give the per-player items when a guaranteed legendary chest is reopened, such as by Sale Star with RetroactiveMacro. When off, a reopen drops its normal amount.");
            SceneManager.sceneLoaded += (scene, mode) => CaptureSceneChests(scene);
            SceneDirector.onPrePopulateSceneServer += director =>
            {
                if (director) CaptureSceneChests(director.gameObject.scene);
            };
            SceneDirector.onPostPopulateSceneServer += ClassifySceneChests;
            SceneDirector.onSpawnInteractableServer += (director, spawned, card) =>
            {
                if (spawned && spawned.GetComponent<ChestBehavior>() is ChestBehavior chest)
                    DirectorChests.Add(chest);
            };
            SceneManager.sceneUnloaded += scene =>
            {
                SceneChests.Remove(scene.handle);
                FixedChests.RemoveWhere(chest => !chest || chest.gameObject.scene == scene);
                DirectorChests.RemoveWhere(chest => !chest || chest.gameObject.scene == scene);
                OpenedChests.RemoveWhere(chest => !chest || chest.gameObject.scene == scene);
            };
            Run.onRunDestroyGlobal += run => { FixedChests.Clear(); DirectorChests.Clear(); ReportedChests.Clear(); OpenedChests.Clear(); SceneChests.Clear(); };
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.GuaranteedLegendaryChests");
            harmony.Patch(AccessTools.Method(typeof(ChestBehavior), "ItemDrop"),
                prefix: new HarmonyMethod(typeof(GuaranteedLegendaryChests), nameof(BeforeDrop)),
                finalizer: new HarmonyMethod(typeof(GuaranteedLegendaryChests), nameof(AfterDrop)));
        }

        private static void CaptureSceneChests(Scene scene)
        {
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !Run.instance) return;
            SceneDef definition = SceneCatalog.FindSceneDef(scene.name);
            if (!definition || definition.sceneType != SceneType.Stage ||
                (definition.stageOrder != 4 && Run.instance.stageClearCount % 5 != 3) ||
                !SpawnRequirements.SceneAllowed(definition)) return;
            // Capture authored objects before Start methods generate random chests.
            // Include inactive locations; the game's own selection still chooses which appear.
            if (!SceneChests.TryGetValue(scene.handle, out var authored))
                SceneChests.Add(scene.handle, authored = new List<ChestBehavior>());
            foreach (ChestBehavior chest in UnityEngine.Object.FindObjectsOfType<ChestBehavior>(true))
                if (chest.gameObject.scene == scene && !authored.Contains(chest)) authored.Add(chest);
        }

        private static void ClassifySceneChests(SceneDirector director)
        {
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !Run.instance || !director || !SceneChests.TryGetValue(director.gameObject.scene.handle, out var authored)) return;
            int count = 0;
            foreach (ChestBehavior chest in authored)
                if (IsLegendaryCandidate(chest) && FixedChests.Add(chest)) ++count;
            Log.Info("Stage 4 legendary chests: recognised " + count + " fixed chest location(s) on " + director.gameObject.scene.name + ".");
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

        private static int RewardPlayerCount()
        {
            int players = Mathf.Max(1, Run.instance.participatingPlayerCount);
            if (Tweaks.DebugStuff.ExtraPlayers.Active)
                players = Mathf.Max(players, PlayerCharacterMasterController.instances.Count + Tweaks.DebugStuff.ExtraPlayers.Count);
            return players;
        }

        private static void BeforeDrop(ChestBehavior __instance, out int? __state)
        {
            __state = null;
            // Some modded stages create their guaranteed chest after the director's
            // population pass. Random director chests are recorded separately.
            if (NetworkServer.active && __instance && IsLegendaryCandidate(__instance) &&
                SceneChests.ContainsKey(__instance.gameObject.scene.handle) && !DirectorChests.Contains(__instance))
                FixedChests.Add(__instance);
            if (NetworkServer.active && Run.instance && __instance && IsLegendaryCandidate(__instance) &&
                ReportedChests.Add(__instance))
                Log.Info("Legendary chest drop: " + __instance.gameObject.scene.name + "/" + __instance.name +
                    ", fixed=" + FixedChests.Contains(__instance) + ", director spawned=" + DirectorChests.Contains(__instance) +
                    ", players=" + RewardPlayerCount() +
                    ", base drops=" + __instance.dropCount + ".");
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !FixedChests.Contains(__instance) || __instance.dropCount < 1) return;
            // Only the first drop is the chest's own reward; a reopen keeps its normal count unless configured.
            if (!OpenedChests.Add(__instance) && !ModConfig.Get(reopensPerPlayer)) return;
            int players = RewardPlayerCount();
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
