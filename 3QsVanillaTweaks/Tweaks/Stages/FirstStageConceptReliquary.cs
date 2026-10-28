using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class FirstStageConceptReliquary
    {
        private static StageFeatureSpawnHelper.Settings settings;
        private static readonly HashSet<int> Completed = new HashSet<int>();

        internal static void Init()
        {
            const string section = "Stages - Stage 1 - General - Concept Reliquary";
            settings = StageFeatureSpawnHelper.BindRandom(section, "Stage 1 Maps", "Concept Reliquary");
            SceneDirector.onPostPopulateSceneServer += AfterPopulate;
            SceneManager.sceneUnloaded += scene => Completed.Remove(scene.handle);
        }

        private static void AfterPopulate(SceneDirector director)
        {
            if (!NetworkServer.active || !Run.instance || !director ||
                !ModConfig.Get(settings.Enabled) || !SpawnRequirements.ExpansionEnabled("DLC4")) return;
            SceneDef scene = SceneCatalog.FindSceneDef(director.gameObject.scene.name);
            if (!scene || scene.stageOrder != 1 || !SpawnRequirements.SceneAllowed(scene)) return;
            if (!Completed.Add(director.gameObject.scene.handle)) return;
            try
            {
                var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
                if (!StageFeatureSpawnHelper.Roll(ModConfig.Get(settings.Chance), rng)) return;
                SpawnCard card = StageInteractableAssets.Card("HollowChest");
                if (!StageFeatureSpawnHelper.PrefabAllowed(card.prefab)) return;
                ChestBehavior template = card.prefab.GetComponent<ChestBehavior>();
                foreach (ChestBehavior chest in UnityEngine.Object.FindObjectsOfType<ChestBehavior>())
                    if (chest.gameObject.scene == director.gameObject.scene &&
                        (chest.name.StartsWith(card.prefab.name, StringComparison.OrdinalIgnoreCase) ||
                         (template && template.dropTable && chest.dropTable == template.dropTable))) return;
                if (!DirectorCore.instance) throw new InvalidOperationException("DirectorCore is unavailable.");
                for (int attempt = 0; attempt < 32; ++attempt)
                {
                    var rule = new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Random };
                    if (DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, rule, rng))) return;
                }
                Log.Warning("Stage 1 Concept Reliquary: no valid director placement found after 32 attempts.");
            }
            catch (Exception e) { Log.Warning("Stage 1 Concept Reliquary guarantee failed: " + e); }
        }
    }
}
