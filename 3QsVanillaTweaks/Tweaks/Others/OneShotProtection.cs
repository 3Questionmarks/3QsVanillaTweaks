using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class OneShotProtection
    {
        private const string Section = "Other - One-Shot Protection";
        private static ConfigEntry<bool> enabled, curseCounts, includeShield, includeBarrier, allHits;
        private static ConfigEntry<float> threshold, immunity, cooldown;
        private static BuffDef cooldownBuff;
        private static bool installed;
        private static readonly FieldInfo timerField = AccessTools.Field(typeof(HealthComponent), "ospTimer");
        private sealed class ProtectionState { internal float ImmuneUntil, CooldownUntil; }
        private static readonly ConditionalWeakTable<HealthComponent, ProtectionState> states = new ConditionalWeakTable<HealthComponent, ProtectionState>();

        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true, "Enable configurable one-shot protection. Restart to apply.");
            threshold = C.Bind(Section, "Threshold (%)", 90f, new ConfigDescription("Current counted health must be above this percentage of counted maximum health. Vanilla: 90%.", new AcceptableValueRange<float>(0f, 100f)));
            curseCounts = C.BindBool(Section, "HP Curse Counts", true, "Use maximum health before HP curse reductions. Enough curse therefore disables protection. Vanilla: True.");
            includeShield = C.BindBool(Section, "Include Shield In One-Shot Protection", true, "Include current and maximum shield in the threshold calculation. When false, only health is required even if shield is depleted. Vanilla: True.");
            includeBarrier = C.BindBool(Section, "Include Barrier In One-Shot Protection", false, "Include current barrier and its maximum capacity in the threshold calculation. When false, barrier cannot activate protection or raise its threshold.");
            immunity = C.Bind(Section, "Immunity Time", 0.5f, new ConfigDescription("Seconds of protection after one-shot protection actually triggers. Vanilla: 0.1 seconds.", new AcceptableValueRange<float>(0f, 60f)));
            allHits = C.BindBool(Section, "Immunity From All Hits", true, "Reject all damage hits during the triggered immunity window, including delayed damage that bypasses the vanilla OSP timer. Being above the threshold alone does not grant immunity.");
            cooldown = C.Bind(Section, "One-Shot Protection Cooldown", 5f, new ConfigDescription("Seconds after triggering before protection can activate again. Starts at the trigger, including the immunity window. Healing does not reset it. Vanilla: 0.", new AcceptableValueRange<float>(0f, 600f)));
            if (!enabled.Value) return;
            if (timerField == null) throw new MissingFieldException(typeof(HealthComponent).FullName, "ospTimer");
            cooldownBuff = ScriptableObject.CreateInstance<BuffDef>();
            cooldownBuff.name = "3QS_ONE_SHOT_PROTECTION_COOLDOWN";
            cooldownBuff.canStack = false; cooldownBuff.isDebuff = false; cooldownBuff.isCooldown = true;
            cooldownBuff.buffColor = new Color(0.6f, 0.6f, 0.6f);
            R2API.ContentAddition.AddBuffDef(cooldownBuff);
            RoR2Application.onLoad += () => { if (RoR2Content.Buffs.SmallArmorBoost) cooldownBuff.iconSprite = RoR2Content.Buffs.SmallArmorBoost.iconSprite; };
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.OneShotProtection");
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess"),
                prefix: new HarmonyMethod(typeof(OneShotProtection), nameof(BeforeDamage)),
                transpiler: new HarmonyMethod(typeof(OneShotProtection), nameof(DamageIL)));
            if (!installed) throw new InvalidOperationException("One-shot protection damage hook was not installed.");
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TriggerOneShotProtection"), postfix: new HarmonyMethod(typeof(OneShotProtection), nameof(AfterTrigger)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "GetHealthBarValues"), postfix: new HarmonyMethod(typeof(OneShotProtection), nameof(AfterHealthBar)));
            ThreeQsVanillaTweaks.Log.Info("One-shot protection: threshold, damage cap, immunity and cooldown hooks installed.");
        }

        internal static float CountedMaximum(float health, float shield, float barrier, float curse, bool countCurse, bool countShield, bool countBarrier)
        {
            float total = Math.Max(0f, health) + (countShield ? Math.Max(0f, shield) : 0f) + (countBarrier ? Math.Max(0f, barrier) : 0f);
            return total * (countCurse ? Math.Max(1f, curse) : 1f);
        }
        internal static bool AboveThreshold(float current, float maximum, float percent) => maximum > 0f && current > maximum * Math.Min(100f, Math.Max(0f, percent)) / 100f;
        internal static float CountedCurrent(float health, float shield, float barrier, bool countShield, bool countBarrier) => health + (countShield ? shield : 0f) + (countBarrier ? barrier : 0f);
        internal static bool WindowActive(float now, float until) => now < until;
        internal static float DamageAllowance(float maximum, float percent, float total, float excluded) => Math.Max(0f, Math.Min(total - 1f, maximum * percent / 100f + excluded));
        private static float Current(HealthComponent h) => CountedCurrent(h.health, h.shield, h.barrier, C.Get(includeShield), C.Get(includeBarrier));
        private static float Maximum(HealthComponent h)
        {
            float curseFactor = C.Get(curseCounts) ? Math.Max(1f, h.body.cursePenalty) : 1f;
            float maximum = h.fullHealth * curseFactor;
            if (C.Get(includeShield)) maximum += h.fullShield * (ShieldMechanics.ShieldIsCurseReduced ? curseFactor : 1f);
            // Barrier already calculated from pre-curse pools must not be uncursed twice.
            if (C.Get(includeBarrier)) maximum += h.fullBarrier * (BarrierMechanics.UsesPreCurseMaximum ? 1f : curseFactor);
            return maximum;
        }
        private static bool Eligible(CharacterBody body)
        {
            if (!body || !body.hasOneShotProtection) return false;
            if (!installed || !C.Get(enabled)) return body.hasOneShotProtection;
            HealthComponent health = body.healthComponent;
            if (!health || !health.alive || body.HasBuff(cooldownBuff)) return false;
            if (states.TryGetValue(health, out ProtectionState state) && WindowActive(Time.fixedTime, state.CooldownUntil)) return false;
            return AboveThreshold(Current(health), Maximum(health), C.Get(threshold));
        }
        private static float DamageBudget(HealthComponent health)
        {
            if (!C.Get(enabled)) return (health.fullCombinedHealth + health.barrier) * (1f - health.body.oneShotProtectionFraction);
            // Excluded pools still absorb damage, but cannot make OSP eligible.
            float excluded = (C.Get(includeShield) ? 0f : health.shield) + (C.Get(includeBarrier) ? 0f : health.barrier);
            float total = health.health + health.shield + health.barrier;
            return DamageAllowance(Maximum(health), C.Get(threshold), total, excluded);
        }
        private static bool BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (!NetworkServer.active || !installed || !C.Get(enabled) || !C.Get(allHits) || !states.TryGetValue(__instance, out ProtectionState state) || !WindowActive(Time.fixedTime, state.ImmuneUntil)) return true;
            damageInfo.rejected = true;
            return false;
        }
        private static void AfterTrigger(HealthComponent __instance)
        {
            if (!NetworkServer.active || !installed || !C.Get(enabled)) return;
            float now = Time.fixedTime;
            ProtectionState state = states.GetOrCreateValue(__instance);
            state.ImmuneUntil = now + C.Get(immunity); state.CooldownUntil = now + C.Get(cooldown);
            timerField.SetValue(__instance, C.Get(immunity));
            if (__instance.body && C.Get(cooldown) > 0f) __instance.body.AddTimedBuff(cooldownBuff, C.Get(cooldown));
        }
        private static void AfterHealthBar(HealthComponent __instance, ref HealthComponent.HealthBarValues __result)
        {
            if (!installed || !C.Get(enabled) || !__instance.body) return;
            if (!Eligible(__instance.body)) { __result.ospFraction = 0f; return; }
            float excess = Current(__instance) - Maximum(__instance) * C.Get(threshold) / 100f;
            __result.ospFraction = Mathf.Clamp01(excess / Math.Max(1f, __instance.fullCombinedHealth * __instance.body.cursePenalty));
        }

        private static IEnumerable<CodeInstruction> DamageIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            int eligible = code.FindIndex(i => i.operand is MethodInfo m && m.Name == "get_hasOneShotProtection");
            int trigger = code.FindIndex(i => i.operand is MethodInfo m && m.Name == "TriggerOneShotProtection");
            if (eligible < 0 || trigger <= eligible) throw new InvalidOperationException("Native OSP block not found; refusing a partial damage patch.");
            int start = code.FindIndex(eligible + 1, trigger - eligible - 1, i => i.operand is MethodInfo m && m.Name == "get_fullCombinedHealth") - 1;
            int end = start < 0 ? -1 : code.FindIndex(start + 1, i => i.opcode == OpCodes.Stloc || i.opcode == OpCodes.Stloc_S || i.opcode == OpCodes.Stloc_0 || i.opcode == OpCodes.Stloc_1 || i.opcode == OpCodes.Stloc_2 || i.opcode == OpCodes.Stloc_3);
            if (start <= eligible || end < start || end >= trigger || code[start].opcode != OpCodes.Ldarg_0) throw new InvalidOperationException("Native OSP budget expression changed; refusing a partial damage patch.");
            if (code.Skip(start + 1).Take(end - start - 1).Any(i => i.labels.Count > 0 || i.blocks.Count > 0)) throw new InvalidOperationException("Unexpected branch inside OSP budget expression.");
            code[eligible].opcode = OpCodes.Call; code[eligible].operand = AccessTools.Method(typeof(OneShotProtection), nameof(Eligible));
            var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(code[start].labels); load.blocks.AddRange(code[start].blocks);
            code.RemoveRange(start, end - start);
            code.InsertRange(start, new[] { load, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OneShotProtection), nameof(DamageBudget))) });
            installed = true;
            return code;
        }
    }
}
