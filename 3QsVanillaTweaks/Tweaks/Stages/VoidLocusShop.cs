using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class VoidLocusShop
    {
        private static ConfigEntry<bool> redAlternative;
        private static GameObject poolPrefab;
        private const string LegendaryPoolName = "3QS_VOID_LEGENDARY_POOL_NAME";
        private static readonly ItemTier[] VoidTiers = { ItemTier.VoidTier1, ItemTier.VoidTier2, ItemTier.VoidTier3, ItemTier.VoidBoss };
        private static readonly string[] PrinterPrefabs = { "Duplicator", "DuplicatorLarge", "DuplicatorMilitary", "DuplicatorWild" };
        internal sealed class PoolMarker : MonoBehaviour { }

        internal static void Bind()
        {
            const string section = "Stages - Void Locus - Void Shop";
            RegisterPool(section + " - Common Pool", "Void Common Pool", ItemTier.VoidTier1, CostTypeIndex.GreenItem, 2,
                -186.74f, 29.39f, 211.01f, 0.87f, -0.49f);
            RegisterPool(section + " - Uncommon Pool", "Void Uncommon Pool", ItemTier.VoidTier2, CostTypeIndex.WhiteItem, 3,
                -176.97f, 29.49f, 222.89f, -0.79f, -0.62f);
            RegisterPool(section + " - Legendary Pool", "Void Legendary Pool", ItemTier.VoidTier3, CostTypeIndex.GreenItem, 5,
                -162.41f, 29.58f, 233.07f, 0.59f, -0.81f);
            redAlternative = ModConfig.BindBool(section + " - Legendary Pool", "Accept One Legendary Item", true,
                "Also accept one legendary item. Payment priority: enough green scrap for a trade, one red scrap, green items, then a red item.");
            var scrapper = Location(section + " - Scrapper", "Void Shop Scrapper", -144.03f, 29.03f, 245.91f, 0.34f, -0.94f);
            StageFeatureSpawnHelper.Register(scrapper, (settings, rng) =>
            {
                if (VoidLocus.Ready) StageFeatureSpawnHelper.Spawn(settings, StageInteractableAssets.Prefab("Scrapper"));
            });
            float[,] positions = {
                { -68.42f, 29.31f, 237.43f, -0.05f, -1f },
                { -83.35f, 29.34f, 243.74f, -0.28f, -0.96f },
                { -101.04f, 29.42f, 248.53f, -0.08f, -1f },
                { -120.53f, 29.49f, 249.24f, 0.13f, -0.99f }
            };
            for (int i = 0; i < 4; i++)
            {
                string name = "Void Printer " + (i + 1);
                string printerSection = section + " - " + name;
                var printer = Location(printerSection, name, positions[i, 0], positions[i, 1], positions[i, 2], positions[i, 3], positions[i, 4]);
                printer.SpawnWithCommand = ModConfig.BindBool(printerSection, "Spawn With Command", false,
                    "Allow this printer to spawn while the Artifact of Command is enabled.");
                StageFeatureSpawnHelper.Register(printer, SpawnPrinter);
            }
            RegisterReturnPortal();
            R2API.LanguageAPI.Add("3QS_VOID_COMMON_POOL_NAME", "Void Common Pool");
            R2API.LanguageAPI.Add("3QS_VOID_UNCOMMON_POOL_NAME", "Void Uncommon Pool");
            R2API.LanguageAPI.Add(LegendaryPoolName, "Void Legendary Pool");
            R2API.LanguageAPI.Add("3QS_VOID_POOL_RED_OPTION", "or 1 legendary item");
            RoR2Application.onLoad += () =>
            {
                try { PreparePool(); }
                catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Locus pool prefab could not be prepared: " + error.Message); }
            };
            Tweaks.Bazaar.AlternateCauldronCostDisplay.Register(Alternative);
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.VoidLocusShop");
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "CanBeAffordedByInteractor", new[] { typeof(Interactor) }),
                prefix: new HarmonyMethod(typeof(VoidLocusShop), nameof(CheckAffordability)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "GetContextString", new[] { typeof(Interactor) }),
                postfix: new HarmonyMethod(typeof(VoidLocusShop), nameof(AfterContext)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin", new[] { typeof(Interactor) }),
                prefix: new HarmonyMethod(typeof(VoidLocusShop), nameof(BeforePurchase)),
                finalizer: new HarmonyMethod(typeof(VoidLocusShop), nameof(AfterPurchase)));
        }

        private static StageFeatureSpawnHelper.Settings Location(string section, string name,
            float x, float y, float z, float fx, float fz, bool snap = true)
        {
            return new StageFeatureSpawnHelper.Settings
            {
                SceneName = "voidstage", Name = name,
                Enabled = ModConfig.BindBool(section, "Enabled", true, "Spawn " + name + "."),
                Chance = StageFeatureSpawnHelper.Percent(section, "Spawn Chance (%)", 100f, "Chance to spawn this interactable."),
                X = ModConfig.Bind(section, "Position X", x, "Spawn position on the X axis."),
                Y = ModConfig.Bind(section, "Position Y", y, "Spawn height."),
                Z = ModConfig.Bind(section, "Position Z", z, "Spawn position on the Z axis."),
                ForwardX = ModConfig.Bind(section, "Forward X", fx, "Facing direction on the X axis."),
                ForwardZ = ModConfig.Bind(section, "Forward Z", fz, "Facing direction on the Z axis."),
                SnapToGround = ModConfig.BindBool(section, "Snap To Ground", snap, "Place the interactable on the ground."),
                AlignToGround = ModConfig.BindBool(section, "Align To Ground", snap, "Match the slope of the ground."),
                HeightOffset = ModConfig.Bind(section, "Height Offset", 0f, "Height above the ground."),
                GroundSearchDistance = ModConfig.Bind(section, "Ground Search Distance", 12f,
                    new ConfigDescription("Distance above and below the spawn height to search for ground.", new AcceptableValueRange<float>(0.1f, 100f)))
            };
        }

        private static void PreparePool()
        {
            if (poolPrefab) return;
            GameObject original = StageFeatureSpawnHelper.LoadPrefab("RoR2/Base/LunarCauldrons/LunarCauldron, GreenToRed Variant.prefab");
            poolPrefab = R2API.PrefabAPI.InstantiateClone(original, "3Qs_VoidLocusPool", true);
            poolPrefab.AddComponent<PoolMarker>();
        }

        private static void RegisterPool(string section, string name, ItemTier tier, CostTypeIndex input, int price,
            float x, float y, float z, float fx, float fz)
        {
            var location = Location(section, name, x, y, z, fx, fz);
            ConfigEntry<int> cost = ModConfig.Bind(section, "Cost", price,
                new ConfigDescription("Number of items required for one trade.", new AcceptableValueRange<int>(1, 100)));
            StageFeatureSpawnHelper.Register(location, (settings, rng) =>
            {
                if (!VoidLocus.Ready) return;
                List<PickupIndex> items = VoidLocusRewards.Pool(tier);
                if (items.Count == 0) return;
                PreparePool();
                PickupIndex reward = items[rng.RangeInt(0, items.Count)];
                StageFeatureSpawnHelper.Spawn(settings, poolPrefab, obj => ConfigureTerminal(obj, tier, reward, input, ModConfig.Get(cost), true));
            });
        }

        private static void ConfigureTerminal(GameObject obj, ItemTier tier, PickupIndex reward, CostTypeIndex input, int price, bool pool)
        {
            ShopTerminalBehavior terminal = obj.GetComponent<ShopTerminalBehavior>();
            PurchaseInteraction purchase = obj.GetComponent<PurchaseInteraction>();
            if (!terminal || !purchase) throw new InvalidOperationException("Void shop interactable lacks its terminal or purchase component.");
            terminal.dropTable = null;
            terminal.itemTier = tier;
            terminal.selfGeneratePickup = false;
            terminal.dropAmount = 1;
            var setter = AccessTools.Method(typeof(ShopTerminalBehavior), "SetPickup", new[] { typeof(UniquePickup), typeof(bool) });
            if (setter == null) throw new InvalidOperationException("The shop terminal's pickup setter is unavailable.");
            setter.Invoke(terminal, new object[] { new UniquePickup(reward), false });
            purchase.NetworkcostType = input;
            purchase.Networkcost = price;
            if (pool) purchase.NetworkdisplayNameToken = tier == ItemTier.VoidTier3 ? LegendaryPoolName :
                tier == ItemTier.VoidTier2 ? "3QS_VOID_UNCOMMON_POOL_NAME" : "3QS_VOID_COMMON_POOL_NAME";
        }

        private static void SpawnPrinter(StageFeatureSpawnHelper.Settings settings, Xoroshiro128Plus rng)
        {
            if (!VoidLocus.Ready || SpawnRequirements.CommandActive && !ModConfig.Get(settings.SpawnWithCommand)) return;
            var items = new List<PickupIndex>();
            foreach (ItemTier tier in VoidTiers) items.AddRange(VoidLocusRewards.Pool(tier));
            if (items.Count == 0) return;
            PickupIndex reward = items[rng.RangeInt(0, items.Count)];
            ItemDef item = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(reward).itemIndex);
            int grade = Array.IndexOf(VoidTiers, item.tier);
            if (grade < 0) return;
            CostTypeIndex input = InputForTier(item.tier);
            StageFeatureSpawnHelper.Spawn(settings, StageInteractableAssets.Prefab(PrinterPrefabs[grade]),
                obj => ConfigureTerminal(obj, item.tier, reward, input, 1, false));
        }

        internal static CostTypeIndex InputForTier(ItemTier tier)
        {
            switch (tier)
            {
                case ItemTier.VoidTier2: return CostTypeIndex.GreenItem;
                case ItemTier.VoidTier3: return CostTypeIndex.RedItem;
                case ItemTier.VoidBoss: return CostTypeIndex.BossItem;
                default: return CostTypeIndex.WhiteItem;
            }
        }

        private static void RegisterReturnPortal()
        {
            var location = Location("Stages - Void Locus - Return Portal", "Void Locus Return Portal",
                -37.22f, 20.46f, -264.38f, -0.04f, 1f, false);
            StageFeatureSpawnHelper.Register(location, (settings, rng) =>
            {
                if (!VoidLocus.Ready) return;
                StageFeatureSpawnHelper.Spawn(settings, StageInteractableAssets.Prefab("PortalVoid"), obj =>
                {
                    SceneExitController exit = obj.GetComponent<SceneExitController>();
                    if (!exit) throw new InvalidOperationException("Return portal has no scene exit controller.");
                    obj.AddComponent<VoidLocus.ReturnPortal>();
                    exit.useRunNextStageScene = false;
                    exit.isAlternatePath = false;
                    AccessTools.Field(typeof(SceneExitController), "doNotIncrementStageNumber")?.SetValue(exit, true);
                });
            });
        }

        private static bool Alternative(PurchaseInteraction purchase) => VoidLocus.Ready && purchase &&
            purchase.gameObject.scene.name == "voidstage" && purchase.GetComponentInParent<PoolMarker>() &&
            ModConfig.Get(redAlternative) && purchase.costType == CostTypeIndex.GreenItem &&
            purchase.displayNameToken == LegendaryPoolName;

        private static bool CanPay(CostTypeIndex type, int price, Interactor buyer) => buyer &&
            CostTypeCatalog.GetCostTypeDef(type) != null && CostTypeCatalog.GetCostTypeDef(type).IsAffordable(price, buyer);

        private static int Scrap(Interactor buyer, ItemTier tier)
        {
            CharacterBody body = buyer ? buyer.GetComponent<CharacterBody>() : null;
            Inventory inventory = body ? body.inventory : null;
            if (!inventory || inventory.inventoryDisabled) return 0;
            int count = 0;
            foreach (ItemIndex index in ItemCatalog.allItems)
            {
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (item && item.tier == tier && item.canRemove && !item.ContainsTag(ItemTag.ObjectiveRelated) &&
                    (item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap))) count += inventory.GetItemCountPermanent(index);
            }
            return count;
        }

        internal static bool PreferRed(bool greenAffordable, bool redAffordable, int greenScrap, int redScrap, int greenPrice)
        {
            if (!redAffordable || greenAffordable && greenScrap >= greenPrice) return false;
            return redScrap >= 1 || !greenAffordable;
        }

        private static bool CheckAffordability(PurchaseInteraction __instance, Interactor activator, ref bool __result)
        {
            if (!Alternative(__instance) || CanPay(CostTypeIndex.GreenItem, __instance.cost, activator)) return true;
            __result = CanPay(CostTypeIndex.RedItem, 1, activator);
            return false;
        }

        private static void AfterContext(PurchaseInteraction __instance, ref string __result)
        {
            if (Alternative(__instance)) __result += " (" + Tweaks.Bazaar.AlternateCauldronCostDisplay.RedOption() + ")";
        }

        internal sealed class Payment { internal CostTypeIndex Type; internal int Cost; }

        private static void BeforePurchase(PurchaseInteraction __instance, Interactor activator, out Payment __state)
        {
            __state = null;
            if (!NetworkServer.active || !Alternative(__instance) || !PreferRed(
                CanPay(CostTypeIndex.GreenItem, __instance.cost, activator), CanPay(CostTypeIndex.RedItem, 1, activator),
                Scrap(activator, ItemTier.Tier2), Scrap(activator, ItemTier.Tier3), __instance.cost)) return;
            __state = new Payment { Type = __instance.costType, Cost = __instance.cost };
            __instance.costType = CostTypeIndex.RedItem;
            __instance.cost = 1;
        }

        private static Exception AfterPurchase(PurchaseInteraction __instance, Payment __state, Exception __exception)
        {
            if (__instance && __state != null) { __instance.costType = __state.Type; __instance.cost = __state.Cost; }
            return __exception;
        }
    }
}
