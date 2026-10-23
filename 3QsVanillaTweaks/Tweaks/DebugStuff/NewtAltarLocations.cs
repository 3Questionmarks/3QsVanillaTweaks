using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    /// <summary>
    /// Debug-only Newt Altar location discovery. Unlike SceneObjectToggleGroup-only
    /// diagnostics, this also finds permanently placed and inactive stage objects.
    /// Does not change any altar's active state or spawn behaviour.
    /// </summary>
    internal static class NewtAltarLocations
    {
        private const string Section = "Debug";
        private const string HarmonyId = "3Questionmarks.3QsVanillaTweaks.DebugStuff.NewtAltarLocations";
        private static ConfigEntry<bool> logLocations;
        private static ConfigEntry<bool> showMarkers;
        private static ConfigEntry<bool> diagnosticOnly;
        private static MarkerOverlay overlay;

        internal static bool DisableGuaranteesDuringMapping =>
            diagnosticOnly != null && ModConfig.Get(diagnosticOnly);

        internal static void Init()
        {
            logLocations = ModConfig.BindBool(Section, "Newt Altar - Log Candidate Positions", false,
                "Log the locations of all Newt Altars found in the loaded stage, including inactive and permanently placed objects.");
            showMarkers = ModConfig.BindBool(Section, "Newt Altar - Show Location Markers", false,
                "Show a lettered marker over every Newt Altar location found in the loaded stage.");
            diagnosticOnly = ModConfig.BindBool(Section, "Newt Altar - Disable Guarantees During Mapping", false,
                "Temporarily disable configured Newt Altar guarantees during mapping.");

            // Stage.Start ensures the scanner exists even on maps without a
            // SceneObjectToggleGroup containing Newt objects.
            var stageStart = AccessTools.Method(typeof(Stage), "Start");
            if (stageStart != null)
                new Harmony(HarmonyId).Patch(stageStart,
                    postfix: new HarmonyMethod(typeof(NewtAltarLocations), nameof(AfterStageStart)));
            else
                ThreeQsVanillaTweaks.Log.Warning("Newt Altar diagnostics: Stage.Start was not found.");

            ThreeQsVanillaTweaks.Log.Info("Debug: full-scene Newt Altar discovery installed (includes inactive and fixed altars).");
        }

        private static void AfterStageStart()
        {
            if (ModConfig.Get(showMarkers) || ModConfig.Get(logLocations)) EnsureOverlay();
        }

        private static void EnsureOverlay()
        {
            if (overlay != null) return;
            var root = new GameObject("3Qs Newt Altar Diagnostic Scanner");
            UnityEngine.Object.DontDestroyOnLoad(root);
            overlay = root.AddComponent<MarkerOverlay>();
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
            private string currentScene = "";
            private float nextScan;
            private float stageScanEnd;

            private void Update()
            {
                bool show = ModConfig.Get(showMarkers);
                bool log = ModConfig.Get(logLocations);
                if (!show && !log)
                {
                    entries.Clear();
                    return;
                }

                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid() || !scene.isLoaded) return;
                if (currentScene != scene.name)
                {
                    currentScene = scene.name;
                    entries.Clear();
                    logged.Clear();
                    nextScan = 0f;
                    stageScanEnd = Time.unscaledTime + 30f;
                }

                if (Time.unscaledTime < nextScan || Time.unscaledTime > stageScanEnd) return;
                nextScan = Time.unscaledTime + 1f;
                ScanScene(scene, show, log);
            }

            private void ScanScene(Scene scene, bool show, bool log)
            {
                try
                {
                    // Resources.FindObjectsOfTypeAll includes inactive scene objects.
                    // Restrict to the active, loaded stage to exclude asset prefabs
                    // and objects belonging to other loaded scenes.
                    List<Tweaks.Stages.NewtAltar.AltarCandidate> candidates =
                        Tweaks.Stages.NewtAltar.GetCandidates(scene);
                    entries.Clear();
                    foreach (var candidate in candidates)
                    {
                        if (show) entries.Add(new Entry { label = candidate.letter, position = candidate.position });
                        string logKey = candidate.letter + ":" + candidate.hierarchy + ":" + candidate.position.ToString("F2");
                        if (log && logged.Add(logKey))
                        {
                            ThreeQsVanillaTweaks.Log.Info(
                                "Newt Altar DIAGNOSTIC: scene='" + scene.name +
                                "', marker=" + candidate.letter + ", object='" + candidate.gameObject.name +
                                "', position=" + candidate.position.ToString("F2") +
                                ", active=" + candidate.gameObject.activeInHierarchy +
                                ", hierarchy='" + candidate.hierarchy + "'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Newt Altar full-scene diagnostic failed in '" + scene.name + "': " + ex);
                }
            }

            private void OnGUI()
            {
                if (!ModConfig.Get(showMarkers) || entries.Count == 0) return;
                Camera camera = Camera.main;
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
