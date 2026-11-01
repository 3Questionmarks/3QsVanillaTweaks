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
    internal static class Ukulele
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Ukulele", "ChainLightning", false);
            c.Pair("Damage (%)", 80, 0, "Lightning total damage. Vanilla: 80%.");
            c.Pair("Trigger Chance (%)", 25, 0, "Chance on hit. Vanilla: 25%.");
            c.Pair("Targets", 3, 2, "Maximum lightning targets. Vanilla: 3 +2 per stack.");
            c.Pair("Range", 20, 2, "Bounce range in metres. Vanilla: 20 +2 per stack.");
            c.Pair("Proc Coefficient", .2f, 0, "Lightning proc coefficient. Vanilla: 0.2.");
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 25, () => c.Total("Trigger Chance (%)", c.Count(UncommonNative.Body)), "ChainLightning", 0, 40);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", .8f, () => c.Total("Damage (%)", c.Count(UncommonNative.Body)) / 100, "ChainLightning", 0, 100);
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", .2f, () => c.Total("Proc Coefficient", c.Count(UncommonNative.Body)), "ChainLightning", 0, 150);
            if (c.Enabled.Value)
                U.Patch(typeof(LightningOrb), "Begin", typeof(Ukulele), prefix: nameof(Orb));
            c.Text(() => c.V("Trigger Chance (%)", "%") + " chance on hit to fire <style=cIsDamage>chain lightning</style> for " + c.V("Damage (%)", "%") + " TOTAL damage, striking up to " + c.V("Targets", "", "cIsUtility") + " enemies within " + c.V("Range", "m", "cIsUtility") + ".", () => "Chance on hit to fire <style=cIsDamage>chain lightning</style>.");
        }

        static void Orb(LightningOrb __instance)
        {
            var o = __instance;
            if (o.lightningType != LightningOrb.LightningType.Ukulele)
                return;
            var b = o.attacker ? o.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(b);
            if (n > 0)
            {
                o.range = c.Total("Range", n);
                if (o.bouncedObjects != null && o.bouncedObjects.Count == 0)
                    o.bouncesRemaining = Mathf.Max(0, Mathf.FloorToInt(c.Total("Targets", n)) - 1);
            }
        }
    }
}
