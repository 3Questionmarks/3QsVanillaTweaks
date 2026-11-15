using System;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
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
        private const string RetroactiveMacroChestCost = "SaleStarCost";
        static UncommonConfig c;
        static bool useRetroactiveMacro;
        static ConfigEntry<bool> macroReopenFailure, macroExcludeSaleStarChests;
        sealed class State
        {
            internal int Used;
            internal RoR2.Stage Stage;
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
            c.Pair("Uses Per Stage", 1, 1, "Successful Sale Star uses per stage, including RetroactiveMacro reopens. Vanilla: 1 total.");
            c.Pair("Extra Items", 1, 0, "Guaranteed additional items per qualifying chest. Vanilla: 1, plus additional random rewards at higher stacks.");
            useRetroactiveMacro = Chainloader.PluginInfos.TryGetValue(ThreeQsVanillaTweaks.VanillaTweaksPlugin.RetroactiveMacroGuid, out var plugin) &&
                plugin.Instance && plugin.Instance.Config.TryGetEntry<bool>("Sale Star", "Change Sale Star", out var changeSaleStar) &&
                changeSaleStar.Value;
            if (useRetroactiveMacro)
            {
                plugin.Instance.Config.TryGetEntry<bool>("Sale Star", "Nerf/Rework", out macroReopenFailure);
                plugin.Instance.Config.TryGetEntry<bool>("Sale Star", "Exclude Sale Star Chests", out macroExcludeSaleStarChests);
                c.ShowStat = key => key == "Uses Per Stage";
            }
            if (c.Enabled.Value)
            {
                // RetroactiveMacro matches the native saleStarCompatible field read and branch.
                // Keep those instructions intact when its chest rework is active.
                U.Harmony.Patch(A.Method(typeof(PurchaseInteraction), "OnInteractionBegin"),
                    prefix: new HarmonyMethod(typeof(SaleStar), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(SaleStar), nameof(After)),
                    transpiler: useRetroactiveMacro ? null : new HarmonyMethod(typeof(SaleStar), nameof(ChestEligibilityIL)),
                    finalizer: new HarmonyMethod(typeof(SaleStar), nameof(Finish)), ilmanipulator: null);
                U.Patch(typeof(Inventory.ItemTransformation), "TryTransform", typeof(SaleStar), prefix: nameof(LimitConsumption));
                if (useRetroactiveMacro)
                {
                    U.Patch(typeof(PurchaseInteraction), "CanBeAffordedByInteractor", typeof(SaleStar), postfix: nameof(AfterAffordability));
                    ThreeQsVanillaTweaks.Log.Info("Sale Star: using the configured stage limit with RetroactiveMacro chest reopens.");
                }
            }

            if (useRetroactiveMacro)
                c.Text(MacroDescription, MacroPickupDescription);
            else
                c.Text(() => "The first " + c.V("Uses Per Stage", "", "cIsUtility") + " chests opened each stage drop " + c.V("Extra Items", "", "cIsUtility") + " additional items." + (c.F("Extra Item Chance (%)") > 0 ? " Each has a " + c.V("Extra Item Chance (%)", "%", "cIsUtility") + " chance to drop another item." : ""), () => "Gain <style=cIsUtility>extra items</style> from the first chests you open each stage.");
        }

        static string MacroPickupDescription() =>
            "<style=cIsUtility>Reopen a chest</style> for an extra item. Uses per stage: " +
            c.V("Uses Per Stage", "", "cIsUtility") + ".";

        static string MacroDescription()
        {
            string text = MacroPickupDescription() + " Each successful reopen spends <style=cIsUtility>one use</style>." +
                " <style=cStack>Each additional Sale Star increases the chance of more items by 5%.</style>";
            if (macroReopenFailure?.Value == true)
                text += " Reopening can fail. Failed attempts <style=cIsUtility>do not spend a use</style>.";
            if (macroExcludeSaleStarChests?.Value == true)
                text += " Chests that dropped a Sale Star are excluded.";
            return text;
        }

        static bool IsReopen(PurchaseInteraction purchase) => purchase.saleStarCompatible &&
            purchase.costType != CostTypeIndex.None &&
            CostTypeCatalog.GetCostTypeDef(purchase.costType)?.name == RetroactiveMacroChestCost;

        static void AfterAffordability(PurchaseInteraction __instance, Interactor activator, ref bool __result)
        {
            if (!__result || !NetworkServer.active || !c.Active || !IsReopen(__instance)) return;
            __result = TryGetPurchaseBudget(activator, out _, out var state, out int count) && state.Used < Uses(count);
        }

        static bool CanUseSaleStar(PurchaseInteraction purchase) => purchase.saleStarCompatible &&
            (!c.Active || purchase.GetComponent<ChestBehavior>() || purchase.GetComponent<RouletteChestController>());

        static IEnumerable<CodeInstruction> ChestEligibilityIL(IEnumerable<CodeInstruction> instructions)
        {
            var compatible = A.Field(typeof(PurchaseInteraction), nameof(PurchaseInteraction.saleStarCompatible));
            var check = A.Method(typeof(SaleStar), nameof(CanUseSaleStar));
            foreach (var instruction in instructions)
            {
                // Gate the native inventory transformation with the same check as the reward hook.
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, compatible))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = check;
                }
                yield return instruction;
            }
        }

        static bool Before(PurchaseInteraction __instance, Interactor activator, out Purchase __state)
        {
            __state = new Purchase { Previous = current };
            current = null;
            if (!c.Active || !NetworkServer.active || !__instance.available || !CanUseSaleStar(__instance))
                return true;
            bool reopen = useRetroactiveMacro && IsReopen(__instance);
            // RetroactiveMacro spends stars on reopens and retains the vanilla roulette purchase path.
            if (useRetroactiveMacro && !reopen && !__instance.GetComponent<RouletteChestController>())
                return true;
            if (!TryGetPurchaseBudget(activator, out var body, out var state, out int count))
                return !reopen;
            if (reopen && state.Used >= Uses(count))
                return false;

            __state.Body = body;
            __state.State = state;
            __state.Count = count;
            __state.Original = body.inventory.GetItemCountEffective(c.Item);
            current = __state;
            return true;
        }

        static int Uses(int count) => Mathf.Max(0, Mathf.FloorToInt(c.Total("Uses Per Stage", count)));

        static bool TryGetPurchaseBudget(Interactor activator, out CharacterBody body, out State state, out int count)
        {
            body = activator ? activator.GetComponent<CharacterBody>() : null;
            state = null;
            count = 0;
            if (!body || !body.master || !body.inventory) return false;
            count = body.inventory.GetItemCountEffective(c.Item) +
                body.inventory.GetItemCountEffective(DLC2Content.Items.LowerPricedChestsConsumed);
            if (count == 0) return false;
            state = states.GetOrCreateValue(body.master);
            // Hidden realms can restore Sale Star without increasing stageClearCount.
            // Compare stage instances so their uses reset just like normal stages.
            var stage = RoR2.Stage.instance;
            if (!ReferenceEquals(state.Stage, stage))
            {
                state.Stage = stage;
                state.Used = 0;
            }

            return true;
        }

        // Represent remaining uses as active stars. Defaults consume exactly one
        // per chest; custom use counts distribute consumption across those uses.
        internal static int RemainingStars(int count, int uses, int used)
        {
            if (count <= 0 || uses <= 0) return 0;
            int remaining = Math.Max(0, uses - used);
            return Math.Min(count, (int)Math.Ceiling((double)count * remaining / uses));
        }

        static bool LimitConsumption(ref Inventory.ItemTransformation __instance, Inventory inventory, ref bool __result)
        {
            var p = current;
            if (p == null || !p.Body || p.Body.inventory != inventory || !c.Active
                || __instance.originalItemIndex != c.Item.itemIndex
                || __instance.newItemIndex != DLC2Content.Items.LowerPricedChestsConsumed.itemIndex)
                return true;
            int uses = Uses(p.Count);
            if (useRetroactiveMacro && p.State.Used >= uses)
            {
                __result = false;
                return false;
            }
            int remaining = RemainingStars(p.Count, uses, p.State.Used + 1);
            int consume = Math.Max(1, p.Original - remaining);
            __instance.maxToTransform = Math.Min(__instance.maxToTransform, consume);
            return true;
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
            int uses = Uses(p.Count);
            bool eligible = p.State.Used < uses;
            if (!useRetroactiveMacro)
            {
                var chest = __instance.GetComponent<ChestBehavior>();
                var roulette = __instance.GetComponent<RouletteChestController>();
                int extra = eligible ? Mathf.Max(0, Mathf.FloorToInt(c.Total("Extra Items", p.Count))) : 0;
                if (eligible && U.Roll(c.Total("Extra Item Chance (%)", p.Count), p.Body))
                    extra++;
                if (chest)
                    chest.dropCount = 1 + extra;
                if (roulette)
                    roulette.dropCount = 1 + extra;
            }
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
