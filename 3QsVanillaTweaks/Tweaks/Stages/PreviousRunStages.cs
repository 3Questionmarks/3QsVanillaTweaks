using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class PreviousRunStages
    {
        private enum HistoryScope { PlaySession, PreviousRun }
        private const string Section = "Stages - Previous Run";
        private static ConfigEntry<bool> enabled, debug;
        private static ConfigEntry<HistoryScope> scope;
        private static Run activeRun;
        private static bool loaded;
        private static string historyPath;
        private static readonly HashSet<string> LastVisited = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> PreviousRunVisited = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> CurrentVisited = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> SessionVisited = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Families = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly MethodInfo BazaarUnlocked = AccessTools.Method(typeof(BazaarController), "IsUnlockedBeforeLooping");

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Avoid Previous Run Stages", true,
                "Avoid selecting visited stages while an unvisited eligible destination exists. Alternate layouts and day/night variants count as the same stage. History follows the host.");
            scope = ModConfig.Bind(Section, "History Scope", HistoryScope.PlaySession,
                "PlaySession avoids stages visited since the host launched the game, including earlier in the current run. PreviousRun avoids stages from the last hosted run and remembers them after a restart.");
            debug = ModConfig.BindBool("Debug - Stage History", "Log Stage History", true,
                "Log visited stage families, exclusions and fallback choices. Requires Use Debug Options.");
            historyPath = Path.Combine(Path.GetDirectoryName(ModConfig.File.ConfigFilePath),
                Path.GetFileNameWithoutExtension(ModConfig.File.ConfigFilePath) + ".StageHistory.txt");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.PreviousRunStages");
            harmony.Patch(AccessTools.Method(typeof(Run), "Start"),
                prefix: new HarmonyMethod(typeof(PreviousRunStages), nameof(BeforeRunStart)));
            harmony.Patch(AccessTools.Method(typeof(Run), "PickNextStageScene", new[] { typeof(WeightedSelection<SceneDef>) }),
                prefix: new HarmonyMethod(typeof(PreviousRunStages), nameof(BeforePickStage))
                {
                    priority = Priority.Last,
                    after = new[] { "3Questionmarks.3QsVanillaTweaks.Stages.FirstStageVariants" }
                });
            harmony.Patch(AccessTools.Method(typeof(BazaarController), "SetUpSeerStations"),
                postfix: new HarmonyMethod(typeof(PreviousRunStages), nameof(AfterSetUpSeers)));
            Stage.onStageStartGlobal += OnStageStart;
        }

        private static void Trace(string message)
        {
            if (ModConfig.Get(debug)) Log.Info("Stage history: " + message);
        }

        private static void BeforeRunStart(Run __instance)
        {
            if (NetworkServer.active) BeginRun(__instance);
        }

        private static void BeginRun(Run run)
        {
            if (ReferenceEquals(activeRun, run)) return;
            if (!loaded)
            {
                loaded = true;
                try
                {
                    if (File.Exists(historyPath))
                        foreach (string line in File.ReadAllLines(historyPath))
                            if (!string.IsNullOrWhiteSpace(line)) LastVisited.Add(line.Trim());
                }
                catch (Exception e) { Log.Warning("Could not read stage history: " + e.Message); }
            }
            activeRun = run;
            CurrentVisited.Clear();
            Families.Clear();
            // Link loop variants in either direction, then merge the two vanilla alternate layouts.
            foreach (SceneDef scene in SceneCatalog.allSceneDefs)
                if (scene && scene.loopedSceneDef) Union(scene.baseSceneName, scene.loopedSceneDef.baseSceneName);
            Union("blackbeach", "blackbeach2");
            Union("golemplains", "golemplains2");
            // Explicit aliases also cover catalogues where a mod removes loop links.
            Union("lakes", "lakesnight");
            Union("village", "villagenight");
            PreviousRunVisited.Clear();
            foreach (string stage in LastVisited) PreviousRunVisited.Add(Root(stage));
            Trace(ModConfig.Get(scope) + " history at run start: " + string.Join(", ", VisitedForSelection));
        }

        private static string Root(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (!Families.TryGetValue(name, out string parent)) Families[name] = parent = name;
            if (parent != name) Families[name] = parent = Root(parent);
            return parent;
        }

        private static void Union(string a, string b)
        {
            a = Root(a); b = Root(b);
            if (a.Length == 0 || b.Length == 0 || a == b) return;
            if (string.CompareOrdinal(a, b) < 0) Families[b] = a;
            else Families[a] = b;
        }

        private static HashSet<string> VisitedForSelection =>
            ModConfig.Get(scope) == HistoryScope.PlaySession ? SessionVisited : PreviousRunVisited;

        private static bool WasVisited(SceneDef scene)
        {
            return scene && scene.sceneType == SceneType.Stage && VisitedForSelection.Contains(Root(scene.baseSceneName));
        }

        private static void OnStageStart(Stage stage)
        {
            if (!NetworkServer.active || !Run.instance) return;
            BeginRun(Run.instance);
            RecordVisit(stage && stage.sceneDef ? stage.sceneDef : SceneCatalog.mostRecentSceneDef);
        }

        private static void RecordVisit(SceneDef scene)
        {
            if (!scene || scene.sceneType != SceneType.Stage || string.IsNullOrEmpty(scene.baseSceneName)) return;
            string family = Root(scene.baseSceneName);
            if (!CurrentVisited.Add(family)) return;

            // Retain both histories so changing scope mid-session can use earlier visits.
            SessionVisited.Add(family);
            LastVisited.Clear();
            LastVisited.UnionWith(CurrentVisited);
            try
            {
                var lines = new List<string>(LastVisited);
                lines.Sort(StringComparer.Ordinal);
                string temporary = historyPath + ".tmp";
                File.WriteAllLines(temporary, lines);
                if (File.Exists(historyPath)) File.Replace(temporary, historyPath, null);
                else File.Move(temporary, historyPath);
            }
            catch (Exception e) { Log.Warning("Could not save stage history: " + e.Message); }
            Trace("visited " + scene.baseSceneName);
        }

        private static bool BeforePickStage(Run __instance, ref WeightedSelection<SceneDef> __0)
        {
            if (!NetworkServer.active || !__instance || __0 == null) return true;
            BeginRun(__instance);
            if (!ModConfig.Get(enabled)) return true;
            // A stage can select its next destination before onStageStartGlobal fires.
            // Only record a scene that has actually become the active stage.
            if (ModConfig.Get(scope) == HistoryScope.PlaySession && __instance == Run.instance &&
                SceneInfo.instance && SceneInfo.instance.gameObject.scene == SceneManager.GetActiveScene())
                RecordVisit(SceneInfo.instance.sceneDef);
            if (VisitedForSelection.Count == 0) return true;
            if (__instance.ruleBook.stageOrder != StageOrder.Normal)
            {
                // The game's random-stage-order branch ignores the supplied weighted pool.
                // Recreate its candidate rules, filtering only if an unvisited choice exists.
                var all = new List<SceneDef>();
                foreach (SceneDef scene in SceneCatalog.allStageSceneDefs)
                {
                    if (__instance.nextStageScene && __instance.nextStageScene.baseSceneName == scene.baseSceneName) continue;
                    if (!scene.hasAnyDestinations || !scene.validForRandomSelection || !__instance.CanPickStage(scene)) continue;
                    if (__instance.stageClearCount == 0 && Array.IndexOf(__instance.blacklistedScenesForFirstScene, scene) >= 0) continue;
                    all.Add(scene);
                }
                var allowed = all.FindAll(scene => !WasVisited(scene));
                if (allowed.Count == 0 || allowed.Count == all.Count) return true;
                __instance.nextStageScene = allowed[__instance.nextStageRng.RangeInt(0, allowed.Count)];
                Trace("filtered random-stage-order pool from " + all.Count + " to " + allowed.Count);
                return false;
            }
            bool hasAlternative = false, hasBlocked = false;
            for (int i = 0; i < __0.Count; ++i)
            {
                var choice = __0.GetChoice(i);
                if (!choice.value || choice.weight <= 0f) continue;
                if (WasVisited(choice.value)) hasBlocked = true;
                else hasAlternative = true;
            }
            if (!hasBlocked) return true;
            if (!hasAlternative)
            {
                Trace("all positive-weight destinations visited; keeping the original pool.");
                return true;
            }
            var replacement = new WeightedSelection<SceneDef>();
            for (int i = 0; i < __0.Count; ++i)
            {
                var choice = __0.GetChoice(i);
                if (!WasVisited(choice.value)) replacement.AddChoice(choice.value, choice.weight);
            }
            // Relative weights remain unchanged; zero-weight scenes never become alternatives.
            __0 = replacement;
            Trace("removed previously visited destinations from stage selection.");
            return true;
        }

        private static void AfterSetUpSeers(BazaarController __instance)
        {
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled)) return;
            BeginRun(Run.instance);
            if (VisitedForSelection.Count == 0 || __instance.seerStations == null) return;
            var used = new HashSet<SceneDef>();
            foreach (SeerStationController station in __instance.seerStations)
            {
                if (!station) continue;
                SceneDef target = SceneCatalog.GetSceneDef((SceneIndex)station.ReturnTargetSceneDefIndex());
                if (target && !WasVisited(target)) used.Add(target);
            }
            foreach (SeerStationController station in __instance.seerStations)
            {
                if (!station) continue;
                SceneDef target = SceneCatalog.GetSceneDef((SceneIndex)station.ReturnTargetSceneDefIndex());
                if (!WasVisited(target)) continue;
                var alternatives = new List<SceneDef>();
                foreach (SceneDef scene in SceneCatalog.allSceneDefs)
                {
                    if (!scene || scene.filterOutOfBazaar || scene.sceneType == SceneType.Junk ||
                        scene.stageOrder != target.stageOrder || !Run.instance.CanPickStage(scene) || WasVisited(scene)) continue;
                    if (!(bool)BazaarUnlocked.Invoke(__instance, new object[] { scene })) continue;
                    alternatives.Add(scene);
                }
                if (alternatives.Count == 0) continue; // All eligible destinations were visited.
                var distinct = alternatives.FindAll(scene => !used.Contains(scene));
                if (distinct.Count > 0) alternatives = distinct;
                SceneDef replacement = alternatives[Run.instance.nextStageRng.RangeInt(0, alternatives.Count)];
                station.SetTargetScene(replacement);
                used.Add(replacement);
                Trace("Bazaar preview changed from " + target.baseSceneName + " to " + replacement.baseSceneName);
            }
        }
    }
}
