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
    // Native item ID: UtilitySkillMagazine. Configuration and hooks for this item only.
    internal static class HardlightAfterburner
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig afterburner;
        internal static void Init()=>LegendaryConfigSupport.Register("UtilitySkillMagazine",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.Number("Utility Cooldown Reduction (%)",100f/3,"Utility cooldown reduction.");v.Literal(Body,"RecalculateStats",2f/3,()=>1-v.F("Utility Cooldown Reduction (%)")/100);
            c.Pair("Extra Utility Stocks",2,2,"Extra utility skill stocks.");afterburner=c;if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(HardlightAfterburner),nameof(AfterburnerIL)));
        }
        static IEnumerable<CodeInstruction> AfterburnerIL(IEnumerable<CodeInstruction> input)
        {
            bool utility=false;foreach(var ins in input){if(ins.operand is FieldInfo f&&f.Name=="utilityBonusStockSkill")utility=true;
                if(utility&&ins.operand is MethodInfo m&&m.Name=="SetBonusStockFromBody"){
                    yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(HardlightAfterburner),nameof(UtilityStocks)));utility=false;
                }yield return ins;
            }
        }
        static int UtilityStocks(int native,CharacterBody b){int n=afterburner.Values.Count(b);return n>0?native+Mathf.RoundToInt(afterburner.Values.Total("Extra Utility Stocks",n))-2*n:native;}
    }
}
