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
    internal static class KjarosBand
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Kjaro's Band", "FireRing", true);
            c.Pair("Damage (%)", 250, 250, "Band total damage. Vanilla: 300% per stack.");
            c.Pair("Proc Coefficient", 0.2f, 0, "Outgoing proc coefficient. Vanilla: 0.2.");
            c.N("Trigger Damage (%)", 400, "Minimum damage as a percentage of base damage. Vanilla: 400%.");
            c.N("Cooldown", 10, "Seconds to recharge. Vanilla: 10.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 3f, "Damage (%)", 100);
            BandSupport.Register(c);
            UncommonProjectiles.Register(c, "Prefabs/Projectiles/FireTornado");
            c.Text(() => "Hits dealing more than " + c.V("Trigger Damage (%)", "%") + " damage trigger a <style=cIsDamage>burning tornado</style> for " + c.V("Damage (%)", "%") + " TOTAL damage. Recharges in " + c.V("Cooldown", "s", "cIsUtility") + ".", () => "High-damage hits trigger a <style=cIsDamage>burning tornado</style>.");
        }
    }
}
