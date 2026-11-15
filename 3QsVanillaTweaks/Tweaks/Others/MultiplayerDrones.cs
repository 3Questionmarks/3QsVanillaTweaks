using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using Tweaks.DebugStuff;

namespace Tweaks.Others
{
    internal static class MultiplayerDrones
    {
        private static ConfigEntry<bool> enabled, interact, pickup, ownDronePickups, benefits, deathUsesDisconnectCooldown;
        private static ConfigEntry<int> respawns;
        private static ConfigEntry<float> disconnectCooldown;
        private sealed class Count { internal Stage Stage; internal int Used; }
        private sealed class DisconnectState
        {
            internal Run.FixedTimeStamp Finish;
            internal bool AllowNextSpawn;
            internal bool WaitingAfterDeath;
        }
        private static readonly ConditionalWeakTable<CharacterMaster, Count> counts = new ConditionalWeakTable<CharacterMaster, Count>();
        private static readonly ConditionalWeakTable<CharacterMaster, DisconnectState> disconnectStates = new ConditionalWeakTable<CharacterMaster, DisconnectState>();
        private static readonly System.Reflection.FieldInfo preventRespawn = AccessTools.Field(typeof(CharacterMaster), "preventRespawnUntilNextStageServer");
        internal static void Init()
        {
            const string section = "Remote Operation - General";
            enabled = C.BindStartupBool(section, "Enabled", true, "Enable multiplayer remote-operation drone settings. Restart to apply.");
            interact = C.BindBool(section, "Can Interact With Interactables", true, "Allow player-controlled remote-operation drones to use interactables. Does not affect ordinary AI drones.");
            pickup = C.BindBool(section, "Can Pick Up Items", true, "Allow remote-operation drones to collect items. Equipment restrictions remain native.");
            ownDronePickups = C.BindBool(section, "Can Collect Own Drone Pickups", true,
                "Allow player-controlled remote-operation drones to collect the gold and healing pickups created by their own drone abilities. Does not affect AI drones.");
            benefits = C.BindBool(section, "Gain Benefits Of Items", true, "Apply held item effects while in remote-operation drone form. Disabling this suppresses item benefits without removing items.");
            respawns = C.Bind(section, "Number Of Respawns", -1, new ConfigDescription("Remote-operation drone spawns allowed per player per stage. -1 allows unlimited; 0 disables drone respawns.",
                new AcceptableValueRange<int>(-1, 1000000)));
            disconnectCooldown = C.Bind(section, "Disconnect Cooldown (s)", 180f,
                new ConfigDescription("Shared cooldown for Disconnect across every player-controlled drone.", new AcceptableValueRange<float>(0f, 1000000f)));
            deathUsesDisconnectCooldown = C.BindBool(section, "Drone Death Respects Disconnect Cooldown", true,
                "Prevent a player from controlling another drone after their drone dies until the shared Disconnect cooldown finishes.");
            if (!enabled.Value) return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.MultiplayerDrones");
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), "SpawnRemoteOperationDrone"),
                prefix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(BeforeSpawn)),
                postfix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(AfterSpawn)));
            harmony.Patch(AccessTools.Method(typeof(GenericPickupController), "BodyHasPickupPermission"),
                prefix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(BeforePickup)));
            harmony.Patch(AccessTools.Method(typeof(Interactor), "AttemptInteraction", new[]{typeof(GameObject)}),
                prefix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(BeforeInteraction)));
            harmony.Patch(AccessTools.Method(typeof(Interactor), "PerformInteraction", new[]{typeof(GameObject)}),
                prefix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(BeforeInteraction)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "GetInteractability", new[]{typeof(Interactor)}),
                prefix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(BeforePurchaseCheck)));
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), "OnBodyDeath", new[]{typeof(CharacterBody)}),
                postfix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(AfterBodyDeath)));
            harmony.Patch(AccessTools.Method(typeof(RemoteOperationDronePickerPanel), nameof(RemoteOperationDronePickerPanel.SetUser)),
                postfix: new HarmonyMethod(typeof(MultiplayerDrones), nameof(AfterPickerSetUser)));
            CharacterBody.onBodyStartGlobal += BodyStarted;
        }
        internal static bool CanCollectOwnDronePickups => ownDronePickups == null || C.Get(ownDronePickups);
        private static bool Active => enabled != null && C.Get(enabled);
        private static bool Drone(CharacterBody body)
        {
            if (!body) return false;
            return body.isRemoteOp && body.master && body.master.playerCharacterMasterController;
        }
        internal static bool HasSpawns(int used, int maximum) => maximum < 0 || used < maximum;
        private static Count Counter(CharacterMaster master)
        {
            Count count = counts.GetOrCreateValue(master);
            if (count.Stage != Stage.instance) { count.Stage = Stage.instance; count.Used = 0; }
            return count;
        }
        internal static float DisconnectCooldownSeconds => disconnectCooldown == null ? 300f : Mathf.Max(0f, C.Get(disconnectCooldown));
        internal static void BeginDisconnectCooldown(CharacterMaster master)
        {
            if (!master || !Run.instance) return;
            DisconnectState state = disconnectStates.GetOrCreateValue(master);
            state.Finish = Run.FixedTimeStamp.now + DisconnectCooldownSeconds;
            // Disconnect itself is allowed to replace the current drone once. The
            // shared timer applies to the replacement drone and any later death.
            state.AllowNextSpawn = true;
            state.WaitingAfterDeath = false;
        }
        // Each successful remote-drone spawn begins a fresh shared Disconnect cooldown,
        // including the first drone selected after a normal survivor death.
        internal static void BeginSpawnDisconnectCooldown(CharacterMaster master)
        {
            if (!master || !Run.instance) return;
            DisconnectState state = disconnectStates.GetOrCreateValue(master);
            state.Finish = Run.FixedTimeStamp.now + DisconnectCooldownSeconds;
            state.AllowNextSpawn = false;
            state.WaitingAfterDeath = false;
        }
        internal static void CancelDisconnectSelection(CharacterMaster master)
        {
            if (!master) return;
            DisconnectState state = disconnectStates.GetOrCreateValue(master);
            state.AllowNextSpawn = false;
            state.WaitingAfterDeath = false;
        }
        internal static bool DisconnectCooldownActive(CharacterMaster master)
        {
            return master && Run.instance && disconnectStates.TryGetValue(master, out DisconnectState state) &&
                state.Finish > Run.FixedTimeStamp.now;
        }
        internal static void ApplyDisconnectCooldown(CharacterMaster master, Inventory inventory, EquipmentLocation location, EquipmentIndex equipment)
        {
            if (!master || !inventory || !DisconnectCooldownActive(master) ||
                !disconnectStates.TryGetValue(master, out DisconnectState state)) return;
            var replacement = new EquipmentState(equipment, state.Finish, 0);
            inventory.SetEquipment(in replacement, in location);
        }
        internal static bool ReleaseDisconnectDeathWait(CharacterMaster master)
        {
            if (!master || !disconnectStates.TryGetValue(master, out DisconnectState state) || !state.WaitingAfterDeath) return true;
            if (DisconnectCooldownActive(master)) return false;
            state.WaitingAfterDeath = false;
            if (NetworkServer.active && HasSpawns(Counter(master).Used, C.Get(respawns)))
                preventRespawn?.SetValue(master, false);
            return true;
        }
        private static bool BeforeSpawn(CharacterMaster __instance, string droneBodyToSpawn, out CharacterBody __state)
        {
            __state = __instance.GetBody();
            if (!Active || !NetworkServer.active || !__instance.playerCharacterMasterController) return true;
            if (!Run.instance || !HasSpawns(Counter(__instance).Used, C.Get(respawns))) return false;
            if (__state && __state.healthComponent && __state.healthComponent.alive &&
                !RemoteOperationDrone.DebugSpawnInProgress) return false;
            BodyIndex index = BodyCatalog.FindBodyIndex(droneBodyToSpawn);
            DroneIndex drone = DroneCatalog.GetDroneIndexFromBodyIndex(index);
            if (drone == DroneIndex.None || DroneCatalog.GetRemoteOpBodyIndexFromDroneIndex(drone) != index) return false;
            if (!RemoteOperationDrone.DebugSpawnInProgress && deathUsesDisconnectCooldown != null &&
                C.Get(deathUsesDisconnectCooldown) && DisconnectCooldownActive(__instance))
            {
                DisconnectState state = disconnectStates.GetOrCreateValue(__instance);
                if (!state.AllowNextSpawn) return false;
            }
            ReleaseDisconnectDeathWait(__instance);
            return true;
        }
        private static void AfterSpawn(CharacterMaster __instance, CharacterBody __state)
        {
            if (!Active || !NetworkServer.active || !__instance.playerCharacterMasterController) return;
            CharacterBody body = __instance.GetBody();
            if (!body || body == __state || !Drone(body)) return;
            Count count = Counter(__instance); count.Used++;
            DisconnectState disconnectState = disconnectStates.GetOrCreateValue(__instance);
            disconnectState.AllowNextSpawn = false;
            disconnectState.WaitingAfterDeath = false;
            BeginSpawnDisconnectCooldown(__instance);
            preventRespawn?.SetValue(__instance, !HasSpawns(count.Used, C.Get(respawns)));
        }
        private static void AfterBodyDeath(CharacterMaster __instance, CharacterBody body)
        {
            if (!Active || !NetworkServer.active || !__instance.playerCharacterMasterController ||
                body == null || !body.isRemoteOp || !Run.instance) return;
            DisconnectState disconnectState = disconnectStates.GetOrCreateValue(__instance);
            if (deathUsesDisconnectCooldown != null && C.Get(deathUsesDisconnectCooldown) &&
                DisconnectCooldownActive(__instance) && !disconnectState.AllowNextSpawn)
            {
                disconnectState.WaitingAfterDeath = true;
            }
            // Vanilla blocks a second remote-operation spawn until the next stage.
            // Keep that block only after the configured quota has been used.
            preventRespawn?.SetValue(__instance, !HasSpawns(Counter(__instance).Used, C.Get(respawns)));
        }
        internal static void MarkManualDisconnectPicker(GameObject picker)
        {
            if (picker && !picker.GetComponent<ManualDisconnectPicker>()) picker.AddComponent<ManualDisconnectPicker>();
        }
        private static void AfterPickerSetUser(RemoteOperationDronePickerPanel __instance, LocalUser __0)
        {
            if (!Active || !__instance || __instance.GetComponent<ManualDisconnectPicker>() || __0 == null || !__0.cachedMaster ||
                deathUsesDisconnectCooldown == null || !C.Get(deathUsesDisconnectCooldown) ||
                !RemoteOperation.RemoteDroneDisconnect.EquipmentCooldownActive(__0.cachedMaster)) return;
            CooldownPicker gate = __instance.GetComponent<CooldownPicker>();
            if (!gate) gate = __instance.gameObject.AddComponent<CooldownPicker>();
            gate.Initialize(__0.cachedMaster, __instance.buttonContainer);
        }
        private sealed class ManualDisconnectPicker : MonoBehaviour { }
        private sealed class CooldownPicker : MonoBehaviour
        {
            private CharacterMaster master;
            private MPButton[] buttons;
            private bool[] interactable;
            internal void Initialize(CharacterMaster value, RectTransform buttonContainer)
            {
                master = value;
                buttons = buttonContainer ? buttonContainer.GetComponentsInChildren<MPButton>(true) : Array.Empty<MPButton>();
                interactable = new bool[buttons.Length];
                for (int i = 0; i < buttons.Length; i++)
                {
                    interactable[i] = buttons[i] && buttons[i].interactable;
                    if (buttons[i]) buttons[i].interactable = false;
                }
            }
            private void Update()
            {
                if (master && RemoteOperation.RemoteDroneDisconnect.EquipmentCooldownActive(master)) return;
                if (buttons != null)
                    for (int i = 0; i < buttons.Length; i++)
                        if (buttons[i]) buttons[i].interactable = interactable[i];
                Destroy(this);
            }
        }
        private static bool BeforePickup(CharacterBody body, UniquePickup pickupState, ref bool __result)
        {
            if (!Active || !Drone(body)) return true;
            PickupDef item = PickupCatalog.GetPickupDef(pickupState.pickupIndex);
            if (item == null || item.itemIndex == ItemIndex.None) return true;
            __result = C.Get(pickup) && body.inventory && body.master && body.master.playerCharacterMasterController;
            return false;
        }
        private static bool BeforeInteraction(Interactor __instance)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (Active && Drone(body)) __instance.isRemoteOp = !C.Get(interact);
            return !Active || !Drone(body) || C.Get(interact);
        }
        private static void BeforePurchaseCheck(PurchaseInteraction __instance, Interactor activator)
        {
            if (Active && activator && Drone(activator.GetComponent<CharacterBody>()))
                activator.isRemoteOp = !C.Get(interact);
        }
        private static void BodyStarted(CharacterBody body)
        {
            if (Active && Drone(body) && !body.GetComponent<Settings>())
            {
                var interactor = body.GetComponent<Interactor>();
                if (interactor) interactor.isRemoteOp = !C.Get(interact);
                body.gameObject.AddComponent<Settings>();
            }
        }
        private sealed class Settings : MonoBehaviour
        {
            private CharacterBody body;
            private Inventory inventory;
            private bool disabledByUs, hadIgnoreItems, applied;
            private void Awake() { body = GetComponent<CharacterBody>(); hadIgnoreItems = (body.bodyFlags & CharacterBody.BodyFlags.IgnoreItemUpdates) != 0; }
            private void FixedUpdate()
            {
                if (!body || !Drone(body)) return;
                bool active = Active;
                if (body.TryGetComponent<Interactor>(out var interactor)) interactor.isRemoteOp = active ? !C.Get(interact) : body.isRemoteOp;
                bool useItems = !active || C.Get(benefits);
                // Keep buff/debuff timers running; an owned inventory disabler
                // suppresses item effects without freezing all body item/buff updates.
                bool ignore = active ? false : hadIgnoreItems;
                if (((body.bodyFlags & CharacterBody.BodyFlags.IgnoreItemUpdates) != 0) != ignore)
                {
                    if (ignore) body.bodyFlags |= CharacterBody.BodyFlags.IgnoreItemUpdates;
                    else body.bodyFlags &= ~CharacterBody.BodyFlags.IgnoreItemUpdates;
                    body.MarkAllStatsDirty();
                }
                if (NetworkServer.active && body.inventory)
                {
                    if (inventory && inventory != body.inventory) RestoreInventory();
                    inventory = body.inventory;
                    if (!useItems && !disabledByUs) { inventory.AddInventoryDisabler(); disabledByUs = true; }
                    else if (useItems && disabledByUs) RestoreInventory();
                }
                applied = true;
            }
            private void RestoreInventory()
            {
                if (disabledByUs && inventory && NetworkServer.active) inventory.RemoveInventoryDisabler();
                disabledByUs = false;
            }
            private void OnDestroy()
            {
                RestoreInventory();
                if (applied && body)
                {
                    if (hadIgnoreItems) body.bodyFlags |= CharacterBody.BodyFlags.IgnoreItemUpdates;
                    else body.bodyFlags &= ~CharacterBody.BodyFlags.IgnoreItemUpdates;
                }
            }
        }
    }
}
