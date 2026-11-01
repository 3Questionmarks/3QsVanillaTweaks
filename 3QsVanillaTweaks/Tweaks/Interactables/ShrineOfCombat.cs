using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class ShrineOfCombat
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> goldReward;
        private static readonly ConfigEntry<float>[] chances = new ConfigEntry<float>[9];
        private static readonly System.Reflection.FieldInfo PurchaseCount = AccessTools.Field(typeof(ShrineCombatBehavior), "purchaseCount");

        internal sealed class Rewards : MonoBehaviour { internal int LastPurchase; }

        internal static void Init()
        {
            const string section = "Interactables - Shrine of Combat";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Drop one copy of the same item per player when the shrine's monsters are defeated.");
            goldReward = ModConfig.Bind(section, "Enemy Gold Reward (%)", 50f,
                new ConfigDescription("Gold awarded by shrine enemies, as a percentage of their normal reward. Experience is unchanged. Vanilla: 100%.", new AcceptableValueRange<float>(0f, 100000f)));
            string[] labels = { "Common", "Uncommon", "Legendary", "Boss", "Lunar", "Void Common", "Void Uncommon", "Void Legendary", "Void Boss" };
            float[] defaults = { 65f, 34f, 1f, 0f, 0f, 0f, 0f, 0f, 0f };
            for (int i = 0; i < chances.Length; ++i)
                chances[i] = ModConfig.Bind(section, labels[i] + " Reward Weight", defaults[i],
                    new ConfigDescription("Relative chance of a " + labels[i].ToLowerInvariant() + " reward. Set to 0 to exclude this rarity.", new AcceptableValueRange<float>(0f, 100f)));
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.Combat");
            harmony.Patch(AccessTools.Method(typeof(ShrineCombatBehavior), "AddShrineStack"),
                prefix: new HarmonyMethod(typeof(ShrineOfCombat), nameof(BeforeActivation)));
            harmony.Patch(
                AccessTools.Method(typeof(ShrineCombatBehavior), "OnDefeatedServer"),
                postfix: new HarmonyMethod(typeof(ShrineOfCombat), nameof(AfterDefeated)));
        }

        private static void BeforeActivation(ShrineCombatBehavior __instance)
        {
            if (!NetworkServer.active || !__instance) return;
            ShrineEncounterRewards.ConfigureGold(__instance.GetComponent<CombatDirector>(),
                () => ModConfig.Get(enabled) ? ModConfig.Get(goldReward) : 100f);
        }

        private static void AfterDefeated(ShrineCombatBehavior __instance)
        {
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !__instance) return;
            int purchased = (int)PurchaseCount.GetValue(__instance);
            Rewards state = __instance.GetComponent<Rewards>();
            if (!state) state = __instance.gameObject.AddComponent<Rewards>();
            if (purchased <= state.LastPurchase) return;
            state.LastPurchase = purchased;
            Run run = Run.instance;
            var rng = new Xoroshiro128Plus(run.treasureRng.nextUlong);
            var selection = new WeightedSelection<List<PickupIndex>>();
            List<PickupIndex>[] pools = { run.availableTier1DropList, run.availableTier2DropList,
                run.availableTier3DropList, run.availableBossDropList, run.availableLunarItemDropList,
                run.availableVoidTier1DropList, run.availableVoidTier2DropList, run.availableVoidTier3DropList, run.availableVoidBossDropList };
            for (int i = 0; i < pools.Length; ++i)
            {
                float weight = ModConfig.Get(chances[i]);
                if (weight <= 0f) continue;
                var allowed = new List<PickupIndex>();
                foreach (PickupIndex pickup in pools[i])
                {
                    PickupDef def = PickupCatalog.GetPickupDef(pickup);
                    ItemDef item = def == null ? null : ItemCatalog.GetItemDef(def.itemIndex);
                    if (item && SpawnRequirements.ItemAllowed(item)) allowed.Add(pickup);
                }
                if (allowed.Count > 0) selection.AddChoice(allowed, weight);
            }
            if (selection.Count == 0) return;
            List<PickupIndex> chosen = selection.Evaluate(rng.nextNormalizedFloat);
            var pickupState = new UniquePickup(chosen[rng.RangeInt(0, chosen.Count)]);
            int players = run.participatingPlayerCount;
            Bounds bounds = ShrineEncounterRewards.BoundsFor(__instance.gameObject);
            float angle = rng.nextNormalizedFloat * 360f;
            ShrineEncounterRewards.PlayRewardEffect(bounds.center);
            for (int i = 0; i < players; ++i)
                ShrineEncounterRewards.Launch(pickupState, bounds, i, players, angle);
        }
    }
}
