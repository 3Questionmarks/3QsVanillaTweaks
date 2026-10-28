using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class PrimordialTeleporters
    {
        private static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = ModConfig.BindBool("Stages - General - Primordial Teleporter", "Enabled", true,
                "Use the Primordial Teleporter on every normal stage from stage 6 onwards, allowing you to travel to the moon or continue looping.");
            new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.PrimordialTeleporters").Patch(
                AccessTools.Method(typeof(SceneDirector), "PlaceTeleporter"),
                prefix: new HarmonyMethod(typeof(PrimordialTeleporters), nameof(BeforePlaceTeleporter)));
        }

        private static void BeforePlaceTeleporter(SceneDirector __instance)
        {
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) ||
                Run.instance.stageClearCount < 5 || !__instance || !__instance.teleporterSpawnCard) return;
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            if (!scene || scene.sceneType != SceneType.Stage) return;

            try
            {
                // Replace the director's card before placement so the native teleporter
                // handles its model, alignment interaction, destinations and networking.
                SpawnCard card = StageFeatureSpawnHelper.LoadSpawnCard("iscLunarTeleporter");
                if (card && card.prefab) __instance.teleporterSpawnCard = card;
            }
            catch (Exception e)
            {
                Log.Warning("Could not use the Primordial Teleporter on this stage: " + e);
            }
        }
    }
}
