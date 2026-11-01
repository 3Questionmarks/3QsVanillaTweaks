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
    internal static class GrowthNectar
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Growth Nectar", "BoostAllStats");
            c.Pair("Stat Increase Per Buff (%)", 5, 0, "Increase to all native Growth Nectar stats per qualifying buff. Vanilla: 4%.");
            c.Pair("Maximum Buffs", 4, 4, "Maximum qualifying buffs. Vanilla: 4 per stack.");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .04f, () => c.Total("Stat Increase Per Buff (%)", c.Count(N.Body)) / 100f, "BoostAllStatsBuff");
            RareItemSupport.Nectar(c);
            c.Text(() => "Each unique buff increases all stats by " + c.V("Stat Increase Per Buff (%)", "%", "cIsUtility") + ", up to " + c.V("Maximum Buffs", "", "cIsUtility") + " buffs.", () => "Unique buffs increase all stats.");
        }
    }
}
