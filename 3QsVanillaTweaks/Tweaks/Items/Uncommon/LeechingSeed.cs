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
    internal static class LeechingSeed
    {
        static UncommonConfig c;
        [ThreadStatic]
        static bool healing;
        internal static void Init()
        {
            c = new UncommonConfig("Leeching Seed", "Seed", true);
            c.B("Rework", true, "Heal when dealing damage over time instead of on ordinary hits.");
            c.Pair("Flat Healing", 1, 1, "Flat healing per qualifying damage event. Vanilla: 1 per stack on hit.");
            c.Pair("Damage Healed (%)", 1, 1, "Heal this percentage of damage dealt. Vanilla: 0%.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance per qualifying damage event. Vanilla: 100%.");
            c.B("Scale Healing With Proc Coefficient", false, "Multiply healing by the hit's proc coefficient. Vanilla: true. Damage-over-time hits normally have zero proc coefficient.");
            c.Suppress("RoR2.GlobalEventManager", "ProcessHitEnemy");
            U.Ensure();
            U.Hit += Hit;
            c.Text(() => "Dealing " + (c.B("Rework") ? "<style=cIsDamage>damage over time</style>" : "damage on hit") + " heals for " + c.V("Flat Healing", "", "cIsHealing") + " plus " + c.V("Damage Healed (%)", "%", "cIsHealing") + " of damage dealt.", () => c.B("Rework") ? "Dealing <style=cIsDamage>damage over time</style> <style=cIsHealing>heals</style> you." : "Dealing damage <style=cIsHealing>heals</style> you.");
        }

        static void Hit(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            bool dot = (r.damageInfo.damageType & DamageType.DoT) != 0;
            if (healing || n == 0 || c.B("Rework") != dot || (!dot && r.damageInfo.procCoefficient <= 0) || r.damageInfo.procChainMask.HasProc(ProcType.HealOnHit) || !U.Roll(c.Total("Trigger Chance (%)", n), b))
                return;
            var mask = r.damageInfo.procChainMask;
            mask.AddProc(ProcType.HealOnHit);
            try
            {
                healing = true;
                b.healthComponent.Heal((c.Total("Flat Healing", n) + r.damageDealt * c.Total("Damage Healed (%)", n) / 100) * (c.B("Scale Healing With Proc Coefficient") ? r.damageInfo.procCoefficient : 1), mask);
            }
            finally
            {
                healing = false;
            }
        }
    }
}
