using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using RoR2.UI.LogBook;

namespace Tweaks.ModCompatibility
{
    internal static class LogbookMissingLore
    {
        internal static void Init()
        {
            var check = AccessTools.Method(typeof(LogBookController), "IsEntryPickupItemWithoutLore");
            new Harmony("3Questionmarks.3QsVanillaTweaks.LogbookMissingLore").Patch(check,
                transpiler: new HarmonyMethod(typeof(LogbookMissingLore), nameof(GuardLoreLookup)));
        }

        private static IEnumerable<CodeInstruction> GuardLoreLookup(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo registered = AccessTools.Method(typeof(Language), "TokenIsRegistered", new[] { typeof(string) });
            MethodInfo safeLookup = AccessTools.Method(typeof(LogbookMissingLore), nameof(HasRegisteredLore));
            bool found = false;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(registered))
                {
                    // Check before invoking the language hooks, which use the token as a dictionary key.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = safeLookup;
                    found = true;
                }
                yield return instruction;
            }

            if (!found)
                ThreeQsVanillaTweaks.Log.Warning("Logbook: no language lookup found in the item lore check.");
        }

        private static bool HasRegisteredLore(Language language, string token) =>
            !string.IsNullOrEmpty(token) && language.TokenIsRegistered(token);
    }
}
