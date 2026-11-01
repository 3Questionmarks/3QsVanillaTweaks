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
    internal static class LightFluxPauldron
    {
        static LunarConfig c;
        internal static void Init() => LunarConfigSupport.Register("HalfAttackSpeedHalfCooldowns", Configure);
        static void Configure(LunarConfig config)
        {
            c = config;
            c.Pair("Attack Speed Penalty", 1, 1, "Attack speed is divided by one plus this penalty. Vanilla: 1 +1 per stack, halving attack speed with one item.");
            c.Number("Cooldown Reduction Per Stack (%)", 50, "Remaining skill cooldown is multiplied by this reduction for each stack.");
            c.ColouredText(@"(?:Reduce|Decreases?|Halve|Halves)\s+(?:your\s+)?attack speed(?:\s+by\s+[\d.,]+%)?", () => "Decrease skill cooldowns by <style=cIsUtility>" + c.Values.F("Cooldown Reduction Per Stack (%)").ToString("0.##") + "%</style> <style=cStack>(+" + c.Values.F("Cooldown Reduction Per Stack (%)").ToString("0.##") + "% per stack)</style>. Reduce attack speed by " + (100f - 100f / (1 + c.Values.F("Attack Speed Penalty"))).ToString("0.##") + "% <style=cStack>(+" + (100f - 100f / (1 + c.Values.F("Attack Speed Penalty Per Stack"))).ToString("0.##") + "% per stack)</style>.");
            if (!c.Values.Enabled.Value) return;
            U.Stats += body =>
            {
                int n = c.Values.Count(body); if (n <= 0) return;
                body.attackSpeed *= (1f + n) / (1 + c.Values.Total("Attack Speed Penalty", n));
                float ratio = Mathf.Pow(Mathf.Clamp01(1 - c.Values.F("Cooldown Reduction Per Stack (%)") / 100f) / .5f, n);
                foreach (var skill in body.GetComponents<GenericSkill>()) skill.cooldownScale *= ratio;
            };
        }
    }
}
