using System;
using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    internal static class CleansingPool
    {
        private static BazaarSpawnHelper.Settings settings;
        private static Stage lastStage;

        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 07 Cleansing Pool",
                "07 Cleansing Pool", "RoR2/Base/ShrineCleanse/ShrineCleanse.prefab", 20.0f, -65.0f, -22.7f, -18.0f, -0.54f, 0.0f, 0.88f);
            BazaarSpawnHelper.Register(typeof(CleansingPool), "CleansingPool", nameof(AfterStageStart));
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || __instance == lastStage) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, () =>
            {
                if (BazaarSpawnHelper.Roll(settings))
                    BazaarSpawnHelper.Spawn(settings.Path, settings.Name, settings.Position, settings.Forward);
            }));
        }
    }
}
