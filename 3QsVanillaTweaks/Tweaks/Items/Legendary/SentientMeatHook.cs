using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class SentientMeatHook
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Sentient Meat Hook", "BounceNearby");
            c.Pair("Proc Coefficient", .5f, 0, "Hook proc coefficient. Vanilla: 0.33.");
            c.Pair("Damage (%)", 100, 0, "Hook damage relative to the triggering hit. Vanilla: 100%.");
            c.Pair("Chance Amplification (%)", 20, 20, "Hyperbolic trigger chance amplification, scaled by the triggering proc coefficient. Vanilla: 20 per stack.");
            c.Pair("Maximum Targets", 10, 5, "Maximum hooked targets. Vanilla: 10 +5 per stack.");
            c.Pair("Range", 30, 0, "Hook range in metres. Vanilla: 30.");
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", .33f, () => c.Total("Proc Coefficient", c.Count(N.Source)), "BounceNearby");
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 1f, () => c.Total("Damage (%)", c.Count(N.Source)) / 100f, "BounceNearby", 1);
            c.NativeCount("RoR2.GlobalEventManager", "ProcessHitEnemy", 20, "Chance Amplification (%)");
            c.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 30, () => c.Total("Range", c.Count(N.Source)), "BounceNearby");
            RareItemSupport.HookTargets(c);
            c.Text(() => "Hits have a <style=cIsUtility>" + Chance(c.F("Chance Amplification (%)")).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%</style>" + (c.F("Chance Amplification (%) Per Stack") == 0 ? "" : " <style=cStack>(increases with stacks)</style>") + " chance to hook up to " + c.V("Maximum Targets", "", "cIsUtility") + " nearby enemies for " + c.V("Damage (%)", "%") + " damage.", () => "Hits can hook nearby enemies.");
        }

        static float Chance(float amplification) => 100 * amplification / (100 + amplification);
    }
}
