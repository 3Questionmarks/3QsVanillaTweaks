using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage3
{
    internal static class ScorchedAcres
    {
        private static ConfigEntry<bool> fragment;
        private static ConfigEntry<float> fragmentChance;
        private static ConfigEntry<int> level;
        private static readonly HashSet<PurchaseInteraction> Altars = new HashSet<PurchaseInteraction>();
        private static readonly HashSet<PurchaseInteraction> Rewarded = new HashSet<PurchaseInteraction>();

        internal static void Init()
        {
            const string section = "Stages - Stage 3 - Scorched Acres - Altar Of Gold";
            var settings = StageFeatureSpawnHelper.Bind(section, "wispgraveyard", "Altar Of Gold", -450.73f, 154.89f, 234.07f);
            fragment = ModConfig.BindBool(section, "Drop Aurelionite Fragment", true, "Buying this altar also drops a fragment offering copies of the buyer's items. Excludes lunar and void items.");
            fragmentChance = StageFeatureSpawnHelper.Percent(section, "Fragment Drop Chance (%)", 100f, "Chance to drop the additional fragment when the altar is purchased.");
            level = ModConfig.Bind(section, "Fragment Level", 3, new ConfigDescription("Fragment level: 1 chooses one item, 2 chooses two, and 3 chooses three.", new AcceptableValueRange<int>(1, 3)));
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
                StageFeatureSpawnHelper.Spawn(spawn, StageInteractableAssets.Interactable("ShrineGoldshores", "RoR2.PortalStatueBehavior"), instance =>
                {
                    Stage3SpawnHelper.ScaleCost(instance);
                    Altars.Add(instance.GetComponent<PurchaseInteraction>());
                }));
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.ScorchedAcres");
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(ScorchedAcres), nameof(BeforePurchase)),
                postfix: new HarmonyMethod(typeof(ScorchedAcres), nameof(AfterPurchase)));
            Run.onRunDestroyGlobal += run => { Altars.Clear(); Rewarded.Clear(); };
        }

        private static void BeforePurchase(PurchaseInteraction __instance, out bool __state)
        {
            __state = NetworkServer.active && Run.instance && Altars.Contains(__instance) && __instance.available;
        }

        private static void AfterPurchase(PurchaseInteraction __instance, Interactor activator, bool __state)
        {
            if (!__state || __instance.available || !ModConfig.Get(fragment) || !SpawnRequirements.ExpansionEnabled("DLC2") || !Rewarded.Add(__instance)) return;
            try
            {
                var rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
                if (!StageFeatureSpawnHelper.Roll(ModConfig.Get(fragmentChance), rng)) return;
                CharacterBody buyer = activator ? activator.GetComponent<CharacterBody>() : null;
                if (!buyer || !buyer.inventory) return;
                DropFragment(buyer.inventory, __instance.transform.position + Vector3.up * 2f, rng);
            }
            catch (Exception e) { Log.Warning("Altar of Gold fragment failed: " + e.Message); }
        }

        private static bool Eligible(ItemDef item)
        {
            return item && SpawnRequirements.ItemAllowed(item) &&
                (item.tier == ItemTier.Tier1 || item.tier == ItemTier.Tier2 || item.tier == ItemTier.Tier3 || item.tier == ItemTier.Boss || item.tier.ToString() == "FoodTier");
        }

        private static void DropFragment(Inventory inventory, Vector3 position, Xoroshiro128Plus rng)
        {
            var owned = new List<ItemDef>();
            foreach (ItemIndex index in ItemCatalog.allItems)
            {
                ItemDef item = ItemCatalog.GetItemDef(index);
#pragma warning disable CS0612, CS0618
                if (Eligible(item) && inventory.GetItemCount(index) > 0) owned.Add(item);
#pragma warning restore CS0612, CS0618
            }
            if (owned.Count == 0) { Log.Info("Altar of Gold: buyer has no eligible items for a fragment."); return; }
            Stage3SpawnHelper.Shuffle(owned, rng);
            int fragmentLevel = ModConfig.Get(level);
            var options = new PickupPickerController.Option[fragmentLevel + 2];
            for (int i = 0; i < options.Length; ++i)
                options[i] = new PickupPickerController.Option { available = true, pickup = new UniquePickup(PickupCatalog.FindPickupIndex(owned[i % owned.Count].itemIndex)) };
            var shrine = StageFeatureSpawnHelper.LoadPrefab("RoR2/DLC2/ShrineHalcyonite.prefab").GetComponent<HalcyoniteShrineInteractable>();
            var field = AccessTools.Field(typeof(HalcyoniteShrineInteractable), "rewardPickupPrefab");
            GameObject prefab = field?.GetValue(shrine) as GameObject;
            if (!StageFeatureSpawnHelper.PrefabAllowed(prefab)) return;
            object displayTier = AccessTools.Field(typeof(HalcyoniteShrineInteractable), "rewardDisplayTier")?.GetValue(shrine);
            GenericPickupController.CreatePickup(new GenericPickupController.CreatePickupInfo
            {
                prefabOverride = prefab, pickerOptions = options,
                pickup = new UniquePickup(PickupCatalog.FindPickupIndex(displayTier is ItemTier tier ? tier : ItemTier.Boss)),
                position = position, rotation = Quaternion.identity
            });
        }
    }
}
