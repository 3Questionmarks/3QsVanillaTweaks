using System;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class PreonAccumulator
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("Preon Accumulator","BFG",true,120);
            c.N("Damage (%)",8000,"Explosion damage. Native effective damage: 8000%.");c.N("Proc Coefficient",2,"Explosion proc coefficient. Vanilla: 2.");
            c.N("Lightning Interval",.25f,"Seconds between lightning bolts. Vanilla: 0.1.",.01f);c.N("Lightning Damage (%)",300,"Damage per lightning bolt. Native effective damage: 1200%.");c.N("Lightning Proc Coefficient",.5f,"Proc coefficient per lightning bolt. Vanilla: 0.1.");
            c.N("Charge Time",2,"Seconds before launching the projectile. Vanilla: 2.",.01f);c.Literal("FireBfg",2,"Charge Time");c.Values.Assign("RoR2.EquipmentSlot","FireBfg","subcooldownTimer",()=>c.F("Charge Time")+.2f);
            RoR2Application.onLoad+=()=>{var p=LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/BeamSphere");if(!p)return;var e=p.GetComponent<ProjectileExplosion>();var b=p.GetComponent<ProjectileProximityBeamController>();
                if(e)c.N("Explosion Radius",e.blastRadius,"Explosion radius in metres. Default read from native projectile.");
                if(b){c.N("Lightning Range",b.attackRange,"Lightning search radius in metres. Default read from native projectile.");c.N("Lightning Targets Per Interval",b.attackFireCount,"Bolts fired per interval. Default read from native projectile.",1);c.N("Lightning Target Reset Interval",b.listClearInterval,"Seconds until previous targets may be struck again. Default read from native projectile.",.01f);}
            };
            c.Patch(typeof(ProjectileController),"Start",typeof(PreonAccumulator),prefix:nameof(Start));
            c.Text(()=>"Fires preon tendrils, zapping enemies within <style=cIsDamage>"+c.F("Lightning Range")+"m</style> for "+c.V("Lightning Damage (%)","%")+" damage every "+c.V("Lightning Interval"," seconds","cIsUtility")+". On contact, detonate in a <style=cIsDamage>"+c.F("Explosion Radius")+"m radius</style> for "+c.V("Damage (%)","%")+" damage.",()=>"Fire a powerful ball of energy that zaps nearby enemies.");
        }
        static void Start(ProjectileController __instance)
        {
            if(!c.Active||!__instance.name.StartsWith("BeamSphere",StringComparison.Ordinal)||!c.Values.Numbers.ContainsKey("Explosion Radius"))return;
            // EquipmentSlot launches this projectile at 2x base damage. Compensate here for both independent damage values.
            var e=__instance.GetComponent<ProjectileExplosion>();if(e){e.blastDamageCoefficient=c.F("Damage (%)")/200;e.blastProcCoefficient=1;e.blastRadius=c.F("Explosion Radius");}
            __instance.procCoefficient=c.F("Proc Coefficient");
            var b=__instance.GetComponent<ProjectileProximityBeamController>();if(b){b.attackInterval=c.F("Lightning Interval");b.damageCoefficient=c.F("Lightning Damage (%)")/200;b.procCoefficient=c.F("Lightning Proc Coefficient");b.attackRange=c.F("Lightning Range");b.attackFireCount=Mathf.RoundToInt(c.F("Lightning Targets Per Interval"));b.listClearInterval=c.F("Lightning Target Reset Interval");}
        }
    }
}
