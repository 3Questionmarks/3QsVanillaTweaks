using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    /// <summary>Logs real projectile prefab references used shortly after player skill activations.</summary>
    internal static class SkillPrefabs
    {
        private sealed class SkillWindow
        {
            internal int Number;
            internal string SkillName;
            internal string StateName;
            internal float Started;
            internal int Projectiles;
            internal readonly HashSet<string> Seen = new HashSet<string>();
        }

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<int> captureSeconds;
        private static ConfigEntry<bool> logRepeatedProjectiles;
        private static ConfigEntry<bool> inspectDrifterProjectile;
        private static readonly HashSet<int> inspectedDrifterPrefabs = new HashSet<int>();
        private static readonly Dictionary<GameObject, SkillWindow> recent = new Dictionary<GameObject, SkillWindow>();
        private static int nextWindow;

        internal static void Init()
        {
            enabled = C.BindBool("Debug - Skill Prefabs", "Log Player Skill Projectiles", true,
                "Log projectile prefab and ghost names observed after any player skill activation. Requires Debug Options. Hitscan and non-projectile skills may not fire a projectile.", clientSide: true);
            captureSeconds = C.Bind("Debug - Skill Prefabs", "Skill Prefab Capture Seconds", 5,
                "Log projectiles fired for this many seconds after a skill activation (default 5). Long windows may include unrelated projectiles.", clientSide: true);
            logRepeatedProjectiles = C.BindBool("Debug - Skill Prefabs", "Include Repeated Projectiles", false,
                "Log each projectile fired, even when the same prefab has already been recorded for the current skill. Useful for testing multi-shot skills.", clientSide: true);

            inspectDrifterProjectile = C.BindBool("Debug - Skill Prefabs", "Inspect Drifter Junk Projectile", true,
                "When Drifter fires Cleanup, log the shotgun projectile prefab's components, renderers, child hierarchy, and referenced visual effects once. Requires Debug Options.", clientSide: true);

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.SkillPrefabs");
            var activated = AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.OnSkillActivated),
                new[] { typeof(GenericSkill) });
            if (activated != null)
                harmony.Patch(activated, postfix: new HarmonyMethod(typeof(SkillPrefabs), nameof(Activated)));
            else
                ThreeQsVanillaTweaks.Log.Warning("[Skill Prefabs] Could not hook CharacterBody.OnSkillActivated(GenericSkill).");

            // Inspect the actual serialized projectile assigned to Drifter's Cleanup
            // state. The FireProjectile callback alone cannot identify its field origin.
            var cleanupEnter = AccessTools.Method(typeof(EntityStates.Drifter.AimMagicBag), "OnEnter");
            if (cleanupEnter != null)
                harmony.Patch(cleanupEnter, postfix: new HarmonyMethod(typeof(SkillPrefabs), nameof(DumpDrifterCleanup)));

            var fired = AccessTools.Method(typeof(ProjectileManager), nameof(ProjectileManager.FireProjectile),
                new[] { typeof(FireProjectileInfo) });
            if (fired != null)
                harmony.Patch(fired, prefix: new HarmonyMethod(typeof(SkillPrefabs), nameof(FiredProjectile)));
            else
                ThreeQsVanillaTweaks.Log.Warning("[Skill Prefabs] Could not hook ProjectileManager.FireProjectile(FireProjectileInfo).");
        }

        private static bool Active => C.DebugOptionsEnabled && C.Get(enabled);

        private static void Activated(CharacterBody __instance, GenericSkill skill)
        {
            if (!Active || !__instance || !__instance.isPlayerControlled || !skill) return;

            var definition = skill.skillDef;
            string name = definition ? definition.skillName : "<unknown>";
            string state = definition && definition.activationState.stateType != null
                ? definition.activationState.stateType.FullName : "<unknown>";
            var window = new SkillWindow
            {
                Number = ++nextWindow,
                SkillName = name,
                StateName = state,
                Started = Time.realtimeSinceStartup
            };
            recent[__instance.gameObject] = window;
            ThreeQsVanillaTweaks.Log.Info($"[Skill Prefabs] #{window.Number} START body={__instance.name} skill={name} state={state}");
        }

        private static void DumpDrifterCleanup(EntityStates.Drifter.AimMagicBag __instance)
        {
            if (!Active || __instance == null || !__instance.characterBody ||
                !__instance.characterBody.isPlayerControlled) return;

            // These fields are assigned to the vanilla state by its serialized assets.
            // Log the actual runtime object references rather than guessing asset paths.
            GameObject projectile = __instance.shotgunShotPrefab;
            GameObject muzzle = __instance.shotgunMuzzleFlashPrefab;
            var controller = projectile ? projectile.GetComponent<ProjectileController>() : null;
#pragma warning disable CS0618
            GameObject ghost = controller ? controller.ghostPrefab : null;
#pragma warning restore CS0618
            InspectDrifterProjectile(projectile, "AimMagicBag.shotgunShotPrefab");
            ThreeQsVanillaTweaks.Log.Info(
                $"[Skill Prefabs] DRIFTER AIM MAGIC BAG serialized shotgunShotPrefab={(projectile ? projectile.name : "<null>")} " +
                $"shotgunMuzzleFlashPrefab={(muzzle ? muzzle.name : "<null>")} " +
                $"ghost={(ghost ? ghost.name : "<null>")} " +
                $"projectileController={(controller ? "yes" : "no")}");
        }

        private static void FiredProjectile(FireProjectileInfo __0)
        {
            if (!Active || !__0.owner) return;
            CharacterBody body = __0.owner.GetComponent<CharacterBody>()
                ?? __0.owner.GetComponentInParent<CharacterBody>();
            if (!body || !body.isPlayerControlled) return;
            if (!recent.TryGetValue(body.gameObject, out SkillWindow window)) return;

            float elapsed = Time.realtimeSinceStartup - window.Started;
            if (elapsed < 0f || elapsed > Mathf.Max(1, C.Get(captureSeconds))) return;

            GameObject prefab = __0.projectilePrefab;
            ProjectileController controller = prefab ? prefab.GetComponent<ProjectileController>() : null;
#pragma warning disable CS0618
            GameObject ghost = controller ? controller.ghostPrefab : null;
#pragma warning restore CS0618
            InspectDrifterProjectile(prefab, "ProjectileManager.FireProjectile");
            string prefabName = prefab ? prefab.name : "<null>";
            string ghostName = ghost ? ghost.name : "<null>";
            string identity = prefabName + "|" + ghostName;
            window.Projectiles++;
            if (!C.Get(logRepeatedProjectiles) && !window.Seen.Add(identity)) return;

            ThreeQsVanillaTweaks.Log.Info(
                $"[Skill Prefabs] #{window.Number} +{elapsed:F2}s skill={window.SkillName} " +
                $"projectile={prefabName} ghost={ghostName} owner={__0.owner.name} " +
                $"projectileController={(controller ? "yes" : "no")} count={window.Projectiles}");
        }

        private static void InspectDrifterProjectile(GameObject prefab, string source)
        {
            if (!Active || !C.Get(inspectDrifterProjectile) || !prefab ||
                prefab.name.IndexOf("DrifterJunkBallette", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            if (!inspectedDrifterPrefabs.Add(prefab.GetInstanceID())) return;

            var report = new StringBuilder(2048);
            report.AppendLine($"[Skill Prefabs] DRIFTER PROJECTILE INSPECTION source={source} prefab={prefab.name} instanceID={prefab.GetInstanceID()}");
            DumpObject(prefab.transform, report, 0);
            ThreeQsVanillaTweaks.Log.Info(report.ToString());
        }

        private static void DumpObject(Transform transform, StringBuilder report, int depth)
        {
            if (!transform || depth > 12) return;
            GameObject gameObject = transform.gameObject;
            report.Append(' ', depth * 2).Append("Object: ").Append(gameObject.name)
                .Append(" activeSelf=").Append(gameObject.activeSelf)
                .Append(" layer=").Append(gameObject.layer).AppendLine();

            foreach (Component component in gameObject.GetComponents<Component>())
            {
                if (!component)
                {
                    report.Append(' ', depth * 2 + 2).AppendLine("<missing component>");
                    continue;
                }
                report.Append(' ', depth * 2 + 2).Append("Component: ")
                    .Append(component.GetType().FullName);

                Renderer renderer = component as Renderer;
                if (renderer)
                {
                    report.Append(" enabled=").Append(renderer.enabled);
                    var material = renderer.sharedMaterial;
                    report.Append(" material=").Append(material ? material.name : "<null>");
                }
                MeshFilter meshFilter = component as MeshFilter;
                if (meshFilter)
                    report.Append(" mesh=").Append(meshFilter.sharedMesh ? meshFilter.sharedMesh.name : "<null>");
                ParticleSystem particleSystem = component as ParticleSystem;
                if (particleSystem)
                    report.Append(" playing=").Append(particleSystem.isPlaying);
                report.AppendLine();

                // Only inspect references relevant to projectile appearance. Do not
                // evaluate arbitrary properties, which can have runtime side effects.
                for (Type type = component.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Behaviour) && type != typeof(Component); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(UnityEngine.Object).IsAssignableFrom(field.FieldType)) continue;
                        string name = field.Name;
                        if (name.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf("visual", StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf("prefab", StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf("tracer", StringComparison.OrdinalIgnoreCase) < 0 &&
                            name.IndexOf("mesh", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        try
                        {
                            var value = field.GetValue(component) as UnityEngine.Object;
                            report.Append(' ', depth * 2 + 4).Append(name).Append(" = ")
                                .AppendLine(value ? value.name : "<null>");
                        }
                        catch (Exception ex)
                        {
                            report.Append(' ', depth * 2 + 4).Append(name).Append(" = <read failed: ")
                                .Append(ex.GetType().Name).AppendLine(">");
                        }
                    }
                }
            }

            for (int i = 0; i < transform.childCount; i++)
                DumpObject(transform.GetChild(i), report, depth + 1);
        }
    }
}
