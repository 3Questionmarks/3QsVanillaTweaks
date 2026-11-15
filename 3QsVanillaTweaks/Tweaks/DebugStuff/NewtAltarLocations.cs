using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;
using Log = ThreeQsVanillaTweaks.Log;
using ModConfig = Config.ConfigManager;
using ReflectionTools = ThreeQsVanillaTweaks.ReflectionTools;
using NewtAltar = Tweaks.Stages.NewtAltar;

namespace Tweaks.DebugStuff
{
    /// <summary>
    /// Debug-only discovery of altar instances and declared spawn points in the loaded stage.
    /// </summary>
    internal static class NewtAltarLocations
    {
        private const string Section = "Debug";
        private const string NewtPrefabAddress = "RoR2/Base/NewtStatue/NewtStatue.prefab";
        private static ConfigEntry<bool> logLocations;
        private static ConfigEntry<bool> showMarkers;
        private static ConfigEntry<bool> diagnosticOnly;

        internal static bool DisableGuaranteesDuringMapping =>
            diagnosticOnly != null && ModConfig.Get(diagnosticOnly);

        internal static void Init()
        {
            logLocations = ModConfig.BindBool(Section, "Newt Altar - Log Candidate Positions", true,
                "Log Newt Altar positions and rotations throughout the stage, including inactive altars and declared spawn points.");
            showMarkers = ModConfig.BindBool(Section, "Newt Altar - Show Location Markers", true,
                "Show markers over Newt Altar locations. S markers identify unconfirmed spawn points.");
            diagnosticOnly = ModConfig.BindBool(Section, "Newt Altar - Disable Guarantees During Mapping", true,
                "Temporarily disable configured Newt Altar guarantees during mapping.");

            var root = new GameObject("3Qs Newt Altar Diagnostic Scanner");
            UnityEngine.Object.DontDestroyOnLoad(root);
            root.AddComponent<MarkerOverlay>();
            Log.Info("Debug: Newt Altar discovery will scan loaded stages throughout each visit.");
        }

        private sealed class MarkerOverlay : MonoBehaviour
        {
            private sealed class Entry
            {
                internal string label;
                internal Vector3 position;
            }

            private readonly List<Entry> entries = new List<Entry>();
            private readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
            private GUIStyle markerStyle;
            private int? currentSceneHandle;
            private float nextScan;
            private float firstScan;
            private bool addressableSpawnerResolved;
            private Type addressableSpawnerType;
            private FieldInfo addressField;
            private FieldInfo zeroPoseField;
            private FieldInfo zeroPositionField;
            private FieldInfo zeroRotationField;
            private FieldInfo localPoseField;

            private void Update()
            {
                bool show = ModConfig.Get(showMarkers);
                bool log = ModConfig.Get(logLocations);
                if (!Run.instance || (!show && !log))
                {
                    currentSceneHandle = null;
                    entries.Clear();
                    return;
                }

                var sceneInfo = SceneInfo.instance;
                Scene scene = sceneInfo ? sceneInfo.gameObject.scene : SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    entries.Clear();
                    return;
                }

                float now = Time.unscaledTime;
                if (currentSceneHandle != scene.handle)
                {
                    currentSceneHandle = scene.handle;
                    entries.Clear();
                    logged.Clear();
                    nextScan = firstScan = now;
                }

                if (now < nextScan) return;
                nextScan = now + (now - firstScan < 30f ? 1f : 3f);
                SceneDef sceneDef = sceneInfo && sceneInfo.sceneDef
                    ? sceneInfo.sceneDef : SceneCatalog.GetSceneDefFromScene(scene);
                ScanScene(scene, sceneDef, show, log);
            }

