using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Others.RemoteOperation
{
    // The picker asks GetDroneCost for affordability, display, and purchase.
    // Validate the value again on the server because the client supplies moneyToUse.
    internal static class RemoteDroneCosts
    {
        private const string Section = "Remote Operation - Drone Costs";
        private static ConfigEntry<bool> enabled;
        private static readonly FieldInfo dronePool = AccessTools.Field(typeof(RemoteOperationDronePickerPanel), "dronePool");
        private static readonly HashSet<string> loggedPriceDefinitions = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ConfigEntry<int>> basePrices =
            new Dictionary<string, ConfigEntry<int>>(StringComparer.OrdinalIgnoreCase);

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Override Drone Costs", true,
                "Base Remote Operation selection prices for Remote Operation drones before difficulty scaling. Does not change ordinary drone purchases. Disable to use vanilla prices.");
            Add("Junk Drone", 50);
            Add("Emergency Drone", 100);
            Add("Incinerator Drone", 100);
            Add("Cleanup Drone", 100);
            Add("Missile Drone", 200);
            Add("Jailer Drone", 200);
            Add("Barrier Drone", 200);
            Add("Bombardment Drone", 300);
            Add("Freeze Drone", 300);
            Add("TC-280 Prototype", 500);

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteDroneCosts");
            // Vanilla builds the drone pool in Awake and captures each drone definition
            // in OnCreateButton. Reorder the pool before button allocation, rather than
            // moving already-bound UI tiles afterwards.
            harmony.Patch(AccessTools.Method(typeof(RemoteOperationDronePickerPanel), "Awake"),
                postfix: new HarmonyMethod(typeof(RemoteDroneCosts), nameof(AfterPickerAwake)));
            harmony.Patch(AccessTools.Method(typeof(RemoteOperationDronePickerPanel), "GetDroneCost"),
                postfix: new HarmonyMethod(typeof(RemoteDroneCosts), nameof(AfterGetDroneCost)));
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), nameof(CharacterMaster.SpawnRemoteOperationDrone)),
                prefix: new HarmonyMethod(typeof(RemoteDroneCosts), nameof(BeforeSpawn)) { priority = Priority.First });
        }

        private static void AfterPickerAwake(RemoteOperationDronePickerPanel __instance)
        {
            if (!__instance || dronePool == null) return;
            DroneIndex[] pool = dronePool.GetValue(__instance) as DroneIndex[];
            if (pool == null || pool.Length == 0) return;

            // Preserve vanilla ordering for all other drones. The pool is read by
            // OnCreateButton, InitializeDronePoolButtons and their selection callbacks.
            var order = new List<DroneIndex>(pool);
            int missileIndex = order.FindIndex(i => Identify(DroneCatalog.GetDroneDef(i)) == "Missile Drone");
            if (missileIndex < 0)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote drone ordering: Missile Drone not found in vanilla drone pool.");
                return;
            }
            DroneIndex missile = order[missileIndex];
            order.RemoveAt(missileIndex);
            int fireIndex = order.FindIndex(i => Identify(DroneCatalog.GetDroneDef(i)) == "Incinerator Drone");
            int cleanupIndex = order.FindIndex(i => Identify(DroneCatalog.GetDroneDef(i)) == "Cleanup Drone");
            if (fireIndex < 0 || cleanupIndex < 0)
            {
                ThreeQsVanillaTweaks.Log.Warning($"Remote drone ordering: Incinerator={fireIndex}, Cleanup={cleanupIndex}; no changes made.");
                return;
            }
            order.Insert(Math.Max(fireIndex, cleanupIndex) + 1, missile);
            dronePool.SetValue(__instance, order.ToArray());
            ThreeQsVanillaTweaks.Log.Info("Remote drone ordering: updated picker dronePool before button creation: " +
                string.Join(", ", order.ConvertAll(i => Identify(DroneCatalog.GetDroneDef(i)) ?? "<unknown>")));
        }

        private static void Add(string name, int basePrice)
        {
            basePrices[name] = C.Bind(Section, name + " Base Cost", basePrice,
                new ConfigDescription("Base Remote Operation selection cost for " + name + ". Applied before difficulty scaling.",
                    new AcceptableValueRange<int>(0, 1000000)));
        }

        private static string Identify(DroneDef def)
        {
            if (def == null || !def.remoteOpBody) return null;
            string name = def.remoteOpBody.name;
            string label = def.name ?? string.Empty;
            string combined = (name + " " + label).ToLowerInvariant();
            if (combined.Contains("junk")) return "Junk Drone";
            if (combined.Contains("emergency")) return "Emergency Drone";
            if (combined.Contains("incinerat") || combined.Contains("flamedrone")) return "Incinerator Drone";
            if (combined.Contains("cleanup")) return "Cleanup Drone";
            if (combined.Contains("missile")) return "Missile Drone";
            if (combined.Contains("jailer")) return "Jailer Drone";
            if (combined.Contains("recharge") || combined.Contains("barrier")) return "Barrier Drone";
            if (combined.Contains("bombard")) return "Bombardment Drone";
            if (combined.Contains("freeze") || combined.Contains("copycat")) return "Freeze Drone";
            if (combined.Contains("megadrone") || combined.Contains("tc280") || combined.Contains("tc-280")) return "TC-280 Prototype";
            return null;
        }

        private static int BaseCost(DroneDef def)
        {
            if (def == null) return 0;
            if (enabled == null || !C.Get(enabled)) return def.remoteOpCost;
            string name = Identify(def);
            return name != null && basePrices.TryGetValue(name, out var entry)
                ? Mathf.Max(0, C.Get(entry))
                : def.remoteOpCost;
        }

        private static uint Price(DroneDef def)
        {
            if (def == null || Run.instance == null) return 0;
            long baseCost = Math.Max(0L, BaseCost(def));
            int boundedBaseCost = (int)Math.Min(baseCost, int.MaxValue);
            float difficulty = Stage.instance ? Stage.instance.entryDifficultyCoefficient : Run.instance.difficultyCoefficient;
            return (uint)Math.Max(0, Run.instance.GetDifficultyScaledCost(boundedBaseCost, difficulty));
        }

        private static void AfterGetDroneCost(DroneDef __0, ref int __result)
        {
            if (enabled == null || !C.Get(enabled) || __0 == null || Run.instance == null) return;
            string identifiedName = Identify(__0);
            int configuredBaseCost = identifiedName != null && basePrices.TryGetValue(identifiedName, out var entry)
                ? C.Get(entry) : -1;
            __result = (int)Math.Min(Price(__0), (uint)int.MaxValue);
            // Log each catalog definition once, so an unexpected vanilla name can be
            // identified without flooding the log every time the UI updates.
            string key = (__0.name ?? "<unnamed>") + "|" + (__0.remoteOpBody ? __0.remoteOpBody.name : "<no body>");
            if (loggedPriceDefinitions.Add(key))
                ThreeQsVanillaTweaks.Log.Info($"Remote drone price mapping: def='{__0.name}', body='{(__0.remoteOpBody ? __0.remoteOpBody.name : "<null>")}', identified='{identifiedName ?? "<unmatched>"}', vanillaBase={__0.remoteOpCost}, configBase={configuredBaseCost}, scaledCost={__result}");
        }

        // Server-authoritative pricing. Do not trust the value transmitted by the menu.
        // In particular, prevent sending zero to obtain an expensive drone for free.
        private static bool BeforeSpawn(CharacterMaster __instance, string droneBodyToSpawn, ref uint moneyToUse)
        {
            if (!NetworkServer.active || enabled == null || !C.Get(enabled) || __instance == null ||
                !__instance.playerCharacterMasterController) return true;
            if (Tweaks.DebugStuff.RemoteOperationDrone.DebugSpawnInProgress) return true;
            if (string.IsNullOrEmpty(droneBodyToSpawn) || Run.instance == null) return false;

            BodyIndex index = BodyCatalog.FindBodyIndex(droneBodyToSpawn);
            DroneIndex droneIndex = DroneCatalog.GetDroneIndexFromBodyIndex(index);
            if (droneIndex == DroneIndex.None || DroneCatalog.GetRemoteOpBodyIndexFromDroneIndex(droneIndex) != index)
                return false;
            DroneDef def = DroneCatalog.GetDroneDef(droneIndex);
            if (def == null) return false;
            uint actualCost = Price(def);
            if (__instance.money < actualCost) return false;
            moneyToUse = actualCost;
            return true;
        }
    }
}
