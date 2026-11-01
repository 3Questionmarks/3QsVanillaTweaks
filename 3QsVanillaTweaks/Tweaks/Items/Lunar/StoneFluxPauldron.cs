using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
namespace Tweaks.Items.Lunar
{
    internal static class StoneFluxPauldron
    {
        internal enum Downside { MovementSpeed, Armour }
        private const string Section = "Items - Lunar - Stone Flux Pauldron";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<Downside> downside;
        private static ConfigEntry<float> health, healthStack, slow, slowStack, armour, armourStack;
        internal static void Init()
        {
            enabled = C.BindStartupBool(Section,"Enabled",true,"Enable configurable Stone Flux Pauldron stats. Restart to apply.");
            downside = C.Bind(Section,"Downside",Downside.Armour,"MovementSpeed: reduce movement speed. Armour: replace the speed penalty with flat armour loss.");
            health = Number("Health Increase (%)",100f,"First-stack maximum health increase. Vanilla: 100%.");
            healthStack = Number("Health Increase Per Additional Stack (%)",100f,"Additional maximum health increase per extra stack. Vanilla: 100%.");
            slow = Number("Movement Speed Reduction (%)",25f,"First-stack speed reduction in isolation. Converted to an additive slowing coefficient; 25% at one stack and 40% at two with defaults.",99.9f);
            slowStack = Number("Movement Speed Reduction Per Additional Stack (%)",25f,"Additional slowing coefficient per extra stack, expressed as the equivalent isolated speed reduction.",99.9f);
            armour = Number("Armour Loss",50f,"Flat armour removed by the first stack in Armour mode.");
            armourStack = Number("Armour Loss Per Additional Stack",50f,"Additional flat armour loss per extra stack in Armour mode.");
            if (!enabled.Value) return;
            var h=new Harmony("3Questionmarks.3QsVanillaTweaks.StoneFluxPauldron");
            h.Patch(AccessTools.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(StoneFluxPauldron),nameof(StatsIL)),postfix:new HarmonyMethod(typeof(StoneFluxPauldron),nameof(AfterStats)));
            Tweaks.Survivors.SurvivorSettings.SetText("ITEM_HALFSPEEDDOUBLEHEALTH_DESC",Description);
            Tweaks.Survivors.SurvivorSettings.SetText("ITEM_HALFSPEEDDOUBLEHEALTH_PICKUP", () => "Double your health... but reduce your <style=cIsHealth>" + (C.Get(downside)==Downside.Armour ? "armour" : "movement speed") + "</style>.");
            C.EffectiveSettingsChanged += Refresh;
        }
        private static ConfigEntry<float> Number(string key,float value,string description,float max=100000f)=>C.Bind(Section,key,value,new ConfigDescription(description,new AcceptableValueRange<float>(0f,max)));
        private static bool Active=>enabled!=null&&C.Get(enabled);
        internal static float Linear(int count,float first,float extra)=>count>0?first+extra*(count-1):0f;
        internal static float SlowCoefficient(float percent)=>percent/(100f-percent);
        private static float Health(float count)=>!Active?count:Linear((int)count,C.Get(health),C.Get(healthStack))/100f;
        private static float RemoveSlow(float count)=>Active?0f:count;
        private static int Local(CodeInstruction i,bool store)
        {
            var op=i.opcode;
            if(op==(store?OpCodes.Stloc_0:OpCodes.Ldloc_0))return 0;
            if(op==(store?OpCodes.Stloc_1:OpCodes.Ldloc_1))return 1;
            if(op==(store?OpCodes.Stloc_2:OpCodes.Ldloc_2))return 2;
            if(op==(store?OpCodes.Stloc_3:OpCodes.Ldloc_3))return 3;
            if(op!=(store?OpCodes.Stloc:OpCodes.Ldloc)&&op!=(store?OpCodes.Stloc_S:OpCodes.Ldloc_S))return -1;
            if(i.operand is LocalBuilder b)return b.LocalIndex;
            if(i.operand is LocalVariableInfo v)return v.LocalIndex;
            return Convert.ToInt32(i.operand);
        }
        private static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> instructions)
        {
            var list=new List<CodeInstruction>(instructions);int slot=-1, uses=0;
            for(int i=0;i<list.Count;i++)if(list[i].operand is FieldInfo f&&f.Name=="HalfSpeedDoubleHealth")
                for(int j=i+1;j<Math.Min(i+8,list.Count);j++)if(Local(list[j],true)>=0){slot=Local(list[j],true);break;}
            if(slot<0)throw new InvalidOperationException("Stone Flux item-count local not found.");
            for(int i=0;i<list.Count-1;i++)if(Local(list[i],false)==slot&&list[i+1].opcode==OpCodes.Conv_R4)
            {
                uses++;
                list.Insert(i+2,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StoneFluxPauldron),uses==1?nameof(Health):nameof(RemoveSlow))));i+=2;
            }
            // This game build applies the speed penalty twice. Replace both without changing any other slows.
            if(uses<2||uses>3)throw new InvalidOperationException("Unexpected Stone Flux stat anchors: "+uses);
            return list;
        }
        private static void AfterStats(CharacterBody __instance)
        {
            if(!Active||!__instance.inventory)return;
            int count=__instance.inventory.GetItemCountEffective(DLC1Content.Items.HalfSpeedDoubleHealth);if(count<=0)return;
            if(C.Get(downside)==Downside.Armour)
                AccessTools.Property(typeof(CharacterBody),"armor").SetValue(__instance,__instance.armor-Linear(count,C.Get(armour),C.Get(armourStack)),null);
            else
            {
                float factor=1f/(1f+SlowCoefficient(C.Get(slow))+(count-1)*SlowCoefficient(C.Get(slowStack)));
                AccessTools.Property(typeof(CharacterBody),"moveSpeed").SetValue(__instance,__instance.moveSpeed*factor,null);
                AccessTools.Property(typeof(CharacterBody),"acceleration").SetValue(__instance,__instance.acceleration*factor,null);
            }
        }
        private static void Refresh(){foreach(var body in CharacterBody.readOnlyInstancesList)if(body&&body.inventory&&body.inventory.GetItemCountEffective(DLC1Content.Items.HalfSpeedDoubleHealth)>0)body.RecalculateStats();}
        private static string Description()=>"Increase <style=cIsHealing>maximum health</style> by <style=cIsHealing>"+C.Get(health)+"%</style> <style=cStack>(+"+C.Get(healthStack)+"% per stack)</style>. "+(C.Get(downside)==Downside.Armour?"Reduce <style=cIsHealth>armour</style> by <style=cIsHealth>"+C.Get(armour)+"</style> <style=cStack>(+"+C.Get(armourStack)+" per stack)</style>.":"Reduce <style=cIsHealth>movement speed</style> by <style=cIsHealth>"+C.Get(slow)+"%</style> <style=cStack>(+"+C.Get(slowStack)+"% per stack)</style>.");
    }
}
