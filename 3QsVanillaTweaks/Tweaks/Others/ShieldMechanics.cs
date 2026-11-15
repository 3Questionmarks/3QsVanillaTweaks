using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class ShieldMechanics
    {
        private const string Section = "Other - Shield Mechanics";
        private static ConfigEntry<bool> enabled, gate, limitGate, transcendenceOSP, fall, curse, healing;
        private static ConfigEntry<float> gateLimit, delay, replenish;
        private static readonly FieldInfo dangerTimer = AccessTools.Field(typeof(CharacterBody), "outOfDangerStopwatch");
        internal static bool ShieldIsCurseReduced => enabled == null || !C.Get(enabled) || C.Get(curse);
        internal static float RechargeDelay => Active ? C.Get(delay) : 7f;
        internal static bool TranscendenceDisablesOSP => Active && C.Get(gate) && C.Get(transcendenceOSP);
        internal static bool DisablesOneShotProtection(CharacterBody body) => TranscendenceDisablesOSP && body && body.inventory && body.inventory.GetItemCountEffective(RoR2Content.Items.ShieldOnly) > 0;
        private static bool Active => enabled != null && C.Get(enabled);

        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true, "Enable general shield changes. Restart to apply.");
            gate = C.BindBool(Section, "Shield Gates HP", true, "A hit that breaks an existing shield cannot spill into health. Barrier is absorbed first. The next hit can damage health normally. Vanilla: False.");
            limitGate = C.BindBool(Section, "Limit Shield Gating Damage", true, "Hits exceeding the configured multiple of current shield bypass its gate. Vanilla: no shield gate.");
            gateLimit = C.Bind(Section, "Shield Gate Maximum Damage Multiplier", 3f, new ConfigDescription("Gate only when damage reaching shield is at most this multiple of its current amount. Exactly this amount still gates.", new AcceptableValueRange<float>(1f, 1000000f)));
            transcendenceOSP = C.BindBool(Section, "Transcendence Disables One-Shot Protection", true, "While holding Transcendence and shield gating is enabled, normal one-shot protection is disabled even with an empty shield or a hit that bypasses the gate.");
            delay = C.Bind(Section, "Shield Regeneration Delay", 7f, new ConfigDescription("Seconds without taking damage before natural shield recharge begins. Does not change other out-of-danger effects. Vanilla: 7 seconds.", new AcceptableValueRange<float>(0f, 600f)));
            replenish = C.Bind(Section, "Shield Replenish Time", 3f, new ConfigDescription("Seconds to naturally restore an empty shield to full once recharge starts. Forced recharge and instant shield restores retain their normal effects. Vanilla: 2 seconds.", new AcceptableValueRange<float>(0.01f, 600f)));
            fall = C.BindBool(Section, "Shield Is Used To Calculate Fall Damage", true, "Calculate fall damage from maximum health plus maximum shield. Vanilla: False.");
            curse = C.BindBool(Section, "Shield Is Reduced By HP Curse", true, "HP curse reduces maximum shield as well as maximum health. Vanilla: True.");
            healing = C.BindBool(Section, "Percentage Healing Includes Shield", true, "Use maximum health plus shield for percentage-based healing amounts. Healing still restores health, not shield; flat healing is unchanged. Supports vanilla healing and this mod's healing changes. Vanilla: False.");
            if (!enabled.Value) return;
            if (dangerTimer == null) throw new MissingFieldException(typeof(CharacterBody).FullName, "outOfDangerStopwatch");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.ShieldMechanics");
            Patch(harmony, typeof(HealthComponent), "TakeDamageProcess", nameof(DamageIL));
            Patch(harmony, typeof(HealthComponent), "ServerFixedUpdate", nameof(RechargeIL));
            Patch(harmony, typeof(CharacterBody), "RecalculateStats", nameof(StatsIL));
            Patch(harmony, typeof(GlobalEventManager), "OnCharacterHitGroundServer", nameof(FallIL));
            int count = 0;
            // Only healing-amount calculations: do not replace health caps, damage
            // formulas, targeting/full-health checks or barrier grants globally.
            string[][] targets = {
                new[]{"RoR2.HealthComponent","HealFraction"},
                new[]{"RoR2.HealthComponent+RepeatHealComponent","FixedUpdate","AddReserve"},
                new[]{"RoR2.HealingWard","HealOccupants"},
                new[]{"RoR2.HealthPickup","OnTriggerStay"},
                new[]{"RoR2.Projectile.ProjectileHealOwnerPercentage","OnDamageInflictedServer"},
                new[]{"RoR2.SeekerCycloneController","FireReprieveAllyHealing"},
                new[]{"PalmBlastProjectileController","ApplyHealingToAlly"},
                new[]{"EntityStates.Seeker.Meditate","OnEnter"},
                new[]{"EntityStates.VoidSurvivor.Vent.VentCorruption","OnEnter"},
                new[]{"EntityStates.Treebot.Weapon.FirePlantSonicBoom","AddDebuff"},
                new[]{"EntityStates.Treebot.ProliferationTeleport","HandleHealingServer"},
                new[]{"EntityStates.Treebot.TreebotFlower.TreebotFlower2Projectile","HealPulse"},
                new[]{"EntityStates.Toolbot.DroneProjectileHoverHeal","HealOccupants"},
                new[]{"EntityStates.Drone.Command.CommandHealShot+HealShotOrb","Begin"},
                new[]{"RoR2.DroneRepairMaster","TickHealthRepairServer"}
            };
            foreach (string[] target in targets)
            {
                Type type = typeof(CharacterBody).Assembly.GetType(target[0]);
                if (type == null) continue;
                foreach (string method in target.Skip(1))
                {
                    MethodInfo found = AccessTools.Method(type, method);
                    if (found == null) continue;
                    harmony.Patch(found, transpiler: new HarmonyMethod(typeof(ShieldMechanics), nameof(HealingIL)));
                    count++;
                }
            }
            // This mixed damage/healing handler has one fullHealth load for its
            // percentage heal; its maxHealth-based damage expressions are untouched.
            Patch(harmony, typeof(GlobalEventManager), "ProcessHitEnemy", nameof(FullHealthHealingIL));
            foreach (var target in new[] {
                new[]{"Tweaks.Survivors.Acrid","BeforeRegenerativeBuff"},
                new[]{"Tweaks.Survivors.REX","BeforeHealOrb","AfterRoot"},
                new[]{"Tweaks.Survivors.Captain","UpdateSupport","UpdateBeaconDaisy"}
            })
            {
                Type type = typeof(ShieldMechanics).Assembly.GetType(target[0]);
                if (type == null) continue;
                foreach (string method in target.Skip(1))
                {
                    MethodInfo found = AccessTools.Method(type, method);
                    if (found == null) continue;
                    harmony.Patch(found, transpiler: new HarmonyMethod(typeof(ShieldMechanics), nameof(FullHealthHealingIL)));
                    count++;
                }
            }
            C.EffectiveSettingsChanged += () =>
            {
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (body) body.RecalculateStats();
            };
            ThreeQsVanillaTweaks.Log.Info("Shield mechanics: shield gate, recharge, fall damage and curse hooks installed; " + count + " percentage-healing handlers patched.");
        }
        private static void Patch(Harmony harmony, Type type, string name, string transpiler)
        {
            MethodInfo method = AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            harmony.Patch(method, transpiler: new HarmonyMethod(typeof(ShieldMechanics), transpiler));
        }
        internal static float SpillDamage(float damage, float shield, bool gates, bool limited = false, float limit = 3f) => gates && shield > 0f && (!limited || damage <= shield * limit) ? 0f : damage - shield;
        internal static float HealingMaximum(float health, float shield, bool includes) => health + (includes ? shield : 0f);
        internal static float RechargeRate(float seconds) => 1f / Math.Max(0.01f, seconds);
        private static float GateSpill(float damage, float shield) => SpillDamage(damage, shield, Active && C.Get(gate), C.Get(limitGate), C.Get(gateLimit));
        // A shield gate can be bypassed by an oversized hit. Keep ordinary OSP available
        // for that hit; only the explicit Transcendence option disables it.
        private static bool GateBeforeOSP(bool eligible, HealthComponent health) => eligible && !DisablesOneShotProtection(health.body);
        private static bool ShieldReady(CharacterBody body) => !Active ? body.outOfDanger : (float)dangerTimer.GetValue(body) >= C.Get(delay);
        private static float Rate() => Active ? RechargeRate(C.Get(replenish)) : 0.5f;
        private static float CurseDivisor(float value) => ShieldIsCurseReduced ? value : 1f;
        private static float HealingHealth(HealthComponent health) => HealingMaximum(health.fullHealth, health.fullShield, Active && C.Get(healing));
        private static float HealingBody(CharacterBody body) => HealingMaximum(body.maxHealth, body.maxShield, Active && C.Get(healing));
        private static float FallHealth(CharacterBody body) => body.maxHealth + (Active && C.Get(fall) ? body.maxShield : 0f);

        private static IEnumerable<CodeInstruction> DamageIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            int gates = 0, osp = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction c = code[i];
                if (c.opcode == OpCodes.Sub && i >= 2 && code[i-1].operand is FieldInfo f && f.DeclaringType == typeof(HealthComponent) && f.Name == "shield")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(GateSpill)); gates++; }
                yield return c;
                if (c.operand is MethodInfo m && (m.Name == "get_hasOneShotProtection" ||
                    m.DeclaringType == typeof(OneShotProtection) && m.Name == "Eligible"))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShieldMechanics), nameof(GateBeforeOSP)));
                    osp++;
                }
            }
            if (gates != 1 || osp != 1) throw new InvalidOperationException("Shield gate anchors changed: spill=" + gates + ", OSP=" + osp);
        }
        private static IEnumerable<CodeInstruction> RechargeIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            int ready = 0, rates = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction c = code[i];
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(CharacterBody) && m.Name == "get_outOfDanger")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(ShieldReady)); ready++; }
                if (c.opcode == OpCodes.Ldc_R4 && (float)c.operand == 0.5f && i > 0 &&
                    code[i-1].operand is MethodInfo getter && getter.Name == "get_maxShield")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(Rate)); rates++; }
                yield return c;
            }
            if (ready != 1 || rates != 1) throw new InvalidOperationException("Shield recharge anchors changed: delay=" + ready + ", rate=" + rates);
        }
        private static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            bool regen = false, regenDone = false; int curses = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction c = code[i];
                if (c.operand is MethodInfo m)
                {
                    if (m.Name == "set_maxShield" && !regenDone) regen = true;
                    if (m.Name == "set_regen") { regen = false; regenDone = true; }
                    if (regen && m.Name == "get_maxHealth" && m.DeclaringType == typeof(CharacterBody) &&
                        code.Skip(Math.Max(0, i-12)).Take(Math.Min(12, i)).Any(previous => previous.operand is MemberInfo member && (member.Name.EndsWith("CrocoRegen", StringComparison.Ordinal) || member.Name.EndsWith("HealNovaRegen", StringComparison.Ordinal))))
                    { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(HealingBody)); }
                }
                yield return c;
                if (c.operand is MethodInfo curseGetter && curseGetter.Name == "get_cursePenalty" && i + 2 < code.Count &&
                    code[i+1].opcode == OpCodes.Div && code[i+2].operand is MethodInfo setter && setter.Name == "set_maxShield")
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShieldMechanics), nameof(CurseDivisor))); curses++; }
            }
            if (curses != 1) throw new InvalidOperationException("Shield curse anchor changed: " + curses);
        }
        private static IEnumerable<CodeInstruction> FallIL(IEnumerable<CodeInstruction> instructions)
        {
            int count = 0;
            foreach (CodeInstruction original in instructions)
            {
                var c = new CodeInstruction(original);
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(CharacterBody) && m.Name == "get_maxHealth")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(FallHealth)); count++; }
                yield return c;
            }
            if (count != 1) throw new InvalidOperationException("Fall damage health anchor changed: " + count);
        }
        private static IEnumerable<CodeInstruction> HealingIL(IEnumerable<CodeInstruction> instructions) => RewriteHealing(instructions, true);
        private static IEnumerable<CodeInstruction> FullHealthHealingIL(IEnumerable<CodeInstruction> instructions) => RewriteHealing(instructions, false);
        private static IEnumerable<CodeInstruction> RewriteHealing(IEnumerable<CodeInstruction> instructions, bool bodyHealth)
        {
            foreach (CodeInstruction original in instructions)
            {
                var c = new CodeInstruction(original);
                if (c.operand is MethodInfo m && m.DeclaringType == typeof(HealthComponent) && m.Name == "get_fullHealth")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(HealingHealth)); }
                else if (bodyHealth && c.operand is MethodInfo b && b.DeclaringType == typeof(CharacterBody) && b.Name == "get_maxHealth")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(ShieldMechanics), nameof(HealingBody)); }
                yield return c;
            }
        }
    }
}
