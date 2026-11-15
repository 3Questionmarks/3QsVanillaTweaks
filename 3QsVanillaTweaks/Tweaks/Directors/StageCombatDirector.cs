using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Directors
{
    internal static class StageCombatDirector
    {
        private const string Section = "Directors - Stage Combat";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> startingCreditsMultiplier;
        private static ConfigEntry<int> maximumLivingMonsters;
        private static ConfigEntry<int> maximumSpawnsPerWave;
        private static ConfigEntry<float> spawnRateMultiplier;

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enabled", true,
                "Adjust initial enemies and regular enemy spawning on normal stages.");
            startingCreditsMultiplier = C.Bind(Section, "Initial Monster Credits Multiplier", 1.2f,
                new ConfigDescription("Multiplies the credits used to place enemies when entering a stage. Vanilla: 1.",
                    new AcceptableValueRange<float>(0f, 10f)));
            maximumLivingMonsters = C.Bind(Section, "Maximum Living Monsters", 60,
                new ConfigDescription("Maximum monsters alive at once before ordinary monster spawns are blocked. Special spawns can exceed this limit. Vanilla: 40.",
                    new AcceptableValueRange<int>(1, 500)));
            maximumSpawnsPerWave = C.Bind(Section, "Maximum Spawns Per Wave", 8,
                new ConfigDescription("Maximum enemies the regular director attempts to spawn in one wave. Vanilla: 6.",
                    new AcceptableValueRange<int>(1, 100)));
            spawnRateMultiplier = C.Bind(Section, "Spawn Rate Multiplier", 1f,
                new ConfigDescription("How quickly the regular director attempts to spend its credits. Higher is faster; it does not grant extra credits. Vanilla: 1.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Directors.StageCombat");
            harmony.Patch(AccessTools.Method(typeof(SceneDirector), "PopulateScene"),
                prefix: new HarmonyMethod(typeof(StageCombatDirector), nameof(BeforePopulateScene)));
            SceneDirector.onPostPopulateSceneServer += ConfigureAmbientDirectors;
        }

        private static bool NormalStage(SceneDirector sceneDirector)
        {
            if (!NetworkServer.active || !Run.instance || !sceneDirector || !SceneInfo.instance) return false;
            SceneDef scene = SceneInfo.instance.sceneDef;
            return scene && scene.sceneType == SceneType.Stage &&
                scene.baseSceneName != "meridian" && scene.baseSceneName != "moon2";
        }

        private static void BeforePopulateScene(SceneDirector __instance, ref int ___monsterCredit)
        {
            if (C.Get(enabled) && NetworkServer.active && Run.instance)
            {
                TeamDef monsterTeam = TeamCatalog.GetTeamDef(TeamIndex.Monster);
                if (monsterTeam != null)
                    monsterTeam.softCharacterLimit = C.Get(maximumLivingMonsters);
            }
            if (!C.Get(enabled) || !NormalStage(__instance)) return;
            ___monsterCredit = Mathf.Max(0,
                Mathf.RoundToInt(___monsterCredit * C.Get(startingCreditsMultiplier)));
        }

        private static void ConfigureAmbientDirectors(SceneDirector sceneDirector)
        {
            if (!C.Get(enabled) || !NormalStage(sceneDirector)) return;
            float rate = Mathf.Max(0.1f, C.Get(spawnRateMultiplier));
            int limit = C.Get(maximumSpawnsPerWave);
            foreach (CombatDirector director in UnityEngine.Object.FindObjectsOfType<CombatDirector>())
            {
                if (!director || director.gameObject.scene != sceneDirector.gameObject.scene ||
                    director.GetComponent<SceneDirector>() || director.shouldSpawnOneWave ||
                    director.teamIndex != TeamIndex.Monster || !director.targetPlayers ||
                    director.GetComponentInParent<TeleporterInteraction>() ||
                    director.GetComponentInParent<HoldoutZoneController>()) continue;

                director.maximumNumberToSpawnBeforeSkipping = limit;
                director.minSeriesSpawnInterval /= rate;
                director.maxSeriesSpawnInterval /= rate;
                director.minRerollSpawnInterval /= rate;
                director.maxRerollSpawnInterval /= rate;
            }
        }
    }
}
