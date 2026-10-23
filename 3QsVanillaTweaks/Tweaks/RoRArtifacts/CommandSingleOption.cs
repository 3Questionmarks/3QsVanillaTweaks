using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.RoRArtifacts
{
    internal static class CommandSingleOption
    {
        private const string Section = "Artifacts - Command";
        private static ConfigEntry<bool> enabled;
        private static readonly FieldInfo PickupInfoField = AccessTools.Field(typeof(PickupDropletController), "createPickupInfo");
        private static readonly MethodInfo GetOptions = AccessTools.Method(typeof(PickupPickerController), "GetOptionsFromPickupState", new[] { typeof(UniquePickup) });

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Directly Drop Single-Option Pickups", true,
                "Skip the Command cube when its selection contains exactly one available pickup.");
            var target = AccessTools.Method(typeof(PickupDropletController), "CreateCommandCube");
            if (target == null || PickupInfoField == null || GetOptions == null)
            {
                Debug.LogError("[3QsVanillaTweaks] Command single-option: expected private members not found.");
                return;
            }
            new Harmony("3Questionmarks.3QsVanillaTweaks.CommandSingleOption")
                .Patch(target, prefix: new HarmonyMethod(typeof(CommandSingleOption), nameof(BeforeCreateCommandCube)));
        }

        private static bool BeforeCreateCommandCube(PickupDropletController __instance)
        {
            if (!ModConfig.Get(enabled) || !NetworkServer.active || !__instance) return true;
            try
            {
                var info = (GenericPickupController.CreatePickupInfo)PickupInfoField.GetValue(__instance);
                UniquePickup source = info.pickup;
                PickupPickerController.Option[] options =
                    (PickupPickerController.Option[])GetOptions.Invoke(null, new object[] { source });
                if (options == null) return true;
                int available = 0;
                PickupIndex only = PickupIndex.none;
                foreach (var option in options)
                {
                    if (!option.available) continue;
                    available++;
                    if (available > 1) return true;
                    only = option.pickup.pickupIndex;
                }
                if (available != 1 || only == PickupIndex.none) return true;
                source.pickupIndex = only;
                info.pickup = source;
                GenericPickupController.CreatePickup(in info);
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError("[3QsVanillaTweaks] Command single-option fallback to vanilla cube: " + ex);
                return true;
            }
        }
    }
}
