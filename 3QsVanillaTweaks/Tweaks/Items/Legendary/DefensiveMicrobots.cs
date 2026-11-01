using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class DefensiveMicrobots
    {
        static LegendaryConfig config;
        const string State = "EntityStates.CaptainDefenseMatrixItem.DefenseMatrixOn";
        internal static void Init() => LegendaryConfigSupport.Register("CaptainDefenseMatrix", Configure);
        static void Configure(LegendaryConfig c)
        {
            config = c;
            c.StaticField(State, "projectileEraserRadius", new[] { State + "|DeleteNearbyProjectile" });
            c.StaticField(State, "baseRechargeFrequency", new[] { State + "|get_rechargeFrequency" });
            c.StaticField(State, "minimumFireFrequency", new[] { State + "|get_fireFrequency" });
            c.Pair("Projectiles Destroyed", 1, 1, "Maximum projectiles destroyed per activation.");
            if (c.Values.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(CharacterBody).Assembly.GetType(State), "GetItemStack"), postfix: new HarmonyMethod(typeof(DefensiveMicrobots), nameof(Count)));
        }
        static void Count(ref int __result)
        {
            if (config.Values.Active && __result > 0) __result = Mathf.Max(0, Mathf.RoundToInt(config.Values.Total("Projectiles Destroyed", __result)));
        }
    }
}
