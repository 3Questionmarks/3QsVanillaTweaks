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
    internal static class KineticDampener
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Kinetic Dampener", "ShieldBooster", true);
            c.Pair("Shield (%)", 4, 0, "Maximum health converted into additional shield. Vanilla: 4%, no stacking.");
            c.Pair("Armor", 30, 30, "Armor while shield remains. Vanilla: 30 per stack.");
            c.Pair("Explosion Damage (%)", 100, 0, "Additional base damage when shields break. Vanilla: 100% base damage.");
            c.Pair("Shield Explosion Damage (%)", 100, 10, "Damage as a percentage of maximum shield. Vanilla: 100 +10% per additional stack.");
            c.Pair("Proc Coefficient", 1, 0, "Shield-break blast proc coefficient. Vanilla: 1.");
            c.Pair("Explosion Radius", 12.5f, 0, "Shield-break blast radius. Vanilla: 12.5 metres.");
            // Preserve the native shield grant and adjust only the configured delta.
            // The minimum also covers cases where another stat hook suppresses that grant.
            U.Ensure();
            U.Stats += b =>
            {
                int n = b.inventory && c.Item ? b.inventory.GetItemCountEffective(c.Item) : 0;
                if (n == 0)
                    return;
                float configuredShield = b.maxHealth * (c.Active ? c.Total("Shield (%)", n) : 4f) / 100f;
                float nativeShield = b.maxHealth * 0.04f;
                float newShield = Mathf.Max(configuredShield, b.maxShield + configuredShield - nativeShield);
                float extraShield = newShield - b.maxShield;
                if (extraShield != 0f)
                {
                    U.Add(b, "maxShield", extraShield);
                    U.Add(b, "maxBarrier", extraShield);
                    U.Add(b, "maxHull", extraShield);
                }
                if (c.Active && b.healthComponent.shield > 0)
                    U.Add(b, "armor", c.Total("Armor", n) - 30f * n);
            };
            if (c.Enabled.Value)
                U.Patch(typeof(HealthComponent), "GetShieldBoosterDamage", typeof(KineticDampener), postfix: nameof(Damage));
            KineticSupport.Register(c);
            c.Text(() => "Gain " + c.V("Shield (%)", "%", "cIsHealing") + " maximum health as shield and " + c.V("Armor", "", "cIsHealing") + " armor while shield remains. Breaking your shield releases a <style=cIsDamage>blast</style>.", () => "Gain <style=cIsHealing>shield and armor</style>. Broken shields release a blast.");
        }

        static void Damage(HealthComponent __instance, int stack, ref float __result)
        {
            if (c.Active)
                __result = __instance.fullShield * c.Total("Shield Explosion Damage (%)", stack) / 100 + __instance.body.damage * c.Total("Explosion Damage (%)", stack) / 100;
        }
    }
}
