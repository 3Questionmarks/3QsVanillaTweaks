using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using Object = UnityEngine.Object;

namespace Tweaks.Stages
{
    internal static class ProvidenceGeodePortal
    {
        private const string Section = "Stages - Ultima - Golden Geodes";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> delay, portalHeight;
        private static Run routeRun;
        private static Stage coastStage;
        private static bool enteringCoast, coastPortalReady;
        private static readonly Vector3[] Positions = {
            new Vector3(1.70f,14.95f,-172.52f), new Vector3(-75.35f,27.61f,-125.12f),
            new Vector3(64.03f,33.25f,-105.26f), new Vector3(-169.43f,14.95f,-1.90f),
            new Vector3(-97.57f,24.67f,88.61f), new Vector3(82.84f,33.01f,116.25f),
            new Vector3(132.12f,11.95f,1.66f), new Vector3(-1.73f,14.95f,175.33f), new Vector3(0f,10f,0f)
        };
        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true, "After Providence's final defeat, add nine sequential Golden Geodes and a gold portal to Gilded Coast. Completing that encounter offers a green portal to Prime Meridian, or the full path if enabled in the Green Portals config. Restart to apply.");
            delay = C.Bind(Section, "First Geode Delay", 30f, new ConfigDescription("Seconds after Providence's defeat before the first geode appears.", new AcceptableValueRange<float>(0f, 3600f)));
            portalHeight = C.Bind(Section, "Portal Height Above Ground", 2f, new ConfigDescription("Gold portal vertical offset above the ground at the ninth geode.", new AcceptableValueRange<float>(0f, 20f)));
            if (!enabled.Value) return;
            SceneDirector.onPostPopulateSceneServer += OnSceneReady;
            // Reference assemblies can omit the Providence encounter types. Resolve the
            // native defeat event from the installed game instead of requiring them to compile.
            var bossDeath = typeof(Run).Assembly.GetType("EntityStates.Missions.ProvidenceBossEncounter.BossDeath", false);
            var defeated = bossDeath?.GetEvent("onProvidenceDefeatedServer",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (defeated == null || defeated.EventHandlerType != typeof(Action))
            {
                Log.Warning("Ultima geode puzzle unavailable: this game version has no supported Providence defeat event.");
                return;
            }
            defeated.GetAddMethod(true).Invoke(null, new object[] { (Action)OnProvidenceDefeated });
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.ProvidenceGeodePortal");
            harmony.Patch(AccessTools.Method(typeof(SceneExitController), "Begin"),
                prefix: new HarmonyMethod(typeof(ProvidenceGeodePortal), nameof(BeforePortalBegin)),
                postfix: new HarmonyMethod(typeof(ProvidenceGeodePortal), nameof(AfterPortalBegin)));
            harmony.Patch(AccessTools.Method(typeof(EntityStates.Missions.MiniGeodeTracker.GeodeDeathState), "OnEnter"),
                prefix: new HarmonyMethod(typeof(ProvidenceGeodePortal), nameof(OnGeodeDeath)));
            harmony.Patch(AccessTools.Method(typeof(EntityStates.Missions.Goldshores.Exit), "OnEnter"),
                prefix: new HarmonyMethod(typeof(ProvidenceGeodePortal), nameof(BeforeCoastExit)),
                postfix: new HarmonyMethod(typeof(ProvidenceGeodePortal), nameof(AfterCoastExit)));
        }
        private static bool Active => enabled != null && C.Get(enabled);
        private static bool IsScene(string name) => Stage.instance && Stage.instance.sceneDef && Stage.instance.sceneDef.baseSceneName == name;
        private static void OnSceneReady(SceneDirector director)
        {
            if (!NetworkServer.active || !Run.instance || !Stage.instance || !director) return;
            bool arriving = enteringCoast && routeRun == Run.instance && IsScene("goldshores");
            enteringCoast = false; coastPortalReady = false;
            coastStage = arriving ? Stage.instance : null;
            if (!arriving) routeRun = null;
            if (!Active || director.gameObject.scene.name != "ultima" || Stage.instance.GetComponent<Puzzle>()) return;
            var puzzle = Stage.instance.gameObject.AddComponent<Puzzle>();
            puzzle.Prepare(new Xoroshiro128Plus(Run.instance.stageRng.nextUlong));
        }
        private static void OnProvidenceDefeated()
        {
            if (!NetworkServer.active || !Active || !IsScene("ultima")) return;
            var puzzle = Stage.instance.GetComponent<Puzzle>();
            if (!puzzle)
            {
                puzzle = Stage.instance.gameObject.AddComponent<Puzzle>();
                puzzle.Prepare(new Xoroshiro128Plus(Run.instance.stageRng.nextUlong));
            }
            puzzle.Begin();
        }
        // Fisher-Yates only shuffles the middle seven entries. IDs 1 and 9 stay fixed.
        internal static int[] CreateOrder(Func<int, int, int> range)
        {
            var order = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            for (int i = 7; i > 1; --i)
            {
                int j = range(1, i + 1);
                int swap = order[i]; order[i] = order[j]; order[j] = swap;
            }
            return order;
        }
        private static void OnGeodeDeath(EntityStates.Missions.MiniGeodeTracker.GeodeDeathState __instance)
        {
            if (!NetworkServer.active) return;
            var marker = __instance.gameObject.GetComponent<GeodeMarker>();
            if (marker && marker.Owner) marker.Owner.Broken(__instance.gameObject);
        }
        private static void BeforePortalBegin(SceneExitController __instance, out bool __state)
        {
            __state = NetworkServer.active && Active && __instance && __instance.GetComponent<GoldPortalMarker>() &&
                (SceneExitController.ExitState)AccessTools.Field(typeof(SceneExitController), "exitState").GetValue(__instance) == SceneExitController.ExitState.Idle;
        }
        private static void AfterPortalBegin(SceneExitController __instance, bool __state)
        {
            if (!__state) return;
            routeRun = Run.instance; enteringCoast = true;
        }
        private static bool SpecialCoast => Active && NetworkServer.active && Run.instance && routeRun == Run.instance && coastStage == Stage.instance && IsScene("goldshores");
        private static void BeforeCoastExit(out HashSet<SceneExitController> __state)
        {
            __state = SpecialCoast && !coastPortalReady ? new HashSet<SceneExitController>(Object.FindObjectsOfType<SceneExitController>()) : null;
        }
        private static void AfterCoastExit(EntityStates.Missions.Goldshores.Exit __instance, HashSet<SceneExitController> __state)
        {
            if (__state == null || !SpecialCoast) return;
            try
            {
                var destination = SceneCatalog.FindSceneDef("meridian");
                if (!destination) throw new InvalidOperationException("Prime Meridian scene missing.");
                var card = LegacyResourcesAPI.Load<SpawnCard>("SpawnCards/InteractableSpawnCard/iscColossusPortal");
                if (!card || !card.prefab) throw new InvalidOperationException("Green portal spawn card missing.");
                SceneExitController exit = null;
                foreach (var candidate in Object.FindObjectsOfType<SceneExitController>())
                    if (!__state.Contains(candidate) && candidate.gameObject.name == card.prefab.name + "(Clone)") { exit = candidate; break; }
                if (!exit)
                {
                    var mission = GoldshoresMissionController.instance;
                    var position = mission && mission.bossSpawnPosition ? mission.bossSpawnPosition.position : __instance.transform.position;
                    var spawned = DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, new DirectorPlacementRule {
                        placementMode = DirectorPlacementRule.PlacementMode.NearestNode, position = position, minDistance = 10f, maxDistance = float.PositiveInfinity
                    }, Run.instance.stageRng));
                    if (!spawned) throw new InvalidOperationException("Could not place green portal after Aurelionite.");
                    exit = spawned.GetComponent<SceneExitController>();
                    Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "PORTAL_STORM_OPEN" });
                }
                SetDestination(exit, destination);
                coastPortalReady = true;
                Log.Info("Ultima geode route: green portal ready (respects Green Portals routing).");
            }
            catch (Exception error) { Log.Warning("Ultima geode route could not open Prime Meridian: " + error); }
        }
        private static void SetDestination(SceneExitController exit, SceneDef destination)
        {
            if (!exit) throw new InvalidOperationException("Portal has no SceneExitController.");
            exit.useRunNextStageScene = false;
            exit.destinationScene = destination;
            // Alternates are checked before destinationScene by the native controller.
            exit.tier1AlternateDestinationScene = exit.tier2AlternateDestinationScene = exit.tier3AlternateDestinationScene =
                exit.tier4AlternateDestinationScene = exit.tier5AlternateDestinationScene = null;
        }
        private sealed class GeodeMarker : MonoBehaviour { internal Puzzle Owner; }
        private sealed class GoldPortalMarker : MonoBehaviour { }
        private sealed class Puzzle : MonoBehaviour
        {
            private Stage stage;
            private Run run;
            private int[] order;
            private float[] rotations;
            private GameObject prefab, current, portal;
            private int next;
            private float remaining;
            private bool begun, advancing, finished;
            internal void Prepare(Xoroshiro128Plus rng)
            {
                stage = Stage.instance; run = Run.instance;
                order = CreateOrder((a, b) => rng.RangeInt(a, b));
                rotations = new float[9];
                for (int i = 0; i < 9; ++i) rotations[i] = rng.nextNormalizedFloat * 360f;
            }
            internal void Begin()
            {
                if (begun || finished) return;
                // The route requires Storm, but uses the same registered destructible bodies as the Bazaar puzzle.
                if (!StageFeatureSpawnHelper.ExpansionEnabled("RoR2/DLC2/Common/DLC2.asset")) { finished = true; return; }
                try
                {
                    prefab = BodyCatalog.GetBodyPrefab(BodyCatalog.FindBodyIndex("MiniGeodeBody"));
                    if (!prefab || !prefab.GetComponent<NetworkIdentity>() || !prefab.GetComponent<HealthComponent>())
                        throw new InvalidOperationException("Registered MiniGeodeBody prefab unavailable.");
                    remaining = C.Get(delay); begun = true; advancing = true;
                    Log.Info("Ultima geode puzzle: first geode in " + remaining + " seconds.");
                }
                catch (Exception error) { Fail(error); }
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !begun || finished || !advancing) return;
                if (!Active || !stage || stage != Stage.instance || !run || run != Run.instance) { finished = true; return; }
                remaining -= Time.fixedDeltaTime;
                if (remaining > 0f || current) return;
                try
                {
                    advancing = false;
                    if (next == order.Length) { OpenPortal(); finished = true; return; }
                    int id = order[next++];
                    // Use the supplied world coordinates exactly; do not snap to a different floor.
                    current = Object.Instantiate(prefab, Positions[id], Quaternion.Euler(0f, rotations[id], 0f));
                    current.AddComponent<GeodeMarker>().Owner = this;
                    NetworkServer.Spawn(current);
                    Log.Info("Ultima geode puzzle: spawned geode " + (id + 1) + " (" + next + "/9).");
                }
                catch (Exception error) { Fail(error); }
            }
            internal void Broken(GameObject geode)
            {
                if (!finished && begun && geode == current) { advancing = true; remaining = 0f; }
            }
            private void OpenPortal()
            {
                var destination = SceneCatalog.FindSceneDef("goldshores");
                if (!destination) throw new InvalidOperationException("Gilded Coast scene missing.");
                var card = LegacyResourcesAPI.Load<SpawnCard>("SpawnCards/InteractableSpawnCard/iscGoldshoresPortal");
                if (!card || !card.prefab) throw new InvalidOperationException("Gold portal spawn card missing.");
                Vector3 position = Positions[8];
                // Choose the ground closest to the supplied height, avoiding overhead surfaces.
                float distance = float.PositiveInfinity;
                foreach (var hit in Physics.RaycastAll(position + Vector3.up * 12f, Vector3.down, 24f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    if (Mathf.Abs(hit.point.y - Positions[8].y) < distance) { distance = Mathf.Abs(hit.point.y - Positions[8].y); position = hit.point; }
                position += Vector3.up * C.Get(portalHeight);
                portal = Object.Instantiate(card.prefab, position, Quaternion.identity);
                SetDestination(portal.GetComponent<SceneExitController>(), destination);
                portal.AddComponent<GoldPortalMarker>();
                NetworkServer.Spawn(portal);
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = "PORTAL_GOLDSHORES_OPEN" });
                Log.Info("Ultima geode puzzle: gold portal opened at " + position + ".");
            }
            private void Fail(Exception error)
            {
                finished = true;
                Log.Warning("Ultima geode puzzle stopped: " + error);
                if (current) NetworkServer.Destroy(current);
                if (portal) NetworkServer.Destroy(portal);
            }
            private void OnDestroy()
            {
                if (!NetworkServer.active) return;
                if (current) NetworkServer.Destroy(current);
                if (portal) NetworkServer.Destroy(portal);
            }
        }
    }
}
