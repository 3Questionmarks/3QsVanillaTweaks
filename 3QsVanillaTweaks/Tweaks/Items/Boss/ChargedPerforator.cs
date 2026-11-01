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
    internal static class ChargedPerforator
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Charged Perforator", "LightningStrikeOnHit");
            c.Pair("Trigger Chance (%)", 10f, 0f, "Chance on hit, scaled by incoming proc coefficient. Vanilla: 10%.");
            c.Pair("Damage (%)", 500f, 500f, "TOTAL damage per strike. Vanilla: 500% +500% per stack.");
            c.Pair("Proc Coefficient", 0f, 0f, "Outgoing proc coefficient. Vanilla: 1.");
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 5, "Damage (%)", 100);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 10, () => c.Total("Trigger Chance (%)", c.Count(N.Body)), "LightningStrikeOnHit", 0, 55);
            c.Assign("RoR2.GlobalEventManager", "ProcessHitEnemy", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Body)), "LightningStrikeOnHit");
            c.Text(() => c.V("Trigger Chance (%)", "%") + " chance on hit to call down a lightning strike, dealing " + c.V("Damage (%)", "%") + " TOTAL damage.", () => "Chance on hit to call down a lightning strike.");
        }
    }
}
