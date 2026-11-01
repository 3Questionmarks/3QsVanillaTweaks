using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Void
{
    internal static class PlasmaShrimp
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Plasma Shrimp", "MissileVoid", true, "Void");
            c.Pair("Damage (%)", 50, 25, "Missile total damage. Vanilla: 40% +40% per stack.");
            c.Pair("Proc Coefficient", 0, 0, "Missile proc coefficient. Vanilla: 0.2.");
            c.Pair("Shield (%)", 10, 0, "Maximum health gained as shield. Vanilla: 10%, no stacking.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", .4f, "Damage (%)", 100);
            c.Assign("RoR2.GlobalEventManager", "ProcessHitEnemy", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Source)), "MissileVoid");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .1f, () => c.Total("Shield (%)", c.Count(N.Body)) / 100, "EngiTeamShield", 0, 80);
            c.Text(() => "Gain a <style=cIsHealing>shield</style> equal to " + c.V("Shield (%)", "%", "cIsHealing") + " of your maximum health. While you have a shield, hitting an enemy fires a <style=cIsDamage>missile</style> that deals " + c.V("Damage (%)", "%") + " TOTAL damage. <style=cIsVoid>Corrupts all AtG Missile Mk. 1s</style>.", () => "While shielded, fire missiles on hit. <style=cIsVoid>Corrupts all AtG Missile Mk. 1s</style>.");
        }
    }
}
