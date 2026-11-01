using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Legendary
{
    // Native item ID: ShockNearby. Configuration and hooks for this item only.
    internal static class VanillaShockNearby
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig tesla;
        internal static void Init() => LegendaryConfigSupport.Register("ShockNearby", Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            tesla=c;c.Pair("Targets",3,2,"Maximum targets per lightning chain.");if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(RoR2.Items.ShockNearbyBodyBehavior),"FixedUpdate"),transpiler:new HarmonyMethod(typeof(VanillaShockNearby),nameof(TeslaIL)));
            c.InstanceField("RoR2.Items.ShockNearbyBodyBehavior","teslaResetListInterval",0.5f,new[]{"RoR2.Items.ShockNearbyBodyBehavior|FixedUpdate"});
            c.Literal("RoR2.Items.ShockNearbyBodyBehavior","FixedUpdate",10f,"Fixed Update - teslaBuffRollTimer",occurrence:0);
            c.Literal("RoR2.Items.ShockNearbyBodyBehavior","FixedUpdate",0.083333336f,"Fixed Update - teslaFireTimer",occurrence:0);
            c.Literal("RoR2.Items.ShockNearbyBodyBehavior","FixedUpdate",2f,"Fixed Update - damageValue",occurrence:0);
            c.Literal("RoR2.Items.ShockNearbyBodyBehavior","FixedUpdate",0.3f,"Fixed Update - procCoefficient",occurrence:0);
            c.Literal("RoR2.Items.ShockNearbyBodyBehavior","FixedUpdate",35f,"Fixed Update - range",occurrence:0);
            c.Assets("RoR2.Items.ShockNearbyBodyBehavior");
        }
        static IEnumerable<CodeInstruction> TeslaIL(IEnumerable<CodeInstruction> input)
        {
            foreach(var ins in input){if(ins.opcode==OpCodes.Stfld&&ins.operand is FieldInfo f&&f.Name=="bouncesRemaining")yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(VanillaShockNearby),nameof(TeslaBounces)));yield return ins;}
        }
        static int TeslaBounces(int native)=>tesla.Values.Active?Mathf.Max(0,Mathf.RoundToInt(tesla.Values.Total("Targets",Mathf.Max(1,native/2)))-1):native;
    }
}
