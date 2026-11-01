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
    // Native item ID: EquipmentMagazineVoid. Configuration and hooks for this item only.
    internal static class LysateCell
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static VoidConfig lysate;
        internal static void Init()=>VoidConfigSupport.Register("EquipmentMagazineVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            c.Number("Special Cooldown Reduction (%)",33,"Cooldown reduction for the special skill.");v.Literal(Body,"RecalculateStats",.67f,()=>1-v.F("Special Cooldown Reduction (%)")/100);
            c.Pair("Extra Special Stocks",1,1,"Additional special skill stocks.");lysate=c;if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(LysateCell),nameof(LysateIL)));
        }
        static IEnumerable<CodeInstruction> LysateIL(IEnumerable<CodeInstruction> input)
        {
            bool special=false;
            foreach(var ins in input){
                if(ins.operand is FieldInfo f&&f.Name=="specialBonusStockSkill")special=true;
                if(special&&ins.operand is MethodInfo m&&m.Name=="SetBonusStockFromBody"){
                    yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(LysateCell),nameof(LysateStocks)));special=false;
                }
                yield return ins;
            }
        }
        static int LysateStocks(int native,CharacterBody b){int n=lysate.Values.Count(b);return n>0?native+Mathf.RoundToInt(lysate.Values.Total("Extra Special Stocks",n))-n:native;}
    }
}
