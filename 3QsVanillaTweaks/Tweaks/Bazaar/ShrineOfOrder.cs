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
    internal static class ShrineOfOrder
    {
        private static BazaarSpawnHelper.Settings settings;
        private static Stage lastStage;

        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 01 Shrine of Order",
                "01 Shrine of Order", "RoR2/Base/ShrineRestack/ShrineRestack.prefab", 50.0f, -75.0f, -24.9f, -68.0f, -0.7f, 0.0f, 0.6f);
            BazaarSpawnHelper.Register(typeof(ShrineOfOrder), "ShrineOfOrder", nameof(AfterStageStart));
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
