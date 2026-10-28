namespace Tweaks.Stages.Stage1
{
    internal static class ShatteredAbodes
    {
        internal static void Init()
        {
            var settings = AurelioniteFragmentSpawnHelper.Bind("Stages - Stage 1 - Shattered Abodes - Aurelionite Fragment",
                "village", "Shattered Abodes Aurelionite Fragment", -1.43f, -24.89f, -93.86f);
            AurelioniteFragmentSpawnHelper.Register(settings);
        }
    }
}
