using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class StunGrenade
    {
        static ConfigEntry<bool> enabled, linear;
        static ConfigEntry<float> chance, extra, duration, durationStack, damage, damageStack, proc, procStack;
        [ThreadStatic] static bool dealingDamage;
        internal static void Init()
        {
            var s = S.Section("Stun Grenade");
            enabled = S.Enable(s);
            linear = C.BindBool(s, "Linear Scaling", true, "Stack stun chance linearly. False restores vanilla hyperbolic conversion.");
            chance = S.Num(s, "Trigger Chance (%)", 5, "Base chance before proc-coefficient scaling. Vanilla: 5%.");
            extra = S.Num(s, "Trigger Chance Per Stack (%)", 5, "Chance added by each additional stack. Vanilla: 5%.");
            duration = S.Num(s, "Stun Duration", 2, "Stun duration in seconds. Vanilla: 2.");
            durationStack = S.Num(s, "Stun Duration Per Stack", 0, "Additional duration per extra stack. Vanilla: 0.");
            damage = S.Num(s, "Damage (%)", 100, "Base damage dealt to the target when Stun Grenade triggers. Vanilla: 0%.");
            damageStack = S.Num(s, "Damage Per Stack (%)", 0, "Additional base damage per extra item stack. Vanilla: 0%.");
            proc = S.Num(s, "Proc Coefficient", 0, "Proc coefficient of the extra damage hit. Default: 0.");
            procStack = S.Num(s, "Proc Coefficient Per Stack", 0, "Additional proc coefficient per extra stack. Default: 0.");
            if (!enabled.Value)
                return;
            S.Patch(S.Harmony("StunGrenade"), typeof(SetStateOnHurt), "OnTakeDamageServer", typeof(StunGrenade), transpiler: nameof(IL));
            S.Text("ITEM_STUNCHANCEONHIT_DESC", () => "<style=cIsDamage>" + C.Get(chance) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> chance on hit to <style=cIsDamage>stun</style> for <style=cIsUtility>" + C.Get(duration) + "s</style> and deal <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(damageStack) + "% per stack)</style> damage.");
        }

        static float Convert(float incoming, DamageReport report)
        {
            if (!S.Active(enabled))
                return Util.ConvertAmplificationPercentageIntoReductionPercentage(incoming);
            int n = S.Count(report.attackerBody, RoR2Content.Items.StunChanceOnHit);
            if (n <= 0)
                return Util.ConvertAmplificationPercentageIntoReductionPercentage(incoming);
            float adjusted = Mathf.Max(0, incoming + (S.Total(chance, extra, n) - 5 * n) * report.damageInfo.procCoefficient);
            return C.Get(linear) ? adjusted : Util.ConvertAmplificationPercentageIntoReductionPercentage(adjusted);
        }

        static float Duration(float native, DamageReport r)
        {
            int n = S.Count(r.attackerBody, RoR2Content.Items.StunChanceOnHit);
            return S.Active(enabled) && n > 0 ? S.Total(duration, durationStack, n) : native;
        }

        static void ProcDamage(DamageReport report)
        {
            if (!S.Active(enabled) || !NetworkServer.active || dealingDamage || report == null ||
                !report.attackerBody || !report.victim || !report.victim.alive ||
                report.damageInfo.rejected || report.damageInfo.procCoefficient <= 0)
                return;
            int stacks = S.Count(report.attackerBody, RoR2Content.Items.StunChanceOnHit);
            float coefficient = S.Total(damage, damageStack, stacks) / 100;
            if (stacks <= 0 || coefficient <= 0) return;
            var hit = new DamageInfo
            {
                attacker = report.damageInfo.attacker,
                inflictor = report.damageInfo.attacker,
                damage = report.attackerBody.damage * coefficient,
                crit = report.damageInfo.crit,
                position = report.damageInfo.position,
                force = Vector3.zero,
                procCoefficient = S.Total(proc, procStack, stacks),
                procChainMask = report.damageInfo.procChainMask,
                damageColorIndex = DamageColorIndex.Item
            };
            // The bonus hit can never trigger another bonus hit from this module,
            // including when the user opts into a non-zero proc coefficient.
            dealingDamage = true;
            try
            {
                report.victim.TakeDamage(hit);
                if (!hit.rejected && hit.procCoefficient > 0 && GlobalEventManager.instance)
                {
                    GlobalEventManager.instance.OnHitEnemy(hit, report.victim.gameObject);
                    GlobalEventManager.instance.OnHitAll(hit, report.victim.gameObject);
                }
            }
            finally { dealingDamage = false; }
        }

        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int conversions = 0, grenadeStuns = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].operand is MethodInfo method && method.Name == "ConvertAmplificationPercentageIntoReductionPercentage")
                {
                    list.Insert(i, new CodeInstruction(OpCodes.Ldarg_1));
                    i++;
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = A.Method(typeof(StunGrenade), nameof(Convert));
                    conversions++;
                }
                // Only the first SetStun belongs to the successful item roll.
                // Later calls handle stun damage flags from character abilities.
                if (grenadeStuns == 0 && conversions > 0 && list[i].operand is MethodInfo stun &&
                    stun.Name == "SetStun" && i > 0 && list[i - 1].LoadsConstant(2f))
                {
                    list.InsertRange(i, new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Call, A.Method(typeof(StunGrenade), nameof(Duration))) });
                    i += 2;
                    list.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Call, A.Method(typeof(StunGrenade), nameof(ProcDamage))) });
                    i += 2;
                    grenadeStuns++;
                }
            }
            if (conversions != 1 || grenadeStuns != 1)
                throw new InvalidOperationException("Stun Grenade native roll/stun anchors changed.");
            return list;
        }
    }
}
