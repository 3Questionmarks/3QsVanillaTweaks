using System;
using System.Collections.Generic;
using Path = System.IO.Path;
using BepInEx.Configuration;
using RoR2;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using Log = ThreeQsVanillaTweaks.Log;

namespace Tweaks.Bazaar
{
    internal static partial class BazaarAlternateMode
    {
        private enum ResetWhen { EveryVisit, EveryRun, EveryGameLaunch, EveryRunWin, PermanentlyUnlocked }
        private enum Recipients { Everyone, Host, Buyer }

        private static ConfigEntry<ResetWhen> resetWhen;
        private static ConfigEntry<Recipients> recipients;
        private static ConfigEntry<bool> launchCharacter, launchGameMode, permanentCharacter, permanentGameMode;
        private static ConfigFile progress;
        private static readonly Dictionary<string, HostProgress> hostProgress =
            new Dictionary<string, HostProgress>(StringComparer.Ordinal);
        private static readonly Dictionary<NetworkUserId, HashSet<string>> visitUnlocks =
            new Dictionary<NetworkUserId, HashSet<string>>();
        private static readonly Dictionary<NetworkUserId, HashSet<string>> runUnlocks =
            new Dictionary<NetworkUserId, HashSet<string>>();

        private const string HostRecipient = "host";

        private sealed class HostProgress
        {
            internal ConfigEntry<string> Saved;
            internal readonly Dictionary<string, HashSet<string>> Slots =
                new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        }

        private static NetworkUser Host => LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

        private static void BindProgression()
        {
            resetWhen = C.Bind(ModeSettings, "Reset Unlocks", ResetWhen.EveryRunWin,
                "Reset on each Bazaar visit, each run, each game launch, or a successful run ending. PermanentlyUnlocked keeps unlocks across launches.");
            recipients = C.Bind(ModeSettings, "Unlock Recipients", Recipients.Everyone,
                "Grant access to all players currently in the game, only the host, or only the buyer. Shared grants carry over with the host's saved unlocks.");
            string launch = ModeSettings + " - Every Game Launch";
            string permanent = ModeSettings + " - Permanently Unlocked";
            launchCharacter = BindCharacterScope(launch);
            launchGameMode = BindGameModeScope(launch);
            permanentCharacter = BindCharacterScope(permanent);
            permanentGameMode = BindGameModeScope(permanent);
            Run.onRunStartGlobal += _ => runUnlocks.Clear();
            Run.onRunDestroyGlobal += _ => runUnlocks.Clear();
            Run.onServerGameOver += OnServerGameOver;
        }

        private static ConfigEntry<bool> BindCharacterScope(string section) =>
            C.BindBool(section, "Character Specific", false, "Keep separate unlocks for each survivor selected by the host.");

        private static ConfigEntry<bool> BindGameModeScope(string section) =>
            C.BindBool(section, "Game Mode Specific", true, "Keep separate unlocks for modes such as Classic and Eclipse.");

        private static string Scope(ResetWhen reset, Run run)
        {
            var local = LocalUserManager.GetFirstLocalUser();
            var profile = local?.userProfile;
            if (profile == null || string.IsNullOrEmpty(profile.fileName)) return null;

            string survivor = "AnySurvivor", gameMode = "AnyGameMode";
            if ((reset == ResetWhen.EveryGameLaunch && C.Get(launchCharacter)) ||
                (reset == ResetWhen.PermanentlyUnlocked && C.Get(permanentCharacter)))
                survivor = Host?.GetSurvivorPreference()?.cachedName ?? "UnknownSurvivor";
            if ((reset == ResetWhen.EveryGameLaunch && C.Get(launchGameMode)) ||
                (reset == ResetWhen.PermanentlyUnlocked && C.Get(permanentGameMode)))
                gameMode = GameModeCatalog.GetGameModeName(run.gameModeIndex) ?? "UnknownGameMode";

            // Config definitions restrict punctuation. Each field is encoded independently.
            return Encode(reset.ToString()) + "." + Encode(profile.fileName) + "." + Encode(survivor) + "." + Encode(gameMode);
        }

        private static string Encode(string value) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool IsUnlocked(Slot slot, NetworkUser user)
        {
            if (!user) return false;
            var reset = C.Get(resetWhen);
            var grants = reset == ResetWhen.EveryVisit ? visitUnlocks : runUnlocks;
            if (grants.TryGetValue(user.id, out var unlocked) && unlocked.Contains(slot.ProgressKey)) return true;
            if (!Run.instance || reset == ResetWhen.EveryVisit || reset == ResetWhen.EveryRun) return false;

            var saved = GetHostProgress(reset, Run.instance);
            return saved != null && saved.Slots.TryGetValue(slot.ProgressKey, out var allowed) &&
                allowed.Contains(HostRecipient) && allowed.Contains(PlayerKey(user));
        }

