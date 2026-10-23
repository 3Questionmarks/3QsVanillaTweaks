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
    internal static class VoidCradle
    {
        private static BazaarSpawnHelper.Settings settings;
        private static Stage lastStage;

        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 05 Void Cradle",
                "05 Void Cradle", "RoR2/DLC1/VoidChest/VoidChest.prefab", 15.0f, 44.4f, -274.8f, 13.1f, -0.2f, 0.0f, -0.97f);
            BazaarSpawnHelper.Register(typeof(VoidCradle), "VoidCradle", nameof(AfterStageStart));
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
