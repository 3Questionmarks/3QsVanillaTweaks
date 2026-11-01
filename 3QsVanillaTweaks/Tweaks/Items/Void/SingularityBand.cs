using RoR2;
using RoR2.Projectile;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Void
{
    internal static class SingularityBand
    {
        static R c;
        internal static void Init()
        {
            U.Ensure(); c = new R("Singularity Band", "ElementalRingVoid", true, "Void");
            c.Pair("Damage (%)", 300, 200, "Black-hole explosion TOTAL damage. Vanilla: 100% +100% per stack.");
            c.Pair("Cooldown", 15, 0, "Seconds between activations. Vanilla: 20 seconds. Fractional values are rounded down by the native timed-buff counter.", 1);
            c.Pair("Proc Coefficient", 1, 0, "Explosion proc coefficient. Vanilla: 1.");
            c.Pair("Radius", 15, 0, "Explosion radius in metres. Vanilla: 15.");
            c.N("Explosion Delay", 5, "Seconds before the black hole explodes. Vanilla: 5.", .01f);
            c.N("Trigger Damage Threshold (%)", 400, "Minimum triggering hit damage relative to base damage. Vanilla: 400%.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 1, "Damage (%)", 100);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 20, () => c.Total("Cooldown", c.Count(N.Source)), "ElementalRingVoidReady", 0, 80);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 4, () => c.Count(N.Source) > 0 && !N.Source.HasBuff(RoR2Content.Buffs.ElementalRingsReady) ? c.F("Trigger Damage Threshold (%)") / 100 : 4, "StickyBomb", 0, 250);
            if (c.Enabled.Value) U.Patch(typeof(ProjectileController), "Start", typeof(SingularityBand), prefix: nameof(Start));
            c.Text(() => "Hits that deal more than " + c.V("Trigger Damage Threshold (%)", "%") + " damage create a <style=cIsUtility>black hole</style> that draws enemies within " + c.V("Radius", "m", "cIsUtility") + " into its center. Lasts <style=cIsUtility>" + c.F("Explosion Delay").ToString("0.##") + " seconds</style> before collapsing, dealing " + c.V("Damage (%)", "%") + " TOTAL damage. Recharges every " + c.V("Cooldown", " seconds", "cIsUtility") + ". <style=cIsVoid>Corrupts all Runald's and Kjaro's Bands</style>.", () => "High damage hits create an exploding black hole. Recharges over time. <style=cIsVoid>Corrupts all Runald's and Kjaro's Bands</style>.");
        }
        static void Start(ProjectileController __instance)
        {
            if (!__instance.name.StartsWith("ElementalRingVoid")) return;
            int n = c.Count(__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null); if (n == 0) return;
            __instance.procCoefficient = 1;
            var explosion = __instance.GetComponent<ProjectileExplosion>();
            if (explosion) { explosion.blastRadius = c.Total("Radius", n); explosion.blastProcCoefficient = c.Total("Proc Coefficient", n); }
            foreach (var component in __instance.GetComponents<UnityEngine.Component>())
            {
                if (__instance.name.StartsWith("ElementalRingVoidBlackHole") && component.GetType().Name == "ProjectileFuse") A.Field(component.GetType(), "fuse")?.SetValue(component, c.F("Explosion Delay"));
                if (__instance.name.StartsWith("ElementalRingVoidBlackHole") && component.GetType().Name == "ProjectileImpactExplosion") A.Field(component.GetType(), "lifetime")?.SetValue(component, c.F("Explosion Delay"));
                if (component.GetType().Name == "RadialForce") A.Field(component.GetType(), "radius")?.SetValue(component, c.Total("Radius", n));
            }
        }
    }
}
