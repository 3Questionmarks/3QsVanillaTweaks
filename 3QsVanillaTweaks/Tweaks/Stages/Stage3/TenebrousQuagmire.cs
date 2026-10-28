namespace Tweaks.Stages.Stage3
{
    internal static class TenebrousQuagmire
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 3 - Tenebrous Quagmire - Hallowed Shrine", "mire", "Hallowed Shrine", -209.77f, 29.86f, 210.25f, 0.73f, -0.68f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
            {
                if (!SpawnRequirements.ExpansionEnabled("DLC4")) return;
                var prefab = StageInteractableAssets.Interactable("Hallow", "RoR2.GrantHexesController");
                StageFeatureSpawnHelper.Spawn(spawn, prefab);
            });
        }
    }
}
