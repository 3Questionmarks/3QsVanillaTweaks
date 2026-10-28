using RoR2;

namespace Tweaks.Stages.Stage1
{
    internal static class DistantRoost
    {
        internal static void Init()
        {
            var settings0 = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 1 - Distant Roost - Boss Printer",
                "blackbeach", "Distant Roost Boss Printer", 200.9f, -118.2f, -107.4f, -0.6f, -0.79f);
            StageFeatureSpawnHelper.Register(settings0, (settings, rng) =>
                StageFeatureSpawnHelper.SpawnPrinter(settings, "RoR2/Base/DuplicatorWild/DuplicatorWild.prefab", ItemTier.Boss));
            var settings1 = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 1 - Distant Roost Alternate - Boss Printer",
                "blackbeach2", "Distant Roost Alternate Boss Printer", -131.3f, 47f, -96.3f, 0.91f, 0.39f);
            StageFeatureSpawnHelper.Register(settings1, (settings, rng) =>
                StageFeatureSpawnHelper.SpawnPrinter(settings, "RoR2/Base/DuplicatorWild/DuplicatorWild.prefab", ItemTier.Boss));
        }
    }
}
