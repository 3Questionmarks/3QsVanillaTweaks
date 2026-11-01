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
    // Native item ID: MoreMissile. Configuration and hooks for this item only.
    internal static class PocketICBM
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig missiles;
        internal static void Init()=>LegendaryConfigSupport.Register("MoreMissile",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.Number("Extra Missiles",2,"Extra missiles launched per missile attack.");c.Number("Missile Damage Per Extra Stack (%)",50,"Additional missile damage per extra stack.");
            v.Literal("RoR2.MissileUtils","GetMoreMissileDamageMultiplier",.5f,()=>v.F("Missile Damage Per Extra Stack (%)")/100);
            missiles=c;if(v.Enabled.Value)U.Harmony.Patch(A.Method(typeof(MissileUtils),"FireMissile",new[]{typeof(Vector3),typeof(CharacterBody),typeof(ProcChainMask),typeof(GameObject),typeof(float),typeof(bool),typeof(GameObject),typeof(DamageColorIndex),typeof(Vector3),typeof(float),typeof(bool)}),prefix:new HarmonyMethod(typeof(PocketICBM),nameof(FireMissiles)));
            c.Assets("RoR2.MissileUtils");
        }
        static bool FireMissiles(Vector3 position,CharacterBody attackerBody,ProcChainMask procChainMask,GameObject victim,float missileDamage,bool isCrit,GameObject projectilePrefab,DamageColorIndex damageColorIndex,Vector3 initialDirection,float force,bool addMissileProc)
        {
            var v=missiles.Values;if(!v.Active)return true;if(!attackerBody)return true;
            int n=attackerBody.inventory?attackerBody.inventory.GetItemCountEffective(DLC1Content.Items.MoreMissile):0;
            if(addMissileProc)procChainMask.AddProc(ProcType.Missile);
            var info=new RoR2.Projectile.FireProjectileInfo{projectilePrefab=projectilePrefab,position=position,rotation=Util.QuaternionSafeLookRotation(initialDirection),procChainMask=procChainMask,target=victim,owner=attackerBody.gameObject,damage=missileDamage*MissileUtils.GetMoreMissileDamageMultiplier(n),crit=isCrit,force=force,damageColorIndex=damageColorIndex};
            RoR2.Projectile.ProjectileManager.instance.FireProjectile(info);
            if(n<=0)return false;
            var bank=attackerBody.GetComponent<InputBankTest>();Vector3 axis=bank?bank.aimDirection:attackerBody.transform.position;
            int extra=Mathf.Clamp(Mathf.RoundToInt(v.F("Extra Missiles")),0,100);
            for(int i=0;i<extra;i++){float angle=45*(1+i/2)*(i%2==0?1:-1);info.rotation=Util.QuaternionSafeLookRotation(Quaternion.AngleAxis(angle,axis)*initialDirection);RoR2.Projectile.ProjectileManager.instance.FireProjectile(info);}
            return false;
        }
    }
}
