using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class NativeEquipmentData
    {
        internal static readonly string[] Ids={"AffixBlue","AffixHaunted","AffixLunar","AffixPoison","AffixRed","AffixWhite","BFG","Blackhole","BossHunter","BossHunterConsumed","BurnNearby","Cleanse","CommandMissile","CrippleWard","CritOnUse","DeathProjectile","DroneBackup","DroneShockDamage","EliteAurelioniteEquipment","EliteBeadEquipment","EliteCollectiveEquipment","EliteDeathEquipment","EliteEarthEquipment","EliteVoidEquipment","FireBallDash","Fruit","GainArmor","Gateway","GoldGat","GroundEnemies","GummyClone","HealAndRevive","HealAndReviveConsumed","Jetpack","LifestealOnHit","Lightning","LunarPortalOnUse","LunarPotion","Meteor","Molotov","MultiShopCard","Parry","PassiveHealing","QuestVolatileBattery","Recycle","Saw","Scanner","SpawnAltar","TeamWarCry","Tonic","VendingMachine"};
        internal static void Configure(Tweaks.Items.Equipment.EquipmentNativeConfig c)
        {
            switch(c.Id){
case "Fruit":
break;
case "Meteor":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/MeteorStorm"));
break;
case "Blackhole":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/GravSphere"));
break;
case "Saw":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/Sawmerang"));
break;
case "OrbitalLaser":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/OrbitalLaser"));
break;
case "GhostGun":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/GhostGun"));
break;
case "PassiveHealing":
break;
case "CrippleWard":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/CrippleWard"));
break;
case "Gateway":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/Zipline"));
break;
case "Cleanse":
break;
case "FireBallDash":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/FireballVehicle"));
break;
case "Recycle":
break;
case "LifestealOnHit":
break;
case "DeathProjectile":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/DeathProjectile"));
break;
case "VendingMachine":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/VendingMachineProjectile"));
break;
case "BossHunter":
break;
case "GroundEnemies":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/GroundEnemiesZone"));
break;
case "HealAndReviveConsumed":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/SproutOfLifeHealing"));
break;
case "SpawnAltar":
c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/BodyAttachments/SpawnAltarPlacementHolder"));
break;
            }
        }
    }
}
