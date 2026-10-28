namespace Tweaks.Stages.Stage5
{
    internal static class HelminthHatchery
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 5 - Helminth Hatchery - Scrapper",
                "helminthroost", "Helminth Hatchery Scrapper", -718.62f, -150.04f, 385.50f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
            {
                if (!SpawnRequirements.ExpansionEnabled("DLC2")) return;
                StageFeatureSpawnHelper.Spawn(spawn, StageInteractableAssets.Interactable("Scrapper", "RoR2.ScrapperController"));
            });
        }
    }
}
