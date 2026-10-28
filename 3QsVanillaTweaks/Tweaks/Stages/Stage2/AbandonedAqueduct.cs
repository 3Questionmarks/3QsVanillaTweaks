using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.EntityLogic;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage2
{
    internal static class AbandonedAqueduct
    {
        private static ConfigEntry<bool> enabled, stayPressed;
        private static ConfigEntry<int> buttonCount, requiredCount;
        private static float nextClientCheck;
        private static readonly HashSet<int> MissingCounterReported = new HashSet<int>();
        private static readonly HashSet<int> Completed = new HashSet<int>();
        private static readonly System.Reflection.FieldInfo SwitchDown = AccessTools.Field(typeof(PressurePlateController), "switchDown");

        internal static void Init()
        {
            const string section = "Stages - Stage 2 - Abandoned Aqueduct - Buttons";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable the configured Ancient Gate button puzzle.");
            var oldOptions = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(ModConfig.SettingsFile(section), null) as IDictionary<ConfigDefinition, string>;
            if (oldOptions?.Remove(new ConfigDefinition(section, "Apply Chance (%)")) == true) ModConfig.SettingsFile(section).Save();
            buttonCount = ModConfig.Bind(section, "Button Count", 4,
                new ConfigDescription("Number of buttons to spawn. Zero opens the gate without pressing any buttons.", new AcceptableValueRange<int>(0, 8)));
            requiredCount = ModConfig.Bind(section, "Buttons Required", 3,
                new ConfigDescription("Buttons needed to open the gate, up to the number spawned.", new AcceptableValueRange<int>(0, 8)));
            stayPressed = ModConfig.BindBool(section, "Buttons Stay Pressed", true,
                "Pressed buttons stay down. Disable to require them to be held.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.Aqueduct");
            harmony.Patch(AccessTools.Method(typeof(SceneObjectToggleGroup), "Awake"),
                prefix: new HarmonyMethod(typeof(AbandonedAqueduct), nameof(BeforeToggleAwake)));
            harmony.Patch(AccessTools.Method(typeof(PressurePlateController), "SetSwitch"),
                prefix: new HarmonyMethod(typeof(AbandonedAqueduct), nameof(BeforeSetSwitch)));
            SceneDirector.onPostPopulateSceneServer += AfterPopulate;
            RoR2Application.onFixedUpdate += UpdateClientCounters;
            SceneManager.sceneUnloaded += scene =>
            {
                Completed.Remove(scene.handle); MissingCounterReported.Remove(scene.handle);
            };
        }

        private static bool Apply(Scene scene)
        {
            return Run.instance && scene.name == "goolake" && ModConfig.Get(enabled);
        }

        private static bool IsPlateObject(GameObject obj)
        {
            return obj && obj.GetComponentInChildren<PressurePlateController>(true);
        }

        private static void BeforeToggleAwake(SceneObjectToggleGroup __instance)
        {
            if (!Apply(__instance.gameObject.scene) || __instance.toggleGroups == null) return;
            for (int i = 0; i < __instance.toggleGroups.Length; ++i)
            {
                var group = __instance.toggleGroups[i];
                if (group.objects == null || group.objects.Length == 0) continue;
                // Alter only the button-location group, never chests or other scenery.
                bool buttonsOnly = true;
                foreach (GameObject obj in group.objects) if (!IsPlateObject(obj)) { buttonsOnly = false; break; }
                if (!buttonsOnly) continue;
                int count = Mathf.Clamp(ModConfig.Get(buttonCount), 0, group.objects.Length);
                group.minEnabled = group.maxEnabled = count;
                __instance.toggleGroups[i] = group;
            }
        }

        private static void AddCounters(PressurePlateController plate, HashSet<Counter> counters)
        {
            if (plate.OnSwitchDown == null) return;
            for (int i = 0; i < plate.OnSwitchDown.GetPersistentEventCount(); ++i)
            {
                var target = plate.OnSwitchDown.GetPersistentTarget(i) as Counter;
                if (target && plate.OnSwitchDown.GetPersistentMethodName(i) == "Add") counters.Add(target);
            }
        }

        private static void AfterPopulate(SceneDirector director)
        {
            ConfigureScene(director.gameObject.scene, false);
        }

        private static void UpdateClientCounters()
        {
            if (NetworkServer.active || !NetworkClient.active || !Run.instance || Time.unscaledTime < nextClientCheck) return;
            nextClientCheck = Time.unscaledTime + 0.25f;
            foreach (PressurePlateController plate in UnityEngine.Object.FindObjectsOfType<PressurePlateController>(true))
                if (plate.gameObject.scene.name == "goolake") { ConfigureScene(plate.gameObject.scene, true); break; }
        }

        private static void ConfigureScene(Scene scene, bool waitForActivation)
        {
            if (!Apply(scene) || Completed.Contains(scene.handle)) return;
            var counters = new HashSet<Counter>();
            var all = new List<PressurePlateController>();
            foreach (PressurePlateController plate in UnityEngine.Object.FindObjectsOfType<PressurePlateController>(true))
                if (plate.gameObject.scene == scene)
                {
                    AddCounters(plate, counters);
                    all.Add(plate);
                }
            if (counters.Count == 0)
            {
                if (MissingCounterReported.Add(scene.handle)) Log.Warning("Aqueduct buttons: could not find the pressure plates' gate counter; leaving the gate logic alone.");
                return;
            }
            // The scene toggle group has already synchronised the selected locations.
            int activeCount = 0;
            foreach (PressurePlateController plate in all)
                if (plate.gameObject.activeInHierarchy) ++activeCount;
            if (waitForActivation && activeCount != Mathf.Clamp(ModConfig.Get(buttonCount), 0, all.Count)) return;
            Completed.Add(scene.handle);
            int required = Mathf.Clamp(ModConfig.Get(requiredCount), 0, activeCount);
            foreach (Counter counter in counters)
            {
                counter.threshold = required;
                if (required == 0) counter.Add(0);
            }
            Log.Info("Aqueduct buttons: " + activeCount + " spawned, " + required + " required, stay pressed=" + ModConfig.Get(stayPressed));
        }

        private static bool BeforeSetSwitch(PressurePlateController __instance, bool __0)
        {
            if (__0 || !ModConfig.Get(enabled) || !ModConfig.Get(stayPressed) ||
                !(SwitchDown?.GetValue(__instance) is bool down && down)) return true;
            return !Apply(__instance.gameObject.scene);
        }
    }
}
