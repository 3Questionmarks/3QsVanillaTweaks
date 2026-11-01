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
namespace Tweaks.Items.Lunar
{
    // Native item ID: LunarTrinket. Configuration and hooks for this item only.
    internal static class BeadsOfFealty
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LunarConfigSupport.Register("LunarTrinket",Configure);
        static void Configure(LunarConfig c)
        {
            var v=c.Values;

        }
    }
}
