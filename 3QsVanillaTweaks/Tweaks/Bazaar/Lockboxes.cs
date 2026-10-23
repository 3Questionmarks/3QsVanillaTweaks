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
    internal static class Lockboxes
    {
        private const string NormalPath = "RoR2/Base/TreasureCache/Lockbox.prefab";
        private const string VoidPath = "RoR2/DLC1/TreasureCacheVoid/LockboxVoid.prefab";
        private static BazaarSpawnHelper.Settings settings;
        private static ConfigEntry<int> maxLockboxes;
        private static ConfigEntry<float> voidChance, stepX, stepY, stepZ;
        private static Stage lastStage;

        internal static void Init()
        {
            string section = "Bazaar - 05 Extra Interactables - 04 Lockboxes";
            settings = BazaarSpawnHelper.Bind(section, "04 Lockboxes", NormalPath,
                50f, -101f, -23f, -61f, 0.41f, 0f, 0.9f);
            maxLockboxes = ModConfig.Bind(section, "Maximum Lockboxes", 3,
                "Number of independent lockbox positions to roll, 0-3.");
            voidChance = ModConfig.Bind(section, "Void Replacement Chance (0-100%)", 25f,
                "Chance each spawned lockbox becomes a void lockbox (0-100%).");
            stepX = ModConfig.Bind(section, "Position Step X", 5f,
                "X shift between successive lockboxes.");
            stepY = ModConfig.Bind(section, "Position Step Y", 0f,
                "Y shift between successive lockboxes (default 0).");
            stepZ = ModConfig.Bind(section, "Position Step Z", -3f,
                "Z shift between successive lockboxes (default -3).");
            BazaarSpawnHelper.Register(typeof(Lockboxes), "Lockboxes", nameof(AfterStageStart));
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || __instance == lastStage) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, SpawnAll));
        }

        private static void SpawnAll()
        {
            int count = Mathf.Clamp(ModConfig.Get(maxLockboxes), 0, 3);
            for (int i = 0; i < count; i++)
            {
                if (!BazaarSpawnHelper.Roll(settings)) continue;
                bool isVoid = UnityEngine.Random.value * 100f < Mathf.Clamp(ModConfig.Get(voidChance), 0f, 100f);
                Vector3 position = settings.Position + i * new Vector3(
                    ModConfig.Get(stepX), ModConfig.Get(stepY), ModConfig.Get(stepZ));
                BazaarSpawnHelper.Spawn(isVoid ? VoidPath : NormalPath,
                    "Lockbox " + (i + 1) + (isVoid ? " (Void)" : ""), position, settings.Forward);
            }
        }
    }
}
