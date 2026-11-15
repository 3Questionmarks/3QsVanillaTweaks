using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteDroneDisconnect
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> revivalWait;
        private static EquipmentDef disconnect;
        private static readonly ConditionalWeakTable<CharacterMaster, EquipmentBackup> backups = new ConditionalWeakTable<CharacterMaster, EquipmentBackup>();
        private static readonly HashSet<CharacterMaster> pending = new HashSet<CharacterMaster>();
        private static GameObject runnerObject;
        private static Watcher watcher;
        private static readonly List<UnityEngine.Object> iconAssets = new List<UnityEngine.Object>();
        private sealed class EquipmentBackup
        {
            internal bool Saved;
            internal EquipmentState State;
            internal EquipmentLocation Location;
        }

        internal static void Init()
        {
            enabled = C.BindStartupBool("Remote Operation - Disconnect", "Changes Enabled", true,
                "Give player-controlled drones a Disconnect equipment action. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            revivalWait = C.Bind("Remote Operation - Disconnect", "Revival Wait (s)", 3f,
                "Wait for a revival item to return you to your survivor before opening the drone selection menu.");
            ContentManager.collectContentPackProviders += add => add(new DisconnectContent());
            RoR2Application.onLoad += Install;
        }

        private sealed class DisconnectContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.RemoteDroneDisconnect";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                disconnect = ScriptableObject.CreateInstance<EquipmentDef>();
                disconnect.name = "3QSRemoteDroneDisconnect";
                disconnect.nameToken = "3QS_REMOTE_DRONE_DISCONNECT_NAME";
                disconnect.pickupToken = "3QS_REMOTE_DRONE_DISCONNECT_PICKUP";
                disconnect.descriptionToken = "3QS_REMOTE_DRONE_DISCONNECT_DESCRIPTION";
                disconnect.cooldown = Tweaks.Others.MultiplayerDrones.DisconnectCooldownSeconds;
                disconnect.canDrop = false;
                disconnect.canBeRandomlyTriggered = false;
                disconnect.enigmaCompatible = false;
                R2API.LanguageAPI.Add(disconnect.nameToken, "Disconnect");
                R2API.LanguageAPI.Add(disconnect.pickupToken, "Leave this drone and select another.");
                R2API.LanguageAPI.Add(disconnect.descriptionToken, "Leave remote operation and choose another drone. Revival items can return you to your survivor instead.");
                content.equipmentDefs.Add(new[] { disconnect });
                args.ReportProgress(1f); yield break;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
            { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }

        private static void Install()
        {
            if (!disconnect) return;
            disconnect.cooldown = Tweaks.Others.MultiplayerDrones.DisconnectCooldownSeconds;
            C.EffectiveSettingsChanged += ApplyCooldown;
            Sprite retool = RemoteDroneSkills.VanillaIcon("ToolbotBody", "Special", null);
            if (retool) ApplyDisconnectIcon(retool);
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteDroneDisconnect");
            harmony.Patch(AccessTools.Method(typeof(EquipmentSlot), "PerformEquipmentAction"),
                prefix: new HarmonyMethod(typeof(RemoteDroneDisconnect), nameof(BeforeEquipment)));
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), nameof(CharacterMaster.PushBodyHandlingConfig)),
                prefix: new HarmonyMethod(typeof(RemoteDroneDisconnect), nameof(BeforeBodyConfig)));
            harmony.Patch(AccessTools.Method(typeof(Run), nameof(Run.BeginGameOver)),
                prefix: new HarmonyMethod(typeof(RemoteDroneDisconnect), nameof(BeforeGameOver)));
            harmony.Patch(AccessTools.Method(typeof(CharacterMaster), nameof(CharacterMaster.SpawnRemoteOperationDrone)),
                postfix: new HarmonyMethod(typeof(RemoteDroneDisconnect), nameof(AfterSpawn)));
            CharacterBody.onBodyStartGlobal += BodyStarted;
            Run.onRunDestroyGlobal += run => pending.Clear();
            runnerObject = new GameObject("3Qs Remote Drone Disconnect");
            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            watcher = runnerObject.AddComponent<Watcher>();
        }

        private static void ApplyCooldown()
        {
            if (disconnect) disconnect.cooldown = Tweaks.Others.MultiplayerDrones.DisconnectCooldownSeconds;
        }

        private static void ApplyDisconnectIcon(Sprite source)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            try
            {
                Rect rect = source.textureRect;
                temporary = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(source.texture, temporary);
                RenderTexture.active = temporary;
                Texture2D texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = pixels[i];
                    Color.RGBToHSV(pixel, out _, out float saturation, out float value);
                    if (pixel.a > 0f && saturation > .12f)
                    {
                        Color changed = Color.HSVToRGB(.52f, Mathf.Max(.45f, saturation), value);
                        changed.a = pixel.a; pixels[i] = changed;
                    }
                }
                texture.SetPixels(pixels); texture.Apply(); texture.name = "3QSDisconnectRetoolIcon";
                Sprite replacement = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(.5f, .5f), source.pixelsPerUnit);
                replacement.name = texture.name;
                iconAssets.Add(texture); iconAssets.Add(replacement);
                FieldInfo spriteField = AccessTools.Field(typeof(EquipmentDef), "pickupIconSprite");
                spriteField?.SetValue(disconnect, replacement);
                FieldInfo referenceField = AccessTools.Field(typeof(EquipmentDef), "pickupIconSpriteReference");
                if (referenceField != null)
                    referenceField.SetValue(disconnect, Activator.CreateInstance(referenceField.FieldType, new object[] { "" }));
                PickupDef pickup = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(disconnect.equipmentIndex));
                if (pickup != null)
                {
                    AccessTools.Field(typeof(PickupDef), "iconTexture")?.SetValue(pickup, texture);
                    AccessTools.Field(typeof(PickupDef), "iconSprite")?.SetValue(pickup, replacement);
                }
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Disconnect icon could not be recoloured: " + error.Message); }
            finally { RenderTexture.active = previous; if (temporary) RenderTexture.ReleaseTemporary(temporary); }
        }

        private static void BodyStarted(CharacterBody body)
        {
            if (!NetworkServer.active || !body || !body.master || !body.master.playerCharacterMasterController || !body.inventory) return;
            EquipmentBackup backup = backups.GetOrCreateValue(body.master);
            if (body.isRemoteOp)
            {
                pending.Remove(body.master);
                // The remote-operation preset disables equipment on the shared inventory.
                // Ensure the action remains usable even if another body-config patch ran first.
                body.inventory.SetEquipmentDisabled(false);
                if (!backup.Saved)
                {
                    backup.Location = body.inventory.activeEquipmentLocation;
                    backup.State = body.inventory.GetEquipment(in backup.Location);
                    backup.Saved = true;
                }
                body.inventory.SetEquipmentIndex(disconnect.equipmentIndex, backup.Location);
                Tweaks.Others.MultiplayerDrones.ApplyDisconnectCooldown(body.master, body.inventory, backup.Location, disconnect.equipmentIndex);
            }
            else if (backup.Saved)
            {
                body.inventory.SetEquipment(in backup.State, in backup.Location);
                backup.Saved = false;
                pending.Remove(body.master);
            }
        }

        private static void BeforeBodyConfig(CharacterMaster __instance, BodyHandlingConfig handlingConfig)
        {
            if (__instance && __instance.playerCharacterMasterController && handlingConfig != null &&
                (handlingConfig.currentPreset == BodyHandlingPresetIndex.RemoteOperation ||
                 handlingConfig.bodyIndex != BodyIndex.None &&
                 DroneCatalog.GetRemoteOpBodyIndexFromDroneIndex(
                     DroneCatalog.GetDroneIndexFromBodyIndex(handlingConfig.bodyIndex)) == handlingConfig.bodyIndex))
                handlingConfig[BodyHandlingOption.DisableEquipment] = false;
        }

        private static bool BeforeEquipment(EquipmentSlot __instance, EquipmentDef equipmentDef, ref bool __result)
        {
            if (equipmentDef != disconnect) return true;
            CharacterBody body = __instance.characterBody;
            if (!NetworkServer.active || !body || !body.isRemoteOp || !body.master) { __result = false; return false; }
            if (Tweaks.Others.MultiplayerDrones.DisconnectCooldownActive(body.master)) { __result = false; return false; }
            Tweaks.Others.MultiplayerDrones.BeginDisconnectCooldown(body.master);
            pending.Add(body.master);
            watcher.Begin(body);
            watcher.StartCoroutine(KillNextFrame(body));
            __result = true;
            return false;
        }

        private static IEnumerator KillNextFrame(CharacterBody body)
        {
            yield return null;
            if (body && body.healthComponent && body.healthComponent.alive)
                body.healthComponent.Suicide(); // Normal death path permits Dio and other revival effects.
        }

        private static bool BeforeGameOver(Run __instance, GameEndingDef gameEndingDef)
        {
            if (!NetworkServer.active || !gameEndingDef || gameEndingDef.isWin || pending.Count == 0) return true;
            foreach (CharacterMaster master in pending)
                if (master && master.playerCharacterMasterController && __instance == Run.instance) return false;
            return true;
        }

        private static void AfterSpawn(CharacterMaster __instance)
        {
            if (!NetworkServer.active || !__instance) return;
            pending.Remove(__instance);
            // BodyStarted may execute before the spawn postfix that starts the
            // shared cooldown. Refresh the equipment state after successful spawn
            // so the HUD immediately displays the entire countdown.
            CharacterBody body = __instance.GetBody();
            if (!body || !body.isRemoteOp || !body.inventory || !disconnect) return;
            EquipmentBackup backup = backups.GetOrCreateValue(__instance);
            if (backup.Saved)
                Tweaks.Others.MultiplayerDrones.ApplyDisconnectCooldown(
                    __instance, body.inventory, backup.Location, disconnect.equipmentIndex);
        }

        internal static bool EquipmentCooldownActive(CharacterMaster master)
        {
            if (!disconnect || !master || !master.inventory) return false;
            Inventory inventory = master.inventory;
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
            for (uint set = 0; set < inventory.equipmentSetCount; set++)
            {
                var location = new EquipmentLocation { slot = slot, set = set };
                EquipmentState state = inventory.GetEquipment(in location);
                if (state.equipmentIndex == disconnect.equipmentIndex && state.charges == 0 &&
                    state.chargeFinishTime > Run.FixedTimeStamp.now) return true;
            }
            return false;
        }

        private sealed class Watcher : MonoBehaviour
        {
            private LocalUser waitingUser;
            private CharacterMaster waitingMaster;
            private CharacterBody oldBody;
            private float pressedTime;
            private bool opened;
            private bool mayRevive;
            private GameObject activePicker;
            internal void Begin(CharacterBody body)
            {
                LocalUser local = LocalUserManager.GetFirstLocalUser();
                if (!body || local == null || local.cachedMaster != body.master) return;
                waitingUser = local; waitingMaster = body.master; oldBody = body; pressedTime = Time.unscaledTime; opened = false;
                mayRevive = body.inventory && Util.HasReviveItemOrEquipment(body.inventory);
            }
            private void Update()
            {
                if (!disconnect || !Run.instance)
                {
                    if (waitingMaster) Tweaks.Others.MultiplayerDrones.CancelDisconnectSelection(waitingMaster);
                    waitingUser = null; waitingMaster = null; opened = false; return;
                }
                LocalUser local = LocalUserManager.GetFirstLocalUser();
                CharacterBody body = local?.cachedBody;
                if (body && body.isRemoteOp && body.inventory && body.inventory.currentEquipmentIndex == disconnect.equipmentIndex &&
                    body.inputBank && body.inputBank.activateEquipment.justPressed &&
                    !Tweaks.Others.MultiplayerDrones.DisconnectCooldownActive(body.master))
                {
                    if (waitingUser == null) Begin(body);
                    // On a local host, also handle the input if the vanilla equipment
                    // slot does not execute while switching from a remote body.
                    if (NetworkServer.active && pending.Add(body.master))
                    {
                        Tweaks.Others.MultiplayerDrones.BeginDisconnectCooldown(body.master);
                        StartCoroutine(KillNextFrame(body));
                    }
                }
                if (waitingUser == null) return;
                CharacterBody current = waitingUser.cachedBody;
                if (current && current != oldBody && !current.isRemoteOp)
                {
                    Tweaks.Others.MultiplayerDrones.CancelDisconnectSelection(waitingMaster);
                    waitingUser = null; waitingMaster = null; return;
                } // Revived as a survivor.
                if (current && current != oldBody && current.isRemoteOp) { waitingUser = null; waitingMaster = null; return; } // Already switched.
                if (Time.unscaledTime - pressedTime < (mayRevive ? Mathf.Max(.5f, C.Get(revivalWait)) : .5f) ||
                    opened || oldBody && oldBody.healthComponent && oldBody.healthComponent.alive) return;
                if (Run.instance.availableRemoteOpDroneList.Count == 0)
                {
                    Tweaks.Others.MultiplayerDrones.CancelDisconnectSelection(waitingMaster);
                    waitingUser = null; waitingMaster = null; return;
                }
                HUD hud = null;
                foreach (HUD candidate in HUD.readOnlyInstanceList)
                    if (candidate && candidate.localUserViewer == waitingUser) { hud = candidate; break; }
                if (!hud) return;
                if (hud.GetComponentInChildren<RemoteOperationDronePickerPanel>()) { opened = true; return; }
                GameObject prefab = Addressables.LoadAssetAsync<GameObject>(RemoteOperationDronePickerPanel.RemoteOperationPrefabPath).WaitForCompletion();
                if (!prefab) return;
                activePicker = UnityEngine.Object.Instantiate(prefab, hud.transform);
                Tweaks.Others.MultiplayerDrones.MarkManualDisconnectPicker(activePicker);
                activePicker.GetComponent<MPEventSystemProvider>().eventSystem = waitingUser.eventSystem;
                activePicker.GetComponent<RemoteOperationDronePickerPanel>().SetUser(waitingUser);
                opened = true;
            }
        }

    }
}
