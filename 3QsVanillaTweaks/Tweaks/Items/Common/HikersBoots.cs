using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class HikersBoots
    {
        static ConfigEntry<int> cap, capStack, gain, gainStack;
        static VanillaCommonConfig settings;
        static ConfigEntry<float> crit, damage, duration;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Hikers Boots");
            cap = settings.Integer("Maximum Buffs", 10, "Maximum buffs at one item stack. Vanilla: 10.", 1);
            capStack = settings.Integer("Maximum Buffs Per Stack", 10, "Additional maximum buffs per extra item stack. Vanilla: 10.");
            gain = settings.Integer("Buffs Per Hit", 1, "Buffs gained per qualifying hit at one item stack. Vanilla: 1.");
            gainStack = settings.Integer("Buffs Per Hit Per Stack", 1, "Additional buffs gained per qualifying hit per extra item. Vanilla: 1.");
            crit = settings.Number("Critical Chance Per Buff (%)", 1, "Critical chance per active buff. Each qualifying hit adds one buff per item stack. Vanilla: 1%.");
            damage = settings.Number("Critical Damage Per Buff (%)", 1, "Additional critical-damage multiplier per active buff. Vanilla: 1%.");
            duration = settings.Number("Buff Duration", 10, "Buff duration and refresh duration in seconds. Vanilla: 10.");
            settings.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 10, () => C.Get(duration), "CritAtLowerElevation", window: 25);
            settings.Literal("RoR2.GlobalEventManager", "ProcessHitEnemy", 10, () => C.Get(duration), "CritAtLowerElevation", occurrence: 1, window: 250);
            if (!settings.Enabled.Value)
                return;
            S.Patch(S.Harmony("HikersBoots"), typeof(GlobalEventManager), "ProcessHitEnemy", typeof(HikersBoots), transpiler: nameof(IL));
            S.Ensure();
            S.Stats += b =>
            {
                if (!settings.Active)
                    return;
                int n = b.GetBuffCount(DLC3Content.Buffs.CritChanceAndDamage);
                if (n > 0)
                {
                    S.Set(b, "crit", b.crit + n * (C.Get(crit) - 1));
                    S.Set(b, "critMultiplier", b.critMultiplier + n * (C.Get(damage) - 1) / 100);
                }
            };
            settings.Describe("ITEM_CRITATLOWERELEVATION_DESC", () => "Hit enemies from above to gain <style=cIsUtility>" + C.Get(gain) + "</style> <style=cStack>(+" + C.Get(gainStack) + " per stack)</style> buffs, up to <style=cIsUtility>" + C.Get(cap) + "</style> <style=cStack>(+" + C.Get(capStack) + " per stack)</style>. Each buff grants <style=cIsDamage>" + C.Get(crit) + "% critical chance</style> and <style=cIsDamage>" + C.Get(damage) + "% critical damage</style> for <style=cIsUtility>" + C.Get(duration) + " seconds</style>.");
        }

        static int Cap(int native) => settings.Active ? (int)S.Linear(native / 10, C.Get(cap), C.Get(capStack)) : native;
        static int Gain(int native) => settings.Active ? (int)S.Linear(native, C.Get(gain), C.Get(gainStack)) : native;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            int start = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "CritAtLowerElevation");
            if (start < 0)
                throw new InvalidOperationException("Hiker count missing");
            int value = code.FindIndex(start, Math.Min(80, code.Count - start), i => i.LoadsConstant(10));
            if (value < 0)
                throw new InvalidOperationException("Hiker buff cap missing");
            int mul = code.FindIndex(value, Math.Min(5, code.Count - value), i => i.opcode == OpCodes.Mul);
            if (mul < 0)
                throw new InvalidOperationException("Hiker buff cap multiplication missing");
            code.Insert(mul + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(HikersBoots), nameof(Cap))));
            int add = code.FindIndex(mul, Math.Min(220, code.Count - mul), i => i.operand is MethodInfo m && m.Name == "AddTimedBuff");
            if (add < 0)
                throw new InvalidOperationException("Hiker buff gain missing");
            int branch = code.FindIndex(add, Math.Min(25, code.Count - add), i => i.opcode == OpCodes.Blt || i.opcode == OpCodes.Blt_S);
            if (branch < 0)
                throw new InvalidOperationException("Hiker buff gain loop missing");
            code.Insert(branch, new CodeInstruction(OpCodes.Call, A.Method(typeof(HikersBoots), nameof(Gain))));
            return code;
        }
    }
}
