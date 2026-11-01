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
    internal static class HelfireTincture
    {
        internal static void Init() => LunarConfigSupport.RegisterEquipment("BurnNearby", Configure);
        static void Configure(LunarConfig c)
        {
            c.Literal("RoR2.EquipmentSlot", "FireBurnNearby", 12f, "Duration");
            var prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/HelfireController");
            if (!prefab) return;
            var controller = prefab.GetComponent<HelfireController>();
            c.PrefabField(controller, "baseRadius", "Radius");
            c.PrefabField(controller, "dotDuration", "Burn Duration");
            c.PrefabField(controller, "interval", "Burn Application Interval");
            c.PrefabField(controller, "healthFractionPerSecond", "Self Damage Per Second (%)", 100);
            c.PrefabField(controller, "allyDamageScalar", "Ally Damage Multiplier");
            c.PrefabField(controller, "enemyDamageScalar", "Enemy Damage Multiplier");
        }
    }
}
