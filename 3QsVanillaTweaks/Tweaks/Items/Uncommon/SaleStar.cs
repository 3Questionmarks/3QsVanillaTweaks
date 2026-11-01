using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class SaleStar
    {
        static UncommonConfig c;
        sealed class State
        {
            internal int Used;
            internal int Stage = -1;
        }

        static readonly ConditionalWeakTable<CharacterMaster, State> states = new ConditionalWeakTable<CharacterMaster, State>();
        sealed class Purchase
        {
            internal Purchase Previous;
            internal CharacterBody Body;
            internal State State;
            internal int Count, Original;
                    }

        [ThreadStatic] static Purchase current;

        internal static void Init()
        {
            c = new UncommonConfig("Sale Star", "LowerPricedChests", true);
            c.Pair("Extra Item Chance (%)", 0, 0, "Chance of one further extra item on a qualifying chest, in addition to the guaranteed extra item. Vanilla: additional reward chances scale with stacks.");
            c.Pair("Uses Per Stage", 1, 1, "Qualifying chests per stage. Vanilla: 1 total.");
            c.Pair("Extra Items", 1, 0, "Guaranteed additional items per qualifying chest. Vanilla: 1, plus additional random rewards at higher stacks.");
            if (c.Enabled.Value)
            {
                U.Harmony.Patch(A.Method(typeof(PurchaseInteraction), "OnInteractionBegin"),
                    prefix: new HarmonyMethod(typeof(SaleStar), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(SaleStar), nameof(After)),
                    finalizer: new HarmonyMethod(typeof(SaleStar), nameof(Finish)));
                U.Patch(typeof(Inventory.ItemTransformation), "TryTransform", typeof(SaleStar), prefix: nameof(LimitConsumption));
            }

            c.Text(() => "The first " + c.V("Uses Per Stage", "", "cIsUtility") + " chests opened each stage drop " + c.V("Extra Items", "", "cIsUtility") + " additional items." + (c.F("Extra Item Chance (%)") > 0 ? " Each has a " + c.V("Extra Item Chance (%)", "%", "cIsUtility") + " chance to drop another item." : ""), () => "Gain <style=cIsUtility>extra items</style> from the first chests you open each stage.");
        }

        static void Before(PurchaseInteraction __instance, Interactor activator, out Purchase __state)
        {
            __state = new Purchase { Previous = current };
            current = null;
            if (!c.Active || !NetworkServer.active || !__instance.saleStarCompatible || !__instance.available || !activator)
                return;
            var b = activator.GetComponent<CharacterBody>();
            if (!b || !b.master || !b.inventory)
                return;
            int n = b.inventory.GetItemCountEffective(c.Item) + b.inventory.GetItemCountEffective(DLC2Content.Items.LowerPricedChestsConsumed);
            if (n == 0)
                return;
            var state = states.GetOrCreateValue(b.master);
            int stage = Run.instance ? Run.instance.stageClearCount : 0;
            if (state.Stage != stage)
            {
                state.Stage = stage;
                state.Used = 0;
            }

            int original = b.inventory.GetItemCountEffective(c.Item);
            __state.Body = b;
            __state.State = state;
            __state.Count = n;
            __state.Original = original;
            current = __state;
        }

        // Represent remaining uses as active stars. Defaults consume exactly one
        // per chest; custom use counts distribute consumption across those uses.
        internal static int RemainingStars(int count, int uses, int used)
        {
            if (count <= 0 || uses <= 0) return 0;
            int remaining = Math.Max(0, uses - used);
            return Math.Min(count, (int)Math.Ceiling((double)count * remaining / uses));
        }

        static void LimitConsumption(ref Inventory.ItemTransformation __instance, Inventory inventory)
        {
            var p = current;
            if (p == null || !p.Body || p.Body.inventory != inventory || !c.Active
                || __instance.originalItemIndex != c.Item.itemIndex
                || __instance.newItemIndex != DLC2Content.Items.LowerPricedChestsConsumed.itemIndex)
                return;
            int uses = Mathf.Max(0, Mathf.FloorToInt(c.Total("Uses Per Stage", p.Count)));
            int remaining = RemainingStars(p.Count, uses, p.State.Used + 1);
            int consume = Math.Max(1, p.Original - remaining);
            __instance.maxToTransform = Math.Min(__instance.maxToTransform, consume);
        }

        static Exception Finish(Exception __exception, Purchase __state)
        {
            current = __state?.Previous;
            return __exception;
        }

        static void After(PurchaseInteraction __instance, Purchase __state)
        {
            var p = __state;
            if (p == null || !p.Body || !p.Body.inventory)
                return;
            var inv = p.Body.inventory;
            int transformed = p.Original - inv.GetItemCountEffective(c.Item);
            if (transformed <= 0)
                return;
            var chest = __instance.GetComponent<ChestBehavior>();
            var roulette = __instance.GetComponent<RouletteChestController>();
            int uses = Mathf.Max(0, Mathf.FloorToInt(c.Total("Uses Per Stage", p.Count)));
            bool eligible = p.State.Used < uses;
            int extra = eligible ? Mathf.Max(0, Mathf.FloorToInt(c.Total("Extra Items", p.Count))) : 0;
            if (eligible && U.Roll(c.Total("Extra Item Chance (%)", p.Count), p.Body))
                extra++;
            if (chest)
                chest.dropCount = 1 + extra;
            if (roulette)
                roulette.dropCount = 1 + extra;
            if (eligible)
                p.State.Used++;
            int desiredActive = RemainingStars(p.Count, uses, p.State.Used);
            int restoreCount = Math.Min(transformed, Math.Max(0, desiredActive - inv.GetItemCountEffective(c.Item)));
            if (restoreCount > 0)
            {
                var restore = new Inventory.ItemTransformation
                {
                    originalItemIndex = DLC2Content.Items.LowerPricedChestsConsumed.itemIndex,
                    newItemIndex = c.Item.itemIndex,
                    maxToTransform = restoreCount
                };
                restore.TryTransform(inv, out var ignored);
            }
        }
    }
}
