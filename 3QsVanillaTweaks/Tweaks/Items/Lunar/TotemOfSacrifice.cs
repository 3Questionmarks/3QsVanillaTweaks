using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using S = Tweaks.Items.Common.CommonItemSupport;
namespace Tweaks.Items.Lunar
{
    internal static class TotemOfSacrifice
    {
        internal static void Init() => LunarConfigSupport.RegisterEquipment("SpawnAltar", Configure);
        static void Configure(LunarConfig c)
        {
            var holder = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/BodyAttachments/SpawnAltarPlacementHolder");
            if (!holder) return;
            var handler = holder.GetComponent<SpawnAltarPlacementHandler>();
            if (!handler) return;
            c.PrefabField(handler, "placementMaxUp", "Placement Maximum Height");
            c.PrefabField(handler, "placementMaxDown", "Placement Maximum Drop");
            c.PrefabField(handler, "placementForwardDistance", "Placement Distance");
            var altar = A.Field(typeof(SpawnAltarPlacementHandler), "altarObject").GetValue(handler) as GameObject;
            if (!altar) return;
            var zone = altar.GetComponent<SpawnAltarZoneController>();
            c.PrefabField(zone, "basePointsPerLevel", "Points Per Reward Level");
            c.PrefabField(zone, "levelsForAltar", "Maximum Reward Level", 1, 1, 3);
            c.PrefabField(zone, "ghostAliveDuration", "Ghost Duration");
            var holdout = altar.GetComponent<HoldoutZoneController>();
            c.PrefabField(holdout, "baseRadius", "Radius");
            c.PrefabField(holdout, "baseChargeDuration", "Duration");
            var tables = A.Field(typeof(SpawnAltarZoneController), "altarDropTables").GetValue(zone) as System.Collections.IEnumerable;
            if (tables != null) foreach (var table in tables) c.DropTable(table as PickupDropTable);
        }
    }
}
