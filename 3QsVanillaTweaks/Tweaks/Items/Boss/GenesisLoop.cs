using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EntityStates;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Boss
{
    internal static class GenesisLoop
    {
        static R c;
        static Type recharge, charge, detonate, ready;
        static string vanillaDescription;
        static readonly BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        internal static void Init()
        {
            c = BossItemSupport.Config("Genesis Loop", "NovaOnLowHealth", false);
            recharge = typeof(CharacterBody).Assembly.GetType("EntityStates.VagrantNovaItem.RechargeState", true);
            charge = typeof(CharacterBody).Assembly.GetType("EntityStates.VagrantNovaItem.ChargeState", true);
            detonate = typeof(CharacterBody).Assembly.GetType("EntityStates.VagrantNovaItem.DetonateState", true);
            ready = typeof(CharacterBody).Assembly.GetType("EntityStates.VagrantNovaItem.ReadyState", true);
            RoR2Application.onLoad += Bind;
            c.ShowStat = key => key != "Recharge Scaling Per Stack (%)" && key != "Recovery Time" && key != "Knockback Force";
            c.StatValue = (key, n, value) => key == "Recharge Time" ? RechargeTime(n) : value;
            c.Text(Description, () => "Falling to low health causes you to explode.");
        }

        static float Native(Type type, string field) => Convert.ToSingle(type.GetField(field, Fields).GetValue(null));
        static void Bind()
        {
            if (!c.Item) return;
            vanillaDescription = Language.GetString(c.Item.descriptionToken);
            c.Pair("Damage (%)", Native(detonate, "blastDamageCoefficient") * 100, 0, "Nova damage as a percentage of base damage. Vanilla value is read from the loaded item state.");
            c.Pair("Proc Coefficient", Native(detonate, "blastProcCoefficient"), 0, "Outgoing nova proc coefficient. Vanilla value is read from the loaded item state.");
            c.Pair("Explosion Radius", Native(detonate, "blastRadius"), 0, "Explosion radius in metres. Vanilla value is read from the loaded item state.");
            c.Pair("Knockback Force", Native(detonate, "blastForce"), 0, "Outward explosion force. Vanilla value is read from the loaded item state.");
            c.Pair("Charge Time", Native(charge, "baseDuration"), 0, "Seconds before the nova, before attack-speed scaling. Vanilla value is read from the loaded item state.");
            c.N("Recharge Time", Native(recharge, "baseDuration") / 2, "First-stack recharge time in seconds. Vanilla uses the state's base duration divided by item count plus one.", .01f);
            c.N("Recharge Scaling Per Stack (%)", 50, "Additional recharge-rate scaling per extra item. Recharge time is divided by 1 plus this fraction per extra stack. Vanilla: 50%.");
            c.Pair("Health Threshold (%)", 25, 0, "Trigger at or below this percentage of combined health. Vanilla: 25%.");
            c.Pair("Recovery Time", Native(detonate, "baseDuration"), 0, "Seconds after detonation before entering recharge. Vanilla value is read from the loaded item state.");
            if (!c.Enabled.Value) return;
            foreach (Type state in new[] { recharge, charge, detonate })
                U.Harmony.Patch(A.Method(state, "OnEnter"), prefix: new HarmonyMethod(typeof(GenesisLoop), nameof(BeforeState)),
                    finalizer: new HarmonyMethod(typeof(GenesisLoop), nameof(AfterState)));
            foreach (string method in new[] { "FixedUpdate", "OnDamaged" })
                U.Harmony.Patch(A.Method(ready, method), transpiler: new HarmonyMethod(typeof(GenesisLoop), nameof(ThresholdIL)));
        }

        static float RechargeTime(int count) => c.F("Recharge Time") / (1 + c.F("Recharge Scaling Per Stack (%)") * Math.Max(0, count - 1) / 100);
        static CharacterBody Body(object state)
        {
            var body = A.Property(state.GetType(), "attachedBody")?.GetValue(state, null) as CharacterBody;
            if (body) return body;
            var entity = state as EntityState;
            var attachment = entity?.gameObject ? entity.gameObject.GetComponent<NetworkedBodyAttachment>() : null;
            return attachment ? attachment.attachedBody : null;
        }
        static void BeforeState(object __instance, out Dictionary<FieldInfo, object> __state)
        {
            __state = null;
            if (!c.Active) return;
            __state = new Dictionary<FieldInfo, object>();
            int count = Math.Max(1, c.Count(Body(__instance)));
            if (__instance.GetType() == recharge)
                Set(__state, recharge, "baseDuration", RechargeTime(count) * (count + 1));
            if (__instance.GetType() == charge)
            {
                Set(__state, charge, "baseDuration", c.Total("Charge Time", count));
                Set(__state, detonate, "blastRadius", c.Total("Explosion Radius", count));
            }
            if (__instance.GetType() == detonate)
            {
                Set(__state, detonate, "blastDamageCoefficient", c.Total("Damage (%)", count) / 100);
                Set(__state, detonate, "blastProcCoefficient", c.Total("Proc Coefficient", count));
                Set(__state, detonate, "blastRadius", c.Total("Explosion Radius", count));
                Set(__state, detonate, "blastForce", c.Total("Knockback Force", count));
                Set(__state, detonate, "baseDuration", c.Total("Recovery Time", count));
            }
        }
        static void Set(Dictionary<FieldInfo, object> saved, Type type, string name, float value)
        {
            var field = type.GetField(name, Fields);
            saved[field] = field.GetValue(null);
            field.SetValue(null, value);
        }
        static Exception AfterState(Dictionary<FieldInfo, object> __state, Exception __exception)
        {
            if (__state != null) foreach (var pair in __state) pair.Key.SetValue(null, pair.Value);
            return __exception;
        }
        static bool LowHealth(HealthComponent health)
        {
            if (!c.Active) return health.isHealthLow;
            int count = Math.Max(1, c.Count(health.body));
            return health.combinedHealthFraction <= Mathf.Clamp01(c.Total("Health Threshold (%)", count) / 100);
        }
        static bool HitLowHealth(DamageReport report)
        {
            if (!c.Active) return report.hitLowHealth;
            if (!report.victim) return false;
            int count = Math.Max(1, c.Count(report.victimBody));
            return report.combinedHealthBeforeDamage - report.damageDealt <= report.victim.fullCombinedHealth * Mathf.Clamp01(c.Total("Health Threshold (%)", count) / 100);
        }
        static IEnumerable<CodeInstruction> ThresholdIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int found = 0;
            foreach (var instruction in code)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(HealthComponent) && method.Name == "get_isHealthLow")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = A.Method(typeof(GenesisLoop), nameof(LowHealth));
                    found++;
                }
                else if (instruction.operand is FieldInfo field && field.DeclaringType == typeof(DamageReport) && field.Name == "hitLowHealth")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = A.Method(typeof(GenesisLoop), nameof(HitLowHealth));
                    found++;
                }
            }
            if (found != 1) throw new InvalidOperationException("Genesis Loop health threshold anchor changed: " + found);
            return code;
        }
        static string Description()
        {
            if (c.Numbers.Count == 0) return vanillaDescription ?? "";
            return "Falling below <style=cIsHealth>" + c.F("Health Threshold (%)").ToString("0.##") + "% health</style> causes you to explode, dealing "
                + c.V("Damage (%)", "%") + " base damage. Recharges every <style=cIsUtility>" + c.F("Recharge Time").ToString("0.##") + " seconds</style>"
                + (c.F("Recharge Scaling Per Stack (%)") == 0 ? "" : " <style=cStack>(+" + c.F("Recharge Scaling Per Stack (%)").ToString("0.##") + "% per stack)</style>") + ".";
        }
    }
}
