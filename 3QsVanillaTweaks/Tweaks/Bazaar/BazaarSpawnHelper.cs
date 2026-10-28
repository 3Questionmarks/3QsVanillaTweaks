using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    // Shared utilities only. No Init() method: the plugin initializes each spawn tweak independently.
    internal static class BazaarSpawnHelper
    {
        internal sealed class Settings
        {
            internal string Name, Path;
            internal ConfigEntry<bool> Enabled;
            internal GroundSettings Ground;
            internal ConfigEntry<float> Chance, X, Y, Z, FX, FY, FZ;
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

        internal static GroundSettings BindGround(string section)
        {
            return new GroundSettings
            {
                SnapToGround = ModConfig.BindBool(section, "Snap To Ground", true,
                    "Move the interactable to the ground nearest Position Y. Each offset lockbox snaps independently."),
                AlignToGround = ModConfig.BindBool(section, "Align To Ground", true,
                    "Tilt the interactable to match the ground. Disable to keep it upright."),
                HeightOffset = ModConfig.Bind(section, "Height Offset", 0f,
                    "World-space vertical offset added after snapping, or to Position Y if snapping is disabled."),
                SearchDistance = ModConfig.Bind(section, "Ground Search Distance", 12f,
                    new ConfigDescription("Search this far above and below Position Y; use the nearest walkable surface.",
                        new AcceptableValueRange<float>(0.1f, 100f)))
            };
        }

        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        internal static Settings Bind(string section, string name, string path, float chance,
            float x, float y, float z, float fx, float fy, float fz)
        {
            Settings s = new Settings { Name = name, Path = path };
            s.Enabled = ModConfig.BindBool(section, "Enabled", true, "Enable this interactable in the Bazaar.");
            s.Chance = ModConfig.Bind(section, "Spawn Chance (%)", chance, "Spawn probability per Bazaar visit (0-100).");
            s.X = ModConfig.Bind(section, "Position X", x, "Absolute world-space X.");
            s.Y = ModConfig.Bind(section, "Position Y", y, "Absolute world-space Y.");
            s.Z = ModConfig.Bind(section, "Position Z", z, "Absolute world-space Z.");
            s.FX = ModConfig.Bind(section, "Forward X", fx, "World-space facing vector X.");
            s.FY = ModConfig.Bind(section, "Forward Y", fy, "World-space facing vector Y.");
            s.FZ = ModConfig.Bind(section, "Forward Z", fz, "World-space facing vector Z.");
            s.Ground = BindGround(section);
            return s;
        }

        internal static bool InBazaar()
        {
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            return scene != null && string.Equals(scene.baseSceneName, "bazaar", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool Roll(Settings s) => ModConfig.Get(s.Enabled) &&
            UnityEngine.Random.value * 100f < Mathf.Clamp(ModConfig.Get(s.Chance), 0f, 100f);

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
            GameObject instance = null;
            try
            {
                GameObject prefab;
                if (!Prefabs.TryGetValue(path, out prefab))
                {
                    prefab = Addressables.LoadAssetAsync<GameObject>(path).WaitForCompletion();
                    if (prefab == null) throw new InvalidOperationException("Prefab not found: " + path);
                    Prefabs[path] = prefab;
                }
                if (!Tweaks.Stages.StageFeatureSpawnHelper.PrefabAllowed(prefab, path)) return null;
                settings.Ground.GetPlacement(name, position, facing, out position, out Quaternion rotation);
                instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                instance.name = "3Qs_Bazaar_" + name;
                configure?.Invoke(instance);
                NetworkServer.Spawn(instance);
                Log.Info("Bazaar spawned " + name + " at " + position);
                return instance;
            }
            catch (Exception e)
            {
                if (instance != null) UnityEngine.Object.Destroy(instance);
                Log.Warning("Bazaar failed to spawn " + name + ": " + e);
                return null;
            }
        }
    }
}
