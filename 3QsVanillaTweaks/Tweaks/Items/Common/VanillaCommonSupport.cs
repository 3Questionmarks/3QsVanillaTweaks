using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    // Unchanged items bind their vanilla defaults even when their changes are disabled.
    internal sealed class VanillaCommonConfig
    {
        internal readonly string Section;
        internal readonly ConfigEntry<bool> Enabled;
        internal bool Active => C.Get(Enabled);

        internal VanillaCommonConfig(string name, bool defaultEnabled = false)
        {
            Section = S.Section(name);
            Enabled = C.BindStartupBool(Section, "Enabled", defaultEnabled, defaultEnabled ? "Enable this item's configurable changes. Restart to apply." : "Enable this item's configurable changes. Defaults preserve vanilla. Restart to apply.");
            if (Enabled.Value)
                S.Ensure();
        }

        internal void Describe(string token, Func<string> description)
        {
            if (Enabled.Value)
                S.Text(token, description);
        }

        internal ConfigEntry<float> Number(string key, float value, string description, float min = 0, float max = 100000) => S.Num(Section, key, value, description, min, max);
        internal ConfigEntry<int> Integer(string key, int value, string description, int min = 0, int max = 10000) => S.Int(Section, key, value, description, min, max);
        internal float Value(ConfigEntry<float> entry) => C.Get(entry);
        internal void Count(string type, string method, string item, float native, ConfigEntry<float> first, ConfigEntry<float> extra, float unit = 1)
        {
            if (Enabled.Value)
                VanillaCommonSupport.AddCount(this, type, method, item, native, first, extra, unit);
        }

        internal void Literal(string type, string method, float native, Func<float> value, string anchor = null, int occurrence = 0, int window = int.MaxValue)
        {
            if (Enabled.Value)
                VanillaCommonSupport.AddLiteral(this, type, method, native, value, anchor, occurrence, window);
        }
    }

    internal static class VanillaCommonSupport
    {
        sealed class Rule
        {
            internal VanillaCommonConfig Config;
            internal string Item, Anchor;
            internal float Native;
            internal float Unit = 1;
            internal Func<float> Value;
            internal ConfigEntry<float> First, Extra;
            internal int Occurrence, Window;
            internal bool Count;
        }

        static readonly List<Rule> rules = new List<Rule>();
        static readonly Dictionary<MethodBase, List<int>> methods = new Dictionary<MethodBase, List<int>>();
        [ThreadStatic]
        internal static CharacterBody Body;
        static void Before(object __instance, object[] __args, out CharacterBody __state)
        {
            __state = Body;
            Body = __instance as CharacterBody ?? (__instance as HealthComponent)?.body;
            if (Body == null)
                foreach (var argument in __args)
                {
                    if (argument is DamageReport report)
                    {
                        Body = report.attackerBody;
                        break;
                    }

                    if (argument is DamageInfo info && info.attacker)
                    {
                        Body = info.attacker.GetComponent<CharacterBody>();
                        break;
                    }
                }
        }

        static Exception Finish(CharacterBody __state, Exception __exception)
        {
            Body = __state;
            return __exception;
        }

        static readonly Harmony harmony = S.Harmony("VanillaDefaults");
        static MethodInfo Target(string type, string method)
        {
            var target = A.Method(typeof(CharacterBody).Assembly.GetType(type, true), method);
            if (target == null)
                throw new MissingMethodException(type, method);
            return target;
        }

        static void Add(string type, string method, Rule rule)
        {
            var target = Target(type, method);
            if (!methods.TryGetValue(target, out var list))
                methods[target] = list = new List<int>();
            list.Add(rules.Count);
            rules.Add(rule);
            // Rebuild from the original IL as more item modules register the same target.
            harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(VanillaCommonSupport), nameof(Before)), transpiler: new HarmonyMethod(typeof(VanillaCommonSupport), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(VanillaCommonSupport), nameof(Finish)), ilmanipulator: null);
        }

        internal static void AddCount(VanillaCommonConfig c, string type, string method, string item, float native, ConfigEntry<float> first, ConfigEntry<float> extra, float unit) => Add(type, method, new Rule { Config = c, Item = item, Native = native, First = first, Extra = extra, Unit = unit, Count = true });
        internal static void AddLiteral(VanillaCommonConfig c, string type, string method, float native, Func<float> value, string anchor, int occurrence, int window) => Add(type, method, new Rule { Config = c, Native = native, Value = value, Anchor = anchor, Occurrence = occurrence, Window = window });
        static float Scaled(int count, int id)
        {
            var r = rules[id];
            return r.Config.Active ? S.Linear(count, C.Get(r.First), C.Get(r.Extra)) / (r.Native * r.Unit) : count;
        }

        static float Literal(float native, int id)
        {
            var r = rules[id];
            return r.Config.Active ? r.Value() : native;
        }

        static int Local(CodeInstruction i, bool store)
        {
            if (i.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0))
                return 0;
            if (i.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1))
                return 1;
            if (i.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2))
                return 2;
            if (i.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3))
                return 3;
            if (i.opcode != (store ? OpCodes.Stloc : OpCodes.Ldloc) && i.opcode != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S))
                return -1;
            if (i.operand is LocalBuilder b)
                return b.LocalIndex;
            if (i.operand is LocalVariableInfo v)
                return v.LocalIndex;
            return Convert.ToInt32(i.operand);
        }

        static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            var code = new List<CodeInstruction>(input);
            foreach (int id in methods[__originalMethod])
            {
                var rule = rules[id];
                int found = 0;
                if (rule.Count)
                {
                    int anchor = code.FindIndex(i => i.operand is FieldInfo f && f.Name == rule.Item);
                    int local = -1;
                    if (anchor >= 0 && char.IsUpper(rule.Item[0]))
                        for (int j = anchor + 1; j < Math.Min(anchor + 8, code.Count); j++)
                            if (Local(code[j], true) >= 0)
                            {
                                local = Local(code[j], true);
                                break;
                            }

                    if (anchor >= 0)
                        for (int j = 0; j + 1 < code.Count; j++)
                        {
                            if (!(local >= 0 ? Local(code[j], false) == local : code[j].operand is FieldInfo cached && cached.Name == rule.Item) || code[j + 1].opcode != OpCodes.Conv_R4)
                                continue;
                            bool coefficient = code.Skip(Math.Max(0, j - 4)).Take(4).Any(i => i.LoadsConstant(rule.Native)) || code.Skip(j + 2).Take(3).Any(i => i.LoadsConstant(rule.Native));
                            if (!coefficient)
                                continue;
                            var old = code[j + 1];
                            var load = new CodeInstruction(OpCodes.Ldc_I4, id);
                            load.labels.AddRange(old.labels);
                            load.blocks.AddRange(old.blocks);
                            code[j + 1] = load;
                            code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(VanillaCommonSupport), nameof(Scaled))));
                            found++;
                            j += 2;
                        }
                }
                else
                {
                    int start = rule.Anchor == null ? 0 : code.FindIndex(i => i.operand is FieldInfo f && f.Name == rule.Anchor);
                    if (start >= 0)
                    {
                        int seen = 0;
                        for (int j = start; j < Math.Min(code.Count, (long)start + rule.Window); j++)
                            if (code[j].LoadsConstant(rule.Native))
                            {
                                if (seen++ != rule.Occurrence)
                                    continue;
                                code.Insert(j + 1, new CodeInstruction(OpCodes.Ldc_I4, id));
                                code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(VanillaCommonSupport), nameof(Literal))));
                                found++;
                                break;
                            }
                    }
                }

                if (found == 0)
                    throw new InvalidOperationException("No vanilla item anchor: " + rule.Config.Section + " / " + __originalMethod.Name + " / " + rule.Item + " / " + rule.Native);
            }

            return code;
        }
    }
}
