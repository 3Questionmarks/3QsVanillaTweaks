using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// In the Bazaar only, let the five-green-to-one-red cauldron alternatively
    /// take one red item with priority given to green scrap, then red scrap,
    /// then green items, then red items.
    /// Existing vanilla payment code still chooses scrap before other items.
    /// Also shows both payments in the native price hologram and interaction prompt.
    /// </summary>
    internal static class LegendaryCauldron
    {
        private const string Section = "Bazaar - 03 Legendary Cauldron";
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

            AlternateCauldronCostDisplay.Register(Applies);
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "GetContextString", new[] { typeof(Interactor) }),
                postfix: new HarmonyMethod(typeof(LegendaryCauldron), nameof(AfterContext)));

            Log.Info("Bazaar legendary cauldron alternate-payment tweak initialized.");
        }

        private static void AfterContext(PurchaseInteraction __instance, ref string __result)
        {
            if (Applies(__instance)) __result += " (" + AlternateCauldronCostDisplay.RedOption() + ")";
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

    internal static class AlternateCauldronCostDisplay
    {
        private static readonly List<Func<PurchaseInteraction, bool>> rules = new List<Func<PurchaseInteraction, bool>>();
        private static bool installed;
        private sealed class PriceSource : MonoBehaviour { internal PurchaseInteraction Purchase; }

        internal static void Register(Func<PurchaseInteraction, bool> rule)
        {
            rules.Add(rule);
            if (installed) return;
            installed = true;
            R2API.LanguageAPI.Add("3QS_CAULDRON_COST_OR", "or");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.AlternateCauldronCostDisplay");
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "UpdateHologramContent"),
                postfix: new HarmonyMethod(typeof(AlternateCauldronCostDisplay), nameof(AfterHologram)));
            harmony.Patch(AccessTools.Method(typeof(CostHologramContent), "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(AlternateCauldronCostDisplay), nameof(BeforePriceUpdate)));
        }

        private static bool Applies(PurchaseInteraction purchase)
        {
            if (!purchase) return false;
            foreach (var rule in rules) if (rule(purchase)) return true;
            return false;
        }

        internal static string RedOption()
        {
            var text = new StringBuilder(Language.GetString("3QS_CAULDRON_COST_OR") + " ");
            CostTypeCatalog.GetCostTypeDef(CostTypeIndex.RedItem).BuildCostStringStyled(1, text, false, true);
            return text.ToString();
        }

        private static void AfterHologram(PurchaseInteraction __instance, GameObject hologramContentObject)
        {
            if (!hologramContentObject) return;
            PriceSource source = hologramContentObject.GetComponent<PriceSource>();
            if (!source && Applies(__instance)) source = hologramContentObject.AddComponent<PriceSource>();
            if (source) source.Purchase = __instance;
        }

        private static bool BeforePriceUpdate(CostHologramContent __instance, ref int ___oldDisplayValue)
        {
            PriceSource source = __instance.GetComponent<PriceSource>();
            if (!source || !Applies(source.Purchase) || !__instance.targetTextMesh) return true;
            var text = new StringBuilder();
            CostTypeCatalog.GetCostTypeDef(CostTypeIndex.GreenItem)
                .BuildCostStringStyled(source.Purchase.cost, text, true, true);
            text.Append("\n").Append(Language.GetString("3QS_CAULDRON_COST_OR")).Append(" ");
            CostTypeCatalog.GetCostTypeDef(CostTypeIndex.RedItem).BuildCostStringStyled(1, text, true, true);
            __instance.targetTextMesh.text = text.ToString();
            __instance.targetTextMesh.color = Color.white;
            // Force vanilla to restore its own label if the alternative is disabled.
            ___oldDisplayValue = int.MinValue;
            return false;
        }
    }
}
