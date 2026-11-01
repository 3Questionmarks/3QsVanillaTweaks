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
    // Native item ID: Talisman. Configuration and hooks for this item only.
    internal static class SoulboundCatalyst
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LegendaryConfigSupport.Register("Talisman",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.Pair("Cooldown Reduction On Kill",4,2,"Equipment cooldown reduced on kill, in seconds.");v.Literal(Events,"OnCharacterDeath",2,()=>v.F("Cooldown Reduction On Kill")-v.F("Cooldown Reduction On Kill Per Stack"),occurrence:7);v.Literal(Events,"OnCharacterDeath",2,()=>v.F("Cooldown Reduction On Kill Per Stack"),occurrence:8);
        }
    }
}
