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
    internal static class Chronobauble
    {
        static UncommonConfig c;
        static BuffDef slow;
        internal static void Init()
        {
            c = new UncommonConfig("Chronobauble", "SlowOnHit", true);
            c.Pair("Movement Speed Reduction (%)", 60, 0, "Movement speed reduction. Vanilla: 60%.");
            c.Pair("Attack Speed Reduction (%)", 20, 0, "Attack speed reduction. Vanilla: 0%.");
            c.Pair("Duration", 2, 2, "Seconds of slow. Vanilla: 2 +2 per stack.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on hit. Vanilla: 100%.");
            c.Suppress("RoR2.GlobalEventManager", "ProcessHitEnemy");
            slow = U.Buff("Chronobauble Slow", true, () => RoR2Content.Buffs.Slow60, () => "Reduced <style=cIsUtility>movement and attack speed</style>.");
            U.Ensure();
            U.Hit += Hit;
            U.Stats += Stats;
            c.Text(() => "Hits <style=cIsUtility>slow</style> enemies by " + c.V("Movement Speed Reduction (%)", "%", "cIsUtility") + " and reduce their attack speed by " + c.V("Attack Speed Reduction (%)", "%", "cIsUtility") + " for " + c.V("Duration", "s", "cIsUtility") + ".", () => "Hits <style=cIsUtility>slow movement and attack speed</style>.");
        }

        static void Hit(DamageReport r)
        {
            int n = c.Count(r.attackerBody);
            if (n == 0 || !r.victimBody || r.damageInfo.procCoefficient <= 0 || !U.Roll(c.Total("Trigger Chance (%)", n), r.attackerBody))
                return;
            r.victimBody.ClearTimedBuffs(slow);
            for (int i = 0; i < n; i++)
                r.victimBody.AddTimedBuff(slow, c.Total("Duration", n));
        }

        static void Stats(CharacterBody b)
        {
            int n = b.GetBuffCount(slow);
            if (n == 0 || !c.Active)
                return;
            U.Scale(b, "moveSpeed", -Mathf.Clamp(c.Total("Movement Speed Reduction (%)", n), 0, 100));
            U.Scale(b, "attackSpeed", -Mathf.Clamp(c.Total("Attack Speed Reduction (%)", n), 0, 100));
        }
    }
}
