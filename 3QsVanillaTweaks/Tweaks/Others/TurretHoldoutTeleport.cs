using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    // Allied Gunner Turrets move into a holdout zone (teleporter, Void Fields cells and so on) when it starts.
    internal static class TurretHoldoutTeleport
    {
        private const string Section = "Other - Holdout Turrets";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<string> bodies;
        private static ConfigEntry<float> minimumDistance, maximumDistance;
        private sealed class Started { }
        private static readonly ConditionalWeakTable<HoldoutZoneController, Started> started =
            new ConditionalWeakTable<HoldoutZoneController, Started>();
        private static readonly PropertyInfo fullyCharged = A.Property(typeof(HoldoutZoneController), "isFullyCharged");

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enabled", true,
                "Teleport your Gunner Turrets into a holdout zone when it starts, including Captain beacon zones. Vanilla: false.");
            bodies = C.Bind(Section, "Turret Bodies", "Turret1Body",
                "Comma-separated body names that are teleported. Turret1Body is the Gunner Turret; add EngiTurretBody to include Engineer turrets.");
            minimumDistance = C.Bind(Section, "Minimum Distance From Centre", 4f, new ConfigDescription(
                "Closest a turret is placed to the zone's centre, in metres.", new AcceptableValueRange<float>(0f, 100f)));
            maximumDistance = C.Bind(Section, "Maximum Distance From Centre", 10f, new ConfigDescription(
                "Furthest a turret is placed from the zone's centre, in metres. Never more than 60% of the zone's radius.", new AcceptableValueRange<float>(0f, 100f)));
            if (!enabled.Value) return;
            var update = A.Method(typeof(HoldoutZoneController), "DoUpdate")
                ?? A.Method(typeof(HoldoutZoneController), "FixedUpdate")
                ?? A.Method(typeof(HoldoutZoneController), "Update");
            if (update == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Holdout turrets: holdout zone update not found; turrets will not teleport.");
                return;
            }
            new Harmony("3Questionmarks.3QsVanillaTweaks.Others.TurretHoldoutTeleport").Patch(update,
                postfix: new HarmonyMethod(typeof(TurretHoldoutTeleport), nameof(AfterZoneUpdate)));
        }

        private static bool FullyCharged(HoldoutZoneController zone) =>
            fullyCharged != null && fullyCharged.PropertyType == typeof(bool) ? (bool)fullyCharged.GetValue(zone, null) : zone.charge >= 1f;

        private static void AfterZoneUpdate(HoldoutZoneController __instance)
        {
            // The first update of an enabled, uncharged zone is when it starts.
            if (!NetworkServer.active || !C.Get(enabled) || !__instance || !__instance.enabled || FullyCharged(__instance)) return;
            if (started.TryGetValue(__instance, out _)) return;
            started.Add(__instance, new Started());
            MoveTurrets(__instance);
        }

        private static HashSet<string> TurretNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (C.Get(bodies) ?? "").Split(','))
                if (part.Trim().Length > 0) names.Add(part.Trim());
            return names;
        }

        private static void MoveTurrets(HoldoutZoneController zone)
        {
            var names = TurretNames();
            var turrets = new List<CharacterBody>();
            foreach (var body in CharacterBody.readOnlyInstancesList)
                if (body && body.teamComponent && body.teamComponent.teamIndex == TeamIndex.Player &&
                    body.healthComponent && body.healthComponent.alive &&
                    names.Contains(BodyCatalog.GetBodyName(body.bodyIndex) ?? ""))
                    turrets.Add(body);
            if (turrets.Count == 0) return;

            Vector3 centre = zone.transform.position;
            float radius = Mathf.Max(zone.baseRadius, zone.currentRadius);
            float maximum = Mathf.Max(0f, C.Get(maximumDistance));
            if (radius > 0f) maximum = Mathf.Min(maximum, radius * .6f);
            float minimum = Mathf.Min(Mathf.Max(0f, C.Get(minimumDistance)), maximum);
            var rng = new Xoroshiro128Plus(Run.instance ? Run.instance.stageRng.nextUlong : 0UL);
            float baseAngle = rng.RangeFloat(0f, Mathf.PI * 2f);

            for (int i = 0; i < turrets.Count; i++)
            {
                // Spread turrets evenly around the centre, then search nearby for clear ground.
                float angle = baseAngle + i * Mathf.PI * 2f / turrets.Count;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float tryAngle = angle + attempt * 2.399963f / turrets.Count;
                    float distance = Mathf.Lerp(minimum, maximum, rng.nextNormalizedFloat);
                    Vector3 candidate = centre + new Vector3(Mathf.Cos(tryAngle), 0f, Mathf.Sin(tryAngle)) * distance;
                    if (!Physics.Raycast(candidate + Vector3.up * 8f, Vector3.down, out RaycastHit ground, 20f,
                        LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    candidate = ground.point + Vector3.up * .15f;
                    if (Physics.CheckCapsule(candidate + Vector3.up * .5f, candidate + Vector3.up * 2f, .5f,
                        LayerIndex.world.mask, QueryTriggerInteraction.Ignore)) continue;
                    TeleportHelper.TeleportBody(turrets[i], candidate, false);
                    break;
                }
            }
        }
    }
}
