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
    internal static class FuelCell
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Fuel Cell", "EquipmentMagazine", false);
            c.Pair("Equipment Charges", 1, 1, "Additional equipment charges. Vanilla: 1 per stack.");
            c.Pair("Cooldown Reduction (%)", 15, 15, "Multiplicative reduction for the first and each additional item. Vanilla: 15% per stack.");
            if (c.Enabled.Value)
            {
                S.Patch(U.Harmony, typeof(Inventory), "GetEquipmentSlotMaxCharges", typeof(FuelCell), postfix: nameof(Charges), args: Type.EmptyTypes);
                U.Patch(typeof(Inventory), "CalculateEquipmentCooldownScale", typeof(FuelCell), postfix: nameof(Cooldown));
            }

            c.StatValue = (key, n, value) => key == "Cooldown Reduction (%)" ? 100 * (1 - (1 - Mathf.Clamp01(c.F("Cooldown Reduction (%)") / 100)) * Mathf.Pow(1 - Mathf.Clamp01(c.F("Cooldown Reduction (%) Per Stack") / 100), n - 1)) : value;
            c.Text(() => "Gain " + c.V("Equipment Charges", "", "cIsUtility") + " additional <style=cIsUtility>equipment charges</style>. Reduce equipment cooldown by " + c.V("Cooldown Reduction (%)", "%", "cIsUtility") + ".", () => "Gain an additional <style=cIsUtility>equipment charge</style> and reduce equipment cooldown.");
        }

        static void Charges(Inventory __instance, ref int __result)
        {
            if (!c.Active)
                return;
            int n = __instance.GetItemCountEffective(c.Item);
            if (n > 0)
                __result = Mathf.Clamp(__result - n + Mathf.RoundToInt(c.Total("Equipment Charges", n)), 1, 255);
        }

        static void Cooldown(Inventory __instance, ref float __result)
        {
            if (!c.Active)
                return;
            int n = __instance.GetItemCountEffective(c.Item);
            if (n > 0)
                __result *= Mathf.Pow(1 - Mathf.Clamp01(c.F("Cooldown Reduction (%)") / 100), 1) * Mathf.Pow(1 - Mathf.Clamp01(c.F("Cooldown Reduction (%) Per Stack") / 100), n - 1) / Mathf.Pow(.85f, n);
        }
    }
}
