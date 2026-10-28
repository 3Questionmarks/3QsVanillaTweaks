using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using BepInEx.Configuration;
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
                "Seconds required to fully charge the Teleporter at full speed. Vanilla: 90.");
            fullSpeed = ModConfig.BindBool(section, "Full Speed With One Player", false,
                "Charge at full speed with at least one player inside the holdout zone. Vanilla: False.");
            defeatedRadius = Number(section, "Radius After Boss Defeated", 10000f, 1f, 100000f,
                "Minimum holdout radius after all Teleporter bosses are defeated. 10000 covers effectively the entire map. Vanilla: no increase.");
            defeatedSpeed = Number(section, "Charge Speed After Boss Defeated", 2f, 0.01f, 100f,
                "Multiply the charge speed after all Teleporter bosses are defeated. Vanilla: 1.");

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

        private static void BossDefeated(BossGroup group)
        {
            if (!NetworkServer.active || !group) return;
            TeleporterInteraction owner = group.GetComponent<TeleporterInteraction>();
            if (!owner || owner.bossGroup != group || !owner.isCharging || !owner.holdoutZoneController) return;
            Settings settings = owner.holdoutZoneController.GetComponent<Settings>();
            if (settings) settings.Defeated = true;
        }

        internal sealed class Settings : MonoBehaviour
        {
            internal TeleporterInteraction Owner;
            internal HoldoutZoneController Zone;
            internal CombatSquad Squad;
            internal float OriginalRadius, OriginalDuration, OriginalScaling;
            internal bool Defeated;

            private bool AfterBoss => ModConfig.Get(Teleporter.enabled) && Owner && Owner.isCharging && Defeated;

            internal void Configure()
            {
                if (!Zone) return;
                bool active = ModConfig.Get(Teleporter.enabled);
                Zone.baseRadius = active ? ModConfig.Get(radius) : OriginalRadius;
                Zone.baseChargeDuration = active ? ModConfig.Get(duration) : OriginalDuration;
                Zone.playerCountScaling = active && ModConfig.Get(fullSpeed) ? 0f : OriginalScaling;
            }

            internal void CalculateRadius(ref float value)
            {
                if (AfterBoss) value = Mathf.Max(value, ModConfig.Get(defeatedRadius));
            }

            internal void CalculateRate(ref float value)
            {
                // Keep native player presence checks and other charge modifiers intact.
                if (AfterBoss && value > 0f) value *= ModConfig.Get(defeatedSpeed);
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
