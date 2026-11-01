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
    // Native item ID: RandomDamageZone. Configuration and hooks for this item only.
    internal static class MercurialRachis
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LunarConfigSupport.Register("RandomDamageZone",Configure);
        static void Configure(LunarConfig c)
        {
            var v=c.Values;
            c.Literal(Body, "RecalculateStats", .5f, "Damage Increase (%)", 100, anchor: "PowerBuff", window: 20);
            c.StaticField("RoR2.Items.RandomDamageZoneBodyBehavior","wardDuration",new[]{"RoR2.Items.RandomDamageZoneBodyBehavior|FixedUpdate"});
            c.StaticField("RoR2.Items.RandomDamageZoneBodyBehavior","wardRespawnRetryTime",new[]{"RoR2.Items.RandomDamageZoneBodyBehavior|FixedUpdate"});
            c.StaticField("RoR2.Items.RandomDamageZoneBodyBehavior","baseWardRadius",new[]{"RoR2.Items.RandomDamageZoneBodyBehavior|FixedUpdate"});
            c.StaticField("RoR2.Items.RandomDamageZoneBodyBehavior","wardRadiusMultiplierPerStack",new[]{"RoR2.Items.RandomDamageZoneBodyBehavior|FixedUpdate"});
            c.Literal("RoR2.Items.RandomDamageZoneBodyBehavior","FindWardSpawnPosition",50f,"Find Ward Spawn Position - Value",occurrence:0);
            c.Assets("RoR2.Items.RandomDamageZoneBodyBehavior");
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/DamageZoneWard"));
        }
    }
}
