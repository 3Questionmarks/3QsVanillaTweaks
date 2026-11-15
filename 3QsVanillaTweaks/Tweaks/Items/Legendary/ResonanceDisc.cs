using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EntityStates.LaserTurbine;
using HarmonyLib;
using RoR2;
using RoR2.Items;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using LookingGlassStats = Tweaks.ModCompatibility.LookingGlassStatRegistry;
using Native = Tweaks.Items.Uncommon.UncommonNative;
using CommonItemSupport = Tweaks.Items.Common.CommonItemSupport;
using ItemConfig = Tweaks.Items.Uncommon.UncommonConfig;
using ItemSupport = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Legendary
{
    internal static class ResonanceDisc
    {
        const int RequiredSkills = 4;
        const int AllSkills = (1 << RequiredSkills) - 1;
        const string BeamDamage = "Beam Damage (%)";
        const string ExplosionDamage = "Explosion Damage (%)";
        const string BeamProc = "Proc Coefficient";
        const string KillsRequired = "Kills Required";
        const string KillTimeframe = "Kill Timeframe";

        static readonly Dictionary<LaserTurbineBodyBehavior, SkillCharge> charges = new Dictionary<LaserTurbineBodyBehavior, SkillCharge>();
        static readonly ConditionalWeakTable<EntityStateMachine, DiscTarget> targets = new ConditionalWeakTable<EntityStateMachine, DiscTarget>();
        static readonly List<Action> applyStats = new List<Action>();
        static ItemConfig config;
        static bool appliedRework;
        static float blastMultiplier = 1f;

        static bool ReworkActive => config.Active && config.B("Rework");

        internal static void Init()
        {
            config = RareItemSupport.Config("Resonance Disc", "LaserTurbine");
            config.B("Rework", true, "Charge the disc by using each of your four skill slots instead of killing enemies.");

            RoR2Application.onLoad += BindStats;
            if (!config.Enabled.Value)
                return;

            ItemSupport.Patch(typeof(LaserTurbineBodyBehavior), "OnEnable", typeof(ResonanceDisc), prefix: nameof(WaitForBodyNetworkId), postfix: nameof(Attach));
            ItemSupport.Patch(typeof(LaserTurbineBodyBehavior), "OnDestroy", typeof(ResonanceDisc), prefix: nameof(Detach));
            ItemSupport.Patch(typeof(LaserTurbineController), "OnOwnerKilledOtherServer", typeof(ResonanceDisc), prefix: nameof(AllowKillCharge));
            ItemSupport.Patch(typeof(LaserTurbineController), nameof(LaserTurbineController.ExpendCharge), typeof(ResonanceDisc), prefix: nameof(AllowNativeChargeConsumption));
            ItemSupport.Patch(typeof(AimState), nameof(AimState.OnEnter), typeof(ResonanceDisc), postfix: nameof(SelectTarget));
            CommonItemSupport.Patch(ItemSupport.Harmony, typeof(ChargeMainBeamState), nameof(ChargeMainBeamState.Update), typeof(ResonanceDisc),
                prefix: nameof(TrackTarget), transpiler: nameof(ReplaceAimRay));
            CommonItemSupport.Patch(ItemSupport.Harmony, typeof(FireMainBeamState), nameof(FireMainBeamState.OnEnter), typeof(ResonanceDisc),
                transpiler: nameof(ReplaceAimRay));
            ItemSupport.Patch(typeof(FireMainBeamState), "FireBeamServer", typeof(ResonanceDisc), prefix: nameof(AllowTargetedBeam), postfix: nameof(ConsumeAfterShot));

            CommonItemSupport.BuffTexts["bdLaserTurbineKillCharge"] = new CommonItemSupport.BuffText
            {
                Title = "Resonance Disc Charge",
                Text = icon => ReworkActive
                    ? "Each stack represents one different skill used. Use your primary, secondary, utility, and special to charge the disc. Stacks are consumed when it fires."
                    : "Rapid kills charge the Resonance Disc."
            };

            string fireState = typeof(FireMainBeamState).FullName;
            config.Field(fireState, "FireBeamServer", nameof(FireMainBeamState.mainBeamDamageCoefficient), () => DamageCoefficient(BeamDamage, FireMainBeamState.mainBeamDamageCoefficient));
            config.Field(fireState, "FireBeamServer", nameof(FireMainBeamState.secondBombDamageCoefficient), () => DamageCoefficient(ExplosionDamage, FireMainBeamState.secondBombDamageCoefficient));
            config.Field(fireState, "FireBeamServer", nameof(FireMainBeamState.mainBeamProcCoefficient), BeamProcCoefficient);

            config.ShowStat = key => !ReworkActive || (key != KillsRequired && key != KillTimeframe);
            config.StatValue = FormatStatValue;
            config.Text(Description, () => ReworkActive
                ? "Use each of your four skills to charge a powerful disc."
                : "Rapid kills charge a powerful disc.");
        }

        static void BindStats()
        {
            float beamDamage = FireMainBeamState.mainBeamDamageCoefficient * 100f;
            float explosionDamage = FireMainBeamState.secondBombDamageCoefficient * 100f;
            float beamProc = FireMainBeamState.mainBeamProcCoefficient;
            config.Pair(BeamDamage, beamDamage, beamDamage, "Damage dealt by each pass of the beam as a percentage of base damage.");
            config.Pair(ExplosionDamage, explosionDamage, explosionDamage, "Explosion projectile damage as a percentage of base damage, before the bomb's vanilla blast multiplier and falloff.");
            config.Pair(BeamProc, beamProc, 0f, "Proc coefficient of each beam pass.");

            int vanillaKills = RechargeState.killChargesRequired;
            config.N(KillsRequired, vanillaKills, "Number of rapid kills needed to charge the disc with Rework disabled.", 1);
            applyStats.Add(() => RechargeState.killChargesRequired = ReworkActive
                ? RequiredSkills
                : config.Active ? Mathf.RoundToInt(config.F(KillsRequired)) : vanillaKills);
            BindNativeNumber(KillTimeframe, RechargeState.killChargeDuration, value => RechargeState.killChargeDuration = Mathf.RoundToInt(value), "Seconds for which each kill contributes charge with Rework disabled.", 1);

            BindNativeNumber("Beam Radius", FireMainBeamState.mainBeamRadius, value => FireMainBeamState.mainBeamRadius = value, "Beam radius in metres.");
            BindNativeNumber("Beam Range", FireMainBeamState.mainBeamMaxDistance, value => FireMainBeamState.mainBeamMaxDistance = value, "Maximum beam distance in metres.");
            BindNativeNumber("Beam Force", FireMainBeamState.mainBeamForce, value => FireMainBeamState.mainBeamForce = value, "Force applied by the beam.");
            BindNativeNumber("Firing Duration", FireMainBeamState.baseDuration, value => FireMainBeamState.baseDuration = value, "Seconds between the outgoing and returning beams.");
            BindNativeNumber("Target Search Radius", AimState.targetAcquisitionRadius, value => AimState.targetAcquisitionRadius = value, "Distance in metres at which the charged disc searches for enemies.");
            BindNativeNumber("Target Search Interval", ReadyState.baseDuration, value => ReadyState.baseDuration = value, "Seconds between target searches while the disc is ready.");
            BindNativeNumber("Beam Windup", ChargeMainBeamState.baseDuration, value => ChargeMainBeamState.baseDuration = value, "Seconds between aiming and firing the outgoing beam.");

            var bomb = RareItemSupport.StaticPrefab(typeof(FireMainBeamState), nameof(FireMainBeamState.secondBombPrefab));
            if (bomb)
            {
                var explosion = bomb.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    blastMultiplier = explosion.blastDamageCoefficient;
                    BindNativeNumber("Explosion Radius", explosion.blastRadius, value => explosion.blastRadius = value, "Explosion radius in metres.");
                    BindNativeNumber("Explosion Proc Coefficient", explosion.blastProcCoefficient, value => explosion.blastProcCoefficient = value, "Proc coefficient of the explosion.");
                }

                var projectile = bomb.GetComponent<ProjectileSimple>();
                if (projectile)
                {
                    BindNativeNumber("Projectile Speed", projectile.desiredForwardSpeed, value => projectile.desiredForwardSpeed = value, "Speed of the explosion projectile in metres per second.");
                    BindNativeNumber("Projectile Lifetime", projectile.lifetime, value => projectile.lifetime = value, "Lifetime of the explosion projectile in seconds.");
                }
            }

            if (config.Enabled.Value)
            {
                ApplyStats();
                C.EffectiveSettingsChanged += ApplyStats;
            }
        }

        static void BindNativeNumber(string key, float native, Action<float> apply, string description, float min = 0f)
        {
            config.N(key, native, description, min);
            applyStats.Add(() => apply(config.Active ? config.F(key) : native));
        }

        static void ApplyStats()
        {
            foreach (var apply in applyStats)
                apply();

            bool rework = ReworkActive;
            if (rework == appliedRework)
                return;

            foreach (var charge in charges.Values)
                charge.Reset();
            appliedRework = rework;
        }

        static float DamageCoefficient(string key, float native)
        {
            // Vanilla already multiplies the owner's damage by the item count.
            int stacks = config.Count(Native.Body);
            return stacks > 0 && config.Numbers.ContainsKey(key) ? config.Total(key, stacks) / (100f * stacks) : native;
        }

        static float BeamProcCoefficient()
        {
            int stacks = config.Count(Native.Body);
            return stacks > 0 && config.Numbers.ContainsKey(BeamProc)
                ? config.Total(BeamProc, stacks)
                : FireMainBeamState.mainBeamProcCoefficient;
        }

        static bool WaitForBodyNetworkId(LaserTurbineBodyBehavior __instance)
        {
            if (!NetworkServer.active || !__instance.body)
                return true;

            var identity = __instance.body.GetComponent<NetworkIdentity>();
            if (!identity || identity.netId.Value != 0)
                return true;

            __instance.body.StartCoroutine(AttachAfterBodySpawn(__instance, identity));
            return false;
        }

        static IEnumerator AttachAfterBodySpawn(LaserTurbineBodyBehavior behavior, NetworkIdentity identity)
        {
            while (behavior && behavior.isActiveAndEnabled && identity && identity.netId.Value == 0)
                yield return null;

            if (behavior && behavior.isActiveAndEnabled && identity && identity.netId.Value != 0)
                behavior.OnEnable();
        }

        static void Attach(LaserTurbineBodyBehavior __instance, GameObject ___laserTurbineControllerInstance)
        {
            if (!NetworkServer.active || !___laserTurbineControllerInstance)
                return;

            var machine = ___laserTurbineControllerInstance.GetComponent<EntityStateMachine>();
            if (machine && __instance.body)
            {
                Detach(__instance);
                charges.Add(__instance, new SkillCharge(__instance.body, machine));
            }
        }

        static void Detach(LaserTurbineBodyBehavior __instance)
        {
            if (!charges.TryGetValue(__instance, out var charge))
                return;

            charges.Remove(__instance);
            charge.Detach();
        }

        static bool AllowKillCharge() => !ReworkActive;

        static bool AllowNativeChargeConsumption() => !ReworkActive;

        static void ConsumeAfterShot(FireMainBeamState __instance, bool __3, Ray ___initialAimRay)
        {
            if (NetworkServer.active && ReworkActive && __3 && ___initialAimRay.direction.sqrMagnitude > 0f)
            {
                var owner = RareItemSupport.Owner(__instance);
                if (owner)
                    owner.ClearTimedBuffs(RoR2Content.Buffs.LaserTurbineKillCharge);
            }
        }

        sealed class DiscTarget
        {
            internal HurtBox HurtBox;
        }

        static float TargetRange => Mathf.Min(AimState.targetAcquisitionRadius, FireMainBeamState.mainBeamMaxDistance);

        static bool TryTargetDirection(Vector3 origin, HurtBox target, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!target || !target.healthComponent || !target.healthComponent.alive)
                return false;

            Vector3 point = target.transform.position;
            direction = point - origin;
            float range = TargetRange;
            return direction.sqrMagnitude > 0.0001f && direction.sqrMagnitude <= range * range
                && !Physics.Linecast(origin, point, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        }

        static HurtBox FindNearestTarget(LaserTurbineBaseState state)
        {
            var owner = RareItemSupport.Owner(state);
            float range = TargetRange;
            if (!owner || !owner.teamComponent || range <= 0f)
                return null;

            Vector3 origin = state.transform.position;
            var search = new SphereSearch
            {
                origin = origin,
                radius = range,
                mask = LayerIndex.entityPrecise.mask,
                queryTriggerInteraction = QueryTriggerInteraction.UseGlobal
            };
            var enemies = TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex);
            var candidates = search.RefreshCandidates().FilterCandidatesByHurtBoxTeam(enemies).GetHurtBoxes();
            HurtBox nearest = null;
            float distanceSquared = float.PositiveInfinity;
            foreach (HurtBox candidate in candidates)
            {
                if (candidate && (candidate.transform.position - origin).sqrMagnitude < distanceSquared
                    && TryTargetDirection(origin, candidate, out var direction))
                {
                    nearest = candidate;
                    distanceSquared = direction.sqrMagnitude;
                }
            }

            return nearest;
        }

        static void SelectTarget(AimState __instance, ref bool ___foundTarget)
        {
            if (!config.Active || !__instance.isAuthority)
                return;

            var target = targets.GetOrCreateValue(__instance.outer);
            target.HurtBox = FindNearestTarget(__instance);
            ___foundTarget = target.HurtBox;
            if (___foundTarget)
                FaceTarget(__instance, target.HurtBox.transform.position - __instance.transform.position);
        }

        static bool TrackTarget(ChargeMainBeamState __instance)
        {
            if (!config.Active || !__instance.isAuthority)
                return true;

            var target = targets.GetOrCreateValue(__instance.outer);
            Vector3 origin = __instance.transform.position;
            if (!TryTargetDirection(origin, target.HurtBox, out var direction))
            {
                target.HurtBox = FindNearestTarget(__instance);
                if (!target.HurtBox)
                {
                    __instance.outer.SetNextState(new ReadyState());
                    return false;
                }

                direction = target.HurtBox.transform.position - origin;
            }

            FaceTarget(__instance, direction);
            return true;
        }

        static void FaceTarget(LaserTurbineBaseState state, Vector3 direction)
        {
            var rotation = state.GetComponent<SimpleRotateToDirection>();
            if (rotation)
                rotation.targetRotation = Quaternion.LookRotation(direction);
        }

        static IEnumerable<CodeInstruction> ReplaceAimRay(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            // Replace the call sites so the beam cannot use an inlined transform.forward ray.
            var native = AccessTools.Method(typeof(LaserTurbineBaseState), "GetAimRay");
            var replacement = AccessTools.Method(typeof(ResonanceDisc), nameof(GetTargetAimRay));
            bool replaced = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(native))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replaced = true;
                }

                yield return instruction;
            }

            if (!replaced)
                throw new InvalidOperationException("Resonance Disc aim ray call was not found in " + __originalMethod.DeclaringType.FullName + "." + __originalMethod.Name);
        }

        static Ray GetTargetAimRay(LaserTurbineBaseState state)
        {
            Vector3 origin = state.transform.position;
            if (!config.Active || !state.isAuthority)
                return new Ray(origin, state.transform.forward);

            var target = targets.GetOrCreateValue(state.outer);
            if (state is FireMainBeamState)
            {
                target.HurtBox = FindNearestTarget(state);
                if (!target.HurtBox)
                    state.outer.SetNextState(new ReadyState());
            }

            return new Ray(origin, target.HurtBox ? target.HurtBox.transform.position - origin : Vector3.zero);
        }

        static bool AllowTargetedBeam(Ray ___initialAimRay)
        {
            // The native firing state serializes this ray, so zero also cancels a lost target on the server.
            return !config.Active || ___initialAimRay.direction.sqrMagnitude > 0f;
        }

        static float FormatStatValue(string key, int stacks, float value)
        {
            if (key == ExplosionDamage)
                return value * blastMultiplier;
            return key == KillsRequired || key == KillTimeframe ? Mathf.RoundToInt(value) : value;
        }

        static string Description()
        {
            int kills = Mathf.RoundToInt(config.F(KillsRequired));
            float seconds = Mathf.RoundToInt(config.F(KillTimeframe));
            string trigger = ReworkActive
                ? "Use your " + RareItemSupport.Utility("primary, secondary, utility, and special") + " once each to charge a Resonance Disc."
                : "Kill " + RareItemSupport.Utility(kills + (kills == 1 ? " enemy" : " enemies"))
                    + " within " + RareItemSupport.Utility(seconds + (seconds == 1 ? " second" : " seconds"))
                    + " to charge a Resonance Disc.";

            string blast = RareItemSupport.Damage(LookingGlassStats.Number(config.F(ExplosionDamage) * blastMultiplier) + "%");
            float extraBlast = config.F(ExplosionDamage + " Per Stack") * blastMultiplier;
            if (extraBlast > 0f)
                blast += " <style=cStack>(+" + LookingGlassStats.Number(extraBlast) + "% per stack)</style>";

            return trigger + " The charged disc targets the nearest enemy in range with a clear line of sight. Its piercing beam deals "
                + config.V(BeamDamage, "%") + " damage on each pass. The outgoing beam explodes on impact for up to "
                + blast + " damage.";
        }

        sealed class SkillCharge
        {
            readonly CharacterBody body;
            readonly EntityStateMachine machine;
            int usedSlots;

            internal SkillCharge(CharacterBody body, EntityStateMachine machine)
            {
                this.body = body;
                this.machine = machine;
                body.onSkillActivatedServer += OnSkillActivated;
            }

            internal void Reset()
            {
                usedSlots = 0;
                if (NetworkServer.active && body)
                    body.ClearTimedBuffs(RoR2Content.Buffs.LaserTurbineKillCharge);
            }

            internal void Detach()
            {
                body.onSkillActivatedServer -= OnSkillActivated;
                if (usedSlots != 0)
                    Reset();
            }

            void OnSkillActivated(GenericSkill skill)
            {
                if (!ReworkActive || !machine || !(machine.state is RechargeState) || !body.skillLocator)
                    return;

                int slot = (int)body.skillLocator.FindSkillSlot(skill);
                if (slot < 0 || slot >= RequiredSkills)
                    return;

                var chargeBuff = RoR2Content.Buffs.LaserTurbineKillCharge;
                // The vanilla recharge state consumes all four timed stacks when it readies the disc.
                if (body.GetBuffCount(chargeBuff) == 0)
                    usedSlots = 0;

                int bit = 1 << slot;
                if (usedSlots == AllSkills || (usedSlots & bit) != 0)
                    return;

                usedSlots |= bit;
                body.AddTimedBuff(chargeBuff, float.PositiveInfinity);
            }
        }
    }
}
