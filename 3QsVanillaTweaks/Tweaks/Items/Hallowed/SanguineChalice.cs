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
namespace Tweaks.Items.Hallowed
{
    // Native item ID: SanguineChalice. Configuration and hooks for this item only.
    internal static class SanguineChalice
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("SanguineChalice",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            
            c.Number("Bleed Chance (%)",10,"Additional chance to inflict bleed while holding this item.");
            if(v.Enabled.Value) U.Stats += body =>
            {
                if(v.Count(body)>0) U.Add(body,"bleedChance",v.F("Bleed Chance (%)")-10f);
            };
        }
    }
}
