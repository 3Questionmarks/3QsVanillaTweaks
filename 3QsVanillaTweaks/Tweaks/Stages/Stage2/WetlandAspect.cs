using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx.Configuration;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage2
{
    internal static class WetlandAspect
    {
        private static ConfigEntry<bool> enabled, hexes;
        private static ConfigEntry<float> chance, offsetX, offsetY, offsetZ;
        private static ConfigEntry<int> hexesPerPlayer;
        private static readonly HashSet<int> Rewarded = new HashSet<int>();
        private static readonly Dictionary<string, ItemTier> Tiers = new Dictionary<string, ItemTier>();
        private static readonly Dictionary<string, Func<bool>> AdditionalScenes = new Dictionary<string, Func<bool>>(StringComparer.OrdinalIgnoreCase);

        private static Scene pendingScene;
        private static int pendingUpdates = -1;

        internal static void Init()
        {
            const string section = "Stages - Stage 2 - Wetland Aspect - Nkuhana Altar";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Keep N'kuhana's altar skeleton available and add hex rewards when it is destroyed.");
            chance = StageFeatureSpawnHelper.Percent(section, "Reward Chance (%)", 100f, "Chance per altar destruction to drop the configured rewards.");
            hexes = ModConfig.BindBool(section, "Drop Random Hexes", true, "Drop random hexes for each participating player when the skeleton is destroyed.");
            hexesPerPlayer = ModConfig.Bind(section, "Hexes Per Player", 3, new ConfigDescription("Random hex drops per participating player.", new AcceptableValueRange<int>(0, 100)));
            offsetX = ModConfig.Bind(section, "Drop Offset X", 0f, "Horizontal X offset for the hex drops.");
            offsetY = ModConfig.Bind(section, "Drop Offset Y", 2f, "Height offset for the hex drops.");
            offsetZ = ModConfig.Bind(section, "Drop Offset Z", 0f, "Horizontal Z offset for the hex drops.");
            var oldOptions = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(ModConfig.SettingsFile(section), null) as IDictionary<ConfigDefinition, string>;
            bool removed = false;
            if (oldOptions != null)
                foreach (string key in new[] { "Drop Random Pages", "Drop Missing Ntorment Books", "Page Count" })
                    removed |= oldOptions.Remove(new ConfigDefinition(section, key));
            if (removed) ModConfig.SettingsFile(section).Save();
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.WetlandAspect");
            harmony.Patch(
                AccessTools.Method(typeof(EntityStates.Destructible.AltarSkeletonDeath), "OnEnter"),
                prefix: new HarmonyMethod(typeof(WetlandAspect), nameof(BeforeAltarDeath)));
            harmony.Patch(AccessTools.Method(typeof(GameObjectUnlockableFilter), "ShouldShowGameObject"),
                postfix: new HarmonyMethod(typeof(WetlandAspect), nameof(AfterShouldShowGameObject)));
            PatchRuntime(harmony, "RoR2.DestroyIfNoUnlockable", "Awake", nameof(BeforeUnlockableDestruction));
            PatchRuntime(harmony, "RoR2.HideObjectsIfUnlockable", "CheckAgainstUnlockables", nameof(BeforeHideObjects), nameof(AfterHideObjects));
            PatchRuntime(harmony, "RoR2.GameObjectExpansionActivator", "ActivateOrDeactivateGameObject", nameof(BeforeExpansionActivation));
            SceneManager.sceneLoaded += (scene, mode) => ScheduleCheck(scene);
            SceneDirector.onPostPopulateSceneServer += director => ScheduleCheck(director.gameObject.scene);
            RoR2Application.onFixedUpdate += CheckPendingScene;
            Run.onRunDestroyGlobal += run => { Rewarded.Clear(); pendingUpdates = -1; };
        }

        private static void PatchRuntime(Harmony harmony, string typeName, string methodName, string prefix, string finalizer = null)
        {
            try
            {
                Type type = typeof(Run).Assembly.GetType(typeName);
                var method = type == null ? null : AccessTools.Method(type, methodName);
                if (method == null)
                {
                    Log.Warning("Wetland altar: visibility hook unavailable: " + typeName + "." + methodName);
                    return;
                }
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(WetlandAspect), prefix),
                    finalizer: finalizer == null ? null : new HarmonyMethod(typeof(WetlandAspect), finalizer));
            }
            catch (Exception e) { Log.Warning("Wetland altar: visibility hook failed for " + typeName + ": " + e.Message); }
        }

        private static List<ItemIndex> Items(string tierName)
        {
            if (!Tiers.TryGetValue(tierName, out ItemTier tier))
            {
                if (!Enum.TryParse(tierName, out tier)) return new List<ItemIndex>();
                Tiers[tierName] = tier;
            }
            var result = new List<ItemIndex>();
            foreach (ItemIndex index in ItemCatalog.allItems)
            {
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (item && item.tier == tier && SpawnRequirements.ItemAllowed(item) && Run.instance.IsItemAvailable(index)) result.Add(index);
            }
            return result;
        }

        private static void Drop(List<ItemIndex> pool, int count, Vector3 position, Xoroshiro128Plus rng)
        {
            if (count <= 0) return;
            if (pool.Count == 0) { Log.Warning("Wetland altar: no available items for this reward tier."); return; }
            for (int i = 0; i < count; ++i)
            {
                ItemIndex item = pool[rng.RangeInt(0, pool.Count)];
                Vector3 velocity = Quaternion.AngleAxis(i * 137.5f, Vector3.up) * new Vector3(3f, 8f, 0f);
                PickupDropletController.CreatePickupDroplet(new GenericPickupController.CreatePickupInfo
                {
                    pickup = new UniquePickup(PickupCatalog.FindPickupIndex(item)),
                    position = position,
                    rotation = Quaternion.identity
                }, position, velocity);
            }
        }

        private static bool HexRewardsEnabled()
        {
            return Run.instance && ModConfig.Get(enabled) && ModConfig.Get(hexes) && SpawnRequirements.ExpansionEnabled("DLC4");
        }

        internal static void RegisterScene(string scene, Func<bool> enabled) => AdditionalScenes[scene] = enabled;

        private static bool IsSupportedScene(string scene) =>
            scene == "foggyswamp" || (AdditionalScenes.TryGetValue(scene, out var enabled) && enabled());

        internal static bool IsSkeletonDeath(CharacterDeathBehavior death)
        {
            return death && death.deathState.stateType == typeof(EntityStates.Destructible.AltarSkeletonDeath);
        }

        private static bool IsSkeletonObject(GameObject target)
        {
            if (!target || !IsSupportedScene(target.scene.name)) return false;
            foreach (CharacterDeathBehavior death in target.GetComponentsInChildren<CharacterDeathBehavior>(true))
                if (IsSkeletonDeath(death)) return true;
            return IsSkeletonDeath(target.GetComponentInParent<CharacterDeathBehavior>());
        }

        private static void AfterShouldShowGameObject(GameObjectUnlockableFilter __instance, ref bool __result)
        {
            if (!__result && HexRewardsEnabled() && IsSkeletonObject(__instance.gameObject)) __result = true;
        }

        private static bool BeforeUnlockableDestruction(Component __instance)
        {
            return !HexRewardsEnabled() || !IsSkeletonObject(__instance.gameObject);
        }

        private static bool BeforeExpansionActivation(GameObject obj)
        {
            if (!HexRewardsEnabled() || !IsSkeletonObject(obj)) return true;
            obj.SetActive(true);
            return false;
        }

        private static void BeforeHideObjects(Component __instance, out Array __state)
        {
            __state = null;
            if (!HexRewardsEnabled()) return;
            var field = AccessTools.Field(__instance.GetType(), "pairs");
            Array pairs = field?.GetValue(__instance) as Array;
            if (pairs == null) return;
            var retained = new List<object>();
            foreach (object pair in pairs)
            {
                GameObject target = pair == null ? null : AccessTools.Field(pair.GetType(), "gameObject")?.GetValue(pair) as GameObject;
                if (!IsSkeletonObject(target)) retained.Add(pair);
            }
            if (retained.Count == pairs.Length) return;
            Array filtered = Array.CreateInstance(pairs.GetType().GetElementType(), retained.Count);
            for (int i = 0; i < retained.Count; ++i) filtered.SetValue(retained[i], i);
            __state = pairs;
            field.SetValue(__instance, filtered);
        }

        private static void AfterHideObjects(Component __instance, Array __state)
        {
            if (__state != null) AccessTools.Field(__instance.GetType(), "pairs")?.SetValue(__instance, __state);
        }

        private static void ScheduleCheck(Scene scene)
        {
            pendingScene = scene;
            pendingUpdates = IsSupportedScene(scene.name) ? 5 : -1;
        }

        private static void CheckPendingScene()
        {
            if (pendingUpdates < 0 || --pendingUpdates > 0) return;
            pendingUpdates = -1;
            RestoreSkeletons(pendingScene);
        }

        private static void RestoreSkeletons(Scene scene)
        {
            if (!HexRewardsEnabled()) return;
            int found = 0, restored = 0;
            foreach (CharacterDeathBehavior death in UnityEngine.Object.FindObjectsOfType<CharacterDeathBehavior>(true))
            {
                if (death.gameObject.scene != scene || !IsSkeletonDeath(death) || Rewarded.Contains(death.gameObject.GetInstanceID())) continue;
                HealthComponent health = death.GetComponent<HealthComponent>();
                if (health && !health.alive) continue;
                ++found;
                bool inactive = !death.gameObject.activeInHierarchy;
                for (Transform current = death.transform; current; current = current.parent)
                    if (!current.gameObject.activeSelf) current.gameObject.SetActive(true);
                if (inactive && death.gameObject.activeInHierarchy) ++restored;
            }
            Log.Info("Wetland altar: found " + found + " skeleton(s), restored " + restored + ".");
            if (found == 0) Log.Warning("Wetland altar: no living altar skeleton found after scene initialisation.");
        }

        private static void BeforeAltarDeath(EntityStates.Destructible.AltarSkeletonDeath __instance)
        {
            GameObject altar = AccessTools.Property(typeof(EntityStates.EntityState), "gameObject")?.GetValue(__instance, null) as GameObject;
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !altar ||
                !IsSupportedScene(altar.scene.name) || !SpawnRequirements.ExpansionEnabled("DLC4") ||
                !Rewarded.Add(altar.GetInstanceID())) return;
            try
            {
                var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
                if (!StageFeatureSpawnHelper.Roll(ModConfig.Get(chance), rng)) return;
                Vector3 position = altar.transform.position + new Vector3(ModConfig.Get(offsetX), ModConfig.Get(offsetY), ModConfig.Get(offsetZ));
                if (ModConfig.Get(hexes)) Drop(Items("Hex"), ModConfig.Get(hexesPerPlayer) * Run.instance.participatingPlayerCount, position, rng);
            }
            catch (Exception e) { Log.Warning("Wetland altar reward failed: " + e); }
        }
    }
}
