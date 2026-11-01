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
    // Native item ID: DroneWeapons. Configuration and hooks for this item only.
    internal static class SpareDroneParts
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LegendaryConfigSupport.Register("DroneWeapons",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.StaticField("RoR2.DroneWeaponsBehavior","display2Chance",new[]{"RoR2.DroneWeaponsBehavior|UpdateMinionInventory"});
            c.StaticField("RoR2.DroneWeaponsBehavior","minSpawnDist",new[]{"RoR2.DroneWeaponsBehavior|OnEnable"});
            c.StaticField("RoR2.DroneWeaponsBehavior","maxSpawnDist",new[]{"RoR2.DroneWeaponsBehavior|OnEnable"});
            c.StaticField("RoR2.DroneWeaponsBehavior","spawnRetryDelay",new[]{"RoR2.DroneWeaponsBehavior|TrySpawnDrone"});
            c.StaticField("RoR2.DroneWeaponsBoostBehavior","microMissileDamageCoefficient",new[]{"RoR2.DroneWeaponsBoostBehavior|OnEnemyHit"});
            c.StaticField("RoR2.DroneWeaponsBoostBehavior","microMissileProcCoefficient",new[]{"RoR2.DroneWeaponsBoostBehavior|OnEnemyHit"});
            c.StaticField("RoR2.DroneWeaponsBoostBehavior","procPercentChance",new[]{"RoR2.DroneWeaponsBoostBehavior|OnEnemyHit"});
            c.StaticField("RoR2.DroneWeaponsBoostBehavior","attackSpeedPerStack",new[]{"RoR2.CharacterBody|RecalculateStats"});
            c.StaticField("RoR2.DroneWeaponsBoostBehavior","cooldownReductionPerStack",new[]{"RoR2.CharacterBody|RecalculateStats"});
            c.Assets("EntityStates.DroneWeaponsChainGun.AimChainGun","EntityStates.DroneWeaponsChainGun.BaseDroneWeaponChainGunState","EntityStates.DroneWeaponsChainGun.FireChainGun","RoR2.DroneWeaponsBehavior","RoR2.DroneWeaponsBoostBehavior");
        }
    }
}
