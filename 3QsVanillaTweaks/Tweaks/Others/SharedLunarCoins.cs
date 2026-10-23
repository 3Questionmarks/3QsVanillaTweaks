using System;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Other
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
        private static MethodInfo awardMethod;
        private static bool sharing;
        private static bool warned;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Share Coin Pickups", true,
                "When a dropped Lunar Coin is collected, grant one Lunar Coin to each other connected player (including dead players). Does not share Lunar Coins from achievements or end-of-run rewards.");

            // Resolve the method dynamically because the game's public API has
            // changed across releases. Only patch the single-argument coin grant.
            foreach (MethodInfo method in typeof(NetworkUser).GetMethods(
                         BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (method.Name != "AwardLunarCoins") continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                Type amountType = parameters[0].ParameterType;
                if (amountType != typeof(uint) && amountType != typeof(int)) continue;
                awardMethod = method;
                break;
            }

            if (awardMethod == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Shared Lunar Coins: NetworkUser.AwardLunarCoins was not found; tweak disabled.");
                return;
            }

            try
            {
                new Harmony("3Questionmarks.3QsVanillaTweaks.SharedLunarCoins").Patch(
                    awardMethod,
                    postfix: new HarmonyMethod(typeof(SharedLunarCoins), nameof(AfterCoinAward)));
                ThreeQsVanillaTweaks.Log.Info("Shared Lunar Coins: coin pickup sharing initialized.");
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Shared Lunar Coins: could not install hook: " + e);
            }
        }

        private static void AfterCoinAward(NetworkUser __instance, object __0)
        {
            if (sharing || !NetworkServer.active || !ModConfig.Get(enabled) || __instance == null)
                return;

            // AwardLunarCoins is also used by other mechanics. Only share when
            // this award came from the physical LunarCoinPickup component.
            if (!AwardOriginatedFromPickup()) return;

            uint count;
            try { count = Convert.ToUInt32(__0); }
            catch { return; }
            if (count == 0) return;

            sharing = true;
            try
            {
                foreach (NetworkUser user in UnityEngine.Object.FindObjectsOfType<NetworkUser>())
                {
                    if (user == null || user == __instance) continue;
                    // Skip non-player network users, but include dead players.
                    object amount = awardMethod.GetParameters()[0].ParameterType == typeof(int)
                        ? (object)checked((int)count) : count;
                    awardMethod.Invoke(user, new[] { amount });
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
            finally { sharing = false; }
        }

        private static bool AwardOriginatedFromPickup()
        {
            StackFrame[] frames = new StackTrace(false).GetFrames();
            if (frames == null) return false;
            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                Type type = method != null ? method.DeclaringType : null;
                if (type != null && type.Name.IndexOf("LunarCoinPickup", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
