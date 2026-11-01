using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class WarBonds
    {
        static LegendaryConfig config;
        const string TypeName = "RoR2.BarrageOnBossBehaviour";
        internal static void Init() => LegendaryConfigSupport.Register("BarrageOnBoss", Configure);
        static void Configure(LegendaryConfig c)
        {
            config = c;
            c.Number("Starting Missiles", 5, "Missiles supplied at the start of the stage.");
            c.Pair("Maximum Missiles", 20, 5, "Maximum stored missiles.");
            c.Pair("Boss Maximum Health Damage (%)", 2.5f, 2.5f, "Damage per missile against bosses.");
            c.Pair("Other Enemy Damage (%)", 3200, 0, "Damage per missile against other enemies.");
            c.Number("Gold Per Missile", 50, "Gold needed for another missile, before difficulty scaling.");
            c.Number("Initial Missile Delay", 5, "Delay before the bombardment begins.");
            c.Number("Missile Interval", .3f, "Time between missiles.");
            c.Number("Barrage Radius", 2.5f, "Spread of missiles around a boss.");
            c.Number("Target Search Range", 50, "Range for finding targets after the boss dies.");
            c.Describe(() => "During boss events, <style=cIsUtility>" + c.Values.F("Starting Missiles").ToString("0") + " missiles</style> bombard the area, dealing " + c.Values.V("Boss Maximum Health Damage (%)", "%", "cIsDamage") + " of bosses' <style=cIsDamage>maximum health</style> in damage. Collect <style=cIsUtility>$" + c.Values.F("Gold Per Missile").ToString("0") + "</style> <style=cStack>(scales over time)</style> to add a missile, up to " + c.Values.V("Maximum Missiles", "", "cIsUtility") + ".");
            c.FieldValue(TypeName, "missileBarrageCount", "Starting Missiles", 1, "Start");
            c.FieldValue(TypeName, "initialMissileDelay", "Initial Missile Delay", 1, "StartWarBondOnBossGroup", "StartMissileCountdown");
            c.FieldValue(TypeName, "fireDelay", "Missile Interval", 1, "StartMissileBarrage", "UpdateBarrage");
            c.FieldValue(TypeName, "BarrageRadius", "Barrage Radius", 1, "CalculateHitPosition");
            c.FieldValue(TypeName, "searchRange", "Target Search Range", 1, "OnEnable");
            c.Values.Literal(TypeName, "UpdateExtraMissileMoneyCount", 50, () => Mathf.Max(1, c.Values.F("Gold Per Missile")));
            c.Assets(TypeName);
            if (!c.Values.Enabled.Value) return;
            U.Harmony.Patch(AccessTools.PropertyGetter(typeof(BarrageOnBossBehaviour), "maxBuffCount"), postfix: new HarmonyMethod(typeof(WarBonds), nameof(Maximum)));
            U.Harmony.Patch(A.Method(typeof(BarrageOnBossBehaviour), "CalculateMissileDamage"), postfix: new HarmonyMethod(typeof(WarBonds), nameof(Damage)));
        }
        static void Maximum(BarrageOnBossBehaviour __instance, ref int __result)
        {
            if (config.Values.Active) __result = Mathf.Max(0, Mathf.RoundToInt(config.Values.Total("Maximum Missiles", __instance.stack)));
        }
        static void Damage(BarrageOnBossBehaviour __instance, CharacterBody targetBody, ref float __result)
        {
            if (!config.Values.Active || !targetBody || !__instance.body) return;
            bool boss = (bool)AccessTools.PropertyGetter(typeof(BarrageOnBossBehaviour), "isTargetBoss").Invoke(__instance, null);
            __result = boss ? (targetBody.maxHealth + targetBody.maxShield) * config.Values.Total("Boss Maximum Health Damage (%)", __instance.stack) / 100f
                : __instance.body.damage * config.Values.Total("Other Enemy Damage (%)", __instance.stack) / 100f;
        }
    }
}
