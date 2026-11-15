using System.Collections;
using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using C = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class RemoteOperationDrone
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<KeyboardShortcut> shortcut;
        private static Run debugRun;
        private static bool loggedPrevention;
        private static bool ownsGameOverProtection;
        private static bool awaitingConfirmation;
        private static bool droneSpawned;
        private static GameObject activePicker;
        private static ShortcutListener listener;
        private static readonly FieldInfo dronePool = AccessTools.Field(typeof(RemoteOperationDronePickerPanel), "dronePool");
        private static readonly FieldInfo preventGameOver = AccessTools.Field(typeof(Run), "<preventGameOver>k__BackingField");
        internal static bool DebugSpawnInProgress { get; private set; }

        private static void ProtectRun(Run run)
        {
            if (!run) return;
            if (debugRun && debugRun != run) ReleaseRun();
            debugRun = run;
            if (!run.preventGameOver && preventGameOver != null)
            {
                preventGameOver.SetValue(run, true);
                ownsGameOverProtection = true;
            }
        }

        private static void ReleaseRun()
        {
            if (ownsGameOverProtection && debugRun && preventGameOver != null)
                preventGameOver.SetValue(debugRun, false);
            debugRun = null;
            ownsGameOverProtection = false;
            awaitingConfirmation = false;
            droneSpawned = false;
            loggedPrevention = false;
        }

        internal static void Init()
        {
            const string section = "Debug - Remote Operation Drone";
            enabled = C.BindBool(section, "Spawn Drone On Shortcut", true,
                "Open the remote-operation drone selection menu in a single-player run when the shortcut is pressed.", clientSide: true);
            shortcut = C.Bind(section, "Spawn Drone Shortcut", new KeyboardShortcut(KeyCode.F7),
                "Shortcut used to choose a remote-operation drone for testing.", clientSide: true);
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.RemoteOperationDrone");
            harmony.Patch(
                AccessTools.Method(typeof(Run), nameof(Run.BeginGameOver)),
                prefix: new HarmonyMethod(typeof(RemoteOperationDrone), nameof(BeforeGameOver)));
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), nameof(CharacterMaster.PushBodyHandlingConfig)),
                prefix: new HarmonyMethod(typeof(RemoteOperationDrone), nameof(BeforeBodyConfig)));
            harmony.Patch(AccessTools.Method(typeof(RemoteOperationDronePickerPanel), "OnCreateButton"),
                postfix: new HarmonyMethod(typeof(RemoteOperationDrone), nameof(AfterCreateButton)));
            harmony.Patch(AccessTools.Method(typeof(RemoteOperationDronePickerPanel), nameof(RemoteOperationDronePickerPanel.SetUser)),
                postfix: new HarmonyMethod(typeof(RemoteOperationDrone), nameof(AfterSetUser)));
            Run.onRunDestroyGlobal += run => { if (debugRun == run) ReleaseRun(); };
            var root = new GameObject("3Qs Remote Operation Drone Shortcut");
            Object.DontDestroyOnLoad(root);
            listener = root.AddComponent<ShortcutListener>();
        }

        private static bool BeforeGameOver(Run __instance, GameEndingDef gameEndingDef)
        {
            if (!NetworkServer.active || __instance != debugRun || gameEndingDef == null)
                return true;
            if (gameEndingDef.isWin)
            {
                ReleaseRun();
                return true;
            }
            if (!loggedPrevention)
            {
                loggedPrevention = true;
                ThreeQsVanillaTweaks.Log.Info("Debug: suppressing game over during remote-operation drone testing.");
            }
            return false;
        }

        [HarmonyPriority(Priority.Last)]
        private static void BeforeBodyConfig(CharacterMaster __instance, BodyHandlingConfig handlingConfig)
        {
            if (!DebugSpawnInProgress || !__instance || !__instance.playerCharacterMasterController ||
                handlingConfig == null)
                return;

            // The remote-operation preset normally does not count as a living player.
            // A debug drone must count so the stage continues to run after the handoff.
            handlingConfig[BodyHandlingOption.BodyPreventsGameOver] = true;
        }

        private sealed class DebugPicker : MonoBehaviour { }

        private static void AfterCreateButton(RemoteOperationDronePickerPanel __instance, int index, MPButton button)
        {
            if (!__instance.GetComponent<DebugPicker>() || !button) return;
            var pool = dronePool?.GetValue(__instance) as DroneIndex[];
            if (pool == null || index < 0 || index >= pool.Length) return;
            DroneDef drone = DroneCatalog.GetDroneDef(pool[index]);
            button.onClick.RemoveAllListeners();
            if (drone && drone.remoteOpBody)
                button.onClick.AddListener(() => SelectDrone(__instance, drone));
        }

        private static void AfterSetUser(RemoteOperationDronePickerPanel __instance)
        {
            if (!__instance.GetComponent<DebugPicker>() || !__instance.buttonContainer) return;
            foreach (MPButton button in __instance.buttonContainer.GetComponentsInChildren<MPButton>(true))
            {
                button.interactable = true;
                button.disableGamepadClick = false;
                if (button.image) button.image.color = Color.white;
                var locator = button.GetComponent<ChildLocator>();
                Transform icon = locator ? locator.FindChild("Icon") : null;
                Transform label = locator ? locator.FindChild("Text") : null;
                if (icon && icon.TryGetComponent<RawImage>(out var image)) image.color = Color.white;
                if (label && label.TryGetComponent<TMP_Text>(out var text)) text.text = "Free";
            }
        }


        private static void SelectDrone(RemoteOperationDronePickerPanel panel, DroneDef drone)
        {
            if (!panel || !drone || !drone.remoteOpBody || !Run.instance || !NetworkServer.active || !listener) return;
            LocalUser local = LocalUserManager.GetFirstLocalUser();
            CharacterBody body = local == null ? null : local.cachedBody;
            CharacterMaster master = body ? body.master : null;
            if (!master) return;
            string survivor = BodyCatalog.GetBodyName(body.bodyIndex);

            DebugSpawnInProgress = true;
            ProtectRun(Run.instance);
            awaitingConfirmation = true;
            loggedPrevention = false;
            try
            {
                master.SpawnRemoteOperationDrone(drone.remoteOpBody.name, 0u);
                listener.StartCoroutine(listener.ConfirmSpawn(master, body, drone.remoteOpBody.name, debugRun));
                Object.Destroy(panel.gameObject);
                activePicker = null;
            }
            catch (Exception error)
            {
                ThreeQsVanillaTweaks.Log.Warning("Debug: remote-operation drone selection failed: " + error);
                awaitingConfirmation = false;
                if (master && !string.IsNullOrEmpty(survivor))
                {
                    try
                    {
                        master.Respawn(survivor);
                        CharacterBody restored = master.GetBody();
                        if (restored && !restored.isRemoteOp) ReleaseRun();
                    }
                    catch (Exception recoveryError)
                    {
                        ThreeQsVanillaTweaks.Log.Warning("Debug: could not restore the survivor after failed drone spawn: " + recoveryError);
                    }
                }
            }
            finally { DebugSpawnInProgress = false; }
        }

        private sealed class ShortcutListener : MonoBehaviour
        {
            private void Update()
            {
                if (debugRun && !awaitingConfirmation)
                {
                    LocalUser current = LocalUserManager.GetFirstLocalUser();
                    CharacterBody currentBody = current == null ? null : current.cachedBody;
                    if (currentBody && !currentBody.isRemoteOp &&
                        (droneSpawned || !activePicker))
                        ReleaseRun();
                }
                if (!C.DebugOptionsEnabled || !C.Get(RemoteOperationDrone.enabled) || !C.Get(shortcut).IsDown() ||
                    !NetworkServer.active || Run.instance == null || NetworkUser.readOnlyInstancesList.Count != 1)
                    return;

                LocalUser local = LocalUserManager.GetFirstLocalUser();
                CharacterBody body = local == null ? null : local.cachedBody;
                CharacterMaster master = body ? body.master : null;
                if (!master || !master.playerCharacterMasterController) return;

                if (activePicker)
                {
                    Object.Destroy(activePicker);
                    activePicker = null;
                    if (!droneSpawned) ReleaseRun();
                    return;
                }
                if (Run.instance.availableRemoteOpDroneList.Count == 0)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Debug: no remote-operation drone is available in this run.");
                    return;
                }
                HUD hud = null;
                foreach (HUD candidate in HUD.readOnlyInstanceList)
                    if (candidate && candidate.localUserViewer == local) { hud = candidate; break; }
                if (!hud) return;

                GameObject prefab = Addressables.LoadAssetAsync<GameObject>(
                    RemoteOperationDronePickerPanel.RemoteOperationPrefabPath).WaitForCompletion();
                if (!prefab) return;
                ProtectRun(Run.instance);
                GameObject picker = Object.Instantiate(prefab, hud.transform);
                picker.AddComponent<DebugPicker>();
                picker.GetComponent<MPEventSystemProvider>().eventSystem = local.eventSystem;
                picker.GetComponent<RemoteOperationDronePickerPanel>().SetUser(local);
                activePicker = picker;
            }

            internal IEnumerator ConfirmSpawn(CharacterMaster master, CharacterBody oldBody, string droneName, Run requestedRun)
            {
                // The replacement body is created after SpawnRemoteOperationDrone returns.
                // Keep game-over suppression active while the player has no living body.
                for (int i = 0; i < 100; i++)
                {
                    if (debugRun != requestedRun || !requestedRun) yield break;
                    CharacterBody body = master ? master.GetBody() : null;
                    if (body && body != oldBody && body.isRemoteOp)
                    {
                        awaitingConfirmation = false;
                        droneSpawned = true;
                        ThreeQsVanillaTweaks.Log.Info("Debug: spawned remote-operation drone " + droneName + ".");
                        yield break;
                    }
                    yield return new WaitForSeconds(.1f);
                }
                awaitingConfirmation = false;
                CharacterBody current = master ? master.GetBody() : null;
                ThreeQsVanillaTweaks.Log.Warning("Debug: remote-operation drone did not replace the current body within 10 seconds.");
                if (current && current.healthComponent && current.healthComponent.alive && !current.isRemoteOp)
                    ReleaseRun();
            }
        }
    }
}
