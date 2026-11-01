using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using EntityStates.Drone.DroneWeapon;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Equipment
{
    internal static class TheBackUp
    {
        static EquipmentConfig c;
        static readonly ConditionalWeakTable<CharacterMaster,List<CharacterMaster>> drones=new ConditionalWeakTable<CharacterMaster,List<CharacterMaster>>();
        [ThreadStatic]static bool firing;
        internal static void Init()
        {
            c=new EquipmentConfig("The Back-up","DroneBackup");c.Flag("Ignore Ally Cap",true,"Allow strike drones to spawn at the normal team ally limit. A separate per-owner limit still applies.");
            c.N("Maximum Back-up Allies",4,"Maximum living strike drones summoned by this equipment per owner.",1);c.N("Drones Per Use",4,"Drones summoned per activation, up to the per-owner limit. Vanilla: 4.",1);
            c.N("Drone Duration",25,"Base drone lifetime in seconds. Vanilla: 25.",.01f);c.N("Random Extra Duration",3,"Maximum random extra lifetime in seconds. Vanilla: 3.");
            c.N("Strike Drone Primary Burst",4,"Base bullets in each Enfilade burst. Vanilla: 4.",1);c.N("Strike Drone Primary Damage (%)",75,"Damage per Enfilade bullet. Vanilla: 50%.");c.N("Strike Drone Primary Proc Coefficient",1,"Proc coefficient per bullet. Vanilla: 1.");
            c.Patch(typeof(EquipmentSlot),"FireDroneBackup",typeof(TheBackUp),prefix:nameof(Spawn));
            if(c.Values.Enabled.Value)U.Harmony.Patch(A.Method(typeof(FireTurret),"OnEnter"),prefix:new HarmonyMethod(typeof(TheBackUp),nameof(Begin)),finalizer:new HarmonyMethod(typeof(TheBackUp),nameof(End)));
            c.Patch(typeof(BulletAttack),"Fire",typeof(TheBackUp),prefix:nameof(Bullet));
            c.Text(()=>"Call "+c.V("Drones Per Use","","cIsUtility")+" <style=cIsDamage>Strike Drones</style> to fight for you. Lasts "+c.V("Drone Duration"," seconds","cIsUtility")+". Up to "+c.V("Maximum Back-up Allies"," drones","cIsUtility")+" at once.",()=>"Call drones for backup. Lasts "+c.F("Drone Duration")+" seconds.");
        }
        static bool Spawn(EquipmentSlot __instance,ref bool __result)
        {
            if(!c.Active)return true;
            __result=false;if(!NetworkServer.active||!__instance.characterBody||!__instance.characterBody.master)return false;
            var body=__instance.characterBody;var list=drones.GetOrCreateValue(body.master);list.RemoveAll(m=>!m||(m.GetBody()&&m.GetBody().healthComponent&&!m.GetBody().healthComponent.alive));
            int count=Mathf.Min(Mathf.RoundToInt(c.F("Drones Per Use")),Mathf.RoundToInt(c.F("Maximum Back-up Allies"))-list.Count);
            var prefab=LegacyResourcesAPI.Load<GameObject>("Prefabs/CharacterMasters/DroneBackupMaster");if(!prefab||count<=0)return false;
            for(int i=0;i<count;i++){
                var rotation=Quaternion.Euler(0,body.transform.eulerAngles.y+360f*i/count,0);
                var master=new MasterSummon{masterPrefab=prefab,position=body.corePosition+rotation*Vector3.forward*3,rotation=rotation,summonerBodyObject=body.gameObject,ignoreTeamMemberLimit=c.B("Ignore Ally Cap"),useAmbientLevel=true}.Perform();
                if(!master)continue;list.Add(master);master.gameObject.AddComponent<MasterSuicideOnTimer>().lifeTimer=c.F("Drone Duration")+UnityEngine.Random.Range(0,c.F("Random Extra Duration"));__result=true;
            }
            return false;
        }
        static void Begin(FireTurret __instance,out object[] __state)
        {
            __state=null;if(!c.Active||!__instance.characterBody||!__instance.characterBody.name.StartsWith("DroneBackupBody",StringComparison.Ordinal))return;
            __state=new object[]{FireTurret.bulletCount,FireTurret.damageCoefficient,firing};
            FireTurret.bulletCount=Mathf.RoundToInt(c.F("Strike Drone Primary Burst"));FireTurret.damageCoefficient=c.F("Strike Drone Primary Damage (%)")/100;firing=true;
        }
        static Exception End(object[] __state,Exception __exception){if(__state!=null){FireTurret.bulletCount=(int)__state[0];FireTurret.damageCoefficient=(float)__state[1];firing=(bool)__state[2];}return __exception;}
        static void Bullet(BulletAttack __instance){if(firing)__instance.procCoefficient=c.F("Strike Drone Primary Proc Coefficient");}
    }
}
