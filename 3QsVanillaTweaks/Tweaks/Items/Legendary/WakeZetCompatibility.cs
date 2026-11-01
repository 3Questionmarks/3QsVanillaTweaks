using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class WakeZetCompatibility
    {
        static Func<bool> active;
        static Func<float> first, extra;
        internal static void Install(Func<bool> enabled, Func<float> baseDuration, Func<float> stackDuration)
        {
            active = enabled;
            first = baseDuration;
            extra = stackDuration;
            RoR2Application.onLoad += Apply;
        }

        static void Apply()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name.Equals("ZetAspects", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
                return;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            int count = 0;
            var h = new Harmony("3Questionmarks.3QsVanillaTweaks.WakeZetDuration");
            foreach (var t in types.Where(t => t.FullName.IndexOf("Hook", StringComparison.OrdinalIgnoreCase) >= 0))
                foreach (var method in t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null)
                        continue;
                    try
                    {
                        var code = PatchProcessor.GetOriginalInstructions(method);
                        if (!code.Any(i => i.operand is MethodInfo m && (m.Name == "get_HeadHunterBaseDuration" || m.Name == "get_HeadHunterStackDuration")))
                            continue;
                        h.Patch(method, transpiler: new HarmonyMethod(typeof(WakeZetCompatibility), nameof(DurationIL)));
                        count++;
                    }
                    catch (Exception e)
                    {
                        ThreeQsVanillaTweaks.Log.Warning("Wake of Vultures: ZetAspects duration hook unavailable for " + method.Name + ": " + e.Message);
                    }
                }

            ThreeQsVanillaTweaks.Log.Info("Wake of Vultures: adapted " + count + " ZetAspects duration methods; aspect effects and recipes remain managed by ZetAspects.");
        }

        static IEnumerable<CodeInstruction> DurationIL(IEnumerable<CodeInstruction> input)
        {
            string pending = null;
            foreach (var i in input)
            {
                yield return i;
                if (i.operand is MethodInfo m)
                {
                    if (m.Name == "get_HeadHunterBaseDuration" || m.Name == "get_HeadHunterStackDuration")
                        pending = m.Name;
                    else if (pending != null && m.Name == "get_Value" && m.ReturnType == typeof(float))
                    {
                        yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, A.Method(typeof(WakeZetCompatibility), pending == "get_HeadHunterBaseDuration" ? nameof(Base) : nameof(Stack)));
                        pending = null;
                    }
                }
            }
        }

        static float Base(float original) => active() ? first() : original;
        static float Stack(float original) => active() ? extra() : original;
    }
}
