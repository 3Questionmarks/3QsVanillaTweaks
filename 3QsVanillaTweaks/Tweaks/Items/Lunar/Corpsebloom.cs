using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using S = Tweaks.Items.Common.CommonItemSupport;
namespace Tweaks.Items.Lunar
{
    internal static class Corpsebloom
    {
        static LunarConfig c;
        const string Repeat = "RoR2.HealthComponent+RepeatHealComponent";
        internal static void Init() => LunarConfigSupport.Register("RepeatHeal", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Pair("Healing Increase (%)", 100, 100, "Additional healing stored for later restoration.");
            c.Number("Maximum Healing Per Second (%)", 10, "Maximum health restored per second with one item.");
            c.Number("Healing Rate Divisor Per Extra Stack", 1, "Each extra stack adds this much to the healing-rate divisor. Vanilla: maximum rate divided by item count.");
            c.Number("Healing Interval", .2f, "Seconds between healing ticks.");
            Tweaks.ModCompatibility.LookingGlass.RegisterFullDescription(c.Values.Item.itemIndex);
            c.ColouredText(@"Can heal for a maximum of\s+[\d.,]+%", () =>
            {
                bool active = c.Values.Active;
                float increase = active ? c.Values.F("Healing Increase (%)") : 100f;
                float perStack = active ? c.Values.F("Healing Increase (%) Per Stack") : 100f;
                float maximum = active ? c.Values.F("Maximum Healing Per Second (%)") : 10f;
                float divisor = active ? c.Values.F("Healing Rate Divisor Per Extra Stack") : 1f;
                string healing = "<style=cIsHealing>" + increase.ToString("0.##") + "%</style>" +
                    (perStack == 0f ? "" : " <style=cStack>(+" + perStack.ToString("0.##") + "% per stack)</style>");
                return "Heal " + healing + " more. <style=cIsHealth>All healing is applied over time</style>. Can heal for a maximum of " +
                    maximum.ToString("0.##") + "%" +
                    (divisor == 0f ? "" : " <style=cStack>(-" +
                    (100f - 100f / (1f + divisor)).ToString("0.##") + "% per stack)</style>") +
                    " of your health per second.";
            }, showWhenDisabled: true);
            if (!c.Values.Enabled.Value) return;
            U.Harmony.Patch(A.Method(typeof(HealthComponent), "Heal", new[] { typeof(float), typeof(ProcChainMask), typeof(bool) }), transpiler: new HarmonyMethod(typeof(Corpsebloom), nameof(Healing)));
            var type = typeof(CharacterBody).Assembly.GetType(Repeat);
            U.Harmony.Patch(A.Method(type, "RecalculateStats"), postfix: new HarmonyMethod(typeof(Corpsebloom), nameof(Rate)));
            c.Values.Literal(Repeat, "FixedUpdate", .2f, () => Mathf.Max(.001f, c.Values.F("Healing Interval")), occurrence: 0);
            c.Values.Literal(Repeat, "FixedUpdate", .2f, () => Mathf.Max(.001f, c.Values.F("Healing Interval")), occurrence: 1);
        }
        static float Multiplier(float native)
        {
            int count = Mathf.RoundToInt(native - 1);
            return c.Values.Active && count > 0 ? 1 + c.Values.Total("Healing Increase (%)", count) / 100f : native;
        }
        static IEnumerable<CodeInstruction> Healing(IEnumerable<CodeInstruction> input)
        {
            bool found = false;
            foreach (var ins in input)
            {
                yield return ins;
                if (ins.operand is FieldInfo field && field.Name == "repeatHeal") found = true;
                else if (found && ins.opcode == OpCodes.Conv_R4)
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Corpsebloom), nameof(Multiplier))); found = false; }
            }
        }
        static void Rate(Component __instance)
        {
            if (!c.Values.Active) return;
            var body = __instance.GetComponent<CharacterBody>();
            int count = c.Values.Count(body);
            if (count <= 0) return;
            float rate = c.Values.F("Maximum Healing Per Second (%)") / 100f / (1 + c.Values.F("Healing Rate Divisor Per Extra Stack") * (count - 1));
            A.Field(__instance.GetType(), "healthFractionToRestorePerSecond").SetValue(__instance, rate);
        }
    }
}
