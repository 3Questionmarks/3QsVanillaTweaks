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
    // Native item ID: WyrmOnHit. Configuration and hooks for this item only.
    internal static class VanillaWyrmOnHit
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>MealSupport.Register("WyrmOnHit",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            c.Count("RoR2.Items.WyrmOnHitBehavior","TryFire","stack",5,"Wyrm Damage (%)",100);
            c.Number("Trigger Chance (%)",10,"Base proc chance, before the native reduction for active wyrms.");
            v.Literal("RoR2.Items.WyrmOnHitBehavior","SolveForProcChance",10,()=>v.F("Trigger Chance (%)"));v.Literal("RoR2.Items.WyrmOnHitBehavior","SolveForProcChance",10,()=>v.F("Trigger Chance (%)"),occurrence:1);
            c.Literal("RoR2.Items.WyrmOnHitBehavior","TryFire",10,"Wyrm Duration");c.Literal("RoR2.Items.WyrmOnHitBehavior","TryFire",1,"Cooldown");
            c.Prefab(CharacterBody.CommonAssets.wyrmOnHitProjectile);
            c.Assets("RoR2.Items.WyrmOnHitBehavior");
        }
    }
}
