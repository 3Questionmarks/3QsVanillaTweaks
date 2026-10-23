using System;
using System.Collections.Generic;
using System.Linq;
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
        internal static ConfigFile File { get; private set; }
        private static readonly Dictionary<string, ConfigEntryBase> entries = new Dictionary<string, ConfigEntryBase>();
        private static readonly HashSet<string> localOnly = new HashSet<string>();
        private static readonly Dictionary<string, string> hostValues = new Dictionary<string, string>();
        private static NetworkClient currentClient;
        private static bool serverRegistered, received, wasClient;
        private static float nextRequest;
        internal static event Action EffectiveSettingsChanged;
        private static string Id(ConfigDefinition d) { return d.Section + "\n" + d.Key; }
        internal static void Init(ConfigFile file) { File = file; }
        internal static ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description, bool clientSide = false)
        { return Register(File.Bind(section, key, defaultValue, description), clientSide); }
        internal static ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description, bool clientSide = false)
        { return Register(File.Bind(section, key, defaultValue, description), clientSide); }
        internal static ConfigEntry<bool> BindBool(string section, string key, bool defaultValue, string description, bool clientSide = false)
        { return Bind(section, key, defaultValue, description, clientSide); }
        internal static ConfigEntry<T> Register<T>(ConfigEntry<T> entry, bool clientSide = false)
        {
            string id = Id(entry.Definition);
            if (!entries.ContainsKey(id))
            {
                entries.Add(id, entry);
                entry.SettingChanged += (sender, args) =>
                {
                    if (NetworkServer.active) Broadcast();
                    if (!UsingHost || localOnly.Contains(id)) NotifyChanged();
                };
            }
            if (clientSide) localOnly.Add(id);
            return entry;
        }
        private static bool UsingHost { get { return NetworkClient.active && !NetworkServer.active && received; } }
        internal static T Get<T>(ConfigEntry<T> entry)
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
                message.conn.Send(SnapshotMessage, new SettingsMessage { payload = Serialize() });
        }
        private static void Broadcast()
        {
            if (NetworkServer.active) NetworkServer.SendToAll(SnapshotMessage, new SettingsMessage { payload = Serialize() });
        }
        private static string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var pair in entries.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (localOnly.Contains(pair.Key)) continue;
                string raw = TomlTypeConverter.ConvertToString(pair.Value.BoxedValue, pair.Value.SettingType);
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
            var snapshot = message.ReadMessage<SettingsMessage>();
            var incoming = new Dictionary<string, string>();
            foreach (string line in (snapshot.payload ?? "").Split('\n'))
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
            ThreeQsVanillaTweaks.Log.Info("Received host settings: " + hostValues.Count);
            NotifyChanged();
        }
        private static void NotifyChanged() { if (EffectiveSettingsChanged != null) EffectiveSettingsChanged(); }
        private sealed class SettingsMessage : MessageBase
        {
            public string payload;
            public override void Serialize(NetworkWriter writer) { writer.Write(payload ?? ""); }
            public override void Deserialize(NetworkReader reader) { payload = reader.ReadString(); }
        }
    }
}
