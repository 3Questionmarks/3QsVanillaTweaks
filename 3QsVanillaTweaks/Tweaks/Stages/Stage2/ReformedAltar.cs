using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage2
{
    internal static class ReformedAltar
    {
        private const short SnapshotMessage = 183, RequestMessage = 184;
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> offsetX, offsetY, offsetZ;
        private static ConfigEntry<int> choices;
        private static readonly HashSet<string> Claimed = new HashSet<string>();
        private static readonly string[] FoodNames = { "FlatHealth", "Mushroom", "HealWhileSafe", "SprintBonus", "AttackSpeedAndMoveSpeed", "HealingPotion" };
        private static string epoch = "";
        private static NetworkClient client;
        private static bool serverRegistered, received;
        private static float nextRequest;

        private sealed class ClaimMessage : MessageBase
        {
            public string payload;
            public override void Serialize(NetworkWriter writer) { writer.Write(payload ?? ""); }
            public override void Deserialize(NetworkReader reader) { payload = reader.ReadString(); }
        }

        internal static void Init()
        {
            const string section = "Stages - Stage 2 - Reformed Altar - Wok Food Potential";
            enabled = ModConfig.BindBool(section, "Enabled", true,
                "Let players who completed Order Up! claim one food potential from the Wok per visit.");
            var oldOptions = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(ModConfig.SettingsFile(section), null) as IDictionary<ConfigDefinition, string>;
            if (oldOptions?.Remove(new ConfigDefinition(section, "Spawn Chance (%)")) == true) ModConfig.SettingsFile(section).Save();
            choices = ModConfig.Bind(section, "Food Choices", 3, new ConfigDescription("Number of different food items offered.", new AcceptableValueRange<int>(1, 6)));
            offsetX = ModConfig.Bind(section, "Drop Offset X", 0f, "Horizontal X offset from the Wok.");
            offsetY = ModConfig.Bind(section, "Drop Offset Y", 2f, "Height above the Wok.");
            offsetZ = ModConfig.Bind(section, "Drop Offset Z", 0f, "Horizontal Z offset from the Wok.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.ReformedAltar");
            harmony.Patch(AccessTools.Method(typeof(WokController), "GetInteractability"),
                prefix: new HarmonyMethod(typeof(ReformedAltar), nameof(BeforeInteractability)));
            harmony.Patch(AccessTools.Method(typeof(WokController), "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(ReformedAltar), nameof(BeforeInteraction)));
            harmony.Patch(AccessTools.Method(typeof(WokController), "GetContextString"),
                prefix: new HarmonyMethod(typeof(ReformedAltar), nameof(BeforeContext)));
            foreach (string method in new[] { "EvaluateChefVisibility", "RpcHideChef" })
                harmony.Patch(AccessTools.Method(typeof(WokController), method),
                    postfix: new HarmonyMethod(typeof(ReformedAltar), nameof(KeepWokVisible)));
            RoR2Application.onFixedUpdate += Tick;
            Run.onRunDestroyGlobal += run => { epoch = ""; Claimed.Clear(); received = false; };
        }

        private static void EnsureEpoch()
        {
            string next = Run.instance && SceneCatalog.mostRecentSceneDef
                ? Run.instance.seed + ":" + Run.instance.stageClearCount + ":" + SceneCatalog.mostRecentSceneDef.baseSceneName : "";
            if (next == epoch) return;
            epoch = next; Claimed.Clear(); received = false; nextRequest = 0f;
        }

        private static bool Ready(WokController wok)
        {
            return wok && wok.gameObject.scene.name == "lemuriantemple" && Run.instance && ModConfig.Get(enabled) &&
                SpawnRequirements.ExpansionEnabled("DLC2") && SpawnRequirements.ExpansionEnabled("DLC1");
        }

        private static object UserId(NetworkUser user)
        {
            return user ? AccessTools.Property(user.GetType(), "netId")?.GetValue(user, null) : null;
        }

        private static string ClaimKey(NetworkUser user)
        {
            object account = user ? AccessTools.Property(user.GetType(), "id")?.GetValue(user, null) : null;
            if (account != null)
            {
                object value = AccessTools.Field(account.GetType(), "value")?.GetValue(account);
                string text = AccessTools.Field(account.GetType(), "strValue")?.GetValue(account) as string;
                object sub = AccessTools.Field(account.GetType(), "subId")?.GetValue(account);
                if ((value is ulong number && number != 0) || !string.IsNullOrEmpty(text))
                    return "account:" + value + ":" + sub + ":" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? ""));
            }
            return "connection:" + UserId(user);
        }

        private static bool Unlocked(NetworkUser user)
        {
            object id = UserId(user);
            var data = AccessTools.Field(typeof(WokController), "wokDataDictionary")?.GetValue(null) as IDictionary;
            if (id == null || data == null || !data.Contains(id)) return false;
            object entry = data[id];
            return entry != null && AccessTools.Field(entry.GetType(), "unlockedChef")?.GetValue(entry) is bool unlocked && unlocked;
        }

        private static NetworkUser User(Interactor activator)
        {
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            return body && body.master && body.master.playerCharacterMasterController ? body.master.playerCharacterMasterController.networkUser : null;
        }

        private static List<ItemIndex> Foods()
        {
            var foods = new List<ItemIndex>();
            foreach (string name in FoodNames)
            {
                ItemIndex index = ItemCatalog.FindItemIndex(name);
                if (index == ItemIndex.None) continue;
                ItemDef item = ItemCatalog.GetItemDef(index);
                if (item && item.tier == ItemTier.Tier1 && SpawnRequirements.ItemAllowed(item) && Run.instance.IsItemAvailable(index)) foods.Add(index);
            }
            return foods;
        }

        private static bool BeforeInteractability(WokController __instance, Interactor __0, ref Interactability __result)
        {
            if (!Ready(__instance)) return true;
            EnsureEpoch();
            NetworkUser user = User(__0);
            if (!Unlocked(user)) return true; // Preserve the original ingredient/unlock path.
            __result = Claimed.Contains(ClaimKey(user)) || Foods().Count == 0 ? Interactability.Disabled : Interactability.Available;
            return false;
        }

        private static bool BeforeContext(WokController __instance, Interactor __0, ref string __result)
        {
            if (!Ready(__instance) || !Unlocked(User(__0))) return true;
            __result = "Savour the offering";
            return false;
        }

        private static bool BeforeInteraction(WokController __instance, Interactor __0)
        {
            if (!Ready(__instance)) return true;
            NetworkUser user = User(__0);
            if (!Unlocked(user)) return true;
            if (!NetworkServer.active) return false;
            EnsureEpoch();
            string key = ClaimKey(user);
            if (Claimed.Contains(key)) return false;
            try
            {
                var foods = Foods();
                if (foods.Count == 0) return false;
                var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
                SpawnPotential(__instance, foods, rng);
                Claimed.Add(key);
                Broadcast();
                AccessTools.Method(typeof(WokController), "EvaluateChefVisibility")?.Invoke(__instance, null);
            }
            catch (Exception e) { Log.Warning("Wok food potential could not be spawned: " + e); }
            return false;
        }

        private static void SpawnPotential(WokController wok, List<ItemIndex> foods, Xoroshiro128Plus rng)
        {
            GameObject prefab = StageInteractableAssets.Prefab("OptionPickup");
            if (!StageFeatureSpawnHelper.PrefabAllowed(prefab)) throw new InvalidOperationException("Void Potential prefab is not enabled for this run.");
            int count = Mathf.Min(ModConfig.Get(choices), foods.Count);
            var options = new PickupPickerController.Option[count];
            for (int i = 0; i < count; ++i)
            {
                int chosen = rng.RangeInt(0, foods.Count);
                options[i] = new PickupPickerController.Option
                {
                    pickup = new UniquePickup(PickupCatalog.FindPickupIndex(foods[chosen])), available = true
                };
                foods.RemoveAt(chosen);
            }
            Vector3 position = wok.transform.position + new Vector3(ModConfig.Get(offsetX), ModConfig.Get(offsetY), ModConfig.Get(offsetZ));
            GameObject instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            try
            {
                PickupPickerController picker = instance.GetComponent<PickupPickerController>();
                if (!picker) throw new InvalidOperationException("Void Potential has no pickup picker.");
                picker.SetOptionsServer(options);
                // Runtime lookup avoids the newer CreatePickupInfo.maxOptionCount member.
                AccessTools.Method(typeof(PickupPickerController), "SetMaxOptionCount", new[] { typeof(int) })?.Invoke(picker, new object[] { 1 });
                PickupIndexNetworker display = instance.GetComponent<PickupIndexNetworker>();
                if (display) display.NetworkpickupState = new UniquePickup(PickupCatalog.FindPickupIndex(ItemTier.Tier1));
                NetworkServer.Spawn(instance);
                Util.PlaySound("Play_obj_chefWok_cook_complete", wok.gameObject);
            }
            catch { UnityEngine.Object.Destroy(instance); throw; }
        }

        private static void KeepWokVisible(WokController __instance)
        {
            if (!Ready(__instance)) return;
            EnsureEpoch();
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                NetworkUser user = player ? player.networkUser : null;
                if (!user || !Unlocked(user) || Claimed.Contains(ClaimKey(user))) continue;
                Animator animator = AccessTools.Field(typeof(WokController), "WokAnimator")?.GetValue(__instance) as Animator;
                if (animator) animator.gameObject.SetActive(true);
                return;
            }
        }

        private static ClaimMessage Snapshot() { return new ClaimMessage { payload = epoch + "\n" + string.Join("\n", Claimed) }; }
        private static void Broadcast()
        {
            if (!NetworkServer.active) return;
            ClaimMessage message = Snapshot();
            message.payload += "\n@serving";
            NetworkServer.SendToAll(SnapshotMessage, message);
        }
        private static void OnRequest(NetworkMessage message)
        {
            EnsureEpoch();
            if (NetworkServer.active && message.conn != null) message.conn.Send(SnapshotMessage, Snapshot());
        }
        private static void OnSnapshot(NetworkMessage message)
        {
            EnsureEpoch();
            string[] lines = (message.ReadMessage<ClaimMessage>().payload ?? "").Split('\n');
            if (lines.Length == 0 || lines[0] != epoch) return;
            bool serving = false, newClaim = false;
            var claims = new HashSet<string>();
            for (int i = 1; i < lines.Length; ++i)
            {
                if (lines[i] == "@serving") serving = true;
                else if (lines[i].Length > 0)
                {
                    claims.Add(lines[i]);
                    if (!Claimed.Contains(lines[i])) newClaim = true;
                }
            }
            Claimed.Clear(); Claimed.UnionWith(claims);
            received = true;
            if (serving && newClaim && !NetworkServer.active)
                foreach (WokController wok in UnityEngine.Object.FindObjectsOfType<WokController>())
                    if (Ready(wok)) { Util.PlaySound("Play_obj_chefWok_cook_complete", wok.gameObject); break; }
        }
        private static void Tick()
        {
            EnsureEpoch();
            if (NetworkServer.active && !serverRegistered)
            {
                NetworkServer.RegisterHandler(RequestMessage, OnRequest); serverRegistered = true;
            }
            if (!NetworkServer.active) serverRegistered = false;
            if (!NetworkClient.active || NetworkServer.active) { client = null; return; }
            NetworkClient next = NetworkManager.singleton ? NetworkManager.singleton.client : null;
            if (next != client)
            {
                client = next; received = false;
                client?.RegisterHandler(SnapshotMessage, OnSnapshot);
            }
            if (client != null && client.isConnected && !received && Time.unscaledTime >= nextRequest)
            {
                client.Send(RequestMessage, new ClaimMessage { payload = "" }); nextRequest = Time.unscaledTime + 2f;
            }
        }
    }
}
