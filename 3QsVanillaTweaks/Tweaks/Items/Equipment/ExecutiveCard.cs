using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentMultiShopCard
    {
        private const string RetroactiveMacroGuid = "Onyx.RetroactiveMacro";
        private static EquipmentConfig config;
        private static bool retroactiveMacroLoaded;
        private static bool macroReopensShops;
        private static bool macroExcludesEquipmentShops;
        private static readonly HashSet<ShopRecord> shops = new HashSet<ShopRecord>();
        private static readonly FieldInfo terminalsField = AccessTools.Field(typeof(MultiShopController), "terminalGameObjects");
        private static readonly MethodInfo setShopAvailable = AccessTools.PropertySetter(typeof(MultiShopController), "Networkavailable");
        private static readonly FieldInfo terminalPurchasedField = AccessTools.Field(typeof(ShopTerminalBehavior), "hasBeenPurchased");
        private static readonly MethodInfo updateTerminalDisplay = AccessTools.Method(typeof(ShopTerminalBehavior), "UpdatePickupDisplayAndAnimations");

        internal static void Init() => RoR2Application.onLoad += Load;

        private static void Load()
        {
            EquipmentConfig.MarkLoaded();
            EquipmentDef def = EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex("MultiShopCard"));
            if (!def) return;
            retroactiveMacroLoaded = Chainloader.PluginInfos.TryGetValue(RetroactiveMacroGuid, out var plugin) &&
                plugin.Instance;
            if (retroactiveMacroLoaded)
            {
                macroReopensShops = plugin.Instance.Config.TryGetEntry<bool>("Executive Card", "Change Credit Card", out var changeCard) &&
                    changeCard.Value;
                macroExcludesEquipmentShops = plugin.Instance.Config.TryGetEntry<bool>("Executive Card", "Exclude Equipment Shops", out var excludeShops) &&
                    excludeShops.Value;
            }
            config = new EquipmentConfig("Executive Card", "MultiShopCard", true);
            config.Flag("Keep Multishops Open For Everyone", true,
                "While any player holds an Executive Card, unbought terminals stay open for everyone after a purchase.");
            config.Flag("Reopen Previously Closed Multishops", true,
                "When a player gains an Executive Card, reopen unbought terminals in multishops that were already closed. RetroactiveMacro handles reopening when its plugin is loaded.");
            config.N("Cashback (%)", 10f,
                "Percentage of the purchase cost refunded to the Executive Card holder. Vanilla: 10%.", 0f, 100f);
            config.Text(Description, PickupDescription);

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.ExecutiveCardMultishops");
            harmony.Patch(AccessTools.Method(typeof(MultiShopController), "Start"),
                postfix: new HarmonyMethod(typeof(VanillaEquipmentMultiShopCard), nameof(AfterShopStart)));
            harmony.Patch(AccessTools.Method(typeof(MultiShopController), "OnPurchase"),
                prefix: new HarmonyMethod(typeof(VanillaEquipmentMultiShopCard), nameof(BeforePurchase)),
                postfix: new HarmonyMethod(typeof(VanillaEquipmentMultiShopCard), nameof(AfterPurchase)));
            var cashbackPatch = new HarmonyMethod(typeof(VanillaEquipmentMultiShopCard), nameof(CashbackIL));
            harmony.Patch(AccessTools.Method(typeof(RoR2.Items.MultiShopCardUtils), "OnPurchase"),
                transpiler: cashbackPatch);
            MethodInfo extraSlotCard = AccessTools.Method(typeof(Tweaks.Items.Boss.FunctionalCoupler), "ExtraExecutiveCard");
            if (extraSlotCard != null) harmony.Patch(extraSlotCard, transpiler: cashbackPatch);
            if (retroactiveMacroLoaded)
            {
                ThreeQsVanillaTweaks.Log.Info("Executive Card: RetroactiveMacro handles previously closed multishops.");
                return;
            }
            var watcher = new GameObject("3Qs Executive Card Shop Watcher");
            UnityEngine.Object.DontDestroyOnLoad(watcher);
            watcher.AddComponent<ShopWatcher>();
        }

        private static bool KeepOpenEnabled => config != null && config.Active && config.B("Keep Multishops Open For Everyone");
        private static bool RetroactiveEnabled => !retroactiveMacroLoaded && config != null && config.Active &&
            config.B("Reopen Previously Closed Multishops");

        private static string CashbackText =>
            "<style=cIsUtility>" + config.F("Cashback (%)").ToString("0.##", CultureInfo.InvariantCulture) + "%</style>";

        private static string Description()
        {
            string text = "Whenever you make a gold purchase, get " + CashbackText + " of the spent gold back.";
            text += KeepOpenEnabled
                ? " While any player holds the card, purchases from <style=cIsUtility>multishop terminals</style> leave the other terminals <style=cIsUtility>open for everyone</style>."
                : " If you buy from a <style=cIsUtility>multishop terminal</style>, the other terminals <style=cIsUtility>remain open</style>.";

            if (macroReopensShops)
            {
                text += " While held, <style=cIsUtility>previously closed multishops reopen</style>";
                text += macroExcludesEquipmentShops ? ", except equipment multishops." : ".";
            }
            else if (RetroactiveEnabled)
                text += " When you gain this equipment, <style=cIsUtility>previously closed multishops reopen</style>.";

            return text;
        }

        private static string PickupDescription() =>
            "Gain " + CashbackText + " cashback on gold purchases. Multishops <style=cIsUtility>remain open</style>." +
            (macroReopensShops || RetroactiveEnabled ? " Reopen closed multishops." : "");

        private static float CashbackFraction() =>
            config != null && config.Active ? config.F("Cashback (%)") / 100f : 0.1f;

        private static IEnumerable<CodeInstruction> CashbackIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value &&
                    Math.Abs(value - 0.1f) < 0.00001f)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(VanillaEquipmentMultiShopCard), nameof(CashbackFraction));
                }
                yield return instruction;
            }
        }

        private static bool AnyPlayerHoldsCard()
        {
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                Inventory inventory = player && player.master ? player.master.inventory : null;
                if (!inventory || inventory.GetEquipmentDisabled()) continue;
                if (inventory.currentEquipmentIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex)
                    return true;
                for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
                    for (uint set = 0; set < inventory.accessibleEquipmentSetCount; set++)
                    {
                        var location = new EquipmentLocation { slot = slot, set = set };
                        if (inventory.GetEquipment(in location).equipmentIndex ==
                            DLC1Content.Equipment.MultiShopCard.equipmentIndex) return true;
                    }
            }
            return false;
        }

        private static void AfterShopStart(MultiShopController __instance)
        {
            if (!NetworkServer.active || __instance.isTripleDroneVendor) return;
            ShopRecord record = __instance.GetComponent<ShopRecord>();
            if (!record) record = __instance.gameObject.AddComponent<ShopRecord>();
            record.Capture(__instance, null);
        }

        private static void BeforePurchase(MultiShopController __instance, CostTypeDef.PayCostContext payCostContext)
        {
            if (!NetworkServer.active || (!KeepOpenEnabled && !RetroactiveEnabled) || __instance.isTripleDroneVendor) return;
            ShopRecord record = __instance.GetComponent<ShopRecord>();
            if (!record) record = __instance.gameObject.AddComponent<ShopRecord>();
            record.Capture(__instance, payCostContext.purchaseInteraction);
            if (KeepOpenEnabled && AnyPlayerHoldsCard())
                __instance.SetCloseOnTerminalPurchase(payCostContext.purchaseInteraction, false);
        }

        private static void AfterPurchase(MultiShopController __instance)
        {
            if (!NetworkServer.active || !KeepOpenEnabled || __instance.isTripleDroneVendor) return;
            ShopRecord record = __instance.GetComponent<ShopRecord>();
            if (!record) return;
            if (AnyPlayerHoldsCard()) record.Reopen();
        }

        private static void ReopenClosedShops()
        {
            if (!NetworkServer.active || !RetroactiveEnabled || shops.Count == 0) return;
            foreach (ShopRecord record in new List<ShopRecord>(shops))
                if (record) record.Reopen();
        }

        private sealed class ShopWatcher : MonoBehaviour
        {
            private float nextCheck;
            private bool previouslyHeldCard;
            private bool previouslyEnabled;

            private void Update()
            {
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + 0.5f;
                bool heldCard = NetworkServer.active && AnyPlayerHoldsCard();
                bool retroactive = RetroactiveEnabled;
                if (heldCard && retroactive && (!previouslyHeldCard || !previouslyEnabled))
                    ReopenClosedShops();
                previouslyHeldCard = heldCard;
                previouslyEnabled = retroactive;
            }
        }

        private sealed class ShopRecord : MonoBehaviour
        {
            private MultiShopController controller;
            private GameObject[] terminals;
            private UniquePickup[] contents;
            private bool[] hidden;
            private bool[] bought;

            private void OnEnable() => shops.Add(this);
            private void OnDestroy() => shops.Remove(this);

            internal void Capture(MultiShopController shop, PurchaseInteraction purchased)
            {
                controller = shop;
                GameObject[] current = terminalsField.GetValue(shop) as GameObject[];
                if (current == null) return;
                if (contents == null || contents.Length != current.Length)
                {
                    terminals = current;
                    contents = new UniquePickup[current.Length];
                    hidden = new bool[current.Length];
                    bought = new bool[current.Length];
                }
                for (int i = 0; i < current.Length; i++)
                {
                    GameObject terminal = current[i];
                    if (!terminal) continue;
                    PurchaseInteraction purchase = terminal.GetComponent<PurchaseInteraction>();
                    if (purchase == purchased) { bought[i] = true; continue; }
                    ShopTerminalBehavior shopTerminal = terminal.GetComponent<ShopTerminalBehavior>();
                    if (!bought[i] && shopTerminal)
                    {
                        contents[i] = shopTerminal.CurrentPickup();
                        hidden[i] = shopTerminal.pickupIndexIsHidden;
                    }
                }
            }

            internal void Reopen()
            {
                if (!controller || contents == null) return;
                bool anyAvailable = false;
                for (int i = 0; i < terminals.Length; i++)
                {
                    GameObject terminal = terminals[i];
                    if (!terminal || bought[i] || !contents[i].pickupIndex.isValid) continue;
                    PurchaseInteraction purchase = terminal.GetComponent<PurchaseInteraction>();
                    ShopTerminalBehavior shopTerminal = terminal.GetComponent<ShopTerminalBehavior>();
                    if (!purchase || !shopTerminal) continue;
                    if (terminalPurchasedField != null && (bool)terminalPurchasedField.GetValue(shopTerminal)) continue;
                    if (purchase.setUnavailableOnTeleporterActivated && TeleporterInteraction.instance &&
                        TeleporterInteraction.instance.activationState > TeleporterInteraction.ActivationState.Idle)
                        continue;
                    if (purchase.available) { anyAvailable = true; continue; }
                    shopTerminal.SetPickup(contents[i], hidden[i]);
                    purchase.SetAvailable(true);
                    updateTerminalDisplay?.Invoke(shopTerminal, null);
                    anyAvailable = true;
                }
                if (anyAvailable) setShopAvailable.Invoke(controller, new object[] { true });
            }
        }
    }
}
