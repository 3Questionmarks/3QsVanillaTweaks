using System;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>Server-authoritative Bazaar Lunar Bud and recycler changes.</summary>
    internal static class LunarShop
    {
        private const string Section = "Bazaar - 02 Lunar Shop";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> replenishPurchased;
        private static ConfigEntry<int> purchaseLimit;
        private static ConfigEntry<string> formula;
        private static ConfigEntry<bool> resetY;
        private static Stage currentStage;
        private static Run currentRun;
        private static int purchases;
        private static int y;
        private static int z;
        private static int nextCost = 1;
        private static bool warnedFormula;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true, "Enable Bazaar lunar shop tweaks.");
            replenishPurchased = ModConfig.BindBool(Section, "Refresh Purchased Lunar Slots", true,
                "Allow rerolls to replenish Lunar Buds that have already been purchased.");
            purchaseLimit = ModConfig.Bind(Section, "Maximum Lunar Purchases Before Disabling Rerolls", 5,
                "Successful Lunar Bud purchases per Bazaar visit. 0 disables rerolls entirely; -1 allows unlimited purchases before disabling rerolls.");
            formula = ModConfig.Bind(Section, "Reroll Price Formula", "x + y",
                "Formula for the NEXT recycler price, where x is the current price, y is the reroll number (starting at 1), and z is the reroll number within this Bazaar. Operators: +, -, *, /, parentheses. Example: x * 2 for vanilla.");
            resetY = ModConfig.BindBool(Section, "Reset Y Every Bazaar", true,
                "If enabled, y resets at every Bazaar. z always resets; x is the current reroll price and resets to 1 for each Bazaar.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.LunarShop");
            harmony.Patch(AccessTools.Method(typeof(Stage), "Start"),
                postfix: new HarmonyMethod(typeof(LunarShop), nameof(OnStageStart)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin", new[] { typeof(Interactor) }),
                prefix: new HarmonyMethod(typeof(LunarShop), nameof(BeforeInteraction)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "ScaleCost"),
                prefix: new HarmonyMethod(typeof(LunarShop), nameof(OnScaleCost)));
            harmony.Patch(AccessTools.Method(typeof(ShopTerminalBehavior), "GenerateNewPickupServer", new[] { typeof(bool) }),
                prefix: new HarmonyMethod(typeof(LunarShop), nameof(BeforeGeneratePickup)),
                postfix: new HarmonyMethod(typeof(LunarShop), nameof(AfterGeneratePickup)));
            harmony.Patch(AccessTools.Method(typeof(ShopTerminalBehavior), "DropPickup", new[] { typeof(bool) }),
                postfix: new HarmonyMethod(typeof(LunarShop), nameof(AfterDropPickup)));
            Log.Info("Bazaar lunar shop tweaks initialized.");
        }

        private static bool Active => NetworkServer.active && ModConfig.Get(enabled);
        private static bool IsRecycler(PurchaseInteraction purchase) =>
            purchase != null && purchase.gameObject.name.StartsWith("LunarRecycler", StringComparison.OrdinalIgnoreCase);
        private static bool IsBud(ShopTerminalBehavior terminal) =>
            terminal != null && terminal.gameObject.name.StartsWith("LunarShop", StringComparison.OrdinalIgnoreCase);

        private static void ResetForStage(Stage stage)
        {
            if (currentRun != Run.instance)
            {
                currentRun = Run.instance;
                y = 0;
            }
            if (currentStage == stage) return;
            currentStage = stage;
            purchases = 0;
            z = 0;
            nextCost = 1;
            if (ModConfig.Get(resetY)) y = 0;
        }
        private static void OnStageStart(Stage __instance)
        {
            if (!Active) return;
            ResetForStage(__instance);
            ApplyLimit();
        }
        private static void ApplyLimit()
        {
            if (!Active) return;
            int limit = ModConfig.Get(purchaseLimit);
            if (limit < 0 || (limit > 0 && purchases < limit)) return;
            foreach (PurchaseInteraction interaction in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
                if (IsRecycler(interaction)) interaction.SetAvailable(false);
        }
        private static bool BeforeInteraction(PurchaseInteraction __instance)
        {
            if (!Active || !IsRecycler(__instance)) return true;
            ResetForStage(Stage.instance);
            int limit = ModConfig.Get(purchaseLimit);
            if (limit < 0 || (limit > 0 && purchases < limit)) return true;
            __instance.SetAvailable(false);
            return false;
        }
        private static bool OnScaleCost(PurchaseInteraction __instance, float scalar)
        {
            if (!Active || !IsRecycler(__instance)) return true;
            ResetForStage(Stage.instance);
            // Vanilla calls ScaleCost after a successful recycler purchase. Override vanilla doubling.
            y++;
            z++;
            float result;
            try
            {
                result = ExpressionParser.Evaluate(ModConfig.Get(formula), nextCost, y, z);
                if (float.IsNaN(result) || float.IsInfinity(result)) throw new FormatException("Result is not finite.");
                nextCost = Mathf.Clamp(Mathf.RoundToInt(result), 0, 100000000);
            }
            catch (Exception ex)
            {
                if (!warnedFormula)
                {
                    warnedFormula = true;
                    Log.Warning("Invalid lunar reroll formula; using vanilla doubling: " + ex.Message);
                }
                nextCost = Math.Min(100000000, nextCost * 2);
            }
            __instance.Networkcost = nextCost;
            ApplyLimit();
            return false;
        }
        private static void BeforeGeneratePickup(ShopTerminalBehavior __instance)
        {
            if (!Active || !ModConfig.Get(replenishPurchased) || !IsBud(__instance)) return;
            __instance.NetworkhasBeenPurchased = false;
        }
        private static void AfterGeneratePickup(ShopTerminalBehavior __instance)
        {
            if (!Active || !ModConfig.Get(replenishPurchased) || !IsBud(__instance)) return;
            PurchaseInteraction purchase = __instance.GetComponent<PurchaseInteraction>();
            if (purchase != null) purchase.SetAvailable(true);
        }
        private static void AfterDropPickup(ShopTerminalBehavior __instance)
        {
            if (!Active || !IsBud(__instance)) return;
            ResetForStage(Stage.instance);
            purchases++;
            ApplyLimit();

            // A non-negative limit is active; -1 means unlimited and suppresses chat notices.
            int limit = ModConfig.Get(purchaseLimit);
            if (limit > 0)
            {
                int remaining = Math.Max(0, limit - purchases);
                string message = remaining == 0
                    ? "<style=cWorldEvent>[Lunar Shop] Rerolling is now disabled (0 Lunar purchases remaining).</style>"
                    : "<style=cWorldEvent>[Lunar Shop] " + remaining +
                      " Lunar purchase" + (remaining == 1 ? "" : "s") +
                      " remaining before rerolling is disabled.</style>";
                Chat.SendBroadcastChat(new Chat.SimpleChatMessage { baseToken = message });
            }
        }

        // Small arithmetic parser. Never compiles or executes arbitrary C# code from configuration.
        private sealed class ExpressionParser
        {
            private readonly string expression;
            private readonly float x, y, z;
            private int pos;
            private ExpressionParser(string expression, float x, float y, float z)
            { this.expression = expression ?? ""; this.x = x; this.y = y; this.z = z; }
            internal static float Evaluate(string expression, float x, float y, float z)
            {
                var parser = new ExpressionParser(expression, x, y, z);
                float value = parser.Sum();
                parser.Skip();
                if (parser.pos != parser.expression.Length) throw new FormatException("Unexpected text in formula.");
                return value;
            }
            private void Skip() { while (pos < expression.Length && char.IsWhiteSpace(expression[pos])) pos++; }
            private bool Consume(char c)
            {
                Skip();
                if (pos < expression.Length && expression[pos] == c) { pos++; return true; }
                return false;
            }
            private float Sum()
            {
                float value = Product();
                while (true)
                {
                    if (Consume('+')) value += Product();
                    else if (Consume('-')) value -= Product();
                    else return value;
                }
            }
            private float Product()
            {
                float value = Atom();
                while (true)
                {
                    if (Consume('*')) value *= Atom();
                    else if (Consume('/')) value /= Atom();
                    else return value;
                }
            }
            private float Atom()
            {
                if (Consume('+')) return Atom();
                if (Consume('-')) return -Atom();
                if (Consume('('))
                {
                    float value = Sum();
                    if (!Consume(')')) throw new FormatException("Missing closing parenthesis.");
                    return value;
                }
                Skip();
                if (pos >= expression.Length) throw new FormatException("Unexpected end of formula.");
                char symbol = char.ToLowerInvariant(expression[pos]);
                if (symbol == 'x' || symbol == 'y' || symbol == 'z')
                {
                    pos++;
                    return symbol == 'x' ? x : symbol == 'y' ? y : z;
                }
                int start = pos;
                while (pos < expression.Length && (char.IsDigit(expression[pos]) || expression[pos] == '.')) pos++;
                float number;
                if (start == pos || !float.TryParse(expression.Substring(start, pos - start), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out number)) throw new FormatException("Expected number or variable.");
                return number;
            }
        }
    }
}
