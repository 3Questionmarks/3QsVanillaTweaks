using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using C = Config.ConfigManager;

namespace Tweaks.ModCompatibility
{
    // Optional skill stats integration for both HUD and loadout tooltips.
    internal static class LookingGlassSkillStats
    {
        private sealed class Entry
        {
            internal Func<string> Extra;
            internal Func<float> Proc;
            internal string StateName;
        }
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        private static readonly Dictionary<string, string> aliases = new Dictionary<string, string>();

        internal static void Register(SkillDef skill, Func<string> extra, Func<float> proc = null, string vanillaToken = null)
        {
            if (!skill || string.IsNullOrEmpty(skill.skillNameToken)) return;
            var entry = new Entry { Extra = extra, Proc = proc, StateName = skill.activationState.stateType?.FullName };
            entries[skill.skillNameToken] = entry;
            if (!string.IsNullOrEmpty(vanillaToken))
            {
                entries[vanillaToken] = entry;
                if (skill.skillNameToken != vanillaToken) aliases[skill.skillNameToken] = vanillaToken;
            }
        }

        internal static void Init()
        {
            var enabled = LookingGlass.BindEnabled();
            if (C.Get(enabled)) RoR2Application.onLoad += Install;
        }

        private static void Install()
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, "LookingGlass", StringComparison.OrdinalIgnoreCase));
            if (assembly == null) return;
            Type type = assembly.GetType("LookingGlass.ItemStatsNameSpace.ProcCoefficientData", false);
            string[] names = { "hasExtra", "GetExtraInfo", "hasProcCoefficient", "GetProcCoefficient" };
            Type[] returns = { typeof(bool), typeof(string), typeof(bool), typeof(float) };
            MethodInfo[] methods = names.Select(n => type == null ? null : AccessTools.Method(type, n, new[] { typeof(string) })).ToArray();
            if (methods.Where((m, i) => m == null || !m.IsStatic || m.ReturnType != returns[i]).Any())
            {
                ThreeQsVanillaTweaks.Log.Warning("LookingGlass skill stats: unsupported skill stat methods.");
                return;
            }
            try
            {
                var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.LookingGlassSkillStats");
                string[] patches = { nameof(AfterHasExtra), nameof(AfterExtra), nameof(AfterHasProc), nameof(AfterProc) };
                for (int i = 0; i < methods.Length; i++)
                    harmony.Patch(methods[i], prefix: new HarmonyMethod(typeof(LookingGlassSkillStats), nameof(BeforeLookup)), postfix: new HarmonyMethod(typeof(LookingGlassSkillStats), patches[i]));
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("LookingGlass skill stats could not be loaded: " + error); }
        }

        private static void BeforeLookup(ref string __0)
        {
            if (aliases.TryGetValue(__0 ?? "", out string token)) __0 = token;
        }

        private static void AfterHasExtra(string __0, ref bool __result)
        {
            if (entries.TryGetValue(__0 ?? "", out Entry entry) && !string.IsNullOrEmpty(entry.Extra?.Invoke())) __result = true;
        }
        private static void AfterExtra(string __0, ref string __result)
        {
            if (!entries.TryGetValue(__0 ?? "", out Entry entry)) return;

            if (entry.StateName == "EntityStates.FalseSon.ClubSwing")
                __result = System.Text.RegularExpressions.Regex.Replace(
                    __result ?? "", @"\r?\nMid-Air Charged Proc:[^\r\n]*", "");

            if (__0 == "HUNTRESS_SPECIAL_NAME" ||
                __0 == "MAGE_SPECIAL_NAME" ||
                entry.StateName == "EntityStates.FalseSon.LaserFather")
                __result = System.Text.RegularExpressions.Regex.Replace(
                    __result ?? "", @"\r?\nTicks:[^\r\n]*", "");

            string extra = entry.Extra?.Invoke();
            if (!string.IsNullOrEmpty(extra)) __result = (__result ?? "") + extra;
        }
        private static void AfterHasProc(string __0, ref bool __result)
        {
            if (entries.TryGetValue(__0 ?? "", out Entry entry) && entry.Proc != null) __result = true;
        }
        private static void AfterProc(string __0, ref float __result)
        {
            if (entries.TryGetValue(__0 ?? "", out Entry entry) && entry.Proc != null) __result = entry.Proc();
        }
    }
}
