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
    internal static class AtGMissile
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("AtG Missile Mk. 1", "Missile", true);
            c.Pair("Damage (%)", 300, 300, "Missile total damage. Vanilla: 300% per stack.");
            c.Pair("Trigger Chance (%)", 10, 0, "Chance per hit, scaled by the triggering hit's proc coefficient. Vanilla: 10%.");
            c.Pair("Proc Coefficient", 0, 0, "Missile proc coefficient. Vanilla: 1.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 3, "Damage (%)", 100);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 10, () => c.Total("Trigger Chance (%)", c.Count(UncommonNative.Body)), "Missile", 0, 35);
            UncommonProjectiles.Register(c, "Prefabs/Projectiles/MissileProjectile");
            c.Text(() => c.V("Trigger Chance (%)", "%") + " chance on hit to fire a missile that deals " + c.V("Damage (%)", "%") + " TOTAL damage.", () => "Chance on hit to fire a <style=cIsDamage>missile</style>.");
        }
    }
}
