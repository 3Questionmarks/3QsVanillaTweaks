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
            internal ConfigEntry<float> Chance, X, Y, Z, FX, FY, FZ;
            internal Vector3 Position => new Vector3(ModConfig.Get(X), ModConfig.Get(Y), ModConfig.Get(Z));
            internal Vector3 Forward => new Vector3(ModConfig.Get(FX), ModConfig.Get(FY), ModConfig.Get(FZ));
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

        internal static GameObject Spawn(string path, string name, Vector3 position, Vector3 facing,
            Action<GameObject> configure = null)
        {
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
                facing.y = 0f;
                if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
                instance = UnityEngine.Object.Instantiate(prefab, position,
                    Quaternion.LookRotation(facing.normalized, Vector3.up));
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
