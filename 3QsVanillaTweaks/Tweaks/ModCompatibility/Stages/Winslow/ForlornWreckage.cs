using System.Collections.Generic;
using RoR2;
using ThreeQsVanillaTweaks;
using Tweaks.Stages;
using Tweaks.Stages.Stage3;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.ModCompatibility.Stages.Winslow
{
    internal static class ForlornWreckage
    {
        internal const string SceneName = "forgottenwreckage_ws";
        private const string Section = "Mod Compatibility - Stages - Forlorn Wreckage";
        private static readonly Pose[] ChestLocations =
        {
            new Pose(new Vector3(189.47f, 264.67f, -338.81f), Quaternion.Euler(0f, 340.05f, 0f)),
            new Pose(new Vector3(-58.42f, 175.28f, -252.09f), Quaternion.Euler(0f, 211.68f, 0f)),
            new Pose(new Vector3(288.93f, 254.89f, 146.86f), Quaternion.Euler(0f, 52.3f, 0f))
        };

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S4 (Newt (3))",
                new Vector3(385.086700f, 314.844100f, 195.014100f), new Vector3(346.364700f, 264.733400f, 356.679000f));
            if (!ModConfig.ModuleEnabled(typeof(RallypointDelta))) return;
            var enabled = ModConfig.BindBool(Section, "Timed Security Chests", true,
                "Add timed chests using the same timer, loot and extra chest chances as Rallypoint Delta.");
            RallypointDelta.RegisterScene(SceneName);
            StageFeatureSpawnHelper.RegisterScene(SceneName, "Timed Security Chests", rng =>
            {
                if (ModConfig.Get(enabled)) SpawnChests(rng);
            });
        }

        private static void SpawnChests(Xoroshiro128Plus rng)
        {
            Scene scene = SceneManager.GetSceneByName(SceneName);
            var prefab = StageFeatureSpawnHelper.LoadPrefab(RallypointDelta.TimedChestAddress);
            int count = RallypointDelta.ChestCount(ChestLocations.Length, rng);
            var freeLocations = new List<Pose>(ChestLocations);
            for (int i = 0; i < count; ++i)
            {
                int index = rng.RangeInt(0, freeLocations.Count);
                Pose pose = freeLocations[index];
                freeLocations.RemoveAt(index);
                ModdedStageFeatures.SpawnInScene(scene, prefab, pose.position, pose.rotation);
            }
            Log.Info("Forlorn Wreckage: spawned " + count + " timed security chest(s).");
        }
    }
}
