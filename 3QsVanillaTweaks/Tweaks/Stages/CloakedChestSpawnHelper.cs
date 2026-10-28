using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class CloakedChestSpawnHelper
    {
        internal static void Register(string scene, string title, params Vector3[] positions)
        {
            string section = "Stages - Stage 3 - " + title + " - Cloaked Chests";
            ConfigEntry<bool> enabled = ModConfig.BindBool(section, "Enabled", true, "Add cloaked chests at randomly selected locations.");
            var chances = new[] {
                StageFeatureSpawnHelper.Percent(section, "First Chest Chance (%)", 100f, "Independent chance to add one chest."),
                StageFeatureSpawnHelper.Percent(section, "Second Chest Chance (%)", 60f, "Independent chance to add another chest, even if the first roll fails."),
                StageFeatureSpawnHelper.Percent(section, "Third Chest Chance (%)", 30f, "Independent chance to add another chest, even if earlier rolls fail.") };
            var locations = new List<StageFeatureSpawnHelper.Settings>();
            for (int i = 0; i < positions.Length; ++i)
                locations.Add(Stage3SpawnHelper.Location(section + " - Location " + (char)('A' + i), scene, title + " Cloaked Chest " + (char)('A' + i), positions[i]));
            StageFeatureSpawnHelper.RegisterScene(scene, title + " cloaked chests", rng =>
            {
                if (!ModConfig.Get(enabled) || !SpawnRequirements.ExpansionEnabled("DLC2")) return;
                int count = 0;
                foreach (var chance in chances) if (StageFeatureSpawnHelper.Roll(ModConfig.Get(chance), rng)) ++count;
                if (count == 0) return;
                var shuffled = new List<StageFeatureSpawnHelper.Settings>(locations);
                Stage3SpawnHelper.Shuffle(shuffled, rng);
                GameObject prefab = StageInteractableAssets.Card("Stealthed", "RoR2.ChestBehavior").prefab;
                for (int i = 0; i < count && i < shuffled.Count; ++i)
                    StageFeatureSpawnHelper.Spawn(shuffled[i], prefab, instance =>
                    {
                        instance.name += "_Stealthed";
                        Stage3SpawnHelper.ScaleCost(instance);
                    });
            });
        }
    }
}
