using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage3
{
    internal static class RallypointDelta
    {
        private static ConfigEntry<bool> timer, reward, extra;
        private static ConfigEntry<float> window, second, third;
        private static ConfigEntry<string> rewards;
        private static readonly Vector3Config[] Offsets = new Vector3Config[3];
        private static string invalidRewardText;
        private static readonly HashSet<GameObject> ExtendedOpenings = new HashSet<GameObject>();
        private static readonly HashSet<string> AdditionalScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal const string TimedChestAddress = "RoR2/Base/TimedChest/TimedChest.prefab";
        private sealed class Vector3Config
        {
            internal ConfigEntry<float> X, Y, Z;
            internal Vector3 Value => new Vector3(ModConfig.Get(X), ModConfig.Get(Y), ModConfig.Get(Z));
        }

        internal static void Init()
        {
            const string section = "Stages - Stage 3 - Rallypoint Delta - Timed Chest";
            timer = ModConfig.BindBool(section, "Stage Arrival Timer", true, "After completing [REDACTED], the chest's timer starts when you enter the stage.");
            window = ModConfig.Bind(section, "Time Limit (Seconds)", 60f, new ConfigDescription("Time to open the chest after entering the stage once [REDACTED] is completed.", new AcceptableValueRange<float>(1f, 3600f)));
            reward = ModConfig.BindBool(section, "Replace Reward After Challenge", true, "Replace the Preon Accumulator reward for players who have completed [REDACTED].");
            rewards = ModConfig.Bind(section, "Reward Categories", "Random Green", "Random Equipment, Random Green, Random White, Random Yellow, Random Red. Separate multiple choices with commas; one is selected at random.");
            extra = ModConfig.BindBool(section, "Additional Timed Chests", true, "Keep one timed chest and roll for more at the remaining locations.");
            second = StageFeatureSpawnHelper.Percent(section, "Second Chest Chance (%)", 60f, "Chance to add a second chest. If this fails, no more chests are added.");
            third = StageFeatureSpawnHelper.Percent(section, "Third Chest Chance (%)", 30f, "Chance to add a third chest after the second chest spawns.");
            for (int i = 0; i < Offsets.Length; ++i)
            {
                string location = section + " - Location " + (char)('A' + i);
                Offsets[i] = new Vector3Config {
                    X = ModConfig.Bind(location, "Position Offset X", 0f, "X offset from this chest's normal location."),
                    Y = ModConfig.Bind(location, "Position Offset Y", 0f, "Height offset from this chest's normal location."),
                    Z = ModConfig.Bind(location, "Position Offset Z", 0f, "Z offset from this chest's normal location.") };
            }
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.RallypointDelta");
            harmony.Patch(AccessTools.Method(typeof(SceneObjectToggleGroup), "Awake"), prefix: new HarmonyMethod(typeof(RallypointDelta), nameof(BeforeToggleAwake)));
            harmony.Patch(AccessTools.Method(typeof(TimedChestController), "GetInteractability"), prefix: new HarmonyMethod(typeof(RallypointDelta), nameof(BeforeInteractability)));
            harmony.Patch(AccessTools.Property(typeof(TimedChestController), "remainingTime").GetGetMethod(true), postfix: new HarmonyMethod(typeof(RallypointDelta), nameof(AfterRemainingTime)));
            harmony.Patch(AccessTools.Method(typeof(TimedChestController), "OnInteractionBegin"), prefix: new HarmonyMethod(typeof(RallypointDelta), nameof(BeforeOpen)));
            harmony.Patch(AccessTools.Method(typeof(EntityStates.TimedChest.Opening), "FixedUpdate"), prefix: new HarmonyMethod(typeof(RallypointDelta), nameof(BeforeOpeningUpdate)));
            Run.onRunDestroyGlobal += run => ExtendedOpenings.Clear();
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += scene => ExtendedOpenings.Clear();
        }

        internal static void RegisterScene(string scene) => AdditionalScenes.Add(scene);

        internal static int ChestCount(int positions, Xoroshiro128Plus rng)
        {
            if (positions < 2 || !ModConfig.Get(extra) || !StageFeatureSpawnHelper.Roll(ModConfig.Get(second), rng))
                return 1;
            return positions >= 3 && StageFeatureSpawnHelper.Roll(ModConfig.Get(third), rng) ? 3 : 2;
        }

        private static bool Relevant(TimedChestController chest) => Run.instance && chest &&
            (chest.gameObject.scene.name == "frozenwall" || AdditionalScenes.Contains(chest.gameObject.scene.name));
        private static UnlockableDef ChallengeUnlock => RoR2Content.Equipment.BFG ? RoR2Content.Equipment.BFG.unlockableDef : null;
        private static bool Completed(Interactor activator)
        {
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            var user = body && body.master && body.master.playerCharacterMasterController ? body.master.playerCharacterMasterController.networkUser : null;
            return user && ChallengeUnlock && user.unlockables.Contains(ChallengeUnlock);
        }

        private static float Remaining() => ModConfig.Get(window) - (Stage.instance ? Stage.instance.entryTime.timeSince : 0f);

        private static void AfterRemainingTime(TimedChestController __instance, ref int __result)
        {
            if (!Relevant(__instance) || !ModConfig.Get(timer) || !ChallengeUnlock) return;
            foreach (LocalUser local in LocalUserManager.readOnlyLocalUsersList)
                if (local?.userProfile != null && local.userProfile.HasUnlockable(ChallengeUnlock))
                { __result = Mathf.CeilToInt(Remaining()); break; }
        }

        private static bool BeforeInteractability(TimedChestController __instance, Interactor activator, ref Interactability __result)
        {
            if (!Relevant(__instance)) return true;
            bool arrivalTimer = ModConfig.Get(timer) && Completed(activator);
            bool withinTime = arrivalTimer ? Remaining() > 0f : (int)(__instance.lockTime - Run.instance.GetRunStopwatch()) > 0;
            __result = __instance.purchased || activator.isRemoteOp ? Interactability.Disabled :
                withinTime ? Interactability.Available : Interactability.ConditionsNotMet;
            return false;
        }

        private static void BeforeToggleAwake(SceneObjectToggleGroup __instance)
        {
            if (!Run.instance || __instance.toggleGroups == null) return;
            string scene = __instance.gameObject.scene.name;
            bool rallypoint = scene == "frozenwall";
            if (!rallypoint && !AdditionalScenes.Contains(scene)) return;
            if (rallypoint) ApplyRallypointOffsets(__instance);
            if (!NetworkServer.active || !ModConfig.Get(extra)) return;
            for (int i = 0; i < __instance.toggleGroups.Length; ++i)
            {
                var group = __instance.toggleGroups[i];
                if (group.objects == null || group.objects.Length == 0 ||
                    Array.Exists(group.objects, obj => !IsTimedChestLocation(obj))) continue;
                group.minEnabled = group.maxEnabled = ChestCount(group.objects.Length, Run.instance.stageRng);
                __instance.toggleGroups[i] = group;
            }
        }

        private static bool IsTimedChestLocation(GameObject obj) => obj &&
            (obj.GetComponentInChildren<TimedChestController>(true) ||
             Tweaks.ModCompatibility.Stages.ModdedStageFeatures.HasAddressableSpawner(obj, TimedChestAddress));

        private static void ApplyRallypointOffsets(SceneObjectToggleGroup toggle)
        {
            foreach (var group in toggle.toggleGroups)
            {
                if (group.objects == null || group.objects.Length == 0) continue;
                var chests = new List<TimedChestController>();
                foreach (GameObject obj in group.objects)
                {
                    TimedChestController chest = obj ? obj.GetComponentInChildren<TimedChestController>(true) : null;
                    if (!chest) { chests.Clear(); break; }
                    chests.Add(chest);
                }
                if (chests.Count == 0) continue;
                for (int i = 0; i < chests.Count && i < Offsets.Length; ++i) chests[i].transform.position += Offsets[i].Value;
            }
        }

        private static List<List<PickupIndex>> RewardPools()
        {
            var pools = new List<List<PickupIndex>>();
            string text = ModConfig.Get(rewards);
            foreach (string value in text.Split(','))
            {
                List<PickupIndex> source;
                switch (value.Trim().Replace(" ", "").ToLowerInvariant())
                {
                    case "randomequipment": source = Run.instance.availableEquipmentDropList; break;
                    case "randomgreen": source = Run.instance.availableTier2DropList; break;
                    case "randomwhite": source = Run.instance.availableTier1DropList; break;
                    case "randomyellow": source = Run.instance.availableBossDropList; break;
                    case "randomred": source = Run.instance.availableTier3DropList; break;
                    default:
                        if (invalidRewardText != text) { Log.Warning("Timed chest: unrecognised reward category: " + value.Trim()); invalidRewardText = text; }
                        continue;
                }
                var allowed = new List<PickupIndex>();
                foreach (PickupIndex index in source) if (Run.instance.IsPickupAvailable(index)) allowed.Add(index);
                if (allowed.Count > 0) pools.Add(allowed);
            }
            return pools;
        }

        private static void BeforeOpeningUpdate(EntityStates.TimedChest.Opening __instance)
        {
            if (!NetworkServer.active) return;
            GameObject chest = AccessTools.Property(typeof(EntityStates.EntityState), "gameObject")?.GetValue(__instance, null) as GameObject;
            if (!chest || !ExtendedOpenings.Contains(chest)) return;
            object ageValue = AccessTools.Property(typeof(EntityStates.EntityState), "fixedAge")?.GetValue(__instance, null) ??
                AccessTools.Field(typeof(EntityStates.EntityState), "fixedAge")?.GetValue(__instance);
            if (!(ageValue is float age) || age < EntityStates.TimedChest.Opening.delayUntilUnlockAchievement) return;
            // Opening a completed player's extended-timer chest must not grant the
            // original ten-minute achievement to unfinished players in the same run.
            AccessTools.Field(typeof(EntityStates.TimedChest.Opening), "hasGrantedAchievement")?.SetValue(__instance, true);
            foreach (GenericPickupController pickup in chest.GetComponentsInChildren<GenericPickupController>(true)) pickup.enabled = true;
        }

        private static bool BeforeOpen(TimedChestController __instance, Interactor activator)
        {
            if (!NetworkServer.active || !Relevant(__instance)) return true;
            if (__instance.GetInteractability(activator) != Interactability.Available) return false;
            bool completed = Completed(activator);
            if (completed && ModConfig.Get(timer) && Run.instance.GetRunStopwatch() >= __instance.lockTime) ExtendedOpenings.Add(__instance.gameObject);
            if (!ModConfig.Get(reward) || !completed) return true;
            try
            {
                var pools = RewardPools();
                if (pools.Count == 0) { Log.Warning("Timed chest: no configured rewards are available; keeping the Preon reward."); return true; }
                var rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
                var pool = pools[rng.RangeInt(0, pools.Count)];
                PickupIndex index = pool[rng.RangeInt(0, pool.Count)];
                foreach (GenericPickupController pickup in __instance.GetComponentsInChildren<GenericPickupController>(true))
                    Stage3SpawnHelper.SetMember(pickup, "pickup", new UniquePickup(index));
            }
            catch (Exception e) { Log.Warning("Timed chest reward failed: " + e.Message); }
            return true;
        }
    }
}
