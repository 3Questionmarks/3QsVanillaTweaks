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
    // Native item ID: KillEliteFrenzy. Configuration and hooks for this item only.
    internal static class Brainstalks
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LegendaryConfigSupport.Register("KillEliteFrenzy",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.Count(Events,"OnCharacterDeath","KillEliteFrenzy",4,"Buff Duration");
        }
    }
}
