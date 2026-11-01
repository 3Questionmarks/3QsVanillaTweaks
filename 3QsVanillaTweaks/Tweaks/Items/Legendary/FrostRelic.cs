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
    internal static class FrostRelic
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Frost Relic", "Icicle");
            c.Pair("Damage Per Icicle (%)", 100, 0, "Damage per tick contributed by each icicle. Vanilla: 100%, no item damage scaling.");
            c.Pair("Damage (%)", 200, 200, "Base aura damage per tick before icicles. Vanilla: 200%.");
            c.Pair("Proc Coefficient", 0, 0, "Proc coefficient per tick. Vanilla: 0.2.");
            c.Pair("Damage Interval", .25f, 0, "Seconds between damage ticks. Vanilla: 0.25.");
            c.Pair("Icicle Duration", 5, 0, "Icicle lifetime in seconds. Vanilla: 5.");
            c.Pair("Base Radius", 10, 0, "Base radius in metres. Vanilla: 10.");
            c.Pair("Radius Per Icicle", 2, 0, "Extra radius per icicle in metres. Vanilla: 2.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance for a kill to add icicles. Vanilla: 100%.");
            c.Pair("Icicles Per Kill", 1, 0, "Icicles added by each kill. Vanilla: 1.");
            c.Pair("Initial Icicles", 2, 0, "Icicles granted on the first kill. Vanilla: 2.");
            c.Pair("Maximum Icicles", 6, 3, "Maximum icicles. Vanilla: 6 +3 per stack.");
            RareItemSupport.Icicles(c);
            c.Text(() => "Killing an enemy surrounds you with an " + "<style=cIsDamage>ice storm</style>" + ", dealing " + c.V("Damage (%)", "%") + " damage plus " + c.V("Damage Per Icicle (%)", "%") + " per icicle every " + c.V("Damage Interval", "s", "cIsUtility") + ". Icicles last " + c.V("Icicle Duration", "s", "cIsUtility") + ".", () => "Kills surround you with a damaging ice storm.");
        }
    }
}
