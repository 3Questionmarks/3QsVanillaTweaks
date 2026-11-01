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
    // Native item ID: VileCenser. Configuration and hooks for this item only.
    internal static class VileCenser
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("VileCenser",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            c.StaticField("RoR2.Orbs.VileCenserTendrilOrb","maxBranches",new[]{"RoR2.Orbs.VileCenserTendrilOrb|SearchForAdditionalTargets"});
            c.StaticField("RoR2.Items.VileCenserBehavior","initialRadius",new[]{"RoR2.Items.VileCenserBehavior|GetSphereRadius"});
            c.StaticField("RoR2.Items.VileCenserBehavior","radiusGrowthFactor",new[]{"RoR2.Items.VileCenserBehavior|GetSphereRadius"});
            c.StaticField("RoR2.Items.VileCenserBehavior","damageCoefficient",new[]{"RoR2.Items.VileCenserBehavior|GetTendrilDamage"});
            c.StaticField("RoR2.Items.VileCenserBehavior","damageCoefficientPerStack",new[]{"RoR2.Items.VileCenserBehavior|GetTendrilDamage"});
            c.Literal("RoR2.Items.VileCenserBehavior","ChanceToActivateOnUnstunnableTarget",25f,"Unstunnable Target Chance (%)",occurrence:0);
            c.Literal("RoR2.Items.VileCenserBehavior","ChanceToActivateOnUnstunnableTarget",25f,"Unstunnable Target Chance Per Stack (%)",occurrence:1);
            c.Literal("RoR2.Items.VileCenserBehavior","OnDamageDealtServer",55f,"Tendril Speed",occurrence:0);
            c.Literal("RoR2.Items.VileCenserDamageController","FireSmokeProjectiles",45f,"Projectile Spread Angle",occurrence:0);
            c.Literal("RoR2.Items.VileCenserDamageController","FireSmokeProjectile",20f,"Projectile Chance (%)",occurrence:0);
            c.Assets("RoR2.Orbs.VileCenserTendrilOrb","RoR2.Orbs.VileCenserTendrilOrb+<>c","RoR2.Items.VileCenserBehavior","RoR2.Items.VileCenserProjectileController","RoR2.Items.VileCenserDamageController","RoR2.Items.VileCenserDamageItem");
        }
    }
}
