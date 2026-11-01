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
    internal static class SonorousWhispers
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Sonorous Whispers", "ItemDropChanceOnKill");
            c.Pair("Elite Drop Chance (%)", 5, 2.5f, "Item drop chance on elite kills. Vanilla: 5% +1% per additional stack.");
            c.Pair("Boss Drop Chance (%)", 100, 0, "Drop chance on champion kills. Vanilla: 100%.");
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 4, () => c.F("Elite Drop Chance (%)") - c.F("Elite Drop Chance (%) Per Stack"), "ItemDropChanceOnKill");
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 1, () => c.F("Elite Drop Chance (%) Per Stack"), "ItemDropChanceOnKill");
            RareItemSupport.BossDrop(c);
            c.Text(() => "Large monsters drop an item. Elites have a " + c.V("Elite Drop Chance (%)", "%", "cIsUtility") + " chance to drop an item.", () => "Large monsters and elites can drop items.");
        }
    }
}
