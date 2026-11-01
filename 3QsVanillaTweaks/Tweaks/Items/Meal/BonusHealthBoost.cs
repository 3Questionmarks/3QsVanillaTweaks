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
    // Native item ID: BonusHealthBoost. Configuration and hooks for this item only.
    internal static class VanillaBonusHealthBoost
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>MealSupport.Register("BonusHealthBoost",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            c.Pair("Health Bonus (%)",.5f,.15f,"Additional maximum health gained from health increases.",100);
            foreach(int occurrence in new[]{0,5})v.Literal(Body,"RecalculateStats",.5f,()=>v.F("Health Bonus (%)")/100,occurrence:occurrence);
            foreach(int occurrence in new[]{0,1})v.Literal(Body,"RecalculateStats",.15f,()=>v.F("Health Bonus (%) Per Stack")/100,occurrence:occurrence);
        }
    }
}
