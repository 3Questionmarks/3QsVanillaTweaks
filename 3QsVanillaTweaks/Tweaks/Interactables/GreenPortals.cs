using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class GreenPortals
    {
        private const string Section = "Interactables - Green Portals";
        private static ConfigEntry<bool> enabled, shrinePortal, lateMeridian, fullPath;
        private static Run routeRun;
        private static int progress;
        private static string pendingScene;
        private static readonly ConditionalWeakTable<HalcyoniteShrineInteractable, ShrineStatus> shrines = new ConditionalWeakTable<HalcyoniteShrineInteractable, ShrineStatus>();
        private static readonly ConditionalWeakTable<PortalSpawner, ShrineStatus> shrineSpawners = new ConditionalWeakTable<PortalSpawner, ShrineStatus>();
        private sealed class ShrineStatus { internal bool Spawned; }
        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true, "Enable green portal spawning and destination options. Restart to apply.");
            shrinePortal = C.BindBool(Section, "Always Spawn From Halcyon Shrines", true,
                "Completing a Halcyon Shrine encounter always opens its green portal, including on stages 4 and 5 or later loops. Requires Seekers of the Storm. Does not open a portal on the initial purchase.");
            lateMeridian = C.BindBool(Section, "Late Portals Lead To Prime Meridian", true,
                "Green portals used after stage 3 (on stage 4 or later) lead to Prime Meridian. The full-path option takes priority. Vanilla: alternate destinations depend on the next stage tier.");
            fullPath = C.BindBool(Section, "Require Full Path", false,
                "Every new green portal journey starts at Reformed Altar, then continues to Treeborn Colony (its loop variant during a loop), then Prime Meridian. Overrides stage-tier routing, including the Providence/Gilded Coast route. Leaving the path resets this sequence.");
            if (!enabled.Value) return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.GreenPortals");
            harmony.Patch(AccessTools.Method(typeof(HalcyoniteShrineInteractable), "AttemptSpawnPortal"),
                prefix: new HarmonyMethod(typeof(GreenPortals), nameof(BeforeShrinePortal)));
            harmony.Patch(AccessTools.Method(typeof(PortalSpawner), "AttemptSpawnPortalServer"),
                postfix: new HarmonyMethod(typeof(GreenPortals), nameof(AfterPortalAttempt)));
            harmony.Patch(AccessTools.Method(typeof(SceneExitController), "TryDetermineDestinationScene"),
                prefix: new HarmonyMethod(typeof(GreenPortals), nameof(BeforeDestination)));
            harmony.Patch(AccessTools.Method(typeof(SceneExitController), "Begin"),
                prefix: new HarmonyMethod(typeof(GreenPortals), nameof(BeforeBegin)),
                postfix: new HarmonyMethod(typeof(GreenPortals), nameof(AfterBegin)));
            SceneDirector.onPostPopulateSceneServer += OnSceneReady;
        }
        private static bool Active => enabled != null && C.Get(enabled);
        private static bool IsGreen(PortalSpawner spawner)
        {
            if (!spawner || !spawner.PortalSpawnCard || !spawner.PortalSpawnCard.prefab) return false;
            var exit = spawner.PortalSpawnCard.prefab.GetComponent<SceneExitController>();
            return exit && exit.isColossusPortal;
        }
        private static bool CanForceShrine => Active && C.Get(shrinePortal) && NetworkServer.active && Run.instance &&
            Tweaks.Stages.StageFeatureSpawnHelper.ExpansionEnabled("RoR2/DLC2/Common/DLC2.asset");
        private static void BeforeShrinePortal(HalcyoniteShrineInteractable __instance)
        {
            if (!CanForceShrine || shrines.GetOrCreateValue(__instance).Spawned || __instance.portalSpawners == null) return;
            var status = shrines.GetOrCreateValue(__instance);
            PortalSpawner chosen = null;
            // Some shrine prefabs have multiple portal spawners for different tiers.
            // Prefer the naturally eligible green one, otherwise use the first; open only one.
            foreach (var spawner in __instance.portalSpawners)
                if (IsGreen(spawner) && (!chosen || spawner.WillSpawn))
                {
                    chosen = spawner;
                    if (spawner.WillSpawn) break;
                }
            if (!chosen) return;
            shrineSpawners.GetValue(chosen, key => status);
            foreach (var spawner in __instance.portalSpawners)
                if (IsGreen(spawner)) spawner.WillSpawn = spawner == chosen;
        }
        private static void AfterPortalAttempt(PortalSpawner __instance, bool __result)
        {
            if (__result && shrineSpawners.TryGetValue(__instance, out var status)) status.Spawned = true;
        }
        internal static string SelectDestination(bool forceFullPath, bool forceLate, int stageTier, int pathProgress, string current, bool loop)
        {
            if (forceFullPath)
            {
                if (pathProgress == 1 && current == "lemuriantemple") return loop ? "habitatfall" : "habitat";
                if (pathProgress == 2 && (current == "habitat" || current == "habitatfall")) return "meridian";
                return "lemuriantemple";
            }
            return forceLate && stageTier > 3 ? "meridian" : null;
        }
        private static bool BeforeDestination(SceneExitController __instance, ref SceneDef result, ref bool __result)
        {
            if (!Active || !Run.instance || !Stage.instance || !Stage.instance.sceneDef || !__instance.isColossusPortal) return true;
            var scene = Stage.instance.sceneDef;
            int currentProgress = routeRun == Run.instance ? progress : 0;
            int tier = scene.stageOrder;
            // Hidden realms have no normal tier: use the main-stage destination they would return to.
            if (tier <= 0 && Run.instance.nextStageScene)
                tier = Run.instance.nextStageScene.stageOrder == 1 ? 5 : Run.instance.nextStageScene.stageOrder - 1;
            string destination = SelectDestination(C.Get(fullPath), C.Get(lateMeridian), tier,
                currentProgress, scene.baseSceneName, Run.instance.loopClearCount > 0 && Run.instance.stageClearCount > 5);
            if (destination == null) return true;
            var target = SceneCatalog.FindSceneDef(destination);
            if (!target)
            {
                Log.Warning("Green portal destination unavailable: " + destination + "; retaining the portal's original route.");
                return true;
            }
            // Resolve directly so both preload and actual travel agree; don't mutate shared prefabs or Run.nextStageScene.
            result = target; __result = true; return false;
        }
        private static void BeforeBegin(SceneExitController __instance, out bool __state)
        {
            __state = NetworkServer.active && Active && __instance &&
                (SceneExitController.ExitState)AccessTools.Field(typeof(SceneExitController), "exitState").GetValue(__instance) == SceneExitController.ExitState.Idle;
        }
        private static void AfterBegin(SceneExitController __instance, bool __state)
        {
            var exit = __instance;
            if (!__state || !Run.instance || !exit) return;
            if (!C.Get(fullPath) || !exit.isColossusPortal || !exit.destinationScene)
            {
                routeRun = null; progress = 0; pendingScene = null; return;
            }
            routeRun = Run.instance;
            pendingScene = exit.destinationScene.baseSceneName;
        }
        private static void OnSceneReady(SceneDirector director)
        {
            if (!NetworkServer.active || !director) return;
            string arrived = director.gameObject.scene.name;
            if (!Active || !C.Get(fullPath) || !Run.instance || routeRun != Run.instance || pendingScene != arrived)
            {
                routeRun = null; progress = 0; pendingScene = null; return;
            }
            progress = arrived == "lemuriantemple" ? 1 : arrived == "habitat" || arrived == "habitatfall" ? 2 : 0;
            pendingScene = null;
        }
    }
}