            private void ScanScene(Scene scene, SceneDef sceneDef, bool show, bool log)
            {
                try
                {
                    string stageId = sceneDef ? sceneDef.baseSceneName : scene.name;
                    if (log && logged.Add("scene"))
                        Log.Info("Newt Altar DIAGNOSTIC: scanning stage='" + stageId +
                            "', scene='" + scene.name + "' for altar instances and spawn points.");

                    entries.Clear();
                    var instances = new List<Transform>();
                    foreach (var candidate in NewtAltar.GetCandidates(scene))
                    {
                        Transform altar = candidate.gameObject.transform;
                        var statue = altar.GetComponentInChildren<PortalStatueBehavior>(true);
                        bool confirmed = statue && statue.portalType == PortalStatueBehavior.PortalType.Shop;
                        if (confirmed) instances.Add(altar);
                        Report(scene, stageId, candidate.letter, altar, candidate.hierarchy,
                            confirmed ? AltarState(statue) : "named candidate", show, log);
                    }

                    var roots = scene.GetRootGameObjects();
                    int renamedAltarIndex = 0;
                    foreach (GameObject root in roots)
                    {
                        foreach (var altar in root.GetComponentsInChildren<PortalStatueBehavior>(true))
                        {
                            if (!altar || altar.portalType != PortalStatueBehavior.PortalType.Shop) continue;
                            Transform position = altar.transform;
                            if (instances.Exists(instance => position.IsChildOf(instance) ||
                                (instance.position - position.position).sqrMagnitude < 0.0001f))
                                continue;

                            instances.Add(position);
                            Report(scene, stageId, "N" + ++renamedAltarIndex, position, Hierarchy(position),
                                AltarState(altar), show, log);
                        }
                    }

                    int spawnPointIndex = 0;
                    var reportedPoints = new HashSet<Transform>();
                    ResolveAddressableSpawner();
                    foreach (GameObject root in roots)
                    {
                        foreach (var spawner in root.GetComponentsInChildren<PrefabSpawner>(true))
                        {
                            if (!spawner || !spawner.prefabToSpawn || spawner.spawnPoints == null) continue;
                            var altar = spawner.prefabToSpawn.GetComponentInChildren<PortalStatueBehavior>(true);
                            if (!altar || altar.portalType != PortalStatueBehavior.PortalType.Shop) continue;

                            foreach (Transform point in spawner.spawnPoints)
                                ReportSpawnPoint(point, "spawn point");
                        }

                        if (addressField == null) continue;
                        foreach (Component spawner in root.GetComponentsInChildren(addressableSpawnerType, true))
                        {
                            if (!spawner || !string.Equals(addressField.GetValue(spawner) as string,
                                NewtPrefabAddress, StringComparison.OrdinalIgnoreCase))
                                continue;

                            // LoP's local zero offsets place the spawned altar exactly at this transform.
                            bool matchesAltarPose = IsSet(zeroPoseField, spawner) && IsSet(zeroPositionField, spawner) &&
                                IsSet(zeroRotationField, spawner) && IsSet(localPoseField, spawner);
                            ReportSpawnPoint(spawner.transform, matchesAltarPose ? "addressable spawn point" : "addressable holder");
                        }
                    }

                    void ReportSpawnPoint(Transform point, string kind)
                    {
                        if (!point || !reportedPoints.Add(point)) return;
                        bool hasInstance = instances.Exists(t => (t.position - point.position).sqrMagnitude < 0.01f);
                        Report(scene, stageId, "S" + ++spawnPointIndex, point, Hierarchy(point), kind,
                            show && !hasInstance, log);
                    }
                }
                catch (Exception ex)
                {
                    if (log && logged.Add("scan error"))
                        Log.Warning("Newt Altar full-scene diagnostic failed in '" + scene.name + "': " + ex);
                }
            }

            private void ResolveAddressableSpawner()
            {
                if (addressableSpawnerResolved) return;
                addressableSpawnerResolved = true;
                addressableSpawnerType = ReflectionTools.TypeByName("LOP.InstantiateAddressablePrefab");
                addressField = ReflectionTools.Field(addressableSpawnerType, "address");
                zeroPoseField = ReflectionTools.Field(addressableSpawnerType, "setPositionAndRotationToZero");
                zeroPositionField = ReflectionTools.Field(addressableSpawnerType, "setPositionToZero");
                zeroRotationField = ReflectionTools.Field(addressableSpawnerType, "setRotationToZero");
                localPoseField = ReflectionTools.Field(addressableSpawnerType, "useLocalPositionAndRotation");
            }

            private static bool IsSet(FieldInfo field, Component spawner) => field?.GetValue(spawner) is true;

            private static string AltarState(PortalStatueBehavior altar) =>
                altar.gameObject.activeInHierarchy ? "active altar" : "inactive altar";

            private void Report(Scene scene, string stageId, string label, Transform target, string hierarchy,
                string kind, bool show, bool log)
            {
                if (show) entries.Add(new Entry { label = label, position = target.position });
                if (!log) return;

                Vector3 position = target.position;
                string logKey = kind + ":" + hierarchy + ":" + PlayerCoordinates.FormatVector(position, 3);
                if (!logged.Add(logKey)) return;

                Log.Info("Newt Altar DIAGNOSTIC: scene='" + scene.name + "', stage='" + stageId +
                    "', marker=" + label + ", type=" + kind + ", object='" + target.name +
                    "', position=" + FormatVector(position) + ", rotation=" + FormatVector(target.eulerAngles) +
                    ", forward=" + FormatVector(target.forward) + ", hierarchy='" + hierarchy + "'");
            }

            private static string Hierarchy(Transform target)
            {
                var names = new Stack<string>();
                for (Transform item = target; item; item = item.parent) names.Push(item.name);
                return string.Join("/", names);
            }

            private static string FormatVector(Vector3 vector) => "(" + PlayerCoordinates.FormatVector(vector, 6) + ")";

            private void OnGUI()
            {
                if (!ModConfig.Get(showMarkers) || entries.Count == 0) return;
                Camera camera = null;
                var local = LocalUserManager.GetFirstLocalUser();
                foreach (var rig in CameraRigController.readOnlyInstancesList)
                {
                    if (!rig || local == null || rig.localUserViewer != local || !rig.sceneCam) continue;
                    camera = rig.sceneCam;
                    break;
                }
                if (!camera) camera = Camera.main;
                if (camera == null) return;
                if (markerStyle == null)
                {
                    markerStyle = new GUIStyle(GUI.skin.box);
                    markerStyle.fontSize = 21;
                    markerStyle.fontStyle = FontStyle.Bold;
                    markerStyle.alignment = TextAnchor.MiddleCenter;
                    markerStyle.normal.textColor = Color.yellow;
                }
                foreach (Entry entry in entries)
                {
                    Vector3 point = camera.WorldToScreenPoint(entry.position + Vector3.up * 3f);
                    if (point.z <= 0f) continue;
                    float x = point.x - 26f;
                    float y = Screen.height - point.y - 26f;
                    if (x < -52f || x > Screen.width || y < -52f || y > Screen.height) continue;
                    GUI.Box(new Rect(x, y, 52f, 52f), entry.label, markerStyle);
                }
            }
        }
    }
}
