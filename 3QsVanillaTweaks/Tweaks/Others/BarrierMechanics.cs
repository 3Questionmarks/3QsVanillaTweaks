using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others
{
    internal static class BarrierMechanics
    {
        internal enum DecayMode { Vanilla, VanillaScaled, PreCollective, CustomFormula }
        private const string Section = "Other - Barrier Mechanics";
        private static ConfigEntry<bool> enabled, gate, preCurse;
        private static ConfigEntry<float> maximum, multiplier;
        private static ConfigEntry<DecayMode> decay;
        private static ConfigEntry<string> formula;
        private static readonly PropertyInfo maxBarrierProperty = AccessTools.Property(typeof(CharacterBody), "maxBarrier");
        private static bool Active => enabled != null && C.Get(enabled);
        internal static bool UsesPreCurseMaximum => Active && C.Get(preCurse);
        private static string cachedFormula, warnedFormula;
        private static Func<double, double, double> compiledFormula;

        internal static void Init()
        {
            enabled = C.BindStartupBool(Section, "Enabled", true, "Enable general barrier changes. Restart to apply.");
            gate = C.BindBool(Section, "Barrier Gates HP", true, "Damage that breaks existing barrier cannot spill into shield or health. The next hit can damage the remaining pools normally. Vanilla: False.");
            maximum = C.Bind(Section, "Max Barrier (%)", 100f, new ConfigDescription("Maximum barrier as a percentage of maximum combined health and shield. Vanilla: 100%.", new AcceptableValueRange<float>(0f, 1000000f)));
            preCurse = C.BindBool(Section, "Max Barrier Uses Pre-Curse HP", true, "Calculate maximum barrier before HP curse reduces maximum health and shield. Shields already exempt from curse are not counted twice. Vanilla: False.");
            decay = C.Bind(Section, "Barrier Decay Formula", DecayMode.Vanilla, "Vanilla: current game formula. VanillaScaled: multiply the complete vanilla formula. PreCollective: maxBarrier / 30 per second. CustomFormula: evaluate the custom expression; invalid results fall back to vanilla.");
            multiplier = C.Bind(Section, "Barrier Decay Multiplier", 0.5f, new ConfigDescription("Multiplier for the complete vanilla barrier decay rate, used only by VanillaScaled mode.", new AcceptableValueRange<float>(0f, 1000000f)));
            formula = C.Bind(Section, "Custom Barrier Decay Formula", "maxBarrier / 30 * (0.5 + 2.5 * barrier / maxBarrier)",
                "Barrier lost per second in CustomFormula mode. Variables: barrier (current), maxBarrier (capacity). Supports numbers, +, -, *, /, ^, parentheses, min(a,b), max(a,b), abs(a), and clamp(value,min,max). Negative, non-finite or invalid results use vanilla decay.");
            if (!enabled.Value) return;
            if (maxBarrierProperty?.GetSetMethod(true) == null) throw new MissingMemberException("CharacterBody.maxBarrier setter unavailable.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.BarrierMechanics");
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess"),
                transpiler: new HarmonyMethod(typeof(BarrierMechanics), nameof(DamageIL)));
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"),
                finalizer: new HarmonyMethod(typeof(BarrierMechanics), nameof(FinalizeStats)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "GetBarrierDecayRate"),
                postfix: new HarmonyMethod(typeof(BarrierMechanics), nameof(AfterDecay)));
            C.EffectiveSettingsChanged += RefreshStats;
            ThreeQsVanillaTweaks.Log.Info("Barrier mechanics: gate, maximum capacity and decay hooks installed.");
        }
        internal static float Capacity(float health, float shield, float curseFactor, bool shieldsCursed, bool beforeCurse, float percent)
        {
            float curse = Math.Max(1f, curseFactor);
            float hp = Math.Max(0f, health) * (beforeCurse ? curse : 1f);
            float shields = Math.Max(0f, shield) * (beforeCurse && shieldsCursed ? curse : 1f);
            return (hp + shields) * Math.Max(0f, percent) / 100f;
        }
        internal static float SpillDamage(float damage, float barrier, bool gates) => gates && barrier > 0f ? 0f : damage - barrier;
        private static float GateSpill(float damage, float barrier) => SpillDamage(damage, barrier, Active && C.Get(gate));
        private static bool GateBeforeOSP(bool eligible, HealthComponent health) => eligible && !(Active && C.Get(gate) && health.barrier > 0f);
        private static Exception FinalizeStats(CharacterBody __instance, Exception __exception)
        {
            if (__exception == null) AfterStats(__instance);
            return __exception;
        }
        private static void AfterStats(CharacterBody __instance)
        {
            if (!Active) return;
            float value = Capacity(__instance.maxHealth, __instance.maxShield, __instance.cursePenalty,
                ShieldMechanics.ShieldIsCurseReduced, C.Get(preCurse), C.Get(maximum));
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            maxBarrierProperty.GetSetMethod(true).Invoke(__instance, new object[] { value });
            if (NetworkServer.active && __instance.healthComponent && __instance.healthComponent.barrier > value)
                __instance.healthComponent.Networkbarrier = value;
        }
        private static void RefreshStats()
        {
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (body) body.RecalculateStats();
        }
        private static void AfterDecay(HealthComponent __instance, ref float __result)
        {
            if (!Active) return;
            float current = __instance.barrier, max = __instance.fullBarrier;
            bool valid;
            __result = DecayRate(C.Get(decay), current, max, __result, C.Get(multiplier), C.Get(formula), out valid);
            if (!valid && warnedFormula != C.Get(formula))
            {
                warnedFormula = C.Get(formula);
                ThreeQsVanillaTweaks.Log.Warning("Invalid custom barrier decay formula or result; using vanilla decay.");
            }
        }
        internal static float DecayRate(DecayMode mode, float barrier, float maximum, float vanilla, float scale, string expression, out bool valid)
        {
            valid = true;
            if (maximum <= 0f) return 0f;
            float fallback = float.IsNaN(vanilla) || float.IsInfinity(vanilla) ? 0f : Math.Max(0f, vanilla);
            switch (mode)
            {
                case DecayMode.Vanilla: return fallback;
                case DecayMode.VanillaScaled: return fallback * Math.Max(0f, scale);
                case DecayMode.PreCollective: return maximum / 30f;
                case DecayMode.CustomFormula:
                    try
                    {
                        if (expression != cachedFormula)
                        {
                            cachedFormula = expression; compiledFormula = null;
                            compiledFormula = Formula.Compile(expression);
                        }
                        if (compiledFormula == null) { valid = false; return fallback; }
                        double result = compiledFormula(barrier, maximum);
                        if (double.IsNaN(result) || double.IsInfinity(result) || result < 0d || result > float.MaxValue)
                        { valid = false; return fallback; }
                        return (float)result;
                    }
                    catch (FormatException) { valid = false; return fallback; }
                    catch (OverflowException) { valid = false; return fallback; }
                default: valid = false; return fallback;
            }
        }
        private static IEnumerable<CodeInstruction> DamageIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(c => new CodeInstruction(c)).ToList();
            int spills = 0, osp = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction c = code[i];
                if (c.opcode == OpCodes.Sub && i > 0 && code[i-1].operand is FieldInfo f &&
                    f.DeclaringType == typeof(HealthComponent) && f.Name == "barrier")
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(BarrierMechanics), nameof(GateSpill)); spills++; }
                yield return c;
                if (c.operand is MethodInfo m && (m.Name == "get_hasOneShotProtection" ||
                    m.DeclaringType == typeof(OneShotProtection) && m.Name == "Eligible"))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(BarrierMechanics), nameof(GateBeforeOSP)));
                    osp++;
                }
            }
            if (spills != 1 || osp != 1) throw new InvalidOperationException("Barrier gate anchors changed: spill=" + spills + ", OSP=" + osp);
        }

        // Parse once into arithmetic delegates. No script engine or arbitrary code execution.
        private sealed class Formula
        {
            private readonly string text;
            private int pos, depth;
            private Formula(string text)
            {
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1024) throw new FormatException("Invalid formula length.");
                this.text = text;
            }
            internal static Func<double, double, double> Compile(string expression)
            {
                var parser = new Formula(expression);
                var result = parser.Sum(); parser.Skip();
                if (parser.pos != parser.text.Length) throw new FormatException("Unexpected formula text.");
                return result;
            }
            private void Skip() { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }
            private bool Take(char symbol)
            {
                Skip(); if (pos < text.Length && text[pos] == symbol) { pos++; return true; } return false;
            }
            private Func<double, double, double> Sum()
            {
                var value = Product();
                while (true)
                {
                    var left = value;
                    if (Take('+')) { var right = Product(); value = (b,m) => left(b,m) + right(b,m); }
                    else if (Take('-')) { var right = Product(); value = (b,m) => left(b,m) - right(b,m); }
                    else return value;
                }
            }
            private Func<double, double, double> Product()
            {
                var value = Unary();
                while (true)
                {
                    var left = value;
                    if (Take('*')) { var right = Unary(); value = (b,m) => left(b,m) * right(b,m); }
                    else if (Take('/')) { var right = Unary(); value = (b,m) => left(b,m) / right(b,m); }
                    else return value;
                }
            }
            private Func<double, double, double> Unary()
            {
                if (++depth > 32) throw new FormatException("Formula nesting too deep.");
                try
                {
                    if (Take('+')) return Unary();
                    if (Take('-')) { var value = Unary(); return (b,m) => -value(b,m); }
                    var left = Atom();
                    if (!Take('^')) return left;
                    var right = Unary(); return (b,m) => Math.Pow(left(b,m),right(b,m));
                }
                finally { depth--; }
            }
            private Func<double, double, double> Atom()
            {
                if (Take('(')) { var value = Sum(); if (!Take(')')) throw new FormatException("Missing )."); return value; }
                Skip();
                if (pos >= text.Length) throw new FormatException("Incomplete formula.");
                int start = pos;
                if (char.IsLetter(text[pos]))
                {
                    while (pos < text.Length && char.IsLetter(text[pos])) pos++;
                    string name = text.Substring(start,pos-start);
                    if (name.Equals("barrier",StringComparison.OrdinalIgnoreCase)) return (b,m) => b;
                    if (name.Equals("maxBarrier",StringComparison.OrdinalIgnoreCase)) return (b,m) => m;
                    if (!Take('(')) throw new FormatException("Unknown variable.");
                    var first = Sum();
                    if (name.Equals("abs",StringComparison.OrdinalIgnoreCase))
                    { if (!Take(')')) throw new FormatException("Expected one argument."); return (b,m) => Math.Abs(first(b,m)); }
                    if (!Take(',')) throw new FormatException("Expected comma.");
                    var second = Sum();
                    if (name.Equals("clamp",StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Take(',')) throw new FormatException("Expected three arguments.");
                        var third = Sum();
                        if (!Take(')')) throw new FormatException("Missing ).");
                        return (b,m) => { double lo=second(b,m), hi=third(b,m); return lo>hi ? double.NaN : Math.Max(lo,Math.Min(hi,first(b,m))); };
                    }
                    if (!Take(')')) throw new FormatException("Expected two arguments.");
                    if (name.Equals("min",StringComparison.OrdinalIgnoreCase)) return (b,m) => Math.Min(first(b,m),second(b,m));
                    if (name.Equals("max",StringComparison.OrdinalIgnoreCase)) return (b,m) => Math.Max(first(b,m),second(b,m));
                    throw new FormatException("Unknown function.");
                }
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos]=='.')) pos++;
                if (pos < text.Length && (text[pos]=='e' || text[pos]=='E'))
                { pos++; if (pos<text.Length && (text[pos]=='+'||text[pos]=='-')) pos++; while(pos<text.Length&&char.IsDigit(text[pos]))pos++; }
                if (start==pos || !double.TryParse(text.Substring(start,pos-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double number))
                    throw new FormatException("Expected a number.");
                return (b,m) => number;
            }
        }
    }
}
