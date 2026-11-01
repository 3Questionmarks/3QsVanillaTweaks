using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Uncommon
{
    internal static class WillOTheWisp
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Will-o'-the-wisp", "ExplodeOnDeath", true);
            c.Pair("Damage (%)", 350, 280, "Explosion base damage. Vanilla: 350% +280% per stack.");
            c.Pair("Radius", 12, 2.4f, "Explosion radius in metres. Vanilla: 12 +2.4 per stack.");
            c.Pair("Proc Coefficient", 0, 0, "Explosion proc coefficient. Vanilla: 1.");
            c.N("Explosion Delay", .5f, "Seconds before the explosion. Vanilla: 0.5.");
            // Keep the native on-kill trigger and its explosion, rather than replacing it.
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 3.5f, () => { int n = c.Count(N.Source); return c.Total("Damage (%)", n) / (100 * (1 + .8f * System.Math.Max(0,n-1))); }, "ExplodeOnDeath", 0, 100);
            c.Assign("RoR2.GlobalEventManager", "OnCharacterDeath", "radius", () => c.Total("Radius", c.Count(N.Source)), "ExplodeOnDeath");
            c.Assign("RoR2.GlobalEventManager", "OnCharacterDeath", "maxTimer", () => c.F("Explosion Delay"), "ExplodeOnDeath");
            if (c.Enabled.Value) U.Patch(typeof(DelayBlast), "FixedUpdate", typeof(WillOTheWisp), prefix: nameof(Blast));
            c.Text(() => "On killing an enemy, spawn a <style=cIsDamage>lava pillar</style> in a " + c.V("Radius", "m", "cIsUtility") + " radius for " + c.V("Damage (%)", "%") + " base damage.", () => "Detonate enemies on kill.");
        }
        static void Blast(DelayBlast __instance)
        {
            if (!__instance.name.StartsWith("WilloWispDelay")) return;
            int n = c.Count(__instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null);
            if (n > 0) __instance.procCoefficient = c.Total("Proc Coefficient", n);
        }
    }
}
