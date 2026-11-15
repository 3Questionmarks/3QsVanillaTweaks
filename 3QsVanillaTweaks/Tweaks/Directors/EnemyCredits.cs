using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
namespace Tweaks.Directors
{
    internal static class EnemyCredits
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> multiplier;
        internal static void Init()
        {
            const string s = "Directors - Enemy Credits";
            enabled=C.BindBool(s,"Enabled",true,"Enable regular enemy director credit scaling.");
            multiplier=C.Bind(s,"Credit Multiplier",1.2f,new ConfigDescription("Multiplies recurring combat director income. Does not multiply shrine or scripted encounter budgets. Vanilla: 1.",new AcceptableValueRange<float>(0f,100f)));
            var type=typeof(CombatDirector).GetNestedType("DirectorMoneyWave",System.Reflection.BindingFlags.NonPublic);
            new Harmony("3Qs.EnemyCredits").Patch(AccessTools.Method(type,"Update"),postfix:new HarmonyMethod(typeof(EnemyCredits),nameof(After)));
        }
        static void After(ref float __result) { if(C.Get(enabled)) __result *= C.Get(multiplier); }
    }
}
