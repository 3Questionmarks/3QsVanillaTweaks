using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace Config
{
    internal static class ConfigManager
    {
        private const short SnapshotMessage = 181;
        private const short RequestMessage = 182;
        // UNet strings are limited to 64 KiB. The full item/survivor config is much larger.
        private const int SnapshotChunkSize = 8192;
        private static int nextSnapshotId;
        private static int receivingSnapshotId = -1;
        private static int lastAppliedSnapshotId = -1;
        private static string[] receivingChunks;
        private static int receivedChunks;
        internal static ConfigFile File { get; private set; }
        internal static ConfigFile DebugFile { get; private set; }
        internal static ConfigFile SurvivorStatsFile { get; private set; }
        private static ConfigEntry<bool> useDebugOptions;
        private static ConfigEntry<bool> regenerateConfig;
        private static bool regenerateRequested, bindingsReady, initializationFinished;
        private static bool regenerationFailed;
        private static readonly Dictionary<ConfigFile, bool> originalSaveOnSet = new Dictionary<ConfigFile, bool>();
        private static readonly HashSet<(ConfigFile File, ConfigDefinition Definition)> retiredSettings =
            new HashSet<(ConfigFile, ConfigDefinition)>();
        private static readonly Dictionary<ConfigFile, HashSet<ConfigDefinition>> originalDefinitions = new Dictionary<ConfigFile, HashSet<ConfigDefinition>>();
        private static readonly HashSet<ConfigFile> dirtyFiles = new HashSet<ConfigFile>();
        private static readonly HashSet<ConfigFile> backedUpFiles = new HashSet<ConfigFile>();
        internal static bool DebugOptionsEnabled => useDebugOptions != null && EffectiveValue(useDebugOptions);
        private static readonly Dictionary<string, ConfigEntryBase> entries = new Dictionary<string, ConfigEntryBase>();
        private static readonly HashSet<string> localOnly = new HashSet<string>();
        private static readonly Dictionary<string, string> hostValues = new Dictionary<string, string>();
        private static NetworkClient currentClient;
        private static bool serverRegistered, received, wasClient;
        private static float nextRequest;
        internal static event Action EffectiveSettingsChanged;
        private static string Id(ConfigDefinition d) { return d.Section + "\n" + d.Key; }
        private static readonly string[] ModuleNames = { "Items", "Stages", "Directors", "Enemies", "Interactables", "Bazaar", "Artifacts", "Others", "RemoteOperation", "ModCompatibility", "Survivors" };
        private static readonly Dictionary<string, ConfigFile> moduleFiles = new Dictionary<string, ConfigFile>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ConfigEntry<bool>> moduleSwitches = new Dictionary<string, ConfigEntry<bool>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> startupModules = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static string warnedModuleMismatch;
        private static readonly Dictionary<string, bool> startupSwitches = new Dictionary<string, bool>();
        internal static ConfigEntry<bool> BindStartupBool(string section, string key, bool value, string description)
        {
            ConfigEntry<bool> entry = BindBool(section, key, value, description);
            startupSwitches[Id(entry.Definition)] = entry.Value;
            return entry;
        }
        private static ConfigFile legacyShrinesFile;

        internal static void Init(ConfigFile file)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            TrackFile(File);
            string directory = Path.GetDirectoryName(file.ConfigFilePath);
            string basename = Path.GetFileNameWithoutExtension(file.ConfigFilePath);
            DebugFile = OpenFile(Path.Combine(directory, basename + ".Debug.cfg"));
            SurvivorStatsFile = OpenFile(Path.Combine(directory, basename + ".Survivor-Stats.cfg"));
            foreach (string module in ModuleNames)
                moduleFiles.Add(module, OpenFile(Path.Combine(directory, basename + "." + module + ".cfg")));
            new HarmonyLib.Harmony("3Questionmarks.3QsVanillaTweaks.ItemConfigNotes").Patch(
                typeof(ConfigFile).GetMethod("Save"),
                postfix: new HarmonyLib.HarmonyMethod(typeof(ConfigManager), nameof(AfterConfigSave)));
            regenerateConfig = BindBool("Debug - Config", "Regenerate Config On Next Launch", false,
                "Refresh config files and remove obsolete options on the next launch, keeping your settings. Turns itself off afterwards.", clientSide: true);
            regenerateRequested = regenerateConfig.Value;
            if (regenerateRequested)
            {
                try
                {
                    string legacyShrinesPath = Path.Combine(directory, basename + ".Shrines.cfg");
                    if (System.IO.File.Exists(legacyShrinesPath)) legacyShrinesFile = OpenFile(legacyShrinesPath);
                    foreach (ConfigFile settings in originalSaveOnSet.Keys)
                        if (System.IO.File.Exists(settings.ConfigFilePath))
                        {
                            System.IO.File.Copy(settings.ConfigFilePath, settings.ConfigFilePath + ".before-regeneration.bak", true);
                            backedUpFiles.Add(settings);
                        }
                    MigrateSettings();
                }
                catch (Exception error)
                {
                    regenerationFailed = true;
                    ThreeQsVanillaTweaks.Log.Warning("Could not prepare config regeneration: " + error.Message);
                }
            }
            MigrateCommonCauldronSettings();
            MigrateRemoteOperationSettings();
            if (SurvivorConfigSections.Migrate(moduleFiles["Survivors"])) dirtyFiles.Add(moduleFiles["Survivors"]);
            useDebugOptions = Register(File.Bind("00 General", "Use Debug Options", false,
                "Enable the options in the debug config file."));
            AddModule("Survivors", "Survivors", "Enable all survivor changes. Restart to apply.");
            AddModule("Items", "Items", "Enable all item changes. Restart to apply.");
            AddModule("Stages", "Stages", "Enable all stage changes. Restart to apply.");
            AddModule("Directors", "Directors", "Enable director and interactable spawning changes. Restart to apply.");
            AddModule("Enemies", "Enemies", "Enable enemy changes. Restart to apply.");
            AddModule("Interactables", "Interactables", "Enable all interactable changes. Restart to apply.");
            AddModule("Bazaar", "Bazaar", "Enable all Bazaar changes. Restart to apply.");
            AddModule("Artifacts", "Artifacts", "Enable artifact changes and added artifacts. Restart to apply.");
            AddModule("Others", "Others", "Enable miscellaneous changes. Restart to apply.");
            AddModule("RemoteOperation", "Remote Operation Drones", "Enable player-controlled drone mechanics, skills and stat changes. Restart to apply.");
            AddModule("ModCompatibility", "Mod Compatibility", "Enable integrations with other mods. Restart to apply.");
            // Survivor settings are bound by their onLoad callbacks. Tick runs after
            // the complete event invocation, so these bindings are included too.
            RoR2.RoR2Application.onLoad += () => bindingsReady = true;
        }

        private const string ItemBalanceNotes = "# Item balance notes\n" +
            "# Most item changes aim to improve multiplayer effectiveness or reduce potential lag,\n" +
            "# and to reduce late-game proc chains that can clear the entire map from a single hit.\n" +
            "# Inspired by Risky Mod. Many damaging items therefore have their proc coefficient\n" +
            "# removed, with other values increased to compensate.\n\n";
        private static void AfterConfigSave(ConfigFile __instance)
        {
            if (!moduleFiles.TryGetValue("Items", out var items) || !ReferenceEquals(items, __instance)) return;
            try
            {
                string text = System.IO.File.ReadAllText(items.ConfigFilePath);
                if (!text.StartsWith("# Item balance notes", StringComparison.Ordinal))
                    System.IO.File.WriteAllText(items.ConfigFilePath, ItemBalanceNotes + text, new UTF8Encoding(false));
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Could not write item config notes: " + error.Message); }
        }

        private static ConfigFile OpenFile(string path)
        {
            var file = new ConfigFile(path, false);
            TrackFile(file);
            return file;
        }

        private static void TrackFile(ConfigFile file)
        {
            originalSaveOnSet.Add(file, file.SaveOnConfigSet);
            file.SaveOnConfigSet = false;
            originalDefinitions.Add(file, new HashSet<ConfigDefinition>(OrphanedSettings(file).Keys.Concat(file.Keys)));
            if (!System.IO.File.Exists(file.ConfigFilePath)) dirtyFiles.Add(file);
        }

        private static bool FileEnabled(ConfigFile file)
        {
            foreach (var module in moduleFiles)
                if (ReferenceEquals(module.Value, file)) return startupModules[module.Key];
            return !ReferenceEquals(file, SurvivorStatsFile) || startupModules["Survivors"];
        }

        private static bool PreserveUnbound(ConfigDefinition definition)
        {
            // Disabled startup switches intentionally leave their settings unbound.
            // Keep these settings, including skills unavailable in the current game.
            string closest = null;
            foreach (var toggle in startupSwitches)
            {
                string section = toggle.Key.Substring(0, toggle.Key.IndexOf('\n'));
                if (definition.Section != section && !definition.Section.StartsWith(section + " -", StringComparison.Ordinal)) continue;
                if (!toggle.Value) return true;
                if (closest == null || section.Length > closest.Length) closest = section;
            }
            return closest != null && !entries.Values.Any(e =>
                (e.Definition.Section == closest || e.Definition.Section.StartsWith(closest + " -", StringComparison.Ordinal)) &&
                !startupSwitches.ContainsKey(Id(e.Definition)));
        }

        private static void CompleteInitialization()
        {
            initializationFinished = true;
            var attemptedWrites = new List<ConfigFile>();
            try
            {
                if (regenerationFailed) throw new InvalidOperationException("Config regeneration could not be completed; it remains queued for the next launch.");
                if (regenerateRequested)
                {
                    foreach (ConfigFile file in originalSaveOnSet.Keys)
                    {
                        if (!FileEnabled(file)) continue;
                        var orphaned = OrphanedSettings(file);
                        foreach (ConfigDefinition definition in orphaned.Keys.ToArray())
                            if (!PreserveUnbound(definition)) orphaned.Remove(definition);
                        dirtyFiles.Add(file);
                    }
                }
                foreach (var retired in retiredSettings)
                    if (OrphanedSettings(retired.File).Remove(retired.Definition)) dirtyFiles.Add(retired.File);
                retiredSettings.Clear();
                // Write the reset flag last. A failed refresh stays queued for retry.
                foreach (ConfigFile file in dirtyFiles.Where(f => !ReferenceEquals(f, DebugFile)))
                { attemptedWrites.Add(file); file.Save(); }
                if (regenerateRequested) regenerateConfig.Value = false;
                if (regenerateRequested || dirtyFiles.Contains(DebugFile))
                { attemptedWrites.Add(DebugFile); DebugFile.Save(); }
                if (regenerateRequested) ThreeQsVanillaTweaks.Log.Info("Config files refreshed; one-use regeneration disabled.");
            }
            catch (Exception error)
            {
                if (regenerateRequested) regenerateConfig.Value = true;
                ThreeQsVanillaTweaks.Log.Warning("Could not finish saving config files: " + error.Message);
                if (regenerateRequested)
                    foreach (ConfigFile file in attemptedWrites)
                        try
                        {
                            if (backedUpFiles.Contains(file)) System.IO.File.Copy(file.ConfigFilePath + ".before-regeneration.bak", file.ConfigFilePath, true);
                            else if (System.IO.File.Exists(file.ConfigFilePath)) System.IO.File.Delete(file.ConfigFilePath);
                        }
                        catch (Exception restoreError)
                        { ThreeQsVanillaTweaks.Log.Warning("Could not restore config backup for " + file.ConfigFilePath + ": " + restoreError.Message); }
            }
            finally
            {
                foreach (var file in originalSaveOnSet) file.Key.SaveOnConfigSet = file.Value;
                dirtyFiles.Clear();
                originalSaveOnSet.Clear();
                originalDefinitions.Clear();
                backedUpFiles.Clear();
            }
        }

        private static void AddModule(string module, string label, string description)
        {
            ConfigEntry<bool> entry = Register(File.Bind("01 Modules", label, true, description));
            moduleSwitches.Add(module, entry);
            startupModules.Add(module, entry.Value);
        }

        internal static bool ModuleEnabled(Type type)
        {
            string ns = type?.Namespace ?? "";
            string module = null;
            if (ns == "Tweaks.Items" || ns.StartsWith("Tweaks.Items.", StringComparison.Ordinal)) module = "Items";
            else if (ns == "Tweaks.Survivors" || ns.StartsWith("Tweaks.Survivors.", StringComparison.Ordinal)) module = "Survivors";
            else if (ns == "Tweaks.Stages" || ns.StartsWith("Tweaks.Stages.", StringComparison.Ordinal)) module = "Stages";
            else if (ns == "Tweaks.Directors" || ns.StartsWith("Tweaks.Directors.", StringComparison.Ordinal)) module = "Directors";
            else if (ns == "Tweaks.Enemies" || ns.StartsWith("Tweaks.Enemies.", StringComparison.Ordinal)) module = "Enemies";
            else if (ns == "Tweaks.Interactables" || ns.StartsWith("Tweaks.Interactables.", StringComparison.Ordinal)) module = "Interactables";
            else if (ns == "Tweaks.Bazaar" || ns.StartsWith("Tweaks.Bazaar.", StringComparison.Ordinal)) module = "Bazaar";
            else if (ns == "Tweaks.RoRArtifacts" || ns.StartsWith("Tweaks.RoRArtifacts.", StringComparison.Ordinal)) module = "Artifacts";
            else if (ns == "Tweaks.ModCompatibility" || ns.StartsWith("Tweaks.ModCompatibility.", StringComparison.Ordinal)) module = "ModCompatibility";
            else if (ns == "Tweaks.Others.RemoteOperation" || ns.StartsWith("Tweaks.Others.RemoteOperation.", StringComparison.Ordinal) ||
                     type.FullName == "Tweaks.Others.MultiplayerDrones") module = "RemoteOperation";
            else if (ns == "Tweaks.Other" || ns.StartsWith("Tweaks.Other.", StringComparison.Ordinal) ||
                     ns == "Tweaks.Others" || ns.StartsWith("Tweaks.Others.", StringComparison.Ordinal)) module = "Others";
            // Debug modules always bind their options, even when the debug master is off.
            return module == null || (startupModules.TryGetValue(module, out bool active) && active);
        }

        private static bool IsDebugSection(string section)
        {
            return string.Equals(section, "Debug", StringComparison.OrdinalIgnoreCase) ||
                (section != null && section.StartsWith("Debug -", StringComparison.OrdinalIgnoreCase));
        }

        private static string ModuleForSection(string section)
        {
            string first = (section ?? "").Split(new[] { " -" }, StringSplitOptions.None)[0].Trim();
            switch (first)
            {
                case "Survivors": return "Survivors";
                case "Items": return "Items";
                case "Stages": return "Stages";
                case "Directors": return "Directors";
                case "Enemies": return "Enemies";
                case "Interactables": case "Shrines": return "Interactables";
                case "Bazaar": return "Bazaar";
                case "Artifacts": return "Artifacts";
                case "Other": case "Others": return "Others";
                case "Remote Operation": return "RemoteOperation";
                case "Mod Compatibility": case "ModCompatibility": return "ModCompatibility";
                default: return null;
            }
        }

        internal static ConfigFile SettingsFile(string section)
        {
            if (IsDebugSection(section)) return DebugFile;
            if (section.StartsWith("Survivor-Stats -", StringComparison.Ordinal)) return SurvivorStatsFile;
            string module = ModuleForSection(section);
            return module == null ? File : moduleFiles[module];
        }

        private static string DebugSection(string section, string key)
        {
            if (!IsDebugSection(section)) return section;
            switch (key)
            {
                case "Give Test Item Loadout": case "Loadout 1 Enabled": return "Debug - Test Loadout";
                case "Log Stage 1 Selection Weights": case "Log Stage Selection Weights": return "Debug - Stage Selection";
                case "Log Stage History": return "Debug - Stage History";
                case "Newt Altar - Log Candidate Positions": case "Newt Altar - Show Location Markers":
                case "Newt Altar - Disable Guarantees During Mapping": return "Debug - Newt Altar Mapping";
                default: return section;
            }
        }

        private static string DebugKey(string section, string key)
        {
            if (IsDebugSection(section) && key == "Give Test Item Loadout") return "Loadout 1 Enabled";
            return IsDebugSection(section) && key == "Log Stage 1 Selection Weights" ? "Log Stage Selection Weights" : key;
        }

        private static Dictionary<ConfigDefinition, string> OrphanedSettings(ConfigFile file)
        {
            PropertyInfo property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            var orphaned = property?.GetValue(file, null) as Dictionary<ConfigDefinition, string>;
            if (orphaned == null) throw new InvalidOperationException("Cannot access BepInEx unbound configuration entries.");
            return orphaned;
        }

        private static string MigratedSection(string section)
        {
            if (section == Tweaks.Bazaar.CommonCauldron.LegacySection) return Tweaks.Bazaar.CommonCauldron.Section;
            if (section == "Survivors - General") return "Survivors - 00 General";
            foreach (string survivor in new[] { "Commando", "Huntress" })
            {
                string oldRoot = "Survivors - " + survivor;
                string number = survivor == "Commando" ? "01" : "02";
                string root = "Survivors - " + number + " " + survivor;
                if (section == oldRoot + " - Stats" || section == root + " - Stats")
                    return "Survivor-Stats - " + number + " " + survivor;
                if (section == oldRoot || section.StartsWith(oldRoot + " -", StringComparison.Ordinal))
                    return root + section.Substring(oldRoot.Length);
            }
            if (section == "Stages - General - Void Seeds") return "Interactables - Void Seeds";
            if (section.StartsWith("Shrines -", StringComparison.Ordinal)) return "Interactables" + section.Substring("Shrines".Length);
            if (section == "Other - Multiplayer Drones") return "Remote Operation - General";
            const string oldRemote = "Other - Remote Drones";
            if (section == oldRemote) return "Remote Operation - Skills";
            if (section == oldRemote + " - Disconnect") return "Remote Operation - Disconnect";
            if (section == oldRemote + " - Gunner" || section.StartsWith(oldRemote + " - Gunner -", StringComparison.Ordinal))
                return "Remote Operation - Gunner Drone" + section.Substring((oldRemote + " - Gunner").Length);
            if (section == oldRemote + " - Healing" || section.StartsWith(oldRemote + " - Healing -", StringComparison.Ordinal))
                return "Remote Operation - Healing Drone" + section.Substring((oldRemote + " - Healing").Length);
            if (section == oldRemote + " - Junk" || section.StartsWith(oldRemote + " - Junk -", StringComparison.Ordinal))
                return "Remote Operation - Junk Drone" + section.Substring((oldRemote + " - Junk").Length);
            return section;
        }

        private static void MigrateCommonCauldronSettings()
        {
            ConfigFile file = moduleFiles["Bazaar"];
            var orphaned = OrphanedSettings(file);
            foreach (var pair in orphaned.ToArray())
            {
                if (pair.Key.Section != Tweaks.Bazaar.CommonCauldron.LegacySection) continue;
                var destination = new ConfigDefinition(Tweaks.Bazaar.CommonCauldron.Section, pair.Key.Key);
                if (!orphaned.ContainsKey(destination)) orphaned.Add(destination, pair.Value);
                orphaned.Remove(pair.Key);
                dirtyFiles.Add(file);
            }
        }

        private static void MigrateRemoteOperationSettings()
        {
            ConfigFile destination = moduleFiles["RemoteOperation"];
            foreach (ConfigFile source in new[] { moduleFiles["Others"], File })
            {
                try
                {
                    var orphaned = OrphanedSettings(source);
                    foreach (var pair in orphaned.ToArray())
                    {
                        string section = MigratedSection(pair.Key.Section);
                        if (!section.StartsWith("Remote Operation", StringComparison.Ordinal)) continue;
                        var definition = new ConfigDefinition(section, pair.Key.Key);
                        var target = OrphanedSettings(destination);
                        if (!target.ContainsKey(definition) && !destination.ContainsKey(definition))
                            target.Add(definition, pair.Value);
                        orphaned.Remove(pair.Key);
                        dirtyFiles.Add(source);
                        dirtyFiles.Add(destination);
                    }
                }
                catch (Exception error)
                {
                    regenerationFailed = true;
                    ThreeQsVanillaTweaks.Log.Warning("Could not move remote-operation settings: " + error.Message);
                }
            }
        }

        private static void MigrateSettings()
        {
            // Existing category files take precedence over the old combined config.
            // Normalise the old debug file first so it also wins over legacy main-file copies.
            foreach (ConfigFile source in new[] { SurvivorStatsFile, DebugFile }.Concat(moduleFiles.Values)
                .Concat(legacyShrinesFile == null ? Array.Empty<ConfigFile>() : new[] { legacyShrinesFile }).Concat(new[] { File }))
            {
                try
                {
                    var raw = new Dictionary<ConfigDefinition, string>(OrphanedSettings(source));
                    foreach (var pair in source)
                        raw[pair.Key] = TomlTypeConverter.ConvertToString(pair.Value.BoxedValue, pair.Value.SettingType);
                    var migrated = new List<ConfigDefinition>();
                    var destinations = new HashSet<ConfigFile>();
                    foreach (var pair in raw)
                    {
                        string key = DebugKey(pair.Key.Section, pair.Key.Key);
                        string section = MigratedSection(DebugSection(pair.Key.Section, key));
                        if (key == "Damage Interval" && (section == "Survivors - 01 Commando - Double Tap" ||
                            section == "Survivors - 02 Huntress - Strafe" || section == "Survivors - 02 Huntress - Flurry"))
                        { migrated.Add(pair.Key); continue; }
                        if (section == "01 Modules" && key == "Shrines") key = "Interactables";
                        bool debugMaster = key == "Use Debug Options" &&
                            (ReferenceEquals(source, DebugFile) || IsDebugSection(pair.Key.Section));
                        if (debugMaster) section = "00 General";
                        ConfigFile destination = debugMaster ? File : SettingsFile(section);
                        var definition = new ConfigDefinition(section, key);
                        if (ReferenceEquals(source, destination) && pair.Key.Equals(definition)) continue;
                        var target = OrphanedSettings(destination);
                        if (!target.ContainsKey(definition) && !destination.ContainsKey(definition)) target.Add(definition, pair.Value);
                        migrated.Add(pair.Key);
                        destinations.Add(destination);
                    }
                    if (migrated.Count == 0) continue;
                    foreach (ConfigFile destination in destinations) dirtyFiles.Add(destination);
                    foreach (ConfigDefinition definition in migrated)
                    {
                        OrphanedSettings(source).Remove(definition);
                        source.Remove(definition);
                    }
                    dirtyFiles.Add(source);
                    ThreeQsVanillaTweaks.Log.Info("Moved " + migrated.Count + " config setting(s) from " + Path.GetFileName(source.ConfigFilePath) + ".");
                }
                catch (Exception e)
                {
                    regenerationFailed = true;
                    ThreeQsVanillaTweaks.Log.Warning("Could not migrate configuration from " + source.ConfigFilePath + ": " + e.Message);
                }
            }
        }

        private static string ClientLabelBase(string key)
        {
            while (key.StartsWith("(Client) ", StringComparison.OrdinalIgnoreCase)) key = key.Substring(9);
            while (key.EndsWith(" (Client)", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 9);
            return key;
        }
        private static string PrepareClientKey(ConfigFile file, string section, string key, bool clientSide)
        {
            string baseKey = ClientLabelBase(key);
            if (!clientSide && baseKey == key) return key;
            var definition = new ConfigDefinition(section, "(Client) " + baseKey);
            var orphaned = OrphanedSettings(file);
            bool hasCurrent = file.ContainsKey(definition) || orphaned.ContainsKey(definition);
            // Import an old value only when the new name has no saved value of its own.
            // Renaming happens in memory; the normal startup flush saves changed files once.
            foreach (string alias in new[] { key, baseKey, baseKey + " (Client)" }.Distinct())
            {
                var old = new ConfigDefinition(section, alias);
                if (old.Equals(definition)) continue;
                bool bound = file.ContainsKey(old);
                bool unbound = orphaned.TryGetValue(old, out string raw);
                if (!bound && !unbound) continue;
                if (!hasCurrent)
                {
                    if (bound)
                    {
                        ConfigEntryBase previous = file.First(pair => pair.Key.Equals(old)).Value;
                        raw = TomlTypeConverter.ConvertToString(previous.BoxedValue, previous.SettingType);
                    }
                    orphaned[definition] = raw;
                    hasCurrent = true;
                }
                orphaned.Remove(old);
                if (bound) file.Remove(old);
                // A direct caller may have just bound an unsaved legacy name.
                // Removing that temporary entry does not change a canonical file.
                if (unbound || originalDefinitions.TryGetValue(file, out var original) && original.Contains(old))
                    dirtyFiles.Add(file);
            }
            return definition.Key;
        }
        internal static void RetireUnboundSectionTree(string section)
        {
            ConfigFile file = SettingsFile(section);
            foreach (var definition in OrphanedSettings(file).Keys)
                if (definition.Section == section || definition.Section.StartsWith(section + " -", StringComparison.Ordinal))
                    retiredSettings.Add((file, definition));
        }

        internal static T ReadRetiredValue<T>(string section, string key, T fallback)
        {
            ConfigFile file = SettingsFile(section);
            var definition = new ConfigDefinition(section, key);
            retiredSettings.Add((file, definition));
            if (!OrphanedSettings(file).TryGetValue(definition, out var raw)) return fallback;
            try { return (T)TomlTypeConverter.ConvertToValue(raw, typeof(T)); }
            catch (Exception) { return fallback; }
        }

        internal static void UseMigratedValue<T>(ConfigEntry<T> entry, T previous)
        {
            if (!originalDefinitions.TryGetValue(entry.ConfigFile, out var existing) ||
                existing.Contains(entry.Definition) || EqualityComparer<T>.Default.Equals(entry.Value, previous)) return;
            entry.Value = previous;
        }

        internal static ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description, bool clientSide = false)
        {
            key = DebugKey(section, key); section = DebugSection(section, key);
            ConfigFile file = SettingsFile(section);
            string descriptionSection = section;
            section = SurvivorConfigSections.Sorted(section);
            key = PrepareClientKey(file, section, key, clientSide);
            return Register(file.Bind(section, key, defaultValue, VanillaConfigDescriptions.Add(descriptionSection, ClientLabelBase(key), description)), clientSide);
        }
        internal static ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description, bool clientSide = false)
        {
            key = DebugKey(section, key); section = DebugSection(section, key);
            ConfigFile file = SettingsFile(section);
            string descriptionSection = section;
            section = SurvivorConfigSections.Sorted(section);
            key = PrepareClientKey(file, section, key, clientSide);
            return Register(file.Bind(section, key, defaultValue, VanillaConfigDescriptions.Add(descriptionSection, ClientLabelBase(key), description)), clientSide);
        }
        internal static ConfigEntry<bool> BindBool(string section, string key, bool defaultValue, string description, bool clientSide = false)
        { return Bind(section, key, defaultValue, description, clientSide); }
        internal static ConfigEntry<T> Register<T>(ConfigEntry<T> entry, bool clientSide = false)
        {
            // Cover direct registrations as well as the shared Bind overloads.
            if (clientSide)
            {
                string key = PrepareClientKey(entry.ConfigFile, entry.Definition.Section, entry.Definition.Key, true);
                if (key != entry.Definition.Key)
                    entry = entry.ConfigFile.Bind(entry.Definition.Section, key, entry.Value, entry.Description);
            }
            if (!initializationFinished && originalDefinitions.TryGetValue(entry.ConfigFile, out var definitions) && !definitions.Contains(entry.Definition))
                dirtyFiles.Add(entry.ConfigFile);
            string id = Id(entry.Definition);
            if (!entries.ContainsKey(id))
            {
                entries.Add(id, entry);
                entry.SettingChanged += (sender, args) =>
                {
                    if (!initializationFinished) dirtyFiles.Add(entry.ConfigFile);
                    if (NetworkServer.active) Broadcast();
                    if (!UsingHost || localOnly.Contains(id) || ReferenceEquals(entry, useDebugOptions)) NotifyChanged();
                };
            }
            if (clientSide) localOnly.Add(id);
            return entry;
        }
        private static bool UsingHost { get { return NetworkClient.active && !NetworkServer.active && received; } }
        internal static T Get<T>(ConfigEntry<T> entry)
        {
            if (entry == null) return default(T);
            if (typeof(T) == typeof(bool) && ReferenceEquals(entry.ConfigFile, DebugFile) &&
                !ReferenceEquals(entry, regenerateConfig))
            {
                // Keep the stored preference intact. Turning the master switch on
                // immediately exposes the configured values, including new true defaults.
                bool enabled = localOnly.Contains(Id(entry.Definition))
                    ? useDebugOptions != null && useDebugOptions.Value
                    : DebugOptionsEnabled;
                if (!enabled) return (T)(object)false;
            }
            return EffectiveValue(entry);
        }

        private static T EffectiveValue<T>(ConfigEntry<T> entry)
        {
            if (entry == null) return default(T);
            string raw;
            if (UsingHost && !localOnly.Contains(Id(entry.Definition)) && hostValues.TryGetValue(Id(entry.Definition), out raw))
            {
                try { return (T)TomlTypeConverter.ConvertToValue(raw, typeof(T)); }
                catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Invalid host config value: " + e.Message); }
            }
            return entry.Value;
        }
        internal static void Tick()
        {
            if (bindingsReady && !initializationFinished) CompleteInitialization();
            if (NetworkServer.active && !serverRegistered)
            {
                NetworkServer.RegisterHandler(RequestMessage, OnRequest);
                serverRegistered = true;
            }
            if (!NetworkServer.active) serverRegistered = false;
            bool clientMode = NetworkClient.active && !NetworkServer.active;
            if (!clientMode)
            {
                if (wasClient || received) { hostValues.Clear(); received = false; NotifyChanged(); }
                receivingChunks = null;
                receivingSnapshotId = -1;
                lastAppliedSnapshotId = -1;
                receivedChunks = 0;
                currentClient = null;
            }
            else
            {
                NetworkClient client = NetworkManager.singleton != null ? NetworkManager.singleton.client : null;
                if (client != null && client != currentClient)
                {
                    currentClient = client;
                    received = false;
                    hostValues.Clear();
                    receivingChunks = null;
                    receivingSnapshotId = -1;
                    lastAppliedSnapshotId = -1;
                    receivedChunks = 0;
                    client.RegisterHandler(SnapshotMessage, OnSnapshot);
                    nextRequest = 0f;
                }
                if (currentClient != null && currentClient.isConnected && !received && Time.unscaledTime >= nextRequest)
                {
                    currentClient.Send(RequestMessage, new SettingsMessage { payload = "" });
                    nextRequest = Time.unscaledTime + 3f;
                }
            }
            wasClient = clientMode;
        }
        private static void OnRequest(NetworkMessage message)
        {
            if (NetworkServer.active && message.conn != null)
                SendSnapshot((type, packet) => message.conn.Send(type, packet));
        }
        private static void Broadcast()
        {
            if (NetworkServer.active) SendSnapshot((type, packet) => NetworkServer.SendToAll(type, packet));
        }
        private static void SendSnapshot(Action<short, MessageBase> send)
        {
            string contents = Serialize();
            int count = Math.Max(1, (contents.Length + SnapshotChunkSize - 1) / SnapshotChunkSize);
            int id = ++nextSnapshotId;
            for (int i = 0; i < count; ++i)
            {
                int start = i * SnapshotChunkSize;
                send(SnapshotMessage, new SnapshotChunkMessage
                {
                    snapshotId = id,
                    index = i,
                    count = count,
                    payload = contents.Substring(start, Math.Min(SnapshotChunkSize, contents.Length - start))
                });
            }
        }
        private static string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var pair in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (localOnly.Contains(pair.Key)) continue;
                object value = pair.Value.BoxedValue;
                // Advertise the modules loaded for this session, rather than a
                // switch edited for the next restart.
                foreach (var module in moduleSwitches)
                    if (ReferenceEquals(pair.Value, module.Value)) { value = startupModules[module.Key]; break; }
                if (startupSwitches.TryGetValue(pair.Key, out bool startupValue)) value = startupValue;
                string raw = TomlTypeConverter.ConvertToString(value, pair.Value.SettingType);
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key)));
                sb.Append('|');
                sb.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
                sb.Append('\n');
            }
            return sb.ToString();
        }
        private static void OnSnapshot(NetworkMessage message)
        {
            if (NetworkServer.active) return;
            var chunk = message.ReadMessage<SnapshotChunkMessage>();
            if (chunk.count < 1 || chunk.count > 4096 || chunk.index < 0 || chunk.index >= chunk.count ||
                chunk.payload == null || chunk.payload.Length > SnapshotChunkSize) return;
            if (chunk.snapshotId <= lastAppliedSnapshotId ||
                receivingSnapshotId >= 0 && chunk.snapshotId < receivingSnapshotId) return;
            if (chunk.snapshotId != receivingSnapshotId || receivingChunks == null || receivingChunks.Length != chunk.count)
            {
                receivingSnapshotId = chunk.snapshotId;
                receivingChunks = new string[chunk.count];
                receivedChunks = 0;
            }
            if (receivingChunks[chunk.index] == null)
            {
                receivingChunks[chunk.index] = chunk.payload;
                receivedChunks++;
            }
            if (receivedChunks != receivingChunks.Length) return;
            string payload = string.Concat(receivingChunks);
            receivingChunks = null;
            receivingSnapshotId = -1;
            lastAppliedSnapshotId = chunk.snapshotId;
            var incoming = new Dictionary<string, string>();
            foreach (string line in payload.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;
                string[] pieces = line.Split('|');
                if (pieces.Length != 2) continue;
                try
                {
                    string id = Encoding.UTF8.GetString(Convert.FromBase64String(pieces[0]));
                    string raw = Encoding.UTF8.GetString(Convert.FromBase64String(pieces[1]));
                    if (!localOnly.Contains(id) && entries.ContainsKey(id)) incoming[id] = raw;
                }
                catch (FormatException) { }
            }
            hostValues.Clear();
            foreach (var pair in incoming) hostValues.Add(pair.Key, pair.Value);
            received = true;
            CheckModuleCompatibility();
            ThreeQsVanillaTweaks.Log.Info("Received host settings: " + hostValues.Count);
            NotifyChanged();
        }
        private static void CheckModuleCompatibility()
        {
            var different = new List<string>();
            foreach (var pair in moduleSwitches)
                if (hostValues.TryGetValue(Id(pair.Value.Definition), out string raw) &&
                    bool.TryParse(raw, out bool hostEnabled) && hostEnabled != startupModules[pair.Key]) different.Add(pair.Key);
            foreach (var pair in startupSwitches)
                if (hostValues.TryGetValue(pair.Key, out string startupRaw) && bool.TryParse(startupRaw, out bool hostStartup) && hostStartup != pair.Value)
                    different.Add(pair.Key.Replace("\n", " / "));
            string mismatch = string.Join(", ", different);
            if (mismatch.Length == 0) { warnedModuleMismatch = null; return; }
            if (mismatch == warnedModuleMismatch) return;
            warnedModuleMismatch = mismatch;
            ThreeQsVanillaTweaks.Log.Warning("Startup switches differ from the host: " + mismatch + ". Match the host's switches and restart the game.");
        }

        private static void NotifyChanged() { if (EffectiveSettingsChanged != null) EffectiveSettingsChanged(); }
        private sealed class SettingsMessage : MessageBase
        {
            public string payload;
            public override void Serialize(NetworkWriter writer) { writer.Write(payload ?? ""); }
            public override void Deserialize(NetworkReader reader) { payload = reader.ReadString(); }
        }
        private sealed class SnapshotChunkMessage : MessageBase
        {
            public int snapshotId, index, count;
            public string payload;
            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(snapshotId); writer.Write(index); writer.Write(count); writer.Write(payload ?? "");
            }
            public override void Deserialize(NetworkReader reader)
            {
                snapshotId = reader.ReadInt32(); index = reader.ReadInt32(); count = reader.ReadInt32(); payload = reader.ReadString();
            }
        }
    }
}
