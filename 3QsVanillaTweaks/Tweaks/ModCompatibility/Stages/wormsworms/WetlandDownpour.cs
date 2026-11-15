using BepInEx.Configuration;
using RoR2;
using ThreeQsVanillaTweaks;
using Tweaks.Stages;
using Tweaks.Stages.Stage2;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class WetlandDownpour
    {
        internal const string SceneName = "foggyswampdownpour";
        private const string Section = "Mod Compatibility - Stages - Wetland Downpour";
        private const string SkeletonAddress = "RoR2/Base/AltarSkeleton/AltarSkeletonBody.prefab";
        private static ConfigEntry<bool> skeleton;

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S3 (Perched Newt)",
                new Vector3(-820.778300f, 166.361900f, -2051.539000f), new Vector3(0.000000f, 144.758700f, 0.000000f));
            skeleton = ModConfig.BindBool(Section, "Nkuhana Altar Skeleton", true,
                "Ensure the altar skeleton appears and use the Wetland Aspect hex reward settings.");
            WetlandAspect.RegisterScene(SceneName, () => ModConfig.Get(skeleton));
            StageFeatureSpawnHelper.RegisterScene(SceneName, "Nkuhana Altar Skeleton", _ => EnsureSkeleton());
        }

        private static void EnsureSkeleton()
        {
            if (!ModConfig.Get(skeleton)) return;
            Scene scene = SceneManager.GetSceneByName(SceneName);
            foreach (var death in UnityEngine.Object.FindObjectsOfType<CharacterDeathBehavior>(true))
                if (death.gameObject.scene == scene && WetlandAspect.IsSkeletonDeath(death)) return;

            // The map already contains the altar site. Use its authored position as a fallback.
            var position = new Vector3(-918.352f, -3.862644f, -2127.793f);
            var rotation = new Quaternion(0.44411418f, -0.5502387f, -0.5502387f, -0.44411418f);
            var prefab = StageFeatureSpawnHelper.LoadPrefab(SkeletonAddress);
            ModdedStageFeatures.SpawnInScene(scene, prefab, position, rotation,
                instance => instance.transform.localScale = Vector3.one * 0.7f);
            Log.Info("Wetland Downpour: restored the altar skeleton at its authored site.");
        }
    }
}
