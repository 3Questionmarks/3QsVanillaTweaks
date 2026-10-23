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
    internal static class Scrapper
    {
        private static BazaarSpawnHelper.Settings settings;
        private static Stage lastStage;

        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 03 Scrapper",
                "03 Scrapper", "RoR2/Base/Scrapper/Scrapper.prefab", 100.0f, -90.0f, -23.9f, -6.0f, 1.0f, 0.0f, -0.22f);
            BazaarSpawnHelper.Register(typeof(Scrapper), "Scrapper", nameof(AfterStageStart));
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
