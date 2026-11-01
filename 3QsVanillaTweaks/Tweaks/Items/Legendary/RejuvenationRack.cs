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
    // Native item ID: IncreaseHealing. Configuration and hooks for this item only.
    internal static class RejuvenationRack
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig rack;
        internal static void Init()=>LegendaryConfigSupport.Register("IncreaseHealing",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.Number("Healing Increase (%)",1,"Multiplier added to healing per stack.",100);
            // Heal reads a cached integer count with an implicit 1x coefficient, so inject after conversion.
            if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(HealthComponent),"Heal",new[]{typeof(float),typeof(ProcChainMask),typeof(bool)}),transpiler:new HarmonyMethod(typeof(RejuvenationRack),nameof(RackIL)));
            rack=c;
        }
        static IEnumerable<CodeInstruction> RackIL(IEnumerable<CodeInstruction> input)
        {
            bool count=false;foreach(var ins in input){yield return ins;if(ins.operand is FieldInfo f&&f.Name=="increaseHealing")count=true;
                else if(count&&ins.opcode==OpCodes.Conv_R4){yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(RejuvenationRack),nameof(RackCount)));count=false;}}
        }
        static float RackCount(float n)=>rack.Values.Active?n*rack.Values.F("Healing Increase (%)")/100:n;
    }
}
