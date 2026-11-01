using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class MoltenPerforator
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Molten Perforator", "FireballsOnHit");
            c.Pair("Trigger Chance (%)", 10f, 0f, "Chance on hit, scaled by incoming proc coefficient. Vanilla: 10%.");
            c.Pair("Damage (%)", 300f, 300f, "TOTAL damage per fireball. Vanilla: 300% +300% per stack.");
            c.Pair("Fireballs", 3f, 0f, "Fireballs per activation. Vanilla: 3.");
            c.Pair("Proc Coefficient", 0f, 0f, "Outgoing proc coefficient. Vanilla: 1.");
            BossItemSupport.Molten();
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 3, "Damage (%)", 100);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 10, () => c.Total("Trigger Chance (%)", c.Count(N.Body)), "FireballsOnHit", 0, 130);
            Tweaks.Items.Uncommon.UncommonProjectiles.Register(c, "Prefabs/Projectiles/FireMeatBall");
            c.Text(() => c.V("Trigger Chance (%)", "%") + " chance on hit to launch " + c.V("Fireballs", "", "cIsUtility") + " magma balls, each dealing " + c.V("Damage (%)", "%") + " TOTAL damage.", () => "Chance on hit to launch magma balls.");
        }
    }
}