        private static string PlayerKey(NetworkUser user) => user == Host ? HostRecipient :
            Encode(FormattableString.Invariant($"{user.id.value}:{user.id.strValue}:{user.id.subId}"));

        private static void GrantUnlock(Slot slot, NetworkUser buyer)
        {
            var granted = new List<NetworkUser>();
            switch (C.Get(recipients))
            {
                case Recipients.Everyone:
                    foreach (var user in NetworkUser.readOnlyInstancesList)
                        if (user && (user == Host || user.connectionToClient != null)) granted.Add(user);
                    break;
                case Recipients.Host:
                    if (Host) granted.Add(Host);
                    break;
                case Recipients.Buyer:
                    granted.Add(buyer);
                    break;
            }
            var reset = C.Get(resetWhen);
            var grants = reset == ResetWhen.EveryVisit ? visitUnlocks : runUnlocks;
            foreach (var user in granted) AddUnlock(grants, user.id, slot.ProgressKey);

            // Cross-run access is tied to a slot the host has unlocked.
            if (reset == ResetWhen.EveryVisit || reset == ResetWhen.EveryRun) return;
            var saved = GetHostProgress(reset, Run.instance);
            if (saved == null) return;
            bool grantsHost = Host && granted.Contains(Host);
            bool sharesExistingHostUnlock = C.Get(recipients) == Recipients.Everyone &&
                saved.Slots.TryGetValue(slot.ProgressKey, out var existing) && existing.Contains(HostRecipient);
            if (!grantsHost && !sharesExistingHostUnlock) return;
            foreach (var user in granted)
                if (user == Host || user.id.HasValidValue()) AddUnlock(saved.Slots, slot.ProgressKey, PlayerKey(user));
            SaveHostUnlocks(saved);
        }

        private static void AddUnlock<TKey>(Dictionary<TKey, HashSet<string>> grants, TKey key, string slot)
        {
            if (!grants.TryGetValue(key, out var unlocked))
            {
                unlocked = new HashSet<string>(StringComparer.Ordinal);
                grants.Add(key, unlocked);
            }
            unlocked.Add(slot);
        }

        private static HostProgress GetHostProgress(ResetWhen reset, Run run)
        {
            string scope = Scope(reset, run);
            if (scope == null) return null;
            if (hostProgress.TryGetValue(scope, out var saved)) return saved;
            saved = new HostProgress();
            if (reset != ResetWhen.EveryGameLaunch)
            {
                try { LoadHostUnlocks(scope, saved); }
                catch (Exception e) { Log.Warning("Could not load Bazaar unlock progress: " + e.Message); }
            }
            hostProgress.Add(scope, saved);
            return saved;
        }

        private static void LoadHostUnlocks(string scope, HostProgress saved)
        {
            if (progress == null)
            {
                string mainPath = C.File.ConfigFilePath;
                string path = Path.Combine(Path.GetDirectoryName(mainPath),
                    Path.GetFileNameWithoutExtension(mainPath) + ".BazaarProgress.cfg");
                progress = new ConfigFile(path, false) { SaveOnConfigSet = false };
            }
            saved.Saved = progress.Bind("Host Unlocks", scope, "", "Saved Bazaar unlocks for this host profile and scope.");
            foreach (string slot in saved.Saved.Value.Split(';'))
            {
                string[] fields = slot.Split('|');
                for (int i = 1; i < fields.Length; i++) AddUnlock(saved.Slots, fields[0], fields[i]);
            }
        }

        private static void SaveHostUnlocks(HostProgress saved)
        {
            if (saved.Saved == null) return;
            var slots = new List<string>();
            foreach (var entry in saved.Slots) slots.Add(entry.Key + "|" + string.Join("|", entry.Value));
            saved.Saved.Value = string.Join(";", slots);
            try { progress.Save(); }
            catch (Exception e) { Log.Warning("Could not save Bazaar unlock progress: " + e.Message); }
        }

        private static void OnServerGameOver(Run run, GameEndingDef ending)
        {
            if (!NetworkServer.active || !ending ||
                (!ending.isWin && ending.endingTextToken != "GAME_RESULT_UNKNOWN")) return;

            var saved = GetHostProgress(ResetWhen.EveryRunWin, run);
            if (saved != null && saved.Slots.Count != 0)
            {
                saved.Slots.Clear();
                SaveHostUnlocks(saved);
            }
            if (C.Get(resetWhen) == ResetWhen.EveryRunWin) runUnlocks.Clear();
        }
    }
}
