using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class PickupRecovery
    {
        private sealed class State
        {
            internal readonly HashSet<MapZone> ExitZones = new HashSet<MapZone>();
            internal float NextRecovery;
        }

        private static ConfigEntry<bool> enabled, items, equipment, lunarCoins, debug;
        private static ConfigEntry<float> heightOffset;
        private static readonly Dictionary<GameObject, State> States = new Dictionary<GameObject, State>();
        private static readonly HashSet<MapZone> FailedZones = new HashSet<MapZone>();
        private static Run currentRun;
        private static float nextCheck;

        internal static void Init()
        {
            const string section = "Others - Pickup Recovery";
            enabled = ModConfig.BindBool(section, "Enabled", true,
                "Return dropped pickups which fall out of bounds to the same safe locations used for players.");
            items = ModConfig.BindBool(section, "Recover Items", true, "Recover items, airborne item droplets and item choice modules.");
            equipment = ModConfig.BindBool(section, "Recover Equipment", true, "Recover equipment, airborne equipment droplets and equipment choice modules.");
            lunarCoins = ModConfig.BindBool(section, "Recover Lunar Coins", true, "Recover lunar coin pickups and airborne lunar coins.");
            heightOffset = ModConfig.Bind(section, "Height Offset", 0.5f,
                new ConfigDescription("Place recovered pickups this far above the player's safe return point to avoid embedding them in the ground.",
                    new AcceptableValueRange<float>(0f, 5f)));
            debug = ModConfig.BindBool("Debug - Pickup Recovery", "Log Pickup Recovery", true,
                "Log recovered pickups.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Others.PickupRecovery");
            foreach (Type type in new[] { typeof(GenericPickupController), typeof(PickupDropletController), typeof(PickupPickerController) })
            {
                harmony.Patch(AccessTools.Method(type, "OnEnable"),
                    postfix: new HarmonyMethod(typeof(PickupRecovery), nameof(AfterPickupEnabled)));
                harmony.Patch(AccessTools.Method(type, "OnDisable"),
                    postfix: new HarmonyMethod(typeof(PickupRecovery), nameof(AfterPickupDisabled)));
            }
            harmony.Patch(AccessTools.Method(typeof(MapZone), "TryZoneStart", new[] { typeof(Collider) }),
                prefix: new HarmonyMethod(typeof(PickupRecovery), nameof(BeforeZoneStart)));
            RoR2Application.onFixedUpdate += Tick;
        }

        private static void EnsureRun()
        {
            if (ReferenceEquals(currentRun, Run.instance)) return;
            currentRun = Run.instance;
            States.Clear();
            FailedZones.Clear();
            nextCheck = 0f;
        }

        private static State Track(GameObject pickup)
        {
            if (!States.TryGetValue(pickup, out State state))
            {
                States.Add(pickup, state = new State());
                RememberExitZones(state, pickup.transform.position);
            }
            return state;
        }

        private static void RememberExitZones(State state, Vector3 position)
        {
            foreach (MapZone zone in InstanceTracker.GetInstancesList<MapZone>())
                if (Relevant(zone) && zone.triggerType == MapZone.TriggerType.TriggerExit && zone.IsPointInsideMapZone(position))
                    state.ExitZones.Add(zone);
        }

        private static bool Relevant(MapZone zone)
        {
            return zone && zone.isActiveAndEnabled && zone.zoneType == MapZone.ZoneType.OutOfBounds;
        }

        private static void AfterPickupEnabled(Component __instance)
        {
            if (!NetworkServer.active || !Run.instance) return;
            EnsureRun();
            // Record starting containment immediately, before a fast droplet can leave the map.
            Track(__instance.gameObject);
        }

        private static void AfterPickupDisabled(Component __instance)
        {
            States.Remove(__instance.gameObject);
        }

        private static bool Allowed(PickupIndex index)
        {
            PickupDef pickup = PickupCatalog.GetPickupDef(index);
            if (pickup == null) return false;
            if (pickup.coinValue > 0) return ModConfig.Get(lunarCoins);
            if (pickup.itemIndex != ItemIndex.None) return ModConfig.Get(items);
            if (pickup.equipmentIndex != EquipmentIndex.None) return ModConfig.Get(equipment);
            return pickup.itemTier != ItemTier.NoTier && ModConfig.Get(items);
        }

        private static bool Allowed(PickupPickerController picker)
        {
            if (!picker || picker.options == null) return false;
#pragma warning disable CS0612, CS0618
            foreach (var option in picker.options) if (Allowed(option.pickupIndex)) return true;
#pragma warning restore CS0612, CS0618
            return false;
        }

        private static void BeforeZoneStart(MapZone __instance, Collider __0)
        {
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !Relevant(__instance) || !__0) return;
            EnsureRun();
            PickupPickerController picker = __0.GetComponentInParent<PickupPickerController>();
            if (Allowed(picker)) { Recover(picker.gameObject, Track(picker.gameObject), __instance); return; }
            PickupDropletController droplet = __0.GetComponentInParent<PickupDropletController>();
#pragma warning disable CS0612, CS0618
            if (droplet && Allowed(droplet.pickupIndex)) Recover(droplet.gameObject, Track(droplet.gameObject), __instance);
            else
            {
                GenericPickupController pickup = __0.GetComponentInParent<GenericPickupController>();
                if (pickup && Allowed(pickup.pickupIndex)) Recover(pickup.gameObject, Track(pickup.gameObject), __instance);
            }
#pragma warning restore CS0612, CS0618
        }

        private static void Tick()
        {
            EnsureRun();
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || Time.fixedTime < nextCheck) return;
            nextCheck = Time.fixedTime + 0.2f;
            // Point tests also cover pickups whose layer does not collide with map-zone triggers.
