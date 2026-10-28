namespace Tweaks.Stages.Stage1
{
    internal static class DisturbedImpact
    {
        internal static void Init()
        {
            var settings = AurelioniteFragmentSpawnHelper.Bind("Stages - Stage 1 - Disturbed Impact - Aurelionite Fragment",
                "villagenight", "Disturbed Impact Aurelionite Fragment", -1.43f, -24.89f, -93.86f);
            AurelioniteFragmentSpawnHelper.Register(settings);
        }
    }
}
