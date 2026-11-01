using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    // Native item ID: Clover. Configuration and hooks for this item only.
    internal static class FiftySevenLeafClover
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig clover;
        internal static void Init()=>LegendaryConfigSupport.Register("Clover",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            clover=c;c.Pair("Luck",1,1,"Extra chance rolls.");if(v.Enabled.Value){U.Ensure();U.Stats+=b=>{int n=v.Count(b);if(n>0&&b.master)b.master.luck+=v.Total("Luck",n)-n;};}
        }
    }
}
