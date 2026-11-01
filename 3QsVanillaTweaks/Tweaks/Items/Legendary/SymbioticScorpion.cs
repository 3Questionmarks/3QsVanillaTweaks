using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class SymbioticScorpion
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Symbiotic Scorpion", "PermanentDebuffOnHit");
            c.B("Rework", true, "Replace permanent armour reduction with poison on skill hits and increased damage over time against wounded enemies.");
            c.Pair("Poison Chance (%)", 100, 0, "Chance to poison on a damaging skill hit, scaled by proc coefficient.");
            c.Pair("Poison Duration", 10, 0, "Poison duration in seconds. Native poison: 10.");
            c.Pair("Poison Damage Multiplier", 1, 0, "Multiplier to native poison damage. Native poison deals 1% maximum health per second, with a minimum based on attacker damage.");
            c.Pair("Damage Over Time Bonus Per Interval (%)", 1, 1, "Bonus damage over time for each missing-health interval. Scales with item count.");
            c.Pair("Target Health Interval (%)", 1, 0, "Missing maximum combined health percentage for each damage bonus interval.", .001f);
            c.N("Armour Reduction Per Applied Stack", 2, "Armour removed by each permanent debuff stack when rework is disabled. Each hit can apply one debuff stack per item. Vanilla: 2.");
            c.Suppress("RoR2.HealthComponent", "TakeDamageProcess", () => c.B("Rework"));
            if (c.Enabled.Value)
            {
                U.Ensure();
                U.Hit += Hit;
                U.BeforeDamage += Damage;
                U.Stats += b =>
                {
                    if (!c.Active || c.B("Rework"))
                        return;
                    int stacks = b.GetBuffCount(DLC1Content.Buffs.PermanentDebuff);
                    if (stacks > 0)
                        U.Add(b, "armor", stacks * (2 - c.F("Armour Reduction Per Applied Stack")));
                };
            }

            c.ShowStat = k => c.B("Rework") ? !k.StartsWith("Armour Reduction") : k.StartsWith("Armour Reduction");
            c.Text(() => c.B("Rework") ? "Skill hits " + "<style=cIsDamage>poison</style> enemies for " + c.V("Poison Duration", "s", "cIsUtility") + ". Deal " + c.V("Damage Over Time Bonus Per Interval (%)", "%") + " more damage over time for every " + c.V("Target Health Interval (%)", "%", "cIsHealth") + " health the target is missing." : "Hits permanently reduce armour by " + c.V("Armour Reduction Per Applied Stack", "", "cIsDamage") + ".", () => c.B("Rework") ? "Skill hits poison enemies. Damage over time grows stronger against wounded enemies." : "Hits permanently reduce enemy armour.");
        }

        static void Hit(DamageReport r)
        {
            int n = c.Count(r.attackerBody);
            if (n == 0 || !c.B("Rework") || !r.victim || !r.victim.alive || r.damageInfo.procCoefficient <= 0 || !r.damageInfo.damageType.IsDamageSourceSkillBased)
                return;
            if (U.Roll(c.Total("Poison Chance (%)", n) * r.damageInfo.procCoefficient, r.attackerBody))
            {
                var info = new InflictDotInfo
                {
                    victimObject = r.victim.gameObject,
                    attackerObject = r.attackerBody.gameObject,
                    dotIndex = DotController.DotIndex.Poison,
                    duration = c.Total("Poison Duration", n),
                    damageMultiplier = c.Total("Poison Damage Multiplier", n)
                };
                DotController.InflictDot(ref info);
            }
        }

        static void Damage(HealthComponent target, DamageInfo info)
        {
            var b = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(b);
            if (n == 0 || !c.B("Rework") || info.dotIndex == DotController.DotIndex.None)
                return;
            float missing = 100f * (1 - Mathf.Clamp01(target.combinedHealth / Mathf.Max(1, target.fullCombinedHealth)));
            float intervals = Mathf.Floor(missing / Mathf.Max(.001f, c.Total("Target Health Interval (%)", n)));
            info.damage *= 1 + intervals * c.Total("Damage Over Time Bonus Per Interval (%)", n) / 100f;
        }
    }
}
