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
namespace Tweaks.Items.Void
{
    // Native item ID: BearVoid. Configuration and hooks for this item only.
    internal static class SaferSpaces
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>VoidConfigSupport.Register("BearVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            c.Number("Recharge Time",15,"Base recharge time before the native per-stack reduction.");c.Number("Recharge Reduction Per Stack (%)",10,"Multiplicative recharge reduction per stack.");
            v.Literal(Health,"TakeDamageProcess",15,()=>v.F("Recharge Time"),"BearVoidReady",0,140);v.Literal(Health,"TakeDamageProcess",15,()=>v.F("Recharge Time"),"BearVoidReady",1,140);
            v.Literal(Health,"TakeDamageProcess",.9f,()=>Mathf.Clamp01(1-v.F("Recharge Reduction Per Stack (%)")/100),"BearVoidReady",0,140);v.Literal(Health,"TakeDamageProcess",.9f,()=>Mathf.Clamp01(1-v.F("Recharge Reduction Per Stack (%)")/100),"BearVoidReady",1,140);
            c.Assets("RoR2.BearVoidBehavior");
        }
    }
}
