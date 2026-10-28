using RoR2;

namespace Tweaks.Stages.Stage1
{
    internal static class SiphonedForest
    {
        internal static void Init()
        {
            var settings0 = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 1 - Siphoned Forest - Legendary Printer",
                "snowyforest", "Siphoned Forest Legendary Printer", -65.23f, 81.02f, -186.54f, 0.45f, 0.89f);
            StageFeatureSpawnHelper.Register(settings0, (settings, rng) =>
                StageFeatureSpawnHelper.SpawnPrinter(settings, "RoR2/Base/DuplicatorMilitary/DuplicatorMilitary.prefab", ItemTier.Tier3));
        }
    }
}
