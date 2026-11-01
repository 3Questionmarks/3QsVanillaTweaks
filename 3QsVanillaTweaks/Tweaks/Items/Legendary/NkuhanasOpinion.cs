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
    // Native item ID: NovaOnHeal. Configuration and hooks for this item only.
    internal static class NkuhanasOpinion
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        static LegendaryConfig skulls;
        internal static void Init()=>LegendaryConfigSupport.Register("NovaOnHeal",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            skulls=c;c.Number("Proc Coefficient",.2f,"Proc coefficient of a skull.");if(v.Enabled.Value)U.Patch(typeof(RoR2.Orbs.DevilOrb),"Begin",typeof(NkuhanasOpinion),prefix:nameof(Skull));
            c.Count(Health,"ServerFixedUpdate","novaOnHeal",2.5f,"Base Skull Damage (%)",100);
            c.Literal(Health,"ServerFixedUpdate",2.5f,"Stored Healing Damage (%)",100,1,"novaOnHeal",150);
            c.Literal(Health,"ServerFixedUpdate",10,"Health Per Skull Divisor",1,0,"novaOnHeal",150);
            c.Literal(Health,"ServerFixedUpdate",40,"Skull Range",1,0,"novaOnHeal",180);
            c.Assets("RoR2.Orbs.DevilOrb","RoR2.Orbs.DevilOrb+EffectType");
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/DevilOrbEffect"));
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/WispOrbEffect"));
        }
        static void Skull(RoR2.Orbs.DevilOrb __instance)
        {
            if(__instance.effectType!=RoR2.Orbs.DevilOrb.EffectType.Skull||!__instance.procChainMask.HasProc(ProcType.HealNova))return;var body=__instance.attacker?__instance.attacker.GetComponent<CharacterBody>():null;
            if(skulls.Values.Count(body)>0)__instance.procCoefficient=skulls.Values.F("Proc Coefficient");
        }
    }
}
