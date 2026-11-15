using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using Glass = Tweaks.ModCompatibility.LookingGlassStatRegistry;
using Purity = Tweaks.Items.Lunar.Purity;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Void
{
    internal static class BenthicBloom
    {
        static R settings;
        static ConfigEntry<int> baseUpgrades, extraUpgrades, maxPerStack;
        static ConfigEntry<bool> countIncreasesOnly, differentStacks, prioritiseScrap, sameType, upgradeVoid, allowClovers, swapEffects;
        static bool installed;

        sealed class StageVisit
        {
            internal Stage Stage;
            internal int Count;
            internal bool CountIncreased;
        }

        static readonly ConditionalWeakTable<Run, StageVisit> stageVisits = new ConditionalWeakTable<Run, StageVisit>();
        internal static bool SwapActive => installed && Purity.StatsPatched && C.Get(swapEffects);
        static bool Active => SwapActive || settings.Active;

        internal static void Init()
        {
            settings = new R("Benthic Bloom", "CloverVoid", true, "Void");
            string section = settings.Section;
            baseUpgrades = S.Int(section, "Number Of Items Upgraded", 5,
                "Upgrade selections per stage. Each selection upgrades items from one stack. Vanilla: 3.");
            extraUpgrades = S.Int(section, "Number Of Items Upgraded Per Stack", 3,
                "Additional upgrade selections for each extra stack of the item granting upgrades. Vanilla: 3.");
            countIncreasesOnly = C.BindBool(section, "Only Upgrade When Stage Count Increments", false,
                "Upgrade on entry to a stage only if the run's stage count has increased since the previous entry. Vanilla: false.");
            maxPerStack = S.Int(section, "Maximum Items Consumed Per Chosen Stack", 1,
                "Maximum copies upgraded per selection. Set to 0 to upgrade the entire chosen stack. Vanilla: 0.");
            differentStacks = C.BindBool(section, "Choose Different Stacks", false,
                "Choose each item stack at most once per stage when the per-selection limit is above 0. Vanilla: true.");
            prioritiseScrap = C.BindBool(section, "Prioritise Scrap To Upgrade", true,
                "Choose from eligible common and uncommon scrap stacks first. Vanilla: false.");
            sameType = C.BindBool(section, "Keep Same Item Type", true,
                "Prefer upgrades that share a Damage, Healing, or Utility tag. Scrap and items with no matching upgrades use the full destination pool. Vanilla: true.");
            upgradeVoid = C.BindBool(section, "Upgrade Void Items", false,
                "Allow Void common and uncommon items to upgrade within Void rarities. Vanilla: false.");
            allowClovers = C.BindBool(section, "Can Upgrade Into Clovers Or Blooms", true,
                "Allow uncommon items to upgrade into 57 Leaf Clover, and into Benthic Bloom when Upgrade Void Items is on. Vanilla: allows 57 Leaf Clover.");
            swapEffects = C.BindBool("Items - Effect Swaps", "Swap Benthic Bloom And Purity Effects", true,
                "Move Purity's configured cooldown reduction to Benthic Bloom and Benthic Bloom's configured upgrades to Purity. Purity keeps its luck change and Benthic Bloom keeps its corruption. Turn this off to use the individual item Enabled settings.");

            U.Harmony.Patch(A.Method(typeof(CharacterMaster), "TryCloverVoidUpgrades"),
                prefix: new HarmonyMethod(typeof(BenthicBloom), nameof(BeforeUpgrades)));
            installed = true;

            RegisterText(settings, () => Active,
                () => (SwapActive ? Purity.CooldownDescription() : UpgradeDescription()) + Corruption,
                () => (SwapActive ? Purity.CooldownPickup() : UpgradePickup()) + Corruption);
            Glass.RegisterItem(() => DLC1Content.Items.CloverVoid ? DLC1Content.Items.CloverVoid.itemIndex : ItemIndex.None,
                (_, stacks, text) => SwapActive ? Purity.CooldownStat(stacks) : UpgradeStat(stacks), () => Active);
        }

        const string Corruption = " <style=cIsVoid>Corrupts all 57 Leaf Clovers</style>.";

        internal static string UpgradeDescription()
        {
            int limit = C.Get(maxPerStack);
            int extra = C.Get(extraUpgrades);
            string count = "<style=cIsUtility>" + C.Get(baseUpgrades) + "</style>";
            if (extra > 0)
                count += " <style=cStack>(+" + extra + " per stack)</style>";
            return "Upgrade " + count + (limit == 1 ? " random items" : " random item stacks") +
                " to the next <style=cIsUtility>higher rarity</style> " + UpgradeTiming() +
                (limit > 1 ? " Upgrade up to " + limit + " copies per selection." : "");
        }

        static string UpgradeTiming() => C.Get(countIncreasesOnly) ?
            "when entering a stage after the stage count increases." : "at the start of each stage.";

        internal static string UpgradePickup() => "Upgrade random items " + UpgradeTiming();

        internal static string UpgradeStat(int stacks) =>
            Glass.AddValue("", "Upgrades Per Stage", UpgradeCount(stacks), "", "#91C8DD");

        static int UpgradeCount(int stacks) => stacks > 0 ?
            (int)Math.Min(int.MaxValue, C.Get(baseUpgrades) + (long)(stacks - 1) * C.Get(extraUpgrades)) : 0;

        static bool BeforeUpgrades(CharacterMaster __instance, ref Xoroshiro128Plus ___cloverVoidRng)
        {
            if (!Active)
                return true;
            var run = Run.instance;
            var inventory = __instance.inventory;
            if (!NetworkServer.active || !run || !inventory)
                return false;

            bool increased = StageCountIncreased(run);
            if (C.Get(countIncreasesOnly) && !increased)
                return false;

            var effectItem = SwapActive ? RoR2Content.Items.LunarBadLuck : DLC1Content.Items.CloverVoid;
            int upgrades = UpgradeCount(inventory.GetItemCountEffective(effectItem));
            if (upgrades <= 0)
                return false;

            if (___cloverVoidRng == null)
                ___cloverVoidRng = new Xoroshiro128Plus(run.seed);
            if (UpgradeItems(run, inventory, ___cloverVoidRng, upgrades))
            {
                var body = __instance.GetBody();
                if (body)
                    Util.PlaySound("Play_item_proc_extraLife", body.gameObject);
            }
            return false;
        }

        static bool StageCountIncreased(Run run)
        {
            var visit = stageVisits.GetOrCreateValue(run);
            if (visit.Stage != Stage.instance)
            {
                visit.Stage = Stage.instance;
                visit.CountIncreased = run.stageClearCount > visit.Count;
                visit.Count = run.stageClearCount;
            }
            return visit.CountIncreased;
        }

        static bool UpgradeItems(Run run, Inventory inventory, Xoroshiro128Plus rng, int upgrades)
        {
            bool voidUpgrades = C.Get(upgradeVoid);
            bool clovers = C.Get(allowClovers);
            var pools = new Dictionary<ItemTier, List<ItemDef>>
            {
                [ItemTier.Tier1] = ReadPool(run.availableTier2DropList, ItemTier.Tier2),
                [ItemTier.Tier2] = ReadPool(run.availableTier3DropList, ItemTier.Tier3)
            };
            var clover = RoR2Content.Items.Clover;
            if (clover && !clovers)
                pools[ItemTier.Tier2].Remove(clover);

            if (voidUpgrades)
            {
                pools[ItemTier.VoidTier1] = ReadPool(run.availableVoidTier2DropList, ItemTier.VoidTier2);
                var voidLegendary = ReadPool(run.availableVoidTier3DropList, ItemTier.VoidTier3);
                var bloom = DLC1Content.Items.CloverVoid;
                if (bloom)
                {
                    if (clovers && run.IsItemAvailable(bloom.itemIndex))
                    {
                        if (!voidLegendary.Contains(bloom))
                            voidLegendary.Add(bloom);
                        pools[ItemTier.Tier2].Add(bloom);
                    }
                    else
                        voidLegendary.Remove(bloom);
                }
                pools[ItemTier.VoidTier2] = voidLegendary;
            }

            int limit = C.Get(maxPerStack);
            bool unique = limit > 0 && C.Get(differentStacks);
            bool scrapFirst = C.Get(prioritiseScrap);
            bool matchType = C.Get(sameType);
            var excluded = new HashSet<ItemIndex>();
            var candidates = new List<ItemDef>();
            var scrap = new List<ItemDef>();
            var matching = new List<ItemDef>();
            bool changed = false;
            while (upgrades > 0)
            {
                candidates.Clear();
                scrap.Clear();
                foreach (var index in inventory.itemAcquisitionOrder)
                {
                    if (excluded.Contains(index) || inventory.GetItemCountEffective(index) <= 0)
                        continue;
                    var item = ItemCatalog.GetItemDef(index);
                    if (!item || !item.canRemove || !pools.TryGetValue(item.tier, out var pool) || pool.Count == 0)
                        continue;
                    candidates.Add(item);
                    if (scrapFirst && (item.tier == ItemTier.Tier1 || item.tier == ItemTier.Tier2) && IsScrap(item))
                        scrap.Add(item);
                }

                var sources = scrap.Count > 0 ? scrap : candidates;
                if (sources.Count == 0)
                    break;
                var source = sources[rng.RangeInt(0, sources.Count)];
                var destinations = pools[source.tier];
                if (matchType && !IsScrap(source))
                {
                    matching.Clear();
                    foreach (var destination in destinations)
                        if (SharesType(source, destination))
                            matching.Add(destination);
                    if (matching.Count > 0)
                        destinations = matching;
                }

                var target = destinations[rng.RangeInt(0, destinations.Count)];
                // Use the native transformation to preserve temporary/rental stacks and the upgrade notification.
                var transformation = new Inventory.ItemTransformation
                {
                    originalItemIndex = source.itemIndex,
                    newItemIndex = target.itemIndex,
                    maxToTransform = limit == 0 ? int.MaxValue : limit,
                    transformationType = (ItemTransformationTypeIndex)2
                };
                bool upgraded = transformation.TryTransform(inventory, out var result) && result.totalTransformed > 0;
                if (unique || !upgraded)
                    excluded.Add(source.itemIndex);
                if (upgraded)
                {
                    changed = true;
                    upgrades--;
                }
            }
            return changed;
        }

        static List<ItemDef> ReadPool(IEnumerable<PickupIndex> pickups, ItemTier tier)
        {
            var pool = new List<ItemDef>();
            foreach (var pickup in pickups)
            {
                var def = PickupCatalog.GetPickupDef(pickup);
                var item = def == null ? null : ItemCatalog.GetItemDef(def.itemIndex);
                if (item && item.tier == tier && !pool.Contains(item))
                    pool.Add(item);
            }
            return pool;
        }

        static bool IsScrap(ItemDef item) => item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap);

        static bool SharesType(ItemDef source, ItemDef target) =>
            (source.ContainsTag(ItemTag.Damage) && target.ContainsTag(ItemTag.Damage)) ||
            (source.ContainsTag(ItemTag.Healing) && target.ContainsTag(ItemTag.Healing)) ||
            (source.ContainsTag(ItemTag.Utility) && target.ContainsTag(ItemTag.Utility));

        internal static void RegisterText(R config, Func<bool> active, Func<string> description, Func<string> pickup)
        {
            RoR2Application.onLoad += () =>
            {
                var item = config.Item;
                if (!item)
                    return;
                string nativeDescription = item.descriptionToken, nativePickup = item.pickupToken;
                string oldDescription = Language.GetString(nativeDescription), oldPickup = Language.GetString(nativePickup);
                item.descriptionToken = "3QS_" + config.ItemName.ToUpperInvariant() + "_DESC";
                item.pickupToken = "3QS_" + config.ItemName.ToUpperInvariant() + "_PICKUP";
                Func<string> descriptionText = () => active() ? description() : oldDescription;
                Func<string> pickupText = () => active() ? pickup() : oldPickup;
                S.Text(nativeDescription, descriptionText);
                S.Text(item.descriptionToken, descriptionText);
                S.Text(nativePickup, pickupText);
                S.Text(item.pickupToken, pickupText);
            };
        }
    }
}
