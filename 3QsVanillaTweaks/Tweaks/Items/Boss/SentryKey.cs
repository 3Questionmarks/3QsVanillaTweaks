using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Boss
{
    internal static class SentryKey
    {
        internal static void Init()
        {
            var c = BossItemSupport.Config("Sentry Key", "PowerPyramid", false);
            c.N("Movement Speed Bonus (%)", 15, "Movement speed granted by each active Sentry Key buff. Vanilla: 15%.");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .15f, () => c.F("Movement Speed Bonus (%)") / 100f, "PowerPyramidBuff");
            c.Text(() => "Increase <style=cIsUtility>movement speed</style> by " + c.V("Movement Speed Bonus (%)", "%", "cIsUtility") + ".", () => "Increase movement speed.");
        }
    }
}
