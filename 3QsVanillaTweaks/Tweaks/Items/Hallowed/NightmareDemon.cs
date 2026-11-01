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
    // Native item ID: DreamDemon. Configuration and hooks for this item only.
    internal static class NightmareDemon
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("DreamDemon",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            c.Literal("RoR2.Items.DreamDemonBehavior","OnServerDamageDealt",20f,"Trigger Chance (%)",occurrence:0);
            c.Literal("RoR2.Items.DreamDemonBehavior","OnServerDamageDealt",0.5f,"Projectile Spawn Offset",occurrence:0);
            c.Literal("RoR2.Items.DreamDemonBehavior","OnServerDamageDealt",6f,"Damage Coefficient Per Stack",occurrence:0);
            c.Literal("RoR2.Items.DreamDemonBehavior","OnServerDamageDealt",5f,"Projectile Speed",occurrence:0);
            c.Assets("RoR2.Items.DreamDemonBehavior","RoR2.Projectile.DreamDemonProjectileController","RoR2.Projectile.DreamDemonProjectileController+GrowthThresholdInfo","RoR2.Projectile.DreamDemonProjectileController+OnTargetTakeDamageServerListener");
        }
    }
}
