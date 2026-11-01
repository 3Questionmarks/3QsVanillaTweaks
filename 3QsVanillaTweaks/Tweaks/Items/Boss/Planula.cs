using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class Planula
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Planula", "ParentEgg");
            c.Pair("Healing", 10f, 10f, "Flat healing when damaged. Vanilla: 15 +15 per stack.");
            c.Pair("Healing (%)", 3f, 3f, "Maximum-health healing when damaged. Vanilla: 0%.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance to heal when taking damage. Vanilla: 100%.");
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", 15, () => 0, "parentEgg", 0, 60);
            U.Patch(typeof(HealthComponent), "TakeDamageProcess", typeof(Planula), prefix: nameof(Before), postfix: nameof(After));
            c.Text(() => "Heal for " + c.V("Healing", "", "cIsHealing") + " plus " + c.V("Healing (%)", "%", "cIsHealing") + " of maximum health when taking damage.", () => "Taking damage heals you.");
        }

        static void Before(HealthComponent __instance, out float __state)
        {
            __state = __instance.health + __instance.shield + __instance.barrier;
        }

        static void After(HealthComponent __instance, DamageInfo damageInfo, float __state)
        {
            int n = c.Count(__instance.body);
            if (!NetworkServer.active || n == 0 || damageInfo.rejected || damageInfo.damage <= 0 || !__instance.alive)
                return;
            if (__instance.health + __instance.shield + __instance.barrier >= __state || !U.Roll(c.Total("Trigger Chance (%)", n), __instance.body))
                return;
            __instance.Heal(c.Total("Healing", n) + BossItemSupport.HealingMaximum(__instance) * c.Total("Healing (%)", n) / 100f, damageInfo.procChainMask);
        }
    }
}
