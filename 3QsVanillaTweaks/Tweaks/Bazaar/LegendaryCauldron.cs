using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// In the Bazaar only, let the five-green-to-one-red cauldron alternatively
    /// take one red item with priority given to green scrap, then red scrap,
    /// then green items, then red items.
    /// Existing vanilla payment code still chooses scrap before other items.
    /// Also adds a custom world-space label showing the alternate cost.
    /// </summary>
    internal static class LegendaryCauldron
    {
        private const string Section = "Bazaar - 03 Legendary Cauldron";
        private const string LabelObjectName = "3QsLegendaryCauldronLabel";
        private static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Accept One Legendary Item", true,
                "In the Bazaar only, allow the legendary cauldron to accept one red item or red scrap as an alternative to five greens. Payment priority: five green scrap, one red scrap, five green items, one red item. Does not affect cauldrons on other stages.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.LegendaryCauldron");
            harmony.Patch(
                AccessTools.Method(typeof(PurchaseInteraction), "CanBeAffordedByInteractor", new[] { typeof(Interactor) }),
                prefix: new HarmonyMethod(typeof(LegendaryCauldron), nameof(CheckAffordability)));
            harmony.Patch(
                AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin", new[] { typeof(Interactor) }),
                prefix: new HarmonyMethod(typeof(LegendaryCauldron), nameof(BeginPurchase)),
                finalizer: new HarmonyMethod(typeof(LegendaryCauldron), nameof(EndPurchase)));

            SceneManager.sceneLoaded += OnSceneLoaded;

            Log.Info("Bazaar legendary cauldron alternate-payment tweak initialized.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!ModConfig.Get(enabled)) return;
            if (!string.Equals(scene.name, "bazaar", StringComparison.OrdinalIgnoreCase)) return;

            RoR2Application.onNextUpdate += ApplyBazaarLabels;
        }

        private static void ApplyBazaarLabels()
        {
            foreach (PurchaseInteraction purchase in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
            {
                if (!Applies(purchase)) continue;
                EnsureAlternateCostLabel(purchase);
            }
        }

        private static void EnsureAlternateCostLabel(PurchaseInteraction purchase)
        {
            if (purchase == null) return;
            Transform existing = purchase.transform.Find(LabelObjectName);
            if (existing != null) return;

            GameObject label = new GameObject(LabelObjectName);
            label.transform.SetParent(purchase.transform, false);
            label.transform.localPosition = new Vector3(0f, 5.25f, 0f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label.transform.localScale = Vector3.one * 0.22f;

            TextMeshPro text = label.AddComponent<TextMeshPro>();
            text.text = "<color=#ff4d4d>1 Red</color> <color=#e5e5e5>or</color> <color=#66ff66>5 Green</color>";
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = 12f;
            text.enableWordWrapping = false;
            text.outlineWidth = 0.18f;
            text.outlineColor = Color.black;
            text.color = Color.white;
            text.sortingOrder = 10;

            MeshRenderer renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // The label size is set by transform.localScale above.
            label.AddComponent<Billboard>();
        }

        private static bool Applies(PurchaseInteraction purchase)
        {
            if (purchase == null || !ModConfig.Get(enabled)) return false;
            if (purchase.costType != CostTypeIndex.GreenItem || purchase.cost != 5) return false;

            // Only the Bazaar Between Time. In particular this excludes the same
            // green-to-red cauldron prefab when it appears on the Moon or elsewhere.
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            return scene != null &&
                   string.Equals(scene.baseSceneName, "bazaar", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanPay(CostTypeIndex type, int price, Interactor buyer)
        {
            if (buyer == null) return false;
            CostTypeDef definition = CostTypeCatalog.GetCostTypeDef(type);
            return definition != null && definition.IsAffordable(price, buyer);
        }

        // Mirrors the eligibility rules of the game's item cost payment: permanent,
        // removable, non-objective items. Tag checks include special scrap.
        private static int CountScrap(Interactor buyer, ItemTier tier)
        {
            if (buyer == null) return 0;
            CharacterBody body = buyer.GetComponent<CharacterBody>();
            Inventory inventory = body != null ? body.inventory : null;
            if (inventory == null || inventory.inventoryDisabled) return 0;

            int total = 0;
            foreach (ItemIndex index in ItemCatalog.allItems)
            {
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (item == null || item.tier != tier || !item.canRemove ||
                    item.ContainsTag(ItemTag.ObjectiveRelated) ||
                    (!item.ContainsTag(ItemTag.Scrap) && !item.ContainsTag(ItemTag.PriorityScrap)))
                    continue;

                total += inventory.GetItemCountPermanent(index);
            }
            return total;
        }

        private static bool PreferRedPayment(Interactor buyer)
        {
            bool greenAffordable = CanPay(CostTypeIndex.GreenItem, 5, buyer);
            bool redAffordable = CanPay(CostTypeIndex.RedItem, 1, buyer);
            if (!redAffordable) return false;

            // Complete five-scrap green payments always win.
            if (greenAffordable && CountScrap(buyer, ItemTier.Tier2) >= 5) return false;

            // A red scrap is preferred over spending regular green items.
            if (CountScrap(buyer, ItemTier.Tier3) >= 1) return true;

            // Otherwise prefer five greens over a regular red item.
            return !greenAffordable;
        }

        // The game's normal interaction prompt calls this method. When neither
        // payment is possible we leave vanilla's unaffordable result unchanged.
        private static bool CheckAffordability(PurchaseInteraction __instance, Interactor activator, ref bool __result)
        {
            if (!Applies(__instance)) return true;
            if (CanPay(CostTypeIndex.GreenItem, 5, activator)) return true;

            __result = CanPay(CostTypeIndex.RedItem, 1, activator);
            return false;
        }

        // Switch only for the duration of this one server-side purchase; vanilla
        // handles removing the chosen red item, scrap priority, and item rewards.
        private static void BeginPurchase(PurchaseInteraction __instance, Interactor activator, ref bool __state)
        {
            __state = false;
            if (!NetworkServer.active || !Applies(__instance)) return;
            if (!PreferRedPayment(activator)) return;

            __instance.costType = CostTypeIndex.RedItem;
            __instance.cost = 1;
            __state = true;
        }

        // A finalizer restores the shop's original price even if the purchase
        // method throws, so later players still get the same choice of payment.
        private static Exception EndPurchase(PurchaseInteraction __instance, bool __state, Exception __exception)
        {
            if (__state && __instance != null)
            {
                __instance.costType = CostTypeIndex.GreenItem;
                __instance.cost = 5;
            }
            return __exception;
        }
    }
}
