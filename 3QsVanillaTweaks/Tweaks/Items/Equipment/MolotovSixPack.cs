using System;
using System.Collections.Generic;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class MolotovSixPack
    {
        static EquipmentConfig c;
        static readonly HashSet<string> projectiles=new HashSet<string>(StringComparer.Ordinal);
        internal static void Init()
        {
            c=new EquipmentConfig("Molotov (6-Pack)","Molotov");c.N("Damage (%)",500,"Explosion damage per bottle. Default corrects the native damage to match the description.");c.N("Burning Area Damage (%)",200,"Burning area damage per second.");
            RoR2Application.onLoad+=()=>Scan(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/MolotovClusterProjectile"));
            c.Patch(typeof(ProjectileController),"Start",typeof(MolotovSixPack),prefix:nameof(Start));c.Patch(typeof(ProjectileDotZone),"Start",typeof(MolotovSixPack),prefix:nameof(Zone));
            c.Text(()=>"Throw "+c.V("Bottles","","cIsDamage")+" flaming molotovs that ignite enemies for "+c.V("Damage (%)","%")+" base damage. Each molotov leaves a burning area for "+c.V("Burning Area Damage (%)","%")+" damage per second.",()=>"Throw flaming molotovs that ignite enemies and leave burning areas.");
        }
        static void Scan(GameObject p)
        {
            if(!p||!projectiles.Add(p.name))return;
            var e=p.GetComponent<ProjectileExplosion>();var fire=p.GetComponent<ProjectileFireChildren>();var zone=p.GetComponent<ProjectileDotZone>();var pc=p.GetComponent<ProjectileController>();
            if(p.name=="MolotovClusterProjectile"){
                c.N("Bottles",e?e.childrenCount:fire?fire.count:6,"Bottles thrown per activation. Vanilla: 6.",1);
            }
            if(e){var impact=e as ProjectileImpactExplosion;if(impact){string lifetimeKey=p.name=="MolotovClusterProjectile"?"Cluster Lifetime":"Bottle Lifetime";if(!c.Values.Numbers.ContainsKey(lifetimeKey))c.N(lifetimeKey,impact.lifetime,"Projectile lifetime in seconds. Default read from native prefab.",.01f);}if(e.blastDamageCoefficient>0&&!c.Values.Numbers.ContainsKey("Explosion Radius")){c.N("Explosion Radius",e.blastRadius,"Bottle explosion radius, in metres. Default read from native prefab.");c.N("Proc Coefficient",pc?pc.procCoefficient*e.blastProcCoefficient:1,"Bottle explosion proc coefficient. Default read from native prefab.");}Scan(e.childrenProjectilePrefab);}
            if(fire)Scan(fire.childProjectilePrefab);
            if(zone&&!c.Values.Numbers.ContainsKey("Burning Area Duration")){c.N("Burning Area Duration",zone.lifetime,"Seconds the burning area remains. Default read from native prefab.");c.N("Burning Area Interval",1/Mathf.Max(.01f,zone.resetFrequency),"Seconds between burning area damage ticks. Default read from native prefab.",.01f);c.N("Burning Area Proc Coefficient",(pc?pc.procCoefficient:1)*zone.overlapProcCoefficient,"Proc coefficient per burning area hit. Default read from native prefab.");}
        }
        static bool Ours(Component p)=>projectiles.Contains(p.name.EndsWith("(Clone)",StringComparison.Ordinal)?p.name.Substring(0,p.name.Length-7):p.name);
        static void Start(ProjectileController __instance)
        {
            if(!c.Active||!Ours(__instance))return;
            var e=__instance.GetComponent<ProjectileExplosion>();var fire=__instance.GetComponent<ProjectileFireChildren>();
            // Keep the entire cluster tree at base damage so each damaging component can use its own coefficient.
            if(e){var impact=e as ProjectileImpactExplosion;if(impact){string key=__instance.name.StartsWith("MolotovClusterProjectile",StringComparison.Ordinal)?"Cluster Lifetime":"Bottle Lifetime";if(c.Values.Numbers.ContainsKey(key))impact.lifetime=c.F(key);}e.childrenDamageCoefficient=1;if(__instance.name.StartsWith("MolotovClusterProjectile",StringComparison.Ordinal))e.childrenCount=Mathf.RoundToInt(c.F("Bottles"));
                if(e.blastDamageCoefficient>0){e.blastDamageCoefficient=c.F("Damage (%)")/100;e.blastRadius=c.F("Explosion Radius");e.blastProcCoefficient=1;__instance.procCoefficient=c.F("Proc Coefficient");}}
            if(fire){fire.childDamageCoefficient=1;fire.childProcCoefficient=1;if(__instance.name.StartsWith("MolotovClusterProjectile",StringComparison.Ordinal))fire.count=Mathf.RoundToInt(c.F("Bottles"));}
        }
        static void Zone(ProjectileDotZone __instance)
        {
            if(!c.Active||!Ours(__instance)||!c.Values.Numbers.ContainsKey("Burning Area Duration"))return;
            var controller=__instance.GetComponent<ProjectileController>();if(controller)controller.procCoefficient=1;
            float interval=c.F("Burning Area Interval");__instance.resetFrequency=1/interval;__instance.fireFrequency=Mathf.Max(__instance.fireFrequency,1/interval);__instance.damageCoefficient=c.F("Burning Area Damage (%)")/100*interval;__instance.lifetime=c.F("Burning Area Duration");__instance.overlapProcCoefficient=c.F("Burning Area Proc Coefficient");
        }
    }
}
