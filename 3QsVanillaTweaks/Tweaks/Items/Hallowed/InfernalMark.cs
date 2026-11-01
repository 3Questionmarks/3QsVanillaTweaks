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
namespace Tweaks.Items.Hallowed
{
    // Native item ID: CausticPheromones. Configuration and hooks for this item only.
    internal static class InfernalMark
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>HallowedSupport.Register("CausticPheromones",Configure);
        static void Configure(HallowedConfig c)
        {
            var v=c.Values;
            c.Literal("RoR2.Items.CausticPheromonesBehavior","OnServerDamageDealt",75f,"Maximum Trigger Chance (%)",occurrence:0);
            c.Literal("RoR2.Items.CausticPheromonesBehavior","OnServerDamageDealt",5f,"Exhaustion Duration",occurrence:0);
            c.Literal("RoR2.Items.CausticPheromonesBehavior","OnServerDamageDealt",10f,"Hits To Activate",occurrence:0);
            c.Literal("RoR2.Items.CausticPheromonesBehavior","OnServerDamageDealt",5f,"On Server Damage Dealt - PheromoneExhaustionDuration 2",occurrence:1);
            c.Literal("RoR2.Items.CausticPheromonesBehavior","OnServerDamageDealt",5f,"On Server Damage Dealt - PheromoneExhaustionDuration 3",occurrence:2);
            c.Assets("RoR2.Items.CausticPheromonesBehavior");
            UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/DLC4/CasuticPheromones/CausticPheromonesAura.prefab").Completed+=op=>{if(op.Status==UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)c.Prefab(op.Result);};
        }
    }
}
