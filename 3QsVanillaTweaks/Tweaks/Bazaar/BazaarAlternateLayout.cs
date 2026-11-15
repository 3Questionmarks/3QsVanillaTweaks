using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using R2API;
using RoR2;
using Tweaks.Stages;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using Log = ThreeQsVanillaTweaks.Log;

namespace Tweaks.Bazaar
{
    internal static class BazaarAlternateLayout
    {
        private const string UncommonCauldronPath = "RoR2/Base/LunarCauldrons/LunarCauldron, WhiteToGreen.prefab";
        private const string LegendaryCauldronPath = "RoR2/Base/LunarCauldrons/LunarCauldron, GreenToRed Variant.prefab";
        private const string SeerPath = "RoR2/Base/bazaar/SeerStation.prefab";
        private static BazaarSpawnHelper.Settings uncommon, legendary, seer;
        private static BazaarAlternateMode.Slot seer1, seer2, vanillaUncommon, vanillaLegendary, exitPortal;
        private static readonly BazaarAlternateMode.Slot[] shopSlots = new BazaarAlternateMode.Slot[5];
        private static ConfigEntry<int> seer1Cost, seer2Cost, uncommonCost, legendaryCost;
        private static GameObject seerPrefab;
        private static Stage lastStage;
        private static readonly MethodInfo unlockedBeforeLoop = A.Method(typeof(BazaarController), "IsUnlockedBeforeLooping");

        internal static void Bind()
        {
            uncommon = BazaarSpawnHelper.BindFormerUnlockOnly("Uncommon Cauldron 2", UncommonCauldronPath,
                -108.25f, -23.18f, -4.46f, -.05f, 0f, -1f, exactPose: "-108.25, -23.93, -4.46, 0, 132.8624, 0");
            legendary = BazaarSpawnHelper.BindFormerUnlockOnly("Legendary Cauldron 2", LegendaryCauldronPath,
                -100.24f, -23.31f, -5.78f, -.4f, 0f, -.92f, exactPose: "-100.49, -24.06, -6.53, 0, 183.498566, 0");
            seer = BazaarSpawnHelper.BindFormerUnlockOnly("Lunar Seer 3", SeerPath,
                -133.58f, -24.68f, -4.65f, .56f, 0f, -.83f, exactPose: "-131.83, -25.43, -7.64999962, 0, 145.992508, 0", chance: 50f);
            seer1 = BazaarAlternateMode.BindSlot("Lunar Seer 1", "Lunar Seer 1");
            seer2 = BazaarAlternateMode.BindSlot("Lunar Seer 2", "Lunar Seer 2");
            vanillaUncommon = BazaarAlternateMode.BindSlot("Uncommon Cauldron 1", "Uncommon Cauldron 1");
            vanillaLegendary = BazaarAlternateMode.BindSlot("Legendary Cauldron 1", "Legendary Cauldron 1");
            exitPortal = BazaarAlternateMode.BindSlot("Exit Portal", "Exit Portal");
            for (int i = 0; i < shopSlots.Length; i++)
            {
                string name = "Lunar Shop Slot " + (i + 1);
                shopSlots[i] = BazaarAlternateMode.BindSlot(name, name);
            }
            seer1Cost = BazaarUseCosts.BindCoinPrice("Bazaar - Lunar Seer 1", 1, "Lunar coins per use. Vanilla: 3.");
            seer2Cost = BazaarUseCosts.BindCoinPrice("Bazaar - Lunar Seer 2", 1, "Lunar coins per use. Vanilla: 3.");
            uncommonCost = BazaarUseCosts.BindSurcharge("Bazaar - Uncommon Cauldron");
            legendaryCost = BazaarUseCosts.BindSurcharge("Bazaar - 03 Legendary Cauldron");
            RoR2Application.onLoad += PrepareSeer;
            BazaarSpawnHelper.Register(typeof(BazaarAlternateLayout), "AlternateLayout", nameof(AfterStageStart));
        }

