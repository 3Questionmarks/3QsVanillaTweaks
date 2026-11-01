using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class Aegis
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Aegis", "BarrierOnOverHeal");
            c.Pair("Extra Maximum Barrier (%)", 25, 25, "Additional maximum barrier as a percentage of maximum combined health. Vanilla: 0.");
            c.Pair("Overheal Converted To Barrier (%)", 50, 50, "Excess non-regeneration healing converted into barrier. Vanilla: 50% per stack.");
            c.NativeCount("RoR2.HealthComponent", "Heal", .5f, "Overheal Converted To Barrier (%)", 100, "barrierOnOverHeal");
            if (c.Enabled.Value)
                U.Harmony.Patch(AccessTools.PropertySetter(typeof(CharacterBody), "maxBarrier"), prefix: new HarmonyMethod(typeof(Aegis), nameof(Capacity)));
            c.Text(
             () => "Healing past full health grants "
                 + c.V("Overheal Converted To Barrier (%)", "%", "cIsHealing")
                 + " of the excess as barrier. Increase your maximum barrier by "
                 + c.V("Extra Maximum Barrier (%)", "%", "cIsHealing") + ".",
             () => "Excess healing grants barrier. Increases maximum barrier.");
        }

        static void Capacity(CharacterBody __instance, ref float value)
        {
            int n = c.Count(__instance);
            if (n > 0)
                value += (__instance.maxHealth + __instance.maxShield) * c.Total("Extra Maximum Barrier (%)", n) / 100f;
        }
    }
}