#pragma warning disable CS0612, CS0618
            foreach (PickupDropletController pickup in InstanceTracker.GetInstancesList<PickupDropletController>())
                if (pickup && Allowed(pickup.pickupIndex)) Check(pickup.gameObject);
            foreach (GenericPickupController pickup in InstanceTracker.GetInstancesList<GenericPickupController>())
                if (pickup && Allowed(pickup.pickupIndex)) Check(pickup.gameObject);
            foreach (PickupPickerController picker in InstanceTracker.GetInstancesList<PickupPickerController>())
                if (Allowed(picker)) Check(picker.gameObject);
#pragma warning restore CS0612, CS0618
        }

        private static void Check(GameObject pickup)
        {
            State state = Track(pickup);
            if (Time.fixedTime < state.NextRecovery) return;
            Vector3 position = pickup.transform.position;
            MapZone fallbackExit = null;
            float closestExit = float.PositiveInfinity;
            bool insideExit = false;
            foreach (MapZone zone in InstanceTracker.GetInstancesList<MapZone>())
            {
                if (!Relevant(zone)) continue;
                bool inside = zone.IsPointInsideMapZone(position);
                if (zone.triggerType == MapZone.TriggerType.TriggerExit)
                {
                    if (inside) { insideExit = true; state.ExitZones.Add(zone); }
                    else
                    {
                        Collider boundary = zone.GetComponent<Collider>();
                        float distance = boundary ? (boundary.ClosestPoint(position) - position).sqrMagnitude : float.PositiveInfinity;
                        if (!fallbackExit || distance < closestExit) { fallbackExit = zone; closestExit = distance; }
                    }
                }
                bool outside = zone.triggerType == MapZone.TriggerType.TriggerEnter
                    ? inside : !inside && state.ExitZones.Contains(zone);
                if (outside) { Recover(pickup, state, zone); return; }
            }
            // A pickup may be created beyond an edge, or recovery may be enabled mid-run.
            // Outside every containing boundary is unambiguously out of bounds, even if
            // this pickup has never entered one. Use the nearest boundary's return rule.
            if (!insideExit && fallbackExit) Recover(pickup, state, fallbackExit);
        }

        private static void Recover(GameObject pickup, State state, MapZone zone)
        {
            if (Time.fixedTime < state.NextRecovery) return;
            state.NextRecovery = Time.fixedTime + 1f;
            try
            {
                Vector3 destination;
                if ((AccessTools.Field(typeof(MapZone), "useExactExplicitPositionAndRotation")?.GetValue(zone) is bool exact && exact) && zone.explicitDestination)
                    destination = zone.explicitDestination.position;
                else
                {
                    Vector3 target = zone.explicitDestination ? zone.explicitDestination.position : pickup.transform.position;
                    destination = Run.instance.FindSafeTeleportPositionSimplified(HullClassification.Human,
                        target, 0f, zone.destinationIdealRadius);
                }
                destination += Vector3.up * ModConfig.Get(heightOffset);
                bool inside = zone.IsPointInsideMapZone(destination);
                if (zone.triggerType == MapZone.TriggerType.TriggerEnter ? inside : !inside)
                    throw new InvalidOperationException("The game's safe-position search did not find a point outside this fall zone.");
                foreach (Rigidbody body in pickup.GetComponentsInChildren<Rigidbody>())
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                TeleportHelper.TeleportGameObject(pickup, destination);
                state.ExitZones.Clear();
                RememberExitZones(state, destination);
                if (ModConfig.Get(debug)) Log.Info("Pickup recovery: returned " + pickup.name + " to " + destination);
            }
            catch (Exception e)
            {
                if (FailedZones.Add(zone)) Log.Warning("Pickup recovery could not return a pickup from " + zone.name + ": " + e.Message);
            }
        }
    }
}
