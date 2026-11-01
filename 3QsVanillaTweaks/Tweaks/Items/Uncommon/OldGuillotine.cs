using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class OldGuillotine
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Old Guillotine", "ExecuteLowHealthElite", true);
            c.B("Rework", true, "Deal bonus damage against low-health enemies instead of executing elites.");
            c.Pair("Damage Bonus (%)", 50, 25, "Extra damage in rework mode. Vanilla: 0%.");
            c.Pair("Damage Threshold (%)", 50, 0, "Target combined health must be below this percentage. Vanilla: not applicable.");
            c.Pair("Elite Execute Threshold (%)", 13, 13, "Elite execute amplification in normal mode, converted to a health fraction. Vanilla: 13% per stack.");
            c.Suppress("RoR2.CharacterBody", "OnInventoryChanged");
            U.Ensure();
            U.BeforeDamage += Damage;
            U.Stats += b =>
            {
                int n = c.Count(b);
                if (c.Active)
                    S.Set(b, "executeEliteHealthFraction", n > 0 && !c.B("Rework") ? Util.ConvertAmplificationPercentageIntoReductionNormalized(c.Total("Elite Execute Threshold (%)", n)) : 0);
            };
            c.StatValue = (key, n, value) => key == "Elite Execute Threshold (%)" ? 100 * Util.ConvertAmplificationPercentageIntoReductionNormalized(value) : value;
            c.ShowStat = key => c.B("Rework") ? !key.StartsWith("Elite Execute") : key.StartsWith("Elite Execute");
            c.Text(() => c.B("Rework") ? "Deal " + c.V("Damage Bonus (%)", "%") + " bonus damage to enemies below " + c.V("Damage Threshold (%)", "%", "cIsHealth") + " health." : "Instantly kill <style=cIsDamage>elite monsters</style> below <style=cIsHealth>" + (100 * Util.ConvertAmplificationPercentageIntoReductionNormalized(c.F("Elite Execute Threshold (%)"))).ToString("0.#") + "% health</style>.", () => c.B("Rework") ? "Deal bonus <style=cIsDamage>damage</style> to low-health enemies." : "Instantly kill low-health <style=cIsDamage>elite monsters</style>.");
        }

        static void Damage(HealthComponent target, DamageInfo info)
        {
            var b = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(b);
            if (n > 0 && c.B("Rework") && target.combinedHealth / target.fullCombinedHealth < c.Total("Damage Threshold (%)", n) / 100)
                info.damage *= 1 + c.Total("Damage Bonus (%)", n) / 100;
        }
    }
}
