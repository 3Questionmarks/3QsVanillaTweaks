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
    // Native item ID: CritAtLowerElevation. Configuration and hooks for this item only.
    internal static class VanillaCritAtLowerElevation
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static MealConfig critFood;
        internal static void Init()=>MealSupport.Register("CritAtLowerElevation",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            c.Number("Buff Duration",10,"Critical chance and damage buff duration, in seconds.");
            v.Literal(Events,"ProcessHitEnemy",10,()=>v.F("Buff Duration"),"CritAtLowerElevation",0,150);
            v.Literal(Events,"ProcessHitEnemy",10,()=>v.F("Buff Duration"),"CritAtLowerElevation",1,150);
            c.Number("Maximum Buff Stacks Per Item",10,"Maximum critical chance/damage stacks per item.");
            // Both the cap and stacks awarded retain the native item scaling.
            critFood=c;if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(GlobalEventManager),"ProcessHitEnemy"),transpiler:new HarmonyMethod(typeof(VanillaCritAtLowerElevation),nameof(CritFoodCapIL)));
            c.Number("Critical Chance Per Buff (%)",1,"Critical chance per buff stack.");c.Number("Critical Damage Per Buff (%)",1,"Critical damage per buff stack.");
            if(v.Enabled.Value){U.Ensure();U.Stats+=b=>{int n=b.GetBuffCount(DLC3Content.Buffs.CritChanceAndDamage);if(!v.Active||n<=0)return;float crit=n*(v.F("Critical Chance Per Buff (%)")-1);bool convert=b.inventory&&b.inventory.GetItemCountEffective(DLC1Content.Items.CritGlassesVoid)>0;if(convert)U.Add(b,"critMultiplier",crit/100);else U.Add(b,"crit",crit);U.Add(b,"critMultiplier",n*(v.F("Critical Damage Per Buff (%)")-1)/100);};}
        }
        static IEnumerable<CodeInstruction> CritFoodCapIL(IEnumerable<CodeInstruction> input)
        {
            bool item=false;foreach(var ins in input){yield return ins;if(ins.operand is FieldInfo f&&f.Name=="CritAtLowerElevation")item=true;
                if(item&&ins.opcode==OpCodes.Ldc_I4_S&&Convert.ToInt32(ins.operand)==10){yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(VanillaCritAtLowerElevation),nameof(CritCap)));item=false;}
            }
        }
        static int CritCap(int native)=>critFood.Values.Active?Mathf.RoundToInt(critFood.Values.F("Maximum Buff Stacks Per Item")):native;
    }
}
