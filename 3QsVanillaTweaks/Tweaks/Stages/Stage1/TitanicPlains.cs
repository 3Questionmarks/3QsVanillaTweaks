using RoR2;

namespace Tweaks.Stages.Stage1
{
    internal static class TitanicPlains
    {
        internal static void Init()
        {
            var settings0 = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 1 - Titanic Plains - Uncommon Printer",
                "golemplains", "Titanic Plains Uncommon Printer", -102.5f, -131.8f, -425.6f, 0.15f, 0.98f);
            StageFeatureSpawnHelper.Register(settings0, (settings, rng) =>
                StageFeatureSpawnHelper.SpawnPrinter(settings, "RoR2/Base/DuplicatorLarge/DuplicatorLarge.prefab", ItemTier.Tier2));
            var settings1 = StageFeatureSpawnHelper.BindPrinter("Stages - Stage 1 - Titanic Plains Alternate - Uncommon Printer",
                "golemplains2", "Titanic Plains Alternate Uncommon Printer", 65.2f, 67.8f, 11.6f, 0.84f, -0.54f);
            StageFeatureSpawnHelper.Register(settings1, (settings, rng) =>
                StageFeatureSpawnHelper.SpawnPrinter(settings, "RoR2/Base/DuplicatorLarge/DuplicatorLarge.prefab", ItemTier.Tier2));
        }
    }
}
