using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using BepInEx.Configuration;
using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class Teleporter
    {
        private static ConfigEntry<bool> enabled, fullSpeed;
        private static ConfigEntry<float> radius, duration, defeatedRadius, defeatedSpeed;

        internal static void Init()
        {
            const string section = "Interactables - Teleporter";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable Teleporter changes.");
            radius = Number(section, "Holdout Radius", 90f, 1f, 100000f,
                "Radius of the Teleporter holdout zone in metres. Vanilla: 60.");
            duration = Number(section, "Charge Time", 90f, 1f, 3600f,
                "Seconds required for normal Teleporter charging at full speed. Does not override DLC overcharging or its pauses. Vanilla: 90.");
            fullSpeed = ModConfig.BindBool(section, "Full Speed With One Player", false,
                "Charge at full speed with at least one player inside the holdout zone. Vanilla: False.");
            defeatedRadius = Number(section, "Radius After Boss Defeated", 10000f, 1f, 100000f,
                "Minimum holdout radius after all Teleporter bosses are defeated. 10000 covers effectively the entire map. Vanilla: no increase.");
            defeatedSpeed = Number(section, "Charge Speed After Boss Defeated", 1.5f, 0.01f, 100f,
                "Multiply normal charging speed after all Teleporter bosses are defeated. Does not affect the Soul-binding ritual or overcharging. Vanilla: 1.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.Teleporter");
            harmony.Patch(AccessTools.Method(typeof(TeleporterInteraction), "Start"),
                postfix: new HarmonyMethod(typeof(Teleporter), nameof(AfterStart)));
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "DoUpdate"),
                prefix: new HarmonyMethod(typeof(Teleporter), nameof(BeforeHoldoutUpdate)));
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "Start"),
                postfix: new HarmonyMethod(typeof(Teleporter), nameof(AfterHoldoutStart)));
            BossGroup.onBossGroupDefeatedServer += BossDefeated;
        }

        private static ConfigEntry<float> Number(string section, string key, float value, float minimum, float maximum, string description)
        {
            return ModConfig.Bind(section, key, value, new ConfigDescription(description,
                new AcceptableValueRange<float>(minimum, maximum)));
        }

        private static void AfterStart(TeleporterInteraction __instance)
        {
            HoldoutZoneController zone = __instance.holdoutZoneController;
            if (!zone || zone.GetComponent<Settings>()) return;
            var settings = zone.gameObject.AddComponent<Settings>();
            settingsInstances.Add(settings);
            settings.Owner = __instance;
            settings.Zone = zone;
            settings.OriginalRadius = zone.baseRadius;
            settings.OriginalDuration = zone.baseChargeDuration;
            settings.OriginalScaling = zone.playerCountScaling;
            zone.calcRadius += settings.CalculateRadius;
            zone.calcChargeRate += settings.CalculateRate;
            settings.Squad = __instance.bossGroup ? __instance.bossGroup.combatSquad : null;
            if (settings.Squad)
            {
                settings.Squad.onMemberDiscovered += settings.MemberDiscovered;
                settings.Squad.onMemberLost += settings.MemberLost;
            }
            settings.Configure();
        }

        private static void BeforeHoldoutUpdate(HoldoutZoneController __instance)
        {
            Settings settings = __instance.GetComponent<Settings>();
            if (settings) settings.Configure();
        }

        private static void AfterHoldoutStart(HoldoutZoneController __instance)
        {
            // Focused Convergence registers during the holdout's Start, which can run
            // after the teleporter's Start. Apply the configured minimum after it.
            Settings settings = __instance.GetComponent<Settings>();
            if (!settings) return;
            __instance.calcRadius -= settings.CalculateRadius;
            __instance.calcRadius += settings.CalculateRadius;
        }

        private static readonly System.Collections.Generic.HashSet<Settings> settingsInstances = new System.Collections.Generic.HashSet<Settings>();

        private static void BossDefeated(BossGroup group)
        {
            if (!NetworkServer.active || !group) return;
            // The Soul-binding trial creates a separate, networked boss group.
            // Ask each installed teleporter to match its own current encounter.
            foreach (Settings settings in settingsInstances)
                if (settings) settings.RecordDefeat(group);
        }

        internal sealed class Settings : MonoBehaviour
        {
            internal TeleporterInteraction Owner;
            internal HoldoutZoneController Zone;
            internal CombatSquad Squad;
            internal float OriginalRadius, OriginalDuration, OriginalScaling;
            internal bool Defeated;

            internal enum Phase { Normal, Interlude, Overcharge }
            private object currentState;
            private Phase phase;
            private BossGroup extraGroup;
            private CombatSquad extraSquad;
            private bool extraSeen, extraDefeated;
            private float nextClientSearch;
            private static readonly Type overchargeType = typeof(TeleporterInteraction).Assembly.GetType("EntityStates.TeleporterCatastrophe.DLC4.Teleporter.OverchargingState");
            private static readonly FieldInfo extraGroupField = overchargeType == null ? null : AccessTools.Field(overchargeType, "extraBossGroupServer");
            private static readonly FieldInfo extraPrefabField = overchargeType == null ? null : AccessTools.Field(overchargeType, "extraBossGroupPrefab");

            internal static Phase Classify(string stateName) => stateName == "EntityStates.TeleporterCatastrophe.DLC4.Teleporter.OverchargingState" ? Phase.Overcharge : stateName == "EntityStates.TeleporterCatastrophe.DLC4.Teleporter.OverchargingInterludeState" ? Phase.Interlude : Phase.Normal;
            internal static float DurationForPhase(Phase phase, float current, bool active, float configured, float original) => phase == Phase.Normal ? (active ? configured : original) : current;
            internal static float RateAfterBoss(float rate, bool afterBoss, float multiplier) => afterBoss && rate > 0 ? rate * multiplier : rate;
            internal static float RateForPhase(Phase phase, float rate, bool afterBoss, float multiplier) => RateAfterBoss(rate, afterBoss && phase == Phase.Normal, multiplier);
            internal static bool BossBonusReady(Phase phase, bool normalDefeated, bool seenExtra, bool defeatedExtra) => phase == Phase.Normal ? normalDefeated : phase == Phase.Overcharge && seenExtra && defeatedExtra;
            private bool AfterBoss
            {
                get
                {
                    RefreshEncounter();
                    return ModConfig.Get(Teleporter.enabled) && Owner && Owner.isCharging && BossBonusReady(phase, Defeated, extraSeen, extraDefeated);
                }
            }

            private void RefreshEncounter()
            {
                object state = Owner && Owner.mainStateMachine ? Owner.mainStateMachine.state : null;
                if (!ReferenceEquals(currentState, state))
                {
                    UnwatchExtra(); currentState = state; phase = Classify(state?.GetType().FullName);
                    extraSeen = false; extraDefeated = false; nextClientSearch = 0;
                }
                if (phase != Phase.Overcharge || state == null) return;
                BossGroup group = extraGroupField?.GetValue(state) as BossGroup;
                if (!group && !extraGroup && Time.unscaledTime >= nextClientSearch)
                {
                    // The server-only state field is absent on clients. Match the
                    // network-spawned group against this state's exact boss prefab.
                    nextClientSearch = Time.unscaledTime + .25f;
                    var prefab = extraPrefabField?.GetValue(state) as GameObject;
                    if (prefab)
                        foreach (BossGroup candidate in InstanceTracker.GetInstancesList<BossGroup>())
                            if (candidate && candidate != Owner.bossGroup && candidate.gameObject.name == prefab.name + "(Clone)") { group = candidate; break; }
                }
                if (group && group != extraGroup)
                {
                    UnwatchExtra(); extraGroup = group; extraSquad = group.combatSquad;
                    if (extraSquad)
                    {
                        extraSquad.onMemberDiscovered += ExtraDiscovered;
                        extraSquad.onMemberLost += ExtraLost;
                    }
                }
                if (extraSquad && extraSquad.memberCount > 0) { extraSeen = true; extraDefeated = false; }
            }

            internal void RecordDefeat(BossGroup group)
            {
                RefreshEncounter();
                if (!Owner || !Owner.isCharging) return;
                if (phase == Phase.Normal && group == Owner.bossGroup) Defeated = true;
                else if (phase == Phase.Overcharge && group == extraGroup) { extraSeen = true; extraDefeated = true; }
            }
            private void ExtraDiscovered(CharacterMaster master) { extraSeen = true; extraDefeated = false; }
            private void ExtraLost(CharacterMaster master)
            {
                if (!NetworkServer.active && extraSeen && extraSquad && extraSquad.memberCount == 0) extraDefeated = true;
            }
            private void UnwatchExtra()
            {
                if (extraSquad)
                {
                    extraSquad.onMemberDiscovered -= ExtraDiscovered;
                    extraSquad.onMemberLost -= ExtraLost;
                }
                extraGroup = null; extraSquad = null;
            }

            internal void Configure()
            {
                if (!Zone) return;
                RefreshEncounter();
                bool active = ModConfig.Get(Teleporter.enabled);
                Zone.baseRadius = active ? ModConfig.Get(radius) : OriginalRadius;
                // OverchargingState owns this value, including its initial
                // infinity pause and the faster rate after that pause ends.
                Zone.baseChargeDuration = DurationForPhase(phase, Zone.baseChargeDuration, active, ModConfig.Get(duration), OriginalDuration);
                Zone.playerCountScaling = active && ModConfig.Get(fullSpeed) ? 0f : OriginalScaling;
            }

            internal void CalculateRadius(ref float value)
            {
                if (AfterBoss) value = Mathf.Max(value, ModConfig.Get(defeatedRadius));
            }

            internal void CalculateRate(ref float value)
            {
                // Keep native player presence checks and other charge modifiers intact.
                bool afterBoss = AfterBoss; // Refresh the current encounter before testing its phase.
                value = RateForPhase(phase, value, afterBoss, ModConfig.Get(defeatedSpeed));
            }

            internal void MemberDiscovered(CharacterMaster master)
            {
                Defeated = false;
            }

            internal void MemberLost(CharacterMaster master)
            {
                // Clients observe the native replicated squad for the matching radius display.
                // The server waits for the authoritative whole-group defeat event.
                if (!NetworkServer.active && Owner && Owner.isCharging && Squad && Squad.memberCount == 0)
                    Defeated = true;
            }

            private void OnDestroy()
            {
                settingsInstances.Remove(this);
                UnwatchExtra();
                if (Zone)
                {
                    Zone.calcRadius -= CalculateRadius;
                    Zone.calcChargeRate -= CalculateRate;
                }
                if (Squad)
                {
                    Squad.onMemberDiscovered -= MemberDiscovered;
                    Squad.onMemberLost -= MemberLost;
                }
            }
        }
    }
}
