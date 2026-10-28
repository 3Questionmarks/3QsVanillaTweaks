using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class Stage3SpawnHelper
    {
        internal static StageFeatureSpawnHelper.Settings Location(string section, string scene, string name, Vector3 position)
        {
            return new StageFeatureSpawnHelper.Settings
            {
                SceneName = scene, Name = name,
                X = ModConfig.Bind(section, "Position X", position.x, "Spawn position on the X axis."),
                Y = ModConfig.Bind(section, "Position Y", position.y, "Spawn height."),
                Z = ModConfig.Bind(section, "Position Z", position.z, "Spawn position on the Z axis."),
                ForwardX = ModConfig.Bind(section, "Forward X", 0f, "Facing direction on the X axis."),
                ForwardZ = ModConfig.Bind(section, "Forward Z", 1f, "Facing direction on the Z axis."),
                SnapToGround = ModConfig.BindBool(section, "Snap To Ground", true, "Place the interactable on the ground."),
                AlignToGround = ModConfig.BindBool(section, "Align To Ground", true, "Match the slope of the ground."),
                HeightOffset = ModConfig.Bind(section, "Height Offset", 0f, "Height above the ground."),
                GroundSearchDistance = ModConfig.Bind(section, "Ground Search Distance", 12f,
                    new ConfigDescription("Distance above and below the spawn height to search for ground.", new AcceptableValueRange<float>(0.1f, 100f)))
            };
        }

        internal static void Shuffle<T>(List<T> list, Xoroshiro128Plus rng)
        {
            for (int i = list.Count - 1; i > 0; --i)
            {
                int j = rng.RangeInt(0, i + 1);
                T value = list[i]; list[i] = list[j]; list[j] = value;
            }
        }

        internal static ItemDef Item(string internalName)
        {
            ItemIndex index = ItemCatalog.FindItemIndex(internalName);
            return index == ItemIndex.None ? null : ItemCatalog.GetItemDef(index);
        }

        internal static void SetMember(object target, string name, object value)
        {
            var property = AccessTools.Property(target.GetType(), name);
            if (property != null && property.CanWrite) { property.SetValue(target, value, null); return; }
            var field = AccessTools.Field(target.GetType(), name);
            if (field == null) throw new InvalidOperationException("Missing game field: " + name);
            field.SetValue(target, value);
        }

        internal static void ScaleCost(GameObject instance, float multiplier = 1f)
        {
            PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
            if (purchase && purchase.costType == CostTypeIndex.Money)
            {
                purchase.cost = Mathf.Max(0, Mathf.RoundToInt(purchase.cost * multiplier));
                purchase.Networkcost = Run.instance.GetDifficultyScaledCost(purchase.cost);
            }
        }

        internal static void Drop(ItemDef item, Vector3 position, int number = 0)
        {
            if (!item || !SpawnRequirements.ItemAllowed(item)) return;
            Vector3 velocity = Quaternion.AngleAxis(number * 137.5f, Vector3.up) * new Vector3(3f, 8f, 0f);
            PickupDropletController.CreatePickupDroplet(new GenericPickupController.CreatePickupInfo
            {
                pickup = new UniquePickup(PickupCatalog.FindPickupIndex(item.itemIndex)),
                position = position, rotation = Quaternion.identity
            }, position, velocity);
        }
    }
}
