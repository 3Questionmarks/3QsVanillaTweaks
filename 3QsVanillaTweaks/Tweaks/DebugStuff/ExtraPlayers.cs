using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class ExtraPlayers
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<int> players;
        private static bool loggedDifficultyCount;

        internal static void Init()
        {
            const string section = "Debug - Extra Players";
            enabled = C.BindBool(section, "Simulate Extra Players", true,
                "Treat the run as though additional players were connected. Affects normal difficulty and player-count scaling.");
            players = C.Bind(section, "Additional Players", 2,
                new ConfigDescription("Extra players counted by the run. Default.",
                    new AcceptableValueRange<int>(0, 15)));
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.ExtraPlayers");
            harmony.Patch(AccessTools.PropertyGetter(typeof(Run), "participatingPlayerCount"),
                postfix: new HarmonyMethod(typeof(ExtraPlayers), nameof(AfterPlayerCount)));
            // A small property getter can be inlined inside the difficulty calculation.
            // Replace its call there before the method is compiled so chest costs and
            // enemy scaling receive the same simulated player count as reward drops.
            harmony.Patch(AccessTools.Method(typeof(Run), "RecalculateDifficultyCoefficentInternal"),
                transpiler: new HarmonyMethod(typeof(ExtraPlayers), nameof(DifficultyPlayerCount)));
        }

        internal static bool Active => enabled != null && C.DebugOptionsEnabled && C.Get(enabled);
        internal static int Count => players != null ? C.Get(players) : 0;
        private static void AfterPlayerCount(ref int __result)
        {
            if (Active) __result += Count;
        }

        private static int SimulatedCount(Run run)
        {
            int real = PlayerCharacterMasterController.instances.Count;
            if (!Active) return real;
            if (!loggedDifficultyCount)
            {
                loggedDifficultyCount = true;
                ThreeQsVanillaTweaks.Log.Info("Extra Players: difficulty scaling uses " + real +
                    " connected + " + Count + " simulated player(s).");
            }
            return real + Count;
        }

        private static IEnumerable<CodeInstruction> DifficultyPlayerCount(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.PropertyGetter(typeof(Run), "participatingPlayerCount");
            MethodInfo simulated = AccessTools.Method(typeof(ExtraPlayers), nameof(SimulatedCount));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(getter))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = simulated;
                }
                yield return instruction;
            }
        }
    }
}
