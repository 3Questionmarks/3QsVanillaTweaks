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
    // Native item ID: LowHealthOrbSpawner. Configuration and hooks for this item only.
    internal static class DreamCrucible
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("LowHealthOrbSpawner",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            c.StaticField("RoR2.Items.LowHealthOrbSpawnerBehavior","baseChanceNormalized",new[]{"RoR2.Items.LowHealthOrbSpawnerBehavior|GetRollChanceNormalized"});
            c.StaticField("RoR2.Items.LowHealthOrbSpawnerBehavior","intervalBetweenRestorations",new[]{"RoR2.Items.LowHealthOrbSpawnerBehavior|FixedUpdate"});
            c.Assets("RoR2.Items.LowHealthOrbSpawnerBehavior");
        }
    }
}
