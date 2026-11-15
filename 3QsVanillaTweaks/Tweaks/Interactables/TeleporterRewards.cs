using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using Tweaks.DebugStuff;
using Tweaks.Stages;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class TeleporterRewards
    {
        private const float RewardDisplayMarker = -137.314f;
        private const int RewardUpgradeMarker = -137314;
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> bossChance;
        private static ConfigEntry<float> hordeReplacementChance;
        private static ConfigEntry<int> hordeCommonCount;
        private static ConfigEntry<bool> tinkerReroll;
        [ThreadStatic] private static Capture active;
        private static readonly ConditionalWeakTable<BossGroup, BossEncounter> encounters =
            new ConditionalWeakTable<BossGroup, BossEncounter>();
        private static readonly System.Reflection.FieldInfo bossRewardRng = AccessTools.Field(typeof(BossGroup), "rng");
        private static readonly Type overchargeStateType = typeof(TeleporterInteraction).Assembly.GetType(
            "EntityStates.TeleporterCatastrophe.DLC4.Teleporter.OverchargingState");
        private static readonly System.Reflection.FieldInfo overchargeBossGroup = overchargeStateType == null
            ? null : AccessTools.Field(overchargeStateType, "extraBossGroupServer");
        private static readonly Dictionary<PickupPickerController.Option[], PendingBundle> pendingBundles =
            new Dictionary<PickupPickerController.Option[], PendingBundle>();
        private static GameObject rewardEffect;
        private static bool lookedForRewardEffect;
        private sealed class BossEncounter
        {
            internal bool SawNonChampion;
            internal bool SawChampionOrUnknown;
        }
        private sealed class Capture
        {
            internal Capture Previous;
            internal BossGroup Group;
            internal bool Scaled;
            internal float BossChance;
            internal bool UsePicker;
            internal float HordeChance;
            internal readonly List<(UniquePickup Pickup, Vector3 Position, Vector3 Velocity)> Drops =
                new List<(UniquePickup Pickup, Vector3 Position, Vector3 Velocity)>();
        }
        private sealed class PendingBundle
        {
            internal CharacterMaster Owner;
        }

        internal static void Init()
        {
            const string section = "Interactables - Teleporter Rewards";
            enabled = C.BindBool(section, "Per Player Reward Choices", true,
                "Each player receives a choice pickup containing the same teleporter rewards. Claim every reward from your pickup. Vanilla: loose items shared by all players.");
            bossChance = C.Bind(section, "Boss Item Replacement Chance (%)", 10f,
                new ConfigDescription("Chance for each teleporter reward to become the defeated boss's item. Vanilla: 15%.", new AcceptableValueRange<float>(0f, 100f)));
            hordeReplacementChance = C.Bind(section, "Horde Of Many Common Replacement Chance (%)", 25f,
                new ConfigDescription("Chance for each item reward from a Horde of Many teleporter boss to become a bundle of random common items. Vanilla: 0%.", new AcceptableValueRange<float>(0f, 100f)));
            hordeCommonCount = C.Bind(section, "Horde Of Many Commons Per Replacement", 3,
                new ConfigDescription("Number of common items per successful Horde of Many replacement.", new AcceptableValueRange<int>(1, int.MaxValue)));
            tinkerReroll = C.BindBool(section, "Drifter Tinker Can Reroll Reward Pickup", false,
                "Allow Drifter's Tinker to reroll the contents of a teleporter reward pickup.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.TeleporterRewards");
            harmony.Patch(AccessTools.Method(typeof(BossGroup), "OnMemberAddedServer"),
                postfix: new HarmonyMethod(typeof(TeleporterRewards), nameof(TrackBossMember)));
            harmony.Patch(AccessTools.Method(typeof(BossGroup), "DropRewards"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(Before)),
                postfix: new HarmonyMethod(typeof(TeleporterRewards), nameof(After)),
                finalizer: new HarmonyMethod(typeof(TeleporterRewards), nameof(Finish)));
#pragma warning disable CS0612, CS0618
            harmony.Patch(AccessTools.Method(typeof(PickupDropletController), "CreatePickupDroplet",
                new[] { typeof(UniquePickup), typeof(Vector3), typeof(Vector3) }),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(CaptureDrop)));
#pragma warning restore CS0612, CS0618
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "GetInteractability"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(CheckOwner)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(AllowOwner)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "SetOptionsServer"),
                postfix: new HarmonyMethod(typeof(TeleporterRewards), nameof(AttachRewardBundle)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "HandlePickupSelected"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(HandleRewardChoice)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "OnDisplayBegin"),
                postfix: new HarmonyMethod(typeof(TeleporterRewards), nameof(EnableClientMultiPick)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "AddPickupSelected"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(KeepRewardPanelOpen)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "KillPanel"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(KeepRewardPanelOpen)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(TinkerableObjectAttributes), "IsTinkerable"),
                postfix: new HarmonyMethod(typeof(TeleporterRewards), nameof(CheckTinkerable)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "RerollCurrentOptions"),
                prefix: new HarmonyMethod(typeof(TeleporterRewards), nameof(AllowReroll)));
        }

        // Count the debug players here as well as in Run.participatingPlayerCount.
        // The reward picker must not depend on another patch to the Run getter.
        private static int RewardPlayerCount => PlayerCharacterMasterController.instances.Count +
            (ExtraPlayers.Active ? ExtraPlayers.Count : 0);

        private static bool IsTeleporter(BossGroup group) =>
            TeleporterInteraction.instance && TeleporterInteraction.instance.bossGroup == group;

        private static bool IsOverchargeBoss(BossGroup group)
        {
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            var state = teleporter && teleporter.mainStateMachine ? teleporter.mainStateMachine.state : null;
            return state != null && overchargeStateType?.IsInstanceOfType(state) == true &&
                ReferenceEquals(overchargeBossGroup?.GetValue(state), group);
        }

        private static void TrackBossMember(BossGroup __instance, CharacterMaster memberMaster)
        {
            if (!NetworkServer.active || (!IsTeleporter(__instance) && !IsOverchargeBoss(__instance))) return;
            // The defeated squad can be empty by the time DropRewards runs.
            BossEncounter encounter = encounters.GetOrCreateValue(__instance);
            CharacterBody body = memberMaster ? memberMaster.GetBody() : null;
            if (!body && memberMaster && memberMaster.bodyPrefab)
                body = memberMaster.bodyPrefab.GetComponent<CharacterBody>();
            if (body && !body.isChampion) encounter.SawNonChampion = true;
            else encounter.SawChampionOrUnknown = true;
        }

        private static void Before(BossGroup __instance, out Capture __state)
        {
            __state = new Capture { Previous = active, Group = __instance,
                Scaled = __instance.scaleRewardsByPlayerCount, BossChance = __instance.bossDropChance };
            active = null;
            if (!NetworkServer.active || !Run.instance) return;
            bool primary = IsTeleporter(__instance);
            bool horde = encounters.TryGetValue(__instance, out BossEncounter encounter) &&
                encounter.SawNonChampion && !encounter.SawChampionOrUnknown;
            if (horde && (primary || IsOverchargeBoss(__instance)) && Run.instance.availableTier1DropList.Count > 0)
                __state.HordeChance = C.Get(hordeReplacementChance) / 100f;
            if (!primary && __state.HordeChance <= 0f) return;
            if (ExtraPlayers.Active)
                ThreeQsVanillaTweaks.Log.Info("Teleporter rewards: " + RewardPlayerCount +
                    " counted players (" + PlayerCharacterMasterController.instances.Count +
                    " connected, " + ExtraPlayers.Count + " simulated); choices " + C.Get(enabled) + ".");
            if (primary) __instance.bossDropChance = C.Get(bossChance) / 100f;
            __state.UsePicker = C.Get(enabled) && RewardPlayerCount > 1;
            if (__state.UsePicker)
            {
                // Native rolls exactly one player's mountain-shrine reward set.
                __instance.scaleRewardsByPlayerCount = false;
            }
            if (__state.UsePicker || __state.HordeChance > 0f) active = __state;
        }

        private static bool CaptureDrop(UniquePickup pickup, Vector3 position, Vector3 velocity)
        {
            if (active == null) return true;
            active.Drops.Add((pickup, position, velocity));
            return false;
        }

        private static List<(UniquePickup Pickup, Vector3 Position, Vector3 Velocity)> ExpandHordeDrops(Capture capture)
        {
            if (capture.HordeChance <= 0f || !Run.instance) return capture.Drops;
            List<PickupIndex> commons = Run.instance.availableTier1DropList;
            var rng = bossRewardRng?.GetValue(capture.Group) as Xoroshiro128Plus ?? Run.instance.bossRewardRng;
            if (commons.Count == 0 || rng == null) return capture.Drops;

            int commonCount = C.Get(hordeCommonCount);
            var drops = new List<(UniquePickup Pickup, Vector3 Position, Vector3 Velocity)>(capture.Drops.Count);
            foreach (var drop in capture.Drops)
            {
                PickupDef def = PickupCatalog.GetPickupDef(drop.Pickup.pickupIndex);
                if (def == null || def.itemIndex == ItemIndex.None ||
                    (capture.HordeChance < 1f && rng.nextNormalizedFloat >= capture.HordeChance))
                {
                    drops.Add(drop);
                    continue;
                }
                for (int i = 0; i < commonCount; i++)
                {
                    var pickup = new UniquePickup(commons[rng.RangeInt(0, commons.Count)]);
                    float angle = commonCount == 1 ? 0f : Mathf.Lerp(-20f, 20f, i / (commonCount - 1f));
                    Vector3 velocity = Quaternion.AngleAxis(angle, Vector3.up) * drop.Velocity;
                    drops.Add((pickup, drop.Position, velocity));
                }
            }
            return drops;
        }

        private static void SpawnLooseDrops(List<(UniquePickup Pickup, Vector3 Position, Vector3 Velocity)> drops)
        {
            foreach (var drop in drops)
                PickupDropletController.CreatePickupDroplet(drop.Pickup, drop.Position, drop.Velocity, false);
        }

        private static void After(BossGroup __instance, Capture __state)
        {
            if (__state == null) return;
            __instance.scaleRewardsByPlayerCount = __state.Scaled;
            __instance.bossDropChance = __state.BossChance;
            active = __state.Previous;
            var drops = ExpandHordeDrops(__state);
            if (ExtraPlayers.Active && IsTeleporter(__instance))
                ThreeQsVanillaTweaks.Log.Info("Teleporter rewards: captured " + __state.Drops.Count +
                    " reward(s), yielding " + drops.Count + " items" +
                    (__state.UsePicker && drops.Count > 0 ? " per player pickup." : "."));
            if (drops.Count == 0) return;
            if (!__state.UsePicker)
            {
                SpawnLooseDrops(drops);
                return;
            }
            GameObject prefab = null;
            try { prefab = StageInteractableAssets.Prefab("OptionPickup"); }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Teleporter reward pickup unavailable: " + error.Message); }
            if (!prefab)
            {
                SpawnLooseDrops(drops);
                return;
            }
            var owners = new List<CharacterMaster>();
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
                if (player && player.master) owners.Add(player.master);
            if (owners.Count == 0)
            {
                SpawnLooseDrops(drops);
                return;
            }
            var rewards = new List<UniquePickup>(drops.Count);
            foreach (var drop in drops) rewards.Add(drop.Pickup);
            int pickupCount = RewardPlayerCount;
            Vector3 origin = __instance.dropPosition.position;
            PlayRewardEffect(origin, __instance.gameObject);
            float startingAngle = UnityEngine.Random.Range(0f, 360f);
            for (int slot = 0; slot < pickupCount; slot++)
            {
                Quaternion direction = Quaternion.AngleAxis(startingAngle + 360f * slot / pickupCount, Vector3.up);
                Vector3 velocity = direction * new Vector3(0f, 40f, 5f);
                CharacterMaster owner = owners[Math.Min(slot, owners.Count - 1)];
                LaunchBundle(prefab, origin, velocity, owner, rewards);
            }
        }

        private static Exception Finish(Exception __exception, BossGroup __instance, Capture __state)
        {
            if (__state != null)
            {
                __instance.scaleRewardsByPlayerCount = __state.Scaled;
                __instance.bossDropChance = __state.BossChance;
                active = __state.Previous;
            }
            return __exception;
        }

        private static PickupPickerController.Option[] Options(List<UniquePickup> rewards)
        {
            var options = new PickupPickerController.Option[rewards.Count];
            for (int i = 0; i < rewards.Count; i++)
                options[i] = new PickupPickerController.Option { available = true, pickup = rewards[i] };
            return options;
        }

        private static UniquePickup DisplayPickup(List<UniquePickup> rewards)
        {
            PickupDef first = PickupCatalog.GetPickupDef(rewards[0].pickupIndex);
            ItemDef item = first == null ? null : ItemCatalog.GetItemDef(first.itemIndex);
            return new UniquePickup(PickupCatalog.FindPickupIndex(item ? item.tier : ItemTier.Tier2))
            {
                ignoreArtifactBasedPickupReplacements = true,
                decayValue = RewardDisplayMarker,
                upgradeValue = RewardUpgradeMarker
            };
        }

        private static void LaunchBundle(GameObject prefab, Vector3 origin, Vector3 velocity,
            CharacterMaster owner, List<UniquePickup> rewards)
        {
            if (rewards.Count == 0 || !owner) return;
            PickupPickerController.Option[] options = Options(rewards);
            pendingBundles.Add(options, new PendingBundle { Owner = owner });
            try
            {
                var info = new GenericPickupController.CreatePickupInfo
                {
                    pickup = DisplayPickup(rewards), pickerOptions = options,
                    maxOptionCount = options.Length, prefabOverride = prefab,
                    position = origin, rotation = Quaternion.identity
                };
                PickupDropletController.CreatePickupDroplet(info, origin, velocity);
            }
            catch
            {
                pendingBundles.Remove(options);
                throw;
            }
        }

        private static void AttachRewardBundle(PickupPickerController __instance,
            PickupPickerController.Option[] newOptions)
        {
            if (!NetworkServer.active || !__instance || newOptions == null ||
                !pendingBundles.TryGetValue(newOptions, out PendingBundle pending)) return;
            if (__instance.GetComponent<RewardBundle>()) return;
            pendingBundles.Remove(newOptions);
            var bundle = __instance.gameObject.AddComponent<RewardBundle>();
            bundle.Owner = pending.Owner;
            ThreeQsVanillaTweaks.Log.Info("Teleporter rewards: attached " + newOptions.Length +
                " rewards to a player pickup.");
        }

        private static bool HandleRewardChoice(PickupPickerController __instance, int choiceIndex)
        {
            RewardBundle bundle = __instance.GetComponent<RewardBundle>();
            if (!NetworkServer.active) return true;
            if (!bundle)
            {
                if (IsRewardPicker(__instance))
                    ThreeQsVanillaTweaks.Log.Warning("Teleporter rewards: selection reached a pickup without its reward bundle.");
                return true;
            }
            NetworkUIPromptController prompt = __instance.GetComponent<NetworkUIPromptController>();
            if (!prompt || prompt.currentParticipantMaster != bundle.Owner) return false;

            PickupPickerController.Option[] options = __instance.options;
            if ((uint)choiceIndex >= options.Length || !options[choiceIndex].available) return false;
            UniquePickup reward = options[choiceIndex].pickup;
            DropSelectedReward(__instance, reward);
            options[choiceIndex].available = false;

            bool remaining = false;
            for (int i = 0; i < options.Length; i++)
                if (options[i].available) { remaining = true; break; }
            if (remaining)
                __instance.SetOptionsServer(options);
            else
            {
                prompt.SetParticipantMaster(null);
                NetworkServer.Destroy(__instance.gameObject);
            }
            ThreeQsVanillaTweaks.Log.Info("Teleporter rewards: choice " + choiceIndex +
                " processed; more rewards=" + remaining + ".");
            return false; // Do not invoke the prefab's one-selection destruction event.
        }

        private static void DropSelectedReward(PickupPickerController picker, UniquePickup reward)
        {
            Vector3 position = picker.transform.position + Vector3.up * 0.8f;
            Vector3 velocity = Vector3.up * 12f + new Vector3(
                UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));
            var info = new GenericPickupController.CreatePickupInfo
            {
                pickup = reward,
                position = position,
                rotation = Quaternion.identity
            };
            PickupDropletController.CreatePickupDroplet(info, position, velocity);
        }

        private static void EnableClientMultiPick(PickupPickerController __instance)
        {
            if (!IsRewardPicker(__instance)) return;
            // SetMaxOptionCount in GenericPickupController.CreatePickup runs only on the server.
            // The client otherwise keeps the prefab's one-pick limit and closes its own panel.
            __instance.SetMaxOptionCount(__instance.options.Length);
        }

        private static bool KeepRewardPanelOpen(PickupPickerController __instance) =>
            !IsRewardPicker(__instance);

        private static void CheckTinkerable(TinkerableObjectAttributes __instance, ref bool __result)
        {
            if (!C.Get(tinkerReroll) && IsRewardPicker(__instance.GetComponent<PickupPickerController>()))
                __result = false;
        }

        private static bool AllowReroll(PickupPickerController __instance) =>
            C.Get(tinkerReroll) || !IsRewardPicker(__instance);

        private static bool IsRewardPicker(PickupPickerController picker)
        {
            if (!picker) return false;
            if (picker.GetComponent<RewardBundle>()) return true;
            // GenericPickupController's local pickup is not necessarily populated on clients.
            // The display state in PickupIndexNetworker is synchronized with the spawned pickup.
            PickupIndexNetworker networker = picker.GetComponent<PickupIndexNetworker>();
            if (networker && IsRewardDisplay(networker.pickupState)) return true;
            GenericPickupController pickup = picker.GetComponent<GenericPickupController>();
            return pickup && IsRewardDisplay(pickup.pickup);
        }

        private static bool IsRewardDisplay(UniquePickup pickup) =>
            pickup.decayValue == RewardDisplayMarker || pickup.upgradeValue == RewardUpgradeMarker;

        private static void PlayRewardEffect(Vector3 position, GameObject teleporter)
        {
            if (!lookedForRewardEffect)
            {
                lookedForRewardEffect = true;
                try
                {
                    GameObject shrine = StageInteractableAssets.Interactable("ShrineChance", "RoR2.ShrineChanceBehavior");
                    ShrineChanceBehavior behavior = shrine ? shrine.GetComponentInChildren<ShrineChanceBehavior>(true) : null;
                    rewardEffect = behavior ? behavior.effectPrefabShrineRewardNormal : null;
                }
                catch (Exception error)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Teleporter reward sound effect unavailable: " + error.Message);
                }
            }
            if (rewardEffect)
                EffectManager.SpawnEffect(rewardEffect, new EffectData
                {
                    origin = position, rotation = Quaternion.identity, scale = 1f, color = Color.white
                }, true);
            else Util.PlaySound("Play_UI_item_pickup", teleporter);
        }

        private static bool CheckOwner(PickupPickerController __instance, Interactor activator, ref Interactability __result)
        {
            RewardBundle bundle = __instance.GetComponent<RewardBundle>();
            if (!NetworkServer.active || !bundle) return true;
            if (OwnerMatches(bundle, activator)) return true;
            __result = Interactability.ConditionsNotMet;
            return false;
        }
        private static bool AllowOwner(PickupPickerController __instance, Interactor activator)
        {
            RewardBundle bundle = __instance.GetComponent<RewardBundle>();
            return !NetworkServer.active || !bundle || OwnerMatches(bundle, activator);
        }
        private static bool OwnerMatches(RewardBundle bundle, Interactor activator)
        {
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            return body && body.master == bundle.Owner;
        }

        internal sealed class RewardBundle : MonoBehaviour
        {
            internal CharacterMaster Owner;
        }
    }
}
