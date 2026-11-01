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
    // Native item ID: GoldenGoose. Configuration and hooks for this item only.
    internal static class TotemOfAvarice
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("GoldenGoose",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            c.Literal(Events,"ProcessHitEnemy",10,"Cooldown",1,6);c.Literal(Events,"ProcessHitEnemy",10,"Radius",1,7);c.Literal(Events,"ProcessHitEnemy",10,"Radius Per Stack",1,8);
            c.Literal("RoR2.GoldenGooseBodyBehavior","MidasTouch",5f,"Midas Touch - Value",occurrence:0);
            c.Literal("RoR2.GoldenGooseBodyBehavior","MidasTouch",0.6f,"Midas Touch - Value 2",occurrence:0);
            c.Literal("RoR2.GoldenGooseBodyBehavior","MidasTouch",0.3f,"Midas Touch - Value 3",occurrence:0);
            c.Literal("RoR2.GoldenGooseBodyBehavior","MidasTouch",0.15f,"Midas Touch - Value 4",occurrence:0);
            c.Assets("RoR2.GoldenGooseBodyBehavior");
        }
    }
}
