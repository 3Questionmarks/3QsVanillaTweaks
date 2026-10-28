using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class AurelioniteFragmentSpawnHelper
    {
        internal enum LevelMode { Level1, Level2, Level3, Random, WeightedRandom }

        internal sealed class Settings
        {
            internal StageFeatureSpawnHelper.Settings Spawn;
            internal ConfigEntry<LevelMode> Mode;
            internal ConfigEntry<float> Level1Weight, Level2Weight, Level3Weight;
        }

        internal static Settings Bind(string section, string sceneName, string name, float x, float y, float z)
        {
            return new Settings
            {
                Spawn = StageFeatureSpawnHelper.Bind(section, sceneName, name, x, y, z, heightOffset: 1.5f),
                Mode = ModConfig.Bind(section, "Fragment Level", LevelMode.WeightedRandom,
                    "Level1: choose 1 of 3 items. Level2: choose 2 of 4. Level3: choose 3 of 5. Random: equal chances. WeightedRandom: use the three configurable weights."),
                Level1Weight = Weight(section, "Level 1 Weight", 60f),
                Level2Weight = Weight(section, "Level 2 Weight", 30f),
                Level3Weight = Weight(section, "Level 3 Weight", 10f)
            };
        }

        private static ConfigEntry<float> Weight(string section, string key, float value)
        {
            return ModConfig.Bind(section, key, value,
                new ConfigDescription("Relative weight in WeightedRandom mode; zero excludes this level. If all weights are zero, use level 1.",
                    new AcceptableValueRange<float>(0f, 100000f)));
        }

        internal static void Register(Settings settings)
        {
            StageFeatureSpawnHelper.Register(settings.Spawn, (spawn, rng) => Spawn(settings, rng));
        }

        private static int ChooseLevel(Settings settings, Xoroshiro128Plus rng)
        {
            switch (ModConfig.Get(settings.Mode))
            {
                case LevelMode.Level1: return 1;
                case LevelMode.Level2: return 2;
                case LevelMode.Level3: return 3;
                case LevelMode.Random: return rng.RangeInt(1, 4);
                default:
                    float a = Mathf.Max(0f, ModConfig.Get(settings.Level1Weight));
                    float b = Mathf.Max(0f, ModConfig.Get(settings.Level2Weight));
                    float c = Mathf.Max(0f, ModConfig.Get(settings.Level3Weight));
                    float total = a + b + c;
                    if (total <= 0f || float.IsNaN(total)) return 1;
                    float roll = rng.nextNormalizedFloat * total;
                    if (roll < a) return 1;
                    if (roll < a + b) return 2;
                    return 3;
            }
        }

        // These are private serialised fields in the supplied game DLL. Read the prefab's
        // actual tables rather than rebuilding its item pools or spawning a hidden shrine.
        private static T PrefabField<T>(HalcyoniteShrineInteractable shrine, string fieldName)
        {
            var field = AccessTools.Field(typeof(HalcyoniteShrineInteractable), fieldName);
            if (field == null || !(field.GetValue(shrine) is T value))
                throw new InvalidOperationException("Halcyon reward field missing: " + fieldName);
            return value;
        }

        private static void Spawn(Settings settings, Xoroshiro128Plus rng)
        {
            if (!StageFeatureSpawnHelper.ExpansionEnabled("RoR2/DLC2/Common/DLC2.asset")) return;
            GameObject shrinePrefab = StageFeatureSpawnHelper.LoadPrefab("RoR2/DLC2/ShrineHalcyonite.prefab");
            HalcyoniteShrineInteractable shrine = shrinePrefab.GetComponent<HalcyoniteShrineInteractable>();
            if (!shrine) throw new InvalidOperationException("Halcyon prefab lacks its reward configuration.");
            int level = ChooseLevel(settings, rng);
            StageFeatureSpawnHelper.GetPlacement(settings.Spawn, out Vector3 position, out Quaternion rotation);

            // Preserve the vanilla Command replacement: levels 1/2/3 become 1/2/3
            // normal item droplets, which the game converts to Command cubes.
            if (RunArtifactManager.instance && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.Command))
            {
                PickupDropTable table = PrefabField<PickupDropTable>(shrine, "halcyoniteDropTableTier" + level);
                for (int i = 0; i < level; ++i)
                {
                    var info = new GenericPickupController.CreatePickupInfo
                    {
                        pickup = table.GeneratePickup(rng),
                        position = position,
                        rotation = rotation
                    };
                    float angle = i * 360f / level;
                    Vector3 velocity = Quaternion.AngleAxis(angle, Vector3.up) * new Vector3(2f, 6f, 0f);
                    PickupDropletController.CreatePickupDroplet(info, position, velocity);
                }
            }
            else
            {
                GameObject pickupPrefab = PrefabField<GameObject>(shrine, "rewardPickupPrefab");
                if (!StageFeatureSpawnHelper.PrefabAllowed(pickupPrefab)) return;
                var info = new GenericPickupController.CreatePickupInfo
                {
                    pickup = new UniquePickup(PickupCatalog.FindPickupIndex(PrefabField<ItemTier>(shrine, "rewardDisplayTier"))),
                    pickerOptions = PickupPickerController.GenerateOptionsFromDropTablePlusForcedStorm(level + 2,
                        PrefabField<PickupDropTable>(shrine, "halcyoniteDropTableTier3"),
                        PrefabField<PickupDropTable>(shrine, "halcyoniteDropTableTier2"), rng),
                    // CreatePickup derives its pick limit from pickerOptions.Length - 2.
                    // This keeps levels 1/2/3 correct without the newer optional maxOptionCount field.
                    prefabOverride = pickupPrefab,
                    position = position,
                    rotation = rotation
                };
                GenericPickupController.CreatePickup(info);
            }
            Log.Info("Stage feature spawned " + settings.Spawn.Name + " (level " + level + ") at " + position);
        }
    }
}
