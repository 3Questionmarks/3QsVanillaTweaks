using A = ThreeQsVanillaTweaks.ReflectionTools;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class OddlyShapedOpal
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> armour, extra, delay;
        internal static void Init()
        {
            var s = S.Section("Oddly-Shaped Opal");
            enabled = S.Enable(s);
            armour = S.Num(s, "Armour", 100, "Armour while protected. Vanilla: 100.");
            extra = S.Num(s, "Armour Per Stack", 100, "Additional armour per extra stack. Vanilla: 100.");
            delay = S.Num(s, "Recharge Delay", 7, "Seconds without taking damage before protection returns. Vanilla: 7.");
            if (!enabled.Value)
                return;
            S.Ensure();
            S.Stats += b =>
            {
                if (!S.Active(enabled) || !b.HasBuff(DLC1Content.Buffs.OutOfCombatArmorBuff))
                    return;
                int n = S.Count(b, DLC1Content.Items.OutOfCombatArmor);
                S.Set(b, "armor", b.armor - 100 * n + S.Total(armour, extra, n));
            };
            var h = S.Harmony("Opal");
            S.Patch(h, typeof(OutOfCombatArmorBehavior), "FixedUpdate", typeof(OddlyShapedOpal), transpiler: nameof(IL));
            S.Text("ITEM_OUTOFCOMBATARMOR_DESC", () => "After <style=cIsUtility>" + C.Get(delay) + " seconds</style> without taking damage, gain <style=cIsHealing>" + C.Get(armour) + " armour</style> <style=cStack>(+" + C.Get(extra) + " per stack)</style> until taking damage again.");
        }

        static bool Ready(CharacterBody b) => S.Active(enabled) ? (float)A.Field(typeof(CharacterBody), "outOfDangerStopwatch").GetValue(b) >= C.Get(delay) : b.outOfDanger;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            foreach (var i in code)
            {
                if (i.Calls(A.Property(typeof(CharacterBody), "outOfDanger").GetGetMethod()))
                {
                    i.opcode = OpCodes.Call;
                    i.operand = A.Method(typeof(OddlyShapedOpal), nameof(Ready));
                }

                yield return i;
            }
        }
    }
}
