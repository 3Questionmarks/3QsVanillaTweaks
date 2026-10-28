using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage3
{
    internal static class DampCaverns
    {
        private static ConfigEntry<int> count;
        private static ConfigEntry<float> capChance, wungusChance, fixChance;
        private static readonly Dictionary<GameObject, bool> Backpacks = new Dictionary<GameObject, bool>();

        internal static void Init()
        {
            const string section = "Stages - Stage 3 - Damp Caverns - Fungus Backpack";
            var settings = StageFeatureSpawnHelper.Bind(section, "fungal", "Fungus Backpack", -41.87f, 224.81f, -48.78f);
            count = ModConfig.Bind(section, "Item Count", 10, new ConfigDescription("Number of items in the backpack.", new AcceptableValueRange<int>(0, 1000)));
            capChance = StageFeatureSpawnHelper.Percent(section, "Mycelium Cap Chance (%)", 100f / 30f, "Chance per item to replace Bustling Fungus with Mycelium Cap.");
            wungusChance = StageFeatureSpawnHelper.Percent(section, "Weeping Fungus Chance (%)", 5f, "Chance per item to replace Bustling Fungus with Weeping Fungus.");
            fixChance = StageFeatureSpawnHelper.Percent(section, "Quick Fix Chance (%)", 2f, "Chance per item to replace Bustling Fungus with Quick Fix. Replacement chances above 100% combined are scaled down together.");
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
                StageFeatureSpawnHelper.Spawn(spawn, StageInteractableAssets.Prefab("ScavBackpack"), instance => Backpacks[instance] = false));
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.DampCaverns");
            harmony.Patch(AccessTools.Method(typeof(ChestBehavior), "ItemDrop"), prefix: new HarmonyMethod(typeof(DampCaverns), nameof(BeforeDrop)));
            Type otherType = typeof(Run).Assembly.GetType("RoR2.ScavBackpackBehavior");
            var otherMethod = otherType == null ? null : AccessTools.Method(otherType, "ItemDrop");
            if (otherMethod != null) harmony.Patch(otherMethod, prefix: new HarmonyMethod(typeof(DampCaverns), nameof(BeforeDrop)));
            Run.onRunDestroyGlobal += run => { Backpacks.Clear(); };
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += scene =>
            {
                var removed = new List<GameObject>();
                foreach (var pair in Backpacks) if (!pair.Key || pair.Key.scene == scene) removed.Add(pair.Key);
                foreach (var key in removed) Backpacks.Remove(key);
            };
        }

        private static ItemDef Replacement(string itemName)
        {
            ItemDef item = Stage3SpawnHelper.Item(itemName);
            return item && SpawnRequirements.ItemAllowed(item) ? item : null;
        }

        private static ItemDef Choose(float roll)
        {
            ItemDef cap = Replacement("MushShot"), wungus = Replacement("MushroomVoid"), fix = Replacement("BonusHealthBoost");
            float a = cap ? ModConfig.Get(capChance) : 0f;
            float b = wungus ? ModConfig.Get(wungusChance) : 0f;
            float c = fix ? ModConfig.Get(fixChance) : 0f;
            roll *= Mathf.Max(100f, a + b + c);
            if (roll < a) return cap;
            if (roll < a + b) return wungus;
            if (roll < a + b + c) return fix;
            return Replacement("Mushroom");
        }

        private static bool BeforeDrop(Component __instance)
        {
            if (!NetworkServer.active || !Run.instance ||
                !Backpacks.TryGetValue(__instance.gameObject, out bool claimed)) return true;
            if (claimed) return false;
            Backpacks[__instance.gameObject] = true;
            ((MonoBehaviour)__instance).StartCoroutine(DropItems(__instance));
            return false;
        }

        private static IEnumerator DropItems(Component source)
        {
            Run run = Run.instance;
            var rng = new Xoroshiro128Plus(run.treasureRng.nextUlong);
            int total = ModConfig.Get(count);
            Type opening = typeof(Run).Assembly.GetType("EntityStates.ScavBackpack.Opening");
            object durationValue = opening == null ? null : AccessTools.Field(opening, "duration")?.GetValue(null);
            float duration = durationValue is float seconds ? seconds : 1f;
            var interval = new WaitForSeconds(Mathf.Max(0.01f, duration / Mathf.Max(1, total)));
            ChestBehavior chest = source as ChestBehavior;
            Transform launch = chest && chest.dropTransform ? chest.dropTransform : source.transform;
            Vector3 forward = launch.forward;
            float upSpeed = chest ? chest.dropUpVelocityStrength : 20f;
            float forwardSpeed = chest ? chest.dropForwardVelocityStrength : 2f;
            if (chest) chest.NetworkisChestOpened = true;
            for (int i = 0; i < total; ++i)
            {
                if (!source || !NetworkServer.active || Run.instance != run) yield break;
                ItemDef item = Choose(rng.nextNormalizedFloat);
                if (item)
                {
                    // Each native single-item drop starts in the same direction. Rotate the
                    // launch velocity ourselves, without rotating the bag or its animated bones.
                    Vector3 direction = Quaternion.AngleAxis(360f * i / total, Vector3.up) * forward;
                    Vector3 position = launch.position + Vector3.up * 1.5f;
                    var info = new GenericPickupController.CreatePickupInfo
                    {
                        pickup = new UniquePickup(PickupCatalog.FindPickupIndex(item.itemIndex)),
                        position = position,
                        chest = chest
                    };
                    PickupDropletController.CreatePickupDroplet(info, position,
                        Vector3.up * upSpeed + direction * forwardSpeed);
                }
                if (i + 1 < total) yield return interval;
            }
        }
    }
}
