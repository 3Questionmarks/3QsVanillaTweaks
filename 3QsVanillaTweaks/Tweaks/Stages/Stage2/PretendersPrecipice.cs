using UnityEngine;

namespace Tweaks.Stages.Stage2
{
    internal static class PretendersPrecipice
    {
        internal static void Init()
        {
            var scrapper = StageFeatureSpawnHelper.Bind("Stages - Stage 2 - Pretenders Precipice - Drone Scrapper",
                "nest", "Pretenders Precipice Drone Scrapper", -137.95f, 126.81f, -61.31f);
            // Move an unchanged old default; preserve any customised position.
            if (Mathf.Approximately(scrapper.X.Value, -268.99f) && Mathf.Approximately(scrapper.Y.Value, 98.54f) &&
                Mathf.Approximately(scrapper.Z.Value, -125.13f))
            {
                scrapper.X.Value = -137.95f;
                scrapper.Y.Value = 126.81f;
                scrapper.Z.Value = -61.31f;
            }
            StageFeatureSpawnHelper.Register(scrapper, (settings, rng) =>
            {
                if (SpawnRequirements.ExpansionEnabled("DLC3"))
                    StageFeatureSpawnHelper.Spawn(settings, StageInteractableAssets.Interactable("DroneScrapper", "RoR2.DroneScrapperController"));
            });
            var combiner = StageFeatureSpawnHelper.Bind("Stages - Stage 2 - Pretenders Precipice - Drone Combiner",
                "nest", "Pretenders Precipice Drone Combiner", 254.63f, 145.02f, 47.99f);
            StageFeatureSpawnHelper.Register(combiner, (settings, rng) =>
            {
                if (SpawnRequirements.ExpansionEnabled("DLC3"))
                    StageFeatureSpawnHelper.Spawn(settings, StageInteractableAssets.Interactable("DroneCombiner", "RoR2.DroneCombinerController"));
            });
        }
    }
}
