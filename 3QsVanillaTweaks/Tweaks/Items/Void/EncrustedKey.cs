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
namespace Tweaks.Items.Void
{
    // Native item ID: TreasureCacheVoid. Configuration and hooks for this item only.
    internal static class EncrustedKey
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>VoidConfigSupport.Register("TreasureCacheVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/TreasureCacheVoid/LockboxVoid.prefab").Completed+=op=>{if(op.Status==UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)c.Prefab(op.Result);};
        }
    }
}
