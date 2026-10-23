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
        private static BazaarSpawnHelper.Settings settings;
        private static Stage lastStage;
        internal static void Init()
        {
            settings = BazaarSpawnHelper.Bind("Bazaar - 05 Extra Interactables - 06 Lunar Bud", "06 Lunar Bud",
                "RoR2/Base/LunarChest/LunarChest.prefab", 80f, -12.1f, 17.2f, -59.9f, -0.89f, 0f, 0.45f);
            BazaarSpawnHelper.Register(typeof(LunarBud), "LunarBud", nameof(AfterStageStart));
        }
        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || __instance == lastStage) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, () =>
            {
                if (!BazaarSpawnHelper.Roll(settings)) return;
                BazaarSpawnHelper.Spawn(settings.Path, settings.Name, settings.Position, settings.Forward, instance =>
                {
                    // Stage Lunar Pod: closed, unrevealed reward until purchased.
                    // Unlike the Bazaar LunarShopTerminal prefab, LunarChest handles
                    // its own random Lunar drop when opened.
                    PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                    if (purchase == null)
                        throw new InvalidOperationException("Lunar Pod prefab lacks PurchaseInteraction.");
                    purchase.costType = CostTypeIndex.LunarCoin;
                    purchase.cost = 1;
                });
            }));
        }
    }
}
