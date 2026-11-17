using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
namespace Tweaks.Enemies
{
    internal static class EnemyLevels
    {
        static ConfigEntry<bool> enabled, noHealing;
        static ConfigEntry<int> cap;
        static readonly List<ILHook> hooks = new List<ILHook>();
        sealed class Snapshot { internal float Level, Health, FullHealth; internal bool Seen; }
        static readonly ConditionalWeakTable<CharacterBody, Snapshot> snapshots = new ConditionalWeakTable<CharacterBody, Snapshot>();
        internal static void Init()
        {
            const string s = "Enemies - Levels";
            enabled = C.BindBool(s, "Enabled", true, "Enable enemy level changes.");
            cap = C.Bind(s, "Level Cap", 9999, new ConfigDescription("Maximum ambient enemy level. Vanilla: 99.", new AcceptableValueRange<int>(1, 9999)));
            noHealing = C.BindBool(s, "No Healing On Level Up", true, "Enemies no longer gain health when their level increases. Other healing is unchanged. Vanilla: false.");
            hooks.Add(new ILHook(AccessTools.Method(typeof(Run), "RecalculateDifficultyCoefficentInternal"), il => {
                var cursor = new ILCursor(il);
                if (!cursor.TryGotoNext(MoveType.After, i => i.MatchLdsfld<Run>("ambientLevelCap"))) throw new InvalidOperationException("Enemy level cap IL not found.");
                cursor.EmitDelegate<Func<int, int>>(original => C.Get(enabled) ? C.Get(cap) : original);
            }));
            var harmony = new Harmony("3Qs.EnemyLevels");
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), prefix: new HarmonyMethod(typeof(EnemyLevels), nameof(Before)), postfix: new HarmonyMethod(typeof(EnemyLevels), nameof(After)));
        }
        static void Before(CharacterBody __instance, out Snapshot __state)
        {
            var tracked = snapshots.GetOrCreateValue(__instance);
            __state = new Snapshot { Seen = tracked.Seen, Level = __instance.level, Health = __instance.healthComponent ? __instance.healthComponent.health : 0f, FullHealth = __instance.maxHealth };
            tracked.Seen = true;
        }
        static void After(CharacterBody __instance, Snapshot __state)
        {
            if (!__state.Seen || !UnityEngine.Networking.NetworkServer.active || !C.Get(enabled) || !C.Get(noHealing) || !__instance.healthComponent || !__instance.teamComponent || __instance.teamComponent.teamIndex == TeamIndex.Player || __state.Level <= 0 || __instance.level <= __state.Level) return;
            // An undamaged enemy stays at full health. Bodies that receive their real level after
            // their first stat calculation (e.g. scripted bosses) would otherwise keep low-level health.
            if (__state.FullHealth > 0f && __state.Health >= __state.FullHealth * 0.999f) return;
            __instance.healthComponent.Networkhealth = Mathf.Min(__instance.healthComponent.health, __state.Health);
        }
    }
}
