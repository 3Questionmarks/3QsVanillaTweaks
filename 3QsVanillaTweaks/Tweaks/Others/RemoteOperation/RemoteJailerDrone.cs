using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    // All state machines and the projectile are installed on the RemoteOp body only.
    internal static class RemoteJailerDrone
    {
        private const string Root = RemoteDroneSkills.JailerSection;
        private const string P = "3QSJailerPrimary", S = "3QSJailerSecondary",
            U = "3QSJailerUtility", X = "3QSJailerSpecial";
        internal static readonly Type[] States = { typeof(RestraintBolt), typeof(Lockdown),
            typeof(PrisonTransfer), typeof(ContainmentCage) };
        private static ConfigEntry<bool> enabled, primaryOn, secondaryOn, utilityOn, specialOn;
        private static ConfigEntry<float> boltDamage, boltBounceLoss, boltBounceRange, boltInterval, boltSpeed, boltLifetime,
            boltHitRadius, boltTurnRate, boltHomingRange, transferCastTime,
            boltProc, boltSlowDuration, lockdownDamage, lockdownRadius, lockdownDuration,
            lockdownRoot, lockdownCooldown, lockdownProc, lockdownRange, transferDistance,
            transferRadius, transferDamage, transferRoot, transferCooldown, transferProc,
            cageInitial, cageDps, cageDuration, cageStun, cagePullRadius, cageRadius,
            cageInitialProc, cageTickProc, cageCooldown, cageRange, cagePull, cageTick,
            cageProjectileSpeed, cageProjectileLifetime, cageProjectileHitRadius;
        private static SkillDef primary, secondary, utility, special;
        private static GameObject boltPrefab, cageProjectile, teleportVfx, fieldVfx, targetingVfx, ringVfx, fieldVisualPrefab;
        private static string teleportStartSound, teleportEndSound;
        private sealed class ShotDelay : MonoBehaviour { internal float Until; }
        private sealed class BoltSkillDef : RemoteDroneSkills.RepeatingSkillDef
        {
            public override bool CanExecute(GenericSkill slot) => Ready(slot) && base.CanExecute(slot);
            public override bool IsReady(GenericSkill slot) => Ready(slot) && base.IsReady(slot);
            private static bool Ready(GenericSkill slot)
            {
                ShotDelay timer = slot ? slot.GetComponent<ShotDelay>() : null;
                return !timer || Time.time >= timer.Until;
            }
            public override void OnExecute(GenericSkill slot)
            {
                base.OnExecute(slot);
                ShotDelay timer = slot.GetComponent<ShotDelay>() ?? slot.gameObject.AddComponent<ShotDelay>();
                CharacterBody body = slot.GetComponent<CharacterBody>();
                timer.Until = Time.time + Mathf.Max(.05f, C.Get(boltInterval)) /
                    Mathf.Max(.1f, body ? body.attackSpeed : 1f);
            }
        }
        private sealed class TargetedCageSkillDef : RemoteDroneSkills.RepeatingSkillDef
        {
            public override bool CanExecute(GenericSkill slot) => HasTarget(slot) && base.CanExecute(slot);
            public override bool IsReady(GenericSkill slot) => HasTarget(slot) && base.IsReady(slot);
            private static bool HasTarget(GenericSkill slot)
            {
                CharacterBody body = slot ? slot.GetComponent<CharacterBody>() : null;
                InputBankTest input = slot ? slot.GetComponent<InputBankTest>() : null;
                return body && input && Target(input.GetAimRay(), body, Mathf.Max(0f, C.Get(cageRange)));
            }
        }

        private static ConfigEntry<float> N(string section, string key, float value, string help) =>
            RemoteDroneSkills.Number(Root + " - " + section, key, value, help);

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true,
                "Add player-controlled Jailer Drone skills. AI drones are unchanged. Vanilla: False.");
            primaryOn = C.BindStartupBool(Root + " - Restraint Bolt", "Changes Enabled", true, "Enable Restraint Bolt.");
            secondaryOn = C.BindStartupBool(Root + " - Lockdown", "Changes Enabled", true, "Enable Lockdown.");
            utilityOn = C.BindStartupBool(Root + " - Prison Transfer", "Changes Enabled", true, "Enable Prison Transfer.");
            specialOn = C.BindStartupBool(Root + " - Containment Cage", "Changes Enabled", true, "Enable Containment Cage.");
            boltDamage = N("Restraint Bolt", "Damage (%)", 200f, "Damage of the initial hit.");
            boltBounceLoss = N("Restraint Bolt", "Damage Loss Per Bounce (percentage points)", 50f,
                "Subtract this much base damage from each successive hit. Set to 0 to disable bounces.");
            boltBounceRange = N("Restraint Bolt", "Bounce Range (m)", 18f,
                "Maximum distance between successive targets.");
            boltInterval = N("Restraint Bolt", "Firing Interval (s)", .8f, "Delay between bolts, reduced by attack speed.");
            boltSpeed = N("Restraint Bolt", "Projectile Speed (m/s)", 125f, "Bolt travel speed. Vanilla (this rework): 90m/s.");
            boltHitRadius = N("Restraint Bolt", "Hit Radius (m)", 1.6f, "Forgiveness around the bolt trajectory.");
            boltTurnRate = N("Restraint Bolt", "Homing Turn Speed (degrees/s)", 65f, "Maximum steering rate toward enemies near the bolt's flight path.");
            boltHomingRange = N("Restraint Bolt", "Homing Range (m)", 18f, "Maximum acquisition distance for subtle projectile steering.");
            boltLifetime = N("Restraint Bolt", "Projectile Lifetime (s)", 5f, "Bolt lifetime.");
            boltProc = N("Restraint Bolt", "Proc Coefficient", 1f, "Proc coefficient on direct hit.");
            boltSlowDuration = N("Restraint Bolt", "Slow Duration (s)", 2f, "Duration of vanilla 50% Slow.");
            lockdownDamage = N("Lockdown", "Damage (%)", 200f, "Damage once per enemy entering each field.");
            lockdownRadius = N("Lockdown", "Radius (m)", 12f, "Field radius.");
            lockdownDuration = N("Lockdown", "Duration (s)", 5f, "Field duration.");
            lockdownRoot = N("Lockdown", "Root Duration (s)", 1f, "Root refresh duration while inside.");
            lockdownCooldown = N("Lockdown", "Cooldown (s)", 15f, "Recharge per stock.");
            lockdownProc = N("Lockdown", "Proc Coefficient", 1f, "Proc coefficient on the first hit per field.");
            lockdownRange = N("Lockdown", "Target Range (m)", 80f, "Maximum placement range.");
            transferDistance = N("Prison Transfer", "Distance (m)", 40f, "Maximum forward teleport distance.");
            transferRadius = N("Prison Transfer", "Radius (m)", 10f, "Arrival effect radius.");
            transferDamage = N("Prison Transfer", "Damage (%)", 200f, "Arrival damage.");
            transferRoot = N("Prison Transfer", "Root Duration (s)", 2f, "Root duration after relocation.");
            transferCooldown = N("Prison Transfer", "Cooldown (s)", 8f, "Skill cooldown.");
            transferCastTime = N("Prison Transfer", "Cast Time (s)", .25f, "Brief teleport windup and animation.");
            transferProc = N("Prison Transfer", "Proc Coefficient", 1f, "Arrival damage proc coefficient.");
            cageInitial = N("Containment Cage", "Initial Damage (%)", 500f, "Initial target damage.");
            cageDps = N("Containment Cage", "Damage Per Second (%)", 200f, "Damage per second inside the cage.");
            cageDuration = N("Containment Cage", "Duration (s)", 6f, "Cage duration.");
            cageStun = N("Containment Cage", "Stun Duration (s)", 2f, "Initial target stun.");
            cagePullRadius = N("Containment Cage", "Pull Radius (m)", 20f, "Range at which the cage attracts enemies.");
            cageRadius = N("Containment Cage", "Damage Radius (m)", 10f, "Cage damage radius.");
            cageInitialProc = N("Containment Cage", "Initial Proc Coefficient", 1f, "Initial target hit proc coefficient.");
            cageTickProc = N("Containment Cage", "Continuous Proc Coefficient", .2f, "Proc coefficient per damage tick.");
            cageCooldown = N("Containment Cage", "Cooldown (s)", 20f, "Skill cooldown.");
            cageRange = N("Containment Cage", "Target Range (m)", 80f, "Maximum target range.");
            cagePull = N("Containment Cage", "Pull Strength", 90f, "Maximum inward acceleration in metres per second squared.");
            cageTick = N("Containment Cage", "Damage Interval (s)", .25f, "Time between cage damage ticks.");
            cageProjectileSpeed = N("Containment Cage", "Projectile Speed (m/s)", 45f, "Speed of the target seeking chain projectile.");
            cageProjectileLifetime = N("Containment Cage", "Projectile Lifetime (s)", 4f, "Maximum time to reach the marked enemy.");
            cageProjectileHitRadius = N("Containment Cage", "Projectile Hit Radius (m)", 1.5f, "Distance from the marked target needed to latch.");
        }

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("JailerRestraintBolt", "Restraint Bolt", "Fire a slowing bolt.", typeof(RestraintBolt), P, 0f, null,
                customDefinition: ScriptableObject.CreateInstance<BoltSkillDef>());
            secondary = RemoteDroneSkills.MakeSkill("JailerLockdown", "Lockdown", "Deploy a rooting field.", typeof(Lockdown), S, C.Get(lockdownCooldown), null, beginsOnEnd: true);
            utility = RemoteDroneSkills.MakeSkill("JailerPrisonTransfer", "Prison Transfer", "Teleport forward and relocate enemies.", typeof(PrisonTransfer), U, C.Get(transferCooldown), null);
            special = RemoteDroneSkills.MakeSkill("JailerContainmentCage", "Containment Cage", "Trap a target and draw enemies inward.", typeof(ContainmentCage), X, C.Get(cageCooldown), null,
                customDefinition: ScriptableObject.CreateInstance<TargetedCageSkillDef>());
            secondary.mustKeyPress = utility.mustKeyPress = special.mustKeyPress = true;
            Refresh();
        }
        internal static BuffDef[] Buffs => Array.Empty<BuffDef>();

        internal static void Refresh()
        {
            if (!primary) return;
            secondary.baseRechargeInterval = Mathf.Max(0f, C.Get(lockdownCooldown));
            utility.baseRechargeInterval = Mathf.Max(0f, C.Get(transferCooldown));
            special.baseRechargeInterval = Mathf.Max(0f, C.Get(cageCooldown));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"Fire a bolt for <style=cIsDamage>{C.Get(boltDamage):0.##}% damage</style>, slowing enemies by <style=cIsUtility>50%</style> for <style=cIsUtility>{C.Get(boltSlowDuration):0.##} seconds</style>."
                + (C.Get(boltBounceLoss) > 0f && C.Get(boltBounceRange) > 0f && C.Get(boltDamage) > C.Get(boltBounceLoss)
                    ? $" Bounces to nearby <style=cIsUtility>Rooted</style> or <style=cIsUtility>Stunned</style> enemies, dealing <style=cIsDamage>{C.Get(boltBounceLoss):0.##}% base damage less</style> with each bounce."
                    : ""));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Hold to aim a field, then release to deal <style=cIsDamage>{C.Get(lockdownDamage):0.##}% damage</style> and <style=cIsUtility>Root</style> enemies within <style=cIsUtility>{C.Get(lockdownRadius):0.##}m</style> for <style=cIsUtility>{C.Get(lockdownDuration):0.##} seconds</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Hold to aim and release to <style=cIsUtility>teleport</style> up to <style=cIsUtility>{C.Get(transferDistance):0.##}m</style> forwards, dealing <style=cIsDamage>{C.Get(transferDamage):0.##}% damage</style> to nearby enemies and transporting them back to your starting position. <style=cIsUtility>Root</style> them for <style=cIsUtility>{C.Get(transferRoot):0.##} seconds</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Fire a seeking chain at an enemy for <style=cIsDamage>{C.Get(cageInitial):0.##}% damage</style> and <style=cIsUtility>Stun</style> it for <style=cIsUtility>{C.Get(cageStun):0.##} seconds</style>. Chain nearby enemies and pull them in, dealing <style=cIsDamage>{C.Get(cageDps) * C.Get(cageTick):0.##}% damage every {C.Get(cageTick):0.##} seconds</style> for <style=cIsUtility>{C.Get(cageDuration):0.##} seconds</style>.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () => $"\nShot Interval: {C.Get(boltInterval):0.##}s\nProjectile Speed: {C.Get(boltSpeed):0.##}m/s\nHit Radius: {C.Get(boltHitRadius):0.##}m\nHoming Turn: {C.Get(boltTurnRate):0.##}°/s\nBounce Range: {C.Get(boltBounceRange):0.##}m", () => C.Get(boltProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () => $"\nRadius: {C.Get(lockdownRadius):0.##}m\nRoot Refresh: {C.Get(lockdownRoot):0.##}s", () => C.Get(lockdownProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility, () => $"\nRadius: {C.Get(transferRadius):0.##}m\nCast Time: {C.Get(transferCastTime):0.##}s", () => C.Get(transferProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () => $"\nPull Radius: {C.Get(cagePullRadius):0.##}m\nDamage Radius: {C.Get(cageRadius):0.##}m\nContinuous Proc: {C.Get(cageTickProc):0.##}", () => C.Get(cageInitialProc));
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !primary) return;
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            Sprite original = locator && locator.primary && locator.primary.skillFamily && locator.primary.skillFamily.variants.Length > 0
                ? locator.primary.skillFamily.variants[0].skillDef.icon : null;
            Color jailerBlue = new Color(.42f, .87f, 1f);
            primary.icon = TintIcon(RemoteDroneSkills.VanillaIcon("RailgunnerBody", "Secondary", original), jailerBlue);
            // This is the native Jailer primary, reassigned to the secondary slot.
            secondary.icon = original;
            utility.icon = TintIcon(RemoteDroneSkills.VanillaIcon("HuntressBody", "Utility", original), jailerBlue);
            special.icon = TintIcon(RemoteDroneSkills.VanillaIcon("TreebotBody", "Special", original), jailerBlue);
            LoadNativeEffects();
            if (primaryOn.Value) { RemoteDroneSkills.AddMachine(prefab, P); RemoteDroneSkills.SetSkill(prefab, "Primary", primary); }
            if (secondaryOn.Value) { RemoteDroneSkills.AddMachine(prefab, S); RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary); }
            if (utilityOn.Value) { RemoteDroneSkills.AddMachine(prefab, U); RemoteDroneSkills.SetSkill(prefab, "Utility", utility); }
            if (specialOn.Value) { RemoteDroneSkills.AddMachine(prefab, X); RemoteDroneSkills.SetSkill(prefab, "Special", special); }
            if (specialOn.Value && !prefab.GetComponent<CageTargetIndicator>()) prefab.AddComponent<CageTargetIndicator>();
        }

        private static Sprite TintIcon(Sprite source, Color accent)
        {
            if (!source || !source.texture) return source;
            RenderTexture previous = RenderTexture.active, buffer = null;
            try
            {
                Rect rect = source.textureRect;
                int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);
                buffer = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source.texture, buffer);
                RenderTexture.active = buffer;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color pixel = pixels[i];
                    float value = Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b));
                    Color recoloured = Color.Lerp(pixel, accent * value, .65f);
                    recoloured.a = pixel.a;
                    pixels[i] = recoloured;
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                return Sprite.Create(texture, new Rect(0, 0, width, height),
                    new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height), source.pixelsPerUnit);
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Jailer: icon recolouring failed: " + e.Message);
                return source;
            }
            finally
            {
                RenderTexture.active = previous;
                if (buffer) RenderTexture.ReleaseTemporary(buffer);
            }
        }

        private static GameObject NativeStaticEffect(string field)
        {
            Type stateType = typeof(EntityStates.Drone.DroneJailer.JailerDroneRemoteOpSkill);
            FieldInfo member = AccessTools.Field(stateType, field);
            if (member == null) return null;
            return ResolveEffectReference(member.GetValue(null));
        }

        private static GameObject ResolveEffectReference(object value)
        {
            if (value == null) return null;
            if (value is GameObject direct) return direct;
            MethodInfo convert = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(GameObject));
            return (convert?.Invoke(null, new[] { value }) as GameObject)
                ?? AccessTools.Property(value.GetType(), "gameObject")?.GetValue(value) as GameObject;
        }

        private static void PlayLockdownCast(Vector3 point)
        {
            try
            {
                if (!fieldVfx) fieldVfx = NativeStaticEffect("wallsVFX");
                if (!fieldVfx)
                {
                    Type stateType = typeof(EntityStates.Drone.DroneJailer.JailerDroneRemoteOpSkill);
                    FieldInfo member = AccessTools.Field(stateType, "wallsVFX");
                    var configuration = Resources.FindObjectsOfTypeAll<EntityStateConfiguration>()
                        .FirstOrDefault(c => c && (Type)c.targetType == stateType);
                    if (configuration && member != null)
                    {
                        foreach (var field in configuration.serializedFieldsCollection.serializedFields)
                        {
                            if (field.fieldName != member.Name) continue;
                            fieldVfx = ResolveEffectReference(field.fieldValue.GetValue(member));
                            break;
                        }
                    }
                }
                if (fieldVfx) EffectManager.SimpleEffect(fieldVfx, point, Quaternion.identity, true);
                else if (!warnedLockdownCast)
                {
                    warnedLockdownCast = true;
                    ThreeQsVanillaTweaks.Log.Warning("Remote Jailer: vanilla Lockdown cast effect (wallsVFX) was not resolved.");
                }
            }
            catch (Exception error)
            {
                if (warnedLockdownCast) return;
                warnedLockdownCast = true;
                ThreeQsVanillaTweaks.Log.Warning("Remote Jailer: vanilla Lockdown cast effect failed: " + error.Message);
            }
        }

        private static void LoadNativeEffects()
        {
            // Native Cage uses the same area indicator and holographic wall effect.
            targetingVfx = NativeStaticEffect("areaIndicatorPrefab");
            var teleport = EntityStateCatalog.InstantiateState(typeof(EntityStates.Treebot.ProliferationTeleport))
                as EntityStates.Treebot.ProliferationTeleport;
            if (teleport != null)
            {
                LegacyResourcesAPI.Load<EntityStateConfiguration>(
                    "EntityStateConfigurations/EntityStates.Treebot.ProliferationTeleport")
                    ?.BuildInstanceInitializer()?.Invoke(teleport);
                teleportVfx = RemoteDroneSkills.NativeGameObject(teleport, "teleportationVFX");
                teleportStartSound = teleport.teleportationStartSound;
                teleportEndSound = teleport.teleportationEndSound;
            }
        }

        private static Material NativeVisualMaterial(Color tint)
        {
            try
            {
                GameObject effect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/HealthOrbEffect");
                var renderer = effect ? effect.GetComponentInChildren<ParticleSystemRenderer>(true) : null;
                Material existing = renderer ? renderer.sharedMaterial : null;
                if (!existing || !existing.shader || !existing.shader.isSupported) return null;
                var result = new Material(existing);
                if (result.HasProperty("_TintColor")) result.SetColor("_TintColor", tint);
                else if (result.HasProperty("_Color")) result.SetColor("_Color", tint);
                return result;
            }
            catch (Exception error)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Jailer: native visual material unavailable: " + error.Message);
                return null;
            }
        }

        internal static void RegisterProjectiles(ContentPack content)
        {
            if (!enabled.Value) return;
            GameObject donor = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/FMJRamping");
            if (!donor) { ThreeQsVanillaTweaks.Log.Warning("Remote Jailer: bolt projectile unavailable."); return; }
            boltPrefab = R2API.PrefabAPI.InstantiateClone(donor, "3QSRemoteJailerBolt", true);
            var simple = boltPrefab.GetComponent<ProjectileSimple>();
            if (simple) simple.lifetime = Mathf.Max(.1f, C.Get(boltLifetime));
            var controller = boltPrefab.GetComponent<ProjectileController>();
            if (controller) controller.procCoefficient = Mathf.Max(0f, C.Get(boltProc));
            var overlap = boltPrefab.GetComponent<ProjectileOverlapAttack>();
            if (overlap) overlap.enabled = false;
            var single = boltPrefab.GetComponent<ProjectileSingleTargetImpact>();
            if (single) single.enabled = false;
            // FMJRamping has no ghost prefab in this game build. Give the bolt
            // a visible body and trail on the networked projectile itself.
            Material material = NativeVisualMaterial(new Color(.35f, .85f, 1f, .85f));
            if (material)
            {
                GameObject glow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                glow.name = "Jailer Bolt Glow";
                glow.transform.SetParent(boltPrefab.transform, false);
                glow.transform.localScale = Vector3.one * .38f;
                UnityEngine.Object.Destroy(glow.GetComponent<Collider>());
                glow.GetComponent<Renderer>().sharedMaterial = material;
                var trail = boltPrefab.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.time = .25f;
                trail.startWidth = .33f;
                trail.endWidth = 0f;
            }
            boltPrefab.AddComponent<BoltImpact>();
            var chain = new GameObject("Jailer Cage Projectile Template");
            chain.SetActive(false);
            chain.AddComponent<NetworkIdentity>();
            var flight = chain.AddComponent<Rigidbody>();
            flight.useGravity = false;
            flight.detectCollisions = false;
            chain.AddComponent<TeamFilter>();
            chain.AddComponent<ProjectileNetworkTransform>();
            chain.AddComponent<ProjectileController>();
            chain.AddComponent<ProjectileDamage>();
            chain.AddComponent<ProjectileTargetComponent>();
            chain.AddComponent<CageImpact>();
            chain.AddComponent<CageProjectileVisual>();
            cageProjectile = R2API.PrefabAPI.InstantiateClone(chain, "3QSRemoteJailerChain", true);
            cageProjectile.SetActive(true);
            UnityEngine.Object.Destroy(chain);
            content.projectilePrefabs.Add(new[] { boltPrefab, cageProjectile });
            ringVfx = new GameObject("3QS Jailer Field Ring");
            var effect = ringVfx.AddComponent<EffectComponent>();
            effect.applyScale = true;
            ringVfx.AddComponent<DestroyOnTimer>().duration = .65f;
            var line = ringVfx.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 64;
            line.widthMultiplier = .05f;
            Material ringMaterial = NativeVisualMaterial(new Color(.35f, .83f, 1f, .45f));
            if (ringMaterial) line.sharedMaterial = ringMaterial;
            else line.enabled = false;
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a), .06f, Mathf.Sin(a)));
            }
            content.effectDefs.Add(new[] { new EffectDef(ringVfx) });

            var fieldTemplate = new GameObject("Jailer Field Visual Template");
            fieldTemplate.SetActive(false);
            fieldTemplate.AddComponent<NetworkIdentity>();
            var movement = fieldTemplate.AddComponent<NetworkTransform>();
            movement.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform;
            movement.sendInterval = .05f;
            fieldTemplate.AddComponent<FieldVisual>();
            fieldVisualPrefab = R2API.PrefabAPI.InstantiateClone(fieldTemplate, "3QS Jailer Active Field", true);
            fieldVisualPrefab.SetActive(true);
            UnityEngine.Object.Destroy(fieldTemplate);
            content.networkedObjectPrefabs.Add(new[] { fieldVisualPrefab });
        }

        private static bool warnedLockdownCast;
        private static readonly Color LockdownColour = new Color(.72f, .30f, 1f, .92f);
        private static readonly Color CageColour = new Color(1f, .6f, .18f, .95f);

        private static LineRenderer MakeLine(Transform parent, string name, Color colour,
            float width, int points, bool loop = false)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false;
            line.loop = loop;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.sharedMaterial = Graphic.defaultGraphicMaterial;
            line.startColor = colour;
            line.endColor = colour;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Vector3 OnCircle(float angle, float radius, float height) =>
            new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);

        private sealed class CageProjectileVisual : MonoBehaviour
        {
            private Transform frame;

            private void Start()
            {
                frame = new GameObject("Rotating cage").transform;
                frame.SetParent(transform, false);
                for (int side = 0; side < 4; side++)
                {
                    Vector3 corner = OnCircle(side * Mathf.PI / 2f, .72f, 0f);
                    Vector3 next = OnCircle((side + 1) * Mathf.PI / 2f, .72f, 0f);
                    var edge = MakeLine(frame, "Cage strut", CageColour, .13f, 4);
                    edge.SetPositions(new[] { Vector3.up, corner, next, Vector3.down });
                }
            }

            private void Update()
            {
                if (frame) frame.Rotate(110f * Time.deltaTime, 165f * Time.deltaTime, 0f);
            }
        }

        private sealed class FieldVisual : NetworkBehaviour
        {
            private bool special;
            private float radius;
            private TeamIndex ownerTeam;
            private GameObject geometry;
            private readonly List<(LineRenderer Line, Vector3[] Points)> boundary =
                new List<(LineRenderer, Vector3[])>();
            private Vector3 lastProjectionOrigin;
            private float nextProjection;
            private readonly Dictionary<CharacterBody, LineRenderer> links = new Dictionary<CharacterBody, LineRenderer>();
            private readonly HashSet<CharacterBody> nearby = new HashSet<CharacterBody>();
            private float nextScan;

            internal void Configure(bool isCage, float fieldRadius, TeamIndex team)
            {
                special = isCage;
                radius = fieldRadius;
                ownerTeam = team;
            }

            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState) return false;
                writer.Write(special);
                writer.Write(radius);
                writer.Write((int)ownerTeam);
                return true;
            }

            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (initialState) Configure(reader.ReadBoolean(), reader.ReadSingle(), (TeamIndex)reader.ReadInt32());
            }

            public override void OnStartClient()
            {
                base.OnStartClient();
                if (radius <= 0f) return;
                geometry = new GameObject(special ? "Containment pull boundary" : "Lockdown root boundary");
                geometry.transform.SetParent(transform, false);
                if (!special)
                {
                    const int segments = 96;
                    var ring = MakeLine(geometry.transform, "Root perimeter", LockdownColour, .18f, segments, true);
                    var perimeter = new Vector3[segments];
                    for (int i = 0; i < segments; i++)
                        perimeter[i] = OnCircle(i * 2f * Mathf.PI / segments, radius, .24f);
                    boundary.Add((ring, perimeter));
                    for (int i = 0; i < 8; i++)
                    {
                        float angle = i * Mathf.PI / 4f;
                        var bar = MakeLine(geometry.transform, "Lockdown field bar", LockdownColour, .14f, 4);
                        boundary.Add((bar, new[] {
                            OnCircle(angle, radius - Mathf.Min(1f, radius * .15f), .24f),
                            OnCircle(angle, radius, .24f),
                            OnCircle(angle, radius, 1.65f),
                            OnCircle(angle, radius - Mathf.Min(.35f, radius * .08f), 1.9f)
                        }));
                    }
                }
                else
                {
                    const int sectors = 16;
                    for (int i = 0; i < sectors; i++)
                    {
                        float angle = i * 2f * Mathf.PI / sectors;
                        float halfSpan = Mathf.PI / sectors;
                        Vector3 left = OnCircle(angle - halfSpan * .82f, radius, .38f);
                        Vector3 tip = OnCircle(angle, radius * .9f, .38f);
                        Vector3 right = OnCircle(angle + halfSpan * .82f, radius, .38f);
                        int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(left, tip)), 1, 8);
                        var points = new Vector3[steps * 2 + 1];
                        for (int j = 0; j <= steps; j++)
                        {
                            float t = (float)j / steps;
                            points[j] = Vector3.Lerp(left, tip, t);
                            points[steps + j] = Vector3.Lerp(tip, right, t);
                        }
                        var arrow = MakeLine(geometry.transform, "Inward pull chevron", CageColour, .21f, points.Length);
                        boundary.Add((arrow, points));
                    }
                }
                ProjectBoundary();
            }

            private void ProjectBoundary()
            {
                Transform frame = geometry.transform;
                foreach (var stroke in boundary)
                {
                    for (int i = 0; i < stroke.Points.Length; i++)
                    {
                        Vector3 point = stroke.Points[i];
                        Vector3 sample = frame.TransformPoint(new Vector3(point.x, 0f, point.z));
                        // Keep the original height where no nearby floor exists, such as over a ledge.
                        if (Physics.Raycast(sample + Vector3.up * 6f, Vector3.down, out RaycastHit ground,
                            18f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && ground.normal.y > .25f)
                        {
                            point.y = frame.InverseTransformPoint(ground.point).y + point.y;
                        }
                        stroke.Line.SetPosition(i, point);
                    }
                }
                lastProjectionOrigin = frame.position;
                nextProjection = Time.time + .12f;
            }

            private void LateUpdate()
            {
                if (!special || !geometry) return;
                if (Time.time >= nextProjection &&
                    (geometry.transform.position - lastProjectionOrigin).sqrMagnitude >= .0625f)
                    ProjectBoundary();
                if (Time.time >= nextScan)
                {
                    nextScan = Time.time + .15f;
                    nearby.Clear();
                    var candidates = new SphereSearch { origin = transform.position, radius = radius, mask = LayerIndex.entityPrecise.mask };
                    candidates.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(ownerTeam))
                        .FilterCandidatesByDistinctHurtBoxEntities();
                    foreach (HurtBox box in candidates.GetHurtBoxes())
                    {
                        CharacterBody body = box.healthComponent ? box.healthComponent.body : null;
                        if (!body || !body.healthComponent.alive ||
                            (body.corePosition - transform.position).sqrMagnitude < 1f) continue;
                        nearby.Add(body);
                        if (!links.ContainsKey(body))
                            links.Add(body, MakeLine(geometry.transform, "Containment tether", CageColour, .15f, 2));
                    }
                    foreach (CharacterBody body in links.Keys.ToArray())
                    {
                        if (nearby.Contains(body)) continue;
                        Destroy(links[body].gameObject);
                        links.Remove(body);
                    }
                }
                foreach (var pair in links)
                {
                    if (!pair.Key) continue;
                    pair.Value.SetPosition(1, geometry.transform.InverseTransformPoint(pair.Key.corePosition));
                }
            }
        }

        private static void Ring(Vector3 position, float radius)
        {
            if (ringVfx && radius > 0f)
                EffectManager.SpawnEffect(ringVfx, new EffectData { origin = position,
                    rotation = Quaternion.identity, scale = radius }, true);
        }
        private static bool Enemy(CharacterBody owner, CharacterBody victim) =>
            owner && victim && victim.healthComponent && victim.healthComponent.alive &&
            owner.teamComponent && victim.teamComponent &&
            owner.teamComponent.teamIndex != victim.teamComponent.teamIndex;

        private static IEnumerable<HurtBox> Around(Vector3 point, float radius, CharacterBody source)
        {
            if (!source || !source.teamComponent) return Array.Empty<HurtBox>();
            return new SphereSearch { origin = point, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(source.teamComponent.teamIndex))
                .FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
        }

        private static void Damage(CharacterBody source, HurtBox box, float coefficient, float proc, bool crit, GameObject inflictor)
        {
            if (!box || !Enemy(source, box.healthComponent ? box.healthComponent.body : null)) return;
            var info = new DamageInfo { attacker = source.gameObject, inflictor = inflictor,
                damage = source.damage * Mathf.Max(0f, coefficient) / 100f,
                crit = crit, procCoefficient = Mathf.Max(0f, proc), position = box.transform.position };
            box.healthComponent.TakeDamage(info);
            GlobalEventManager.instance.OnHitEnemy(info, box.healthComponent.gameObject);
        }

        private static HurtBox Target(Ray ray, CharacterBody source, float range)
        {
            if (!source || !source.teamComponent) return null;
            var search = new BullseyeSearch {
                teamMaskFilter = TeamMask.GetEnemyTeams(source.teamComponent.teamIndex),
                filterByLoS = true, maxDistanceFilter = range, maxAngleFilter = 15f,
                searchOrigin = ray.origin, searchDirection = ray.direction,
                sortMode = BullseyeSearch.SortMode.Angle };
            search.RefreshCandidates();
            return search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive);
        }

        private sealed class CageTargetIndicator : MonoBehaviour
        {
            private Indicator indicator;
            private CharacterBody body;
            private InputBankTest input;
            private SkillLocator skills;
            private void Awake()
            {
                body = GetComponent<CharacterBody>(); input = GetComponent<InputBankTest>();
                skills = GetComponent<SkillLocator>();
                GameObject visual = LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator");
                if (visual) indicator = new Indicator(gameObject, visual);
            }
            private void FixedUpdate()
            {
                if (indicator == null) return;
                GenericSkill slot = skills ? skills.special : null;
                bool ready = body && body.hasEffectiveAuthority && input && slot && slot.skillDef == special &&
                    slot.stock >= special.requiredStock;
                indicator.targetTransform = ready
                    ? Target(input.GetAimRay(), body, Mathf.Max(0f, C.Get(cageRange)))?.transform : null;
                indicator.active = indicator.targetTransform != null;
            }
            private void OnDisable() { if (indicator != null) indicator.active = false; }
        }

        public sealed class RestraintBolt : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority && boltPrefab)
                {
                    Ray ray = GetAimRay();
                    ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                        projectilePrefab = boltPrefab, position = ray.origin,
                        rotation = Util.QuaternionSafeLookRotation(ray.direction), owner = gameObject,
                        damage = damageStat * Mathf.Max(0f, C.Get(boltDamage)) / 100f,
                        crit = Util.CheckRoll(critStat, characterBody.master),
                        speedOverride = Mathf.Max(1f, C.Get(boltSpeed)) });
                    RemoteDroneSkills.PlayPhaseFire(this);
                }
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        private sealed class BoltImpact : MonoBehaviour
        {
            private ProjectileController controller;
            private ProjectileDamage shot;
            private Rigidbody flight;
            private Vector3 last;
            private float age;
            private void Awake()
            {
                controller = GetComponent<ProjectileController>(); shot = GetComponent<ProjectileDamage>();
                flight = GetComponent<Rigidbody>();
                last = transform.position;
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                age += Time.fixedDeltaTime;
                if (age >= Mathf.Max(.1f, C.Get(boltLifetime))) { Destroy(gameObject); return; }
                if (!controller || !controller.owner || !shot) return;
                CharacterBody source = controller.owner.GetComponent<CharacterBody>();
                if (!source) { Destroy(gameObject); return; }
                if (C.Get(boltTurnRate) > 0f && source.teamComponent)
                {
                    var search = new BullseyeSearch {
                        teamMaskFilter = TeamMask.GetEnemyTeams(source.teamComponent.teamIndex),
                        filterByLoS = true, maxDistanceFilter = Mathf.Max(0f, C.Get(boltHomingRange)),
                        maxAngleFilter = 25f, searchOrigin = transform.position,
                        searchDirection = transform.forward, sortMode = BullseyeSearch.SortMode.Angle
                    };
                    search.RefreshCandidates();
                    HurtBox target = search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive);
                    if (target)
                    {
                        Vector3 wanted = (target.transform.position - transform.position).normalized;
                        transform.forward = Vector3.RotateTowards(transform.forward, wanted,
                            Mathf.Deg2Rad * C.Get(boltTurnRate) * Time.fixedDeltaTime, 0f);
                        if (flight) flight.velocity = transform.forward * Mathf.Max(1f, C.Get(boltSpeed));
                    }
                }
                Vector3 now = transform.position, delta = now - last;
                RaycastHit wall = default;
                bool hitWall = delta.sqrMagnitude > .0001f && Physics.Raycast(last, delta.normalized,
                    out wall, delta.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                if (hitWall) { now = wall.point; delta = now - last; }
                float hitRadius = Mathf.Max(.1f, C.Get(boltHitRadius));
                foreach (Collider collider in Physics.OverlapCapsule(last, now, hitRadius,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    HurtBox box = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                    if (!box || !box.healthComponent || !Enemy(source, box.healthComponent.body)) continue;
                    HealthComponent hit = box.healthComponent;
                    Vector3 impact = box.transform.position;
                    HitBolt(source, box, shot.damage, shot.crit, controller.procChainMask, gameObject);
                    float coefficient = C.Get(boltDamage);
                    float loss = C.Get(boltBounceLoss);
                    float range = C.Get(boltBounceRange);
                    if (shot.damage > 0f && loss > 0f && coefficient > loss && range > 0f)
                    {
                        var bounce = new BoltBounceOrb {
                            source = source, damagePerPoint = shot.damage / coefficient,
                            coefficient = coefficient - loss, loss = loss, range = range,
                            crit = shot.crit, procChainMask = controller.procChainMask,
                            visited = new HashSet<HealthComponent> { hit }
                        };
                        bounce.QueueFrom(impact);
                    }
                    Destroy(gameObject);
                    return;
                }
                if (hitWall) { Destroy(gameObject); return; }
                last = now;
            }
        }

        private static void HitBolt(CharacterBody source, HurtBox box, float damage, bool crit,
            ProcChainMask procChainMask, GameObject inflictor)
        {
            HealthComponent health = box.healthComponent;
            CharacterBody victim = health.body;
            var info = new DamageInfo {
                attacker = source.gameObject, inflictor = inflictor, damage = damage, crit = crit,
                procCoefficient = Mathf.Max(0f, C.Get(boltProc)), procChainMask = procChainMask,
                position = box.transform.position
            };
            health.TakeDamage(info);
            GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
            if (victim && health.alive)
                victim.AddTimedBuff(RoR2Content.Buffs.Slow50, Mathf.Max(.01f, C.Get(boltSlowDuration)));
        }

        private static bool IsRootedOrStunned(CharacterBody body)
        {
            if (body.HasBuff(DLC3Content.Buffs.Jailed) || body.HasBuff(RoR2Content.Buffs.Entangle) ||
                body.HasBuff(RoR2Content.Buffs.LunarSecondaryRoot) || body.HasBuff(RoR2Content.Buffs.Nullified) ||
                body.HasBuff(DLC3Content.Buffs.VultureRoot)) return true;
            var hurt = body.GetComponent<SetStateOnHurt>();
            return hurt && hurt.targetStateMachine && hurt.targetStateMachine.state is StunState;
        }

        private sealed class BoltBounceOrb : Orb
        {
            internal CharacterBody source;
            internal HashSet<HealthComponent> visited;
            internal float damagePerPoint, coefficient, loss, range;
            internal bool crit;
            internal ProcChainMask procChainMask;

            internal void QueueFrom(Vector3 position)
            {
                if (coefficient <= 0f || !source || !source.teamComponent || !OrbManager.instance) return;
                var search = new BullseyeSearch {
                    searchOrigin = position, searchDirection = Vector3.forward, maxAngleFilter = 180f,
                    maxDistanceFilter = range, filterByLoS = true, sortMode = BullseyeSearch.SortMode.Distance,
                    teamMaskFilter = TeamMask.GetEnemyTeams(source.teamComponent.teamIndex)
                };
                search.RefreshCandidates();
                target = search.GetResults().FirstOrDefault(box => box && box.healthComponent &&
                    !visited.Contains(box.healthComponent) && Enemy(source, box.healthComponent.body) &&
                    IsRootedOrStunned(box.healthComponent.body));
                if (!target) return;
                origin = position;
                OrbManager.instance.AddOrb(this);
            }

            public override void Begin()
            {
                if (!target) return;
                duration = Mathf.Clamp(Vector3.Distance(origin, target.transform.position) / 70f, .06f, .3f);
                GameObject effect = OrbStorageUtility.Get("Prefabs/Effects/OrbEffects/LightningOrbEffect");
                if (!effect) return;
                var data = new EffectData { origin = origin, genericFloat = duration, scale = 1f };
                data.SetHurtBoxReference(target);
                EffectManager.SpawnEffect(effect, data, true);
            }

            public override void OnArrival()
            {
                if (!target || !target.healthComponent || !Enemy(source, target.healthComponent.body) ||
                    !visited.Add(target.healthComponent)) return;
                Vector3 impact = target.transform.position;
                HitBolt(source, target, damagePerPoint * coefficient, crit, procChainMask, source.gameObject);
                var next = new BoltBounceOrb {
                    source = source, visited = visited, damagePerPoint = damagePerPoint,
                    coefficient = coefficient - loss, loss = loss, range = range,
                    crit = crit, procChainMask = procChainMask
                };
                next.QueueFrom(impact);
            }
        }

        public sealed class Lockdown : BaseState
        {
            private GameObject preview;
            private Vector3 point;
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority)
                {
                    GameObject visual = targetingVfx ? targetingVfx : NativeStaticEffect("areaIndicatorPrefab");
                    if (visual) preview = Instantiate(visual);
                }
            }
            private void Aim()
            {
                Ray ray = GetAimRay();
                float range = Mathf.Max(1f, C.Get(lockdownRange));
                point = Physics.Raycast(ray, out RaycastHit hit, range, LayerIndex.world.mask)
                    ? hit.point : ray.GetPoint(range);
                if (preview)
                {
                    preview.transform.position = point;
                    preview.transform.localScale = Vector3.one * Mathf.Max(.1f, C.Get(lockdownRadius));
                }
            }
            public override void Update()
            {
                base.Update();
                if (isAuthority) Aim();
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && inputBank && !inputBank.skill2.down)
                    outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (preview) Destroy(preview);
                if (NetworkServer.active && !outer.destroying && inputBank && !inputBank.skill2.down)
                {
                    Aim();
                    Field.Create(characterBody, point, false, Mathf.Max(.01f, C.Get(lockdownDuration)));
                }
                if (isAuthority) Util.PlaySound(EntityStates.Drone.DroneJailer.JailerDroneSkill.enterSoundString, gameObject);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class PrisonTransfer : BaseState
        {
            private bool released, complete;
            private float releasedAt;
            private GameObject preview;
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority)
                {
                    GameObject visual = targetingVfx ? targetingVfx : NativeStaticEffect("areaIndicatorPrefab");
                    if (visual) preview = Instantiate(visual);
                }
            }
            private Vector3 Aim()
            {
                Vector3 direction = GetAimRay().direction.normalized;
                float distance = Mathf.Max(0f, C.Get(transferDistance));
                Vector3 origin = transform.position;
                Vector3 destination = origin + direction * distance;
                if (distance > 0f && Physics.SphereCast(origin, .7f, direction, out RaycastHit obstacle,
                    distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                    destination = origin + direction * Mathf.Max(0f, obstacle.distance - 1f);
                if (preview)
                {
                    preview.transform.position = destination;
                    preview.transform.localScale = Vector3.one * Mathf.Max(.1f, C.Get(transferRadius));
                }
                return destination;
            }
            public override void Update()
            {
                base.Update();
                if (isAuthority && !released) Aim();
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!released && inputBank && !inputBank.skill3.down)
                {
                    released = true;
                    releasedAt = fixedAge;
                    if (preview) Destroy(preview);
                    PlayCrossfade("Gesture, Additive", "Create_Cage", Mathf.Max(.1f, C.Get(transferCastTime)));
                    if (teleportVfx) EffectManager.SimpleEffect(teleportVfx, characterBody.corePosition, Quaternion.identity, false);
                    if (isAuthority) Util.PlaySound(string.IsNullOrEmpty(teleportStartSound)
                        ? "Play_item_proc_teleportOnLowHealth" : teleportStartSound, gameObject);
                }
                if (!released || complete || fixedAge - releasedAt < Mathf.Max(0f, C.Get(transferCastTime))) return;
                complete = true;
                if (NetworkServer.active)
                {
                    Vector3 start = characterBody.footPosition;
                    Vector3 destination = Aim();
                    GameObject blink = EntityStates.Huntress.BlinkState.blinkPrefab;
                    if (blink)
                    {
                        Vector3 path = destination - transform.position;
                        int segments = Mathf.Max(1, Mathf.CeilToInt(path.magnitude / 2f));
                        Quaternion facing = Util.QuaternionSafeLookRotation(path);
                        for (int i = 0; i <= segments; i++)
                            EffectManager.SpawnEffect(blink, new EffectData {
                                origin = transform.position + path * ((float)i / segments), rotation = facing
                            }, true);
                    }
                    TeleportHelper.TeleportBody(characterBody, destination, false);
                    if (teleportVfx) EffectManager.SimpleEffect(teleportVfx, destination, Quaternion.identity, true);
                    Ring(destination, C.Get(transferRadius));
                    var affected = Around(destination, Mathf.Max(0f, C.Get(transferRadius)), characterBody).ToArray();
                    int index = 0;
                    foreach (HurtBox box in affected)
                    {
                        CharacterBody victim = box.healthComponent.body;
                        Damage(characterBody, box, C.Get(transferDamage), C.Get(transferProc), false, gameObject);
                        if (victim && !victim.isBoss)
                        {
                            // Find a clear spot around the original position. Never
                            // teleport an enemy into a wall or below the stage.
                            for (int attempt = 0; attempt < 12; attempt++)
                            {
                                float angle = (index++ + attempt * 7) * 2.399963f;
                                float radius = 1.5f + .5f * (attempt / 4);
                                Vector3 candidate = start + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                                if (Physics.Raycast(candidate + Vector3.up * 4f, Vector3.down,
                                    out RaycastHit ground, 9f, LayerIndex.world.mask))
                                    candidate = ground.point + Vector3.up * .15f;
                                if (Physics.CheckCapsule(candidate + Vector3.up * .5f,
                                    candidate + Vector3.up * 2f, .5f, LayerIndex.world.mask)) continue;
                                TeleportHelper.TeleportBody(victim, candidate, false);
                                if (teleportVfx) EffectManager.SimpleEffect(teleportVfx, candidate, Quaternion.identity, true);
                                break;
                            }
                        }
                        if (victim) victim.AddTimedBuff(DLC3Content.Buffs.Jailed, Mathf.Max(.01f, C.Get(transferRoot)));
                    }
                }
                if (isAuthority) Util.PlaySound(string.IsNullOrEmpty(teleportEndSound)
                    ? "Play_item_proc_teleportOnLowHealth" : teleportEndSound, gameObject);
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (preview) Destroy(preview);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class ContainmentCage : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                {
                    HurtBox target = Target(GetAimRay(), characterBody, Mathf.Max(0f, C.Get(cageRange)));
                    if (target && cageProjectile)
                    {
                        Vector3 from = characterBody.corePosition;
                        ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                            projectilePrefab = cageProjectile, owner = gameObject, target = target.gameObject,
                            position = from, rotation = Util.QuaternionSafeLookRotation(target.transform.position - from),
                            damage = characterBody.damage * Mathf.Max(0f, C.Get(cageInitial)) / 100f,
                            speedOverride = Mathf.Max(1f, C.Get(cageProjectileSpeed))
                        });
                    }
                    else skillLocator.special.AddOneStock();
                }
                if (isAuthority) Util.PlaySound(EntityStates.Drone.DroneJailer.JailerDroneSkill.enterSoundString, gameObject);
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        private sealed class CageImpact : MonoBehaviour
        {
            private ProjectileController projectile;
            private ProjectileTargetComponent targeting;
            private Rigidbody flight;
            private float elapsed;
            private void Awake()
            {
                projectile = GetComponent<ProjectileController>();
                targeting = GetComponent<ProjectileTargetComponent>();
                flight = GetComponent<Rigidbody>();
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                elapsed += Time.fixedDeltaTime;
                Transform target = targeting ? targeting.target : null;
                if (!target || !projectile || !projectile.owner || elapsed > Mathf.Max(.1f, C.Get(cageProjectileLifetime)))
                {
                    Destroy(gameObject);
                    return;
                }
                Vector3 toward = target.position - transform.position;
                float step = Mathf.Max(1f, C.Get(cageProjectileSpeed)) * Time.fixedDeltaTime;
                transform.rotation = Util.QuaternionSafeLookRotation(toward);
                if (flight) flight.velocity = transform.forward * Mathf.Max(1f, C.Get(cageProjectileSpeed));
                if (toward.magnitude > Mathf.Max(.2f, C.Get(cageProjectileHitRadius)) + step) return;
                HurtBox box = target.GetComponent<HurtBox>();
                CharacterBody source = projectile.owner.GetComponent<CharacterBody>();
                if (box && Enemy(source, box.healthComponent ? box.healthComponent.body : null))
                {
                    Damage(source, box, C.Get(cageInitial), C.Get(cageInitialProc), false, gameObject);
                    var stun = box.healthComponent.GetComponent<SetStateOnHurt>();
                    if (stun) stun.SetStun(Mathf.Max(0f, C.Get(cageStun)));
                    Field.Create(source, box.transform.position, true,
                        Mathf.Max(.01f, C.Get(cageDuration)), box.healthComponent.body);
                }
                Destroy(gameObject);
            }
        }

        private sealed class Field : MonoBehaviour
        {
            private CharacterBody owner;
            private CharacterBody anchor;
            private GameObject networkVisual;
            private bool cage;
            private float remaining, tick;
            private readonly HashSet<HealthComponent> damaged = new HashSet<HealthComponent>();
            private readonly List<CharacterBody> pullTargets = new List<CharacterBody>();
            internal static void Create(CharacterBody source, Vector3 point, bool isCage, float duration,
                CharacterBody target = null)
            {
                var area = new GameObject(isCage ? "3QS Jailer Containment Cage" : "3QS Jailer Lockdown");
                area.transform.position = point;
                Field field = area.AddComponent<Field>();
                field.owner = source; field.anchor = target; field.cage = isCage; field.remaining = duration;
                if (fieldVisualPrefab)
                {
                    field.networkVisual = Instantiate(fieldVisualPrefab, point, Quaternion.identity);
                    field.networkVisual.GetComponent<FieldVisual>().Configure(isCage,
                        Mathf.Max(0f, C.Get(isCage ? cagePullRadius : lockdownRadius)),
                        TeamComponent.GetObjectTeam(source.gameObject));
                    NetworkServer.Spawn(field.networkVisual);
                }
                if (!isCage) PlayLockdownCast(point);
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !owner || (remaining -= Time.fixedDeltaTime) <= 0f) { Destroy(gameObject); return; }
                if (cage && anchor && anchor.healthComponent && anchor.healthComponent.alive)
                    transform.position = anchor.corePosition;
                if (networkVisual) networkVisual.transform.position = transform.position;
                tick -= Time.fixedDeltaTime;
                if (tick <= 0f) UpdateTargets();
                if (cage) PullEnemies();
            }

            private void UpdateTargets()
            {
                float interval = cage ? Mathf.Max(.05f, C.Get(cageTick)) : .2f;
                tick += interval;
                float radius = cage ? Mathf.Max(0f, C.Get(cagePullRadius)) : Mathf.Max(0f, C.Get(lockdownRadius));
                pullTargets.Clear();
                foreach (HurtBox box in Around(transform.position, radius, owner))
                {
                    CharacterBody victim = box.healthComponent.body;
                    float distance = Vector3.Distance(victim.corePosition, transform.position);
                    if (!cage)
                    {
                        if (damaged.Add(box.healthComponent))
                            Damage(owner, box, C.Get(lockdownDamage), C.Get(lockdownProc), false, gameObject);
                        // Timed refresh lets independent fields and other sources coexist.
                        victim.AddTimedBuff(DLC3Content.Buffs.Jailed, Mathf.Max(.01f, C.Get(lockdownRoot)));
                    }
                    else
                    {
                        if (distance <= C.Get(cageRadius))
                            Damage(owner, box, C.Get(cageDps) * interval, C.Get(cageTickProc), false, gameObject);
                        if (victim != anchor && victim.healthComponent.alive) pullTargets.Add(victim);
                    }
                }
            }

            private void PullEnemies()
            {
                float strength = Mathf.Max(0f, C.Get(cagePull));
                if (strength <= 0f) return;
                float radius = Mathf.Max(0f, C.Get(cagePullRadius));
                float maxSpeed = Mathf.Min(30f, strength * .5f);
                foreach (CharacterBody victim in pullTargets)
                {
                    if (!victim || !victim.healthComponent || !victim.healthComponent.alive) continue;
                    Vector3 offset = transform.position - victim.corePosition;
                    if (offset.sqrMagnitude > radius * radius) continue;

                    Vector3 velocity;
                    var motor = victim.GetComponent<IPhysMotor>();
                    if (motor != null) velocity = motor.velocity;
                    else
                    {
                        var rigidbody = victim.GetComponent<Rigidbody>();
                        if (!rigidbody || rigidbody.isKinematic) continue;
                        velocity = rigidbody.velocity;
                    }
                    Vector3 desired = Vector3.ClampMagnitude(offset * 5f, maxSpeed);
                    var force = PhysForceInfo.Create();
                    force.ignoreGroundStick = true;
                    force.massIsOne = true;
                    force.force = Vector3.ClampMagnitude(desired - velocity, strength * Time.fixedDeltaTime);
                    if (force.force.sqrMagnitude > .0001f) victim.healthComponent.TakeDamageForce(force);
                }
            }
            private void OnDestroy()
            {
                if (NetworkServer.active && networkVisual) NetworkServer.Destroy(networkVisual);
            }
        }
    }
}
