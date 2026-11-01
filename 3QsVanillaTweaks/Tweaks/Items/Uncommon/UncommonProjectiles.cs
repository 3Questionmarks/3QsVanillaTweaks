using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using C = Config.ConfigManager;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Uncommon
{
    internal static class UncommonProjectiles
    {
        sealed class Rule
        {
            internal UncommonConfig Config;
            internal GameObject Prefab;
            internal string CountItem;
            internal float Proc, Speed, Lifetime, Radius;
            internal bool Explosive;
        }

        static readonly List<Rule> rules = new List<Rule>();
        static bool installed;
        internal static void Register(UncommonConfig config, string path) => RoR2Application.onLoad += () => RegisterLoaded(config, LegacyResourcesAPI.Load<GameObject>(path));
        internal static void RegisterLoaded(UncommonConfig config, GameObject prefab, string countItem = null)
        {
            if (!config.Enabled.Value || !prefab)
                return;
            var controller = prefab.GetComponent<ProjectileController>();
            var simple = prefab.GetComponent<ProjectileSimple>();
            var explosion = prefab.GetComponent<ProjectileImpactExplosion>();
            var rule = new Rule
            {
                Config = config,
                Prefab = prefab,
                CountItem = countItem ?? config.ItemName,
                Proc = controller ? controller.procCoefficient : 0,
                Speed = simple ? simple.desiredForwardSpeed : 0,
                Lifetime = simple ? simple.lifetime : 0,
                Radius = explosion ? explosion.blastRadius : 0,
                Explosive = explosion
            };
            rules.Add(rule);
            if (simple)
            {
                config.N("Projectile Speed", simple.desiredForwardSpeed, "Projectile speed in metres per second. Vanilla: " + simple.desiredForwardSpeed + ".");
                config.N("Projectile Lifetime", simple.lifetime, "Projectile lifetime in seconds. Vanilla: " + simple.lifetime + ".");
            }

            if (explosion)
            {
                config.N("Explosion Radius", explosion.blastRadius, "Explosion radius in metres. Vanilla: " + explosion.blastRadius + ".");
                config.N("Explosion Proc Coefficient", explosion.blastProcCoefficient, "Explosion proc multiplier. Vanilla: " + explosion.blastProcCoefficient + ".");
            }

            if (!installed)
            {
                installed = true;
                U.Patch(typeof(ProjectileController), "Start", typeof(UncommonProjectiles), postfix: nameof(Start));
            }
        }

        static void Start(ProjectileController __instance)
        {
            var projectile = __instance;
            var owner = projectile.owner ? projectile.owner.GetComponent<CharacterBody>() : null;
            if (!owner || !owner.inventory)
                return;
            foreach (var rule in rules)
            {
                var c = rule.Config;
                if (!c.Active || !projectile.name.StartsWith(rule.Prefab.name, StringComparison.Ordinal))
                    continue;
                int n = owner.inventory.GetItemCountEffective(ItemCatalog.FindItemIndex(rule.CountItem));
                if (n <= 0)
                    continue;
                if (c.Numbers.ContainsKey("Proc Coefficient"))
                    projectile.procCoefficient = c.Total("Proc Coefficient", n);
                var simple = projectile.GetComponent<ProjectileSimple>();
                if (simple)
                {
                    simple.desiredForwardSpeed = c.F("Projectile Speed");
                    simple.lifetime = c.F("Projectile Lifetime");
                }

                var explosion = projectile.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    explosion.blastRadius = c.F("Explosion Radius");
                    explosion.blastProcCoefficient = c.F("Explosion Proc Coefficient");
                }
            }
        }
    }
}
