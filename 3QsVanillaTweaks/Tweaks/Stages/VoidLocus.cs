using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.DeepVoidPortalBattery;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class VoidLocus
    {
        private static ConfigEntry<bool> enabled, fullSpeed, stopEnemies, keepSignals, rewards;
        private static ConfigEntry<int> required, maximum;
        private static ConfigEntry<float> radius, duration;
        private static ConfigEntry<bool> scaleSignalCredits;
        private static ConfigEntry<float> signalCreditIncrease, extraSignalScaling;
        [ThreadStatic] private static Mission spawning;
        private static Run cachedRun;
        private static SceneDef cachedNormalDestination;

        internal static void Init()
        {
            const string section = "Stages - Void Locus";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable Void Locus changes.");
            radius = ModConfig.Bind(section, "Signal Radius", 30f,
                new ConfigDescription("Radius of the signal holdout in metres.", new AcceptableValueRange<float>(1f, 1000f)));
            duration = ModConfig.Bind(section, "Signal Charge Time", 60f,
                new ConfigDescription("Seconds required to fully charge a signal at full speed.", new AcceptableValueRange<float>(0.1f, 3600f)));
            fullSpeed = ModConfig.BindBool(section, "Full Speed With One Player", true,
                "Charge at full speed with at least one player inside the signal holdout. Vanilla: False.");
            required = Number(section, "Signals Required", 4,
                "Signals needed to open the Deep Void Portal. Also sets the minimum number spawned. Vanilla: 4.");
            maximum = Number(section, "Maximum Signals", 8,
                "Maximum number of signals to spawn. Cannot be lower than the number required. Vanilla: 4.");
            keepSignals = ModConfig.BindBool(section, "Signals Remain Available", true,
                "Allow unfinished signals to be activated after the Deep Void Portal opens.");
            stopEnemies = ModConfig.BindBool(section, "Stop Enemies After Signals", true,
                "Stop enemies spawning once the required signals are completed. Vanilla: True.");
            rewards = ModConfig.BindBool(section, "Signal Rewards", true,
                "Each completed signal drops one Void Potential per player. Vanilla: False.");
            scaleSignalCredits = ModConfig.BindBool(section, "Scale Signal Enemy Credits", true,
                "Increase enemy spawning credits for each subsequent Deep Void Signal. Only affects that signal's encounter. Vanilla: False.");
            signalCreditIncrease = ModConfig.Bind(section, "Enemy Credit Increase Per Signal (%)", 25f,
                new ConfigDescription("Additional percentage of the first signal's credit budget per subsequent signal. Vanilla: 0.", new AcceptableValueRange<float>(0f, 1000f)));
            extraSignalScaling = ModConfig.Bind(section, "Extra Signal Credit Increase Multiplier", 2f,
                new ConfigDescription("Multiply the per-signal increase for each signal beyond the required count. 2 doubles the increase; 1 keeps the normal increase.", new AcceptableValueRange<float>(1f, 100f)));
            VoidLocusRewards.Bind(section);
            VoidLocusShop.Bind();

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.VoidLocus");
            harmony.Patch(AccessTools.Method(typeof(VoidStageMissionController), "Start"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeMissionStart)),
                postfix: new HarmonyMethod(typeof(VoidLocus), nameof(AfterMissionStart)),
                finalizer: new HarmonyMethod(typeof(VoidLocus), nameof(EndMissionStart)));
            harmony.Patch(AccessTools.Method(typeof(DirectorCore), "TrySpawnObject"),
                postfix: new HarmonyMethod(typeof(VoidLocus), nameof(AfterSpawn)));
            harmony.Patch(AccessTools.Method(typeof(Charging), "OnEnter"),
                postfix: new HarmonyMethod(typeof(VoidLocus), nameof(AfterCharging)));
            harmony.Patch(AccessTools.Method(typeof(Charging), "OnExit"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeChargingEnds)));
            harmony.Patch(AccessTools.Method(typeof(Charged), "OnEnter"),
                postfix: new HarmonyMethod(typeof(VoidLocus), nameof(AfterCharged)));
            harmony.Patch(AccessTools.Method(typeof(VoidStageMissionController), "OnBatteryActivated"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeNativeCompletion)));
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "DoUpdate"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeHoldoutUpdate)));
            harmony.Patch(AccessTools.Method(typeof(CombatDirector), "AttemptSpawnOnTarget"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeEnemySpawn)));
            harmony.Patch(AccessTools.Method(typeof(Run), "PickNextStageScene"),
                postfix: new HarmonyMethod(typeof(VoidLocus), nameof(RememberNormalDestination)));
            harmony.Patch(AccessTools.Method(typeof(SceneExitController), "Begin"),
                prefix: new HarmonyMethod(typeof(VoidLocus), nameof(BeforeReturn)));
        }

        internal static bool Ready => ModConfig.Get(enabled) && Run.instance && SpawnRequirements.ExpansionEnabled("DLC1");
        private static bool Apply(GameObject obj) => Ready && obj && obj.scene.name == "voidstage";
        private static ConfigEntry<int> Number(string section, string key, int value, string text) =>
            ModConfig.Bind(section, key, value, new ConfigDescription(text, new AcceptableValueRange<int>(0, 16)));
        private static T Read<T>(object obj, string field) => (T)AccessTools.Field(obj.GetType(), field).GetValue(obj);

        internal sealed class Mission : MonoBehaviour
        {
            internal VoidStageMissionController Owner;
            internal SpawnCard Card;
            internal int Needed, ActualSpawned;
            internal bool Complete;
            private float nextPortalAttempt;
            internal readonly Dictionary<HoldoutZoneController, int> EncounterOrder = new Dictionary<HoldoutZoneController, int>();
            internal readonly List<HoldoutZoneController> Signals = new List<HoldoutZoneController>();
            internal readonly HashSet<HoldoutZoneController> Finished = new HashSet<HoldoutZoneController>();
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !Apply(gameObject) || Complete || Finished.Count < Needed || Time.fixedTime < nextPortalAttempt) return;
                nextPortalAttempt = Time.fixedTime + 3f;
                try { CompleteMission(this); }
                catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Locus portal could not be opened: " + error.Message); }
            }
        }

        private static void BeforeMissionStart(VoidStageMissionController __instance, out Mission __state)
        {
            __state = spawning;
            if (!NetworkServer.active || !Apply(__instance.gameObject)) return;
            var mission = __instance.gameObject.AddComponent<Mission>();
            mission.Owner = __instance;
            mission.Card = Read<InteractableSpawnCard>(__instance, "batterySpawnCard");
            mission.Needed = ModConfig.Get(required);
            int count = Run.instance.stageRng.RangeInt(mission.Needed, Math.Max(mission.Needed, ModConfig.Get(maximum)) + 1);
            AccessTools.Field(typeof(VoidStageMissionController), "batteryCount").SetValue(__instance, count);
            spawning = mission;
        }

        private static void AfterSpawn(DirectorSpawnRequest __0, GameObject __result)
        {
            if (!spawning || !__result || __0.spawnCard != spawning.Card) return;
            HoldoutZoneController zone = __result.GetComponent<HoldoutZoneController>();
            if (zone && !spawning.Signals.Contains(zone)) spawning.Signals.Add(zone);
        }

        private static void AfterMissionStart(VoidStageMissionController __instance)
        {
            Mission mission = __instance.GetComponent<Mission>();
            if (!NetworkServer.active || !mission) return;
            mission.ActualSpawned = __instance.numBatteriesSpawned;
            mission.Needed = Math.Min(mission.Needed, mission.ActualSpawned);
            // Keep the native replicated objective focused on the required count.
            __instance.Network_numBatteriesSpawned = mission.Needed;
            if (mission.Needed == 0) CompleteMission(mission);
        }

        private static Exception EndMissionStart(Exception __exception, Mission __state)
        { spawning = __state; return __exception; }

        internal sealed class Signal : MonoBehaviour
        {
            internal HoldoutZoneController Zone;
            internal void Configure()
            {
                if (!Apply(gameObject)) return;
                Zone.baseRadius = ModConfig.Get(radius);
                Zone.baseChargeDuration = ModConfig.Get(duration);
                Zone.playerCountScaling = ModConfig.Get(fullSpeed) ? 0f : 1f;
            }
        }

        private static void AfterCharging(EntityState __instance, HoldoutZoneController ___holdoutZoneController)
        {
            HoldoutZoneController zone = ___holdoutZoneController;
            if (!zone || !Apply(zone.gameObject)) return;
            Signal signal = zone.GetComponent<Signal>();
            if (!signal) { signal = zone.gameObject.AddComponent<Signal>(); signal.Zone = zone; }
            signal.Configure();
            if (!NetworkServer.active) return;
            var owner = VoidStageMissionController.instance;
            var mission = owner ? owner.GetComponent<Mission>() : null;
            if (!mission) return;
            if (!mission.EncounterOrder.TryGetValue(zone, out int previous))
            {
                previous = mission.EncounterOrder.Count;
                mission.EncounterOrder.Add(zone, previous);
            }
            int needed = mission.Needed;
            foreach (var director in zone.GetComponentsInChildren<CombatDirector>(true))
                HoldoutDirectorScaling.Begin(director, () => Apply(zone.gameObject) && ModConfig.Get(scaleSignalCredits)
                    ? HoldoutDirectorScaling.Multiplier(previous, needed, ModConfig.Get(signalCreditIncrease), ModConfig.Get(extraSignalScaling)) : 1f);
        }

        private static void BeforeChargingEnds(EntityState __instance)
        {
            if (!NetworkServer.active) return;
            foreach (var director in __instance.gameObject.GetComponentsInChildren<CombatDirector>(true))
                HoldoutDirectorScaling.End(director);
        }

        private static void BeforeHoldoutUpdate(HoldoutZoneController __instance)
        { Signal signal = __instance.GetComponent<Signal>(); if (signal) signal.Configure(); }

        private static bool BeforeNativeCompletion(VoidStageMissionController __instance) =>
            !Apply(__instance.gameObject) || !__instance.GetComponent<Mission>();

        private static void AfterCharged(EntityState __instance)
        {
            if (!NetworkServer.active || !__instance.outer) return;
            HoldoutZoneController zone = __instance.outer.GetComponent<HoldoutZoneController>();
            VoidStageMissionController owner = VoidStageMissionController.instance;
            Mission mission = owner ? owner.GetComponent<Mission>() : null;
            if (!mission || !zone || !Apply(zone.gameObject) || !mission.Finished.Add(zone)) return;
            owner.Network_numBatteriesActivated = mission.Finished.Count;
            if (ModConfig.Get(rewards))
                try { VoidLocusRewards.Drop(zone.transform.position); }
                catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Locus signal reward failed: " + error.Message); }
            if (!mission.Complete && mission.Finished.Count >= mission.Needed) CompleteMission(mission);
        }

        private static void CompleteMission(Mission mission)
        {
            if (mission.Complete) return;
            Transform target = Read<Transform>(mission.Owner, "portalRoot");
            InteractableSpawnCard card = Read<InteractableSpawnCard>(mission.Owner, "deepVoidPortalSpawnCard");
            if (!target || !card) throw new InvalidOperationException("Void Locus Deep Void Portal assets are unavailable.");
            GameObject portal = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, new DirectorPlacementRule
            { placementMode = DirectorPlacementRule.PlacementMode.DirectWithoutRandomRotation, spawnOnTarget = target },
                Read<Xoroshiro128Plus>(mission.Owner, "rng")));
            if (!portal) { ThreeQsVanillaTweaks.Log.Warning("Void Locus Deep Void Portal could not be spawned."); return; }
            DebugStuff.InteractablePlacementEditor.Register(portal, "Deep Void Portal", "Stages - Void Locus - Deep Void Portal", alreadySpawned: true);
            mission.Complete = true;
            string token = Read<string>(mission.Owner, "portalOpenChatToken");
            if (!string.IsNullOrEmpty(token)) Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = token });
            GenericObjectiveProvider objective = Read<GenericObjectiveProvider>(mission.Owner, "deepVoidPortalObjectiveProvider");
            if (objective) objective.enabled = true;
            if (!ModConfig.Get(keepSignals))
                foreach (HoldoutZoneController zone in mission.Signals)
                    if (zone && !mission.Finished.Contains(zone))
                    { PurchaseInteraction purchase = zone.GetComponent<PurchaseInteraction>(); if (purchase) purchase.SetAvailable(false); }
            // Preserve event directors and credits for optional signals.
            // BeforeEnemySpawn blocks ambient spawning while no signal is active.
        }

        // Only the currently charging signal may spawn after the portal opens.
        private static bool ActiveSignalDirector(Mission mission, CombatDirector director) =>
            mission.Signals.Any(zone => zone && (director.transform == zone.transform || director.transform.IsChildOf(zone.transform)) &&
                !mission.Finished.Contains(zone) && zone.GetComponent<EntityStateMachine>() &&
                zone.GetComponent<EntityStateMachine>().state is Charging);

        private static bool BeforeEnemySpawn(CombatDirector __instance, ref bool __result)
        {
            if (!NetworkServer.active || !Apply(__instance.gameObject) || !ModConfig.Get(stopEnemies)) return true;
            VoidStageMissionController owner = VoidStageMissionController.instance;
            Mission mission = owner ? owner.GetComponent<Mission>() : null;
            if (!mission || !mission.Complete || ActiveSignalDirector(mission, __instance)) return true;
            __result = false;
            return false;
        }

        internal sealed class ReturnPortal : MonoBehaviour { }

        private static bool NormalScene(SceneDef scene) => scene && scene.sceneType == SceneType.Stage &&
            scene.stageOrder >= 1 && scene.stageOrder <= 5 && SpawnRequirements.SceneAllowed(scene);

        private static void RememberNormalDestination(Run __instance)
        {
            if (!NetworkServer.active) return;
            if (cachedRun != __instance) { cachedRun = __instance; cachedNormalDestination = null; }
            if (NormalScene(__instance.nextStageScene)) cachedNormalDestination = __instance.nextStageScene;
        }

        private static bool BeforeReturn(SceneExitController __instance)
        {
            if (!NetworkServer.active || !Apply(__instance.gameObject) || !__instance.GetComponent<ReturnPortal>()) return true;
            try { SetReturnDestination(__instance); return true; }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Locus return portal could not choose a stage: " + error.Message); return false; }
        }

        private static void SetReturnDestination(SceneExitController exit)
        {
            Run run = Run.instance;
            int tier = (run.stageClearCount % 5) + 1;
            bool randomStages = run.ruleBook.stageOrder != StageOrder.Normal;
            SceneDef target = cachedRun == run ? cachedNormalDestination : null;
            if (!NormalScene(target) || !randomStages && target.stageOrder != tier || !run.CanPickStage(target))
            {
                var choices = new WeightedSelection<SceneDef>();
                if (tier == 1 && run.startingSceneGroup)
                    run.startingSceneGroup.AddToWeightedSelection(choices, scene => NormalScene(scene) && run.CanPickStage(scene));
                else
                    foreach (SceneDef source in SceneCatalog.allSceneDefs)
                    {
                        if (!source || source.sceneType != SceneType.Stage || source.stageOrder != tier - 1) continue;
                        source.AddDestinationsToWeightedSelection(choices, scene => NormalScene(scene) && scene.stageOrder == tier && run.CanPickStage(scene));
                        if (choices.Count > 0) break;
                    }
                var filtered = new WeightedSelection<SceneDef>();
                for (int i = 0; i < choices.Count; i++)
                {
                    var choice = choices.GetChoice(i);
                    if (NormalScene(choice.value) && choice.value.stageOrder == tier && run.CanPickStage(choice.value) && choice.weight > 0f)
                        filtered.AddChoice(choice.value, choice.weight);
                }
                choices = filtered;
                if (choices.Count == 0)
                    foreach (SceneDef scene in SceneCatalog.allSceneDefs)
                        if (NormalScene(scene) && scene.stageOrder == tier && run.CanPickStage(scene)) choices.AddChoice(scene, 1f);
                if (choices.Count == 0) throw new InvalidOperationException("No normal stage is available for the return portal.");
                SceneDef original = run.nextStageScene;
                try { run.PickNextStageScene(choices); target = run.nextStageScene; }
                finally { run.nextStageScene = original; }
            }
            var loopReplacement = AccessTools.Method(typeof(SceneExitController), "GetLoopedStageOrDefault");
            if (loopReplacement != null)
            {
                SceneDef replacement = loopReplacement.Invoke(exit, new object[] { target }) as SceneDef;
                if (NormalScene(replacement) && run.CanPickStage(replacement)) target = replacement;
            }
            if (!NormalScene(target)) throw new InvalidOperationException("The return portal has no valid destination.");
            exit.useRunNextStageScene = false;
            exit.isAlternatePath = false;
            AccessTools.Field(typeof(SceneExitController), "doNotIncrementStageNumber")?.SetValue(exit, true);
            exit.destinationScene = target;
        }
    }
}
