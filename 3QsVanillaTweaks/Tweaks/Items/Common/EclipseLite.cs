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
    internal static class EclipseLite
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> barrier, extra, chance, chanceStack;
        internal static void Init()
        {
            var s = S.Section("Eclipse Lite");
            enabled = S.Enable(s);
            barrier = S.Num(s, "Base Barrier (%)", 0.5f, "Maximum-health barrier per second of the recharged skill's base cooldown. Vanilla: 1%.");
            extra = S.Num(s, "Barrier Per Stack (%)", 0.5f, "Additional barrier per extra stack per cooldown second. Vanilla: 0.25%.");
            chance = S.Num(s, "Trigger Chance (%)", 100, "Chance each time a skill restocks. Vanilla: 100%.", 0, 100);
            chanceStack = S.Num(s, "Trigger Chance Per Stack (%)", 0, "Extra chance per additional stack.", 0, 100);
            if (!enabled.Value)
                return;
            var h = S.Harmony("EclipseLite");
            S.Patch(h, typeof(CharacterBody), "OnSkillCooldown", typeof(EclipseLite), transpiler: nameof(IL));
            S.Text("ITEM_BARRIERONCOOLDOWN_DESC", () => "When a skill recharges, gain <style=cIsHealing>barrier</style> equal to <style=cIsHealing>" + C.Get(barrier) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> maximum health per second of its base cooldown.");
        }

        static float Base(float native) => S.Active(enabled) ? C.Get(barrier) / 100 : native;
        static float Extra(float native) => S.Active(enabled) ? C.Get(extra) / 100 : native;
        static int Roll(int n, CharacterBody b) => !S.Active(enabled) || Util.CheckRoll(S.Total(chance, chanceStack, n), 0, b.master) ? n : 0;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            bool pending = false;
            foreach (var i in code)
            {
                yield return i;
                if (i.operand is System.Reflection.FieldInfo f && f.Name == "BarrierOnCooldown")
                    pending = true;
                if (pending && i.operand is System.Reflection.MethodInfo m && m.Name == "GetItemCountEffective")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(EclipseLite), nameof(Roll)));
                    pending = false;
                }

                if (i.LoadsConstant(.01f))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(EclipseLite), nameof(Base)));
                if (i.LoadsConstant(.0025f))
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(EclipseLite), nameof(Extra)));
            }
        }
    }
}
