using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Others
{
    /// <summary>
    /// When a player collects a dropped Lunar Coin, grant the same coin amount
    /// to the other connected players. The collector receives their usual
    /// vanilla award, not a second award.
    /// </summary>
    internal static class SharedLunarCoins
    {
        private const string Section = "Other - Lunar Coins";
        private static ConfigEntry<bool> enabled;
        private static bool warned;
        private static int pickupDepth;

        internal static bool WorldCoinPickupInProgress => pickupDepth > 0;
        internal static bool SharesPickups => ModConfig.Get(enabled);

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Share Coin Pickups", true,
                "When a dropped Lunar Coin is collected, grant one Lunar Coin to each other connected player (including dead players). Does not share Lunar Coins from achievements or end-of-run rewards.");

            try
            {
                new Harmony("3Questionmarks.3QsVanillaTweaks.SharedLunarCoins").Patch(
                    AccessTools.Method(typeof(LunarCoinDef), nameof(LunarCoinDef.GrantPickup)),
                    prefix: new HarmonyMethod(typeof(SharedLunarCoins), nameof(BeginCoinAward)),
                    postfix: new HarmonyMethod(typeof(SharedLunarCoins), nameof(AfterCoinAward)),
                    finalizer: new HarmonyMethod(typeof(SharedLunarCoins), nameof(EndCoinAward)));
                ThreeQsVanillaTweaks.Log.Info("Shared Lunar Coins: coin pickup sharing initialized.");
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Shared Lunar Coins: could not install hook: " + e);
            }
        }

        private static void BeginCoinAward()
        {
            pickupDepth++;
        }

        private static Exception EndCoinAward(Exception __exception)
        {
            pickupDepth--;
            return __exception;
        }

        private static void AfterCoinAward(LunarCoinDef __instance, ref PickupDef.GrantContext __0)
        {
            if (!NetworkServer.active || !SharesPickups || Tweaks.RoRArtifacts.TemporaryLunarCoins.SharedWalletActive || __instance == null ||
                !__0.shouldDestroy || __0.body == null || __instance.coinValue == 0)
                return;

            // This runs only after the game's physical Lunar Coin grant succeeds.
            // The collector already received the coin in the original method.
            NetworkUser collector = Util.LookUpBodyNetworkUser(__0.body);
            if (collector == null) return;
            try
            {
                foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
                {
                    if (user == null || user == collector || !user.isParticipating) continue;
                    // A NetworkUser remains participating when its body is dead.
                    user.AwardLunarCoins(__instance.coinValue);
                }
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    ThreeQsVanillaTweaks.Log.Warning("Shared Lunar Coins: sharing failed: " + e);
                }
            }
        }
    }
}
