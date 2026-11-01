using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using R=Tweaks.Items.Uncommon.UncommonConfig;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Hallowed
{
    internal static class EarthboundVessel
    {
        static R c;
        internal static void Init()
        {
            U.Ensure();c=new R("Earthbound Vessel","RechargeableJump",true,"Hallowed");
            c.Pair("Armour Per Unused Jump",7,0,"Armour per remaining rechargeable jump. Vanilla: 5.");
            c.Pair("Extra Jumps",3,2,"Rechargeable extra jumps. Vanilla: 3 +2 per stack.");
            c.N("Jump Recharge Time",5,"Seconds grounded to restore one jump. Vanilla: 5.",.01f);
            c.N("Recharge Speed Per Stack (%)",25,"Additional recharge speed per extra stack, using vanilla hyperbolic timing. Vanilla: 25%.");
            if(c.Enabled.Value)
            {
                var type=typeof(CharacterBody).Assembly.GetType("RoR2.Items.RechargeableJumpItemBehaviour");
                U.Patch(type,"GetMaxRechargableJumpCount",typeof(EarthboundVessel),postfix:nameof(Maximum));
                U.Patch(type,"GetRechargeTime",typeof(EarthboundVessel),postfix:nameof(Recharge));
                U.Patch(type,"OnEnable",typeof(EarthboundVessel),postfix:nameof(Enable));
                U.Harmony.Patch(A.Method(typeof(CharacterBody),"RecalculateStats"),transpiler:new HarmonyMethod(typeof(EarthboundVessel),nameof(ArmourIL)));
            }
            c.Text(()=>"Gain "+c.V("Extra Jumps","","cIsUtility")+" extra jumps that recharge while grounded. Restore a jump every <style=cIsUtility>"+c.F("Jump Recharge Time").ToString("0.##")+" seconds</style> <style=cStack>(-"+c.F("Recharge Speed Per Stack (%)").ToString("0.##")+"% per stack)</style>. Gain "+c.V("Armour Per Unused Jump"," armour","cIsHealing")+" per unused extra jump.",()=>"Gain rechargeable extra jumps and armour for each unused jump.");
        }
        static CharacterBody Body(object behaviour)=>(behaviour as Component)?.GetComponent<CharacterBody>();
        static void Maximum(object __instance,ref int __result){int n=c.Count(Body(__instance));if(n>0)__result=Mathf.Max(0,Mathf.RoundToInt(c.Total("Extra Jumps",n)));}
        static void Recharge(object __instance,ref float __result){int n=c.Count(Body(__instance));if(n>0)__result=c.F("Jump Recharge Time")*Util.GetHyperbolicReductionCoefficient(n,0,c.F("Recharge Speed Per Stack (%)"));}
        static void Enable(object __instance)
        {
            var b=Body(__instance);if(!UnityEngine.Networking.NetworkServer.active || c.Count(b)==0 || !b.characterMotor)return;
            int first=Mathf.Max(0,Mathf.RoundToInt(c.F("Extra Jumps")));
            b.SetBuffCount(DLC4Content.Buffs.RechargeableExtraJump.buffIndex,first);b.SetBuffCount(DLC4Content.Buffs.RechargeableExtraJumpCounter.buffIndex,first);
        }
        static float Armour(float native,CharacterBody b)=>c.Count(b)>0?native/5*c.Total("Armour Per Unused Jump",c.Count(b)):native;
        static IEnumerable<CodeInstruction> ArmourIL(IEnumerable<CodeInstruction> input)
        {
            var code=input.ToList();int found=0;
            for(int i=1;i+3<code.Count;i++)
                if(code[i].LoadsConstant(5)&&code[i-1].operand is MethodInfo m&&m.Name=="get_armor"&&code[i+2].opcode==OpCodes.Mul&&code[i+3].opcode==OpCodes.Conv_R4)
                {code.InsertRange(i+4,new[]{new CodeInstruction(OpCodes.Ldarg_0),new CodeInstruction(OpCodes.Call,A.Method(typeof(EarthboundVessel),nameof(Armour)))});found++;i+=5;}
            if(found!=1)throw new InvalidOperationException("Earthbound Vessel armour anchor changed: "+found);return code;
        }
    }
}
