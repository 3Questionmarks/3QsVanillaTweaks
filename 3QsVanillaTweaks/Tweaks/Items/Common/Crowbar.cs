using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class Crowbar
    {
        internal enum DamageMode
        {
            AboveHPThreshold,
            FirstHit
        }

        static ConfigEntry<bool> enabled, procs;
        static ConfigEntry<DamageMode> mode;
        static ConfigEntry<float> damage, extra, threshold;
        sealed class Scope
        {
            internal Scope Previous;
            internal HealthComponent Target;
            internal DamageInfo Info;
            internal CharacterMaster Master;
            internal int Count;
            internal bool Eligible;
            internal float BeforeTotal;
            internal bool AcceptedDamage;
        }

        [ThreadStatic]
        static Scope scope;
        static ConditionalWeakTable<HealthComponent, HashSet<CharacterMaster>> hits = new ConditionalWeakTable<HealthComponent, HashSet<CharacterMaster>>();
        internal static void Init()
        {
            string s = S.Section("Crowbar");
            enabled = S.Enable(s);
            damage = S.Num(s, "Damage Bonus (%)", 50, "First-stack bonus damage. Vanilla: 75%.");
            extra = S.Num(s, "Damage Bonus Per Stack (%)", 50, "Additional damage per extra stack. Vanilla: 75%.");
            threshold = S.Num(s, "HP Threshold (%)", 90, "Combined health and shield threshold for AboveHPThreshold mode. Vanilla: 90%.", 0, 100);
            mode = C.Bind(s, "Apply Damage Mode", DamageMode.FirstHit, "FirstHit: holder's first accepted hit on each target, requiring non-zero proc coefficient. AboveHPThreshold: vanilla health-threshold mode.");
            procs = C.BindBool(s, "Damage Affects Procs", true, "Include the bonus in the damage used by on-hit procs. Vanilla: false.");
            if (!enabled.Value)
                return;
            GlobalEventManager.onServerDamageDealt += report =>
            {
                if (scope != null && report.damageInfo == scope.Info && report.damageDealt > 0 && !report.damageInfo.rejected)
                    scope.AcceptedDamage = true;
            };
            var h = S.Harmony("Crowbar");
            h.Patch(A.Method(typeof(HealthComponent), "TakeDamageProcess"), prefix: new HarmonyMethod(typeof(Crowbar), nameof(Before)), postfix: new HarmonyMethod(typeof(Crowbar), nameof(After)), transpiler: new HarmonyMethod(typeof(Crowbar), nameof(IL)), finalizer: new HarmonyMethod(typeof(Crowbar), nameof(Finish)), ilmanipulator: null);
            S.Text("ITEM_CROWBAR_DESC", () => "Deal <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> bonus damage " + (C.Get(mode) == DamageMode.FirstHit ? "on your first hit against each enemy." : "to enemies above <style=cIsHealth>" + C.Get(threshold) + "% combined health</style>."));
        }

        static void Before(HealthComponent __instance, DamageInfo damageInfo, out Scope __state)
        {
            __state = new Scope
            {
                Previous = scope,
                Target = __instance,
                BeforeTotal = __instance.health + __instance.shield + __instance.barrier,
                Info = damageInfo
            };
            scope = __state;
            if (!S.Active(enabled) || !NetworkServer.active || damageInfo.rejected || damageInfo.damage <= 0 || !damageInfo.attacker)
                return;
            var body = damageInfo.attacker.GetComponent<CharacterBody>();
            scope.Master = body ? body.master : null;
            scope.Count = S.Count(body, RoR2Content.Items.Crowbar);
            if (scope.Count <= 0 || !scope.Master)
                return;
            scope.Eligible = C.Get(mode) == DamageMode.FirstHit ? damageInfo.procCoefficient > 0 && !hits.GetValue(__instance, k => new HashSet<CharacterMaster>()).Contains(scope.Master) : __instance.combinedHealth >= __instance.fullCombinedHealth * C.Get(threshold) / 100f;
            if (scope.Eligible && C.Get(procs))
                damageInfo.damage *= 1 + S.Total(damage, extra, scope.Count) / 100f;
        }

        static void After()
        {
            if (scope != null && scope.Eligible && !scope.Info.rejected && scope.AcceptedDamage && scope.Master && C.Get(mode) == DamageMode.FirstHit)
                hits.GetValue(scope.Target, k => new HashSet<CharacterMaster>()).Add(scope.Master);
        }

        static Exception Finish(Scope __state, Exception __exception)
        {
            scope = __state?.Previous;
            return __exception;
        }

        static float Gate(float native) => S.Active(enabled) ? 0 : native;
        static int NativeCount(int original) => S.Active(enabled) ? scope != null && scope.Eligible && !C.Get(procs) ? original : 0 : original;
        static float Coefficient(float original) => S.Active(enabled) && scope != null && scope.Count > 0 ? S.Total(damage, extra, scope.Count) / 100f / scope.Count : original;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int at = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "Crowbar");
            if (at < 0)
                throw new InvalidOperationException("Crowbar item anchor missing");
            int gate = -1;
            for (int i = at - 1; i >= Math.Max(0, at - 20); i--)
                if (list[i].LoadsConstant(.9f))
                {
                    gate = i;
                    break;
                }

            if (gate < 0)
                throw new InvalidOperationException("Crowbar HP gate missing");
            list.Insert(gate + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(Crowbar), nameof(Gate))));
            at++;
            int call = at + 1;
            while (call < at + 8 && !(list[call].operand is MethodInfo m && m.Name == "GetItemCountEffective"))
                call++;
            if (call >= at + 8)
                throw new InvalidOperationException("Crowbar count missing");
            list.Insert(call + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(Crowbar), nameof(NativeCount))));
            int bonus = list.FindIndex(call + 2, Math.Min(20, list.Count - call - 2), i => i.LoadsConstant(.75f));
            if (bonus < 0)
                throw new InvalidOperationException("Crowbar coefficient missing");
            list.Insert(bonus + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(Crowbar), nameof(Coefficient))));
            return list;
        }
    }
}
