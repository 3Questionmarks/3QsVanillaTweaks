using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteDroneStats
    {
        private const string Root = "Remote Operation - Stats";
        private static ConfigEntry<bool> enabled;
        private static readonly List<Profile> profiles = new List<Profile>();

        private sealed class Profile
        {
            internal readonly CharacterBody Template;
            internal readonly S.Stats Stats;
            internal readonly BodyIndex BodyIndex;
            private readonly ConfigEntry<float> visionDistance;
            private readonly ConfigEntry<float> sprintSpeedMultiplier;

            internal Profile(string section, GameObject prefab, Dictionary<string, float> defaults)
            {
                if (!prefab) throw new InvalidOperationException("Player-controlled drone body is unavailable: " + section);
                Template = prefab.GetComponent<CharacterBody>();
                if (!Template) throw new MissingComponentException(section + " has no CharacterBody.");
                BodyIndex = Template.bodyIndex;
                Stats = new S.Stats(section, Template, defaults);
                visionDistance = S.Float(section, "Vision Distance", Template.baseVisionDistance,
                    Template.baseVisionDistance, "Maximum targeting and visibility distance in metres.");
                sprintSpeedMultiplier = S.Float(section, "Sprint Speed Multiplier", Template.sprintingSpeedMultiplier,
                    Template.sprintingSpeedMultiplier, "Movement speed multiplier while sprinting.");
            }

            internal void Apply(CharacterBody body)
            {
                if (!body) return;
                body.autoCalculateLevelStats = false;
                Stats.Apply(body);
                body.baseVisionDistance = C.Get(visionDistance);
                body.sprintingSpeedMultiplier = C.Get(sprintSpeedMultiplier);

                // Remote-operation prefabs inherit regeneration at 30% of the
                // ordinary drone value. Configured base stats replace that
                // inherited value, so remove only the Regen inheritance flag.
                CharacterBodyInheritStats inherited = body.GetComponent<CharacterBodyInheritStats>();
                if (inherited)
                    inherited.inheritStatTypes &= ~CharacterBodyInheritStats.StatType.Regen;
                body.MarkAllStatsDirty();
            }
        }

        internal static void Init()
        {
            enabled = C.BindStartupBool(Root, "Override Player-Controlled Drone Stats", true,
                "Use configurable base stats for player-controlled drones and remove their native 70% regeneration reduction. AI-controlled drones are unchanged.");
            if (!enabled.Value) return;
            RoR2Application.onLoad += BindAndApply;
        }

        private static void BindAndApply()
        {
            // Resolve only player-controlled drone prefabs. Native AI drone bodies are never modified.
            // Candidate names are checked first; newer DLC builds may use slightly different
            // internal names, so also match the remote body name and its display token.
            Add("Gunner Drone", "Drone1", 85f, 25f, 1f, .2f, 7f, 1.6f);
            Add("Healing Drone", "Drone2", 95f, 30f, 1.5f, .3f, 6f, 1.5f);
            Add("Junk Drone", "JunkDrone", 200f, 60f, 1f, .2f, 9f, 1.8f);
            Add("Emergency Drone", "EmergencyDrone", 200f, 60f, 1.5f, .3f, 10f, 2f);
            Add("Incinerator Drone", "FlameDrone", 180f, 50f, 1f, .2f, 10f, 2f);
            Add("Cleanup Drone", "CleanupDrone", 180f, 50f, 1f, .2f, 10f, 2f);
            Add("Missile Drone", "MissileDrone", 150f, 35f, 1.5f, .5f, 12f, 2f);
            Add("Jailer Drone", "JailerDrone", 250f, 75f, 1f, .2f, 11f, 1.8f);
            Add("Barrier Drone", "RechargeDrone", 230f, 60f, 1f, .2f, 10f, 2f);
            Add("Bombardment Drone", "BombardmentDrone", 220f, 70f, 1f, .2f, 10f, 2f);
            Add("Freeze Drone", "FreezeDrone", 220f, 70f, 1f, .2f, 10f, 2f);
            Add("TC-280 Prototype", "MegaDrone", 600f, 180f, 2f, .4f, 14f, 2.8f);

            ApplyAll();
            C.EffectiveSettingsChanged += ApplyAll;
        }

        private static void Add(string displayName, string internalName, float health, float healthLevel,
            float regen, float regenLevel, float damage, float damageLevel)
        {
            GameObject prefab = ResolveBody(displayName, internalName);
            if (!prefab)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote drone stats: unable to locate the player-controlled body for " +
                    displayName + "; skipping its stat override.");
                return;
            }
            profiles.Add(new Profile(Root + " - " + displayName, prefab, new Dictionary<string, float>
            {
                ["baseMaxHealth"] = health,
                ["levelMaxHealth"] = healthLevel,
                ["baseRegen"] = regen,
                ["levelRegen"] = regenLevel,
                ["baseDamage"] = damage,
                ["levelDamage"] = damageLevel
            }));
        }

        private static GameObject ResolveBody(string displayName, string internalName)
        {
            // The Gunner, Healing and Junk names are already verified in the current mod.
            GameObject exact = BodyCatalog.FindBodyPrefab(internalName + "BodyRemoteOp");
            if (exact) return exact;
            // Different releases may use "DroneBody" and similar naming conventions.
            var all = BodyCatalog.allBodyPrefabs;
            if (all == null) return null;
            foreach (GameObject prefab in all)
            {
                if (!prefab || prefab.name.IndexOf("RemoteOp", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (prefab.name.IndexOf(internalName, StringComparison.OrdinalIgnoreCase) >= 0) return prefab;
            }
            foreach (GameObject prefab in all)
            {
                if (!prefab || prefab.name.IndexOf("RemoteOp", StringComparison.OrdinalIgnoreCase) < 0) continue;
                CharacterBody body = prefab.GetComponent<CharacterBody>();
                if (body != null && string.Equals(Language.GetString(body.baseNameToken), displayName,
                    StringComparison.OrdinalIgnoreCase)) return prefab;
            }
            return null;
        }

        private static void ApplyAll()
        {
            foreach (Profile profile in profiles) profile.Apply(profile.Template);
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                foreach (Profile profile in profiles)
                    if (body && body.bodyIndex == profile.BodyIndex) { profile.Apply(body); break; }
        }
    }
}
