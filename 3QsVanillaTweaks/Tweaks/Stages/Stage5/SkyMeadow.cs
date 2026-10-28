namespace Tweaks.Stages.Stage5
{
    internal static class SkyMeadow
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 5 - Sky Meadow - Scrapper",
                "skymeadow", "Sky Meadow Scrapper", -116.37f, -87.85f, -119.87f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
                StageFeatureSpawnHelper.Spawn(spawn, StageInteractableAssets.Interactable("Scrapper", "RoR2.ScrapperController")));
        }
    }
}
