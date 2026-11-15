using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class CloakedChest
    {
        private const string Section = "Interactables - Cloaked Chest";
        private static ConfigEntry<bool> enabled, glimmer;
        private static ConfigEntry<float> glimmerInterval;
        private static ConfigEntry<float>[] weights;
        private static CloakedDropTable table;

        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true,
                "Configure cloaked chest rewards and make the chest glimmer. Restart to apply.");
            glimmer = C.BindBool(Section, "Glimmer", true, "Briefly reveal the location of cloaked chests.");
            glimmerInterval = C.Bind(Section, "Glimmer Interval (s)", 4f,
                new ConfigDescription("Seconds between glimmers. Default: 4.", new AcceptableValueRange<float>(0.5f, 60f)));
            string[] names = {
                "Common", "Uncommon", "Legendary", "Boss", "Lunar Items", "Lunar Equipment",
                "Equipment", "Void Common", "Void Uncommon", "Void Legendary", "Void Boss",
                "Meal", "Power Shape", "Cursed", "Hex", "Page"
            };
            weights = new ConfigEntry<float>[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int value = i == 1 ? 80 : i == 2 ? 20 : 0;
                weights[i] = C.Bind(Section, names[i] + " Weight", (float)value,
                    new ConfigDescription("Relative chance to roll this item pool. Available pools are normalized together. Default: " + value + ".",
                        new AcceptableValueRange<float>(0f, 1000f)));
            }
            // Existing config files retain their old default values. Migrate only the
            // exact original 80 common / 20 legendary preset, once per config file.
            var defaultsVersion = C.Bind(Section, "Loot Defaults Version", 0,
                "Internal version for migrating the original cloaked chest reward defaults.");
            if (defaultsVersion.Value == 0)
            {
                bool originalDefaults = Mathf.Approximately(weights[0].Value, 80f) &&
                    Mathf.Approximately(weights[1].Value, 0f) &&
                    Mathf.Approximately(weights[2].Value, 20f);
                for (int i = 3; i < weights.Length && originalDefaults; i++)
                    originalDefaults = Mathf.Approximately(weights[i].Value, 0f);
                if (originalDefaults)
                {
                    weights[0].Value = 0f;
                    weights[1].Value = 80f;
                }
                defaultsVersion.Value = 1;
            }
            if (!enabled.Value) return;
            new Harmony("3Questionmarks.3QsVanillaTweaks.CloakedChest").Patch(
                AccessTools.Method(typeof(ChestBehavior), "Start"),
                prefix: new HarmonyMethod(typeof(CloakedChest), nameof(BeforeChestStart)));
        }

        private static bool IsCloaked(ChestBehavior chest)
        {
            if (!chest) return false;
            return IsCloakedName(chest.name) || IsCloakedName(chest.transform.root.name);
        }

        private static bool IsCloakedName(string name) =>
            name.IndexOf("Stealthed", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("CloakedChest", StringComparison.OrdinalIgnoreCase) >= 0;

        private static void BeforeChestStart(ChestBehavior __instance)
        {
            if (!IsCloaked(__instance) || !C.Get(enabled)) return;
            if (!table) table = ScriptableObject.CreateInstance<CloakedDropTable>();
            __instance.dropTable = table;
            if (!Application.isBatchMode && !__instance.GetComponent<Glimmer>())
                __instance.gameObject.AddComponent<Glimmer>();
        }

        private sealed class CloakedDropTable : PickupDropTable
        {
            private static List<PickupIndex>[] AvailablePools(Run run) => new[] {
                run.availableTier1DropList, run.availableTier2DropList, run.availableTier3DropList,
                run.availableBossDropList, run.availableLunarItemDropList, run.availableLunarEquipmentDropList,
                run.availableEquipmentDropList, run.availableVoidTier1DropList, run.availableVoidTier2DropList,
                run.availableVoidTier3DropList, run.availableVoidBossDropList, run.availableFoodTierDropList,
                run.availablePowerShapeItemsDropList, run.availableCursedTierDropList,
                run.availableHexTierDropList, run.availablePageTierDropList
            };

            public override int GetPickupCount()
            {
                if (!Run.instance) return 0;
                int count = 0;
                var available = AvailablePools(Run.instance);
                for (int i = 0; i < available.Length; i++)
                    if (C.Get(weights[i]) > 0f && available[i] != null) count += available[i].Count;
                return count;
            }

            public override UniquePickup GeneratePickupPreReplacement(Xoroshiro128Plus rng)
            {
                Run run = Run.instance;
                if (!run) return UniquePickup.none;
                var pools = new WeightedSelection<List<PickupIndex>>();
                var available = AvailablePools(run);
                for (int i = 0; i < available.Length; i++)
                {
                    float weight = C.Get(weights[i]);
                    if (weight > 0f && available[i] != null && available[i].Count > 0)
                        pools.AddChoice(available[i], weight);
                }
                if (pools.Count == 0) return UniquePickup.none;
                List<PickupIndex> pool = pools.Evaluate(rng.nextNormalizedFloat);
                return new UniquePickup(pool[rng.RangeInt(0, pool.Count)]);
            }
        }

        private sealed class Glimmer : MonoBehaviour
        {
            private ChestBehavior chest;
            private GameObject effect;
            private Light glow;
            private Highlight outline;
            private float nextPulse, pulseStart;

            private void Awake()
            {
                chest = GetComponent<ChestBehavior>();
                effect = new GameObject("Cloaked Chest Glimmer");
                effect.transform.SetParent(transform, false);
                effect.transform.localPosition = Vector3.up;
                glow = effect.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(1f, 1f, 0.96f);
                glow.range = 7f;
                glow.intensity = 0f;
                AddOutline();
                pulseStart = float.NegativeInfinity;
                nextPulse = Time.time + 0.5f;
            }

            private void AddOutline()
            {
                Highlight source = null;
                foreach (Highlight candidate in GetComponentsInChildren<Highlight>(true))
                    if (candidate && (source == null || candidate.useForPingHighlight))
                    {
                        source = candidate;
                        if (candidate.useForPingHighlight) break;
                    }

                var targets = new List<Renderer>();
                if (source)
                {
                    if (source.targetRenderer) targets.Add(source.targetRenderer);
                    if (source.targetRendererList != null)
                        foreach (Renderer renderer in source.targetRendererList)
                            if (renderer && !targets.Contains(renderer)) targets.Add(renderer);
                }
                if (targets.Count == 0)
                    foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                        if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                            targets.Add(renderer);
                if (targets.Count == 0) return;

                outline = gameObject.AddComponent<Highlight>();
                outline.SetTargetRendererList(targets);
                outline.highlightColor = Highlight.HighlightColor.custom;
                outline.CustomColor = new Color(1f, 1f, 0.96f, 0.65f);
                outline.useForPingHighlight = false;
                outline.isOn = false;
            }

            private void Update()
            {
                if (!chest || chest.isChestOpened)
                {
                    if (outline) Destroy(outline);
                    Destroy(effect);
                    Destroy(this);
                    return;
                }
                if (!C.Get(glimmer))
                {
                    glow.intensity = 0f;
                    if (outline) outline.isOn = false;
                    return;
                }
                float now = Time.time;
                if (now >= nextPulse)
                {
                    pulseStart = now;
                    nextPulse = now + C.Get(glimmerInterval);
                }
                float progress = (now - pulseStart) / 1.2f;
                bool active = progress >= 0f && progress <= 1f;
                float strength = active ? Mathf.Sin(progress * Mathf.PI) : 0f;
                glow.intensity = strength * 2.5f;
                if (outline)
                {
                    outline.isOn = active;
                    outline.strength = strength * 0.75f;
                }
            }

            private void OnDestroy()
            {
                if (outline) Destroy(outline);
                if (effect) Destroy(effect);
            }
        }
    }
}
