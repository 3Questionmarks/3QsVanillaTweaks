using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Others
{
    internal static class CloneAwareness
    {
        static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = C.BindStartupBool("Other - Allied Clones", "Map-wide Enemy Awareness", true,
                "Allied Goobo clones and ghosts can find enemies anywhere on the map, including behind them and through walls. Attack ranges and terrain obstruction still apply. Restart to apply.");
            if (!enabled.Value) return;

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.CloneAwareness");
            harmony.Patch(A.Method(typeof(BaseAI), "FindEnemyHurtBox", new[] { typeof(float), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(CloneAwareness), nameof(BeforeSearch)));

            // Remove only the vision-distance limit. Native raycasts still check walls,
            // and each skill driver keeps its normal firing range and line-of-sight rules.
            foreach (var method in typeof(BaseAI).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (method.Name == "HasLOS")
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(CloneAwareness), nameof(VisionRangeIL)));
        }

        static bool Applies(BaseAI ai)
        {
            if (!NetworkServer.active || !C.Get(enabled) || !ai) return false;
            var master = ai.GetComponent<CharacterMaster>();
            if (!master || master.teamIndex != TeamIndex.Player || !master.inventory) return false;
            var body = master.GetBody();
            if (!body || body.isPlayerControlled) return false;
            return master.inventory.GetItemCountEffective(DLC1Content.Items.GummyCloneIdentifier) > 0
                || master.inventory.GetItemCountEffective(RoR2Content.Items.Ghost) > 0;
        }

        static void BeforeSearch(BaseAI __instance, ref float __0, ref bool __1, ref bool __2)
        {
            if (!Applies(__instance)) return;
            __0 = float.PositiveInfinity;
            __1 = true;
            __2 = false;
        }

        static float VisionRange(float native, BaseAI ai) => Applies(ai) ? float.PositiveInfinity : native;

        static IEnumerable<CodeInstruction> VisionRangeIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                yield return instruction;
                bool fieldRead = instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field
                    && field.DeclaringType == typeof(CharacterBody) && field.Name == "visionDistance";
                bool propertyRead = (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                    && instruction.operand is MethodInfo method && method.DeclaringType == typeof(CharacterBody)
                    && method.Name == "get_visionDistance";
                if (fieldRead || propertyRead)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(CloneAwareness), nameof(VisionRange)));
                }
            }
        }
    }
}
