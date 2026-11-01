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
    // Native item ID: UltimateMeal. Configuration and hooks for this item only.
    internal static class VanillaUltimateMeal
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static MealConfig meal;
        internal static void Init()=>MealSupport.Register("UltimateMeal",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            meal=c;c.Pair("Luck",2,2,"Luck while at full health.");c.Number("Buff Grace Period",3,"Seconds the luck bonus remains after losing full health.");
            v.Literal("RoR2.Items.UltimateMealBodyBehavior","FixedUpdate",3,()=>v.F("Buff Grace Period"));
            if(v.Enabled.Value){U.Ensure();U.Stats+=b=>{int n=v.Count(b);if(n>0&&b.master&&b.HasBuff(DLC3Content.Buffs.UltimateMealBoost))b.master.luck+=v.Total("Luck",n)-2*n;};}
            c.InstanceField("RoR2.Items.UltimateMealBodyBehavior","effectCooldown",1f,new[]{"RoR2.Items.UltimateMealBodyBehavior|FixedUpdate"});
            c.Assets("RoR2.Items.UltimateMealBodyBehavior");
        }
    }
}
