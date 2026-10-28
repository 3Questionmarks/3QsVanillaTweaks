using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class ThirdStageVariants
    {
        private static ConfigEntry<bool> iron, colony;

        internal static void Init()
        {
            const string section = "Stages - Stage 3 - Variant Availability";
            iron = ModConfig.BindBool(section, "Mix Iron Alluvium And Iron Auroras", true,
                "Give each Iron variant half of the family's selection chance before and after looping. Ignored while Wolfo's LoopVariantConfig is loaded.");
            colony = ModConfig.BindBool(section, "Mix Treeborn Colony And Golden Dieback", true,
                "Give each Colony variant half of the family's selection chance before and after looping. Ignored while Wolfo's LoopVariantConfig is loaded.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.ThirdStageVariants");
            harmony.Patch(AccessTools.Method(typeof(SceneCollection), "AddToWeightedSelection",
                    new[] { typeof(WeightedSelection<SceneDef>), typeof(Func<SceneDef, bool>) }),
                postfix: new HarmonyMethod(typeof(ThirdStageVariants), nameof(AfterBuildCollection)));
            harmony.Patch(AccessTools.Method(typeof(Run), "PickNextStageScene", new[] { typeof(WeightedSelection<SceneDef>) }),
                prefix: new HarmonyMethod(typeof(ThirdStageVariants), nameof(BeforePickStage)));
            harmony.Patch(AccessTools.Method(typeof(SceneExitController), "GetLoopedStageOrDefault"),
                prefix: new HarmonyMethod(typeof(ThirdStageVariants), nameof(BeforeLoopReplacement)));
            harmony.Patch(AccessTools.Method(typeof(BazaarController), "IsUnlockedBeforeLooping"),
                postfix: new HarmonyMethod(typeof(ThirdStageVariants), nameof(AfterBazaarUnlockCheck)));
        }

        private static bool Mixed(SceneDef scene)
        {
            if (FirstStageVariantRebalance.ExternalOwnsVariants || !scene) return false;
            switch (scene.baseSceneName)
            {
                case "ironalluvium": case "ironalluvium2": return ModConfig.Get(iron);
                case "habitat": case "habitatfall": return ModConfig.Get(colony);
                default: return false;
            }
        }

        private static void BeforePickStage(Run __instance, ref WeightedSelection<SceneDef> __0)
        {
            if (!NetworkServer.active || __0 == null) return;
            // Fallback for callers which build their choices without a SceneCollection.
            WeightedSelection<SceneDef> replacement = Rebalance(__instance, __0, null);
            if (replacement != null) __0 = replacement;
        }

        private static void AfterBuildCollection(WeightedSelection<SceneDef> __0, Func<SceneDef, bool> __1)
        {
            if (!NetworkServer.active || !Run.instance || __0 == null) return;
            WeightedSelection<SceneDef> replacement = Rebalance(Run.instance, __0, __1);
            if (replacement == null) return;

            // Mutate the destination object supplied by the caller. Replacing this
            // method's argument would leave the caller holding the old zero-weight pool.
            // The source SceneCollection asset remains untouched, so disabling a mix
            // immediately restores vanilla weights the next time a pool is built.
            __0.Clear();
            for (int i = 0; i < replacement.Count; ++i)
            {
                var choice = replacement.GetChoice(i);
                __0.AddChoice(choice.value, choice.weight);
            }
        }

        private static WeightedSelection<SceneDef> Rebalance(Run run, WeightedSelection<SceneDef> choices,
            Func<SceneDef, bool> canAdd)
        {
            if (FirstStageVariantRebalance.ExternalOwnsVariants || !run) return null;
            var entries = new List<KeyValuePair<SceneDef, float>>();
            bool stageThree = false;
            for (int i = 0; i < choices.Count; ++i)
            {
                var choice = choices.GetChoice(i);
                if (choice.value)
                {
                    // Do not alter all-stage/randomiser collections containing other stages.
                    if (choice.value.stageOrder != 3) return null;
                    stageThree = true;
                }
                entries.Add(new KeyValuePair<SceneDef, float>(choice.value, choice.weight));
            }
            if (!stageThree) return null;
            bool changed = false;
            if (ModConfig.Get(iron)) changed |= SplitFamily(run, entries, "ironalluvium", "ironalluvium2", canAdd);
            if (ModConfig.Get(colony)) changed |= SplitFamily(run, entries, "habitat", "habitatfall", canAdd);
            if (!changed) return null;
            var replacement = new WeightedSelection<SceneDef>();
            foreach (var entry in entries) replacement.AddChoice(entry.Key, entry.Value);
            return replacement;
        }

        private static bool SplitFamily(Run run, List<KeyValuePair<SceneDef, float>> entries, string dayName, string nightName,
            Func<SceneDef, bool> canAdd)
        {
            SceneDef day = SceneCatalog.FindSceneDef(dayName);
            SceneDef night = SceneCatalog.FindSceneDef(nightName);
            if (!day || !night || !run.CanPickStage(day) || !run.CanPickStage(night)) return false;
            if (canAdd != null && (!canAdd(day) || !canAdd(night))) return false;
            float weight = 0f;
            bool present = false;
            foreach (var entry in entries)
            {
                if (entry.Key == day || entry.Key == night) { weight += entry.Value; present = true; }
            }
            if (!present || weight <= 0f) return false;
            entries.RemoveAll(entry => entry.Key == day || entry.Key == night);
            entries.Add(new KeyValuePair<SceneDef, float>(day, weight * 0.5f));
            entries.Add(new KeyValuePair<SceneDef, float>(night, weight * 0.5f));
            return true;
        }

        private static bool BeforeLoopReplacement(SceneDef __0, ref SceneDef __result)
        {
            if (!Mixed(__0)) return true;
            // Honour the selected variant instead of forcing its looped counterpart.
            __result = __0;
            return false;
        }

        private static void AfterBazaarUnlockCheck(SceneDef __0, ref bool __result)
        {
            if (Mixed(__0)) __result = true;
        }
    }
}
