using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ThreeQsVanillaTweaks;

namespace Tweaks.ModCompatibility
{
    // Optional LookingGlass integration. No reference to LookingGlass.dll.
    // Any item can register its own stat formatting in LookingGlassStatRegistry.
    internal static class LookingGlass
    {
        private static bool warned;

        internal static void Init()
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "LookingGlass", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                Log.Info("LookingGlass compatibility: not installed; skipping.");
                return;
            }

            Type type = assembly.GetType("LookingGlass.ItemStatsNameSpace.ItemStats", false);
            MethodInfo method = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "GetItemStatsFormatted" && m.ReturnType == typeof(void) &&
                    m.GetParameters().Length == 4 &&
                    m.GetParameters()[2].ParameterType == typeof(string).MakeByRefType());
            if (method == null)
            {
                Log.Warning("LookingGlass compatibility: unsupported GetItemStatsFormatted signature.");
                return;
            }

            try
            {
                MethodInfo postfix = typeof(LookingGlass).GetMethod("AfterFormatting", BindingFlags.Static | BindingFlags.NonPublic);
                new Harmony("3Questionmarks.3QsVanillaTweaks.LookingGlass").Patch(method, postfix: new HarmonyMethod(postfix));
                Log.Info("LookingGlass compatibility: installed generic registered-item stat formatter.");
            }
            catch (Exception e)
            {
                Log.Warning("LookingGlass compatibility: failed to install hook: " + e);
            }
        }

        private static void AfterFormatting(ref string __2)
        {
            try { __2 = LookingGlassStatRegistry.Format(__2); }
            catch (Exception e)
            {
                if (warned) return;
                warned = true;
                Log.Warning("LookingGlass compatibility: stat formatting failed: " + e);
            }
        }
    }
}
