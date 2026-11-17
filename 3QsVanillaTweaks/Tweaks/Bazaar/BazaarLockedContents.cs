using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    // Locked Bazaar interactables keep their offered item out of view until their slot is unlocked.
    internal static partial class BazaarAlternateMode
    {
        private static ConfigEntry<bool> hideLockedContents;

        private sealed class LockedTerminal
        {
            // Lunar Shop slots stay empty and roll on unlock; other terminals keep their item hidden.
            internal bool Reroll;
            internal bool WasHidden;
        }

        private static readonly Dictionary<ShopTerminalBehavior, LockedTerminal> lockedTerminals =
            new Dictionary<ShopTerminalBehavior, LockedTerminal>();

        private static void BindLockedContents(Harmony harmony)
        {
            hideLockedContents = C.BindBool(ModeSettings, "Hide Locked Contents", true,
                "Locked Lunar Shop slots stay empty and roll an item when unlocked. Locked printers, cauldrons and Hallowed Pools show a hidden item, and locked Lunar Seers show no stage, until unlocked.");
            // Every terminal pickup change, including rerolls, passes through SetPickup.
            harmony.Patch(A.Method(typeof(ShopTerminalBehavior), "SetPickup", new[] { typeof(UniquePickup), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(KeepLockedContentsHidden)));
            // Vanilla only redraws a terminal when its pickup changes. Redraw when it is un-hidden too.
            harmony.Patch(A.Method(typeof(ShopTerminalBehavior), "OnSyncHidden"),
                postfix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(AfterHiddenSync)));
            // The seer's stage preview is applied locally; keep it in step with each player's lock state.
            RoR2Application.onUpdate += SeerTick;
        }

        private static void KeepLockedContentsHidden(ShopTerminalBehavior __instance, ref UniquePickup newPickup, ref bool newHidden)
        {
            if (!NetworkServer.active || lockedTerminals.Count == 0 || !lockedTerminals.TryGetValue(__instance, out var locked)) return;
            if (locked.Reroll)
            {
                newPickup = UniquePickup.none;
                newHidden = false;
            }
            else newHidden = true;
        }

        private static readonly System.Reflection.MethodInfo redrawTerminal =
            A.Method(typeof(ShopTerminalBehavior), "UpdatePickupDisplayAndAnimations");

        // Runs on the host and on clients when the synced hidden flag changes.
        private static void AfterHiddenSync(ShopTerminalBehavior __instance)
        {
            if (!NetworkClient.active || !__instance || redrawTerminal == null || !BazaarSpawnHelper.InBazaar()) return;
            redrawTerminal.Invoke(__instance, null);
        }

        private static bool AnyUnlocked(Slot slot)
        {
            foreach (var user in NetworkUser.readOnlyInstancesList)
                if (user && IsUnlocked(slot, user)) return true;
            return false;
        }

        private static void HideContents(Gate gate)
        {
            if (!NetworkServer.active || !C.Get(hideLockedContents) || !gate.Target || AnyUnlocked(gate.Slot)) return;
            bool shopSlot = gate.Slot.Name.StartsWith(LunarShopSlotNamePrefix, StringComparison.Ordinal);
            foreach (var terminal in gate.Target.GetComponentsInChildren<ShopTerminalBehavior>(true))
            {
                if (lockedTerminals.ContainsKey(terminal)) continue;
                lockedTerminals.Add(terminal, new LockedTerminal { Reroll = shopSlot, WasHidden = terminal.pickupIndexIsHidden });
                // Reapply the current pickup so the prefix empties or hides it now. A terminal
                // that has not started yet is caught when Start rolls its first pickup.
                terminal.SetPickup(terminal.CurrentPickup(), terminal.pickupIndexIsHidden);
            }
        }

        private static void RevealContents(Gate gate)
        {
            if (!NetworkServer.active || !gate.Target) return;
            foreach (var terminal in gate.Target.GetComponentsInChildren<ShopTerminalBehavior>(true))
            {
                if (!lockedTerminals.TryGetValue(terminal, out var locked)) continue;
                lockedTerminals.Remove(terminal);
                if (locked.Reroll) terminal.GenerateNewPickupServer(false);
                else terminal.SetHidden(locked.WasHidden);
            }
        }

        // Lunar Seers: the target scene stays synced (the layout reads it to avoid duplicates);
        // only each player's own preview is withheld while the seer is locked for them.
        private const string SeerPrefabPath = "RoR2/Base/bazaar/SeerStation.prefab";
        private static readonly List<SeerStationController> seers = new List<SeerStationController>();
        private static readonly List<Material> seerMaterials = new List<Material>();
        private static Material hiddenSeerMaterial;
        private static bool hiddenSeerResolved;
        private static Stage seerStage;
        private static float nextSeerCheck, seerRescanUntil;

        // The prefab's own material in the preview slot never shows a stage.
        private static Material HiddenSeerMaterial()
        {
            if (hiddenSeerResolved) return hiddenSeerMaterial;
            hiddenSeerResolved = true;
            try
            {
                var prefab = Stages.StageFeatureSpawnHelper.LoadPrefab(SeerPrefabPath);
                var template = prefab ? prefab.GetComponentInChildren<SeerStationController>(true) : null;
                if (template && template.targetRenderer) hiddenSeerMaterial = SeerMaterial(template);
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Bazaar seers: could not load the seer prefab: " + e.Message); }
            if (!hiddenSeerMaterial)
                ThreeQsVanillaTweaks.Log.Warning("Bazaar seers: no neutral seer material found; locked seers keep their stage preview.");
            return hiddenSeerMaterial;
        }

        private static void SeerTick()
        {
            if (Time.unscaledTime < nextSeerCheck) return;
            nextSeerCheck = Time.unscaledTime + .25f;
            RefreshSeers();
        }

        private static bool SeerLocked(SeerStationController seer)
        {
            if (hideLockedContents == null || !C.Get(hideLockedContents) || !BazaarSpawnHelper.InBazaar()) return false;
            var user = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            if (!user) return false;
            if (NetworkServer.active)
            {
                var gate = FindGate(seer);
                return gate != null && !IsUnlocked(gate.Slot, user);
            }
            return TryGetState(seer, out var state) && !state.Allows(user);
        }

        private static void RefreshSeers()
        {
            if (!BazaarSpawnHelper.InBazaar())
            {
                seers.Clear();
                seerStage = null;
                return;
            }
            // Seers exist from scene load; the extra seer is spawned a frame later, so rescan briefly.
            if (seerStage != Stage.instance)
            {
                seerStage = Stage.instance;
                seerRescanUntil = Time.unscaledTime + 5f;
            }
            if (Time.unscaledTime < seerRescanUntil || seers.Count == 0)
            {
                seers.Clear();
                seers.AddRange(UnityEngine.Object.FindObjectsOfType<SeerStationController>());
            }
            seers.RemoveAll(seer => !seer);
            Material hidden = null;
            foreach (var seer in seers)
            {
                if (!seer.targetRenderer) continue;
                Material desired;
                if (SeerLocked(seer))
                {
                    if (!hidden) hidden = HiddenSeerMaterial();
                    if (!hidden) continue;
                    desired = hidden;
                }
                else
                {
                    int index = seer.ReturnTargetSceneDefIndex();
                    SceneDef scene = (uint)index < SceneCatalog.sceneDefCount ? SceneCatalog.GetSceneDef((SceneIndex)index) : null;
                    // Same field vanilla's OnTargetSceneChanged reads.
#pragma warning disable CS0618
                    desired = scene ? scene.portalMaterial : null;
#pragma warning restore CS0618
                }
                if (SeerMaterial(seer) != desired) SetSeerMaterial(seer, desired);
            }
        }

        private static Material SeerMaterial(SeerStationController seer)
        {
            seer.targetRenderer.GetSharedMaterials(seerMaterials);
            var material = seer.materialIndexToAssign >= 0 && seer.materialIndexToAssign < seerMaterials.Count
                ? seerMaterials[seer.materialIndexToAssign] : null;
            seerMaterials.Clear();
            return material;
        }

        // Mirrors SeerStationController.SetPortalMaterial.
        private static void SetSeerMaterial(SeerStationController seer, Material material)
        {
            seer.targetRenderer.GetSharedMaterials(seerMaterials);
            if (seer.materialIndexToAssign >= 0 && seer.materialIndexToAssign < seerMaterials.Count)
            {
                seerMaterials[seer.materialIndexToAssign] = material;
                seer.targetRenderer.SetSharedMaterials(seerMaterials);
            }
            seerMaterials.Clear();
        }
    }
}
