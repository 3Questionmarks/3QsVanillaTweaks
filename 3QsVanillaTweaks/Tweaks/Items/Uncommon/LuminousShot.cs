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
    internal static class LuminousShot
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Luminous Shot", "IncreasePrimaryDamage", true);
            c.Pair("Damage (%)", 200, 100, "Total damage per charged explosion. Vanilla: 175 +50 per stack.");
            c.Pair("Proc Coefficient", 1, 0, "Explosion proc coefficient. Vanilla: 1.");
            c.N("Charges Required", 3, "Secondary uses required before a primary hit releases the stored explosions. Vanilla: 3.", 1);
            c.Pair("Radius", 7, 0, "Explosion radius in metres. Vanilla: 7.");
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 1.75f, () => c.F("Damage (%)") / 100, "IncreasePrimaryDamage", 0, 60);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", .5f, () => c.F("Damage (%) Per Stack") / 100, "IncreasePrimaryDamage", 0, 60);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 7, () => c.Total("Radius", c.Count(UncommonNative.Body)), "IncreasePrimaryDamage", 0, 200);
            c.Assign("RoR2.GlobalEventManager", "ProcessHitEnemy", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(UncommonNative.Body)), "IncreasePrimaryDamage");
            LuminousSupport.Register(c);
            c.Text(() => "Using your secondary skill charges your next primary attack. At " + c.V("Charges Required", "", "cIsUtility") + " charges, release explosions dealing " + c.V("Damage (%)", "%") + " TOTAL damage each.", () => "Secondary skills charge <style=cIsDamage>explosions</style> for your next primary attack.");
        }
    }
}
