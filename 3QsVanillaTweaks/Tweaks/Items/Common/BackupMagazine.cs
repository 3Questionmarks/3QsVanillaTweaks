using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class BackupMagazine
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<int> stocks, extra;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Backup Magazine");
            stocks = settings.Integer("Secondary Stocks", 1, "Bonus secondary stocks from one magazine. Vanilla: 1.");
            extra = settings.Integer("Secondary Stocks Per Stack", 1, "Additional secondary stocks per stack. Vanilla: 1.");
            if (settings.Enabled.Value)
                S.Patch(S.Harmony("BackupMagazine"), typeof(CharacterBody), "RecalculateStats", typeof(BackupMagazine), transpiler: nameof(IL));
            settings.Describe("ITEM_SECONDARYSKILLMAGAZINE_DESC", () => "Add <style=cIsUtility>" + C.Get(stocks) + "</style> <style=cStack>(+" + C.Get(extra) + " per stack)</style> <style=cIsUtility>secondary stocks</style>.");
        }

        static int Count(int n) => settings.Active ? (int)S.Linear(n, C.Get(stocks), C.Get(extra)) : n;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> source)
        {
            var code = new List<CodeInstruction>(source);
            int at = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "SecondarySkillMagazine");
            if (at < 0)
                throw new InvalidOperationException("Backup Magazine count missing");
            for (int i = at; i < Math.Min(at + 8, code.Count); i++)
                if (code[i].operand is MethodInfo m && m.Name == "GetItemCountEffective")
                {
                    code.Insert(i + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(BackupMagazine), nameof(Count))));
                    return code;
                }

            throw new InvalidOperationException("Backup Magazine count read missing");
        }
    }
}