        private static void PrepareSeer()
        {
            try
            {
                var original = StageFeatureSpawnHelper.LoadPrefab(SeerPath);
                seerPrefab = PrefabAPI.InstantiateClone(original, "3Qs_ExtraBazaarSeer", true);
            }
            catch (Exception e)
            {
                Log.Warning("The extra Bazaar seer could not be prepared: " + e.Message);
            }
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || lastStage == __instance) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, Spawn));
        }

        private static void Spawn()
        {
            var bazaar = BazaarController.instance;
            var nativeSeers = bazaar && bazaar.seerStations != null ? bazaar.seerStations : Array.Empty<SeerStationController>();
            SpawnCauldron(uncommon);
            SpawnCauldron(legendary);
            SpawnSeer(bazaar, nativeSeers);
            if (bazaar) ApplyVanillaSettings(bazaar, nativeSeers);
        }

        private static void SpawnSeer(BazaarController bazaar, SeerStationController[] nativeSeers)
        {
            if (!BazaarSpawnHelper.Roll(seer) || !seerPrefab) return;
            var existing = Array.Find(nativeSeers, station => station);
            var target = ChooseTarget(bazaar, nativeSeers);
            var instance = BazaarSpawnHelper.Spawn(seer, seerPrefab, seer.Name, seer.Position, seer.Forward, clone =>
            {
                var station = clone.GetComponentInChildren<SeerStationController>(true);
                var purchase = station.GetComponent<PurchaseInteraction>();
                station.explicitTargetSceneExitController = existing ? existing.explicitTargetSceneExitController : null;
                if (!station.explicitTargetSceneExitController)
                    station.explicitTargetSceneExitController = Array.Find(UnityEngine.Object.FindObjectsOfType<SceneExitController>(),
                        exit => exit.name.StartsWith("PortalShop", StringComparison.OrdinalIgnoreCase));
                station.fallBackToFirstActiveExitController = !existing || existing.fallBackToFirstActiveExitController;
                if (target) station.SetTargetScene(target);
                else purchase.SetAvailable(false);
            });
            if (instance && bazaar)
            {
                var updated = new List<SeerStationController>(nativeSeers) { instance.GetComponentInChildren<SeerStationController>(true) };
                bazaar.seerStations = updated.ToArray();
            }
        }

        private static void ApplyVanillaSettings(BazaarController bazaar, SeerStationController[] nativeSeers)
        {
            if (nativeSeers.Length > 0 && nativeSeers[0])
                ApplyScenePurchase(nativeSeers[0].GetComponent<PurchaseInteraction>(), seer1, seer1Cost);
            if (nativeSeers.Length > 1 && nativeSeers[1])
                ApplyScenePurchase(nativeSeers[1].GetComponent<PurchaseInteraction>(), seer2, seer2Cost);

            foreach (var purchase in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
            {
                if (purchase.GetComponentInParent<BazaarUseCosts.SpawnedSlot>()) continue;
                for (var node = purchase.transform; node; node = node.parent)
                {
                    if (node.name.IndexOf("WhiteToGreen", StringComparison.OrdinalIgnoreCase) >= 0)
                    { ApplyScenePurchase(purchase, vanillaUncommon, uncommonCost); break; }
                    if (node.name.IndexOf("GreenToRed", StringComparison.OrdinalIgnoreCase) >= 0)
                    { ApplyScenePurchase(purchase, vanillaLegendary, legendaryCost); break; }
                }
            }

            if (!BazaarAlternateMode.Active) return;
            ApplyShopSlots(bazaar);
            foreach (var exit in UnityEngine.Object.FindObjectsOfType<SceneExitController>())
                if (exit.name.StartsWith("PortalShop", StringComparison.OrdinalIgnoreCase))
                    ApplySceneSlot(exit, exitPortal);
        }

        private static void ApplyShopSlots(BazaarController bazaar)
        {
            // Scene hierarchy order keeps a slot's identity stable between visits.
            var roots = bazaar.gameObject.scene.GetRootGameObjects();
            Array.Sort(roots, (left, right) => left.transform.GetSiblingIndex().CompareTo(right.transform.GetSiblingIndex()));
            int slotIndex = 0;
            foreach (var root in roots)
            {
                foreach (var terminal in root.GetComponentsInChildren<ShopTerminalBehavior>())
                {
                    if (!terminal.gameObject.activeInHierarchy || !LunarShop.IsBud(terminal)) continue;
                    var purchase = terminal.GetComponent<PurchaseInteraction>();
                    if (!purchase) continue;
                    ApplySceneSlot(purchase, shopSlots[slotIndex++]);
                    if (slotIndex == shopSlots.Length) return;
                }
            }
        }

        private static void ApplyScenePurchase(PurchaseInteraction purchase, BazaarAlternateMode.Slot slot, ConfigEntry<int> cost)
        {
            if (!purchase) return;
            BazaarUseCosts.Apply(purchase, cost);
            if (BazaarAlternateMode.Active) ApplySceneSlot(purchase, slot);
        }

        private static void ApplySceneSlot(Component component, BazaarAlternateMode.Slot slot)
        {
            var identity = component.GetComponentInParent<NetworkIdentity>();
            var target = identity ? identity.gameObject : component.gameObject;
            bool enabled = Config.ConfigManager.Get(slot.Spawns);
            if (!enabled)
            {
                target.SetActive(false);
                BazaarAlternateMode.PublishSceneObject(target, false);
                return;
            }

            BazaarAlternateMode.PublishSceneObject(target, true);
            BazaarAlternateMode.LockSpawn(target, slot);
        }

        private static SceneDef ChooseTarget(BazaarController bazaar, SeerStationController[] stations)
        {
            var run = Run.instance;
            if (!run || !run.nextStageScene) return null;
            var used = new HashSet<int>();
            foreach (var station in stations)
                if (station && station.gameObject.activeInHierarchy) used.Add(station.ReturnTargetSceneDefIndex());

            var eligible = new List<SceneDef>();
            foreach (var scene in SceneCatalog.allSceneDefs)
            {
                if (!scene || scene.filterOutOfBazaar || scene.sceneType == SceneType.Junk ||
                    scene.stageOrder != run.nextStageScene.stageOrder ||
                    !run.CanPickStage(scene)) continue;
                bool unlocked = bazaar && unlockedBeforeLoop != null
                    ? (bool)unlockedBeforeLoop.Invoke(bazaar, new object[] { scene })
                    : run.stageClearCount >= 4 || !scene.isLockedBeforeLooping;
                if (!unlocked) continue;
                eligible.Add(scene);
            }
            if (eligible.Count == 0) return null;
            var unique = eligible.FindAll(scene => !used.Contains((int)scene.sceneDefIndex));
            var choices = unique.Count > 0 ? unique : eligible;
            return choices[run.stageRng.RangeInt(0, choices.Count)];
        }

        private static void SpawnCauldron(BazaarSpawnHelper.Settings settings)
        {
            if (BazaarSpawnHelper.Roll(settings))
                BazaarSpawnHelper.Spawn(settings, settings.Path, settings.Name, settings.Position, settings.Forward);
        }
    }
}
