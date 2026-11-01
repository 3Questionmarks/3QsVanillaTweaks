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

namespace Tweaks.Items.Uncommon
{
    internal static class UncommonNative
    {
        sealed class Rule
        {
            internal UncommonConfig Config;
            internal string Item, Anchor;
            internal float Native;
            internal float Unit = 1;
            internal Func<float> Value;
            internal ConfigEntry<float> First, Extra;
            internal int Occurrence, Window;
            internal bool Count;
            internal bool Field, Assign;
        }

        static readonly List<Rule> rules = new List<Rule>();
        static readonly Dictionary<MethodBase, List<int>> methods = new Dictionary<MethodBase, List<int>>();
        [ThreadStatic]
        internal static CharacterBody Body;
        [ThreadStatic]
        internal static DamageInfo Info;
        [ThreadStatic]
        internal static CharacterBody Source;
        [ThreadStatic]
        static Stack<CharacterBody> sourceStack;
        [ThreadStatic]
        static Stack<DamageInfo> infoStack;
        static void Before(object __instance, object[] __args, out CharacterBody __state)
        {
            __state = Body;
            if (infoStack == null)
                infoStack = new Stack<DamageInfo>();
            infoStack.Push(Info);
            if (sourceStack == null)
                sourceStack = new Stack<CharacterBody>();
            sourceStack.Push(Source);
            Info = __args.OfType<DamageInfo>().FirstOrDefault() ?? __args.OfType<DamageReport>().FirstOrDefault()?.damageInfo;
            Body = __instance as CharacterBody ?? (__instance as HealthComponent)?.body;
            if (Body == null && __instance is RoR2.Items.BaseItemBodyBehavior behaviour)
                Body = behaviour.body;
            if (Body == null && __instance is EntityStates.EntityState state)
            {
                Body = state.characterBody;
                if (!Body)
                {
                    var ownership = state.GetComponent<GenericOwnership>();
                    if (ownership && ownership.ownerObject)
                        Body = ownership.ownerObject.GetComponent<CharacterBody>();
                }
            }

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

            Source = Info != null && Info.attacker ? Info.attacker.GetComponent<CharacterBody>() : __args.OfType<CharacterBody>().FirstOrDefault() ?? Body;
        }

        static Exception Finish(CharacterBody __state, Exception __exception)
        {
            Body = __state;
            Info = infoStack.Pop();
            Source = sourceStack.Pop();
            return __exception;
        }

        static readonly Harmony harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Uncommon.Native");
        static MethodInfo Target(string type, string method)
        {
            var target = typeof(CharacterBody).Assembly.GetType(type, true).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == method).OrderByDescending(m => m.GetParameters().Length).FirstOrDefault();
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
            if (!batching) Apply(target);
        }

        static bool batching;
        static readonly HashSet<MethodBase> installed = new HashSet<MethodBase>();

        internal static void BeginRegistration() { batching = true; }

        internal static void FinishRegistration()
        {
            batching = false;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            int count = 0;
            foreach (var target in methods.Keys.ToArray())
            {
                try { Apply(target); count++; }
                catch (Exception exception)
                {
                    ThreeQsVanillaTweaks.Log.Warning("[Startup] Failed item patch: " + target.DeclaringType.FullName + "." + target.Name + ": " + exception);
                }
            }
            ThreeQsVanillaTweaks.Log.Info("[Startup] Shared item patches: " + count + " methods, " + rules.Count + " rules, " + timer.Elapsed.TotalSeconds.ToString("F2") + " s.");
        }

