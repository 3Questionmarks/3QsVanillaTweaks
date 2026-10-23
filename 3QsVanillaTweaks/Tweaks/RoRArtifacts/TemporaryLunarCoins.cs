using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.UI;
using ThreeQsVanillaTweaks;
using ModConfig = Config.ConfigManager;

namespace Tweaks.RoRArtifacts
{
    /// <summary>
    /// Artifact of the Fleeting Moon: a run-only Lunar Coin wallet.
    /// The saved user-profile wallet is never written by this module.
    /// Requires the R2API ContentAddition/ContentManagement API to register
    /// an ArtifactDef (see the project's package/reference notes).
    /// </summary>
    internal static class TemporaryLunarCoins
    {
        private const string Section = "Artifacts - Fleeting Moon";
        private static ConfigEntry<int> startingCoins;
        private static ConfigEntry<float> dropMultiplier;
        private static ConfigEntry<bool> earnPermanentCoins;
        private static ConfigEntry<int> permanentCoinInterval;
        private static ArtifactDef artifact;
        private static readonly Dictionary<NetworkUser, uint> savedBalances = new Dictionary<NetworkUser, uint>();
        private static readonly Dictionary<NetworkUser, uint> temporaryBalances = new Dictionary<NetworkUser, uint>();
        private static readonly Dictionary<NetworkUser, uint> pickupProgress = new Dictionary<NetworkUser, uint>();
        private static readonly Dictionary<NetworkUser, uint> pendingPermanentAwards = new Dictionary<NetworkUser, uint>();
        private static bool flushScheduled;
        private static MethodInfo awardMethod;
        private static bool grantingPermanent;
        private static PropertyInfo networkCoinsProperty;
        private static bool active;
        private static int activationAttempts;
        private static Run pendingRun;
        private static bool warned;
        private static readonly HashSet<int> boostedControllers = new HashSet<int>();

        internal static void Init()
        {
            startingCoins = ModConfig.File.Bind(Section, "Starting Lunar Coins", 5,
                "Temporary Lunar Coins each player receives at the beginning of a run with the Artifact enabled. Your saved coins are left untouched.");
            dropMultiplier = ModConfig.File.Bind(Section, "Lunar Coin Drop Rate Multiplier", 5f,
                "Multiplier to the configured base Lunar Coin drop chance (5 means five times normal). Successful-drop reduction is controlled separately in Other - Lunar Coins.");

            earnPermanentCoins = ModConfig.File.Bind(Section, "Earn Permanent Coins From Pickups", true,
                "While the Artifact is active, earn one permanent Lunar Coin for every N world Lunar Coins collected. With Shared Lunar Coins enabled, each player counts their own awarded coins.");
            permanentCoinInterval = ModConfig.File.Bind(Section, "Pickups Per Permanent Coin", 0,
                "World Lunar Coins needed to earn one permanent coin. 0 means round the drop-rate multiplier UP (5x = 5 coins). Any positive value overrides it.");

            artifact = ScriptableObject.CreateInstance<ArtifactDef>();
            artifact.cachedName = "3QS_ARTIFACT_FLEETING_MOON";
            artifact.nameToken = "3QS_ARTIFACT_FLEETING_MOON_NAME";
            artifact.descriptionToken = "3QS_ARTIFACT_FLEETING_MOON_DESC";
            artifact.smallIconSelectedSprite = CreateIcon(new Color(0.41f, 0.77f, 0.95f, 1f));
            artifact.smallIconDeselectedSprite = CreateIcon(new Color(0.28f, 0.31f, 0.40f, 1f));
            LanguageAPI.Add(artifact.nameToken, "Artifact of the Fleeting Moon");
            UpdateArtifactDescription();
            startingCoins.SettingChanged += (_, __) => UpdateArtifactDescription();
            dropMultiplier.SettingChanged += (_, __) => UpdateArtifactDescription();
            earnPermanentCoins.SettingChanged += (_, __) => UpdateArtifactDescription();
            permanentCoinInterval.SettingChanged += (_, __) => UpdateArtifactDescription();
            ContentAddition.AddArtifactDef(artifact);

            networkCoinsProperty = typeof(NetworkUser).GetProperty("NetworknetLunarCoins",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (networkCoinsProperty == null || !networkCoinsProperty.CanRead || !networkCoinsProperty.CanWrite)
            {
                Log.Warning("Fleeting Moon: NetworkUser.NetworknetLunarCoins is unavailable; currency override disabled.");
                return;
            }

            Harmony harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Artifact.FleetingMoon");
            awardMethod = PatchSingleAmountMethod(harmony, "AwardLunarCoins", nameof(InterceptAward));
            if (awardMethod != null)
                harmony.Patch(awardMethod, postfix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(AfterAward)));
            PatchSingleAmountMethod(harmony, "DeductLunarCoins", nameof(InterceptDeduction));

            MethodInfo sync = AccessTools.Method(typeof(NetworkUser), "SyncLunarCoinsToServer");
            if (sync != null)
                harmony.Patch(sync, prefix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(PreventRealWalletSync)));
            else
                Log.Warning("Fleeting Moon: SyncLunarCoinsToServer method not found.");

