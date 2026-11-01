using System;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine;
using UnityEngine.Networking;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    // Attached only to a master created by a specific Hex holder, never to ambient spawns.
    internal sealed class HexOwnerTarget : MonoBehaviour
    {
        internal CharacterMaster Owner;
        internal Func<bool> Enabled;
        internal bool Active=>Enabled!=null&&Enabled();
        internal CharacterBody Target { get {var b=Owner?Owner.GetBody():null;return b&&b.healthComponent&&b.healthComponent.alive?b:null;} }
    }
    internal static class HexOwnerTargets
    {
        static bool installed;
        internal static void Ensure()
        {
            if(installed)return;installed=true;U.Ensure();
            HexMechanics.Patch(typeof(BaseAI),"ManagedFixedUpdate",typeof(HexOwnerTargets),prefix:nameof(Restrict));
            HexMechanics.Patch(typeof(BaseAI),"EvaluateSkillDrivers",typeof(HexOwnerTargets),prefix:nameof(Restrict));
            HexMechanics.Patch(typeof(BaseAI),"FindEnemyHurtBox",typeof(HexOwnerTargets),prefix:nameof(Find));
            HexMechanics.Patch(typeof(BaseAI),"OnBodyDamaged",typeof(HexOwnerTargets),prefix:nameof(Retaliate));
            HexMechanics.Patch(typeof(MimicController),"IsValidTarget",typeof(HexOwnerTargets),postfix:nameof(MimicTarget));
            U.BeforeDamage+=Damage;
        }
        internal static HexOwnerTarget Get(CharacterMaster master)
        {var marker=master?master.GetComponent<HexOwnerTarget>():null;return marker&&marker.Active?marker:null;}
        internal static void Mark(CharacterMaster spawned,CharacterMaster owner,Func<bool> enabled)
        {
            if(!NetworkServer.active||!spawned||!owner)return;
            var marker=spawned.GetComponent<HexOwnerTarget>();
            if(marker&&marker.Owner==owner)return;
            if(!marker)marker=spawned.gameObject.AddComponent<HexOwnerTarget>();
            marker.Owner=owner;marker.Enabled=enabled;
            foreach(var ai in spawned.GetComponents<BaseAI>())Restrict(ai);
        }
        static void Restrict(BaseAI __instance)
        {
            var marker=Get(__instance.master);if(!marker)return;
            var body=marker.Target;
            __instance.bufferedTarget=null;
            __instance.currentEnemy.Reset();
            if(body){__instance.currentEnemy.gameObject=body.gameObject;__instance.currentEnemy.bestHurtBox=body.mainHurtBox;}
            __instance.SetCustomTargetGameObject(body?body.gameObject:null);
            __instance.enemyAttention=__instance.enemyAttentionDuration+1;
        }
        static bool Find(BaseAI __instance,ref HurtBox __result)
        {
            var marker=Get(__instance.master);if(!marker)return true;
            var body=marker.Target;__result=body?body.mainHurtBox:null;return false;
        }
        static bool Retaliate(BaseAI __instance)=>!Get(__instance.master);
        static void MimicTarget(CharacterBody victimBody,CharacterBody mimicBody,ref bool __result)
        { var marker=Get(mimicBody?mimicBody.master:null);if(marker&&(!victimBody||victimBody.master!=marker.Owner))__result=false; }
        static void Damage(HealthComponent victim,DamageInfo info)
        {
            var body=info.attacker?info.attacker.GetComponent<CharacterBody>():null;
            var marker=Get(body?body.master:null);
            if(marker&&(!victim.body||victim.body.master!=marker.Owner)){info.damage=0;info.rejected=true;}
        }
    }
}
