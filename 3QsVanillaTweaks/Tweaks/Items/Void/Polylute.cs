using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Void
{
    internal static class Polylute
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Polylute", "ChainLightningVoid", true, "Void");
            c.Pair("Trigger Chance (%)", 25, 0, "Chance on hit, scaled by the triggering hit's proc coefficient. Vanilla: 25%.");
            c.Pair("Damage (%)", 60, 0, "Damage per strike relative to the triggering hit. Vanilla: 60%.");
            c.Pair("Strikes", 3, 3, "Lightning strikes per activation. Vanilla: 3 +3 per stack.");
            c.Pair("Proc Coefficient", .2f, 0, "Proc coefficient of each strike. Vanilla: 0.2.");
            c.N("Strike Interval", .1f, "Seconds between lightning strikes. Vanilla: 0.1.", .01f);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 25, () => c.Total("Trigger Chance (%)", c.Count(N.Source)), "ChainLightningVoid", 0, 80);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", .6f, () => c.Total("Damage (%)", c.Count(N.Source)) / 100, "ChainLightningVoid", 0, 100);
            c.Assign("RoR2.GlobalEventManager", "ProcessHitEnemy", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Source)), "ChainLightningVoid");
            c.Assign("RoR2.GlobalEventManager", "ProcessHitEnemy", "secondsPerStrike", () => c.F("Strike Interval"), "ChainLightningVoid");
            if (c.Enabled.Value) U.Patch(typeof(RoR2.Orbs.VoidLightningOrb), "Begin", typeof(Polylute), prefix: nameof(Begin));
            c.Text(() => c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance to fire <style=cIsDamage>lightning</style> for " + c.V("Damage (%)", "%") + " TOTAL damage, striking the target " + c.V("Strikes", "", "cIsUtility") + " times. <style=cIsVoid>Corrupts all Ukuleles</style>.", () => "Chance to repeatedly strike an enemy with lightning. <style=cIsVoid>Corrupts all Ukuleles</style>.");
        }
        static void Begin(RoR2.Orbs.VoidLightningOrb __instance)
        { int n = c.Count(__instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null); if (n > 0 && __instance.procChainMask.HasProc(ProcType.ChainLightning)) __instance.totalStrikes = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(c.Total("Strikes", n))); }
    }
}
