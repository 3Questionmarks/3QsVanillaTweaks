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
    internal static class RunaldsBand
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Runald's Band", "IceRing", true);
            c.Pair("Damage (%)", 200, 200, "Band total damage. Vanilla: 250% per stack.");
            c.Pair("Proc Coefficient", 1f, 0, "Outgoing proc coefficient. Vanilla: 1.");
            c.N("Trigger Damage (%)", 400, "Minimum damage as a percentage of base damage. Vanilla: 400%.");
            c.N("Cooldown", 10, "Seconds to recharge. Vanilla: 10.");
            c.Pair("Slow Duration", 3, 3, "Slow duration in seconds. Vanilla: 3 per stack.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 2.5f, "Damage (%)", 100);
            BandSupport.Register(c);
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 3, "Slow Duration");
            c.Text(() => "Hits dealing more than " + c.V("Trigger Damage (%)", "%") + " damage trigger a <style=cIsDamage>blast of ice</style> for " + c.V("Damage (%)", "%") + " TOTAL damage. Recharges in " + c.V("Cooldown", "s", "cIsUtility") + ".", () => "High-damage hits trigger a <style=cIsDamage>blast of ice</style>.");
        }
    }
}
