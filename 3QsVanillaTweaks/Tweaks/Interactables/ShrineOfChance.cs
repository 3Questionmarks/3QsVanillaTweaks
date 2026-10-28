using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class ShrineOfChance
    {
        private static ConfigEntry<bool> enabled, improve, reset;
        private static ConfigEntry<int> payouts;
        private static ConfigEntry<float> costScaling, x, y, z;
        private static readonly ConfigEntry<float>[] initial = new ConfigEntry<float>[5];
        [ThreadStatic] private static Odds active;

        internal static void Init()
        {
            const string section = "Interactables - Shrine of Chance";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable the Shrine of Chance changes.");
            improve = ModConfig.BindBool(section, "Improve Rewards On Failure", true, "Improve the reward chances each time the shrine fails.");
            reset = ModConfig.BindBool(section, "Reset Chances On Success", false, "Restore the starting chances after a successful purchase.");
            payouts = ModConfig.Bind(section, "Maximum Payouts", 2, new ConfigDescription("Number of successful purchases before the shrine is exhausted.", new AcceptableValueRange<int>(1, 1000)));
            costScaling = ModConfig.Bind(section, "Cost Multiplier Per Purchase", 1.4f, new ConfigDescription("Multiply the cost by this amount after each purchase.", new AcceptableValueRange<float>(1f, 100f)));
            string[] labels = { "Nothing", "Equipment", "Common", "Uncommon", "Legendary" };
            float[] defaults = { 50f, 0f, 40f, 9f, 1f };
            for (int i = 0; i < initial.Length; ++i)
                initial[i] = Percent(section, labels[i] + " Starting Chance (%)", defaults[i], "Starting chance of " + labels[i].ToLowerInvariant() + ". Starting chances are scaled together to total 100%.");
            x = Percent(section, "X - Chance Removed On Failure (%)", 10f, "Percentage points moved from the lowest item rarity above 1% to higher rarities. If no lower rarity is above 1%, reduce Nothing instead.");
            y = Percent(section, "Y - Uncommon Increase (%)", 9f, "Share of X added to Uncommon while Common remains. Y and Z are scaled together to total X.");
            z = Percent(section, "Z - Legendary Increase (%)", 1f, "Share of X added to Legendary while Common remains. Once Common is gone, Legendary receives all of X.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.Chance");
            harmony.Patch(AccessTools.Method(typeof(ShrineChanceBehavior), "Start"), postfix: new HarmonyMethod(typeof(ShrineOfChance), nameof(Configure)));
            harmony.Patch(AccessTools.Method(typeof(ShrineChanceBehavior), "AddShrineStack"),
                prefix: new HarmonyMethod(typeof(ShrineOfChance), nameof(BeforePurchase)),
                finalizer: new HarmonyMethod(typeof(ShrineOfChance), nameof(AfterPurchase)));
            ShrineChanceBehavior.onShrineChancePurchaseGlobal += OnResult;
        }

        private static ConfigEntry<float> Percent(string section, string key, float value, string description)
        {
            return ModConfig.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(0f, 100f)));
        }

        internal sealed class Odds : MonoBehaviour
        {
            internal float[] Weights;
            internal ChanceTable Table;
            internal int Attempts;
            private void OnDestroy() { if (Table) UnityEngine.Object.Destroy(Table); }
        }

        internal sealed class ChanceTable : PickupDropTable
        {
            internal Odds Owner;
            public override int GetPickupCount() => Run.instance ? Run.instance.availableTier1DropList.Count +
                Run.instance.availableTier2DropList.Count + Run.instance.availableTier3DropList.Count + Run.instance.availableEquipmentDropList.Count : 0;

            public override UniquePickup GeneratePickupPreReplacement(Xoroshiro128Plus rng)
            {
                Run run = Run.instance;
                if (!run || !Owner) return UniquePickup.none;
                var choices = new WeightedSelection<List<PickupIndex>>();
                Add(run.availableEquipmentDropList, Owner.Weights[1]);
                Add(run.availableTier1DropList, Owner.Weights[2]);
                Add(run.availableTier2DropList, Owner.Weights[3]);
                Add(run.availableTier3DropList, Owner.Weights[4]);
                if (choices.Count == 0) return UniquePickup.none;
                List<PickupIndex> pool = choices.Evaluate(rng.nextNormalizedFloat);
                return new UniquePickup(pool[rng.RangeInt(0, pool.Count)]);
                void Add(List<PickupIndex> pool, float weight)
                {
                    if (weight > 0f && pool.Count > 0) choices.AddChoice(pool, weight);
                }
            }
        }

        private static void Configure(ShrineChanceBehavior __instance)
        {
            if (!NetworkServer.active || !ModConfig.Get(enabled)) return;
            __instance.maxPurchaseCount = ModConfig.Get(payouts);
            __instance.costMultiplierPerPurchase = ModConfig.Get(costScaling);
        }

        private static float[] StartingWeights()
        {
            var weights = new float[initial.Length];
            float sum = 0f;
            for (int i = 0; i < weights.Length; ++i) sum += weights[i] = ModConfig.Get(initial[i]);
            if (sum <= 0f) { weights[0] = 100f; return weights; }
            for (int i = 0; i < weights.Length; ++i) weights[i] *= 100f / sum;
            return weights;
        }

        internal sealed class PurchaseState
        {
            internal Odds Previous;
            internal PickupDropTable Table;
            internal float Failure;
        }

        private static void BeforePurchase(ShrineChanceBehavior __instance, Interactor activator, out PurchaseState __state)
        {
            __state = null;
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !activator) return;
            CharacterBody body = activator.GetComponent<CharacterBody>();
            if (!body || !body.inventory) return;
            Configure(__instance);
            Odds odds = __instance.GetComponent<Odds>();
            if (!odds)
            {
                odds = __instance.gameObject.AddComponent<Odds>();
                odds.Weights = StartingWeights();
                odds.Table = ScriptableObject.CreateInstance<ChanceTable>();
                odds.Table.Owner = odds;
                odds.Table.canDropBeReplaced = __instance.dropTable && __instance.dropTable.canDropBeReplaced;
            }
            __state = new PurchaseState { Previous = active, Table = __instance.dropTable, Failure = __instance.failureChance };
            active = odds;
            __instance.dropTable = odds.Table;
            __instance.failureChance = odds.Weights[0] / 100f;
            ++odds.Attempts;
            DebugStuff.ChanceShrineChances.Record(odds.gameObject, odds.Attempts, "Before purchase", odds.Weights);
        }

        private static void AfterPurchase(ShrineChanceBehavior __instance, PurchaseState __state)
        {
            if (__state == null) return;
            __instance.dropTable = __state.Table;
            __instance.failureChance = __state.Failure;
            active = __state.Previous;
        }

        private static void OnResult(bool failed, Interactor activator)
        {
            if (!NetworkServer.active || !active) return;
            if (!failed)
            {
                if (ModConfig.Get(reset)) active.Weights = StartingWeights();
            }
            else if (ModConfig.Get(improve)) Improve(active.Weights, ModConfig.Get(x), ModConfig.Get(y), ModConfig.Get(z));
            DebugStuff.ChanceShrineChances.Record(active.gameObject, active.Attempts,
                failed ? "After failure; next purchase" : "After success; next purchase", active.Weights);
        }

        internal static void Improve(float[] weights, float amount, float uncommonShare, float legendaryShare)
        {
            if (amount <= 0f || uncommonShare + legendaryShare <= 0f) return;
            int donor = weights[2] > 1f ? 2 : weights[3] > 1f ? 3 : 0;
            float moved = Mathf.Min(amount, weights[donor]);
            weights[donor] -= moved;
            if (donor == 2)
            {
                float uncommon = moved * uncommonShare / (uncommonShare + legendaryShare);
                weights[3] += uncommon;
                weights[4] += moved - uncommon;
            }
            else weights[4] += moved;
        }
    }
}
