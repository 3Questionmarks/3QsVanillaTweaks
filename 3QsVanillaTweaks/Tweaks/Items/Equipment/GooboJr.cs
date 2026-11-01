using System;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Equipment
{
    internal static class GooboJr
    {
        static EquipmentConfig c;
        [ThreadStatic]static int spawning;
        internal static void Init()
        {
            c=new EquipmentConfig("Goobo Jr.","GummyClone");
            c.Flag("Inherits Your Items",true,"Copy the owner's items when the clone spawns. Equipment is never copied. Vanilla: false.");
            c.N("Health (%)",100,"Clone health as a percentage of its normal maximum, with inherited items. Default: 100%.",.01f);
            c.N("Damage (%)",100,"Clone damage as a percentage of its normal damage, with inherited items. Default: 100%.",.01f);
            c.N("Clone Duration",30,"Clone lifetime in seconds. Vanilla: 30.",.01f);
            c.N("Maximum Clones",3,"Maximum active clones per owner. Vanilla: 3.",1);
            if(c.Values.Enabled.Value){U.Harmony.Patch(A.Method(typeof(GummyCloneProjectile),"SpawnGummyClone"),prefix:new HarmonyMethod(typeof(GooboJr),nameof(Begin)),finalizer:new HarmonyMethod(typeof(GooboJr),nameof(End)));}
            if(c.Values.Enabled.Value)U.Harmony.Patch(A.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(GooboJr),nameof(StatsIL)));
            c.Patch(typeof(MasterCopySpawnCard),"FromMaster",typeof(GooboJr),prefix:nameof(Copy));
            c.Patch(typeof(CharacterMaster),"GetDeployableSameSlotLimit",typeof(GooboJr),postfix:nameof(Limit));
            c.Values.Literal("RoR2.CharacterMaster","SetUpGummyClone",30,()=>c.F("Clone Duration"));
            c.Text(()=>"Create a gummy clone with "+c.V("Damage (%)","%")+" damage and "+c.V("Health (%)","%","cIsHealing")+" health. Expires in "+c.V("Clone Duration"," seconds","cIsUtility")+"."+(c.B("Inherits Your Items")?" <style=cIsUtility>Inherits your items</style>.":""),()=>"Create a temporary gummy clone."+(c.B("Inherits Your Items")?" Inherits your items.":""));
        }
        static void Begin(GummyCloneProjectile __instance,out int[] __state)
        {
            __state=null;if(!c.Active)return;
            var d=A.Field(typeof(GummyCloneProjectile),"damageBoostCount");var h=A.Field(typeof(GummyCloneProjectile),"hpBoostCount");
            __state=new[]{(int)d.GetValue(__instance),(int)h.GetValue(__instance)};
            d.SetValue(__instance,0);h.SetValue(__instance,0);spawning++;
        }
        static Exception End(GummyCloneProjectile __instance,int[] __state,Exception __exception)
        {
            if(__state!=null){spawning--;A.Field(typeof(GummyCloneProjectile),"damageBoostCount").SetValue(__instance,__state[0]);A.Field(typeof(GummyCloneProjectile),"hpBoostCount").SetValue(__instance,__state[1]);}return __exception;
        }
        static System.Collections.Generic.IEnumerable<CodeInstruction> StatsIL(System.Collections.Generic.IEnumerable<CodeInstruction> input)
        {
            var applied=new System.Collections.Generic.HashSet<string>();
            foreach(var ins in input){
                if(ins.operand is System.Reflection.MethodInfo setter&&(setter.Name=="set_maxHealth"||setter.Name=="set_damage")&&applied.Add(setter.Name)){
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Ldarg_0);
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call,A.Method(typeof(GooboJr),setter.Name=="set_maxHealth"?nameof(Health):nameof(Damage)));
                }
                yield return ins;
            }
        }
        static bool Clone(CharacterBody b)=>c.Active&&b.inventory&&b.inventory.GetItemCountEffective(DLC1Content.Items.GummyCloneIdentifier)>0;
        static float Health(float value,CharacterBody b)=>Clone(b)?value*c.F("Health (%)")/100:value;
        static float Damage(float value,CharacterBody b)=>Clone(b)?value*c.F("Damage (%)")/100:value;
        static void Copy(ref bool copyItems,ref bool copyEquipment){if(spawning>0){copyItems=c.B("Inherits Your Items");copyEquipment=false;}}
        static void Limit(DeployableSlot slot,ref int __result){if(c.Active&&slot==DeployableSlot.GummyClone)__result=Mathf.RoundToInt(c.F("Maximum Clones"));}
    }
}