            MethodInfo controllerAwake = AccessTools.Method(typeof(PlayerCharacterMasterController), "Awake");
            if (controllerAwake != null)
                harmony.Patch(controllerAwake, postfix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(AdjustCoinDropChance)));
            else
                Log.Warning("Fleeting Moon: player controller Awake method not found; drop rate boost unavailable.");

            // Lunar purchases must check the run wallet, not the saved profile.
            // Patch both the prompt check and the server-side interaction itself,
            // so a stale prompt or manually triggered interaction cannot buy for free.
            MethodInfo affordability = AccessTools.Method(typeof(PurchaseInteraction),
                "CanBeAffordedByInteractor", new[] { typeof(Interactor) });
            if (affordability != null)
                harmony.Patch(affordability, prefix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(CheckLunarAffordability)));
            else
                Log.Warning("Fleeting Moon: could not patch lunar purchase affordability.");

            MethodInfo interact = AccessTools.Method(typeof(PurchaseInteraction),
                "OnInteractionBegin", new[] { typeof(Interactor) });
            if (interact != null)
                harmony.Patch(interact, prefix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(BlockUnaffordableLunarPurchase)));
            else
                Log.Warning("Fleeting Moon: could not patch lunar purchase transaction.");

            // Set the target value of the vanilla coin counter after each HUD update.
            // Do not modify child TMP labels: the coin icon is itself rendered by text.
            MethodInfo hudUpdate = AccessTools.Method(typeof(HUD), "Update");
            if (hudUpdate != null)
                harmony.Patch(hudUpdate, postfix: new HarmonyMethod(typeof(TemporaryLunarCoins), nameof(UpdateTemporaryCoinCounter)));
            else
                Log.Warning("Fleeting Moon: HUD.Update not found; temporary coin counter display unavailable.");

            Run.onRunStartGlobal += BeginRun;
            Run.onRunDestroyGlobal += EndRun;
            Log.Info("Artifact of the Fleeting Moon registered; run-start activation check installed.");
        }

        private static void UpdateArtifactDescription()
        {
            int initial = Math.Max(0, ModConfig.Get(startingCoins));
            float rate = Mathf.Max(0f, ModConfig.Get(dropMultiplier));
            string description = "Start every run with " + initial +
                " temporary Lunar Coins. Lunar Coin drop chance is multiplied by " +
                rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x.";
            if (ModConfig.Get(earnPermanentCoins))
                description += " Every " + GetPermanentThreshold() +
                    " Lunar Coins collected awards 1 permanent Lunar Coin.";
            description += " Victory and achievement Lunar Coins remain permanent. Your normal balance is restored after the run.";
            LanguageAPI.Add(artifact.descriptionToken, description);
        }

        private static MethodInfo PatchSingleAmountMethod(Harmony harmony, string name, string patchName)
        {
            foreach (MethodInfo method in typeof(NetworkUser).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name != name || parameters.Length != 1 ||
                    (parameters[0].ParameterType != typeof(uint) && parameters[0].ParameterType != typeof(int)))
                    continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(TemporaryLunarCoins), patchName));
                return method;
            }
            Log.Warning("Fleeting Moon: " + name + " was not found. That transaction won't be intercepted.");
            return null;
        }

        private static bool IsArtifactActive()
        {
            return RunArtifactManager.instance != null && artifact != null &&
                RunArtifactManager.instance.IsArtifactEnabled(artifact);
        }

        private static void BeginRun(Run run)
        {
            // onRunStartGlobal may fire before RunArtifactManager has finished
            // applying the rulebook. Recheck for a short time after run start.
            active = false;
            savedBalances.Clear();
            temporaryBalances.Clear();
            pickupProgress.Clear();
            pendingPermanentAwards.Clear();
            boostedControllers.Clear();
            flushScheduled = false;
            pendingRun = run;
            activationAttempts = 0;
            Log.Info("Fleeting Moon: run started; checking artifact activation.");
            RoR2Application.onNextUpdate += CheckRunActivation;
        }

        private static void CheckRunActivation()
        {
            if (pendingRun == null || Run.instance != pendingRun) return;

            if (!IsArtifactActive())
            {
                if (++activationAttempts < 120)
                    RoR2Application.onNextUpdate += CheckRunActivation;
                else
                {
                    Log.Info("Fleeting Moon: artifact is not enabled for this run (checked 120 updates).");
                    pendingRun = null;
                }
                return;
            }

            if (!NetworkServer.active)
            {
                if (++activationAttempts < 120)
                    RoR2Application.onNextUpdate += CheckRunActivation;
                else
                    Log.Warning("Fleeting Moon: artifact enabled, but server authority did not become available.");
                return;
            }

            pendingRun = null;
            active = true;
            uint initial = (uint)Math.Max(0, ModConfig.Get(startingCoins));
            int users = 0;
            foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
            {
                InitializeWallet(user);
                users++;
            }
            int controllerCount = 0;
            foreach (PlayerCharacterMasterController controller in UnityEngine.Object.FindObjectsOfType<PlayerCharacterMasterController>())
            {
                controllerCount++;
                AdjustCoinDropChance(controller);
            }
            Log.Info("Fleeting Moon: player controllers found at activation=" + controllerCount);

            Log.Info("Fleeting Moon: ACTIVATED. Starting temporary coins=" + initial +
                     ", users initialized=" + users +
                     ", drop multiplier=" + ModConfig.Get(dropMultiplier) + ".");
        }

        private static void InitializeWallet(NetworkUser user)
        {
            if (user == null || temporaryBalances.ContainsKey(user)) return;
            savedBalances[user] = ReadNetCoins(user);
            uint count = (uint)Math.Max(0, ModConfig.Get(startingCoins));
            temporaryBalances[user] = count;
            pickupProgress[user] = 0u;
            WriteNetCoins(user, count);
            Log.Info("Fleeting Moon: wallet initialized for " + user.name + ": previous network balance=" + savedBalances[user] + ", requested temporary=" + count + ", network readback=" + ReadNetCoins(user));
        }

        // Only world-coin pickup awards become temporary currency. All other
        // awards (including victory and achievements) go through vanilla, so
        // the player's actual saved profile receives them normally.
        private static bool InterceptAward(NetworkUser __instance, object __0)
        {
            if (!active || !NetworkServer.active || __instance == null || grantingPermanent)
                return true;

            InitializeWallet(__instance);
            uint amount;
            try { amount = Convert.ToUInt32(__0); }
            catch { return true; }

            bool worldPickup = IsWorldCoinAward();
            Log.Info("Fleeting Moon: AwardLunarCoins(" + amount + ") for " + __instance.name + ", classified as " + (worldPickup ? "world pickup" : "permanent reward") + ".");
            if (!worldPickup)
            {
                // Vanilla handles permanent grants. AfterAward restores the
                // temporary balance displayed to the player.
                savedBalances[__instance] = SaturatingAdd(savedBalances[__instance], amount);
                return true;
            }

            temporaryBalances[__instance] = SaturatingAdd(temporaryBalances[__instance], amount);
            WriteNetCoins(__instance, temporaryBalances[__instance]);
            Log.Info("Fleeting Moon: temporary award; network readback=" + ReadNetCoins(__instance));
            RecordPickupForPermanentRewards(__instance, amount);
            return false;
        }

        private static void AfterAward(NetworkUser __instance)
        {
            if (!active || !NetworkServer.active || __instance == null) return;
            uint count;
            if (temporaryBalances.TryGetValue(__instance, out count))
                WriteNetCoins(__instance, count);
        }

        private static uint SaturatingAdd(uint value, uint amount)
        {
            return amount > uint.MaxValue - value ? uint.MaxValue : value + amount;
        }

        private static bool IsWorldCoinAward()
        {
            // The shared-coin mod calls AwardLunarCoins from its postfix.
            // Those grants represent the same physical pickup, and should
            // contribute independently to each player's conversion progress.
            StackFrame[] frames = new StackTrace(false).GetFrames();
            if (frames == null) return false;
            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                Type owner = method != null ? method.DeclaringType : null;
                if (owner == null) continue;
                if (owner.Name.IndexOf("LunarCoinPickup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (owner.Name == "GenericPickupController" && method.Name.IndexOf("Grant", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
                if (owner.Name == "SharedLunarCoins" && method.Name == "AfterCoinAward")
                    return true;
            }
            return false;
        }

        private static int GetPermanentThreshold()
        {
            int overrideValue = ModConfig.Get(permanentCoinInterval);
            if (overrideValue > 0) return overrideValue;
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0f, ModConfig.Get(dropMultiplier))));
        }

        private static void RecordPickupForPermanentRewards(NetworkUser user, uint amount)
        {
            if (!ModConfig.Get(earnPermanentCoins) || amount == 0 || awardMethod == null) return;
            uint previous;
            pickupProgress.TryGetValue(user, out previous);
            ulong accumulated = (ulong)previous + amount;
            uint threshold = (uint)GetPermanentThreshold();
            uint earned = (uint)Math.Min((ulong)uint.MaxValue, accumulated / threshold);
            pickupProgress[user] = (uint)(accumulated % threshold);
            if (earned == 0) return;

            // Defer permanent awards until the next update, outside the
            // LunarCoinPickup call stack. Otherwise SharedLunarCoins could
            // mistake this permanent award for another world pickup.
            uint pending;
            pendingPermanentAwards.TryGetValue(user, out pending);
            pendingPermanentAwards[user] = SaturatingAdd(pending, earned);
            if (!flushScheduled)
            {
                flushScheduled = true;
                RoR2Application.onNextUpdate += FlushPermanentAwards;
            }
        }

        private static void FlushPermanentAwards()
        {
            flushScheduled = false;
            if (!active || !NetworkServer.active)
            {
                pendingPermanentAwards.Clear();
                return;
            }
            var awards = new Dictionary<NetworkUser, uint>(pendingPermanentAwards);
            pendingPermanentAwards.Clear();
            foreach (var award in awards)
            {
                NetworkUser user = award.Key;
                if (user == null || award.Value == 0) continue;
                grantingPermanent = true;
                try
                {
                    object argument = awardMethod.GetParameters()[0].ParameterType == typeof(int)
                        ? (object)checked((int)award.Value) : award.Value;
                    awardMethod.Invoke(user, new[] { argument });
                    uint currentSaved;
                    if (savedBalances.TryGetValue(user, out currentSaved))
                        savedBalances[user] = SaturatingAdd(currentSaved, award.Value);
                }
                catch (Exception e)
                {
                    Log.Warning("Fleeting Moon: permanent pickup reward could not be awarded: " + e);
                }
                finally
                {
                    grantingPermanent = false;
                    uint temporary;
                    if (temporaryBalances.TryGetValue(user, out temporary)) WriteNetCoins(user, temporary);
                }
            }
        }

        // IsArtifactActive works on both clients and the host. The local client
        // does not own the host's temporaryBalances dictionary, so its displayed
        // affordability is read from the server-synchronized NetworkUser balance.
        private static bool CanAffordTemporaryLunars(Interactor activator, int cost)
        {
            if (cost <= 0) return true;
            if (activator == null) return false;
            NetworkUser user = Util.LookUpBodyNetworkUser(activator.gameObject);
            if (user == null) return false;
            uint balance;
            if (!temporaryBalances.TryGetValue(user, out balance))
                balance = ReadNetCoins(user);
            return balance >= (uint)cost;
        }

        private static bool CheckLunarAffordability(PurchaseInteraction __instance,
            Interactor activator, ref bool __result)
        {
            if (!IsArtifactActive() || __instance == null ||
                __instance.costType != CostTypeIndex.LunarCoin)
                return true;
            __result = CanAffordTemporaryLunars(activator, __instance.cost);
            return false;
        }

        private static bool BlockUnaffordableLunarPurchase(PurchaseInteraction __instance,
            Interactor activator)
        {
            if (!IsArtifactActive() || __instance == null ||
                __instance.costType != CostTypeIndex.LunarCoin)
                return true;
            if (CanAffordTemporaryLunars(activator, __instance.cost)) return true;
            if (NetworkServer.active)
                Log.Info("Fleeting Moon: blocked unaffordable Lunar Coin purchase (cost=" +
                         __instance.cost + ").");
            return false;
        }

        private static bool InterceptDeduction(NetworkUser __instance, object __0)
        {
            if (!active || !NetworkServer.active || __instance == null) return true;
            InitializeWallet(__instance);
            uint amount = Convert.ToUInt32(__0);
            uint old = temporaryBalances[__instance];
            if (amount > old)
            {
                Log.Warning("Fleeting Moon: rejected Lunar Coin deduction of " + amount +
                            " because only " + old + " temporary coins remain.");
                return false;
            }
            uint value = amount >= old ? 0u : old - amount;
            temporaryBalances[__instance] = value;
            WriteNetCoins(__instance, value);
            Log.Info("Fleeting Moon: temporary deduction " + amount + "; network readback=" + ReadNetCoins(__instance));
            return false; // Skip vanilla profile-changing deduction.
        }

        private static bool PreventRealWalletSync()
        {
            // The client must not overwrite the temporary network wallet with
            // the permanent user-profile wallet during an active artifact run.
            return !IsArtifactActive();
        }

        private static void AdjustCoinDropChance(PlayerCharacterMasterController __instance)
        {
            if (!active || __instance == null) return;
            if (boostedControllers.Contains(__instance.GetInstanceID())) return;
            FieldInfo multiplierField = AccessTools.Field(typeof(PlayerCharacterMasterController), "lunarCoinChanceMultiplier");
            if (multiplierField == null)
            {
                if (!warned) { warned = true; Log.Warning("Fleeting Moon: lunarCoinChanceMultiplier field missing."); }
                return;
            }
            float rate = Mathf.Max(0f, ModConfig.Get(dropMultiplier));
            // Only modify the per-controller base once; the game's usual chance
            // reduction after a successful coin drop continues to work.
            float oldValue = Convert.ToSingle(multiplierField.GetValue(__instance));
            multiplierField.SetValue(__instance, oldValue * rate);
            Log.Info("Fleeting Moon: drop multiplier adjusted on controller " + __instance.GetInstanceID() + " from " + oldValue + " to " + Convert.ToSingle(multiplierField.GetValue(__instance)));
            boostedControllers.Add(__instance.GetInstanceID());
        }

        // Vanilla HUD.Update sets lunarCoinText.targetValue from the saved profile.
        // Override that numeric target after the vanilla update. Never change any
        // TMP child text: the Lunar Coin icon uses text markup of its own.
        private static void UpdateTemporaryCoinCounter(HUD __instance)
        {
            if (__instance == null || !IsArtifactActive() || __instance.lunarCoinText == null)
                return;

            NetworkUser local = null;
            foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
            {
                if (user != null && user.localUser != null)
                {
                    local = user;
                    break;
                }
            }
            if (local == null) return;

            uint displayed;
            if (!temporaryBalances.TryGetValue(local, out displayed))
                displayed = ReadNetCoins(local); // Remote client receives server-synced amount.

            __instance.lunarCoinText.targetValue = displayed > int.MaxValue
                ? int.MaxValue : (int)displayed;
        }

        private static void EndRun(Run run)
        {
            if (NetworkServer.active)
            {
                foreach (var saved in savedBalances)
                    if (saved.Key != null) WriteNetCoins(saved.Key, saved.Value);
            }
            active = false;
            pendingRun = null;
            boostedControllers.Clear();
            savedBalances.Clear();
            temporaryBalances.Clear();
            pickupProgress.Clear();
            pendingPermanentAwards.Clear();
            flushScheduled = false;
        }

        private static uint ReadNetCoins(NetworkUser user)
        {
            try { return Convert.ToUInt32(networkCoinsProperty.GetValue(user, null)); }
            catch { return 0u; }
        }

        private static void WriteNetCoins(NetworkUser user, uint amount)
        {
            try { networkCoinsProperty.SetValue(user, amount, null); }
            catch (Exception e)
            {
                if (!warned) { warned = true; Log.Warning("Fleeting Moon: unable to set temporary coin balance: " + e); }
            }
        }

        // Small built-in icon so the artifact is selectable without an asset bundle.
        private static Sprite CreateIcon(Color color)
        {
            Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    float dx = x - 15.5f, dy = y - 15.5f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    texture.SetPixel(x, y, distance < 12f && distance > 8f ? color : Color.clear);
                }
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f));
        }
    }
}
