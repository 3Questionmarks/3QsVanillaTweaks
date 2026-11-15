using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class FirstStageHalcyonShrine
    {
        private sealed class Pending
        {
            internal SpawnCard Card;
            internal Xoroshiro128Plus Rng;
            internal bool Spawn;
        }
        private static StageFeatureSpawnHelper.Settings settings;
        private static readonly Dictionary<SceneDirector, Pending> PendingSpawns = new Dictionary<SceneDirector, Pending>();

        internal static void Init()
        {
            const string section = "Stages - Stage 1 - General - Halcyon Shrine";
            settings = StageFeatureSpawnHelper.BindRandom(section, "Stage 1 Maps", "Halcyon Shrine");
            SceneDirector.onGenerateInteractableCardSelection += FilterNaturalShrines;
            SceneDirector.onPostPopulateSceneServer += AfterPopulate;
            Run.onRunDestroyGlobal += run => PendingSpawns.Clear();
        }

        private static void FilterNaturalShrines(SceneDirector director, DirectorCardCategorySelection selection)
        {
            if (!UnityEngine.Networking.NetworkServer.active || !Run.instance || !director ||
                !StageFeatureSpawnHelper.IsStageOneMap(director.gameObject.scene) ||
                !ModConfig.Get(settings.Enabled) || !selection) return;
            if (!PendingSpawns.TryGetValue(director, out Pending pending))
            {
                pending = RollForStage();
                PendingSpawns[director] = pending;
            }
            // This selection is a temporary copy made by SceneDirector; never mutate the original stage assets.
            for (int i = 0; i < selection.categories.Length; ++i)
            {
                var category = selection.categories[i];
                var retained = new List<DirectorCard>();
                foreach (DirectorCard card in category.cards ?? Array.Empty<DirectorCard>())
                {
                    SpawnCard spawnCard = card != null ? card.GetSpawnCard() : null;
                    bool halcyon = spawnCard && spawnCard.prefab && spawnCard.prefab.GetComponent<HalcyoniteShrineInteractable>();
                    if (!halcyon) { retained.Add(card); continue; }
                    if (!pending.Card && card.IsAvailable()) pending.Card = spawnCard;
                }
                category.cards = retained.ToArray();
                selection.categories[i] = category;
            }
        }

        private static Pending RollForStage()
        {
            var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
            return new Pending { Rng = rng, Spawn = StageFeatureSpawnHelper.Roll(ModConfig.Get(settings.Chance), rng) };
        }

        private static void AfterPopulate(SceneDirector director)
        {
            if (!director) return;
            PendingSpawns.TryGetValue(director, out Pending pending);
            PendingSpawns.Remove(director);
            if (!UnityEngine.Networking.NetworkServer.active || !Run.instance ||
                !StageFeatureSpawnHelper.IsStageOneMap(director.gameObject.scene) ||
                !ModConfig.Get(settings.Enabled) || !StageFeatureSpawnHelper.ExpansionEnabled("RoR2/DLC2/Common/DLC2.asset")) return;
            try
            {
                pending = pending ?? RollForStage();
                if (!pending.Spawn) return;
                // Use the stage's own card whenever possible, retaining any stage-specific prefab tuning.
                SpawnCard card = pending.Card ? pending.Card : StageFeatureSpawnHelper.LoadSpawnCard("iscShrineHalcyonite");
                if (!StageFeatureSpawnHelper.PrefabAllowed(card.prefab)) return;
                foreach (HalcyoniteShrineInteractable existing in UnityEngine.Object.FindObjectsOfType<HalcyoniteShrineInteractable>())
                    if (existing.gameObject.scene == director.gameObject.scene) return;
                if (!DirectorCore.instance) throw new InvalidOperationException("DirectorCore is unavailable.");
                // As with all director placement, a map with no valid nodes can fail.
                for (int attempt = 0; attempt < 20; ++attempt)
                {
                    var rule = new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Random };
                    GameObject instance = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, rule, pending.Rng));
                    if (!instance) continue;
                    DebugStuff.InteractablePlacementEditor.Register(instance, settings.Name, settings.Enabled.Definition.Section, alreadySpawned: true);
                    Log.Info("Stage feature spawned Halcyon Shrine on " + director.gameObject.scene.name + " at " + instance.transform.position);
                    return;
                }
                Log.Warning("Stage 1 Halcyon Shrine: chance roll succeeded, but no valid director placement was found after 20 attempts.");
            }
            catch (Exception e) { Log.Warning("Stage 1 Halcyon Shrine failed: " + e); }
        }
    }
}
