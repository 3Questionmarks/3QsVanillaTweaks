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
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> positionX, positionY, positionZ, forwardX, forwardY, forwardZ;
        private static readonly BazaarSpawnHelper.Settings[] slots = new BazaarSpawnHelper.Settings[3];
        private static ConfigEntry<int> maxLockboxes;
        private static ConfigEntry<float> voidChance, stepX, stepY, stepZ;
        private static Stage lastStage;

        internal static void Init()
        {
            string section = "Bazaar - 05 Extra Interactables - 04 Lockboxes";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable lockbox spawns in the normal Bazaar.");
            float previousChance = ModConfig.ReadRetiredValue(section, "Spawn Chance (%)", 50f);
            const string fallback = "Used by lockbox slots with a blank Spawn Pose.";
            positionX = ModConfig.Bind(section, "Position X", -101f, fallback);
            positionY = ModConfig.Bind(section, "Position Y", -29f, fallback);
            positionZ = ModConfig.Bind(section, "Position Z", -61f, fallback);
            forwardX = ModConfig.Bind(section, "Forward X", .41f, fallback);
            forwardY = ModConfig.Bind(section, "Forward Y", 0f, fallback);
            forwardZ = ModConfig.Bind(section, "Forward Z", .9f, fallback);
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
            string[] poses =
            {
                "-100.876762, -23, -58.439846, 5.00000143, 45, 5.00000048",
                "-96.11697, -22.75, -61.69719, 10, 40, 5.00000048",
                "-91.79322, -22.6035976, -64.767, 5.00000143, 45, 5.000001"
            };
            for (int i = 0; i < slots.Length; i++)
            {
                string name = "Lockbox " + (i + 1);
                slots[i] = BazaarSpawnHelper.Bind(section + " - " + name, name, i == 2 ? VoidPath : NormalPath,
                    50f, -101f + 5f * i, -29f, -61f - 3f * i, .41f, 0f, .9f,
                    snapToGround: false, exactPose: poses[i], unlockSection: name);
                ModConfig.UseMigratedValue(slots[i].Chance, previousChance);
            }
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
            bool unlock = BazaarAlternateMode.Active;
            if (!unlock && !ModConfig.Get(enabled)) return;
            int count = unlock ? slots.Length : Mathf.Clamp(ModConfig.Get(maxLockboxes), 0, slots.Length);
            for (int i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (!BazaarSpawnHelper.Roll(slot)) continue;
                bool canSpawnVoid = SpawnRequirements.AddressAllowed(VoidPath);
                if (unlock && slot.Path == VoidPath && !canSpawnVoid) continue;
                bool isVoid = canSpawnVoid && (unlock ? slot.Path == VoidPath :
                    UnityEngine.Random.value * 100f < Mathf.Clamp(ModConfig.Get(voidChance), 0f, 100f));
                Vector3 position = slot.Position;
                Vector3 facing = slot.Forward;
                if (!slot.TryGetPose(out _))
                {
                    position = new Vector3(ModConfig.Get(positionX), ModConfig.Get(positionY), ModConfig.Get(positionZ)) +
                        i * new Vector3(ModConfig.Get(stepX), ModConfig.Get(stepY), ModConfig.Get(stepZ));
                    facing = new Vector3(ModConfig.Get(forwardX), ModConfig.Get(forwardY), ModConfig.Get(forwardZ));
                }
                BazaarSpawnHelper.Spawn(slot, isVoid ? VoidPath : NormalPath,
                    slot.Name + (isVoid ? " (Void)" : ""), position, facing);
            }
        }
    }
}
