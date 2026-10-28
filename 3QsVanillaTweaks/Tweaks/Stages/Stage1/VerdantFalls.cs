namespace Tweaks.Stages.Stage1
{
    internal static class VerdantFalls
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 1 - Verdant Falls - Compulsion Shrine",
                "lakes", "Verdant Falls Shrine of Compulsion", -160.93f, 1.03f, -81.49f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) => StageFeatureSpawnHelper.SpawnCompulsionShrine(spawn));
        }
    }
}
