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

        internal static BepInEx.Configuration.ConfigEntry<bool> BindEnabled()
        {
            var entry = global::Config.ConfigManager.BindBool("Mod Compatibility - General", "LookingGlass Enabled", true,
                "Show item and skill stats that match this mod's changes in LookingGlass.");
            // BepInEx retains renamed keys as unbound entries unless explicitly removed.
            var property = typeof(BepInEx.Configuration.ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            var orphaned = property?.GetValue(entry.ConfigFile, null) as System.Collections.Generic.Dictionary<BepInEx.Configuration.ConfigDefinition, string>;
            orphaned?.Remove(new BepInEx.Configuration.ConfigDefinition("Mod Compatibility - LookingGlass", "Enabled"));
            return entry;
        }

        internal static void Init()
        {
            var enabled = BindEnabled();
            if (!global::Config.ConfigManager.Get(enabled)) return;
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
