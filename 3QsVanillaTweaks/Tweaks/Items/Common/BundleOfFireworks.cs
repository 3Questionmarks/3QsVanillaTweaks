using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class BundleOfFireworks
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> damage, extra, proc, chance, chanceStack, interval, spread, speed, radius, lifetime, procStack, cone;
        static ConfigEntry<BlastAttack.FalloffModel> falloff;
        static ConfigEntry<int> count, countStack;
        static GameObject projectile;
        sealed class Seen
        {
        }

        static ConditionalWeakTable<FireworkLauncher, Seen> configured = new ConditionalWeakTable<FireworkLauncher, Seen>();
        internal static void Init()
        {
            var s = S.Section("Bundle of Fireworks");
            enabled = S.Enable(s);
            proc = S.Num(s, "Proc Coefficient", 0, "Proc coefficient per firework. Default 0 prevents proc-chain lag.");
            procStack = S.Num(s, "Proc Coefficient Per Stack", 0, "Extra proc coefficient per additional stack.");
            cone = S.Num(s, "Launch Cone Angle", 10, "Launch cone angle in degrees, where supported by the native launcher. Vanilla field: 10.");
            count = S.Int(s, "Fireworks Launched", 8, "Fireworks from one stack. Vanilla: 8.");
            countStack = S.Int(s, "Extra Fireworks Per Stack", 0, "Extra fireworks per additional stack. Vanilla: 4.");
            damage = S.Num(s, "Damage (%)", 400, "First-stack damage per firework. Vanilla: 300%.");
            extra = S.Num(s, "Extra Damage Per Stack (%)", 200, "Extra damage per additional stack. Vanilla: 0%.");
            chance = S.Num(s, "Trigger Chance (%)", 100, "Chance per eligible interaction. Vanilla: 100%.", 0, 100);
            chanceStack = S.Num(s, "Trigger Chance Per Stack (%)", 0, "Extra chance per additional stack.", 0, 100);
            interval = S.Num(s, "Launch Interval", .1f, "Seconds between fireworks. Vanilla: 0.1.", .02f);
            spread = S.Num(s, "Launch Spread Radius", 0, "Random horizontal launch-position radius. Vanilla prefab value is normally 0.");
            if (!enabled.Value)
                return;
            var h = S.Harmony("Fireworks");
            S.Patch(h, typeof(FireworkLauncher), "FixedUpdate", typeof(BundleOfFireworks), prefix: nameof(BeforeLauncher));
            S.Patch(h, typeof(ProjectileController), "Start", typeof(BundleOfFireworks), prefix: nameof(BeforeProjectile));
            RoR2Application.onLoad += () =>
            {
                var p = LegacyResourcesAPI.Load<GameObject>("Prefabs/FireworkLauncher");
                projectile = p.GetComponent<FireworkLauncher>().projectilePrefab;
                var simple = projectile.GetComponent<ProjectileSimple>();
                var blast = projectile.GetComponent<ProjectileExplosion>();
                speed = S.Num(s, "Projectile Speed", simple ? simple.desiredForwardSpeed : 0, "Projectile speed in metres per second; default from vanilla prefab.");
                radius = S.Num(s, "Explosion Radius", blast ? blast.blastRadius : 0, "Explosion radius in metres; default from vanilla prefab.");
                falloff = C.Bind(s, "Explosion Falloff", blast ? blast.falloffModel : BlastAttack.FalloffModel.None, "Damage falloff; default from vanilla prefab.");
                lifetime = S.Num(s, "Projectile Lifetime", simple ? simple.lifetime : 8, "Maximum projectile lifetime in seconds, where supported by the projectile.", .02f);
            };
            S.Text("ITEM_FIREWORK_DESC", () => "Activating an interactable launches <style=cIsDamage>" + C.Get(count) + "</style> <style=cStack>(+" + C.Get(countStack) + " per stack)</style> <style=cIsDamage>fireworks</style> for <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> damage each.");
        }

        static void BeforeLauncher(FireworkLauncher __instance)
        {
            if (!S.Active(enabled) || configured.TryGetValue(__instance, out _))
                return;
            configured.Add(__instance, new Seen());
            var b = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            int n = S.Count(b, RoR2Content.Items.Firework);
            if (n <= 0)
                return;
            __instance.remaining = Util.CheckRoll(S.Total(chance, chanceStack, n), b.master) ? Mathf.RoundToInt(S.Linear(n, C.Get(count), C.Get(countStack))) : 0;
            __instance.damageCoefficient = S.Total(damage, extra, n) / 100;
            __instance.launchInterval = C.Get(interval);
            __instance.coneAngle = C.Get(cone);
            __instance.randomCircleRange = C.Get(spread);
        }

        static void BeforeProjectile(ProjectileController __instance)
        {
            if (!S.Active(enabled) || !projectile || __instance.catalogIndex != projectile.GetComponent<ProjectileController>().catalogIndex)
                return;
            int n = S.Count(__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null, RoR2Content.Items.Firework);
            __instance.procCoefficient = S.Total(proc, procStack, Math.Max(1, n));
            var simple = __instance.GetComponent<ProjectileSimple>();
            if (simple && speed != null)
            {
                simple.desiredForwardSpeed = C.Get(speed);
                if (lifetime != null)
                    simple.lifetime = C.Get(lifetime);
            }

            foreach (var blast in __instance.GetComponentsInChildren<ProjectileExplosion>(true))
            {
                blast.blastProcCoefficient = 1;
                if (radius != null)
                    blast.blastRadius = C.Get(radius);
                if (falloff != null)
                    blast.falloffModel = C.Get(falloff);
            }
        }
    }
}
