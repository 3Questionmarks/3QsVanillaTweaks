using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class RollOfPennies
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> gold, extra;
        static ConfigEntry<bool> difficulty;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Roll of Pennies");
            gold = settings.Number("Gold Per Hit", 3, "Gold before difficulty scaling from one stack. Vanilla: 3.");
            extra = settings.Number("Gold Per Hit Per Stack", 3, "Additional gold before difficulty scaling per extra stack. Vanilla: 3.");
            difficulty = C.BindBool(settings.Section, "Scale With Difficulty", true, "Scale gold with run difficulty. Vanilla: true.");
            if (settings.Enabled.Value)
                S.Patch(S.Harmony("RollOfPennies"), typeof(HealthComponent), "TakeDamageProcess", typeof(RollOfPennies), transpiler: nameof(IL));
            settings.Describe("ITEM_GOLDONHURT_DESC", () => "Taking damage from an enemy grants <style=cIsUtility>" + C.Get(gold) + "</style> <style=cStack>(+" + C.Get(extra) + " per stack)</style> <style=cIsUtility>gold</style>" + (C.Get(difficulty) ? ", scaling with difficulty." : "."));
        }

        static float Amount(float native, HealthComponent target)
        {
            if (!settings.Active)
                return native;
            int n = S.Count(target.body, DLC1Content.Items.GoldOnHurt);
            float value = S.Linear(n, C.Get(gold), C.Get(extra));
            return C.Get(difficulty) ? value : value / Mathf.Max(.0001f, Run.instance.difficultyCoefficient);
        }

        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is FieldInfo f && f.Name == "goldOnHurt")
                    for (int j = i + 1; j < Math.Min(i + 8, code.Count); j++)
                        if (code[j].opcode == OpCodes.Conv_R4)
                        {
                            code.Insert(j + 1, new CodeInstruction(OpCodes.Ldarg_0));
                            code.Insert(j + 2, new CodeInstruction(OpCodes.Call, A.Method(typeof(RollOfPennies), nameof(Amount))));
                            return code;
                        }

            throw new InvalidOperationException("Roll of Pennies amount missing");
        }
    }
}
