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
    internal static class ShatteringJustice
    {
        static R c;
        static BuffDef deep;
        sealed class Progress
        {
            internal float Hits;
        }

        static readonly ConditionalWeakTable<CharacterBody, Progress> progress = new ConditionalWeakTable<CharacterBody, Progress>();
        internal static void Init()
        {
            c = RareItemSupport.Config("Shattering Justice", "ArmorReductionOnHit");
            c.Pair("Hits Required", 5, 0, "Hits required to pulverize. Vanilla: 5.");
            c.Pair("Armour Reduction", 60, 0, "Initial armour reduction. Vanilla: 60.");
            c.Pair("Duration", 8, 0, "Pulverize duration in seconds. Vanilla: 8 per stack.");
            c.B("Hits Deepen Armour Reduction", true, "Hits on pulverized targets increase armour reduction.");
            c.Pair("Additional Armour Reduction Per Hit", 2, 2, "Extra armour removed by each hit on an already pulverized target. Vanilla: 0.");
            c.B("Reset Duration On Hit", true, "Refresh pulverize duration when hitting a pulverized target. Vanilla: false.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance for a qualifying hit to contribute to pulverize. Vanilla: 100%.");
            c.Suppress("RoR2.HealthComponent", "TakeDamageProcess");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", 60, () => c.F("Armour Reduction"), "Pulverized", 0, 35);
            if (c.Enabled.Value)
            {
                deep = U.Buff("Deep Pulverize", true, () => RoR2Content.Buffs.Pulverized, () => "Additional armour removed by Shattering Justice.");
                U.Ensure();
                U.Hit += Hit;
                U.Stats += b =>
                {
                    if (c.Active && deep && b.HasBuff(RoR2Content.Buffs.Pulverized))
                        U.Add(b, "armor", -b.GetBuffCount(deep));
                };
                U.Tick += b =>
                {
                    if (deep && !b.HasBuff(RoR2Content.Buffs.Pulverized) && b.HasBuff(deep))
                        b.SetBuffCount(deep.buffIndex, 0);
                };
            }

            c.Text(() => "Hit an enemy " + c.V("Hits Required", "", "cIsUtility") + " times to reduce its armour by " + c.V("Armour Reduction", "", "cIsDamage") + " for " + c.V("Duration", "s", "cIsUtility") + "." + (c.B("Hits Deepen Armour Reduction") ? " Further hits remove " + c.V("Additional Armour Reduction Per Hit", "", "cIsDamage") + " additional armour." : ""), () => "Repeated hits reduce enemy armour.");
        }

        static void Hit(DamageReport r)
        {
            int n = c.Count(r.attackerBody);
            var b = r.victimBody;
            if (n == 0 || !b || !b.healthComponent.alive || r.damageInfo.procCoefficient <= 0 || !U.Roll(c.Total("Trigger Chance (%)", n), r.attackerBody))
                return;
            bool marked = b.HasBuff(RoR2Content.Buffs.Pulverized);
            if (marked)
            {
                if (c.B("Hits Deepen Armour Reduction"))
                    b.SetBuffCount(deep.buffIndex, b.GetBuffCount(deep) + Mathf.RoundToInt(c.Total("Additional Armour Reduction Per Hit", n)));
                if (!c.B("Reset Duration On Hit"))
                    return;
            }
            else
            {
                var s = progress.GetOrCreateValue(b);
                s.Hits++;
                if (s.Hits < c.Total("Hits Required", n))
                    return;
                s.Hits = 0;
                b.SetBuffCount(deep.buffIndex, Mathf.RoundToInt(c.Total("Armour Reduction", n) - c.F("Armour Reduction")));
            }

            b.ClearTimedBuffs(RoR2Content.Buffs.Pulverized);
            b.AddTimedBuff(RoR2Content.Buffs.Pulverized, c.Total("Duration", n));
        }
    }
}
