using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Drone.DroneWeapon;
using HarmonyLib;
using RoR2;
using RoR2.Audio;
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
    internal static class RemoteBarrierDrone
    {
        private const string Root = RemoteDroneSkills.BarrierSection;
        private const string P = "3QSBarrierPrimary", S = "3QSBarrierBeam", U = "3QSBarrierShield", X = "3QSBarrierField";
        private const string OrbName = "Barrier Orb", BeamName = "Barrier Buildup",
            ShieldName = "Shield Projection", FieldName = "Recharge Field";
        internal static readonly Type[] States = { typeof(ChargeOrb), typeof(FireOrb), typeof(Beam), typeof(Shield), typeof(DeployField) };
        private static ConfigEntry<bool> enabled, orbOn, beamOn, shieldOn, fieldOn, rapidCharge, rapidReload, intercept, initialPulse, finalPulse, refillPrimary;
        private static ConfigEntry<float> minCharge, maxCharge, minDamage, maxDamage, minBarrier, maxBarrier,
            orbReload, orbRange, orbSpeed, bounceSpeed, bounceMinTime, bounceRange, orbDelay, orbLockout, orbProc,
            fullHold, orbArcHeight, orbBlastRadius, dischargeLockout, dischargeRadius, dischargeShock,
            beamBarrier, beamTime, beamRange, beamCooldown,
            shieldRadius, shieldTime, shieldCooldown, shieldArmour, shieldAttackSpeed, shieldBarrier, shieldPush, shieldPushTime,
            fieldTime, fieldCooldown, fieldInterval, fieldReduction, fieldBarrier, podSpeed, podLifetime,
            podHitRadius, podDamage, podBigDamage, podExplosionRadius, podLargeRadius, podProc;
        private static ConfigEntry<int> minBounces, maxBounces;
        private static SkillDef primary, secondary, utility, special;
        private static Sprite lockoutIcon;
        private static LoopSoundDef overchargeLoop;
        private static string overchargeStartSound, overchargeEndSound;
        private static bool overchargeAudioLoaded;
        private static BuffDef shieldBuff;
        private static GameObject visualPrefab, nativeBeam, nativeShield, nativeOrbExplosion, rallyBeacon, rallySpawnEffect;
        private static NetworkSoundEventDef launchSound, fizzleSound, dischargeSound, attachSound, burstSound, bounceSound, beamSound, beaconSound, pulseSound;
        private const string ChargeSound = "Play_railgunner_R_gun_chargeUp";
        private const string HoldSound = "Play_railgunner_R_gun_timer_loop";
        private const string StopHoldSound = "Stop_railgunner_R_gun_timer_loop";
        private const byte BarrierShieldCombo = 179;
        private static bool beamLookupWarning, domeLookupWarning, beaconLookupWarning;
        private static readonly PropertyInfo ArmourProperty = AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.armor));
        private static readonly PropertyInfo AttackSpeedProperty = AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.attackSpeed));
        private static readonly HashSet<SupportZone> fields = new HashSet<SupportZone>();
        private static readonly Color Aqua = new Color(.16f, .9f, .83f, .85f);
        private static readonly Color Gold = new Color(1f, .75f, .25f, .9f);
        private static readonly Color OrbExpansion = new Color(.75f, .3f, 1f, .95f);
        private static readonly Color OrbExplosion = new Color(.25f, .75f, 1f, .95f);
        private static readonly int BeaconIdle = Animator.StringToHash("Base Layer.idle");
        private static readonly int BeaconDeactivate = Animator.StringToHash("Base Layer.Deactivate");
        private const float BeaconTransitionTime = .25f;

        private static ConfigEntry<float> N(string group, string key, float value, string description) =>
            RemoteDroneSkills.Number(Root + " - " + group, key, value, description);
        private static ConfigEntry<int> I(string group, string key, int value, string description) =>
            RemoteDroneSkills.Number(Root + " - " + group, key, value, description);
        private static ConfigEntry<bool> B(string group, string key, bool value, string description) =>
            C.BindBool(Root + " - " + group, key, value, description);
        private static float F(ConfigEntry<float> value) => Mathf.Max(0f, C.Get(value));
        private static float Duration(ConfigEntry<float> value) => Mathf.Max(.02f, F(value));

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable custom skills on the player-controlled Barrier Drone.");
            orbOn = C.BindStartupBool(Root + " - " + OrbName, "Changes Enabled", true, "Enable Barrier Orb.");
            beamOn = C.BindStartupBool(Root + " - " + BeamName, "Changes Enabled", true, "Enable Barrier Buildup in the secondary slot.");
            shieldOn = C.BindStartupBool(Root + " - " + ShieldName, "Changes Enabled", true, "Enable Shield Projection.");
            fieldOn = C.BindStartupBool(Root + " - " + FieldName, "Changes Enabled", true, "Enable Recharge Field.");
            minCharge = N(OrbName, "Minimum Charge (s)", .2f, "Time at which damage and barrier start increasing.");
            maxCharge = N(OrbName, "Maximum Charge (s)", 2.5f, "Time to reach maximum strength at base attack speed.");
            fullHold = N(OrbName, "Full Charge Countdown (s)", 2f, "Time to release a fully charged orb before it discharges. Attack speed does not affect this countdown.");
            minDamage = N(OrbName, "Minimum Damage (%)", 200f, "Damage at minimum charge.");
            maxDamage = N(OrbName, "Maximum Damage (%)", 500f, "Damage at full charge.");
            minBarrier = N(OrbName, "Minimum Barrier (%)", 200f, "Barrier as a percentage of your damage stat at minimum charge.");
            maxBarrier = N(OrbName, "Maximum Barrier (%)", 400f, "Barrier as a percentage of your damage stat at full charge.");
            minBounces = I(OrbName, "Minimum Bounces", 1, "Additional targets at minimum charge.");
            maxBounces = I(OrbName, "Maximum Bounces", 3, "Additional targets at full charge.");
            orbReload = N(OrbName, "Full Recharge (s)", 4f, "Time without firing needed to replenish all missing stocks.");
            orbRange = N(OrbName, "Target Range (m)", 80f, "Maximum distance to select the initial target.");
            orbSpeed = N(OrbName, "Orb Speed (m/s)", 60f, "Travel speed of the initial orb along its arc.");
            bounceSpeed = N(OrbName, "Bounce Speed (m/s)", 20f, "Travel speed along the arc between subsequent targets.");
            bounceMinTime = N(OrbName, "Minimum Bounce Travel Time (s)", .35f, "Minimum travel time for a bounce between nearby targets.");
            orbArcHeight = N(OrbName, "Arc Height (m)", 10f, "Maximum arc height for the initial orb and subsequent bounces. Shorter trips use a proportionally lower arc.");
            orbBlastRadius = N(OrbName, "Explosion Radius (m)", 1f, "Extra explosion radius beyond the attached character's body size. Each character can be affected once per orb chain.");
            bounceRange = N(OrbName, "Bounce Range (m)", 24f, "Maximum distance to each subsequent target.");
            orbDelay = N(OrbName, "Detonation Delay (s)", 1f, "Delay after attaching to each target.");
            orbLockout = N(OrbName, "Post-Shot Lockout (s)", 1f, "Fixed delay before charging can begin again.");
            dischargeLockout = N(OrbName, "Discharge Lockout (s)", 4f, "Time all skills are disabled after overcharging, or the primary is disabled after releasing without a valid target. Cooldown reduction does not shorten this lockout.");
            dischargeRadius = N(OrbName, "Discharge Radius (m)", 10f, "Radius of the harmless electrical discharge around the drone after a failed charge.");
            dischargeShock = N(OrbName, "Discharge Shock Duration (s)", 5f, "Shock duration applied to nearby enemies by a failed charge. Vanilla Shock lasts 5 seconds.");
            orbProc = N(OrbName, "Proc Coefficient", 1f, "Proc coefficient when the orb damages an enemy.");
            rapidCharge = B(OrbName, "Attack Speed Scales Charge", true, "Scale charge time with attack speed.");
            rapidReload = B(OrbName, "Attack Speed Scales Recharge", true, "Scale the full reload with attack speed.");

            beamBarrier = N(BeamName, "Total Barrier (%)", 800f, "Total barrier as a percentage of your damage stat.");
            beamTime = N(BeamName, "Duration (s)", 5f, "Maximum beam duration.");
            beamRange = N(BeamName, "Target Range (m)", 50f, "Maximum beam length. Vanilla targeting range: 50m.");
            beamCooldown = N(BeamName, "Cooldown (s)", 10f, "Cooldown starts on cast.");

            shieldRadius = N(ShieldName, "Radius (m)", 15f, "Radius of the moving bubble.");
            shieldTime = N(ShieldName, "Duration (s)", 10f, "Duration of the moving bubble.");
            shieldCooldown = N(ShieldName, "Cooldown (s)", 18f, "Cooldown starts on cast.");
            shieldArmour = N(ShieldName, "Armour Bonus", 50f, "Armour granted while an ally is inside.");
            shieldAttackSpeed = N(ShieldName, "Attack Speed Bonus (%)", 30f, "Attack speed granted while an ally is inside.");
            shieldBarrier = N(ShieldName, "Barrier Per Second (%)", 100f, "Barrier per second as a percentage of your damage stat.");
            shieldPush = N(ShieldName, "Initial Push Speed (m/s)", 10f, "Minimum outward speed for enemies enclosed on deployment. Speed increases as needed to push them beyond the shield. Set to 0 to disable the push.");
            shieldPushTime = N(ShieldName, "Push Duration (s)", .75f, "Time spent pushing initially enclosed non-boss enemies outside the shield, including flying enemies.");
            intercept = B(ShieldName, "Block Enemy Projectiles", true, "Intercept hostile projectiles entering the bubble.");

            fieldTime = N(FieldName, "Duration (s)", 20f, "Field lifetime after the projectile lands.");
            fieldCooldown = N(FieldName, "Cooldown (s)", 20f, "Cooldown starts when the field expires.");
            fieldInterval = N(FieldName, "Pulse Interval (s)", 5f, "Time between recharge pulses.");
            fieldReduction = N(FieldName, "Cooldown Reduction Per Pulse (s)", 1f, "Advance allies' secondary, utility and special cooldowns.");
            fieldBarrier = N(FieldName, "Barrier Per Kill (%)", 200f, "Barrier granted to each ally inside per enemy killed inside, as a percentage of your damage stat.");
            podSpeed = N(FieldName, "Projectile Speed (m/s)", 90f, "Speed of the field deployment projectile.");
            podLifetime = N(FieldName, "Projectile Lifetime (s)", 5f, "Time the projectile can travel before fizzling.");
            podHitRadius = N(FieldName, "Projectile Hit Radius (m)", .4f, "Radius used to detect enemies along the beacon's flight.");
            podDamage = N(FieldName, "Piercing Damage (%)", 200f, "Damage dealt once to each ordinary enemy pierced by the beacon.");
            podBigDamage = N(FieldName, "Large Enemy Explosion Damage (%)", 600f, "Damage dealt when the beacon hits a boss or large enemy and explodes instead of deploying.");
            podExplosionRadius = N(FieldName, "Large Enemy Explosion Radius (m)", 6f, "Radius of the explosion caused by hitting a boss or large enemy.");
            podLargeRadius = N(FieldName, "Large Enemy Size Threshold (m)", 3f, "Minimum body radius that causes the beacon to explode. Bosses always trigger this explosion.");
            podProc = N(FieldName, "Projectile Proc Coefficient", 1f, "Proc coefficient of piercing hits and the large enemy explosion.");
            initialPulse = B(FieldName, "Pulse On Deployment", true, "Pulse immediately when the field deploys.");
            finalPulse = B(FieldName, "Pulse On Expiration", true, "Pulse once when the field expires.");
            refillPrimary = B(FieldName, "Refill Primary Stocks", true, "Restore all missing Primary stocks on each pulse.");
        }

        internal static BuffDef[] Buffs => shieldBuff ? new[] { shieldBuff } : Array.Empty<BuffDef>();

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("BarrierOrb", OrbName, OrbName, typeof(ChargeOrb), P, F(orbReload), null,
                customDefinition: ScriptableObject.CreateInstance<OrbSkill>());
            secondary = RemoteDroneSkills.MakeSkill("BarrierBeam", BeamName, BeamName, typeof(Beam), S, F(beamCooldown), null,
                customDefinition: ScriptableObject.CreateInstance<BeamSkill>());
            utility = RemoteDroneSkills.MakeSkill("BarrierShield", ShieldName, ShieldName, typeof(Shield), U, F(shieldCooldown), null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>());
            special = RemoteDroneSkills.MakeSkill("BarrierField", FieldName, FieldName, typeof(DeployField), X, F(fieldCooldown), null,
                beginsOnEnd: true, customDefinition: ScriptableObject.CreateInstance<FieldSkill>());
            secondary.mustKeyPress = utility.mustKeyPress = special.mustKeyPress = true;
            primary.resetCooldownTimerOnUse = false;
            shieldBuff = ScriptableObject.CreateInstance<BuffDef>();
            shieldBuff.name = "3QSBarrierDroneShieldProjection";
            shieldBuff.canStack = false;
            shieldBuff.buffColor = Aqua;
            Refresh();
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = F(orbReload);
            primary.stockToConsume = 0;
            secondary.baseRechargeInterval = F(beamCooldown);
            utility.baseRechargeInterval = F(shieldCooldown);
            special.baseRechargeInterval = F(fieldCooldown);
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"<style=cIsUtility>{(C.Get(rapidCharge) || C.Get(rapidReload) ? "Rapid. " : "")}</style>Charge an arcing orb for <style=cIsDamage>{F(minDamage):0.##}–{F(maxDamage):0.##}% damage</style> or grant allies <style=cIsHealing>{F(minBarrier):0.##}–{F(maxBarrier):0.##}% of your damage as barrier</style>. Explosions extend <style=cIsUtility>{F(orbBlastRadius):0.##}m</style> beyond the attached target's size. After a <style=cIsUtility>{F(orbDelay):0.##}s</style> detonation delay, it can bounce to <style=cIsUtility>{C.Get(minBounces)}–{C.Get(maxBounces)} more targets</style>. Overcharging or releasing without a target <style=cIsUtility>Shocks</style> nearby enemies without dealing damage. Releasing without a target <style=cDeath>disables this skill for {F(dischargeLockout):0.##}s</style>. Overcharging <style=cDeath>disables all skills for {F(dischargeLockout):0.##}s</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Link to an ally and grant <style=cIsHealing>{F(beamBarrier):0.##}% of your damage as barrier</style> over <style=cIsUtility>{F(beamTime):0.##}s</style>. You can use other skills while linked.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Project a moving shield for <style=cIsUtility>{F(shieldTime):0.##}s</style> that pushes nearby non-boss enemies outside on cast. Allies inside gain <style=cIsUtility>{F(shieldArmour):0.##} armour</style>, <style=cIsDamage>{F(shieldAttackSpeed):0.##}% attack speed</style>, and <style=cIsHealing>{F(shieldBarrier):0.##}% of your damage as barrier per second</style>."
                + (C.Get(intercept) ? " Blocks incoming enemy projectiles." : ""));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Launch a beacon that pierces enemies for <style=cIsDamage>{F(podDamage):0.##}% damage</style>. Hitting a boss or large enemy causes an explosion for <style=cIsDamage>{F(podBigDamage):0.##}% damage</style>. Landing on terrain deploys a support field for <style=cIsUtility>{F(fieldTime):0.##}s</style>. Every <style=cIsUtility>{F(fieldInterval):0.##}s</style>, {(C.Get(refillPrimary) ? "refill allied Primary stocks and " : "")}reduce other cooldowns by <style=cIsUtility>{F(fieldReduction):0.##}s</style>. Enemy kills inside grant allies <style=cIsHealing>{F(fieldBarrier):0.##}% of your damage as barrier</style>.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () =>
                $"\nCharge Time: {F(minCharge):0.##}–{F(maxCharge):0.##}s\nFull Charge Countdown: {F(fullHold):0.##}s\nBounce Range: {F(bounceRange):0.##}m\nBounce Speed: {F(bounceSpeed):0.##}m/s\nExtra Explosion Radius Beyond Target: {F(orbBlastRadius):0.##}m\nCharge Lockout: {F(orbLockout):0.##}s\nDischarge Radius: {F(dischargeRadius):0.##}m\nDischarge Shock Duration: {F(dischargeShock):0.##}s", () => F(orbProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () => $"\nTarget Range: {F(beamRange):0.##}m", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility, () => $"\nRadius: {F(shieldRadius):0.##}m", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () =>
                $"\nRadius: {RallyRadius():0.##}m\nExplosion Radius: {F(podExplosionRadius):0.##}m\nLarge Enemy Size Threshold: {F(podLargeRadius):0.##}m\nCooldown After Expiration: {F(fieldCooldown):0.##}s", () => F(podProc));
        }

        internal static void Install(GameObject body)
        {
            if (!primary) return;
            Sprite native = RemoteDroneSkills.VanillaIcon(body.name, "Primary", null);
            primary.icon = RemoteDroneSkills.VanillaIcon("MageBody", "Secondary", native);
            secondary.icon = native;
            utility.icon = RemoteDroneSkills.VanillaIcon("EngiBody", "Utility", native);
            var rally = SkillCatalog.allSkillDefs.FirstOrDefault(def =>
                def && def.activationState.stateType == typeof(EntityStates.Enforcer.CrowdControlLeap));
            special.icon = rally && rally.icon ? rally.icon : native;
            RoR2Application.onLoad += () =>
            {
                var rallyPoint = SkillCatalog.allSkillDefs.FirstOrDefault(def =>
                    def && def.activationState.stateType == typeof(EntityStates.Enforcer.CrowdControlLeap));
                if (rallyPoint && rallyPoint.icon) special.icon = rallyPoint.icon;
            };
            shieldBuff.iconSprite = utility.icon;
            if (orbOn.Value) { RemoteDroneSkills.AddMachine(body, P); RemoteDroneSkills.SetSkill(body, "Primary", primary); }
            if (beamOn.Value) { RemoteDroneSkills.AddMachine(body, S); RemoteDroneSkills.SetSkill(body, "Secondary", secondary); }
            if (shieldOn.Value) { RemoteDroneSkills.AddMachine(body, U); RemoteDroneSkills.SetSkill(body, "Utility", utility); }
            if (fieldOn.Value) { RemoteDroneSkills.AddMachine(body, X); RemoteDroneSkills.SetSkill(body, "Special", special); }
            if (!body.GetComponent<Control>()) body.AddComponent<Control>();

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteBarrierDrone");
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)),
                postfix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterStats)));
            harmony.Patch(AccessTools.Method(typeof(GlobalEventManager), "HandleCharacterDeath"),
                postfix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterDeath)));
            harmony.Patch(AccessTools.Method(typeof(ProjectileController), "Start"),
                postfix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterProjectileStart)));
            harmony.Patch(AccessTools.Method(typeof(ProjectileController), nameof(ProjectileController.DisconnectFromGhost)),
                prefix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(BeforeGhostDisconnect)));
            harmony.Patch(AccessTools.Method(typeof(ProjectileManager), "InitializeProjectile"),
                postfix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterProjectileInitialized)));
            harmony.Patch(AccessTools.Method(typeof(EntityStates.Engi.EngiBubbleShield.Deployed), "FixedUpdate"),
                prefix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(BeforeDomeTick)),
                finalizer: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterDomeTick)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                prefix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(BeforeSkillCheck)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)),
                prefix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(BeforeSkillCheck)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(GenericSkill), nameof(GenericSkill.icon)),
                postfix: new HarmonyMethod(typeof(RemoteBarrierDrone), nameof(AfterSkillIcon)));
        }

        private static bool BeforeSkillCheck(GenericSkill __instance, ref bool __result)
        {
            var control = __instance.GetComponent<Control>();
            if (!control || !control.IsLocked(__instance)) return true;
            __result = false;
            return false;
        }

        private static void AfterSkillIcon(GenericSkill __instance, ref Sprite __result)
        {
            var control = __instance.GetComponent<Control>();
            if (!control || !control.IsLocked(__instance)) return;
            if (!lockoutIcon)
                lockoutIcon = SkillCatalog.allSkillDefs.OfType<RailgunSkillDef>()
                    .Select(skill => skill.offlineIcon).FirstOrDefault(icon => icon);
            SkillDef disabled = CharacterBody.CommonAssets.disabledSkill;
            if (lockoutIcon) __result = lockoutIcon;
            else if (disabled && disabled.icon) __result = disabled.icon;
        }

        private static void ResolveOverchargeAudio()
        {
            if (overchargeAudioLoaded) return;
            var offline = EntityStateCatalog.InstantiateState(typeof(EntityStates.Railgunner.Backpack.Offline))
                as EntityStates.Railgunner.Backpack.Offline;
            var reboot = EntityStateCatalog.InstantiateState(typeof(EntityStates.Railgunner.Backpack.Reboot))
                as EntityStates.Railgunner.Backpack.Reboot;
            if (offline != null)
            {
                overchargeLoop = offline.loopSoundDef;
                overchargeStartSound = offline.enterSoundString;
            }
            if (reboot != null) overchargeEndSound = reboot.enterSoundString;
            overchargeAudioLoaded = true;
        }

        private static GameObject ResolveEngineerShield()
        {
            if (nativeShield) return nativeShield;
            var state = EntityStateCatalog.InstantiateState(typeof(EntityStates.Engi.EngiWeapon.FireBubbleShield))
                as EntityStates.Engi.EngiWeapon.FireMines;
            if (state != null) nativeShield = ResolveStateObject(state, "projectilePrefab");
            return nativeShield;
        }

        private static bool TryLaunchDome(CharacterBody body)
        {
            GameObject shield = ResolveEngineerShield();
            if (!shield || !shield.GetComponent<ProjectileController>() || !ProjectileManager.instance)
            {
                if (!domeLookupWarning)
                    ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone: native Engineer bubble shield is unavailable. Using the fallback bubble.");
                domeLookupWarning = true;
                return false;
            }
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                projectilePrefab = shield, owner = body.gameObject, position = body.corePosition,
                rotation = Quaternion.identity, speedOverride = 0f, comboNumber = BarrierShieldCombo
            });
            return true;
        }

        private static void AfterProjectileStart(ProjectileController __instance)
        {
            if (__instance.combo != BarrierShieldCombo || !__instance.owner ||
                !__instance.owner.TryGetComponent<Control>(out _)) return;
            AttachDome(__instance);
        }

        private static void AfterProjectileInitialized(ProjectileController projectileController, FireProjectileInfo fireProjectileInfo)
        {
            if (!NetworkServer.active || fireProjectileInfo.comboNumber != BarrierShieldCombo ||
                fireProjectileInfo.projectilePrefab != ResolveEngineerShield() ||
                !fireProjectileInfo.owner || !fireProjectileInfo.owner.GetComponent<Control>()) return;
            AttachDome(projectileController);
        }

        private static void AttachDome(ProjectileController projectile)
        {
            var follow = projectile.GetComponent<NativeDome>();
            if (!follow) follow = projectile.gameObject.AddComponent<NativeDome>();
            follow.Initialize(projectile.owner.GetComponent<CharacterBody>());
        }

        private static void BeforeGhostDisconnect(ProjectileController __instance)
        {
            var dome = __instance.GetComponent<NativeDome>();
            if (dome) dome.RestoreGhost();
        }

        private static void BeforeDomeTick(EntityStates.Engi.EngiBubbleShield.Deployed __instance,
            out (bool Modified, float Lifetime, float Delay) __state)
        {
            __state = default;
            if (!__instance.outer || !__instance.outer.GetComponent<NativeDome>()) return;
            __state = (true, EntityStates.Engi.EngiBubbleShield.Deployed.lifetime,
                EntityStates.Engi.EngiBubbleShield.Deployed.delayToDeploy);
            EntityStates.Engi.EngiBubbleShield.Deployed.lifetime = Duration(shieldTime);
            EntityStates.Engi.EngiBubbleShield.Deployed.delayToDeploy = 0f;
        }

        private static Exception AfterDomeTick(Exception __exception, (bool Modified, float Lifetime, float Delay) __state)
        {
            if (__state.Modified)
            {
                EntityStates.Engi.EngiBubbleShield.Deployed.lifetime = __state.Lifetime;
                EntityStates.Engi.EngiBubbleShield.Deployed.delayToDeploy = __state.Delay;
            }
            return __exception;
        }

        private sealed class NativeDome : MonoBehaviour
        {
            private CharacterBody owner;
            private Rigidbody motor;
            private ChildLocator locator;
            private ProjectileController projectile;
            private ProjectileGhostController hiddenGhost;
            private Renderer[] ghostRenderers;
            private bool[] ghostRenderingOff;
            private Light[] ghostLights;
            private bool[] ghostLightEnabled;
            private bool sized, initialized, emitterHidden;
            private float age;

            internal void Initialize(CharacterBody source)
            {
                owner = source;
                motor = GetComponent<Rigidbody>();
                locator = GetComponent<ChildLocator>();
                projectile = GetComponent<ProjectileController>();
                var movement = GetComponent<ProjectileSimple>();
                if (movement) movement.enabled = false;
                var stick = GetComponent<ProjectileStickOnImpact>();
                if (stick) stick.enabled = false;
                foreach (Collider collider in GetComponents<Collider>()) collider.enabled = false;
                if (motor) motor.isKinematic = true;
                HideGhost();
                // ProjectileController.Start enables its launch colliders again.
                // Repeat the collider setup, but enter Deployed only once.
                if (initialized) return;
                initialized = true;
                if (NetworkServer.active)
                {
                    var machine = GetComponent<EntityStateMachine>();
                    if (machine) machine.SetState(EntityStateCatalog.InstantiateState(typeof(EntityStates.Engi.EngiBubbleShield.Deployed)));
                }
            }

            private void FixedUpdate()
            {
                if (!Alive(owner))
                {
                    if (NetworkServer.active) Destroy(gameObject);
                    return;
                }
                if (NetworkServer.active)
                {
                    age += Time.fixedDeltaTime;
                    if (age >= Duration(shieldTime)) { Destroy(gameObject); return; }
                    if (motor) motor.position = owner.corePosition;
                    transform.position = owner.corePosition;
                }
                if (sized || !locator) return;
                string childName = EntityStates.Engi.EngiBubbleShield.Deployed.childLocatorString;
                Transform child = string.IsNullOrEmpty(childName) ? null : locator.FindChild(childName);
                if (!child) return;
                if (!emitterHidden)
                {
                    // Keep the native shield and its projectile blockers active.
                    // Only hide the projector art outside the deployed shield child.
                    foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                        if (!renderer.transform.IsChildOf(child)) renderer.forceRenderingOff = true;
                    foreach (Light light in GetComponentsInChildren<Light>(true))
                        if (!light.transform.IsChildOf(child)) light.enabled = false;
                    emitterHidden = true;
                }
                if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                Collider[] blockers = child.GetComponentsInChildren<Collider>(true);
                float nativeRadius = 0f;
                foreach (Collider blocker in blockers)
                    if (blocker.enabled) nativeRadius = Mathf.Max(nativeRadius, blocker.bounds.extents.x, blocker.bounds.extents.z);
                if (nativeRadius <= .01f) return;
                sized = true;
                transform.localScale *= Mathf.Max(.01f, F(shieldRadius)) / nativeRadius;
                if (!C.Get(intercept))
                    foreach (Collider blocker in blockers) blocker.enabled = false;
            }

            private void LateUpdate()
            {
                if (!NetworkServer.active && owner) transform.position = owner.corePosition;
                HideGhost();
            }

            private void HideGhost()
            {
                ProjectileGhostController ghost = projectile ? projectile.ghost : null;
                if (ghost != hiddenGhost)
                {
                    RestoreGhost();
                    hiddenGhost = ghost;
                    if (!ghost) return;
                    ghostRenderers = ghost.GetComponentsInChildren<Renderer>(true);
                    ghostRenderingOff = ghostRenderers.Select(renderer => renderer.forceRenderingOff).ToArray();
                    ghostLights = ghost.GetComponentsInChildren<Light>(true);
                    ghostLightEnabled = ghostLights.Select(light => light.enabled).ToArray();
                }
                if (!hiddenGhost) return;
                foreach (Renderer renderer in ghostRenderers)
                    if (renderer) renderer.forceRenderingOff = true;
                foreach (Light light in ghostLights)
                    if (light) light.enabled = false;
            }

            internal void RestoreGhost()
            {
                // Restore the original flags before the native ghost is pooled.
                if (ghostRenderers != null)
                    for (int i = 0; i < ghostRenderers.Length; i++)
                        if (ghostRenderers[i]) ghostRenderers[i].forceRenderingOff = ghostRenderingOff[i];
                if (ghostLights != null)
                    for (int i = 0; i < ghostLights.Length; i++)
                        if (ghostLights[i]) ghostLights[i].enabled = ghostLightEnabled[i];
                hiddenGhost = null;
                ghostRenderers = null;
                ghostRenderingOff = null;
                ghostLights = null;
                ghostLightEnabled = null;
            }

            private void OnDisable() => RestoreGhost();
        }

        private static void AfterStats(CharacterBody __instance)
        {
            if (!shieldBuff || !__instance.HasBuff(shieldBuff)) return;
            ArmourProperty?.SetValue(__instance, __instance.armor + F(shieldArmour));
            AttackSpeedProperty?.SetValue(__instance, __instance.attackSpeed * (1f + F(shieldAttackSpeed) / 100f));
        }

        private static void AfterDeath(DamageReport __0)
        {
            DamageReport damageReport = __0;
            if (!NetworkServer.active || damageReport == null || !damageReport.victimBody) return;
            foreach (SupportZone field in fields.ToArray())
                if (field) field.RewardKill(damageReport);
        }

        internal static void RegisterPrefabs(ContentPack content)
        {
            if (!primary) return;
            var template = new GameObject("3QS Barrier Drone Visual Template");
            template.SetActive(false);
            template.AddComponent<NetworkIdentity>();
            var movement = template.AddComponent<NetworkTransform>();
            movement.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform;
            movement.sendInterval = .05f;
            template.AddComponent<Visual>();
            visualPrefab = R2API.PrefabAPI.InstantiateClone(template, "3QSBarrierDroneVisual", true);
            visualPrefab.SetActive(true);
            UnityEngine.Object.Destroy(template);
            content.networkedObjectPrefabs.Add(new[] { visualPrefab });
            launchSound = Sound("Launch", "Play_railgunner_R_preFire");
            fizzleSound = Sound("Fizzle", "Play_drifter_repossess_fail");
            dischargeSound = Sound("Discharge", "Play_Char_Enforcer_StunNetRounds_Hit_Shock");
            attachSound = Sound("Attach", "Play_Char_Enforcer_StunNetRounds_Hit_Damage");
            burstSound = Sound("Burst", "Play_item_proc_meteorAttackOnHighDamage_impact");
            bounceSound = Sound("Bounce", "Play_GG_CHAR_Drifter_AltSec_JumpPad_Bounce");
            beamSound = Sound("Beam", "Play_seeker_skill1_fire_orb");
            beaconSound = Sound("BeaconLaunch", "Play_commando_M2");
            pulseSound = Sound("BeaconPulse", "Play_item_proc_TPhealingNova_hitPlayer");
            content.networkSoundEventDefs.Add(new[] {
                launchSound, fizzleSound, dischargeSound, attachSound, burstSound, bounceSound, beamSound, beaconSound, pulseSound
            });
        }

        private static NetworkSoundEventDef Sound(string name, string eventName)
        {
            var sound = ScriptableObject.CreateInstance<NetworkSoundEventDef>();
            sound.name = "3QSBarrierDrone" + name;
            sound.eventName = eventName;
            return sound;
        }

        private static void Emit(NetworkSoundEventDef sound, Vector3 position)
        {
            if (sound && NetworkServer.active) PointSoundManager.EmitSoundServer(sound.index, position);
        }

        private static bool Alive(CharacterBody body) => body && body.healthComponent && body.healthComponent.alive;
        private static TeamIndex Team(CharacterBody body) => TeamComponent.GetObjectTeam(body.gameObject);
        private static bool Allied(CharacterBody owner, CharacterBody target) => Alive(target) && Team(owner) == Team(target);
        private static bool ValidTarget(CharacterBody owner, CharacterBody target, bool allyOnly)
        {
            if (!Alive(target) || target == owner) return false;
            if (Team(owner) == Team(target)) return true;
            return !allyOnly && TeamMask.GetEnemyTeams(Team(owner)).HasTeam(Team(target));
        }

        private static HurtBox FindTarget(CharacterBody owner, Ray aim, float range, bool allyOnly)
        {
            TeamIndex team = Team(owner);
            TeamMask mask = allyOnly ? TeamMask.none : TeamMask.GetEnemyTeams(team);
            mask.AddTeam(team);
            var search = new BullseyeSearch {
                teamMaskFilter = mask, searchOrigin = aim.origin, searchDirection = aim.direction,
                maxDistanceFilter = range, maxAngleFilter = 20f, filterByLoS = true,
                sortMode = BullseyeSearch.SortMode.Angle
            };
            search.RefreshCandidates();
            return search.GetResults().FirstOrDefault(h => h && h.healthComponent && ValidTarget(owner, h.healthComponent.body, allyOnly));
        }

        private static GameObject ResolveRallyBeacon()
        {
            if (rallyBeacon) return rallyBeacon;
            try
            {
                var state = new EntityStates.Enforcer.CrowdControlSlamGround();
                rallyBeacon = ResolveStateObject(state, "supportBeaconPrefab");
                rallySpawnEffect = ResolveStateObject(state, "supportBeaconSpawnVFXPrefab");
            }
            catch (Exception error)
            {
                if (!beaconLookupWarning) ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone Rally Point: " + error.Message);
                beaconLookupWarning = true;
            }
            if (!rallyBeacon && !beaconLookupWarning)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone: Enforcer's Rally Point beacon is unavailable.");
                beaconLookupWarning = true;
            }
            return rallyBeacon;
        }

        private static GameObject ResolveStateObject(EntityState state, string name)
        {
            GameObject asset = RemoteDroneSkills.NativeGameObject(state, name);
            if (asset) return asset;
            FieldInfo member = AccessTools.Field(state.GetType(), name);
            if (member == null) return null;
            var config = Resources.FindObjectsOfTypeAll<EntityStateConfiguration>()
                .FirstOrDefault(item => item && (Type)item.targetType == state.GetType());
            if (!config) return null;
            foreach (var field in config.serializedFieldsCollection.serializedFields)
            {
                if (field.fieldName != member.Name) continue;
                object reference = field.fieldValue.GetValue(member);
                MethodInfo convert = reference?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "op_Implicit" && method.ReturnType == typeof(GameObject));
                return reference as GameObject ?? convert?.Invoke(null, new[] { reference }) as GameObject;
            }
            return null;
        }

        private static float RallyRadius()
        {
            GameObject beacon = ResolveRallyBeacon();
            var zone = beacon ? beacon.GetComponent<HoldoutZoneController>() : null;
            return Mathf.Max(0f, Tweaks.Survivors.Enforcer.RallyPointRadius(zone ? zone.baseRadius : 20f));
        }

        private static float BeaconRetireDuration()
        {
            float length = 0f;
            GameObject beacon = ResolveRallyBeacon();
            if (beacon)
                foreach (Animator animator in beacon.GetComponentsInChildren<Animator>(true))
                    if (animator.runtimeAnimatorController)
                        foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                            if (clip) length = Mathf.Max(length, clip.length);
            // Allow the native Deactivate clip and its transition to finish.
            return length + BeaconTransitionTime;
        }

        private sealed class OrbSkill : SkillDef
        {
            public override bool CanExecute(GenericSkill slot) => CanCharge(slot) && base.CanExecute(slot);
            public override bool IsReady(GenericSkill slot) => CanCharge(slot) && base.IsReady(slot);
            public override int GetRechargeStock(GenericSkill slot) => Mathf.Max(1, slot.maxStock);
            public override void OnFixedUpdate(GenericSkill slot, float deltaTime)
            {
                var control = slot.GetComponent<Control>();
                if (!control || !control.Charging) base.OnFixedUpdate(slot, deltaTime);
            }
            public override float GetRechargeRate(GenericSkill slot) =>
                base.GetRechargeRate(slot) * (C.Get(rapidReload) && slot.characterBody ? Mathf.Max(.01f, slot.characterBody.attackSpeed) : 1f);
            private static bool CanCharge(GenericSkill slot)
            {
                Control control = slot.GetComponent<Control>();
                return control && !control.Charging && Time.fixedTime >= control.NextCharge &&
                    Run.FixedTimeStamp.now.t >= control.DischargeUntil;
            }
        }

        private sealed class BeamSkill : SkillDef
        {
            public override bool IsReady(GenericSkill slot) => base.IsReady(slot) && HasTarget(slot);
            public override bool CanExecute(GenericSkill slot) => base.CanExecute(slot) && HasTarget(slot);
            public override EntityState InstantiateNextState(GenericSkill slot)
            {
                var input = slot.GetComponent<InputBankTest>();
                HurtBox target = input && slot.characterBody ? FindTarget(slot.characterBody, input.GetAimRay(), F(beamRange), true) : null;
                var state = (Beam)base.InstantiateNextState(slot);
                state.Chosen = target ? target.healthComponent.body.gameObject : null;
                return state;
            }
            private static bool HasTarget(GenericSkill slot)
            {
                var input = slot.GetComponent<InputBankTest>();
                return input && slot.characterBody && FindTarget(slot.characterBody, input.GetAimRay(), F(beamRange), true);
            }
        }

        private sealed class FieldSkill : SkillDef
        {
            public override bool CanExecute(GenericSkill slot) => base.CanExecute(slot) && !IsAlreadyInState(slot);
            public override bool IsReady(GenericSkill slot) => base.IsReady(slot) && !IsAlreadyInState(slot);
            public override EntityState InstantiateNextState(GenericSkill slot)
            {
                var input = slot.GetComponent<InputBankTest>();
                Ray aim = input ? input.GetAimRay() : new Ray(slot.transform.position, slot.transform.forward);
                var state = (DeployField)base.InstantiateNextState(slot);
                state.Direction = aim.direction;
                return state;
            }
        }

        private sealed class Control : NetworkBehaviour
        {
            internal bool Charging, Pending, FieldActive;
            internal float NextCharge;
            internal float DischargeUntil;
            private float overchargeUntil;
            internal bool AllSkillsLocked => Run.FixedTimeStamp.now.t < overchargeUntil;
            private OrbChain initialChain;
            private CharacterBody initialTarget;
            private CharacterBody body;
            private InputBankTest input;
            private Indicator indicator;
            private float nextScan;
            private bool wasOvercharged;
            private LoopSoundManager.SoundLoopPtr overchargeLoopHandle;

            private void Awake()
            {
                body = GetComponent<CharacterBody>();
                input = GetComponent<InputBankTest>();
            }

            internal void SetPending(bool pending)
            {
                Pending = pending;
                if (NetworkServer.active) SetDirtyBit(1u);
            }

            internal void BeginInitial(OrbChain chain, CharacterBody target)
            {
                initialChain = chain;
                initialTarget = target;
                SetPending(true);
            }

            internal void EndInitial(OrbChain chain)
            {
                if (initialChain != chain) return;
                initialChain = null;
                initialTarget = null;
                SetPending(false);
            }

            internal void SetFieldActive(bool active)
            {
                FieldActive = active;
                if (NetworkServer.active) SetDirtyBit(2u);
            }

            internal bool IsLocked(GenericSkill slot) => AllSkillsLocked ||
                (body && body.skillLocator && slot == body.skillLocator.primary && Run.FixedTimeStamp.now.t < DischargeUntil);

            internal void LockAfterDischarge(bool overcharged)
            {
                DischargeUntil = Run.FixedTimeStamp.now.t + F(dischargeLockout);
                if (overcharged) overchargeUntil = DischargeUntil;
                if (NetworkServer.active) SetDirtyBit(4u);
                UpdateLockoutAudio();
            }

            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                uint bits = initialState ? 7u : syncVarDirtyBits;
                if (!initialState) writer.WritePackedUInt32(bits);
                if ((bits & 1u) != 0u) writer.Write(Pending);
                if ((bits & 2u) != 0u) writer.Write(FieldActive);
                if ((bits & 4u) != 0u)
                {
                    writer.Write(DischargeUntil);
                    writer.Write(overchargeUntil);
                }
                return bits != 0u;
            }

            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                uint bits = initialState ? 7u : reader.ReadPackedUInt32();
                if ((bits & 1u) != 0u) Pending = reader.ReadBoolean();
                if ((bits & 2u) != 0u) FieldActive = reader.ReadBoolean();
                if ((bits & 4u) != 0u)
                {
                    DischargeUntil = reader.ReadSingle();
                    overchargeUntil = reader.ReadSingle();
                    UpdateLockoutAudio();
                }
            }

            private void FixedUpdate()
            {
                UpdateLockoutAudio();
                if (NetworkServer.active && initialChain != null && !Alive(initialTarget))
                    EndInitial(initialChain);
                if (!body || !body.hasEffectiveAuthority || !input || AllSkillsLocked)
                {
                    if (indicator != null) indicator.active = false;
                    return;
                }
                if (Time.fixedTime < nextScan) return;
                nextScan = Time.fixedTime + .06f;
                if (indicator == null)
                {
                    GameObject marker = LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator");
                    if (marker) indicator = new Indicator(gameObject, marker);
                }
                if (indicator == null) return;
                var slots = body.skillLocator;
                bool beamReady = slots && slots.secondary && slots.secondary.skillDef == secondary &&
                    slots.secondary.stock >= secondary.requiredStock;
                bool orbReady = slots && slots.primary && slots.primary.skillDef == primary &&
                    slots.primary.stock >= primary.requiredStock && Time.fixedTime >= NextCharge &&
                    Run.FixedTimeStamp.now.t >= DischargeUntil;
                Ray aim = input.GetAimRay();
                HurtBox target = (Charging || orbReady) ? FindTarget(body, aim, F(orbRange), false) : null;
                if (!Charging && beamReady)
                    target = FindTarget(body, aim, F(beamRange), true) ?? target;
                indicator.targetTransform = target ? target.transform : null;
                indicator.active = target;
            }

            private void UpdateLockoutAudio()
            {
                if (!NetworkClient.active) return;
                bool locked = Alive(body) && AllSkillsLocked;
                if (locked == wasOvercharged) return;
                wasOvercharged = locked;
                ResolveOverchargeAudio();
                if (locked)
                {
                    if (!string.IsNullOrEmpty(overchargeStartSound)) Util.PlaySound(overchargeStartSound, gameObject);
                    if (overchargeLoop) overchargeLoopHandle = LoopSoundManager.PlaySoundLoopLocal(gameObject, overchargeLoop);
                }
                else
                {
                    StopOverchargeLoop();
                    if (Alive(body) && !string.IsNullOrEmpty(overchargeEndSound)) Util.PlaySound(overchargeEndSound, gameObject);
                }
            }

            private void StopOverchargeLoop()
            {
                if (overchargeLoopHandle.isValid) LoopSoundManager.StopSoundLoopLocal(overchargeLoopHandle);
                overchargeLoopHandle = default;
            }

            private void OnDisable()
            {
                if (indicator != null) indicator.active = false;
                StopOverchargeLoop();
                wasOvercharged = false;
            }
        }

        public sealed class ChargeOrb : BaseState
        {
            private Control control;
            private bool released, overcharged;
            private float charge, scaledTime;
            private float chargedAt = -1f;
            private uint chargePlaying, holdPlaying;
            private GameObject chosen;
            public override void OnEnter()
            {
                base.OnEnter();
                control = GetComponent<Control>();
                if (control) control.Charging = true;
                if (isAuthority) chargePlaying = Util.PlaySound(ChargeSound, gameObject);
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority || !inputBank || !control) return;
                if (!released)
                {
                    scaledTime += Time.fixedDeltaTime * (C.Get(rapidCharge) ? Mathf.Max(.01f, attackSpeedStat) : 1f);
                    float maximum = Mathf.Max(F(minCharge), F(maxCharge));
                    charge = chargedAt >= 0f || maximum <= F(minCharge) ? 1f : Mathf.InverseLerp(F(minCharge), maximum, scaledTime);
                    if (charge >= 1f)
                    {
                        if (chargedAt < 0f)
                        {
                            chargedAt = fixedAge;
                            if (chargePlaying != 0u) AkSoundEngine.StopPlayingID(chargePlaying);
                            chargePlaying = 0u;
                            holdPlaying = Util.PlaySound(HoldSound, gameObject);
                        }
                        float countdown = F(fullHold);
                        float remaining = countdown > 0f ? Mathf.Clamp01(1f - (fixedAge - chargedAt) / countdown) : 0f;
                        RemoteDroneSkills.ChargeDisplay(this, remaining, tint: Color.Lerp(new Color(1f, .27f, .12f), Gold, remaining));
                        if (inputBank.skill1.down && remaining > 0f) return;
                        overcharged = remaining <= 0f;
                    }
                    else
                    {
                        RemoteDroneSkills.ChargeDisplay(this, charge, tint: Color.Lerp(new Color(.35f, .57f, 1f), Gold, charge));
                        if (inputBank.skill1.down) return;
                    }
                    released = true;
                    StopChargeAudio();
                    RemoteDroneSkills.HideCharge(this);
                    HurtBox target = FindTarget(characterBody, GetAimRay(), F(orbRange), false);
                    chosen = target ? target.healthComponent.body.gameObject : null;
                }
                if (!control.Pending || overcharged || !chosen)
                    outer.SetNextState(new FireOrb { Charge = charge, Chosen = chosen, Overcharged = overcharged });
            }

            public override void OnExit()
            {
                if (control) control.Charging = false;
                StopChargeAudio();
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }

            private void StopChargeAudio()
            {
                if (chargePlaying != 0u) AkSoundEngine.StopPlayingID(chargePlaying);
                if (holdPlaying != 0u)
                {
                    AkSoundEngine.StopPlayingID(holdPlaying);
                    Util.PlaySound(StopHoldSound, gameObject);
                }
                chargePlaying = holdPlaying = 0u;
            }
        }

        public sealed class FireOrb : BaseState
        {
            internal float Charge;
            internal GameObject Chosen;
            internal bool Overcharged;
            private bool fired;

            public override void OnSerialize(NetworkWriter writer)
            {
                base.OnSerialize(writer);
                writer.Write(Charge);
                writer.Write(Chosen);
                writer.Write(Overcharged);
            }
            public override void OnDeserialize(NetworkReader reader)
            {
                base.OnDeserialize(reader);
                Charge = reader.ReadSingle();
                Chosen = reader.ReadGameObject();
                Overcharged = reader.ReadBoolean();
            }
            public override void OnEnter()
            {
                base.OnEnter();
                TryFire();
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                TryFire();
            }
            private void TryFire()
            {
                if (fired || (!isAuthority && !NetworkServer.active)) return;
                var control = GetComponent<Control>();
                if (!control) return;
                var target = Chosen ? Chosen.GetComponent<CharacterBody>() : null;
                bool valid = !Overcharged && ValidTarget(characterBody, target, false) && target.mainHurtBox &&
                    (target.corePosition - characterBody.corePosition).sqrMagnitude <= F(orbRange) * F(orbRange);
                if (valid && control.Pending) return;
                fired = true;
                control.NextCharge = Time.fixedTime + F(orbLockout);
                var slot = skillLocator.primary;
                if (slot && slot.skillDef == primary)
                {
                    slot.DeductStock(1);
                    slot.rechargeStopwatch = 0f;
                }
                if (valid && NetworkServer.active && OrbManager.instance)
                {
                    Emit(launchSound, characterBody.corePosition);
                    float fraction = Mathf.Clamp01(Charge);
                    int minimum = Mathf.Max(0, C.Get(minBounces));
                    int maximum = Mathf.Max(minimum, C.Get(maxBounces));
                    int bounces = minimum + Mathf.FloorToInt(fraction * (maximum - minimum));
                    var chain = new OrbChain(characterBody, control, fraction, bounces);
                    control.BeginInitial(chain, target);
                    OrbManager.instance.AddOrb(new BarrierTravel {
                        origin = characterBody.corePosition, target = target.mainHurtBox, chain = chain
                    });
                }
                else
                {
                    if (!valid)
                    {
                        control.LockAfterDischarge(Overcharged);
                        if (NetworkServer.active) Discharge(characterBody);
                    }
                    else control.SetPending(!NetworkServer.active);
                }
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        private sealed class OrbChain
        {
            internal readonly CharacterBody Owner;
            internal readonly Control Control;
            internal readonly HashSet<HealthComponent> Hit = new HashSet<HealthComponent>();
            internal readonly float Damage, Barrier, Proc, Delay, Speed, BounceSpeed, Range, BlastRadius;
            internal readonly bool Crit;
            internal int Bounces;
            internal bool Initial = true;

            internal OrbChain(CharacterBody owner, Control control, float charge, int bounces)
            {
                Owner = owner;
                Control = control;
                Damage = owner.damage * Mathf.Lerp(F(minDamage), F(maxDamage), charge) / 100f;
                Barrier = owner.damage * Mathf.Lerp(F(minBarrier), F(maxBarrier), charge) / 100f;
                Proc = F(orbProc);
                Delay = F(orbDelay);
                Speed = Mathf.Max(1f, F(orbSpeed));
                BounceSpeed = Mathf.Max(1f, F(bounceSpeed));
                Range = F(bounceRange);
                BlastRadius = F(orbBlastRadius);
                Crit = Util.CheckRoll(owner.crit, owner.master);
                Bounces = bounces;
            }

            internal void ReleaseInitial()
            {
                if (!Initial) return;
                Initial = false;
                if (Control) Control.EndInitial(this);
            }
        }

        private sealed class BarrierTravel : Orb
        {
            internal OrbChain chain;
            private Visual travel;
            public override void Begin()
            {
                float length = 0f;
                if (target)
                {
                    Vector3 previous = origin;
                    for (int i = 1; i <= 12; i++)
                    {
                        Vector3 point = ArcPoint(origin, target.transform.position, i / 12f);
                        length += Vector3.Distance(previous, point);
                        previous = point;
                    }
                }
                float speed = chain.Initial ? chain.Speed : chain.BounceSpeed;
                duration = Mathf.Max(chain.Initial ? .02f : F(bounceMinTime), length / speed);
                CharacterBody body = target && target.healthComponent ? target.healthComponent.body : null;
                if (body) travel = SpawnVisual(VisualKind.TravelOrb, chain.Owner, body, origin, .58f, duration);
            }
            public override void OnArrival()
            {
                if (travel) NetworkServer.Destroy(travel.gameObject);
                CharacterBody body = target && target.healthComponent ? target.healthComponent.body : null;
                if (!Alive(chain.Owner) || !ValidTarget(chain.Owner, body, false))
                {
                    if (chain.Initial && Alive(chain.Owner)) Emit(fizzleSound, chain.Owner.corePosition);
                    chain.ReleaseInitial();
                    return;
                }
                Emit(attachSound, body.corePosition);
                Visual marker = SpawnVisual(VisualKind.AttachedOrb, chain.Owner, body, body.corePosition, 1f, chain.Delay);
                OrbManager.instance.AddOrb(new DetonateOrb { chain = chain, target = target, marker = marker });
            }
        }

        private sealed class DetonateOrb : Orb
        {
            internal OrbChain chain;
            internal Visual marker;
            public override void Begin() { duration = chain.Delay; }
            public override void OnArrival()
            {
                if (marker) NetworkServer.Destroy(marker.gameObject);
                bool initial = chain.Initial;
                chain.ReleaseInitial();
                CharacterBody victim = target && target.healthComponent ? target.healthComponent.body : null;
                if (!Alive(chain.Owner) || !ValidTarget(chain.Owner, victim, false) || !chain.Hit.Add(victim.healthComponent))
                {
                    if (initial && Alive(chain.Owner)) Emit(fizzleSound, chain.Owner.corePosition);
                    return;
                }
                Bounds bounds = BodyBounds(victim);
                Vector3 point = bounds.center;
                float blastRadius = chain.BlastRadius + BodyRadius(victim, bounds);
                var victims = new List<HealthComponent>();
                bool ally = Allied(chain.Owner, victim);
                if (!ally) victims.Add(victim.healthComponent);
                if (blastRadius > 0f)
                {
                    var search = new SphereSearch {
                        origin = point, radius = blastRadius, mask = LayerIndex.entityPrecise.mask
                    };
                    search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(Team(chain.Owner)))
                        .FilterCandidatesByDistinctHurtBoxEntities();
                    foreach (HurtBox box in search.GetHurtBoxes())
                    {
                        HealthComponent health = box.healthComponent;
                        if (health && health.alive && chain.Hit.Add(health)) victims.Add(health);
                    }
                }
                Emit(burstSound, point);
                Visual flash = SpawnVisual(VisualKind.Burst, chain.Owner, victim, point, blastRadius, .4f);
                if (flash) UnityEngine.Object.Destroy(flash.gameObject, .5f);
                GameObject explosion = ResolveOrbExplosion();
                if (explosion) EffectManager.SpawnEffect(explosion, new EffectData {
                    origin = point, scale = Mathf.Max(1f, blastRadius)
                }, true);
                if (ally) victim.healthComponent.AddBarrier(chain.Barrier);
                foreach (HealthComponent health in victims)
                {
                    var info = new DamageInfo {
                        attacker = chain.Owner.gameObject, damage = chain.Damage, crit = chain.Crit,
                        procCoefficient = chain.Proc, position = point
                    };
                    health.TakeDamage(info);
                    if (GlobalEventManager.instance)
                    {
                        GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
                        GlobalEventManager.instance.OnHitAll(info, health.gameObject);
                    }
                }
                if (chain.Bounces <= 0 || chain.Range <= 0f) return;
                float rangeSquared = chain.Range * chain.Range;
                CharacterBody next = CharacterBody.readOnlyInstancesList
                    .Where(body => ValidTarget(chain.Owner, body, false) && body.mainHurtBox &&
                        !chain.Hit.Contains(body.healthComponent) && (body.corePosition - point).sqrMagnitude <= rangeSquared)
                    .OrderBy(body => (body.corePosition - point).sqrMagnitude).FirstOrDefault();
                if (!next) return;
                chain.Bounces--;
                Emit(bounceSound, point);
                OrbManager.instance.AddOrb(new BarrierTravel { origin = point, target = next.mainHurtBox, chain = chain });
            }
        }

        private static Vector3 ArcPoint(Vector3 from, Vector3 to, float progress)
        {
            float height = Mathf.Min(F(orbArcHeight), Vector3.Distance(from, to) * .65f);
            return Vector3.Lerp(from, to, progress) + Vector3.up * (4f * height * progress * (1f - progress));
        }

        private static Bounds BodyBounds(CharacterBody body, Renderer[] renderers = null)
        {
            Bounds bounds = new Bounds(body.corePosition, Vector3.one * Mathf.Max(.5f, body.radius * 2f));
            if (renderers == null && body.modelLocator && body.modelLocator.modelTransform)
                renderers = body.modelLocator.modelTransform.GetComponentsInChildren<Renderer>();
            bool found = false;
            if (renderers != null)
                foreach (Renderer renderer in renderers)
                {
                    if (!renderer || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer) || !renderer.enabled ||
                        !renderer.gameObject.activeInHierarchy) continue;
                    if (!found) bounds = renderer.bounds;
                    else bounds.Encapsulate(renderer.bounds);
                    found = true;
                }
            if (!found && body.mainHurtBox)
            {
                Collider collider = body.mainHurtBox.GetComponent<Collider>();
                if (collider) bounds = collider.bounds;
            }
            return bounds;
        }

        private static float BodyRadius(CharacterBody body, Bounds bounds) =>
            Mathf.Max(body.radius, bounds.extents.x, bounds.extents.y, bounds.extents.z);

        private static void Discharge(CharacterBody owner)
        {
            Vector3 point = owner.corePosition;
            Emit(dischargeSound, point);
            Visual pulse = SpawnVisual(VisualKind.Discharge, owner, null, point, F(dischargeRadius), .5f);
            if (pulse) UnityEngine.Object.Destroy(pulse.gameObject, .65f);
            GameObject explosion = ResolveOrbExplosion();
            if (explosion) EffectManager.SpawnEffect(explosion, new EffectData {
                origin = point, scale = Mathf.Max(1f, owner.radius * 2f)
            }, true);
            if (F(dischargeShock) <= 0f || F(dischargeRadius) <= 0f) return;
            var search = new SphereSearch {
                origin = point, radius = F(dischargeRadius), mask = LayerIndex.entityPrecise.mask
            };
            search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(Team(owner)))
                .FilterCandidatesByDistinctHurtBoxEntities();
            foreach (HurtBox box in search.GetHurtBoxes())
            {
                HealthComponent health = box.healthComponent;
                if (!health || !health.alive) continue;
                var reaction = health.GetComponent<SetStateOnHurt>();
                if (reaction) reaction.SetShock(F(dischargeShock));
            }
        }

        private static GameObject ResolveOrbExplosion()
        {
            if (nativeOrbExplosion) return nativeOrbExplosion;
            GameObject projectile = ResolveStateObject(new EntityStates.Mage.Weapon.FireLightningBolt(), "projectilePrefab");
            var explosion = projectile ? projectile.GetComponent<ProjectileImpactExplosion>() : null;
            if (explosion) nativeOrbExplosion = explosion.impactEffect;
            return nativeOrbExplosion;
        }

        private static GameObject ResolveNativeBeam()
        {
            if (nativeBeam) return nativeBeam;
            try
            {
                nativeBeam = ResolveStateObject(new RechargeBeam(), "rechargeBeamPrefab");
            }
            catch (Exception e)
            {
                if (!beamLookupWarning) ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone beam: " + e.Message);
                beamLookupWarning = true;
            }
            if (!nativeBeam && !beamLookupWarning)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone: native RechargeBeam visual unavailable. Using the networked beam fallback.");
                beamLookupWarning = true;
            }
            return nativeBeam;
        }

        public sealed class Beam : BaseState
        {
            internal GameObject Chosen;
            private CharacterBody recipient;
            private HealBeamController native;
            private Visual fallback;
            private float lifetime, rate, applied;
            private bool stopped;

            public override void OnSerialize(NetworkWriter writer)
            {
                base.OnSerialize(writer);
                writer.Write(Chosen);
            }
            public override void OnDeserialize(NetworkReader reader)
            {
                base.OnDeserialize(reader);
                Chosen = reader.ReadGameObject();
            }

            public override void OnEnter()
            {
                base.OnEnter();
                lifetime = Duration(beamTime);
                rate = characterBody.damage * F(beamBarrier) / 100f / lifetime;
                recipient = Chosen ? Chosen.GetComponent<CharacterBody>() : null;
                if (!InRange()) return;
                PlayCrossfade("Gesture", "Heal", .2f);
                if (!NetworkServer.active) return;
                Emit(beamSound, characterBody.corePosition);
                GameObject prefab = ResolveNativeBeam();
                if (prefab && prefab.GetComponent<HealBeamController>() && prefab.GetComponent<NetworkIdentity>() &&
                    prefab.GetComponent<GenericOwnership>() && recipient.mainHurtBox)
                {
                    Transform muzzle = FindModelChild("Muzzle") ?? transform;
                    GameObject instance = Instantiate(prefab, muzzle);
                    native = instance.GetComponent<HealBeamController>();
                    native.healBarrier = true;
                    native.healRate = 0f;
                    native.tickInterval = 0f;
                    native.breakOnTargetFullyHealed = false;
                    native.isRechargeBeam = true;
                    native.target = recipient.mainHurtBox;
                    native.ownership.ownerObject = gameObject;
                    NetworkServer.Spawn(instance);
                }
                else fallback = SpawnVisual(VisualKind.Beam, characterBody, recipient, characterBody.corePosition, 0f);
            }

            private bool InRange()
            {
                var control = GetComponent<Control>();
                return (!control || !control.AllSkillsLocked) && ValidTarget(characterBody, recipient, true) &&
                    (recipient.corePosition - characterBody.corePosition).sqrMagnitude <= F(beamRange) * F(beamRange);
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                bool inRange = InRange();
                if (!stopped && NetworkServer.active && inRange)
                {
                    float elapsed = Mathf.Min(fixedAge, lifetime);
                    recipient.healthComponent.AddBarrier(Mathf.Max(0f, elapsed - applied) * rate);
                    applied = elapsed;
                }
                if (inRange && fixedAge < lifetime && !stopped) return;
                StopBeam();
                if (isAuthority) outer.SetNextStateToMain();
            }

            private void StopBeam()
            {
                if (stopped) return;
                stopped = true;
                if (!NetworkServer.active) return;
                if (native) native.BreakServer();
                if (fallback) NetworkServer.Destroy(fallback.gameObject);
            }

            public override void OnExit()
            {
                StopBeam();
                base.OnExit();
            }
        }

        public sealed class Shield : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                    SupportZone.Create(characterBody, characterBody.corePosition, true);
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        public sealed class DeployField : BaseState
        {
            internal Vector3 Direction;
            private Vector3 position;
            private float flightAge;
            private bool finishedFlight;
            private Control control;
            private Visual pod;
            private float damage;
            private bool crit;
            private readonly HashSet<HealthComponent> struck = new HashSet<HealthComponent>();
            private readonly List<(HurtBox Box, float Distance, Vector3 Point)> impacts =
                new List<(HurtBox, float, Vector3)>();

            public override void OnSerialize(NetworkWriter writer)
            {
                base.OnSerialize(writer);
                writer.Write(Direction);
            }
            public override void OnDeserialize(NetworkReader reader)
            {
                base.OnDeserialize(reader);
                Direction = reader.ReadVector3();
            }
            public override void OnEnter()
            {
                base.OnEnter();
                control = GetComponent<Control>();
                if (control && (isAuthority || NetworkServer.active)) control.SetFieldActive(true);
                position = characterBody.corePosition;
                Direction = Direction.sqrMagnitude > .001f ? Direction.normalized : GetAimRay().direction;
                if (NetworkServer.active)
                {
                    damage = characterBody.damage;
                    crit = characterBody.RollCrit();
                    Emit(beaconSound, position);
                    pod = SpawnVisual(VisualKind.Pod, characterBody, null, position, .7f,
                        rotation: Util.QuaternionSafeLookRotation(Direction));
                }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (NetworkServer.active && !finishedFlight)
                {
                    float dt = Mathf.Min(Time.fixedDeltaTime, Duration(podLifetime) - flightAge);
                    flightAge += dt;
                    float distance = Mathf.Max(1f, F(podSpeed)) * dt;
                    bool hitTerrain = Physics.Raycast(position, Direction, out RaycastHit ground, distance,
                        LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                    if (HitEnemies(hitTerrain ? ground.distance : distance))
                    {
                        if (control) control.SetFieldActive(false);
                    }
                    else if (hitTerrain)
                    {
                        EndFlight();
                        ResolveRallyBeacon();
                        if (rallySpawnEffect) EffectManager.SimpleEffect(rallySpawnEffect, ground.point, Quaternion.identity, true);
                        if (!SupportZone.Create(characterBody, ground.point, false) && control)
                            control.SetFieldActive(false);
                    }
                    else if (flightAge >= Duration(podLifetime))
                    {
                        EndFlight();
                        if (control) control.SetFieldActive(false);
                    }
                    else
                    {
                        position += Direction * distance;
                        if (pod) pod.transform.position = position;
                    }
                }
                if (isAuthority && (!control || !control.FieldActive)) outer.SetNextStateToMain();
            }

            private bool HitEnemies(float distance)
            {
                impacts.Clear();
                float radius = Mathf.Max(.01f, F(podHitRadius));
                foreach (Collider collider in Physics.OverlapSphere(position, radius,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    var box = collider.GetComponent<HurtBox>();
                    if (box) impacts.Add((box, 0f, collider.ClosestPoint(position)));
                }
                foreach (RaycastHit hit in Physics.SphereCastAll(position, radius, Direction, distance,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    var box = hit.collider.GetComponent<HurtBox>();
                    if (box) impacts.Add((box, hit.distance, hit.point));
                }
                impacts.Sort((left, right) => left.Distance.CompareTo(right.Distance));
                TeamMask enemies = TeamMask.GetEnemyTeams(Team(characterBody));
                foreach (var hit in impacts)
                {
                    HealthComponent health = hit.Box ? hit.Box.healthComponent : null;
                    CharacterBody body = health ? health.body : null;
                    if (!Alive(body) || !enemies.HasTeam(Team(body)) || !struck.Add(health)) continue;
                    if (body.isBoss || BodyRadius(body, BodyBounds(body)) >= F(podLargeRadius))
                    {
                        Explode(hit.Point, health);
                        return true;
                    }
                    DamageTarget(health, F(podDamage), hit.Point);
                }
                return false;
            }

            private void DamageTarget(HealthComponent health, float coefficient, Vector3 point)
            {
                if (!health || !health.alive) return;
                var info = new DamageInfo {
                    attacker = gameObject, damage = damage * coefficient / 100f,
                    crit = crit, procCoefficient = F(podProc), position = point
                };
                health.TakeDamage(info);
                if (GlobalEventManager.instance)
                {
                    GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
                    GlobalEventManager.instance.OnHitAll(info, health.gameObject);
                }
            }

            private void Explode(Vector3 point, HealthComponent directTarget)
            {
                EndFlight();
                float radius = F(podExplosionRadius);
                var victims = new List<HealthComponent> { directTarget };
                if (radius > 0f)
                {
                    var search = new SphereSearch {
                        origin = point, radius = radius, mask = LayerIndex.entityPrecise.mask
                    };
                    search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(Team(characterBody)))
                        .FilterCandidatesByDistinctHurtBoxEntities();
                    foreach (HurtBox box in search.GetHurtBoxes())
                        if (box.healthComponent && box.healthComponent != directTarget) victims.Add(box.healthComponent);
                }
                Emit(burstSound, point);
                Visual flash = SpawnVisual(VisualKind.Burst, characterBody, null, point, Mathf.Max(.5f, radius), .4f);
                if (flash) UnityEngine.Object.Destroy(flash.gameObject, .5f);
                GameObject effect = ResolveOrbExplosion();
                if (effect) EffectManager.SpawnEffect(effect, new EffectData {
                    origin = point, scale = Mathf.Max(1f, radius)
                }, true);
                foreach (HealthComponent victim in victims) DamageTarget(victim, F(podBigDamage), point);
            }

            private void EndFlight()
            {
                finishedFlight = true;
                if (pod) NetworkServer.Destroy(pod.gameObject);
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Death;
            public override void OnExit()
            {
                if (NetworkServer.active && !finishedFlight)
                {
                    EndFlight();
                    if (control) control.SetFieldActive(false);
                }
                base.OnExit();
            }
        }

        private static void Recharge(CharacterBody body, CharacterBody source)
        {
            var slots = body.skillLocator;
            if (!slots) return;
            GenericSkill main = slots.primary;
            if (C.Get(refillPrimary) && main && main.stock < main.maxStock)
            {
                main.stock = main.maxStock;
                main.rechargeStopwatch = 0f;
            }
            float reduction = F(fieldReduction);
            AdvanceCooldown(slots.secondary, reduction);
            AdvanceCooldown(slots.utility, reduction);
            if (slots.special && !(body == source && slots.special.skillDef == special))
                AdvanceCooldown(slots.special, reduction);
        }

        private static void AdvanceCooldown(GenericSkill slot, float seconds)
        {
            if (slot && slot.rechargeStock > 0)
                slot.RunRecharge(seconds, continuous: slot.rechargeStock == 1);
        }

        private enum VisualKind : byte { AttachedOrb, Beam, Shield, Field, Pod, TravelOrb, Burst, RechargePulse, BarrierPulse, Discharge }

        private static Visual SpawnVisual(VisualKind kind, CharacterBody owner, CharacterBody target, Vector3 position,
            float radius, float lifetime = 0f, Quaternion? rotation = null)
        {
            if (!visualPrefab || !NetworkServer.active) return null;
            GameObject instance = UnityEngine.Object.Instantiate(visualPrefab, position, rotation ?? Quaternion.identity);
            var visual = instance.GetComponent<Visual>();
            visual.Kind = kind;
            visual.Owner = owner;
            visual.Target = target;
            visual.Radius = radius;
            visual.Lifetime = lifetime;
            visual.StartedAt = Run.FixedTimeStamp.now.t;
            visual.Origin = position;
            NetworkServer.Spawn(instance);
            return visual;
        }

        private sealed class Visual : NetworkBehaviour
        {
            internal VisualKind Kind;
            internal CharacterBody Owner, Target;
            internal float Radius, Lifetime, StartedAt;
            internal Vector3 Origin;
            private LineRenderer beam;
            private LineRenderer orbTrail;
            private readonly Dictionary<CharacterBody, int> pendingPulses = new Dictionary<CharacterBody, int>();
            private readonly List<LineRenderer> orbRings = new List<LineRenderer>();
            private readonly List<LineRenderer> pulseRings = new List<LineRenderer>();
            private Renderer[] targetRenderers;
            private Vector3 orbExtents = Vector3.one;
            private float pulseFlashUntil, nextPulseDraw;
            private Renderer beaconRadius;
            private MaterialPropertyBlock radiusTint;
            private Color beaconRadiusColour;
            private readonly List<Animator> beaconAnimators = new List<Animator>();
            private bool beaconRetiring;

            internal void SendPulse(CharacterBody[] allies)
            {
                foreach (CharacterBody ally in allies)
                {
                    pendingPulses.TryGetValue(ally, out int count);
                    pendingPulses[ally] = count + 1;
                }
                pulseFlashUntil = Time.time + .25f;
                SetDirtyBit(1u);
            }

            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (initialState)
                {
                    writer.Write((byte)Kind);
                    writer.Write(Owner ? Owner.gameObject : null);
                    writer.Write(Target ? Target.gameObject : null);
                    writer.Write(Radius);
                    writer.Write(Lifetime);
                    writer.Write(StartedAt);
                    writer.Write(Origin);
                    return true;
                }
                writer.WritePackedUInt32(syncVarDirtyBits);
                if (syncVarDirtyBits == 0u) return false;
                writer.Write((ushort)pendingPulses.Count);
                foreach (var pair in pendingPulses)
                {
                    writer.Write(pair.Key ? pair.Key.gameObject : null);
                    writer.WritePackedUInt32((uint)pair.Value);
                }
                pendingPulses.Clear();
                return true;
            }

            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (initialState)
                {
                    Kind = (VisualKind)reader.ReadByte();
                    GameObject owner = reader.ReadGameObject();
                    GameObject target = reader.ReadGameObject();
                    Owner = owner ? owner.GetComponent<CharacterBody>() : null;
                    Target = target ? target.GetComponent<CharacterBody>() : null;
                    Radius = reader.ReadSingle();
                    Lifetime = reader.ReadSingle();
                    StartedAt = reader.ReadSingle();
                    Origin = reader.ReadVector3();
                    return;
                }
                if (reader.ReadPackedUInt32() == 0u) return;
                pulseFlashUntil = Time.time + .25f;
                int count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                {
                    GameObject ally = reader.ReadGameObject();
                    uint pulses = reader.ReadPackedUInt32();
                    if (!NetworkServer.active && ally)
                    {
                        var body = ally.GetComponent<CharacterBody>();
                        if (Alive(body) && body.hasEffectiveAuthority)
                            for (uint pulse = 0; pulse < pulses; pulse++) Recharge(body, Owner);
                    }
                }
            }

            public override void OnStartClient()
            {
                base.OnStartClient();
                if (Kind == VisualKind.Beam)
                {
                    beam = Line("Barrier Beam", Aqua, .13f, 2, false);
                    return;
                }
                if (Kind == VisualKind.RechargePulse || Kind == VisualKind.BarrierPulse || Kind == VisualKind.Discharge)
                {
                    Color colour = Kind == VisualKind.BarrierPulse ? Gold : Aqua;
                    for (int i = 0; i < 2; i++)
                        pulseRings.Add(Line("Recharge Field pulse", colour, .35f, 48, true));
                    return;
                }
                if (Kind == VisualKind.Pod || Kind == VisualKind.Field)
                {
                    AddBeaconModel();
                    return;
                }
                bool isOrb = Kind == VisualKind.TravelOrb || Kind == VisualKind.AttachedOrb || Kind == VisualKind.Burst;
                if (Kind == VisualKind.TravelOrb)
                {
                    orbTrail = Line("Orb arc trail", Gold, .1f, 12, false);
                    orbTrail.startColor = new Color(Gold.r, Gold.g, Gold.b, 0f);
                    orbTrail.endColor = Gold;
                }
                if ((Kind == VisualKind.AttachedOrb || Kind == VisualKind.Burst) && Target)
                {
                    Transform model = Target.modelLocator ? Target.modelLocator.modelTransform : null;
                    if (model) targetRenderers = model.GetComponentsInChildren<Renderer>()
                        .Where(renderer => renderer is SkinnedMeshRenderer || renderer is MeshRenderer).ToArray();
                    UpdateBodyBounds(true);
                }
                if (Kind == VisualKind.Burst) orbExtents = Vector3.one * Radius;
                Color color = isOrb ? Gold : Aqua;
                int rings = Kind == VisualKind.Shield ? 5 : 3;
                const int points = 72;
                for (int j = 0; j < rings; j++)
                {
                    var line = Line("Barrier Ring", color, Kind == VisualKind.Shield ? .13f : .075f, points, true);
                    if (isOrb) orbRings.Add(line);
                    for (int i = 0; i < points; i++)
                    {
                        float angle = i * 2f * Mathf.PI / points;
                        Vector3 point = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Radius;
                        if (j > 0) point = Quaternion.Euler(90f, (j - 1) * 180f / (rings - 1), 0f) * point;
                        line.SetPosition(i, point);
                    }
                }
            }

            private void AddBeaconModel()
            {
                GameObject prefab = ResolveRallyBeacon();
                if (!prefab) return;
                var modelRoot = new GameObject("Rally Point beacon model");
                modelRoot.SetActive(false);
                modelRoot.transform.SetParent(transform, false);
                GameObject model = Instantiate(prefab, modelRoot.transform, false);
                Animator[] animators = model.GetComponentsInChildren<Animator>(true);
                HoldoutZoneController[] holdouts = model.GetComponentsInChildren<HoldoutZoneController>(true);
                var mainHoldout = model.GetComponent<HoldoutZoneController>();
                if (!mainHoldout || !mainHoldout.radiusIndicator)
                    mainHoldout = holdouts.FirstOrDefault(holdout => holdout.radiusIndicator);
                foreach (HoldoutZoneController holdout in holdouts)
                    if (holdout.radiusIndicator) holdout.radiusIndicator.gameObject.SetActive(false);
                // Disable gameplay while the clone is inactive. Keep required component
                // dependencies intact so Unity can instantiate the beacon art safely.
                foreach (MonoBehaviour component in model.GetComponentsInChildren<MonoBehaviour>(true))
                    component.enabled = false;
                foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (Rigidbody rigidbody in model.GetComponentsInChildren<Rigidbody>(true))
                {
                    rigidbody.isKinematic = true;
                    rigidbody.detectCollisions = false;
                }
                if (Kind == VisualKind.Pod)
                {
                    modelRoot.transform.localScale = Vector3.one * .6f;
                    modelRoot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    foreach (Animator animator in animators) animator.speed = 0f;
                    foreach (Animation animation in model.GetComponentsInChildren<Animation>(true))
                        animation.enabled = false;
                    foreach (ParticleSystem particle in model.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = particle.main;
                        main.playOnAwake = false;
                        particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                    foreach (Light light in model.GetComponentsInChildren<Light>(true)) light.enabled = false;
                }
                modelRoot.SetActive(true);
                if (Kind == VisualKind.Field)
                    foreach (Animator animator in animators)
                        if (animator.isActiveAndEnabled && animator.HasState(0, BeaconIdle) && animator.HasState(0, BeaconDeactivate))
                            beaconAnimators.Add(animator);
                if (Kind == VisualKind.Field && mainHoldout && mainHoldout.radiusIndicator)
                {
                    beaconRadius = mainHoldout.radiusIndicator;
                    if (beaconRadius.sharedMaterial && beaconRadius.sharedMaterial.HasProperty("_TintColor"))
                    {
                        beaconRadiusColour = beaconRadius.sharedMaterial.GetColor("_TintColor");
                        radiusTint = new MaterialPropertyBlock();
                    }
                    UpdateBeaconRadius();
                }
                // The landed model is a fresh instance and plays its deployment.
                // Sample the launch model at its initial pose while it travels.
                if (Kind == VisualKind.Pod)
                    foreach (Animator animator in animators)
                        if (animator.isActiveAndEnabled && animator.runtimeAnimatorController)
                        {
                            animator.Rebind();
                            animator.Update(0f);
                        }
            }

            private void UpdateBeaconRadius()
            {
                if (!beaconRadius) return;
                bool active = Run.FixedTimeStamp.now.t - StartedAt < Lifetime;
                beaconRadius.gameObject.SetActive(active);
                beaconRadius.enabled = active;
                if (!active) return;
                Transform display = beaconRadius.transform;
                Vector3 parentScale = display.parent ? display.parent.lossyScale : Vector3.one;
                float diameter = 2f * Radius;
                display.localScale = new Vector3(
                    diameter / Mathf.Max(.001f, Mathf.Abs(parentScale.x)),
                    diameter / Mathf.Max(.001f, Mathf.Abs(parentScale.y)),
                    diameter / Mathf.Max(.001f, Mathf.Abs(parentScale.z)));
                if (radiusTint == null) return;
                float glow = Mathf.Clamp01((pulseFlashUntil - Time.time) * 4f);
                radiusTint.SetColor("_TintColor", Color.Lerp(beaconRadiusColour, Color.white, glow));
                beaconRadius.SetPropertyBlock(radiusTint);
            }

            private void UpdateBeaconAnimation(float age)
            {
                bool retiring = age >= Lifetime;
                foreach (Animator animator in beaconAnimators)
                {
                    if (!animator || !animator.isActiveAndEnabled) continue;
                    if (retiring)
                    {
                        if (!beaconRetiring) animator.CrossFadeInFixedTime(BeaconDeactivate, BeaconTransitionTime, 0, 0f);
                        continue;
                    }
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    bool deactivating = state.fullPathHash == BeaconDeactivate ||
                        (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == BeaconDeactivate);
                    // The native controller exits idle after eight cycles. Keep
                    // its idle clock within one cycle until gameplay expires.
                    if (deactivating || (state.fullPathHash == BeaconIdle && state.normalizedTime >= 1f))
                    {
                        animator.Play(BeaconIdle, 0, deactivating ? 0f : Mathf.Repeat(state.normalizedTime, 1f));
                        animator.Update(0f);
                    }
                }
                beaconRetiring = retiring;
            }

            private LineRenderer Line(string name, Color color, float width, int count, bool loop)
            {
                var line = new GameObject(name).AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false);
                line.useWorldSpace = false;
                line.loop = loop;
                line.positionCount = count;
                line.widthMultiplier = width;
                line.sharedMaterial = Graphic.defaultGraphicMaterial;
                line.startColor = line.endColor = color;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                return line;
            }

            private void LateUpdate()
            {
                float age = Mathf.Max(0f, Run.FixedTimeStamp.now.t - StartedAt);
                float progress = Lifetime > 0f ? Mathf.Clamp01(age / Lifetime) : 1f;
                if (pulseRings.Count > 0)
                {
                    DrawPulse(progress);
                    return;
                }
                if (Kind == VisualKind.TravelOrb && Target)
                {
                    transform.position = ArcPoint(Origin, Target.corePosition, progress);
                    float tail = Mathf.Max(0f, progress - .12f / Mathf.Max(.02f, Lifetime));
                    for (int i = 0; orbTrail && i < orbTrail.positionCount; i++)
                    {
                        float sample = Mathf.Lerp(tail, progress, i / (float)(orbTrail.positionCount - 1));
                        orbTrail.SetPosition(i, transform.InverseTransformPoint(ArcPoint(Origin, Target.corePosition, sample)));
                    }
                }
                if (Kind == VisualKind.AttachedOrb && Target) UpdateBodyBounds(true);
                if (orbRings.Count > 0)
                {
                    float warning = Kind == VisualKind.AttachedOrb
                        ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - (Lifetime - age) / .35f)) : 0f;
                    Color colour = Color.Lerp(Gold, OrbExpansion, warning);
                    float scale = 1f + warning * .35f;
                    if (Kind == VisualKind.AttachedOrb && Lifetime - age < .08f)
                        scale = Mathf.Lerp(.08f, 1.35f, Mathf.Clamp01((Lifetime - age) / .08f));
                    if (Kind == VisualKind.Burst)
                    {
                        scale = Mathf.Lerp(.1f, 1f, Mathf.Sqrt(progress));
                        colour = Color.Lerp(OrbExpansion, OrbExplosion, Mathf.SmoothStep(0f, 1f, progress / .4f));
                        colour.a = 1f - progress;
                    }
                    for (int i = 0; i < orbRings.Count; i++)
                    {
                        var ring = orbRings[i];
                        ring.startColor = ring.endColor = colour;
                        if (Kind == VisualKind.AttachedOrb || Kind == VisualKind.Burst)
                        {
                            Quaternion rotation = Quaternion.Euler(age * 145f, i * 60f + age * 95f, 0f);
                            for (int j = 0; j < ring.positionCount; j++)
                            {
                                float angle = j * Mathf.PI * 2f / ring.positionCount;
                                Vector3 point = rotation * new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                                ring.SetPosition(j, Vector3.Scale(point, orbExtents) * scale);
                            }
                        }
                        else ring.transform.localRotation = Quaternion.Euler(age * 145f, i * 60f + age * 95f, 0f);
                    }
                }
                if (Kind == VisualKind.Shield && Owner) transform.position = Owner.corePosition;
                if (Kind == VisualKind.Field)
                {
                    UpdateBeaconAnimation(age);
                    UpdateBeaconRadius();
                }
                if (beam && Owner && Target)
                {
                    beam.SetPosition(0, transform.InverseTransformPoint(Owner.corePosition));
                    beam.SetPosition(1, transform.InverseTransformPoint(Target.corePosition));
                }
            }

            private void DrawPulse(float progress)
            {
                bool barrier = Kind == VisualKind.BarrierPulse;
                Color colour = barrier ? Gold : Aqua;
                colour.a = .95f * Mathf.Sin(progress * Mathf.PI);
                foreach (LineRenderer line in pulseRings)
                    line.startColor = line.endColor = colour;
                if (Time.time < nextPulseDraw) return;
                nextPulseDraw = Time.time + .04f;
                for (int i = 0; i < pulseRings.Count; i++)
                {
                    LineRenderer line = pulseRings[i];
                    float wave = Mathf.Clamp01(progress * 1.2f - i * .2f);
                    float radius = Radius * (barrier ? 1f - wave : wave);
                    for (int j = 0; j < line.positionCount; j++)
                    {
                        float angle = j * Mathf.PI * 2f / line.positionCount;
                        Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                        Vector3 point = transform.position + offset;
                        if (Physics.Raycast(point + Vector3.up * 6f, Vector3.down, out RaycastHit ground,
                            18f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) && ground.normal.y > .25f)
                            point = ground.point;
                        line.SetPosition(j, transform.InverseTransformPoint(point + Vector3.up * (.25f + i * .08f)));
                    }
                }
            }

            private void UpdateBodyBounds(bool follow)
            {
                if (!Target) return;
                Bounds bounds = BodyBounds(Target, targetRenderers);
                orbExtents = Vector3.Max(bounds.extents + Vector3.one * .18f, Vector3.one * .45f);
                if (follow) transform.position = bounds.center;
            }
        }

        private sealed class SupportZone : MonoBehaviour
        {
            private static readonly Dictionary<CharacterBody, int> coverage = new Dictionary<CharacterBody, int>();
            private readonly HashSet<CharacterBody> covered = new HashSet<CharacterBody>();
            private readonly HashSet<CharacterBody> inRange = new HashSet<CharacterBody>();
            private readonly HashSet<CharacterBody> pushTargets = new HashSet<CharacterBody>();
            private readonly List<CharacterBody> clearedPushTargets = new List<CharacterBody>();
            private readonly Dictionary<ProjectileController, Vector3> projectilePositions = new Dictionary<ProjectileController, Vector3>();
            private readonly List<ProjectileController> vanished = new List<ProjectileController>();
            private CharacterBody owner;
            private Visual visual;
            private bool shield, expired, nativeDome;
            private float age, life, radius, interval, nextPulse;
            private float nextBarrierPulse;
            private Vector3 previousShieldCentre;

            internal static SupportZone Create(CharacterBody owner, Vector3 position, bool shield)
            {
                float radius = shield ? F(shieldRadius) : RallyRadius();
                float lifetime = Duration(shield ? shieldTime : fieldTime);
                bool useNativeDome = shield && TryLaunchDome(owner);
                Visual visual = useNativeDome ? null : SpawnVisual(shield ? VisualKind.Shield : VisualKind.Field,
                    owner, null, position, radius, lifetime);
                if (!visual && !useNativeDome) return null;
                GameObject area = visual ? visual.gameObject : new GameObject("3QS Shield Projection support");
                area.transform.position = position;
                var zone = area.AddComponent<SupportZone>();
                zone.owner = owner;
                zone.visual = visual;
                zone.shield = shield;
                zone.nativeDome = useNativeDome;
                zone.radius = radius;
                zone.previousShieldCentre = position;
                zone.life = lifetime;
                zone.interval = Duration(fieldInterval);
                zone.nextPulse = zone.interval;
                if (shield) zone.PushEnemies();
                else
                {
                    fields.Add(zone);
                    if (C.Get(initialPulse)) zone.Pulse();
                }
                return zone;
            }

            private bool Contains(Vector3 position) => (position - transform.position).sqrMagnitude <= radius * radius;
            private IEnumerable<CharacterBody> Allies() =>
                CharacterBody.readOnlyInstancesList.Where(body => Allied(owner, body) && Contains(body.corePosition));

            private void FixedUpdate()
            {
                if (expired) return;
                if (!NetworkServer.active || !Alive(owner)) { Destroy(gameObject); return; }
                float dt = Mathf.Min(Time.fixedDeltaTime, life - age);
                age += dt;
                if (shield)
                {
                    transform.position = owner.corePosition;
                    if (age < Duration(shieldPushTime)) PushTargets(Duration(shieldPushTime) - age);
                    UpdateShield(dt);
                    previousShieldCentre = transform.position;
                }
                else
                {
                    while (nextPulse < life - .001f && nextPulse <= age)
                    {
                        Pulse();
                        nextPulse += interval;
                    }
                }
                if (age < life) return;
                expired = true;
                if (!shield && C.Get(finalPulse)) Pulse();
                if (shield) Destroy(gameObject);
                else
                {
                    fields.Remove(this);
                    var control = owner.GetComponent<Control>();
                    if (control) control.SetFieldActive(false);
                    // Gameplay ends now, while the model finishes falling over.
                    Destroy(gameObject, BeaconRetireDuration());
                }
            }

            private void Pulse()
            {
                Emit(pulseSound, transform.position);
                ShowPulse(VisualKind.RechargePulse);
                CharacterBody[] allies = Allies().ToArray();
                foreach (CharacterBody ally in allies) Recharge(ally, owner);
                if (visual) visual.SendPulse(allies);
            }

            internal void RewardKill(DamageReport report)
            {
                if (expired || !Alive(owner) || report.attackerTeamIndex != Team(owner) ||
                    !TeamMask.GetEnemyTeams(Team(owner)).HasTeam(report.victimTeamIndex) ||
                    !Contains(report.victimBody.corePosition)) return;
                float amount = owner.damage * F(fieldBarrier) / 100f;
                if (amount <= 0f) return;
                foreach (CharacterBody ally in Allies()) ally.healthComponent.AddBarrier(amount);
                if (Time.fixedTime >= nextBarrierPulse)
                {
                    nextBarrierPulse = Time.fixedTime + .12f;
                    ShowPulse(VisualKind.BarrierPulse);
                }
            }

            private void ShowPulse(VisualKind kind)
            {
                Visual pulse = SpawnVisual(kind, owner, null, transform.position, radius, .7f);
                if (pulse) Destroy(pulse.gameObject, .8f);
            }

            private void PushEnemies()
            {
                if (F(shieldPush) <= 0f) return;
                TeamMask enemies = TeamMask.GetEnemyTeams(Team(owner));
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                {
                    if (!Alive(body) || body.isBoss || !enemies.HasTeam(Team(body))) continue;
                    float reach = radius + body.radius;
                    if ((body.corePosition - transform.position).sqrMagnitude <= reach * reach) pushTargets.Add(body);
                }
                PushTargets(Duration(shieldPushTime));
            }

            private void PushTargets(float remainingTime)
            {
                clearedPushTargets.Clear();
                foreach (CharacterBody body in pushTargets)
                {
                    Vector3 offset = body ? body.corePosition - transform.position : Vector3.zero;
                    float clearance = radius + (body ? body.radius : 0f) + .5f;
                    if (!Alive(body) || body.isBoss || offset.sqrMagnitude >= clearance * clearance)
                    {
                        clearedPushTargets.Add(body);
                        continue;
                    }
                    Vector3 outward = offset;
                    bool groundedMover = body.characterMotor && !body.characterMotor.isFlying;
                    if (groundedMover) outward.y = 0f;
                    if (outward.sqrMagnitude < .01f) outward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
                    if (outward.sqrMagnitude < .01f) outward = Vector3.forward;
                    Vector3 direction = outward.normalized;
                    if (groundedMover) direction = (direction + Vector3.up * .1f).normalized;
                    float along = Vector3.Dot(offset, direction);
                    float distance = -along + Mathf.Sqrt(Mathf.Max(0f, along * along + clearance * clearance - offset.sqrMagnitude));
                    float speed = Mathf.Max(F(shieldPush), 1.5f * distance / Mathf.Max(.15f, remainingTime));
                    var force = PhysForceInfo.Create();
                    force.force = direction * speed;
                    force.massIsOne = true;
                    force.ignoreGroundStick = true;
                    force.resetVelocity = true;
                    force.disableAirControlUntilCollision = true;
                    force.respectKnockupImmune = false;
                    var motor = body.GetComponent<IPhysMotor>();
                    if (motor != null) motor.ApplyForceImpulse(in force);
                    else
                    {
                        var rigidbody = body.GetComponent<Rigidbody>();
                        if (rigidbody && !rigidbody.isKinematic) rigidbody.velocity = force.force;
                    }
                }
                foreach (CharacterBody body in clearedPushTargets) pushTargets.Remove(body);
            }

            private void UpdateShield(float dt)
            {
                inRange.Clear();
                float barrier = owner.damage * F(shieldBarrier) / 100f * dt;
                foreach (CharacterBody ally in Allies())
                {
                    inRange.Add(ally);
                    if (covered.Add(ally))
                    {
                        coverage.TryGetValue(ally, out int count);
                        coverage[ally] = count + 1;
                        if (count == 0) ally.AddBuff(shieldBuff);
                    }
                    if (barrier > 0f) ally.healthComponent.AddBarrier(barrier);
                }
                foreach (CharacterBody ally in covered.ToArray())
                    if (!inRange.Contains(ally)) { covered.Remove(ally); RemoveCoverage(ally); }
                if (!nativeDome && C.Get(intercept) && radius > 0f) InterceptProjectiles();
            }

            private void InterceptProjectiles()
            {
                TeamMask enemies = TeamMask.GetEnemyTeams(Team(owner));
                vanished.Clear();
                foreach (var pair in projectilePositions)
                    if (!pair.Key) vanished.Add(pair.Key);
                foreach (ProjectileController projectile in vanished) projectilePositions.Remove(projectile);
                foreach (ProjectileController projectile in InstanceTracker.GetInstancesList<ProjectileController>())
                {
                    if (!projectile || !projectile.teamFilter || !enemies.HasTeam(projectile.teamFilter.teamIndex)) continue;
                    Vector3 now = projectile.transform.position;
                    if (!projectilePositions.TryGetValue(projectile, out Vector3 previous))
                    {
                        var motor = projectile.GetComponent<Rigidbody>();
                        previous = motor ? now - motor.velocity * Time.fixedDeltaTime : now;
                    }
                    projectilePositions[projectile] = now;
                    Vector3 previousRelative = previous - previousShieldCentre;
                    if (previousRelative.sqrMagnitude <= radius * radius) continue;
                    Vector3 path = now - transform.position - previousRelative;
                    float t = path.sqrMagnitude > .0001f
                        ? Mathf.Clamp01(-Vector3.Dot(previousRelative, path) / path.sqrMagnitude) : 0f;
                    if ((previousRelative + path * t).sqrMagnitude <= radius * radius) Destroy(projectile.gameObject);
                }
            }

            private static void RemoveCoverage(CharacterBody body)
            {
                if (!coverage.TryGetValue(body, out int count)) return;
                if (count > 1) coverage[body] = count - 1;
                else
                {
                    coverage.Remove(body);
                    if (NetworkServer.active && body) body.RemoveBuff(shieldBuff);
                }
            }

            private void OnDestroy()
            {
                fields.Remove(this);
                foreach (CharacterBody ally in covered) RemoveCoverage(ally);
                if (!NetworkServer.active) return;
                if (!shield && !expired && owner)
                {
                    var control = owner.GetComponent<Control>();
                    if (control) control.SetFieldActive(false);
                }
            }
        }
    }
}
