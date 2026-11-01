using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class PredatoryInstincts
    {
        static UncommonConfig c;
        static BuffDef speed;
        internal static void Init()
        {
            c = new UncommonConfig("Predatory Instincts", "AttackSpeedOnCrit", false);
            c.Pair("Critical Chance (%)", 5, 0, "Critical chance. Vanilla: 5%, no stacking.");
            c.Pair("Attack Speed Per Buff (%)", 12, 0, "Attack speed per buff stack. Vanilla: 12%.");
            c.Pair("Maximum Buff Stacks", 3, 2, "Maximum buff stacks. Vanilla: 3 +2 per stack.");
            c.Pair("Duration", 3, 0, "Buff duration in seconds, scaled by the hit's proc coefficient. Vanilla: 3.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on a critical hit. Vanilla: 100%.");
            speed = U.Buff("Predatory Instincts", false, () => RoR2Content.Buffs.AttackSpeedOnCrit, () => "Each stack increases <style=cIsDamage>attack speed</style>.");
            c.Suppress("RoR2.GlobalEventManager", "OnCrit");
            c.Suppress("RoR2.CharacterBody", "RecalculateStats");
            U.Ensure();
            U.Hit += Hit;
            U.Stats += b =>
            {
                int n = c.Count(b);
                if (n > 0)
                {
                    U.Add(b, "crit", c.Total("Critical Chance (%)", n));
                    U.Scale(b, "attackSpeed", b.GetBuffCount(speed) * c.Total("Attack Speed Per Buff (%)", n));
                }
            };
            c.Text(() => "Gain " + c.V("Critical Chance (%)", "%") + " <style=cIsDamage>critical chance</style>. Critical strikes increase <style=cIsDamage>attack speed</style> by " + c.V("Attack Speed Per Buff (%)", "%") + " for " + c.V("Duration", "s", "cIsUtility") + ", up to " + c.V("Maximum Buff Stacks", "", "cIsUtility") + " times.", () => "Critical strikes increase <style=cIsDamage>attack speed</style>. Gain critical chance.");
        }

        static void Hit(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0 || !r.damageInfo.crit || r.damageInfo.procCoefficient <= 0 || !U.Roll(c.Total("Trigger Chance (%)", n), b))
                return;
            int old = b.GetBuffCount(speed), cap = Mathf.Max(1, Mathf.RoundToInt(c.Total("Maximum Buff Stacks", n)));
            if (old >= cap)
            {
                b.ClearTimedBuffs(speed);
                for (int i = 0; i < cap; i++)
                    b.AddTimedBuff(speed, c.Total("Duration", n) * r.damageInfo.procCoefficient);
            }
            else
                b.AddTimedBuff(speed, c.Total("Duration", n) * r.damageInfo.procCoefficient);
        }
    }
}
