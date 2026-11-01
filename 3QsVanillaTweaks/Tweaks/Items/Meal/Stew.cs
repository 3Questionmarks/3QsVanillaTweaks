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
    // Native item ID: Stew. Configuration and hooks for this item only.
    internal static class VanillaStew
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static MealConfig stew;
        internal static void Init()=>MealSupport.Register("Stew",Configure);
        static void Configure(MealConfig c)
        {
            var v=c.Values;
            stew=c;c.Count(Body,"RecalculateStats","Stew",2.5f,"Health Regeneration");c.Number("Regeneration Converted To Damage (%)",1,"Regeneration added as damage while at full health.",100);
            if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(VanillaStew),nameof(StewIL)));
            c.Assets("RoR2.Items.StewBodyBehavior");
        }
        static IEnumerable<CodeInstruction> StewIL(IEnumerable<CodeInstruction> input)
        {
            bool stewBlock=false,done=false;foreach(var ins in input){yield return ins;if(ins.operand is FieldInfo f&&f.Name=="YummyStew")stewBlock=true;
                if(stewBlock&&!done&&ins.operand is MethodInfo m&&m.Name=="get_regen"){yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(VanillaStew),nameof(StewDamage)));done=true;}}
        }
        static float StewDamage(float regen)=>stew.Values.Active?regen*stew.Values.F("Regeneration Converted To Damage (%)")/100:regen;
    }
}
