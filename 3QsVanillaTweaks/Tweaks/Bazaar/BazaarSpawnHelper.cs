using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using Tweaks.DebugStuff;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    // Shared utilities only. No Init() method: the plugin initializes each spawn tweak independently.
    internal static class BazaarSpawnHelper
    {
        internal sealed class Settings
        {
            internal string Name, Path, EditorSlot;
            internal BazaarAlternateMode.Slot Unlock;
            internal ConfigEntry<string> ExactPose;
            internal BazaarAlternateMode.Slot ActiveUnlock => BazaarAlternateMode.Active ? Unlock : null;
            internal ConfigEntry<bool> Enabled;
            internal ConfigEntry<int> UseCost;
            internal GroundSettings Ground;
            internal ConfigEntry<float> Chance, X, Y, Z, FX, FY, FZ;
            internal bool TryGetPose(out Pose pose) => TryParsePose(ModConfig.Get(ExactPose), out pose);
            internal Vector3 Position => new Vector3(ModConfig.Get(X), ModConfig.Get(Y), ModConfig.Get(Z));
            internal Vector3 Forward => new Vector3(ModConfig.Get(FX), ModConfig.Get(FY), ModConfig.Get(FZ));
        }

        internal sealed class GroundSettings
        {
            internal ConfigEntry<bool> SnapToGround, AlignToGround;
            internal ConfigEntry<float> HeightOffset, SearchDistance;

            internal void GetPlacement(string name, Vector3 configuredPosition, Vector3 facing,
                out Vector3 position, out Quaternion rotation)
            {
                Tweaks.Stages.StageFeatureSpawnHelper.GetPlacement(name, configuredPosition, facing,
                    ModConfig.Get(SnapToGround), ModConfig.Get(AlignToGround),
                    ModConfig.Get(HeightOffset), ModConfig.Get(SearchDistance), out position, out rotation);
            }
        }

        internal static GroundSettings BindGround(string section, bool snapToGround = true,
            bool alignToGround = true, float heightOffset = 0f)
        {
            return new GroundSettings
            {
                SnapToGround = ModConfig.BindBool(section, "Snap To Ground", snapToGround,
                    "Move the interactable to the ground nearest Position Y."),
                AlignToGround = ModConfig.BindBool(section, "Align To Ground", alignToGround,
                    "Tilt the interactable to match the ground. Disable to keep it upright."),
                HeightOffset = ModConfig.Bind(section, "Height Offset", heightOffset,
                    "World-space vertical offset added after snapping, or to Position Y if snapping is disabled."),
                SearchDistance = ModConfig.Bind(section, "Ground Search Distance", 12f,
                    new ConfigDescription("Search this far above and below Position Y; use the nearest walkable surface.",
                        new AcceptableValueRange<float>(0.1f, 100f)))
            };
        }

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        internal const string ExtraSection = "Bazaar - 05 Extra Interactables - ";
        internal const string PoseDescription = "World position X, Y, Z followed by rotation X, Y, Z in degrees.";

        internal static Settings Bind(string section, string name, string path, float chance,
            float x, float y, float z, float fx, float fy, float fz, bool snapToGround = true,
            bool alignToGround = true, float heightOffset = 0f,
            string exactPose = null, string unlockSection = null, string editorSlot = null,
            string enabledKey = "Enabled", string enabledDescription = null)
        {
            var s = new Settings { Name = name, Path = path, EditorSlot = editorSlot ?? section };
            s.Enabled = ModConfig.BindBool(section, enabledKey, true,
                enabledDescription ?? "Enable this extra interactable in the normal Bazaar.");
            s.Chance = ModConfig.Bind(section, "Spawn Chance (%)", chance,
                new ConfigDescription("Spawn probability per normal Bazaar visit.", new AcceptableValueRange<float>(0f, 100f)));
            s.X = ModConfig.Bind(section, "Position X", x, "Absolute world-space X. Used when Spawn Pose is blank.");
            s.Y = ModConfig.Bind(section, "Position Y", y, "Absolute world-space Y. Used when Spawn Pose is blank.");
            s.Z = ModConfig.Bind(section, "Position Z", z, "Absolute world-space Z. Used when Spawn Pose is blank.");
            s.FX = ModConfig.Bind(section, "Forward X", fx, "World-space facing vector X. Used when Spawn Pose is blank.");
            s.FY = ModConfig.Bind(section, "Forward Y", fy, "World-space facing vector Y. Used when Spawn Pose is blank.");
            s.FZ = ModConfig.Bind(section, "Forward Z", fz, "World-space facing vector Z. Used when Spawn Pose is blank.");
            s.Ground = BindGround(section, snapToGround, alignToGround, heightOffset);
            if (exactPose != null)
                s.ExactPose = ModConfig.Bind(section, "Spawn Pose", exactPose, PoseDescription +
                    " Blank uses Position, Forward and ground settings.");

            string suffix = unlockSection ?? section.Substring("Bazaar - ".Length);
            string displayName = NormalName(name);
            s.Unlock = BazaarAlternateMode.BindSlot(suffix, displayName);
            s.UseCost = BazaarUseCosts.BindSpawn(section, displayName);
            return s;
        }

        internal static Settings BindFormerUnlockOnly(string name, string path, float x, float y, float z,
            float fx, float fy, float fz, string exactPose = null, float chance = 0f)
        {
            return Bind(ExtraSection + name, name, path, chance, x, y, z, fx, fy, fz,
                snapToGround: false, alignToGround: false,
                exactPose: exactPose, unlockSection: name);
        }

        private static string NormalName(string name) =>
            name.Length > 3 && char.IsDigit(name[0]) && char.IsDigit(name[1]) && name[2] == ' ' ? name.Substring(3) : name;

        private static bool TryParsePose(string value, out Pose pose)
        {
            pose = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] fields = value.Split(',');
            if (fields.Length != 6) return false;
            var numbers = new float[6];
            for (int i = 0; i < fields.Length; i++)
                if (!float.TryParse(fields[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]) ||
                    float.IsNaN(numbers[i]) || float.IsInfinity(numbers[i])) return false;
            pose = new Pose(new Vector3(numbers[0], numbers[1], numbers[2]),
                Quaternion.Euler(numbers[3], numbers[4], numbers[5]));
            return true;
        }

        internal static bool InBazaar()
        {
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            return scene != null && string.Equals(scene.baseSceneName, "bazaar", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Roll(Settings s)
        {
            var unlock = s.ActiveUnlock;
            if (unlock != null) return ModConfig.Get(unlock.Spawns);
            if (!ModConfig.Get(s.Enabled)) return false;
            float chance = ModConfig.Get(s.Chance);
            return chance >= 100f || chance > 0f && UnityEngine.Random.value * 100f < chance;
        }

        // Use Type rather than a generic parameter: the individual tweak classes are static.
        internal static void Register(Type moduleType, string moduleName, string methodName)
        {
            new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar." + moduleName).Patch(
                AccessTools.Method(typeof(Stage), "Start"),
                postfix: new HarmonyMethod(AccessTools.Method(moduleType, methodName)));
        }

        internal static IEnumerator SpawnNextFrame(Stage stage, Action action)
        {
            yield return null;
            if (stage != null && NetworkServer.active && InBazaar()) action();
        }

        internal static GameObject Spawn(Settings settings, string path, string name, Vector3 position, Vector3 facing,
            Action<GameObject> configure = null)
        {
            if (!NetworkServer.active || !SpawnRequirements.AddressAllowed(path)) return null;
            try
            {
                GameObject prefab;
                if (!Prefabs.TryGetValue(path, out prefab))
                {
                    prefab = Addressables.LoadAssetAsync<GameObject>(path).WaitForCompletion();
                    if (prefab == null) throw new InvalidOperationException("Prefab not found: " + path);
                    Prefabs[path] = prefab;
                }
                return Spawn(settings, prefab, name, position, facing, configure);
            }
            catch (Exception e)
            {
                Log.Warning("Bazaar failed to spawn " + name + ": " + e);
                return null;
            }
        }

        internal static GameObject Spawn(Settings settings, GameObject prefab, string name, Vector3 position, Vector3 facing,
            Action<GameObject> configure = null)
        {
            if (!Tweaks.Stages.StageFeatureSpawnHelper.PrefabAllowed(prefab)) return null;
            GameObject instance = null;
            try
            {
                var unlock = settings.ActiveUnlock;
                Quaternion rotation;
                if (settings.TryGetPose(out var pose))
                {
                    position = pose.position;
                    rotation = pose.rotation;
                }
                else
                    settings.Ground.GetPlacement(name, position, facing, out position, out rotation);
                instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                instance.name = "3Qs_Bazaar_" + name;
                configure?.Invoke(instance);
                PurchaseInteraction purchase = null;
                if (settings.UseCost != null)
                {
                    instance.AddComponent<BazaarUseCosts.SpawnedSlot>();
                    purchase = instance.GetComponentInChildren<PurchaseInteraction>(true);
                    BazaarUseCosts.Apply(purchase, settings.UseCost);
                }
                instance.SetActive(true);
                InteractablePlacementEditor.Register(instance, name, settings.EditorSlot);
                NetworkServer.Spawn(instance);
                BazaarUseCosts.SendFee(purchase);
                BazaarAlternateMode.LockSpawn(instance, unlock);
                Log.Info("Bazaar spawned " + name + " at " + position);
                return instance;
            }
            catch (Exception e)
            {
                if (instance) UnityEngine.Object.Destroy(instance);
                Log.Warning("Bazaar failed to spawn " + name + ": " + e);
                return null;
            }
        }
    }
}
