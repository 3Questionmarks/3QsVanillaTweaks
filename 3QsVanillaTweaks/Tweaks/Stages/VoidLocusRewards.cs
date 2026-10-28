using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class VoidLocusRewards
    {
        private const string Defaults = "White:40,Green:30,Red:5,VoidWhite:10,VoidGreen:10,VoidRed:5";
        private static ConfigEntry<string> chances;
        private static string cachedText;
        private static Dictionary<string, float> weights;

        internal static void Bind(string section)
        {
            chances = ModConfig.Bind(section, "Signal Reward Chances", Defaults,
                "Chance for each of the three choices as Rarity:Chance, separated by commas. Rarities: White, Green, Red, Yellow, Lunar, VoidWhite, VoidGreen, VoidRed, VoidYellow. Chances are relative; 0 excludes a rarity.");
        }

        internal static List<PickupIndex> Pool(ItemTier tier)
        {
            Run run = Run.instance;
            List<PickupIndex> source;
            switch (tier)
            {
                case ItemTier.Tier1: source = run.availableTier1DropList; break;
                case ItemTier.Tier2: source = run.availableTier2DropList; break;
                case ItemTier.Tier3: source = run.availableTier3DropList; break;
                case ItemTier.Boss: source = run.availableBossDropList; break;
                case ItemTier.Lunar: source = run.availableLunarItemDropList; break;
                case ItemTier.VoidTier1: source = run.availableVoidTier1DropList; break;
                case ItemTier.VoidTier2: source = run.availableVoidTier2DropList; break;
                case ItemTier.VoidTier3: source = run.availableVoidTier3DropList; break;
                case ItemTier.VoidBoss: source = run.availableVoidBossDropList; break;
                default: return new List<PickupIndex>();
            }
            bool isVoid = tier == ItemTier.VoidTier1 || tier == ItemTier.VoidTier2 || tier == ItemTier.VoidTier3 || tier == ItemTier.VoidBoss;
            if (isVoid && !SpawnRequirements.ExpansionEnabled("DLC1")) return new List<PickupIndex>();
            return source.Where(pickup =>
            {
                PickupDef def = PickupCatalog.GetPickupDef(pickup);
                return def != null && SpawnRequirements.ItemAllowed(ItemCatalog.GetItemDef(def.itemIndex));
            }).Distinct().ToList();
        }

        private static Dictionary<string, float> Weights()
        {
            string text = ModConfig.Get(chances);
            if (weights != null && text == cachedText) return weights;
            cachedText = text;
            weights = CommencementRewards.Parse(text, out bool invalid);
            if (weights.Remove("pearl")) invalid = true;
            if (weights.Count == 0) weights = CommencementRewards.Parse(Defaults, out _);
            if (invalid) ThreeQsVanillaTweaks.Log.Warning("Void Locus: invalid signal reward entries were ignored. Use Rarity:Chance, separated by commas.");
            return weights;
        }

        internal static PickupPickerController.Option[] Options(Xoroshiro128Plus rng)
        {
            string[] names = { "white", "green", "red", "yellow", "lunar", "voidwhite", "voidgreen", "voidred", "voidyellow" };
            ItemTier[] tiers = { ItemTier.Tier1, ItemTier.Tier2, ItemTier.Tier3, ItemTier.Boss, ItemTier.Lunar,
                ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss };
            var pools = tiers.Select(Pool).ToArray();
            var remaining = pools.Select(pool => new List<PickupIndex>(pool)).ToArray();
            var result = new List<PickupPickerController.Option>();
            Dictionary<string, float> configured = Weights();
            for (int slot = 0; slot < 3; slot++)
            {
                var selection = new WeightedSelection<int>();
                for (int i = 0; i < names.Length; i++)
                    if (remaining[i].Count > 0 && configured.TryGetValue(names[i], out float weight) && weight > 0f)
                        selection.AddChoice(i, weight);
                if (selection.Count == 0)
                {
                    if (result.Count == 0) return Array.Empty<PickupPickerController.Option>();
                    // Keep three slots even if fewer than three eligible items exist.
                    result.Add(result[rng.RangeInt(0, result.Count)]);
                    continue;
                }
                int tier = selection.Evaluate(rng.nextNormalizedFloat);
                int index = rng.RangeInt(0, remaining[tier].Count);
                PickupIndex chosen = remaining[tier][index];
                foreach (List<PickupIndex> pool in remaining) pool.Remove(chosen);
                result.Add(new PickupPickerController.Option { available = true, pickup = new UniquePickup(chosen) });
            }
            return result.ToArray();
        }

        internal static void Drop(Vector3 origin)
        {
            if (!NetworkServer.active || !Run.instance) return;
            var rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            PickupPickerController.Option[] options = Options(rng);
            if (options.Length == 0) return;
            GameObject prefab = StageInteractableAssets.Prefab("OptionPickup");
            if (!StageFeatureSpawnHelper.PrefabAllowed(prefab)) return;
            PickupDef first = PickupCatalog.GetPickupDef(options[0].pickup.pickupIndex);
            ItemDef item = first == null ? null : ItemCatalog.GetItemDef(first.itemIndex);
            ItemTier display = item ? item.tier : ItemTier.Tier1;
            int players = Run.instance.participatingPlayerCount;
            for (int i = 0; i < players; i++)
            {
                Quaternion facing = Quaternion.AngleAxis(360f * i / Math.Max(players, 1), Vector3.up);
                Vector3 position = origin + Vector3.up * 6f + facing * new Vector3(0f, 0f, 6f);
                Vector3 velocity = facing * new Vector3(0f, 30f, 8f);
                var info = new GenericPickupController.CreatePickupInfo
                {
                    pickup = new UniquePickup(PickupCatalog.FindPickupIndex(display)),
                    pickerOptions = options, prefabOverride = prefab, position = position, rotation = Quaternion.identity
                };
                PickupDropletController.CreatePickupDroplet(info, position, velocity);
            }
        }
    }
}
