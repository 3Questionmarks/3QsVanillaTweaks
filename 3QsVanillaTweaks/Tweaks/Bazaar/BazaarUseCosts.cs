using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    internal static class BazaarUseCosts
    {
        internal sealed class SpawnedSlot : MonoBehaviour { }

        private const short FeeMessageId = 189;
        private static readonly Dictionary<PurchaseInteraction, int> serverFees = new Dictionary<PurchaseInteraction, int>();
        private static readonly Dictionary<NetworkInstanceId, int> clientFees = new Dictionary<NetworkInstanceId, int>();
        private static NetworkClient registeredClient;
        private static ConfigEntry<int> scrapperFee, exitFee, nullPortalFee, gongFee;

        internal static void Init()
        {
            exitFee = BindCoinPrice("Bazaar - Exit Portal", 0, "Lunar coins to use the Bazaar's Blue Portal. Vanilla: free.");
            nullPortalFee = BindCoinPrice("Bazaar - Null Portal", 0, "Lunar coins to use the portal to the Void Fields. Vanilla: free.");
            gongFee = BindCoinPrice("Bazaar - Ritual Gong", 0, "Lunar coins to use the Ritual Gong. Vanilla: free.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.UseCosts");
            var purchaseType = typeof(PurchaseInteraction);
            harmony.Patch(A.Method(purchaseType, "CanBeAffordedByInteractor"),
                postfix: new HarmonyMethod(typeof(BazaarUseCosts), nameof(AfterPurchaseAffordability)));
            harmony.Patch(A.Method(purchaseType, "GetInteractability"),
                postfix: new HarmonyMethod(typeof(BazaarUseCosts), nameof(AfterInteractability)));
            harmony.Patch(A.Method(purchaseType, "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(BazaarUseCosts), nameof(BeforePurchase)) { priority = Priority.VeryHigh });
            harmony.Patch(A.Method(purchaseType, "GetContextString"),
                postfix: new HarmonyMethod(typeof(BazaarUseCosts), nameof(AfterContext)) { priority = Priority.First });

            harmony.Patch(A.Method(purchaseType, "OnDestroy"),
                prefix: new HarmonyMethod(typeof(BazaarUseCosts), nameof(RemovePurchaseFee)));
            Run.onRunDestroyGlobal += _ =>
            {
                serverFees.Clear();
                clientFees.Clear();
            };
            RoR2Application.onUpdate += RegisterClient;
            NetworkUser.onNetworkUserLoadedSceneServer += SendFees;
            PatchFee(harmony, typeof(GenericInteraction), nameof(BeforePortalUse));
            PatchFee(harmony, typeof(TrialGongInteraction), nameof(BeforeGongUse));
            PatchFee(harmony, typeof(PickupPickerController), nameof(BeforeScrapperUse), nameof(AfterScrapperUse));
        }

        private static void PatchFee(Harmony harmony, Type type, string begin, string after = null)
        {
            var flags = typeof(BazaarUseCosts);
            string explicitPrefix = type == typeof(GenericInteraction) ? "RoR2.IInteractable." : "";
            harmony.Patch(A.Method(type, explicitPrefix + "GetInteractability"),
                postfix: new HarmonyMethod(flags, nameof(AfterInteractability)));
            harmony.Patch(A.Method(type, explicitPrefix + "GetContextString"),
                postfix: new HarmonyMethod(flags, nameof(AfterContext)) { priority = Priority.First });
            harmony.Patch(A.Method(type, explicitPrefix + "OnInteractionBegin"),
                prefix: new HarmonyMethod(flags, begin) { priority = Priority.Last },
                postfix: after == null ? null : new HarmonyMethod(flags, after));
        }

        internal static ConfigEntry<int> BindCoinPrice(string section, int cost, string description)
        {
            int previous = C.ReadRetiredValue(section, "Use Cost", cost);
            var entry = C.Bind(section, "Lunar Coin Use Cost", cost,
                new ConfigDescription(description, new AcceptableValueRange<int>(0, 999)));
            C.UseMigratedValue(entry, previous);
            return entry;
        }

        internal static ConfigEntry<int> BindSurcharge(string section, int cost = 0)
        {
            C.ReadRetiredValue(section, "Use Cost", -1);
            return C.Bind(section, "Lunar Coin Use Cost", cost,
                new ConfigDescription("Lunar coins charged in addition to the usual payment. Vanilla coin surcharge: 0.",
                    new AcceptableValueRange<int>(0, 999)));
        }

        internal static ConfigEntry<int> BindSpawn(string section, string name)
        {
            if (name.StartsWith("Lunar Pod", StringComparison.Ordinal))
                return BindCoinPrice(section, 1, "Lunar coins to open the pod. Vanilla: 1.");
            if (name.StartsWith("Lunar Seer", StringComparison.Ordinal))
                return BindCoinPrice(section, 1, "Lunar coins per use. Vanilla: 3.");
            if (name.StartsWith("Shrine of the Woods", StringComparison.Ordinal))
                return BindCoinPrice(section, 1, "Lunar coins per use in the Bazaar. Vanilla uses gold.");

            switch (name)
            {
                case "Cleansing Pool": return BindSurcharge(section, 1);
                case "Shrine of Order": return BindCoinPrice(section, 1, "Starting lunar-coin price. Later uses follow the shrine's price scaling. Vanilla: 1.");
                case "Scrapper":
                    scrapperFee = BindCoinPrice(section, 0, "Lunar coins to open the scrapping menu. Vanilla: free.");
                    return null;
                case "Wandering Chef": return null; // The meal is paid for when the order is accepted.
                default: return BindSurcharge(section);
            }
        }

        internal static void Apply(PurchaseInteraction purchase, ConfigEntry<int> entry)
        {
            if (!NetworkServer.active || !purchase || entry == null) return;
            int price = C.Get(entry);
            if (purchase.costType == CostTypeIndex.LunarCoin)
            {
                purchase.Networkcost = price;
                return;
            }

            if (price == 0 || serverFees.ContainsKey(purchase)) return;
            serverFees.Add(purchase, price);
#pragma warning disable CS0618
            if (purchase.onPurchase == null) purchase.onPurchase = new PurchaseEvent();
            purchase.onPurchase.AddListener(activator => Pay(price, activator));
#pragma warning restore CS0618
            SendFee(purchase, price);
        }

        private static void RemovePurchaseFee(PurchaseInteraction __instance)
        {
            if (NetworkServer.active) serverFees.Remove(__instance);
            else clientFees.Remove(__instance.netId);
        }

        private static int PurchaseFee(PurchaseInteraction purchase)
        {
            if (!BazaarSpawnHelper.InBazaar()) return 0;
            if (NetworkServer.active) return serverFees.TryGetValue(purchase, out int fee) ? fee : 0;
            return clientFees.TryGetValue(purchase.netId, out int amount) ? amount : 0;
        }

        private static bool BeforePurchase(PurchaseInteraction __instance, Interactor activator) =>
            !NetworkServer.active || CanPay(PurchaseFee(__instance), activator);

        private static void AfterPurchaseAffordability(PurchaseInteraction __instance, Interactor activator, ref bool __result)
        {
            if (__result) __result = CanPay(PurchaseFee(__instance), activator);
        }

        internal static bool TryPayPurchaseFee(PurchaseInteraction purchase, Interactor activator)
        {
            int price = PurchaseFee(purchase);
            if (!CanPay(price, activator)) return false;
            Pay(price, activator);
            return true;
        }

        internal static void SendFee(PurchaseInteraction purchase)
        {
            if (purchase && serverFees.TryGetValue(purchase, out int price)) SendFee(purchase, price);
        }

        private static void SendFee(PurchaseInteraction purchase, int price, NetworkConnection connection = null)
        {
            if (!purchase || purchase.netId.IsEmpty()) return;
            var message = new FeeMessage { Id = purchase.netId, Price = price };
            if (connection != null) connection.Send(FeeMessageId, message);
            else NetworkServer.SendToAll(FeeMessageId, message);
        }

        private static void SendFees(NetworkUser user)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || !user || user.connectionToClient == null) return;
            foreach (var entry in serverFees) SendFee(entry.Key, entry.Value, user.connectionToClient);
        }

        private static void RegisterClient()
        {
            var manager = NetworkManager.singleton;
            var client = manager ? manager.client : null;
            if (client == registeredClient) return;
            registeredClient = client;
            clientFees.Clear();
            client?.RegisterHandler(FeeMessageId, ReceiveFee);
        }

        private static void ReceiveFee(NetworkMessage message)
        {
            if (NetworkServer.active) return;
            var fee = message.ReadMessage<FeeMessage>();
            clientFees[fee.Id] = fee.Price;
        }

        private sealed class FeeMessage : MessageBase
        {
            internal NetworkInstanceId Id;
            internal int Price;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(Id);
                writer.Write(Price);
            }

            public override void Deserialize(NetworkReader reader)
            {
                Id = reader.ReadNetworkId();
                Price = reader.ReadInt32();
            }
        }

        private static int Fee(Component interaction)
        {
            if (!BazaarSpawnHelper.InBazaar()) return 0;
            if (interaction is PurchaseInteraction purchase) return PurchaseFee(purchase);
            if (interaction is GenericInteraction portal)
            {
                if (portal.contextToken == "PORTAL_SHOP_CONTEXT") return C.Get(exitFee);
                if (portal.contextToken == "PORTAL_ARENA_CONTEXT") return C.Get(nullPortalFee);
            }
            if (interaction is TrialGongInteraction) return C.Get(gongFee);
            if (interaction is PickupPickerController && interaction.GetComponent<ScrapperController>())
                return C.Get(scrapperFee);
            return 0;
        }

        private static NetworkUser Buyer(Interactor activator)
        {
            var body = activator ? activator.GetComponent<CharacterBody>() : null;
            return body && body.master ? body.master.playerCharacterMasterController?.networkUser : null;
        }

        internal static bool CanPay(int price, Interactor activator) =>
            price == 0 || (Buyer(activator) &&
                CostTypeCatalog.GetCostTypeDef(CostTypeIndex.LunarCoin).IsAffordable(price, activator));

        internal static void Pay(int price, Interactor activator)
        {
            if (price > 0 && Buyer(activator) is NetworkUser user && user) user.DeductLunarCoins((uint)price);
        }

        internal static string FormatFee(int price)
        {
            var text = new StringBuilder();
            CostTypeCatalog.GetCostTypeDef(CostTypeIndex.LunarCoin).BuildCostStringStyled(price, text, false, true);
            return " (" + text + ")";
        }

        private static void AfterInteractability(Component __instance, Interactor __0, ref Interactability __result)
        {
            if (__result == Interactability.Available && !CanPay(Fee(__instance), __0))
                __result = Interactability.ConditionsNotMet;
        }

        private static void AfterContext(Component __instance, ref string __result)
        {
            int price = Fee(__instance);
            if (price > 0) __result += FormatFee(price);
        }

        private static bool BeforePortalUse(GenericInteraction __instance, Interactor __0) => ChargeForInteraction(__instance, __0);

        private static bool BeforeGongUse(TrialGongInteraction __instance, Interactor __0) => ChargeForInteraction(__instance, __0);

        private static bool ChargeForInteraction(Component interaction, Interactor activator)
        {
            int price = Fee(interaction);
            if (!NetworkServer.active || price == 0) return true;
            if (((IInteractable)interaction).GetInteractability(activator) != Interactability.Available) return false;
            Pay(price, activator);
            return true;
        }

        private static bool BeforeScrapperUse(PickupPickerController __instance, Interactor __0, out int __state)
        {
            __state = 0;
            int price = Fee(__instance);
            if (!NetworkServer.active || price == 0) return true;
            var prompt = __instance.GetComponent<NetworkUIPromptController>();
            if (!prompt || prompt.inUse || __instance.GetInteractability(__0) != Interactability.Available) return false;
            __state = price;
            return true;
        }

        private static void AfterScrapperUse(PickupPickerController __instance, Interactor __0, int __state, bool __runOriginal)
        {
            if (!__runOriginal || __state == 0) return;
            var prompt = __instance.GetComponent<NetworkUIPromptController>();
            var buyer = Buyer(__0);
            if (prompt && buyer && prompt.currentParticipantMaster == buyer.master) Pay(__state, __0);
        }
    }
}
