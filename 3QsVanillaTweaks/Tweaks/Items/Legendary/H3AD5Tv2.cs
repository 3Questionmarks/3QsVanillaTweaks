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
    // Native item ID: FallBoots. Configuration and hooks for this item only.
    internal static class H3AD5Tv2
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LegendaryConfigSupport.Register("FallBoots",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.StaticField("EntityStates.Headstompers.HeadstompersIdle","inputConfirmationDelay",new[]{"EntityStates.Headstompers.HeadstompersIdle|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersCharge","maxChargeDuration",new[]{"EntityStates.Headstompers.HeadstompersCharge|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersCharge","minVelocityY",new[]{"EntityStates.Headstompers.HeadstompersCharge|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersCharge","accelerationY",new[]{"EntityStates.Headstompers.HeadstompersCharge|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","maxFallDuration",new[]{"EntityStates.Headstompers.HeadstompersFall|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","maxFallSpeed",new[]{"EntityStates.Headstompers.HeadstompersFall|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","maxDistance",new[]{"EntityStates.Headstompers.HeadstompersFall|DoStompExplosionAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","initialFallSpeed",new[]{"EntityStates.Headstompers.HeadstompersFall|OnEnter"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","accelerationY",new[]{"EntityStates.Headstompers.HeadstompersFall|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","minimumRadius",new[]{"EntityStates.Headstompers.HeadstompersFall|DoStompExplosionAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","maximumRadius",new[]{"EntityStates.Headstompers.HeadstompersFall|DoStompExplosionAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","minimumDamageCoefficient",new[]{"EntityStates.Headstompers.HeadstompersFall|DoStompExplosionAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","maximumDamageCoefficient",new[]{"EntityStates.Headstompers.HeadstompersFall|DoStompExplosionAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersFall","seekCone",new[]{"EntityStates.Headstompers.HeadstompersFall|OnEnter","EntityStates.Headstompers.HeadstompersFall|FixedUpdateAuthority"});
            c.StaticField("EntityStates.Headstompers.HeadstompersCooldown","baseDuration",new[]{"EntityStates.Headstompers.HeadstompersCooldown|OnEnter"});
            c.Literal("EntityStates.Headstompers.HeadstompersIdle","FixedUpdateAuthority",2f,"Fixed Update Authority - velocity",occurrence:0);
            c.Literal("EntityStates.Headstompers.HeadstompersFall","OnEnter",300f,"On Enter - maxDistanceFilter",occurrence:0);
            c.Literal("EntityStates.Headstompers.HeadstompersFall","DoStompExplosionAuthority",200f,"Do Stomp Explosion Authority - baseForce",occurrence:0);
            c.Literal("EntityStates.Headstompers.HeadstompersFall","DoStompExplosionAuthority",2000f,"Do Stomp Explosion Authority - bonusForce",occurrence:0);
            c.Assets("EntityStates.Headstompers.BaseHeadstompersState","EntityStates.Headstompers.HeadstompersIdle","EntityStates.Headstompers.HeadstompersCharge","EntityStates.Headstompers.HeadstompersFall","EntityStates.Headstompers.HeadstompersCooldown","RoR2.Items.HeadstomperBodyBehavior");
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/BootShockwave"));
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/HeadstompersController"));
        }
    }
}
