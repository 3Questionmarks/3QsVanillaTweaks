using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteDroneRevival
    {
        private const string Section = "Remote Operation - General";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> protection;

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Revive Drone Players On Boss Defeat", true,
                "Return players controlling Remote Operation drones to their original survivors when the Teleporter finishes charging, including overcharge, or when a supported standalone hidden-realm boss is defeated. Vanilla: False.");
            protection = C.Bind(Section, "Boss Revival Invulnerability (s)", 3f,
                new ConfigDescription("Seconds of invulnerability after returning from a Remote Operation drone.",
                    new AcceptableValueRange<float>(0f, 60f)));
            TeleporterInteraction.onTeleporterChargedGlobal += OnTeleporterCharged;
            BossGroup.onBossGroupDefeatedServer += OnBossDefeated;
        }

        private static bool IsHiddenRealmEncounter(BossGroup group)
        {
            if (!group || !Run.instance) return false;
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            if (teleporter && (teleporter.bossGroup == group || teleporter.isCharging)) return false;

            // Only accept standalone boss encounters in their designated hidden realms.
            // Merely defeating an incidental boss in a normal stage is not sufficient.
            SceneDef scene = SceneInfo.instance ? SceneInfo.instance.sceneDef : null;
            if (!scene) return false;
            string sceneName = (scene.baseSceneName ?? string.Empty).ToLowerInvariant();
            if (sceneName == "goldshores" || sceneName == "goldshoresnight") return true;
            // DLC3's Soulbound Threshold can use a different scene identifier across builds;
            // accept only its dedicated boss group in a matching scene.
            if (sceneName.Contains("soulbound") || sceneName.Contains("soulthreshold")) return true;
            return false;
        }

        private static Vector3 GroundBelowDrone(CharacterBody drone, CharacterMaster master)
        {
            // Restrict the raycast to world geometry: hitting the drone itself or
            // an enemy should not determine the survivor's spawn position.
            Vector3 origin = drone.transform.position + Vector3.up * 2f;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, 1000f,
                LayerIndex.world.mask, QueryTriggerInteraction.Ignore) &&
                Vector3.Angle(hit.normal, Vector3.up) <= 55f)
            {
                return hit.point;
            }

            // No usable ground directly underneath (e.g. void or steep terrain).
            return master.CalculateSafeRespawnPosition();
        }

        private static void OnTeleporterCharged(TeleporterInteraction teleporter)
        {
            if (teleporter && teleporter == TeleporterInteraction.instance)
                ReviveDronePlayers();
        }

        private static void OnBossDefeated(BossGroup group)
        {
            if (IsHiddenRealmEncounter(group))
                ReviveDronePlayers();
        }

        private static void ReviveDronePlayers()
        {
            if (!NetworkServer.active || !Run.instance || enabled == null || !C.Get(enabled)) return;
            // Snapshot avoids invalidation if a respawn fires master/body lifecycle events.
            CharacterMaster[] masters = new CharacterMaster[CharacterMaster.readOnlyInstancesList.Count];
            CharacterMaster.readOnlyInstancesList.CopyTo(masters, 0);
            foreach (CharacterMaster master in masters)
            {
                if (!master || !master.playerCharacterMasterController) continue;
                CharacterBody drone = master.GetBody();
                if (!drone || !drone.isRemoteOp || !master.originalBodyPrefab) continue;

                // Revive beneath the currently controlled drone rather than at the
                // survivor's previous death location. Fall back if no safe ground is found.
                Vector3 position = GroundBelowDrone(drone, master);
                var args = new CharacterMaster.RespawnArgs
                {
                    spawnBodyArgs = new CharacterMaster.SpawnBodyArgs
                    {
                        spawnPointPosition = position,
                        spawnPointRotation = Quaternion.identity,
                        spawnPointIsFootPos = true
                    },
                    wasRevivedMidStage = true,
                    ignoreMetamorphosis = true
                };
                try
                {
                    CharacterBody restored = master.Respawn(in args);
                    if (restored && restored.healthComponent)
                    {
                        // Mid-stage respawn uses the normal CharacterMaster body handling path.
                        if (C.Get(protection) > 0f)
                            restored.AddTimedBuff(RoR2Content.Buffs.Immune, C.Get(protection));
                    }
                    else ThreeQsVanillaTweaks.Log.Warning("Remote Operation revival failed for " + master.name);
                }
                catch (Exception ex)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Remote Operation revival failed: " + ex);
                }
            }
        }
    }
}
