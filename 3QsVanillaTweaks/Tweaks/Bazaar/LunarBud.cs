using System;
using System.Collections;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    internal static class LunarBud
    {
        private static BazaarSpawnHelper.Settings settings, second, third;
        private static Stage lastStage;
        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 06 Lunar Pod", "06 Lunar Pod",
                "RoR2/Base/LunarChest/LunarChest.prefab", 80f, -12.1f, 17.2f, -59.9f, -0.89f, 0f, 0.45f,
                exactPose: "-70.30086, -24.17496, -27.5368366, 9.10516, 191.835388, 355.084778");
            second = BazaarSpawnHelper.BindFormerUnlockOnly("Lunar Pod 2", settings.Path, -71.66f, -24.59f, -41.30f, -.89f, 0f, .45f,
                exactPose: "-70.88565, -24.55618, -30.7341862, 2.13443414E-07, 331.822, 5");
            third = BazaarSpawnHelper.BindFormerUnlockOnly("Lunar Pod 3", settings.Path, -74.14f, -24.62f, -44.52f, -.89f, 0f, .45f,
                exactPose: "-73.26327, -25.3397751, -32.77632, 10.000001, 241.821991, 9.999999");
            BazaarSpawnHelper.Register(typeof(LunarBud), "LunarBud", nameof(AfterStageStart));
        }
        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || __instance == lastStage) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, () =>
            {
                Spawn(settings);
                Spawn(second);
                Spawn(third);
            }));
        }

        private static void Spawn(BazaarSpawnHelper.Settings settings)
        {
            if (!BazaarSpawnHelper.Roll(settings)) return;
            BazaarSpawnHelper.Spawn(settings, settings.Path, settings.Name, settings.Position, settings.Forward, instance =>
            {
                // Stage Lunar Pod: closed, unrevealed reward until purchased.
                // Unlike the Bazaar LunarShopTerminal prefab, LunarChest handles
                // its own random Lunar drop when opened.
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                if (purchase == null)
                    throw new InvalidOperationException("Lunar Pod prefab lacks PurchaseInteraction.");
                purchase.costType = CostTypeIndex.LunarCoin;
                purchase.cost = 1;
                purchase.onDetailedPurchaseServer ??= new DetailedPurchaseEvent();
                purchase.onDetailedPurchaseServer.AddListener((_, _) => Newt.CommentOnLunarPurchase(null));
            });
        }
    }
}
