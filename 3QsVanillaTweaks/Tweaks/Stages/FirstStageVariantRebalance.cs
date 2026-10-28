using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.AddressableAssets;
using C = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class FirstStageVariantRebalance
    {
        private static ConfigEntry<bool> enabled;
        [ThreadStatic] private static int stagePoolDepth;
        private static readonly Dictionary<DirectorCard, int> stageRequirements = new Dictionary<DirectorCard, int>();
        private static readonly Dictionary<(string Scene, SpawnCard Card), int> copiedCardRequirements = new Dictionary<(string, SpawnCard), int>();
        private static bool rulesAttempted;
        private sealed class AvailabilityScope { internal int OriginalMinimum; }
        private sealed class CreditMarker { }
        private static readonly ConditionalWeakTable<ClassicStageInfo, CreditMarker> creditChanges = new ConditionalWeakTable<ClassicStageInfo, CreditMarker>();
        internal static bool ExternalOwnsVariants => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("Wolfo.LoopVariantConfig");
        private static bool Active => !ExternalOwnsVariants && C.Get(enabled) && NetworkServer.active && Run.instance && IsTargetScene();
        private static bool IsTargetScene()
        {
            SceneDef scene = SceneInfo.instance ? SceneInfo.instance.sceneDef : null;
            return scene && (scene.baseSceneName == "lakesnight" || scene.baseSceneName == "villagenight");
        }
        internal static void Init()
        {
            enabled = C.BindBool("Stages - Stage 1 - Variant Availability", "Rebalance Loop Variants For Stage One", true,
                "Match the supplied LoopVariantConfig DLL's spawn-card stage-completion requirements on Viscous Falls and Disturbed Impact. These are minimum stage counts, not a blanket loop-only ban. Shrine summons are unaffected. First-stage Disturbed Impact gets 280 player-scaled interactable credits minus 10 flat. Automatically yields to Wolfo's LoopVariantConfig when loaded.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.FirstStageVariantRebalance");
            harmony.Patch(AccessTools.Method(typeof(ClassicStageInfo), "RebuildCards"),
                prefix: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(BeginStagePool)),
                finalizer: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(EndStagePool)));
            harmony.Patch(AccessTools.Method(typeof(SceneDirector), "GenerateInteractableCardSelection"),
                prefix: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(BeginStagePool)),
                finalizer: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(EndStagePool)));
            harmony.Patch(AccessTools.Method(typeof(DirectorCard), "IsAvailable"),
                prefix: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(BeforeCardAvailable)),
                finalizer: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(AfterCardAvailable)));
            harmony.Patch(AccessTools.Method(typeof(SceneDirector), "Start"),
                prefix: new HarmonyMethod(typeof(FirstStageVariantRebalance), nameof(BeforeDirectorStart)));
        }
        private static void BeginStagePool(out int __state)
        {
            __state = stagePoolDepth;
            if (Active) { LoadStageRequirements(); stagePoolDepth++; }
        }
        private static Exception EndStagePool(int __state, Exception __exception)
        { stagePoolDepth = __state; return __exception; }
        private static void LoadStageRequirements()
        {
            if (rulesAttempted) return;
            rulesAttempted = true;
            try
            {
                var pending = new Dictionary<DirectorCard, int>();
                // Match Stage1_Changes.EditDccs in the supplied Wolfo DLL exactly.
                var falls = Addressables.LoadAssetAsync<DirectorCardCategorySelection>("RoR2/DLC2/lakesnight/dccsLakesnightMonsters.asset").WaitForCompletion();
                AddRule(pending, falls, 0, 2, 2);
                AddRule(pending, falls, 0, 4, 2);
                AddRule(pending, falls, 0, 5, 2);
                AddRule(pending, falls, 1, 2, 2);
                AddRule(pending, falls, 1, 3, 4);
                var interactables = Addressables.LoadAssetAsync<DirectorCardCategorySelection>("RoR2/DLC2/lakesnight/dccsLakesnightInteractables_DLC1.asset").WaitForCompletion();
                AddRule(pending, interactables, 0, 0, 1);
                var village = Addressables.LoadAssetAsync<DirectorCardCategorySelection>("RoR2/DLC2/villagenight/dccsVillageNightMonsters_Additional.asset").WaitForCompletion();
                AddRule(pending, village, 0, 1, 1);
                foreach (var rule in pending) stageRequirements.Add(rule.Key, rule.Value);
                foreach (var rule in pending)
                {
                    bool villageRule = village.categories[0].cards[1] == rule.Key;
                    copiedCardRequirements[(villageRule ? "villagenight" : "lakesnight", rule.Key.GetSpawnCard())] = rule.Value;
                }
            }
            catch (Exception error)
            {
                stageRequirements.Clear();
                copiedCardRequirements.Clear();
                ThreeQsVanillaTweaks.Log.Warning("First-stage variant spawn requirements could not load: " + error);
            }
        }
        private static void AddRule(Dictionary<DirectorCard, int> rules, DirectorCardCategorySelection selection, int category, int card, int minimum)
        {
            if (!selection || category >= selection.categories.Length || card >= selection.categories[category].cards.Length || selection.categories[category].cards[card] == null)
                throw new InvalidOperationException("LoopVariantConfig-compatible spawn-card layout was not found.");
            rules[selection.categories[category].cards[card]] = minimum;
        }
        private static void BeforeCardAvailable(DirectorCard __instance, out AvailabilityScope __state)
        {
            __state = null;
            // Only scope normal stage pools; retain the native availability checks and avoid permanent asset edits.
            if (stagePoolDepth == 0 || !Active) return;
            if (!stageRequirements.TryGetValue(__instance, out int minimum) &&
                !copiedCardRequirements.TryGetValue((SceneInfo.instance.sceneDef.baseSceneName, __instance.GetSpawnCard()), out minimum)) return;
            __state = new AvailabilityScope { OriginalMinimum = __instance.minimumStageCompletions };
            __instance.minimumStageCompletions = minimum;
        }
        private static Exception AfterCardAvailable(DirectorCard __instance, AvailabilityScope __state, Exception __exception)
        {
            if (__state != null) __instance.minimumStageCompletions = __state.OriginalMinimum;
            return __exception;
        }
        private static void BeforeDirectorStart()
        {
            if (!Active || Run.instance.stageClearCount != 0 || SceneInfo.instance.sceneDef.baseSceneName != "villagenight") return;
            ClassicStageInfo stage = ClassicStageInfo.instance;
            if (!stage || creditChanges.TryGetValue(stage, out _)) return;
            stage.sceneDirectorInteractibleCredits = 280;
            var bonuses = stage.bonusInteractibleCreditObjects ?? Array.Empty<ClassicStageInfo.BonusInteractibleCreditObject>();
            stage.bonusInteractibleCreditObjects = bonuses.Concat(new[] {
                new ClassicStageInfo.BonusInteractibleCreditObject {
                    points = -10, objectThatGrantsPointsIfEnabled = Run.instance.gameObject
                }
            }).ToArray();
            creditChanges.Add(stage, new CreditMarker());
        }
    }
}