        static void Apply(MethodBase target)
        {
            // Harmony rebuilds the original method with all registered rules.
            // Late registrations still work after the startup batch is complete.
            if (installed.Contains(target))
                harmony.Unpatch(target, HarmonyPatchType.All, harmony.Id);
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(UncommonNative), nameof(Before)), transpiler: new HarmonyMethod(typeof(UncommonNative), nameof(Transpile)), finalizer: new HarmonyMethod(typeof(UncommonNative), nameof(Finish)), ilmanipulator: null);
            installed.Add(target);
        }

        internal static void AddCount(UncommonConfig c, string type, string method, string item, float native, ConfigEntry<float> first, ConfigEntry<float> extra, float unit) => Add(type, method, new Rule { Config = c, Item = item, Native = native, First = first, Extra = extra, Unit = unit, Count = true });
        internal static void AddLiteral(UncommonConfig c, string type, string method, float native, Func<float> value, string anchor, int occurrence, int window) => Add(type, method, new Rule { Config = c, Native = native, Value = value, Anchor = anchor, Occurrence = occurrence, Window = window });
        internal static void AddAssign(UncommonConfig c, string type, string method, string member, Func<float> value, string anchor) => Add(type, method, new Rule { Config = c, Item = member, Value = value, Anchor = anchor, Assign = true });
        internal static void AddField(UncommonConfig c, string type, string method, string field, Func<float> value) => Add(type, method, new Rule { Config = c, Anchor = field, Value = value, Field = true });
        static float Scaled(int count, int id)
        {
            var r = rules[id];
            return r.Config.Active ? S.Linear(count, C.Get(r.First), C.Get(r.Extra)) / (r.Native * r.Unit) : count;
        }

        static int IntLiteral(int native, int id) => (int)Math.Round(Literal(native, id));
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
            var original = new HashSet<CodeInstruction>(code);
            foreach (int id in methods[__originalMethod])
            {
                var rule = rules[id];
                int found = 0;
                if (rule.Assign)
                {
                    int start = rule.Anchor == null ? 0 : code.FindIndex(i => i.operand is FieldInfo f && f.Name == rule.Anchor);
                    for (int j = Math.Max(0, start); start >= 0 && j < code.Count; j++)
                        if (code[j].opcode == OpCodes.Stfld && code[j].operand is FieldInfo f && f.Name == rule.Item && f.FieldType == typeof(float))
                        {
                            var load = new CodeInstruction(OpCodes.Ldc_I4, id);
                            load.labels.AddRange(code[j].labels);
                            code[j].labels.Clear();
                            code.Insert(j, load);
                            code.Insert(j + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(UncommonNative), nameof(Literal))));
                            found++;
                            break;
                        }
                }
                else if (rule.Field)
                {
                    for (int j = 0; j < code.Count; j++)
                        if (code[j].operand is FieldInfo f && f.Name == rule.Anchor && f.FieldType == typeof(float) && (code[j].opcode == OpCodes.Ldsfld || code[j].opcode == OpCodes.Ldfld))
                        {
                            code.Insert(j + 1, new CodeInstruction(OpCodes.Ldc_I4, id));
                            code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(UncommonNative), nameof(Literal))));
                            found++;
                            j += 2;
                        }
                }
                else if (rule.Count)
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
                            code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(UncommonNative), nameof(Scaled))));
                            found++;
                            j += 2;
                        }
                }
                else
                {
                    int start = rule.Anchor == null ? 0 : code.FindIndex(i => i.operand is FieldInfo f && f.Name == rule.Anchor);
                    if (start >= 0)
                    {
                        bool hasFloat = code.Skip(start).Take(rule.Window).Any(i => original.Contains(i) && i.opcode == OpCodes.Ldc_R4 && i.LoadsConstant(rule.Native));
                        int seen = 0;
                        for (int j = start; j < Math.Min(code.Count, (long)start + rule.Window); j++)
                            if (original.Contains(code[j]) && (hasFloat ? code[j].opcode == OpCodes.Ldc_R4 && code[j].LoadsConstant(rule.Native) : rule.Native == (int)rule.Native && code[j].LoadsConstant((int)rule.Native)))
                            {
                                if (seen++ != rule.Occurrence)
                                    continue;
                                code.Insert(j + 1, new CodeInstruction(OpCodes.Ldc_I4, id));
                                code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(UncommonNative), code[j].opcode == OpCodes.Ldc_R4 ? nameof(Literal) : nameof(IntLiteral))));
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
