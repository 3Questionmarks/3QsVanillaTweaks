using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    // Diagnostic only: does not change scene assets, selection weights or RNG state.
    internal static class StageSelectionWeights
    {
        private const string Prefix = "[Stage Weights] ";
        private static bool dumpingPools;
        private static ConfigEntry<bool> enabled;

        private sealed class Row
        {
            internal SceneDef Scene;
            internal float Weight;
        }

        internal static void Init()
        {
            enabled = ModConfig.BindBool("Debug - Stage Selection", "Log Stage Selection Weights", true,
                "Log every catalogued stage and all referenced normal/looped stage pools at run start, then log constructed pools, effective weighted choices and selected destinations throughout the run.",
                clientSide: true);
            Run.onRunStartGlobal += OnRunStart;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.StageSelectionWeights");
            harmony.Patch(AccessTools.Method(typeof(Run), "PickNextStageScene", new[] { typeof(WeightedSelection<SceneDef>) }),
                prefix: new HarmonyMethod(typeof(StageSelectionWeights), nameof(BeforePickStage))
                {
                    priority = Priority.Last,
                    after = new[]
                    {
                        "3Questionmarks.3QsVanillaTweaks.Stages.FirstStageVariants",
                        "3Questionmarks.3QsVanillaTweaks.Stages.PreviousRunStages"
                    }
                },
                postfix: new HarmonyMethod(typeof(StageSelectionWeights), nameof(AfterPickStage)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(SceneCollection), "AddToWeightedSelection",
                    new[] { typeof(WeightedSelection<SceneDef>), typeof(Func<SceneDef, bool>) }),
                postfix: new HarmonyMethod(typeof(StageSelectionWeights), nameof(AfterBuildCollection))
                {
                    priority = Priority.Last,
                    after = new[] { "3Questionmarks.3QsVanillaTweaks.Stages.FirstStageVariants" }
                });
        }

        private static bool Active => NetworkServer.active && ModConfig.Get(enabled);
        private static string Number(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static void OnRunStart(Run run)
        {
            if (Active && run) run.StartCoroutine(LogPoolsNextFrame(run));
        }

        private static IEnumerator LogPoolsNextFrame(Run run)
        {
            // Allow the remaining onRunStartGlobal subscribers to finish their asset changes.
            yield return null;
            if (!Active || !run || Run.instance != run) yield break;
            dumpingPools = true;
            try
            {
                Log.Info(Prefix + "BEGIN ALL-STAGE POOL DUMP. RAW weights come directly from scene entries; ELIGIBLE pools include DLC fallback and collection patches, before previous-run exclusion.");
                var collections = new Dictionary<SceneCollection, List<string>>();
                AddCollection(collections, run.startingSceneGroup, "Run starting pool");
                foreach (SceneDef scene in SceneCatalog.allSceneDefs)
                {
                    if (!scene) continue;
                    AddCollection(collections, scene.destinationsGroup, scene.cachedName + " normal destinations");
                    AddCollection(collections, scene.loopedDestinationsGroup, scene.cachedName + " looped destinations");
                    if (scene.sceneType == SceneType.Stage || scene.stageOrder > 0 || scene.hasAnyDestinations)
                        Log.Info(Prefix + "CATALOG " + scene.cachedName + " | " + DisplayName(scene) +
                            " | stageOrder=" + scene.stageOrder + " | type=" + scene.sceneType +
                            " | CanPickStage=" + run.CanPickStage(scene) +
                            " | normalPool=" + (scene.destinationsGroup ? scene.destinationsGroup.name : "none") +
                            " | loopedPool=" + (scene.loopedDestinationsGroup ? scene.loopedDestinationsGroup.name : "none") +
                            " | loopVariant=" + (scene.loopedSceneDef ? scene.loopedSceneDef.cachedName : "none"));
#pragma warning disable CS0612, CS0618
                    if (scene.destinations != null && scene.destinations.Length > 0)
                        DumpLegacy("LEGACY destinations from " + scene.cachedName + " (additional weight 1 per entry)", scene.destinations, run);
#pragma warning restore CS0612, CS0618
                }
                var ordered = new List<SceneCollection>(collections.Keys);
                ordered.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                foreach (SceneCollection collection in ordered)
                {
                    try { DumpCollection(run, collection.name + " | referenced by: " + string.Join(", ", collections[collection]), collection); }
                    catch (Exception e) { Log.Warning(Prefix + "Could not inspect " + collection.name + ": " + e.Message); }
                }
                if (!run.startingSceneGroup)
                    DumpLegacy("LEGACY run startingScenes (before DLC filtering)", AccessTools.Field(typeof(Run), "startingScenes")?.GetValue(run) as SceneDef[], run);
                Log.Info(Prefix + "END ALL-STAGE POOL DUMP. A stage may belong to several pools; its percentage is specific to that pool. Forced portal destinations have no universal selection weight.");
            }
            finally { dumpingPools = false; }
        }

        private static void AddCollection(Dictionary<SceneCollection, List<string>> collections,
            SceneCollection collection, string source)
        {
            if (!collection) return;
            if (!collections.TryGetValue(collection, out List<string> sources))
                collections.Add(collection, sources = new List<string>());
            sources.Add(source);
        }

        private static void DumpLegacy(string label, SceneDef[] scenes, Run run)
        {
            if (scenes == null || scenes.Length == 0) return;
            var choices = new WeightedSelection<SceneDef>();
            foreach (SceneDef scene in scenes) if (scene) choices.AddChoice(scene, 1f);
            DumpSelection(label, choices, run);
        }

        private static void DumpCollection(Run run, string label, SceneCollection collection)
        {
            var raw = new WeightedSelection<SceneDef>();
            // Do not call AddToWeightedSelection for RAW: its patches can rebalance variants.
            foreach (var entry in collection.sceneEntries)
                if (entry.sceneDef) raw.AddChoice(entry.sceneDef, entry.weight);
            DumpSelection(label + " RAW (" + collection.name + "; before DLC filtering)", raw, run);
            var eligible = new WeightedSelection<SceneDef>();
            collection.AddToWeightedSelection(eligible, run.CanPickStage);
            DumpSelection(label + " ELIGIBLE (DLC filtering and fallback scenes applied)", eligible, run);
        }

        private static void BeforePickStage(Run __instance, WeightedSelection<SceneDef> __0)
        {
            if (!Active || !__instance || __0 == null) return;
            try
            {
                Log.Info(Prefix + "Effective selection: stageClearCount=" + __instance.stageClearCount +
                    ", stageOrder=" + __instance.ruleBook.stageOrder + ". Current scene=" + (SceneCatalog.mostRecentSceneDef ? SceneCatalog.mostRecentSceneDef.cachedName : "none") + ". Captured after this mod's variant mixing and previous-run exclusion.");
                if (__instance.ruleBook.stageOrder != StageOrder.Normal)
                    Log.Info(Prefix + "Stage order is not Normal: vanilla may ignore these weighted choices. Their percentages are not the active selection probabilities in that mode.");
                DumpSelection("EFFECTIVE WEIGHTED CHOICES", __0, __instance);
            }
            catch (Exception e) { Log.Warning(Prefix + "Effective selection could not be read: " + e.Message); }
        }

        private static void AfterPickStage(Run __instance)
        {
            if (!Active || !__instance) return;
            SceneDef selected = __instance.nextStageScene;
            Log.Info(Prefix + "SELECTED DESTINATION: " + (selected ? selected.cachedName + " | " + DisplayName(selected) +
                " | stageOrder=" + selected.stageOrder : "none") + " | stageClearCount=" + __instance.stageClearCount);
        }

        private static void AfterBuildCollection(SceneCollection __instance, WeightedSelection<SceneDef> __0,
            Func<SceneDef, bool> __1)
        {
            if (!Active || dumpingPools || !Run.instance || __0 == null) return;
            try
            {
                DumpSelection("CONSTRUCTED POOL " + __instance.name +
                    (__1 == null ? " (no eligibility predicate)" : " (eligibility predicate and fallback applied)") +
                    "; destination may include earlier additions; before previous-run exclusion", __0, Run.instance);
            }
            catch (Exception e) { Log.Warning(Prefix + "Constructed pool could not be read: " + e.Message); }
        }

        private static void DumpSelection(string label, WeightedSelection<SceneDef> selection, Run run)
        {
            var rows = new List<Row>();
            float total = 0f;
            for (int i = 0; i < selection.Count; ++i)
            {
                var choice = selection.GetChoice(i);
                if (!choice.value) continue;
                Row row = rows.Find(candidate => candidate.Scene == choice.value);
                if (row == null) { row = new Row { Scene = choice.value }; rows.Add(row); }
                row.Weight += choice.weight;
                total += choice.weight;
            }
            rows.Sort((a, b) => string.Compare(a.Scene.cachedName, b.Scene.cachedName, StringComparison.Ordinal));
            Log.Info(Prefix + label + ": entries=" + selection.Count + ", distinct scenes=" + rows.Count + ", total weight=" + Number(total));
            foreach (Row row in rows)
            {
                SceneDef scene = row.Scene;
                float percent = total > 0f ? row.Weight / total * 100f : 0f;
                string expansion = scene.requiredExpansion ? scene.requiredExpansion.name : "Base game";
                Log.Info(Prefix + scene.cachedName + " | " + DisplayName(scene) + " | weight=" + Number(row.Weight) +
                    " | stageOrder=" + scene.stageOrder + " | share of this pool=" + Number(percent) + "% | expansion=" + expansion + " | CanPickStage=" + run.CanPickStage(scene));
            }
            var families = new SortedDictionary<string, float>(StringComparer.Ordinal);
            foreach (Row row in rows)
            {
                string family = Family(row.Scene);
                if (!families.ContainsKey(family)) families[family] = 0f;
                families[family] += row.Weight;
            }
            foreach (var family in families)
                Log.Info(Prefix + "FAMILY " + family.Key + " | combined weight=" + Number(family.Value) +
                    " | share of this pool=" + Number(total > 0f ? family.Value / total * 100f : 0f) + "%");
        }

        private static string DisplayName(SceneDef scene)
        {
            string translated = string.IsNullOrEmpty(scene.nameToken) ? null : Language.GetString(scene.nameToken);
            return string.IsNullOrEmpty(translated) || translated == scene.nameToken ? scene.cachedName : translated;
        }

        private static string Family(SceneDef scene)
        {
            switch (scene.baseSceneName)
            {
                case "blackbeach": case "blackbeach2": return "Distant Roost (both layouts)";
                case "golemplains": case "golemplains2": return "Titanic Plains (both layouts)";
                case "lakes": case "lakesnight": return "Verdant / Viscous Falls";
                case "village": case "villagenight": return "Shattered Abodes / Disturbed Impact";
                default:
                    var connected = new HashSet<SceneDef> { scene };
                    bool added;
                    do
                    {
                        added = false;
                        foreach (SceneDef candidate in SceneCatalog.allSceneDefs)
                        {
                            if (!candidate || !candidate.loopedSceneDef) continue;
                            if (connected.Contains(candidate) || connected.Contains(candidate.loopedSceneDef))
                            {
                                added |= connected.Add(candidate);
                                added |= connected.Add(candidate.loopedSceneDef);
                            }
                        }
                    } while (added);
                    string key = scene.cachedName;
                    foreach (SceneDef variant in connected)
                        if (string.CompareOrdinal(variant.cachedName, key) < 0) key = variant.cachedName;
                    return connected.Count > 1 ? key + " (loop variants)" : DisplayName(scene);
            }
        }
    }
}
