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
    internal static class BreachingFin
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Breaching Fin", "KnockBackHitEnemies", false);
            c.Pair("Cooldown", 15, 0, "Seconds to recharge. Vanilla: 15, no effective stacking.");
            c.Pair("Maximum Juggles", 2, 1, "Maximum consecutive launches. Vanilla: 2 +1 per stack.");
            c.Pair("Bonus Damage Per Juggle (%)", 10, 0, "Additional damage against juggled enemies. Vanilla: 10% per juggle.");
            c.N("Stun Duration", 3.2f, "Launch stun duration. Vanilla: 3.2 seconds.");
            c.N("Launch Force Multiplier", 14, "Victim mass multiplier for launch impulse. Vanilla: 14.");
            c.N("Boss Force Multiplier", .45f, "Launch-force multiplier against bosses. Vanilla: 0.45.");
            c.N("Champion Force Multiplier", .65f, "Launch-force multiplier against champions. Vanilla: 0.65.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(KnockbackFinUtil), "GetCooldown", typeof(BreachingFin), postfix: nameof(Cooldown));
            }

            c.Field("RoR2.KnockbackFinUtil", "CalculateImpulseForce", "victimMassMultiplier", () => c.F("Launch Force Multiplier"));
            c.Field("RoR2.KnockbackFinUtil", "CalculateImpulseForce", "bossImpulseModifier", () => c.F("Boss Force Multiplier"));
            c.Field("RoR2.KnockbackFinUtil", "CalculateImpulseForce", "championImpulseModifier", () => c.F("Champion Force Multiplier"));
            c.Field("RoR2.KnockbackFinUtil", "ModifyDamageInfo", "damageAddPerJuggle", () => c.Total("Bonus Damage Per Juggle (%)", Mathf.Max(1, c.Count(UncommonNative.Source))) / 100);
            if (c.Enabled.Value)
                foreach (var method in typeof(HealthComponent).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic))
                    if (method.Name.Contains("g__DoJuggle"))
                        c.Field("RoR2.HealthComponent", method.Name, "StunDuration", () => c.F("Stun Duration"));
            BreachingFinSupport.Register(c);
            c.Text(() => "Hits <style=cIsUtility>launch enemies</style>, allowing up to " + c.V("Maximum Juggles", "", "cIsUtility") + " juggles. Each juggle increases damage taken by " + c.V("Bonus Damage Per Juggle (%)", "%") + ". Recharges in " + c.V("Cooldown", "s", "cIsUtility") + ".", () => "Hits <style=cIsUtility>launch enemies</style>. Juggled enemies take more damage.");
        }

        static void Cooldown(int stacks, ref float __result)
        {
            if (c.Active && stacks > 0)
                __result = Mathf.Max(0, c.Total("Cooldown", stacks));
        }
    }
}
