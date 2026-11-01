using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using R=Tweaks.Items.Uncommon.UncommonConfig;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Hallowed
{
    internal static class VengefulSpirit
    {
        static R c;
        static Type controller;
        static FieldInfo interval,timer;
        internal static void Init()
        {
            U.Ensure();c=new R("Vengeful Spirit","VengefulSpirit",true,"Hallowed");
            c.B("Rework",true,"The spirit respawns without consuming the item. Allied deaths grant Vengeful while holding the item, including the spirit's death.");
            c.N("Spirit Respawn Time",30,"Seconds after a spirit dies before the next spirit can spawn. Only used by the rework. Vanilla: spirit death consumes a stack until the next stage.",.1f);
            c.N("Damage Absorbed (%)",30,"Incoming damage redirected to living spirits. Vanilla: 50%.",0,100);
            c.Pair("Maximum Spirits",1,1,"Maximum living spirits. Vanilla: 1 per stack.");
            c.Pair("Vengeful Duration",15,15,"Seconds of Vengeful after an allied death. Vanilla consumed item: 15 seconds per stack.",.01f);
            c.Pair("Vengeful Attack Speed (%)",40,0,"Attack speed gained while Vengeful. Vanilla: 40%.");
            c.Pair("Vengeful Cooldown Reduction (%)",20,0,"Skill cooldown reduction while Vengeful. Vanilla: 20%.");
            controller=typeof(CharacterBody).Assembly.GetType("RoR2.Items.VengefulSpiritAttachmentController");
            interval=A.Field(controller,"spiritSpawnInterval");timer=A.Field(controller,"spiritSpawnTimer");
            c.Field("RoR2.Items.VengefulSpiritAttachmentController","TryDistributeDamage","absorbedDamageFraction",()=>c.F("Damage Absorbed (%)")/100);
            c.Literal("RoR2.CharacterBody","RecalculateStats",.4f,()=>c.Total("Vengeful Attack Speed (%)",Math.Max(1,c.Count(N.Body)))/100,"VengefulRage",0,60);
            c.Literal("RoR2.CharacterBody","RecalculateStats",.8f,()=>Mathf.Max(0,1-c.Total("Vengeful Cooldown Reduction (%)",Math.Max(1,c.Count(N.Body)))/100),"VengefulRage",2);
            if(c.Enabled.Value)
            {
                U.Harmony.Patch(A.Method(controller,"OnSpiritDefeatedServer"),prefix:new HarmonyMethod(typeof(VengefulSpirit),nameof(SpiritDied)));
                U.Harmony.Patch(A.Method(controller,"FixedUpdate"),prefix:new HarmonyMethod(typeof(VengefulSpirit),nameof(BeforeSpawn)),finalizer:new HarmonyMethod(typeof(VengefulSpirit),nameof(AfterSpawn)));
                U.Patch(typeof(CharacterMaster),"GetDeployableSameSlotLimit",typeof(VengefulSpirit),postfix:nameof(Maximum));
                var death=typeof(GlobalEventManager).GetEvent("onCharacterDeathServer",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                if(death!=null)death.AddEventHandler(null,(Action<DamageReport>)AllyDied);
                else U.Harmony.Patch(A.Method(typeof(GlobalEventManager),"HandleCharacterDeath")??A.Method(typeof(GlobalEventManager),"OnCharacterDeath"),postfix:new HarmonyMethod(typeof(VengefulSpirit),nameof(AfterDeath)));
            }
            c.Text(()=>"Gain "+c.V("Maximum Spirits","","cIsUtility")+" <style=cIsUtility>spirit allies</style> that absorb <style=cIsHealing>"+c.F("Damage Absorbed (%)").ToString("0.##")+"%</style> of incoming damage."+(c.B("Rework")?" Spirits respawn after <style=cIsUtility>"+c.F("Spirit Respawn Time").ToString("0.##")+" seconds</style>. Whenever an ally dies, gain <style=cIsDamage>Vengeful</style> for "+c.V("Vengeful Duration"," seconds","cIsUtility")+", increasing attack speed by "+c.V("Vengeful Attack Speed (%)","%")+" and reducing skill cooldowns by "+c.V("Vengeful Cooldown Reduction (%)","%","cIsUtility")+".":" When the spirit dies, this item transforms, granting <style=cIsDamage>Vengeful</style> when allies die until the next stage."),()=>c.B("Rework")?"Gain a spirit that absorbs damage and respawns. Allied deaths make you Vengeful.":"Gain a spirit that absorbs damage. Its death awakens Vengeful.");
        }
        static bool Reworked=>c.Active&&c.B("Rework");
        static bool SpiritDied(object __instance,DamageReport damageReport)
        {
            if(!Reworked)return true;
            // Undeploy/owner-death cleanup is not a combat death or a respawn request.
            if(damageReport?.damageInfo==null||damageReport.damageInfo.inflictor==((Component)__instance).gameObject)return false;
            timer.SetValue(__instance,c.F("Spirit Respawn Time"));
            return false; // Keep the unconsumed item and its native attachment.
        }
        static void BeforeSpawn(object __instance,out float? __state)
        {
            __state=null;if(!Reworked)return;
            __state=(float)interval.GetValue(__instance);interval.SetValue(__instance,c.F("Spirit Respawn Time"));
        }
        static Exception AfterSpawn(object __instance,float? __state,Exception __exception){if(__state.HasValue)interval.SetValue(__instance,__state.Value);return __exception;}
        static void Maximum(CharacterMaster __instance,DeployableSlot slot,ref int __result)
        {if(!c.Active||slot.ToString()!="VengefulSpiritFollower"||!__instance.inventory)return;int n=__instance.inventory.GetItemCountEffective(c.Item);if(n>0)__result=Mathf.Max(0,Mathf.RoundToInt(c.Total("Maximum Spirits",n)));}
        static void AfterDeath(DamageReport __0)=>AllyDied(__0);
        static void AllyDied(DamageReport report)
        {
            if(!NetworkServer.active||!Reworked||report==null)return;
            foreach(var holder in CharacterBody.readOnlyInstancesList)
            {
                int n=c.Count(holder);
                if(n==0||!holder.healthComponent||!holder.healthComponent.alive||holder==report.victimBody||!holder.teamComponent||holder.teamComponent.teamIndex!=report.victimTeamIndex)continue;
                // Native cleanup of spirits during stage transitions/owner death
                // uses the attachment as inflictor; do not turn it into a free buff.
                if(report.damageInfo?.inflictor && report.damageInfo.inflictor.GetComponent(controller))continue;
                holder.AddTimedBuff(DLC4Content.Buffs.VengefulRage,c.Total("Vengeful Duration",n));
            }
        }
    }
}
