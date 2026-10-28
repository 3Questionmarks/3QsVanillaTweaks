using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class CommencementRewards
    {
        private const string DefaultWeights = "White:50,Green:20,Red:5,Pearl:10,Lunar:15";
        private static ConfigEntry<string> chances;
        private static string cachedText;
        private static Dictionary<string, float> cachedWeights;

        internal static void Bind(string section)
        {
            chances = ModConfig.Bind(section, "Pillar Reward Chances", DefaultWeights,
                "Reward chances as Rarity:Chance, separated by commas. Rarities: White, Green, Red, Yellow, Lunar, Pearl, VoidWhite, VoidGreen, VoidRed, VoidYellow. Pearl has a 20% chance to become an Irradiant Pearl. Chances are relative; 0 excludes a reward.");
        }

        internal static string Rarity(string name)
        {
            switch (name.Trim().Replace(" ", "").ToLowerInvariant())
            {
                case "white": case "common": return "white";
                case "green": case "uncommon": return "green";
                case "red": case "rare": case "legendary": return "red";
                case "yellow": case "boss": return "yellow";
                case "blue": case "lunar": return "lunar";
                case "pearl": return "pearl";
                case "voidwhite": case "voidcommon": return "voidwhite";
                case "voidgreen": case "voiduncommon": return "voidgreen";
                case "voidred": case "voidlegendary": return "voidred";
                case "voidyellow": case "voidboss": return "voidyellow";
                default: return null;
            }
        }

        internal static Dictionary<string, float> Parse(string text, out bool invalid)
        {
            var result = new Dictionary<string, float>();
            invalid = false;
            foreach (string entry in (text ?? "").Split(','))
            {
                string[] pair = entry.Split(':');
                string rarity = pair.Length == 2 ? Rarity(pair[0]) : null;
                if (rarity == null || !float.TryParse(pair[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float weight) ||
                    float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f || weight > 1000000f)
                { invalid = true; continue; }
                result.TryGetValue(rarity, out float previous);
                result[rarity] = Math.Min(1000000f, previous + weight);
            }
            return result;
        }

        private static Dictionary<string, float> Weights()
        {
            string text = ModConfig.Get(chances);
            if (cachedWeights != null && text == cachedText) return cachedWeights;
            cachedText = text;
            cachedWeights = Parse(text, out bool invalid);
            if (cachedWeights.Count == 0) cachedWeights = Parse(DefaultWeights, out _);
            if (invalid) ThreeQsVanillaTweaks.Log.Warning("Commencement: invalid pillar reward entries were ignored. Use Rarity:Chance, separated by commas.");
            return cachedWeights;
        }

        private static List<PickupIndex> Pool(string rarity)
        {
            Run run = Run.instance;
            switch (rarity)
            {
                case "white": return run.availableTier1DropList;
                case "green": return run.availableTier2DropList;
                case "red": return run.availableTier3DropList;
                case "yellow": return run.availableBossDropList;
                case "lunar": return run.availableLunarItemDropList;
                case "voidwhite": return run.availableVoidTier1DropList;
                case "voidgreen": return run.availableVoidTier2DropList;
                case "voidred": return run.availableVoidTier3DropList;
                case "voidyellow": return run.availableVoidBossDropList;
                default: return new List<PickupIndex>();
            }
        }

        internal static void Drop(Vector3 pillarPosition)
        {
            if (!NetworkServer.active || !Run.instance) return;
            var rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            var selection = new WeightedSelection<string>();
            var pools = new Dictionary<string, List<PickupIndex>>();
            foreach (var entry in Weights())
            {
                if (entry.Value <= 0f) continue;
                if (entry.Key == "pearl")
                {
                    ItemDef pearl = Stage3SpawnHelper.Item("Pearl");
                    if (SpawnRequirements.ItemAllowed(pearl)) selection.AddChoice(entry.Key, entry.Value);
                    continue;
                }
                if (entry.Key.StartsWith("void", StringComparison.Ordinal) && !SpawnRequirements.ExpansionEnabled("DLC1")) continue;
                var pool = new List<PickupIndex>();
                foreach (PickupIndex index in Pool(entry.Key))
                {
                    PickupDef pickup = PickupCatalog.GetPickupDef(index);
                    ItemDef item = pickup == null ? null : ItemCatalog.GetItemDef(pickup.itemIndex);
                    if (SpawnRequirements.ItemAllowed(item)) pool.Add(index);
                }
                if (pool.Count == 0) continue;
                pools[entry.Key] = pool;
                selection.AddChoice(entry.Key, entry.Value);
            }
            if (selection.Count == 0) return;
            string chosen = selection.Evaluate(rng.nextNormalizedFloat);
            PickupIndex reward;
            if (chosen == "pearl")
            {
                ItemDef pearl = Stage3SpawnHelper.Item(rng.nextNormalizedFloat < 0.2f ? "ShinyPearl" : "Pearl");
                if (!SpawnRequirements.ItemAllowed(pearl)) pearl = Stage3SpawnHelper.Item("Pearl");
                if (!SpawnRequirements.ItemAllowed(pearl)) return;
                reward = PickupCatalog.FindPickupIndex(pearl.itemIndex);
            }
            else
            {
                List<PickupIndex> pool = pools[chosen];
                reward = pool[rng.RangeInt(0, pool.Count)];
            }
            Quaternion facing = Quaternion.AngleAxis(rng.nextNormalizedFloat * 360f, Vector3.up);
            Vector3 position = pillarPosition + Vector3.up * 6f + facing * new Vector3(0f, 0f, 8f);
            Vector3 velocity = facing * new Vector3(0f, 25f, 10f);
            var info = new GenericPickupController.CreatePickupInfo
            { pickup = new UniquePickup(reward), position = position, rotation = Quaternion.identity };
            PickupDropletController.CreatePickupDroplet(info, position, velocity);
        }
    }
}
