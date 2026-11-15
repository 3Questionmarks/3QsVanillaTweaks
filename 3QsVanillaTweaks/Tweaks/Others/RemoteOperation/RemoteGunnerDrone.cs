using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Events;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteGunnerDrone
    {
        private const string PrimaryMachine = "3QSRemoteGunnerPrimary", ChargeMachine = "3QSRemoteGunnerCharge",
            DashMachine = "3QSRemoteGunnerDash", BurstMachine = "3QSRemoteGunnerBurst";
        internal static readonly Type[] States = { typeof(Fusillade), typeof(ChargedShot), typeof(Dash), typeof(RapidBurst) };
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> primaryOn, secondaryOn, utilityOn, specialOn;
        internal static ConfigEntry<int> primaryShots, burstShots;
        internal static ConfigEntry<float> primaryDamage, primaryProc, primaryInterval, primaryCooldown;
        private static ConfigEntry<float> shotRange, shotRadius, shotForce;
        internal static ConfigEntry<float> chargeTime, chargeMinDamage, chargeMaxDamage, chargeMinProc, chargeMaxProc, chargeCooldown;
        internal static ConfigEntry<float> dashDistance, dashDuration, dashCooldown;
        internal static ConfigEntry<float> burstDamage, burstProc, burstInterval, burstCooldown;
        private static SkillDef primary, secondary, utility, special;
        private static GameObject chargedProjectile, fullyChargedProjectile;
        private static ConfigEntry<float> chargedSpeed, hitRadius, splashRadius, splashDamage, splashProc, fullChargeHold, recovery;

        internal static void Bind()
        {
            string s = RemoteDroneSkills.GunnerSection;
            enabled = C.BindStartupBool(s, "Changes Enabled", true, "Add Gunner Drone remote-operation skills.");
            primaryOn = C.BindStartupBool(s + " - Fusillade", "Changes Enabled", true, "Configure Fusillade for player-controlled Gunner Drones.");
            secondaryOn = C.BindStartupBool(s + " - Charged Shot", "Changes Enabled", true, "Add Charged Shot.");
            utilityOn = C.BindStartupBool(s + " - Dash", "Changes Enabled", true, "Add Dash.");
            specialOn = C.BindStartupBool(s + " - Rapid Burst", "Changes Enabled", true, "Add Rapid Burst. ");
            primaryShots = RemoteDroneSkills.Number(s + " - Fusillade", "Shots", 4, "Shots per barrage. Vanilla: 4.");
            primaryDamage = RemoteDroneSkills.Number(s + " - Fusillade", "Damage Per Shot (%)", 50f, "Damage dealt by each shot. Vanilla: 50%.");
            primaryProc = RemoteDroneSkills.Number(s + " - Fusillade", "Proc Coefficient", 1f, "Proc coefficient of each shot. Vanilla: 1.");
            primaryInterval = RemoteDroneSkills.Number(s + " - Fusillade", "Shot Interval (s)", .15f, "Time between shots before attack speed. Vanilla: 0.15s.");
            shotRange = RemoteDroneSkills.Number(s, "Shot Range (m)", 200f, "Maximum range for Gunner Drone shots. Vanilla: 200m.");
            shotRadius = RemoteDroneSkills.Number(s, "Shot Radius (m)", .5f, "Width of Gunner Drone shots. Vanilla: 0.5m.");
            chargeTime = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Charge Time (s)", 2f, "Time to fully charge the shot.");
            chargeMinDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Minimum Damage (%)", 200f, "Damage when released immediately.");
            chargeMaxDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Damage (%)", 600f, "Damage at full charge.");
            chargeMinProc = RemoteDroneSkills.Number(s + " - Charged Shot", "Minimum Proc Coefficient", 1f, "Proc coefficient below full charge.");
            chargeMaxProc = RemoteDroneSkills.Number(s + " - Charged Shot", "Maximum Proc Coefficient", 1.5f, "Proc coefficient at full charge.");
            chargeCooldown = RemoteDroneSkills.Number(s + " - Charged Shot", "Cooldown (s)", 5f, "Cooldown after the charged shot.");
            chargedSpeed = RemoteDroneSkills.Number(s + " - Charged Shot", "Projectile Speed (m/s)", 95f, "Travel speed of the charged projectile.");
            fullChargeHold = RemoteDroneSkills.Number(s + " - Charged Shot", "Full Charge Hold (s)", 2f, "Time a fully charged shot can be held before firing.");
            hitRadius = RemoteDroneSkills.Number(s + " - Charged Shot", "Hit Radius (m)", .75f, "Projectile collision radius.");
            splashRadius = RemoteDroneSkills.Number(s + " - Charged Shot", "Splash Radius (m)", 2.5f, "Radius of the blast at each pierced enemy.");
            splashDamage = RemoteDroneSkills.Number(s + " - Charged Shot", "Splash Damage (%)", 50f, "Blast damage relative to the shot's direct damage.");
            splashProc = RemoteDroneSkills.Number(s + " - Charged Shot", "Splash Proc Coefficient", .5f, "Proc coefficient of the impact blast.");
            dashDistance = RemoteDroneSkills.Number(s + " - Dash", "Distance (m)", 8f, "Dash distance; movement items do not increase it.");
            dashDuration = RemoteDroneSkills.Number(s + " - Dash", "Duration (s)", 0.2f, "Time to complete the dash.");
            dashCooldown = RemoteDroneSkills.Number(s + " - Dash", "Cooldown (s)", 3f, "Dash cooldown.");
            burstShots = RemoteDroneSkills.Number(s + " - Rapid Burst", "Shots", 6, "Shots fired by the burst.");
            burstDamage = RemoteDroneSkills.Number(s + " - Rapid Burst", "Damage Per Shot (%)", 100f, "Damage dealt by each shot.");
            burstProc = RemoteDroneSkills.Number(s + " - Rapid Burst", "Proc Coefficient", 1f, "Proc coefficient of each shot.");
            burstInterval = RemoteDroneSkills.Number(s + " - Rapid Burst", "Shot Interval (s)", .1f, "Time between shots before attack speed.");
            burstCooldown = RemoteDroneSkills.Number(s + " - Rapid Burst", "Cooldown (s)", 10f, "Burst cooldown.");
        }

        internal static void RegisterProjectiles(ContentPack content)
        {
            if (!enabled.Value) return;
            GameObject vanilla = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/FMJRamping");
            if (!vanilla || !vanilla.GetComponent<ProjectileOverlapAttack>())
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Gunner: the Commando Phase Round projectile is unavailable.");
                return;
            }
            chargedProjectile = R2API.PrefabAPI.InstantiateClone(vanilla, "3QSRemoteGunnerChargedShot", true);
            fullyChargedProjectile = R2API.PrefabAPI.InstantiateClone(vanilla, "3QSRemoteGunnerFullChargedShot", true);
            chargedProjectile.GetComponent<ProjectileController>().procCoefficient = Mathf.Max(0f, C.Get(chargeMinProc));
            fullyChargedProjectile.GetComponent<ProjectileController>().procCoefficient = Mathf.Max(0f, C.Get(chargeMaxProc));
            chargedProjectile.GetComponent<ProjectileDamage>().damageType = new DamageTypeCombo(DamageType.Stun1s, DamageTypeExtended.Generic, DamageSource.Secondary);
            fullyChargedProjectile.GetComponent<ProjectileDamage>().damageType = new DamageTypeCombo(DamageType.Stun1s, DamageTypeExtended.Generic, DamageSource.Secondary);
            chargedProjectile.AddComponent<PhaseSplash>();
            fullyChargedProjectile.AddComponent<PhaseSplash>();
            foreach (GameObject projectile in new[] { chargedProjectile, fullyChargedProjectile })
            {
                ProjectileOverlapAttack attack = projectile.GetComponent<ProjectileOverlapAttack>();
                attack.damageCoefficient = 1f;
                attack.onServerHit = new UnityEvent();
                attack.isSphereOverlap = true;
                HitBoxGroup hitboxes = projectile.GetComponent<HitBoxGroup>();
                if (hitboxes && hitboxes.hitBoxes != null)
                    foreach (HitBox box in hitboxes.hitBoxes)
                        if (box) box.transform.localScale = Vector3.one * (2f * Mathf.Max(.1f, C.Get(hitRadius)));
            }
            content.projectilePrefabs.Add(new[] { chargedProjectile, fullyChargedProjectile });
        }

        private sealed class PhaseSplash : MonoBehaviour
        {
            private ProjectileController controller;
            private ProjectileDamage damage;
            private readonly HashSet<HealthComponent> splashed = new HashSet<HealthComponent>();
            private void Awake()
            {
                controller = GetComponent<ProjectileController>();
                damage = GetComponent<ProjectileDamage>();
                GetComponent<ProjectileOverlapAttack>().onServerHitHurtBox.AddListener(Hit);
            }
            private void Hit(HurtBox direct)
            {
                if (!NetworkServer.active || !direct || !damage || !controller) return;
                CharacterBody attacker = controller.owner ? controller.owner.GetComponent<CharacterBody>() : null;
                if (!attacker || !attacker.teamComponent) return;
                foreach (Collider collider in Physics.OverlapSphere(direct.transform.position,
                    Mathf.Max(0f, C.Get(splashRadius)), LayerIndex.entityPrecise.mask))
                {
                    HurtBox nearby = collider.GetComponent<HurtBox>();
                    HealthComponent health = nearby ? nearby.healthComponent : null;
                    if (!health || !health.alive || health == direct.healthComponent || splashed.Contains(health)) continue;
                    CharacterBody victim = health.body;
                    if (!victim || !victim.teamComponent || victim.teamComponent.teamIndex == attacker.teamComponent.teamIndex) continue;
                    splashed.Add(health);
                    DamageInfo hit = new DamageInfo
                    {
                        attacker = controller.owner, inflictor = gameObject,
                        damage = damage.damage * Mathf.Max(0f, C.Get(splashDamage)) / 100f,
                        procCoefficient = Mathf.Max(0f, C.Get(splashProc)),
                        crit = damage.crit, position = nearby.transform.position
                    };
                    health.TakeDamage(hit);
                    GlobalEventManager.instance.OnHitEnemy(hit, health.gameObject);
                }
            }
        }

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("GunnerFusillade", "Fusillade", "Fire a barrage of 4 shots for 50% damage each.", typeof(Fusillade), PrimaryMachine, 0f, null);
            secondary = RemoteDroneSkills.MakeSkill("GunnerChargedShot", "Charged Shot", "Charge a piercing shot for 200%–600% damage and blast nearby enemies on impact.", typeof(ChargedShot), ChargeMachine, chargeCooldown.Value, null, false);
            utility = RemoteDroneSkills.MakeSkill("GunnerDash", "Dash", "Dash 8m in your movement direction.", typeof(Dash), DashMachine, dashCooldown.Value, null);
            special = RemoteDroneSkills.MakeSkill("GunnerRapidBurst", "Rapid Burst", "Rapidly fire 6 shots for 100% damage each.", typeof(RapidBurst), BurstMachine, burstCooldown.Value, null);
            Refresh();
        }

        internal static void Refresh()
        {
            if (!primary) return;
            if (primaryCooldown != null) primary.baseRechargeInterval = Mathf.Max(0f, C.Get(primaryCooldown));
            secondary.baseRechargeInterval = Mathf.Max(0f, C.Get(chargeCooldown));
            secondary.stockToConsume = 1; // The shared press-gated SkillDef also enforces one stock per distinct press.
            secondary.requiredStock = 1;
            utility.baseRechargeInterval = Mathf.Max(0f, C.Get(dashCooldown));
            special.baseRechargeInterval = Mathf.Max(0f, C.Get(burstCooldown));
            if (chargedProjectile) chargedProjectile.GetComponent<ProjectileController>().procCoefficient = Mathf.Max(0f, C.Get(chargeMinProc));
            if (fullyChargedProjectile) fullyChargedProjectile.GetComponent<ProjectileController>().procCoefficient = Mathf.Max(0f, C.Get(chargeMaxProc));
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () => $"Fire <style=cIsDamage>{C.Get(primaryShots)} shots</style> in a burst for <style=cIsDamage>{C.Get(primaryDamage):0.##}% damage</style> each.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Charge a <style=cIsDamage>piercing shot</style> for <style=cIsDamage>{C.Get(chargeMinDamage):0.##}%–{C.Get(chargeMaxDamage):0.##}% damage</style>. <style=cIsUtility>Stun</style> enemies hit for <style=cIsUtility>1 second</style>. Each hit blasts nearby enemies for <style=cIsDamage>{C.Get(splashDamage):0.##}% of the shot's damage</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () => $"<style=cIsUtility>Dash</style> in your movement direction.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Rapidly fire <style=cIsDamage>{C.Get(burstShots)} shots</style> for <style=cIsDamage>{C.Get(burstDamage):0.##}% damage</style> each. Attack speed increases the number of shots.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () => $"\nRecovery: {C.Get(recovery):0.##}s\nShot Interval: {C.Get(primaryInterval):0.##}s", () => C.Get(primaryProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () => $"\nCharge Time: {C.Get(chargeTime):0.##}s\nFull Charge Hold: {C.Get(fullChargeHold):0.##}s\nBlast Radius: {C.Get(splashRadius):0.##}m\nBlast Proc: {C.Get(splashProc):0.##}", () => C.Get(chargeMinProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                () => $"\nDash Distance: {C.Get(dashDistance):0.##}m\nDash Duration: {C.Get(dashDuration):0.##}s", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () => $"\nShot Interval: {C.Get(burstInterval):0.##}s", () => C.Get(burstProc));
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !primary) return;
            SkillLocator native = prefab.GetComponent<SkillLocator>();
            SkillDef vanilla = native && native.primary && native.primary.skillFamily && native.primary.skillFamily.variants.Length > 0
                ? native.primary.skillFamily.variants[0].skillDef : null;
            primaryCooldown = RemoteDroneSkills.Number(RemoteDroneSkills.GunnerSection + " - Fusillade", "Cooldown (s)",
                vanilla ? vanilla.baseRechargeInterval : 0f,
                "Time before Fusillade recharges. Vanilla: the original Gunner Drone primary cooldown.");
            recovery = RemoteDroneSkills.Number(RemoteDroneSkills.GunnerSection + " - Fusillade", "Recovery (s)",
                EntityStates.Drone.DroneWeapon.FireTurret.baseDuration > 0f ? EntityStates.Drone.DroneWeapon.FireTurret.baseDuration : 2f,
                "Time after the final shot before another primary barrage. Vanilla: original Gunner Drone recovery.");
            shotForce = RemoteDroneSkills.Number(RemoteDroneSkills.GunnerSection, "Shot Force",
                EntityStates.Drone.DroneWeapon.FireTurret.force, "Force applied by Gunner Drone shots. Vanilla: native Gunner Drone force.");
            Refresh();
            if (native && native.primary && native.primary.skillFamily && native.primary.skillFamily.variants.Length != 0)
            {
                Sprite icon = native.primary.skillFamily.variants[0].skillDef.icon;
                primary.icon = icon;
            }
            secondary.icon = RemoteDroneSkills.VanillaIcon("GolemBody", "Primary",
                RemoteDroneSkills.VanillaIcon("CommandoBody", "Secondary", primary.icon));
            utility.icon = RemoteDroneSkills.VanillaIcon("MercBody", "Utility", primary.icon);
            special.icon = RemoteDroneSkills.VanillaIcon("CommandoBody", "Special", primary.icon);
            if (!primaryOn.Value && !secondaryOn.Value && !utilityOn.Value && !specialOn.Value) return;
            if (primaryOn.Value) RemoteDroneSkills.AddMachine(prefab, PrimaryMachine);
            if (secondaryOn.Value) RemoteDroneSkills.AddMachine(prefab, ChargeMachine);
            if (utilityOn.Value) RemoteDroneSkills.AddMachine(prefab, DashMachine);
            if (specialOn.Value) RemoteDroneSkills.AddMachine(prefab, BurstMachine);
            if (primaryOn.Value) RemoteDroneSkills.SetSkill(prefab, "Primary", primary);
            if (secondaryOn.Value) RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary);
            if (utilityOn.Value) RemoteDroneSkills.SetSkill(prefab, "Utility", utility);
            if (specialOn.Value) RemoteDroneSkills.SetSkill(prefab, "Special", special);
            new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteGunnerAvailability")
                .Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                    postfix: new HarmonyMethod(typeof(RemoteGunnerDrone), nameof(AfterCanExecute)));
        }

        private static void AfterCanExecute(GenericSkill __instance, ref bool __result)
        {
            if (!__result || __instance.skillDef != primary) return;
            EntityStateMachine burst = EntityStateMachine.FindByCustomName(__instance.gameObject, BurstMachine);
            if (burst && burst.state is RapidBurst) __result = false;
        }

        internal static void Fire(BaseState state, float percent, float proc)
        {
            if (!state.isAuthority) return;
            Ray aim = state.GetAimRay();
            Util.PlaySound(string.IsNullOrEmpty(EntityStates.Drone.DroneWeapon.FireTurret.attackSoundString)
                ? "Play_drone_attack" : EntityStates.Drone.DroneWeapon.FireTurret.attackSoundString, state.gameObject);
            new BulletAttack
            {
                owner = state.gameObject, weapon = state.gameObject,
                origin = aim.origin, aimVector = aim.direction,
                damage = state.damageStat * percent / 100f,
                procCoefficient = Mathf.Max(0f, proc),
                isCrit = Util.CheckRoll(state.critStat, state.characterBody.master),
                damageType = DamageTypeCombo.GenericPrimary,
                radius = Mathf.Max(0f, C.Get(shotRadius)), force = C.Get(shotForce), smartCollision = true,
                minSpread = EntityStates.Drone.DroneWeapon.FireTurret.minSpread,
                maxSpread = EntityStates.Drone.DroneWeapon.FireTurret.maxSpread,
                tracerEffectPrefab = EntityStates.Drone.DroneWeapon.FireTurret.tracerEffectPrefab,
                hitEffectPrefab = EntityStates.Drone.DroneWeapon.FireTurret.hitEffectPrefab,
                maxDistance = Mathf.Max(0f, C.Get(shotRange))
            }.Fire();
        }

        private static void FireProjectile(BaseState state, GameObject prefab, float damage, float speed)
        {
            if (!state.isAuthority || !prefab) return;
            Ray aim = state.GetAimRay();
            ProjectileManager.instance.FireProjectile(new FireProjectileInfo
            {
                projectilePrefab = prefab, position = aim.origin,
                rotation = Util.QuaternionSafeLookRotation(aim.direction), owner = state.gameObject,
                damage = state.damageStat * damage / 100f, force = C.Get(shotForce),
                crit = Util.CheckRoll(state.critStat, state.characterBody.master),
                speedOverride = Mathf.Max(1f, speed)
            });
            RemoteDroneSkills.PlayPhaseMuzzle(state);
        }

        public sealed class Fusillade : BaseState
        {
            private int fired;
            private float nextShot;
            public override void OnEnter() { base.OnEnter(); Shoot(); }
            private void Shoot() { Fire(this, C.Get(primaryDamage), C.Get(primaryProc)); fired++; }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float interval = Mathf.Max(.01f, C.Get(primaryInterval) / Mathf.Max(.1f, attackSpeedStat));
                if (base.fixedAge >= nextShot + interval && fired < Mathf.Max(1, C.Get(primaryShots)))
                { nextShot = base.fixedAge; Shoot(); }
                if (fired >= Mathf.Max(1, C.Get(primaryShots)) &&
                    base.fixedAge >= nextShot + Mathf.Max(interval, C.Get(recovery) / Mathf.Max(.1f, attackSpeedStat)) &&
                    isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        public sealed class ChargedShot : BaseState
        {
            private float nextChargeEffect;
            private uint chargeSoundId;
            private bool queued;
            public override void OnEnter()
            {
                base.OnEnter(); RemoteDroneSkills.ChargeDisplay(this, 0f);
                if (isAuthority) chargeSoundId = Util.PlaySound("Play_mage_m2_charge", gameObject);
                RemoteDroneSkills.PlayPhaseChargePulse(this, ref nextChargeEffect);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float duration = Mathf.Max(.01f, C.Get(chargeTime));
                RemoteDroneSkills.ChargeDisplay(this, base.fixedAge / duration);
                RemoteDroneSkills.PlayPhaseChargePulse(this, ref nextChargeEffect);
                if (!isAuthority) return;
                queued |= !inputBank.skill2.down || base.fixedAge >= duration + Mathf.Max(0f, C.Get(fullChargeHold));
                if (!queued) return;
                EntityStateMachine burst = EntityStateMachine.FindByCustomName(gameObject, BurstMachine);
                if (burst && burst.state is RapidBurst) return;
                float charge = Mathf.Clamp01(base.fixedAge / duration);
                float damage = Mathf.Lerp(C.Get(chargeMinDamage), C.Get(chargeMaxDamage), charge);
                if (chargedProjectile)
                    FireProjectile(this, charge >= 1f ? fullyChargedProjectile : chargedProjectile, damage, C.Get(chargedSpeed));
                else Fire(this, damage, charge >= 1f ? C.Get(chargeMaxProc) : C.Get(chargeMinProc));
                outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (!outer.destroying && queued)
                    RemoteDroneSkills.PlayPhaseFire(this);
                if (chargeSoundId != 0) { AkSoundEngine.StopPlayingID(chargeSoundId); chargeSoundId = 0; }
                RemoteDroneSkills.HideCharge(this); base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        internal static void MoveDash(BaseState state, float distance, float duration, Vector3 direction, float previousAge)
        {
            if (!state.isAuthority && !NetworkServer.active) return;
            float amount = Mathf.Max(0f, Mathf.Min(state.fixedAge, duration) - previousAge) * distance / duration;
            Vector3 origin = state.transform.position;
            if (Physics.Raycast(origin, direction, out RaycastHit hit, amount + .3f, LayerIndex.world.mask))
                amount = Mathf.Max(0f, hit.distance - .3f);
            Rigidbody rigidbody = state.GetComponent<Rigidbody>();
            if (rigidbody) rigidbody.MovePosition(origin + direction * amount);
            else state.transform.position = origin + direction * amount;
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
                MoveDash(this, Mathf.Max(0f, C.Get(dashDistance)), duration, direction, previousAge);
                previousAge = base.fixedAge;
                if (base.fixedAge >= duration && isAuthority) outer.SetNextStateToMain();
            }
            public override void OnExit() { RemoteDroneSkills.PlayNativeDashEffect(this, direction, true); base.OnExit(); }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class RapidBurst : BaseState
        {
            private int fired;
            private float nextShot;
            private int totalShots;
            private float duration;
            private float interval;
            public override void OnEnter()
            {
                base.OnEnter();
                EntityStateMachine primaryMachine = EntityStateMachine.FindByCustomName(gameObject, PrimaryMachine);
                if (isAuthority && primaryMachine && primaryMachine.state is Fusillade)
                    primaryMachine.SetNextStateToMain();
                int baseShots = Mathf.Max(1, C.Get(burstShots));
                totalShots = Mathf.Max(1, Mathf.FloorToInt(baseShots * Mathf.Max(.1f, attackSpeedStat)));
                duration = Mathf.Max(.01f, C.Get(burstInterval) * baseShots);
                interval = duration / totalShots;
                Shoot();
            }
            private void Shoot()
            {
                if (EntityStates.Commando.CommandoWeapon.FireBarrage.effectPrefab)
                    EffectManager.SimpleMuzzleFlash(EntityStates.Commando.CommandoWeapon.FireBarrage.effectPrefab,
                        gameObject, "Muzzle", false);
                if (isAuthority)
                {
                    Ray aim = GetAimRay();
                    new BulletAttack
                    {
                        owner = gameObject, weapon = gameObject,
                        origin = aim.origin, aimVector = aim.direction,
                        damage = damageStat * C.Get(burstDamage) / 100f,
                        procCoefficient = Mathf.Max(0f, C.Get(burstProc)),
                        isCrit = Util.CheckRoll(critStat, characterBody.master),
                        force = EntityStates.Commando.CommandoWeapon.FireBarrage.force,
                        minSpread = EntityStates.Commando.CommandoWeapon.FireBarrage.minSpread,
                        maxSpread = EntityStates.Commando.CommandoWeapon.FireBarrage.maxSpread,
                        radius = EntityStates.Commando.CommandoWeapon.FireBarrage.bulletRadius,
                        smartCollision = true,
                        tracerEffectPrefab = EntityStates.Commando.CommandoWeapon.FireBarrage.tracerEffectPrefab,
                        hitEffectPrefab = EntityStates.Commando.CommandoWeapon.FireBarrage.hitEffectPrefab,
                        damageType = new DamageTypeCombo(DamageType.Generic,
                            DamageTypeExtended.Generic, DamageSource.Special)
                    }.Fire();
                }
                if (!string.IsNullOrEmpty(EntityStates.Commando.CommandoWeapon.FireBarrage.fireBarrageSoundString))
                    Util.PlaySound(EntityStates.Commando.CommandoWeapon.FireBarrage.fireBarrageSoundString, gameObject);
                fired++;
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                while (base.fixedAge >= nextShot + interval && fired < totalShots)
                { nextShot += interval; Shoot(); }
                if (base.fixedAge >= duration && fired >= totalShots && isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }
    }
}
