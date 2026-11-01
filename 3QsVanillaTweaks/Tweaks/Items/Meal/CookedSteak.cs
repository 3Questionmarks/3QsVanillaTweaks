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
namespace Tweaks.Items.Meal
{
    // Native item ID: CookedSteak. Configuration and hooks for this item only.
    internal static class VanillaCookedSteak
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>MealSupport.Register("CookedSteak",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            c.Count(Body,"RecalculateStats","CookedSteak",50,"Maximum Health");c.Count(Body,"RecalculateStats","CookedSteak",.05f,"Maximum Health (%)",100);
        }
    }
}
