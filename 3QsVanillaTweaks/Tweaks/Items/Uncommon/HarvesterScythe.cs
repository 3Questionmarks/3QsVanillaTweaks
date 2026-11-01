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
    internal static class HarvesterScythe
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Harvester's Scythe", "HealOnCrit", true);
            c.Pair("Healing", 4, 2, "Flat healing on a critical hit. Vanilla: 8 +4 per stack.");
            c.Pair("Damage Healed (%)", 5, 2, "Heal this percentage of damage dealt on critical hits. Vanilla: 0%.");
            c.Pair("Critical Chance (%)", 5, 0, "Critical chance. Vanilla: 5% at any stack count.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on each critical hit. Vanilla: 100%.");
            c.B("Scale Healing With Proc Coefficient", true, "Scale healing with the triggering hit's proc coefficient. Vanilla: true.");
            c.Suppress("RoR2.GlobalEventManager", "OnCrit");
            c.Suppress("RoR2.CharacterBody", "RecalculateStats");
            U.Ensure();
            U.Hit += Hit;
            U.Stats += b => U.Add(b, "crit", c.Total("Critical Chance (%)", c.Count(b)));
            c.Text(() => "Gain " + c.V("Critical Chance (%)", "%") + " <style=cIsDamage>critical chance</style>. Critical hits heal for " + c.V("Healing", "", "cIsHealing") + " plus " + c.V("Damage Healed (%)", "%", "cIsHealing") + " of damage dealt.", () => "Critical strikes <style=cIsHealing>heal</style> you. Gain critical chance.");
        }

        static void Hit(DamageReport r)
        {
            int n = c.Count(r.attackerBody);
            if (n <= 0 || !r.damageInfo.crit || r.damageInfo.procCoefficient <= 0 || r.damageInfo.procChainMask.HasProc(ProcType.HealOnCrit) || !U.Roll(c.Total("Trigger Chance (%)", n), r.attackerBody))
                return;
            var mask = r.damageInfo.procChainMask;
            mask.AddProc(ProcType.HealOnCrit);
            float amount = c.Total("Healing", n) + r.damageDealt * c.Total("Damage Healed (%)", n) / 100;
            r.attackerBody.healthComponent.Heal(amount * (c.B("Scale Healing With Proc Coefficient") ? r.damageInfo.procCoefficient : 1), mask);
        }
    }
}
