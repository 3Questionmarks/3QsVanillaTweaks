using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RoR2;
using RoR2.Items;
using UnityEngine;
using UnityEngine.Networking;
using R=Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Hex
{
    internal sealed class HexPatienceFog : MonoBehaviour
    {
        internal float Tick;
        internal float[] Original;
        internal bool Modified;
        internal readonly Dictionary<CharacterBody,float> Exposure=new Dictionary<CharacterBody,float>();
    }
    internal static class HexOfPatience
    {
        static R c;
        static float nativeRamp=1.125f;
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Patience","OutOfTPRadius");
            c.B("Hurt Holder Only",true,"Only damage holders outside the charging teleporter. Each holder uses their own stacks. False restores vanilla team-wide damage and stacking.");
            RoR2Application.onLoad+=Configure;
            HexMechanics.Patch(typeof(OutOfTPRadiusManager),"InstantiateDamagerForTeam",typeof(HexOfPatience),postfix:nameof(Mark));
            HexMechanics.Patch(typeof(FogDamageController),"MyFixedUpdate",typeof(HexOfPatience),prefix:nameof(Tick));
            HexMechanics.Text(c,()=>"While outside the charging teleporter zone, take "+c.V("Health Damage Per Second (%)","%","cDeath")+" of maximum health as damage per second. Damage increases by "+c.V("Ramp Increase Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style> every "+c.V("Ramp Interval","s","cDeath")+"."+(c.B("Hurt Holder Only")?" Only affects you.":" Affects your entire team."),()=>c.B("Hurt Holder Only")?"Take increasing damage outside the charging teleporter zone.":"Your team takes increasing damage outside the charging teleporter zone.");
        }
        static float Get(FogDamageController fog,string name)=>Convert.ToSingle(AccessTools.Field(typeof(FogDamageController),name).GetValue(fog));
        static void Configure()
        {
            var prefab=AccessTools.Field(typeof(OutOfTPRadiusManager),"_fogDamageControllerPrefab").GetValue(null) as GameObject;
            var fog=prefab?prefab.GetComponent<FogDamageController>():null;
            if(fog)nativeRamp=fog.healthFractionRampCoefficientPerSecond;
            c.N("Health Damage Per Second (%)",fog?Get(fog,"healthFractionPerSecond")*100:1,"Percentage of combined maximum health dealt per second; defaults to the native prefab value.");
            c.N("Damage Interval",fog?Get(fog,"tickPeriodSeconds"):1,"Seconds between damage ticks; defaults to vanilla.",.05f);
            c.N("Ramp Interval",fog?Get(fog,"healthFractionRampIncreaseCooldown"):10,"Seconds outside before damage increases; defaults to vanilla.",.05f);
            c.N("Ramp Increase Per Stack (%)",fog?(fog.healthFractionRampCoefficientPerSecond-1)*100:12.5f,"Damage increase per ramp interval per item stack; defaults to vanilla.");
        }
        static void Mark()
        {
            var dictionary=(IDictionary)AccessTools.Field(typeof(OutOfTPRadiusManager),"_teamIndextoFogDamageController").GetValue(null);
            foreach(var value in dictionary.Values)
            {
                var fog=AccessTools.Field(value.GetType(),"fogDamageControllerInstance").GetValue(value) as FogDamageController;
                if(fog&&!fog.GetComponent<HexPatienceFog>()){var marker=fog.gameObject.AddComponent<HexPatienceFog>();marker.Original=new[]{Get(fog,"healthFractionPerSecond"),Get(fog,"tickPeriodSeconds"),Get(fog,"healthFractionRampIncreaseCooldown")};}
            }
        }
        static void Restore(FogDamageController fog,HexPatienceFog marker)
        {
            string[] fields={"healthFractionPerSecond","tickPeriodSeconds","healthFractionRampIncreaseCooldown"};
            for(int i=0;i<fields.Length;i++)AccessTools.Field(typeof(FogDamageController),fields[i]).SetValue(fog,marker.Original[i]);
            fog.healthFractionRampCoefficientPerSecond=nativeRamp+(nativeRamp%1)*(TeamCount(fog)-1);
        }
        static int TeamCount(FogDamageController fog)
        {
            var filter=fog.GetComponent<TeamFilter>();
            return filter?(int)AccessTools.Method(typeof(OutOfTPRadiusManager),"GetOutOfTPRadiusCountFromTeam").Invoke(null,new object[]{filter.teamIndex}):0;
        }
        static bool Tick(FogDamageController __instance,float deltaTime)
        {
            var marker=__instance.GetComponent<HexPatienceFog>();
            if(!marker||!c.Numbers.ContainsKey("Damage Interval"))return true;
            if(!c.Active){if(marker.Modified){Restore(__instance,marker);marker.Modified=false;}marker.Exposure.Clear();return true;}
            if(!NetworkServer.active)return true;
            // Only instances created by the Patience manager are changed. Ordinary Void fog is untouched.
            if(!c.B("Hurt Holder Only"))
            {
                marker.Modified=true;
                AccessTools.Field(typeof(FogDamageController),"healthFractionPerSecond").SetValue(__instance,c.F("Health Damage Per Second (%)")/100);
                AccessTools.Field(typeof(FogDamageController),"tickPeriodSeconds").SetValue(__instance,c.F("Damage Interval"));
                AccessTools.Field(typeof(FogDamageController),"healthFractionRampIncreaseCooldown").SetValue(__instance,c.F("Ramp Interval"));
                int n=TeamCount(__instance);
                __instance.healthFractionRampCoefficientPerSecond=1+c.F("Ramp Increase Per Stack (%)")*n/100;
                marker.Exposure.Clear();marker.Tick=0;return true;
            }
            if(!(bool)AccessTools.Field(typeof(FogDamageController),"isActive").GetValue(__instance))return false;
            marker.Tick+=deltaTime;
            float interval=c.F("Damage Interval");
            if(marker.Tick<interval)return false;
            marker.Tick-=interval;
            var affected=new HashSet<CharacterBody>(__instance.GetAffectedBodies().Where(b=>b&&b.healthComponent&&b.healthComponent.alive&&c.Count(b)>0));
            foreach(var old in marker.Exposure.Keys.Where(b=>!b||!affected.Contains(b)).ToArray())marker.Exposure.Remove(old);
            var buff=AccessTools.Field(typeof(FogDamageController),"dangerBuffDef").GetValue(__instance) as BuffDef;
            foreach(var body in affected)
            {
                marker.Exposure.TryGetValue(body,out float time);
                float ramps=Mathf.Floor(time/c.F("Ramp Interval"));
                float multiplier=1+c.F("Ramp Increase Per Stack (%)")*c.Count(body)/100;
                float damage=body.healthComponent.fullCombinedHealth*c.F("Health Damage Per Second (%)")/100*interval*Mathf.Pow(multiplier,ramps);
                marker.Exposure[body]=time+interval;
                if(damage>0)body.healthComponent.TakeDamage(new DamageInfo{damage=damage,procCoefficient=0,position=body.corePosition,damageType=new DamageTypeCombo(DamageType.BypassArmor|DamageType.BypassBlock,DamageTypeExtended.DamageField,DamageSource.Hazard),damageColorIndex=DamageColorIndex.Void});
                if(buff)body.AddTimedBuff(buff,Mathf.Max(interval,Get(__instance,"dangerBuffDuration")));
            }
            return false;
        }
    }
}
