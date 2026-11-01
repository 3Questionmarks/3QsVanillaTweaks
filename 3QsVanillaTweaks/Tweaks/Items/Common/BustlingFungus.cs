using A = ThreeQsVanillaTweaks.ReflectionTools;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Items;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class BustlingFungus
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> heal, extra, radius, radiusStack, interval, delay, flat, flatStack;
        internal static void Init()
        {
            var s = S.Section("Bustling Fungus");
            enabled = S.Enable(s);
            heal = S.Num(s, "Base Heal (%)", 5, "Maximum-health healing per second. Vanilla: 4.5%.");
            extra = S.Num(s, "Heal Per Stack (%)", 2.5f, "Additional maximum-health healing per second per extra stack. Vanilla: 2.25%.");
            radius = S.Num(s, "Base Radius", 5, "Healing radius in metres, measured from the centre.");
            radiusStack = S.Num(s, "Radius Per Stack", 2.5f, "Extra metres per additional stack. Vanilla: 1.5.");
            interval = S.Num(s, "Healing Interval", .25f, "Seconds between healing ticks. Healing per tick scales with this interval. Vanilla: 0.25.", .02f);
            delay = S.Num(s, "Stationary Delay", 1, "Seconds stationary before activating. Vanilla: 1.");
            flat = S.Num(s, "Flat Healing Per Second", 0, "Flat healing per second, in addition to percentage healing.");
            flatStack = S.Num(s, "Flat Healing Per Second Per Stack", 0, "Extra flat healing per additional stack.");
            if (!enabled.Value)
                return;
            var h = S.Harmony("Fungus");
            S.Patch(h, typeof(MushroomBodyBehavior), "FixedUpdate", typeof(BustlingFungus), postfix: nameof(After), transpiler: nameof(IL));
            S.Text("ITEM_MUSHROOM_DESC", () => "After standing still for <style=cIsUtility>" + C.Get(delay) + " seconds</style>, <style=cIsHealing>heal</style> nearby allies for <style=cIsHealing>" + C.Get(heal) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> maximum health per second within <style=cIsUtility>" + C.Get(radius) + "m</style> <style=cStack>(+" + C.Get(radiusStack) + "m per stack)</style>.");
        }

        static bool Still(CharacterBody b) => S.Active(enabled) ? (float)A.Field(typeof(CharacterBody), "notMovingStopwatch").GetValue(b) >= C.Get(delay) : b.GetNotMoving();
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            foreach (var i in code)
            {
                if (i.Calls(A.Method(typeof(CharacterBody), "GetNotMoving")))
                {
                    i.opcode = OpCodes.Call;
                    i.operand = A.Method(typeof(BustlingFungus), nameof(Still));
                }

                yield return i;
            }
        }

        static void After(MushroomBodyBehavior __instance)
        {
            if (!S.Active(enabled))
                return;
            var ward = A.Field(typeof(MushroomBodyBehavior), "mushroomHealingWard").GetValue(__instance) as HealingWard;
            if (!ward)
                return;
            int n = __instance.stack;
            ward.interval = C.Get(interval);
            ward.healFraction = S.Total(heal, extra, n) / 100 * ward.interval;
            ward.healPoints = S.Total(flat, flatStack, n) * ward.interval;
            ward.Networkradius = S.Total(radius, radiusStack, n);
        }
    }
}
