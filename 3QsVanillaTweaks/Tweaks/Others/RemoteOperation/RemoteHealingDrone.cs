using System;
using System.Linq;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using RoR2.ContentManagement;
using RoR2.Projectile;
using RoR2.Orbs;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteHealingDrone
    {
        private const string ChargeMachine = "3QSRemoteHealingCharge", DashMachine = "3QSRemoteHealingDash",
            RescueMachine = "3QSRemoteHealingRescue", BeamMachine = "3QSRemoteHealingBeam";
        internal static readonly Type[] States = { typeof(HealBeam), typeof(ChargedShot), typeof(Dash), typeof(Rescue) };
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> primaryOn, secondaryOn, utilityOn, specialOn;
        private static ConfigEntry<float> healingPerSecond, healDuration, healCooldown, healRange, healInterval;
        private static ConfigEntry<float> chargeDuration, minDamage, maxDamage, shotProc, shotCooldown, allyHeal;
        private static ConfigEntry<float> bounceRange, healAngle, rescueAngle;
        private static ConfigEntry<float> dotDuration, dotInterval, dotDamage, lifesteal;
        private static ConfigEntry<float> buffDuration, buffAttack, buffSpeed, buffReduction;
        private static ConfigEntry<int> bounceTargets;
        private static ConfigEntry<float> dashDistance, dashDuration, dashCooldown;
        private static ConfigEntry<float> rescueRange, rescueBarrier, rescueCooldown, rescueOffset;
        private static ConfigEntry<float> fullChargeHold, shotSpeed, hitRadius, rescueCast;
        private static GameObject projectile, fullProjectile;
        private static GameObject healingImpactEffect, teleportEffect;
        private static string teleportStartSound, teleportEndSound;
        private static SkillDef primary, secondary, utility, special;
        private static BuffDef empowered, vulnerable;

        internal static void Bind()
        {
            string s = RemoteDroneSkills.HealingSection;
            enabled = C.BindStartupBool(s, "Changes Enabled", true, "Add Healing Drone remote-operation skills. Vanilla: False.");
            primaryOn = C.BindStartupBool(s + " - Heal Beam", "Changes Enabled", true, "Configure Heal Beam for player-controlled Healing Drones.");
            secondaryOn = C.BindStartupBool(s + " - Charged Shot", "Changes Enabled", true, "Add Charged Shot.");
            utilityOn = C.BindStartupBool(s + " - Dash", "Changes Enabled", true, "Add Dash.");
            specialOn = C.BindStartupBool(s + " - Rescue", "Changes Enabled", true, "Add Rescue.");
            healingPerSecond = RemoteDroneSkills.Number(s + " - Heal Beam", "Healing Per Second (%)", 50f, "Healing per second as a percentage of the drone's damage. Vanilla: 40%.");
            healDuration = RemoteDroneSkills.Number(s + " - Heal Beam", "Duration (s)", 5f, "How long the beam heals its target.");
            healCooldown = RemoteDroneSkills.Number(s + " - Heal Beam", "Cooldown (s)", 10f, "Heal Beam cooldown.");
            healRange = RemoteDroneSkills.Number(s + " - Heal Beam", "Target Range (m)", 60f, "Maximum range for the targeted ally.");
            healInterval = RemoteDroneSkills.Number(s + " - Heal Beam", "Heal Interval (s)", .2f, "Time between healing ticks.");
            healAngle = RemoteDroneSkills.Number(s + " - Heal Beam", "Target Angle (degrees)", 30f, "Maximum angle from the crosshair for targeting an ally.");
            chargeDuration = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Charge Time (s)", 2f, "Time to fully charge the shot.");
            fullChargeHold = RemoteDroneSkills.Number(s + " - Charged Shot", "Full Charge Hold (s)", 2f, "Time a fully charged shot can be held before firing.");
            shotSpeed = RemoteDroneSkills.Number(s + " - Charged Shot", "Projectile Speed (m/s)", 90f, "Travel speed of the charged shot.");
            hitRadius = RemoteDroneSkills.Number(s + " - Charged Shot", "Hit Radius (m)", .75f, "Collision radius of the shot.");
            minDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Minimum Damage (%)", 100f, "Damage when released immediately.");
            maxDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Damage (%)", 400f, "Damage at full charge.");
            shotProc = RemoteDroneSkills.Number(s + " - Charged Shot", "Proc Coefficient", 0.7f, "Proc coefficient on enemies.");
            shotCooldown = RemoteDroneSkills.Number(s + " - Charged Shot", "Cooldown (s)", 6f, "Cooldown after firing.");
            bounceTargets = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Targets", 5, "Maximum number of targets hit, including the first target.");
            bounceRange = RemoteDroneSkills.Number(s + " - Charged Shot", "Bounce Range (m)", 12f, "Maximum distance between targets.");
            allyHeal = RemoteDroneSkills.Number(s + " - Charged Shot", "Ally Healing (%)", 100f, "Healing dealt to allies as a percentage of the shot's damage.");
            dotDuration = RemoteDroneSkills.Number(s + " - Charged Shot", "Debuff Duration (s)", 8f, "Duration of the damage and healing debuff at full charge.");
            dotInterval = RemoteDroneSkills.Number(s + " - Charged Shot", "Debuff Tick Interval (s)", .2f, "Time between debuff damage ticks.");
            dotDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Debuff Damage Per Tick (%)", 10f, "Damage per tick as a percentage of the drone's damage.");
            lifesteal = RemoteDroneSkills.Number(s + " - Charged Shot", "Healing From Damage (%)", 10f, "Percentage of damage to a debuffed target healed to its attacker.");
            buffDuration = RemoteDroneSkills.Number(s + " - Charged Shot", "Ally Buff Duration (s)", 8f, "Duration of the ally buff at full charge.");
            buffAttack = RemoteDroneSkills.Number(s + " - Charged Shot", "Ally Attack Speed (%)", 30f, "Attack speed gained by a buffed ally.");
            buffSpeed = RemoteDroneSkills.Number(s + " - Charged Shot", "Ally Movement Speed (%)", 30f, "Movement speed gained by a buffed ally.");
            buffReduction = RemoteDroneSkills.Number(s + " - Charged Shot", "Ally Damage Reduction (%)", 30f, "Damage reduction gained by a buffed ally.");
            dashDistance = RemoteDroneSkills.Number(s + " - Dash", "Distance (m)", 8f, "Dash distance; movement items do not increase it.");
            dashDuration = RemoteDroneSkills.Number(s + " - Dash", "Duration (s)", .2f, "Time to complete the dash.");
            dashCooldown = RemoteDroneSkills.Number(s + " - Dash", "Cooldown (s)", 3f, "Dash cooldown.");
            rescueRange = RemoteDroneSkills.Number(s + " - Rescue", "Range (m)", 100f, "Maximum range to an ally; terrain does not block targeting.");
            rescueAngle = RemoteDroneSkills.Number(s + " - Rescue", "Target Angle (degrees)", 30f, "Maximum angle from the crosshair for targeting an ally.");
            rescueOffset = RemoteDroneSkills.Number(s + " - Rescue", "Arrival Offset (m)", 2f, "Distance to the target's side after teleporting.");
            rescueCast = RemoteDroneSkills.Number(s + " - Rescue", "Cast Time (s)", .4f, "Delay before teleporting to the target.");
            rescueBarrier = RemoteDroneSkills.Number(s + " - Rescue", "Barrier (%)", 50f, "Barrier granted to both allies as a percentage of each recipient's combined maximum health.");
            rescueCooldown = RemoteDroneSkills.Number(s + " - Rescue", "Cooldown (s)", 25f, "Rescue cooldown.");
        }

        internal static void RegisterProjectiles(ContentPack content)
        {
            if (!enabled.Value) return;
            GameObject donor = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/FMJRamping");
            if (!donor) { ThreeQsVanillaTweaks.Log.Warning("Remote Healing: charged projectile unavailable."); return; }
            projectile = R2API.PrefabAPI.InstantiateClone(donor, "3QSRemoteHealingShot", true);
            fullProjectile = R2API.PrefabAPI.InstantiateClone(donor, "3QSRemoteHealingFullShot", true);
            ProjectileOverlapAttack overlap = projectile.GetComponent<ProjectileOverlapAttack>();
            if (overlap) overlap.enabled = false;
            projectile.AddComponent<HealingImpact>();
            overlap = fullProjectile.GetComponent<ProjectileOverlapAttack>();
            if (overlap) overlap.enabled = false;
            fullProjectile.AddComponent<HealingImpact>();
            fullProjectile.AddComponent<FullChargeMarker>();
            content.projectilePrefabs.Add(new[] { projectile, fullProjectile });
        }

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("HealingBeam", "Heal Beam", "Heal an ally over 5 seconds.", typeof(HealBeam), BeamMachine, healCooldown.Value, null);
            secondary = RemoteDroneSkills.MakeSkill("HealingChargedShot", "Charged Shot", "Charge a shot that bounces between nearby targets, damaging enemies or healing allies.", typeof(ChargedShot), ChargeMachine, shotCooldown.Value, null, false);
            utility = RemoteDroneSkills.MakeSkill("HealingDash", "Dash", "Dash in your movement direction.", typeof(Dash), DashMachine, dashCooldown.Value, null);
            special = RemoteDroneSkills.MakeSkill("HealingRescue", "Rescue", "Teleport beside a targeted ally and grant you both barrier.", typeof(Rescue), RescueMachine, rescueCooldown.Value, null);
            empowered = ScriptableObject.CreateInstance<BuffDef>(); empowered.name = "3QSRemoteHealingEmpowered";
            empowered.buffColor = new Color(.35f, .95f, .55f); empowered.canStack = false;
            empowered.iconSprite = RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null;
            vulnerable = ScriptableObject.CreateInstance<BuffDef>(); vulnerable.name = "3QSRemoteHealingVulnerable";
            vulnerable.buffColor = new Color(.8f, .25f, .65f); vulnerable.isDebuff = true; vulnerable.canStack = false;
            vulnerable.iconSprite = RoR2Content.Buffs.Weak ? RoR2Content.Buffs.Weak.iconSprite : null;
            Refresh();
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = Mathf.Max(0f, C.Get(healCooldown));
            secondary.baseRechargeInterval = Mathf.Max(0f, C.Get(shotCooldown));
            secondary.stockToConsume = 1;
            secondary.requiredStock = 1;
            utility.baseRechargeInterval = Mathf.Max(0f, C.Get(dashCooldown));
            special.baseRechargeInterval = Mathf.Max(0f, C.Get(rescueCooldown));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"<style=cIsHealing>Heal</style> a targeted ally for <style=cIsHealing>{C.Get(healingPerSecond):0.##}% damage per second</style> for <style=cIsUtility>{C.Get(healDuration):0.##} seconds</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Charge a <style=cIsDamage>bouncing shot</style> for <style=cIsDamage>{C.Get(minDamage):0.##}–{C.Get(maxDamage):0.##}% damage</style>, striking up to <style=cIsUtility>{C.Get(bounceTargets)} targets</style>. Allies are instead <style=cIsHealing>healed for {C.Get(minDamage) * C.Get(allyHeal) / 100f:0.##}–{C.Get(maxDamage) * C.Get(allyHeal) / 100f:0.##}% damage</style>. At full charge, enemies are afflicted with <style=cIsDamage>Leech</style>, while allies gain <style=cIsUtility>{C.Get(buffAttack):0.##}% attack speed</style>, <style=cIsUtility>{C.Get(buffSpeed):0.##}% movement speed</style>, and <style=cIsHealing>{C.Get(buffReduction):0.##}% damage reduction</style> for <style=cIsUtility>{C.Get(buffDuration):0.##} seconds</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () => $"<style=cIsUtility>Dash</style> in your movement direction.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"<style=cIsUtility>Teleport</style> beside a targeted ally within <style=cIsUtility>{C.Get(rescueRange):0.##}m</style>. Grant both of you <style=cIsHealing>{C.Get(rescueBarrier):0.##}% maximum health as barrier</style>.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () => $"\nBeam Duration: {C.Get(healDuration):0.##}s\nTarget Range: {C.Get(healRange):0.##}m", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () => $"\nCharge Time: {C.Get(chargeDuration):0.##}s\nMaximum Targets: {C.Get(bounceTargets)}\nBounce Range: {C.Get(bounceRange):0.##}m\nFull Charge Hold: {C.Get(fullChargeHold):0.##}s", () => C.Get(shotProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                () => $"\nDash Distance: {C.Get(dashDistance):0.##}m\nDash Duration: {C.Get(dashDuration):0.##}s", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () => $"\nCast Time: {C.Get(rescueCast):0.##}s\nTarget Range: {C.Get(rescueRange):0.##}m\nArrival Offset: {C.Get(rescueOffset):0.##}m", () => 0f);
        }

        internal static BuffDef[] Buffs => enabled.Value ? new[] { empowered, vulnerable } : Array.Empty<BuffDef>();

        private static void LoadNativeEffects()
        {
            var healShot = EntityStateCatalog.InstantiateState(typeof(EntityStates.Drone.Command.CommandHealShot))
                as EntityStates.Drone.Command.CommandHealShot;
            if (healShot != null)
            {
                healingImpactEffect = RemoteDroneSkills.NativeGameObject(healShot, "orbEffectPrefab");
            }
            var teleport = EntityStateCatalog.InstantiateState(typeof(EntityStates.Treebot.ProliferationTeleport))
                as EntityStates.Treebot.ProliferationTeleport;
            if (teleport != null)
            {
                LegacyResourcesAPI.Load<EntityStateConfiguration>(
                    "EntityStateConfigurations/EntityStates.Treebot.ProliferationTeleport")
                    ?.BuildInstanceInitializer()?.Invoke(teleport);
                teleportEffect = RemoteDroneSkills.NativeGameObject(teleport, "teleportationVFX");
                teleportStartSound = teleport.teleportationStartSound;
                teleportEndSound = teleport.teleportationEndSound;
            }
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !primary) return;
            if (RoR2Content.Buffs.Warbanner) empowered.iconSprite = RoR2Content.Buffs.Warbanner.iconSprite;
            if (RoR2Content.Buffs.Weak) vulnerable.iconSprite = RoR2Content.Buffs.Weak.iconSprite;
            SkillLocator native = prefab.GetComponent<SkillLocator>();
            if (native && native.primary && native.primary.skillFamily && native.primary.skillFamily.variants.Length != 0)
            {
                Sprite icon = native.primary.skillFamily.variants[0].skillDef.icon;
                primary.icon = icon;
            }
            secondary.icon = RemoteDroneSkills.VanillaIcon("RailgunnerBody", "Special", RemoteDroneSkills.VanillaIcon("MageBody", "Secondary", primary.icon));
            utility.icon = RemoteDroneSkills.VanillaIcon("MercBody", "Utility", primary.icon);
            special.icon = RemoteDroneSkills.VanillaIcon("DroneTechBody", "Special",
                RemoteDroneSkills.VanillaIcon("BeetleGuardBody", "Special", primary.icon));
            LoadNativeEffects();
            if (primaryOn.Value) RemoteDroneSkills.AddMachine(prefab, BeamMachine);
            if (secondaryOn.Value) RemoteDroneSkills.AddMachine(prefab, ChargeMachine);
            if (utilityOn.Value) RemoteDroneSkills.AddMachine(prefab, DashMachine);
            if (specialOn.Value) RemoteDroneSkills.AddMachine(prefab, RescueMachine);
            if (primaryOn.Value) RemoteDroneSkills.SetSkill(prefab, "Primary", primary);
            if (secondaryOn.Value) RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary);
            if (utilityOn.Value) RemoteDroneSkills.SetSkill(prefab, "Utility", utility);
            if (specialOn.Value) RemoteDroneSkills.SetSkill(prefab, "Special", special);
            if (primaryOn.Value || specialOn.Value) prefab.AddComponent<TargetIndicator>();
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteHealingDrone");
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)), postfix: new HarmonyMethod(typeof(RemoteHealingDrone), nameof(AfterStats)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.TakeDamage)),
                prefix: new HarmonyMethod(typeof(RemoteHealingDrone), nameof(BeforeDamage)),
                postfix: new HarmonyMethod(typeof(RemoteHealingDrone), nameof(AfterDamage)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                postfix: new HarmonyMethod(typeof(RemoteHealingDrone), nameof(AfterCanExecute)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)),
                postfix: new HarmonyMethod(typeof(RemoteHealingDrone), nameof(AfterIsReady)));
        }

        private static void AfterStats(CharacterBody __instance)
        {
            if (!empowered || !__instance.HasBuff(empowered)) return;
            __instance.attackSpeed *= 1f + C.Get(buffAttack) / 100f;
            __instance.moveSpeed *= 1f + C.Get(buffSpeed) / 100f;
        }

        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out float __state)
        {
            __state = __instance.combinedHealth;
            if (empowered && __instance.body && __instance.body.HasBuff(empowered) && damageInfo != null)
                damageInfo.damage *= 1f - Mathf.Clamp01(C.Get(buffReduction) / 100f);
        }

        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo, float __state)
        {
            if (!NetworkServer.active || !vulnerable || !__instance.body || !__instance.body.HasBuff(vulnerable) ||
                damageInfo?.attacker == null || __instance.combinedHealth >= __state) return;
            HealthComponent healer = damageInfo.attacker.GetComponent<HealthComponent>();
            if (healer && healer != __instance && healer.alive)
                healer.Heal((__state - __instance.combinedHealth) * Mathf.Max(0f, C.Get(lifesteal)) / 100f, default(ProcChainMask));
        }

        private static HurtBox Target(BaseState state, bool ally, float range, float angle, bool lineOfSight)
        {
            return TargetAim(state.GetAimRay(), state.gameObject, state.teamComponent, ally, range, angle, lineOfSight);
        }

        private static HurtBox TargetAim(Ray aim, GameObject owner, TeamComponent team, bool ally, float range, float angle, bool lineOfSight)
        {
            var search = new BullseyeSearch
            {
                searchOrigin = aim.origin, searchDirection = aim.direction,
                maxDistanceFilter = range, maxAngleFilter = angle,
                teamMaskFilter = ally ? TeamMask.none : TeamMask.allButNeutral,
                sortMode = BullseyeSearch.SortMode.Angle,
                filterByLoS = lineOfSight
            };
            if (team)
            {
                if (ally) search.teamMaskFilter.AddTeam(team.teamIndex);
                else search.teamMaskFilter.RemoveTeam(team.teamIndex);
            }
            search.RefreshCandidates(); search.FilterOutGameObject(owner);
            return search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive);
        }

        private static void AfterCanExecute(GenericSkill __instance, ref bool __result)
        {
            ApplyTargetAvailability(__instance, ref __result);
        }

        private static void AfterIsReady(GenericSkill __instance, ref bool __result)
        {
            ApplyTargetAvailability(__instance, ref __result);
        }

        private static void ApplyTargetAvailability(GenericSkill skill, ref bool available)
        {
            if (!available || (skill.skillDef != primary && skill.skillDef != special)) return;
            InputBankTest input = skill.GetComponent<InputBankTest>();
            CharacterBody body = skill.GetComponent<CharacterBody>();
            if (!input || !body) { available = false; return; }
            bool beam = skill.skillDef == primary;
            available = TargetAim(input.GetAimRay(), body.gameObject, body.teamComponent, true,
                Mathf.Max(0f, C.Get(beam ? healRange : rescueRange)),
                Mathf.Clamp(C.Get(beam ? healAngle : rescueAngle), 0f, 180f), false) != null;
        }

        private sealed class TargetIndicator : MonoBehaviour
        {
            private CharacterBody body;
            private InputBankTest input;
            private Indicator indicator;
            private void Awake()
            {
                body = GetComponent<CharacterBody>(); input = GetComponent<InputBankTest>();
                indicator = new Indicator(gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator"));
            }
            private void FixedUpdate()
            {
                if (indicator == null) return;
                indicator.active = body && body.hasEffectiveAuthority;
                if (!indicator.active || !input) { indicator.targetTransform = null; return; }
                HurtBox rescue = TargetAim(input.GetAimRay(), gameObject, body.teamComponent, true,
                    Mathf.Max(0f, C.Get(rescueRange)), Mathf.Clamp(C.Get(rescueAngle), 0f, 180f), false);
                indicator.targetTransform = rescue ? rescue.transform : null;
            }
            private void OnDisable() { if (indicator != null) indicator.active = false; }
        }

        public sealed class HealBeam : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                HurtBox target = Target(this, true, Mathf.Max(0f, C.Get(healRange)),
                    Mathf.Clamp(C.Get(healAngle), 0f, 180f), false);
                GameObject donor = EntityStates.Drone.DroneWeapon.HealBeam.healBeamPrefab;
                if (!donor)
                {
                    var native = EntityStateCatalog.InstantiateState(typeof(EntityStates.Drone.DroneWeapon.StartHealBeam))
                        as EntityStates.Drone.DroneWeapon.StartHealBeam;
                    donor = native?.healBeamPrefab;
                }
                if (NetworkServer.active && target && donor)
                {
                    Transform muzzle = FindModelChild("Muzzle") ?? transform;
                    GameObject instance = UnityEngine.Object.Instantiate(donor, muzzle);
                    HealBeamController beam = instance.GetComponent<HealBeamController>();
                    if (beam)
                    {
                        beam.healRate = damageStat * Mathf.Max(0f, C.Get(healingPerSecond)) / 100f;
                        beam.tickInterval = Mathf.Max(.02f, C.Get(healInterval));
                        beam.breakOnTargetFullyHealed = false;
                        beam.target = target;
                        beam.ownership.ownerObject = gameObject;
                        var monitor = instance.AddComponent<BeamMonitor>();
                        monitor.owner = transform; monitor.target = target;
                        monitor.range = Mathf.Max(0f, C.Get(healRange));
                        instance.AddComponent<DestroyOnTimer>().duration = Mathf.Max(.01f, C.Get(healDuration));
                        NetworkServer.Spawn(instance);
                    }
                    else UnityEngine.Object.Destroy(instance);
                }
                if (!donor) ThreeQsVanillaTweaks.Log.Warning("Remote Healing: native heal beam prefab unavailable.");
                else if (isAuthority && target) Util.PlaySound("Play_item_proc_healingPotion", gameObject);
            }
            public override void FixedUpdate() { base.FixedUpdate(); if (isAuthority) outer.SetNextStateToMain(); }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Death;
        }

        private sealed class BeamMonitor : MonoBehaviour
        {
            internal Transform owner;
            internal HurtBox target;
            internal float range;
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (!owner || !target || !target.healthComponent || !target.healthComponent.alive ||
                    (owner.position - target.transform.position).sqrMagnitude > range * range)
                    GetComponent<HealBeamController>()?.BreakServer();
            }
        }

        public sealed class ChargedShot : BaseState
        {
            private float nextChargeEffect;
            private uint chargeSoundId;
            private bool fired;
            public override void OnEnter()
            {
                base.OnEnter(); RemoteDroneSkills.ChargeDisplay(this, 0f);
                if (isAuthority) chargeSoundId = Util.PlaySound("Play_mage_m2_charge", gameObject);
                RemoteDroneSkills.PlayPhaseChargePulse(this, ref nextChargeEffect);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float duration = Mathf.Max(.01f, C.Get(chargeDuration));
                RemoteDroneSkills.ChargeDisplay(this, fixedAge / duration);
                RemoteDroneSkills.PlayPhaseChargePulse(this, ref nextChargeEffect);
                if (!isAuthority || (inputBank.skill2.down && fixedAge < duration + Mathf.Max(0f, C.Get(fullChargeHold)))) return;
                float charge = Mathf.Clamp01(fixedAge / duration);
                if (projectile)
                {
                    Ray aim = GetAimRay();
                    ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                        projectilePrefab = charge >= 1f ? fullProjectile : projectile, position = aim.origin,
                        rotation = Util.QuaternionSafeLookRotation(aim.direction), owner = gameObject,
                        damage = damageStat * Mathf.Lerp(C.Get(minDamage), C.Get(maxDamage), charge) / 100f,
                        crit = Util.CheckRoll(critStat, characterBody.master),
                        speedOverride = Mathf.Max(1f, C.Get(shotSpeed)),
                        procChainMask = default(ProcChainMask)
                    });
                    RemoteDroneSkills.PlayPhaseMuzzle(this);
                }
                fired = true;
                outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (!outer.destroying && fired)
                    RemoteDroneSkills.PlayPhaseFire(this);
                if (chargeSoundId != 0) { AkSoundEngine.StopPlayingID(chargeSoundId); chargeSoundId = 0; }
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        private sealed class HealingImpact : MonoBehaviour
        {
            private ProjectileController controller;
            private ProjectileDamage shot;
            private readonly HashSet<HealthComponent> hit = new HashSet<HealthComponent>();
            private Vector3 last;
            private bool completed;
            private void Awake()
            {
                controller = GetComponent<ProjectileController>(); shot = GetComponent<ProjectileDamage>();
                last = transform.position;
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || completed || !controller || !controller.owner || !shot) return;
                CharacterBody source = controller.owner.GetComponent<CharacterBody>();
                if (!source || !source.teamComponent) return;
                float radius = Mathf.Max(.05f, C.Get(hitRadius));
                Vector3 position = transform.position;
                Vector3 delta = position - last;
                Collider[] contacts = Physics.OverlapSphere(position, radius + delta.magnitude, LayerIndex.entityPrecise.mask);
                foreach (Collider collider in contacts.OrderBy(c => (c.transform.position - last).sqrMagnitude))
                {
                    HurtBox box = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                    HealthComponent health = box ? box.healthComponent : null;
                    if (!health || !health.alive || health == source.healthComponent || hit.Contains(health) ||
                        Vector3.Distance(box.transform.position, position) > radius + delta.magnitude) continue;
                    hit.Add(health);
                    bool fullCharge = GetComponent<FullChargeMarker>();
                    ApplyShot(source, health, box, shot.damage, shot.crit, fullCharge, gameObject);
                    completed = true;
                    int remaining = Mathf.Max(1, C.Get(bounceTargets)) - 1;
                    if (remaining > 0)
                    {
                        HurtBox next = ClosestTarget(box.transform.position, source, hit, Mathf.Max(0f, C.Get(bounceRange)));
                        if (next)
                            OrbManager.instance.AddOrb(new HealingBounceOrb {
                                origin = box.transform.position, target = next, source = source,
                                damage = shot.damage, crit = shot.crit, fullCharge = fullCharge,
                                targetsRemaining = remaining, visited = hit
                            });
                    }
                    Destroy(gameObject);
                    return;
                }
                last = position;
            }
        }

        private sealed class HealingBounceOrb : Orb
        {
            internal CharacterBody source;
            internal float damage;
            internal bool crit, fullCharge;
            internal int targetsRemaining;
            internal HashSet<HealthComponent> visited;
            public override void Begin()
            {
                if (!target) return;
                duration = Mathf.Clamp(Vector3.Distance(origin, target.transform.position) / 50f, .08f, .3f);
                GameObject travelEffect = healingImpactEffect ? healingImpactEffect :
                    OrbStorageUtility.Get("Prefabs/Effects/OrbEffects/HealthOrbEffect");
                if (travelEffect)
                {
                    EffectData effect = new EffectData { origin = origin, genericFloat = duration, scale = 1f };
                    effect.SetHurtBoxReference(target);
                    EffectManager.SpawnEffect(travelEffect, effect, true);
                }
            }
            public override void OnArrival()
            {
                if (!source || !target || !target.healthComponent || !target.healthComponent.alive) return;
                HealthComponent health = target.healthComponent;
                visited ??= new HashSet<HealthComponent>();
                if (visited.Contains(health)) return;
                visited.Add(health);
                ApplyShot(source, health, target, damage, crit, fullCharge, source.gameObject);
                if (targetsRemaining <= 1) return;
                HurtBox next = ClosestTarget(target.transform.position, source, visited, Mathf.Max(0f, C.Get(bounceRange)));
                if (next)
                    OrbManager.instance.AddOrb(new HealingBounceOrb {
                        origin = target.transform.position, target = next, source = source,
                        damage = damage, crit = crit, fullCharge = fullCharge,
                        targetsRemaining = targetsRemaining - 1, visited = visited
                    });
            }
        }

        private static HurtBox ClosestTarget(Vector3 origin, CharacterBody source,
            HashSet<HealthComponent> visited, float range)
        {
            var search = new BullseyeSearch
            {
                searchOrigin = origin,
                searchDirection = Vector3.forward,
                maxDistanceFilter = range,
                maxAngleFilter = 180f,
                teamMaskFilter = TeamMask.allButNeutral,
                sortMode = BullseyeSearch.SortMode.Distance,
                filterByLoS = false,
                filterByDistinctEntity = true
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(source.gameObject);
            return search.GetResults().FirstOrDefault(box => box && box.healthComponent &&
                box.healthComponent.alive && (visited == null || !visited.Contains(box.healthComponent)));
        }

        private static void ApplyShot(CharacterBody source, HealthComponent health, HurtBox box,
            float damage, bool crit, bool fullCharge, GameObject inflictor)
        {
            CharacterBody recipient = health.body;
            bool ally = recipient && recipient.teamComponent && source.teamComponent &&
                recipient.teamComponent.teamIndex == source.teamComponent.teamIndex;
            if (ally)
            {
                health.Heal(damage * Mathf.Max(0f, C.Get(allyHeal)) / 100f, default(ProcChainMask));
                Util.PlaySound("Play_item_proc_healingPotion", health.gameObject);
                if (healingImpactEffect)
                {
                    EffectData effect = new EffectData { origin = box.transform.position, scale = 1f, genericFloat = .15f };
                    effect.SetHurtBoxReference(box);
                    EffectManager.SpawnEffect(healingImpactEffect, effect, true);
                }
                if (fullCharge) recipient.AddTimedBuff(empowered, Mathf.Max(0f, C.Get(buffDuration)));
                return;
            }
            DamageInfo hit = new DamageInfo {
                attacker = source.gameObject, inflictor = inflictor,
                damage = damage, crit = crit, procCoefficient = Mathf.Max(0f, C.Get(shotProc)),
                position = box.transform.position
            };
            health.TakeDamage(hit);
            GlobalEventManager.instance.OnHitEnemy(hit, health.gameObject);
            if (fullCharge && recipient)
            {
                recipient.AddTimedBuff(vulnerable, Mathf.Max(0f, C.Get(dotDuration)));
                (recipient.GetComponent<DamageMark>() ?? recipient.gameObject.AddComponent<DamageMark>()).Reset(source);
            }
        }

        private sealed class FullChargeMarker : MonoBehaviour { }

        private sealed class DamageMark : MonoBehaviour
        {
            private CharacterBody source;
            private CharacterBody target;
            private float nextTick;
            internal void Reset(CharacterBody owner)
            {
                source = owner; target = GetComponent<CharacterBody>();
                nextTick = Mathf.Max(.01f, C.Get(dotInterval));
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !target || !target.healthComponent || !target.healthComponent.alive || !target.HasBuff(vulnerable))
                { Destroy(this); return; }
                nextTick -= Time.fixedDeltaTime;
                if (nextTick > 0f) return;
                nextTick += Mathf.Max(.01f, C.Get(dotInterval));
                if (!source) return;
                var hit = new DamageInfo { attacker = source.gameObject, inflictor = gameObject,
                    damage = source.damage * Mathf.Max(0f, C.Get(dotDamage)) / 100f,
                    procCoefficient = 0f, position = target.corePosition };
                target.healthComponent.TakeDamage(hit);
            }
        }

        public sealed class Dash : BaseState
        {
            private Vector3 direction;
            private float previousAge;
            public override void OnEnter()
            {
                base.OnEnter(); direction = RemoteDroneSkills.MoveDirection(this);
                RemoteDroneSkills.PlayNativeDashEffect(this, direction, false);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float duration = Mathf.Max(.01f, C.Get(dashDuration));
                RemoteGunnerDrone.MoveDash(this, Mathf.Max(0f, C.Get(dashDistance)), duration, direction, previousAge);
                previousAge = base.fixedAge;
                if (base.fixedAge >= duration && isAuthority) outer.SetNextStateToMain();
            }
            public override void OnExit() { RemoteDroneSkills.PlayNativeDashEffect(this, direction, true); base.OnExit(); }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class Rescue : BaseState
        {
            private HurtBox selected;
            public override void OnEnter()
            {
                base.OnEnter();
                selected = Target(this, true, Mathf.Max(0f, C.Get(rescueRange)),
                    Mathf.Clamp(C.Get(rescueAngle), 0f, 180f), false);
                if (Util.PlaySound(string.IsNullOrEmpty(teleportStartSound) ? "Play_item_proc_teleportOnLowHealth" : teleportStartSound, gameObject) == 0)
                    Util.PlaySound("Play_item_proc_teleportOnLowHealth", gameObject);
                if (teleportEffect) EffectManager.SimpleEffect(teleportEffect, transform.position, transform.rotation, false);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (fixedAge < Mathf.Max(0f, C.Get(rescueCast))) return;
                if (NetworkServer.active)
                {
                    HurtBox target = selected;
                    if (target && target.healthComponent && target.healthComponent.body)
                    {
                        CharacterBody ally = target.healthComponent.body;
                        Vector3 arrival = ally.footPosition + ally.transform.right * C.Get(rescueOffset);
                        TeleportHelper.TeleportBody(characterBody, arrival, false);
                        if (teleportEffect) EffectManager.SimpleEffect(teleportEffect, arrival, transform.rotation, true);
                        float fraction = Mathf.Max(0f, C.Get(rescueBarrier)) / 100f;
                        healthComponent.AddBarrier((healthComponent.fullHealth + healthComponent.fullShield) * fraction);
                        ally.healthComponent.AddBarrier((ally.healthComponent.fullHealth + ally.healthComponent.fullShield) * fraction);
                    }
                }
                if (isAuthority && selected &&
                    Util.PlaySound(string.IsNullOrEmpty(teleportEndSound) ? "Play_item_proc_teleportOnLowHealth" : teleportEndSound, gameObject) == 0)
                    Util.PlaySound("Play_item_proc_teleportOnLowHealth", gameObject);
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
    }
}
