using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
namespace Tweaks.Others
{
    internal static class PlayerLevelCap
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<int> cap;
        static ulong[] originalTable, table;
        static uint originalCap;
        static ulong originalLimit;
        [ThreadStatic] static int teamScope;
        internal static void Init()
        {
            const string s = "Others - Player Levels";
            enabled = C.BindStartupBool(s, "Enabled", true, "Adjust the experience curve and player level cap. Restart to apply.");
            cap = C.Bind(s, "Level Cap", 100, new ConfigDescription("Maximum player team level. The XP curve reaches this level at vanilla's total XP for level 94, retaining 20 XP for the first level-up. Restart to apply. Vanilla: 94.", new AcceptableValueRange<int>(3, 1000)));
            if (!enabled.Value) return;
            originalTable = (ulong[])AccessTools.Field(typeof(TeamManager), "levelToExperienceTable").GetValue(null);
            originalCap = TeamManager.naturalLevelCap;
            originalLimit = TeamManager.hardExpCap;
            int target = cap.Value;
            double lo = 1.000001, hi = 1e20;
            for (int i=0; i<256; i++) {
                double mid=(lo+hi)/2;
                double xp=20*(Math.Pow(mid,target-1)-1)/(mid-1);
                if (xp > originalLimit) hi=mid; else lo=mid;
            }
            double growth=(lo+hi)/2;
            table = new ulong[target+1];
            for (int i=2;i<target;i++) table[i]=(ulong)Math.Min(originalLimit,20*(Math.Pow(growth,i-1)-1)/(growth-1));
            table[target]=originalLimit;
            var h = new Harmony("3Qs.PlayerLevelCap");
            h.Patch(AccessTools.Method(typeof(TeamManager), "FindLevelForExperience"), prefix: new HarmonyMethod(typeof(PlayerLevelCap), nameof(FindLevel)));
            h.Patch(AccessTools.Method(typeof(TeamManager), "GetExperienceForLevel"), prefix: new HarmonyMethod(typeof(PlayerLevelCap), nameof(Experience)));
            h.Patch(AccessTools.Method(typeof(TeamManager), "SetTeamLevel"), prefix: new HarmonyMethod(typeof(PlayerLevelCap), nameof(Before)), finalizer: new HarmonyMethod(typeof(PlayerLevelCap), nameof(Finish)));
            h.Patch(AccessTools.Method(typeof(TeamManager), "SetTeamExperience"), prefix: new HarmonyMethod(typeof(PlayerLevelCap), nameof(Before)), finalizer: new HarmonyMethod(typeof(PlayerLevelCap), nameof(Finish)));
        }
        static void Before(TeamIndex teamIndex, out int __state)
        {
            __state = teamScope;
            teamScope = teamIndex == TeamIndex.Player ? 1 : -1;
        }
        static bool FindLevel(ulong experience, ref uint __result)
        {
            if (teamScope < 0) return true;
            int index = Array.BinarySearch(table, experience);
            __result = (uint)(index >= 0 ? Math.Max(1,index) : Math.Max(1,~index-1));
            return false;
        }
        static bool Experience(uint level, ref ulong __result)
        {
            if (teamScope < 0) return true;
            __result = table[Math.Min(level, (uint)table.Length-1)];
            return false;
        }
        static Exception Finish(Exception __exception, int __state)
        {
            teamScope = __state;
            return __exception;
        }
    }
}
