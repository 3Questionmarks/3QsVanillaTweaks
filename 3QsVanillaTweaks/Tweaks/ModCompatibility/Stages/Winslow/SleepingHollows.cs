using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.ModCompatibility.Stages.Winslow
{
    internal static class SleepingHollows
    {
        internal const string SceneName = "swampybognight_winslow";
        private const string Section = "Mod Compatibility - Stages - Sleeping Hollows";
        private static ConfigEntry<bool> mixVariants;

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S1 (Newt (1))",
                new Vector3(9.696171f, -68.564280f, -132.575800f), new Vector3(352.354000f, 156.595700f, 358.023800f));
            mixVariants = ModConfig.BindBool(Section, "Allow Before Looping", true,
                "Let Sleeping Hollows and Stagnant Pipeworks appear before and after looping. Each receives half of the family's stage selection weight.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.SleepingHollows");
            harmony.Patch(ReflectionTools.Method(typeof(SceneCollection), "AddToWeightedSelection",
                new[] { typeof(WeightedSelection<SceneDef>), typeof(Func<SceneDef, bool>) }),
                postfix: new HarmonyMethod(typeof(SleepingHollows), nameof(AfterStageCollection)));
            harmony.Patch(ReflectionTools.Method(typeof(Run), "PickNextStageScene",
                new[] { typeof(WeightedSelection<SceneDef>) }),
                prefix: new HarmonyMethod(typeof(SleepingHollows), nameof(BeforeStageSelection)) { priority = Priority.First });
            harmony.Patch(ReflectionTools.Method(typeof(SceneExitController), "GetLoopedStageOrDefault"),
                prefix: new HarmonyMethod(typeof(SleepingHollows), nameof(PreserveSelectedVariant)));
            harmony.Patch(ReflectionTools.Method(typeof(BazaarController), "IsUnlockedBeforeLooping"),
                postfix: new HarmonyMethod(typeof(SleepingHollows), nameof(AllowBazaarVariant)));
        }

        private static bool IsFamily(SceneDef scene) => scene &&
            (scene.baseSceneName == SceneName || scene.baseSceneName == StagnantPipeworks.SceneName);

        private static void AfterStageCollection(WeightedSelection<SceneDef> __0, Func<SceneDef, bool> __1)
        {
            WeightedSelection<SceneDef> split = SplitVariants(__0, __1);
            if (split == null) return;
            __0.Clear();
            for (int i = 0; i < split.Count; ++i)
            {
                var item = split.GetChoice(i);
                __0.AddChoice(item.value, item.weight);
            }
        }

        private static void BeforeStageSelection(ref WeightedSelection<SceneDef> __0)
        {
            WeightedSelection<SceneDef> split = SplitVariants(__0, null);
            if (split != null) __0 = split;
        }

        private static WeightedSelection<SceneDef> SplitVariants(WeightedSelection<SceneDef> choices,
            Func<SceneDef, bool> canAdd)
        {
            if (!ModConfig.Get(mixVariants) || !NetworkServer.active || !Run.instance || choices == null) return null;
            float familyWeight = 0f;
            var others = new List<WeightedSelection<SceneDef>.ChoiceInfo>();
            for (int i = 0; i < choices.Count; ++i)
            {
                var entry = choices.GetChoice(i);
                if (entry.value && entry.value.stageOrder != 4) return null;
                if (IsFamily(entry.value)) familyWeight += entry.weight;
                else others.Add(entry);
            }
            if (familyWeight <= 0f) return null;

            SceneDef day = SceneCatalog.FindSceneDef(StagnantPipeworks.SceneName);
            SceneDef night = SceneCatalog.FindSceneDef(SceneName);
            if (!day || !night || !Run.instance.CanPickStage(day) || !Run.instance.CanPickStage(night)) return null;
            if (canAdd != null && (!canAdd(day) || !canAdd(night))) return null;

            var split = new WeightedSelection<SceneDef>();
            foreach (var entry in others) split.AddChoice(entry.value, entry.weight);
            split.AddChoice(day, familyWeight * 0.5f);
            split.AddChoice(night, familyWeight * 0.5f);
            return split;
        }

        private static bool PreserveSelectedVariant(SceneDef __0, ref SceneDef __result)
        {
            if (!ModConfig.Get(mixVariants) || !IsFamily(__0)) return true;
            __result = __0;
            return false;
        }

        private static void AllowBazaarVariant(SceneDef __0, ref bool __result)
        {
            if (ModConfig.Get(mixVariants) && IsFamily(__0)) __result = true;
        }
    }
}
