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
    internal static class ResonanceDisc
    {
        static R c;
        sealed class Cycle
        {
            internal int Mask;
            internal float Start;
        }

        static readonly ConditionalWeakTable<CharacterBody, Cycle> cycles = new ConditionalWeakTable<CharacterBody, Cycle>();
        internal static void Init()
        {
            c = RareItemSupport.Config("Resonance Disc", "LaserTurbine");
            c.B("Rework", true, "Charge by using primary, secondary, utility and special once each instead of killing enemies.");
            c.N("Skill Use Timeframe", -1, "Seconds to use all four skill slots. -1 gives unlimited time.", -1);
            c.N("Kill Timeframe", 9, "Seconds that each kill contributes charge in normal mode. Vanilla: game-defined kill-charge lifetime.");
            c.N("Kills Required", 4, "Kills required in normal mode. Vanilla: 4.");
            c.Pair("Proc Coefficient", 0, 0, "Beam and blast proc coefficient. Vanilla: beam 1, blast 1.");
            c.Pair("Beam Damage (%)", 300, 300, "Beam base damage. Vanilla: 300% per stack.");
            c.Pair("Explosion Damage (%)", 1000, 1000, "Explosion base damage. Vanilla: 1000% per stack.");
            c.Field("EntityStates.LaserTurbine.FireMainBeamState", "FireBeamServer", "mainBeamProcCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Body)));
            c.Field("EntityStates.LaserTurbine.FireMainBeamState", "FireBeamServer", "mainBeamDamageCoefficient", () => c.Total("Beam Damage (%)", Math.Max(1, c.Count(N.Body))) / (100f * Math.Max(1, c.Count(N.Body))));
            c.Field("EntityStates.LaserTurbine.FireMainBeamState", "FireBeamServer", "secondBombDamageCoefficient", () => c.Total("Explosion Damage (%)", Math.Max(1, c.Count(N.Body))) / (100f * Math.Max(1, c.Count(N.Body))));
            if (c.Enabled.Value)
            {
                U.Patch(typeof(CharacterBody), "OnSkillActivated", typeof(ResonanceDisc), postfix: nameof(Skill));
                U.Patch(typeof(LaserTurbineController), "OnOwnerKilledOtherServer", typeof(ResonanceDisc), prefix: nameof(Kill));
                U.Patch(typeof(EntityStates.LaserTurbine.RechargeState), "FixedUpdate", typeof(ResonanceDisc), prefix: nameof(Recharge));
                U.Patch(typeof(LaserTurbineController), "CalcCurrentChargeValue", typeof(ResonanceDisc), postfix: nameof(Charge));
                RoR2Application.onLoad += BindBeam;
            }

            c.ShowStat = k => c.B("Rework") ? !k.StartsWith("Kill") : !k.StartsWith("Skill Use");
            c.Text(() => c.B("Rework") ? "Using your primary, secondary, utility and special skills" + (c.F("Skill Use Timeframe") >= 0 ? " within " + RareItemSupport.Utility(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Skill Use Timeframe")) + "s") : "") + " charges a disc that fires a beam for " + c.V("Beam Damage (%)", "%") + " damage and explodes for " + c.V("Explosion Damage (%)", "%") + " damage." : "Killing " + RareItemSupport.Utility(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Kills Required"))) + " enemies within " + RareItemSupport.Utility(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Kill Timeframe")) + "s") + " charges a disc that fires a beam for " + c.V("Beam Damage (%)", "%") + " damage and explodes for " + c.V("Explosion Damage (%)", "%") + " damage.", () => c.B("Rework") ? "Use all four skills to charge a powerful disc." : "Rapid kills charge a powerful disc.");
        }

        static void BindBeam()
        {
            c.N("Beam Radius", EntityStates.LaserTurbine.FireMainBeamState.mainBeamRadius, "Beam radius in metres.");
            c.N("Beam Range", EntityStates.LaserTurbine.FireMainBeamState.mainBeamMaxDistance, "Maximum beam distance in metres.");
            c.N("Beam Force", EntityStates.LaserTurbine.FireMainBeamState.mainBeamForce, "Beam force.");
            c.N("Firing Duration", EntityStates.LaserTurbine.FireMainBeamState.baseDuration, "Seconds before the disc begins charging again.");
            var prefab = RareItemSupport.StaticPrefab(typeof(EntityStates.LaserTurbine.FireMainBeamState), "secondBombPrefab");
            if (prefab)
                Tweaks.Items.Uncommon.UncommonProjectiles.RegisterLoaded(c, prefab);
            foreach (var pair in new[]
            {
                Tuple.Create("mainBeamRadius", "Beam Radius"),
                Tuple.Create("mainBeamMaxDistance", "Beam Range"),
                Tuple.Create("mainBeamForce", "Beam Force")
            }

            )
                c.Field("EntityStates.LaserTurbine.FireMainBeamState", "FireBeamServer", pair.Item1, () => c.F(pair.Item2));
            c.Field("EntityStates.LaserTurbine.FireMainBeamState", "Update", "baseDuration", () => c.F("Firing Duration"));
        }

        static CharacterBody Owner(LaserTurbineController t) => A.Field(typeof(LaserTurbineController), "cachedOwnerBody").GetValue(t) as CharacterBody;
        static bool Kill(LaserTurbineController __instance)
        {
            var b = Owner(__instance);
            if (c.Count(b) == 0)
                return true;
            if (!c.B("Rework"))
                b.AddTimedBuff(RoR2Content.Buffs.LaserTurbineKillCharge, c.F("Kill Timeframe"), Mathf.Max(1, Mathf.RoundToInt(c.F("Kills Required"))));
            return false;
        }

        static void Skill(CharacterBody __instance, GenericSkill skill)
        {
            if (!NetworkServer.active || c.Count(__instance) == 0 || !c.B("Rework") || !__instance.skillLocator)
                return;
            var slots = new[]
            {
                __instance.skillLocator.primary,
                __instance.skillLocator.secondary,
                __instance.skillLocator.utility,
                __instance.skillLocator.special
            };
            int index = Array.IndexOf(slots, skill);
            if (index < 0)
                return;
            var s = cycles.GetOrCreateValue(__instance);
            float frame = c.F("Skill Use Timeframe");
            if (frame >= 0 && s.Mask != 0 && Time.fixedTime - s.Start > frame)
                s.Mask = 0;
            if (s.Mask == 0)
                s.Start = Time.fixedTime;
            s.Mask |= 1 << index;
            if (s.Mask == 15)
            {
                s.Mask = 0;
                __instance.SetBuffCount(RoR2Content.Buffs.LaserTurbineKillCharge.buffIndex, Mathf.Max(1, Mathf.RoundToInt(c.F("Kills Required"))));
            }
        }

        static bool Recharge(EntityStates.LaserTurbine.RechargeState __instance)
        {
            var b = RareItemSupport.Owner(__instance);
            if (c.Count(b) == 0)
                return true;
            if (__instance.isAuthority && b.GetBuffCount(RoR2Content.Buffs.LaserTurbineKillCharge) >= Mathf.Max(1, Mathf.RoundToInt(c.F("Kills Required"))))
            {
                if (NetworkServer.active)
                    __instance.GetComponent<LaserTurbineController>().ExpendCharge();
                __instance.outer.SetNextState(new EntityStates.LaserTurbine.ReadyState());
            }

            return false;
        }

        static void Charge(LaserTurbineController __instance, int killChargeCount, ref float __result)
        {
            if (c.Count(Owner(__instance)) > 0)
                __result = Mathf.Clamp01(killChargeCount / Mathf.Max(1, c.F("Kills Required")));
        }
    }
}
