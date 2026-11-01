using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class ElectricBoomerang
    {
        internal static void Init() => LegendaryConfigSupport.Register("StunAndPierce", Configure);
        static void Configure(LegendaryConfig c)
        {
            c.Pair("Trigger Chance (%)", 15, 0, "Chance on hit, multiplied by the triggering hit's proc coefficient.");
            c.Values.Literal("RoR2.GlobalEventManager", "OnHitEnemy", 15f, () => c.Values.Total("Trigger Chance (%)", c.Values.Count(N.Source)), "StunAndPierce", 0, 70);
            // Native launch damage is 40%; the projectile's impact multiplier supplies the remaining factor.
            c.Count("RoR2.GlobalEventManager", "OnHitEnemy", "StunAndPierce", .4f, "Damage (%)", 300);
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/StunAndPierceBoomerang"));
        }
    }
}
