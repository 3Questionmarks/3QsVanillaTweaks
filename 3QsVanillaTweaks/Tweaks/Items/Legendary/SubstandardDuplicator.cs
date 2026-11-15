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
    internal static class SubstandardDuplicator
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Substandard Duplicator", "Duplicator");
            c.Pair("Additional Temporary Item Duration", 30, 10, "Additional lifetime of temporary items, in seconds. Added to the general temporary-item duration setting. Vanilla: 10 seconds per stack.");
            RareItemSupport.TempDuration(c);
            c.Text(
                () => "Picking up an item gives you a temporary copy of itself. Temporary items last "
                    + c.V("Additional Temporary Item Duration", "s", "cIsUtility") + " longer.",
                () => "Picking up an item grants a temporary copy. Temporary items last longer.");
        }
    }
}
