using System;
using System.Collections;
using System.Reflection;
using RoR2;
using ThreeQsVanillaTweaks;
using Tweaks.Stages;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.ModCompatibility.Stages
{
    internal static class ModdedStageFeatures
    {
        private const string NewtAddress = "RoR2/Base/NewtStatue/NewtStatue.prefab";
        private static Type addressableSpawnerType;
        private static FieldInfo addressField;
        private static bool lookedUpSpawner;

        internal static void RegisterNewt(string section, string sceneName, string marker, Vector3 position, Vector3 rotation)
        {
            var enabled = ModConfig.BindBool(section, "Guarantee Newt Altar", true,
                "Use the mapped " + marker + " location when the Newt Altar Spawn Mode calls for a guarantee.");
            StageFeatureSpawnHelper.RegisterScene(sceneName, marker + " Newt Altar", _ =>
            {
                if (!ModConfig.Get(enabled)) return;
                Scene scene = SceneManager.GetSceneByName(sceneName);
                Run.instance.StartCoroutine(EnsureNewt(scene, marker, position, Quaternion.Euler(rotation)));
            });
        }

        private static IEnumerator EnsureNewt(Scene scene, string marker, Vector3 position, Quaternion rotation)
        {
            // Allow the map's own toggles and addressable spawners to finish.
            yield return new WaitForSecondsRealtime(0.5f);
            if (!NetworkServer.active || !Run.instance || !scene.IsValid() || !scene.isLoaded) yield break;
            bool anyAltarPresent = false;
            foreach (var altar in UnityEngine.Object.FindObjectsOfType<PortalStatueBehavior>())
            {
                if (!altar || altar.gameObject.scene != scene || altar.portalType != PortalStatueBehavior.PortalType.Shop)
                    continue;
                for (Transform current = altar.transform; current; current = current.parent)
                    if ((current.position - position).sqrMagnitude < 1f) yield break;
                anyAltarPresent = true;
            }
            if (!NewtAltar.ShouldEnsureModdedAltar(anyAltarPresent)) yield break;

            try
            {
                SpawnInScene(scene, StageFeatureSpawnHelper.LoadPrefab(NewtAddress), position, rotation);
                Log.Info("Modded Newt Altar: guaranteed " + marker + " on " + scene.name + " at " + position + ".");
            }
            catch (Exception error) { Log.Warning("Modded Newt Altar failed on " + scene.name + ": " + error); }
        }

        internal static GameObject SpawnInScene(Scene scene, GameObject prefab, Vector3 position, Quaternion rotation,
            Action<GameObject> configure = null)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
            try
            {
                SceneManager.MoveGameObjectToScene(instance, scene);
                configure?.Invoke(instance);
                NetworkServer.Spawn(instance);
                return instance;
            }
            catch
            {
                UnityEngine.Object.Destroy(instance);
                throw;
            }
        }

        internal static bool HasAddressableSpawner(GameObject obj, string address)
        {
            if (!lookedUpSpawner)
            {
                lookedUpSpawner = true;
                addressableSpawnerType = ReflectionTools.TypeByName("LOP.InstantiateAddressablePrefab");
                addressField = ReflectionTools.Field(addressableSpawnerType, "address");
            }
            if (addressField == null) return false;
            foreach (Component spawner in obj.GetComponentsInChildren(addressableSpawnerType, true))
                if (spawner && string.Equals(addressField.GetValue(spawner) as string, address, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        internal static void RegisterScrapper(string section, string scene, string title, Vector3 position)
        {
            var settings = StageFeatureSpawnHelper.Bind(section, scene, title,
                position.x, position.y, position.z);
            StageFeatureSpawnHelper.Register(settings, (placement, _) =>
                StageFeatureSpawnHelper.Spawn(placement, StageInteractableAssets.Prefab("Scrapper")));
        }
    }
}
