using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class AurelioniteFragmentRewards
    {
        private static ConfigEntry<bool> enabled;
        internal static void Init()
        {
            enabled = C.BindStartupBool("Interactables - Aurelionite Fragments", "Remove Storm Item Bias", true,
                "Remove guaranteed Seekers of the Storm choices from Aurelionite Fragments. All choices use their normal reward pool, including eligible expansion items. Applies to Halcyon Shrines and this mod's fixed fragments. Restart to apply.");
            if (!enabled.Value) return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.AurelioniteFragmentRewards");
            harmony.Patch(AccessTools.Method(typeof(HalcyoniteShrineInteractable), "DropRewards"),
                transpiler: new HarmonyMethod(typeof(AurelioniteFragmentRewards), nameof(ReplaceForcedChoices)));
            harmony.Patch(AccessTools.Method(typeof(Tweaks.Stages.AurelioniteFragmentSpawnHelper), "Spawn"),
                transpiler: new HarmonyMethod(typeof(AurelioniteFragmentRewards), nameof(ReplaceForcedChoices)));
        }
        private static IEnumerable<CodeInstruction> ReplaceForcedChoices(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(PickupPickerController), "GenerateOptionsFromDropTablePlusForcedStorm");
            var replacement = AccessTools.Method(typeof(AurelioniteFragmentRewards), nameof(Options));
            int matches = 0;
            var result = new List<CodeInstruction>();
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; matches++; }
                result.Add(instruction);
            }
            if (matches != 1) throw new InvalidOperationException("Expected one forced-Storm fragment reward call; found " + matches + ".");
            return result;
        }
        private static PickupPickerController.Option[] Options(int count, PickupDropTable normal, PickupDropTable storm, Xoroshiro128Plus rng)
        {
            return C.Get(enabled)
                ? PickupPickerController.GenerateOptionsFromDropTable(count, normal, rng)
                : PickupPickerController.GenerateOptionsFromDropTablePlusForcedStorm(count, normal, storm, rng);
        }
    }
}
