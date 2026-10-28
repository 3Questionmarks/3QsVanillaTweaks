namespace Tweaks.Stages.Stage1
{
    internal static class ViscousFalls
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 1 - Viscous Falls - Compulsion Shrine",
                "lakesnight", "Viscous Falls Shrine of Compulsion", 13.39f, -49.78f, 130.88f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) => StageFeatureSpawnHelper.SpawnCompulsionShrine(spawn));
        }
    }
}
