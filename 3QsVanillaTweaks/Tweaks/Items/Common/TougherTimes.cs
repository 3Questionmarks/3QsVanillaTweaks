using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class TougherTimes
    {
        static ConfigEntry<bool> enabled, luck, linear;
        static ConfigEntry<float> chance, extra;
        internal static void Init()
        {
            var s = S.Section("Tougher Times");
            enabled = S.Enable(s);
            luck = C.BindBool(s, "Affected By Luck", false, "Allow luck to affect block rolls. Vanilla: false.");
            linear = C.BindBool(s, "Linear Scaling", false, "Use linear block chance instead of vanilla hyperbolic stacking.");
            chance = S.Num(s, "Block Chance (%)", 15, "First-stack chance amplification before hyperbolic conversion. Vanilla: 15%.");
            extra = S.Num(s, "Block Chance Per Stack (%)", 15, "Extra chance amplification per additional stack. Vanilla: 15%.");
            if (!enabled.Value)
                return;
            S.Patch(S.Harmony("TougherTimes"), typeof(HealthComponent), "TakeDamageProcess", typeof(TougherTimes), transpiler: nameof(IL));
            S.Text("ITEM_BEAR_DESC", () => "<style=cIsUtility>" + C.Get(chance) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> chance to <style=cIsUtility>block</style> damage. " + (C.Get(luck) ? " Affected by luck." : " Unaffected by luck."));
        }

        static float Chance(float original, HealthComponent hc)
        {
            if (!S.Active(enabled))
                return original;
            float p = S.Total(chance, extra, S.Count(hc.body, RoR2Content.Items.Bear));
            return C.Get(linear) ? p : Util.ConvertAmplificationPercentageIntoReductionPercentage(p);
        }

        static bool Roll(float p, float nativeLuck, CharacterMaster master) => Util.CheckRoll(p, S.Active(enabled) && C.Get(luck) && master ? master.luck : nativeLuck, master);
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int at = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "bear" && i.opcode == OpCodes.Ldfld);
            if (at < 0)
                throw new InvalidOperationException("Tougher Times anchor missing");
            int converted = 0, rolls = 0;
            for (int i = at; i < Math.Min(at + 55, list.Count); i++)
            {
                if (list[i].operand is MethodInfo m && m.Name == "ConvertAmplificationPercentageIntoReductionPercentage")
                {
                    list.InsertRange(i + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, A.Method(typeof(TougherTimes), nameof(Chance))) });
                    i += 2;
                    converted++;
                }
                else if (list[i].operand is MethodInfo r && r.Name == "CheckRoll" && r.GetParameters().Length == 3)
                {
                    list[i].opcode = OpCodes.Call;
                    list[i].operand = A.Method(typeof(TougherTimes), nameof(Roll));
                    rolls++;
                    break;
                }
            }

            if (converted != 1 || rolls != 1)
                throw new InvalidOperationException("Tougher Times chance anchors changed");
            return list;
        }
    }
}
