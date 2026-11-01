using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Tweaks.Stages
{
    // Scoped to the director attached to an active pillar or Deep Void Signal.
    // Ambient, teleporter, final-boss and escape directors are never registered here.
    internal sealed class HoldoutDirectorScaling : MonoBehaviour
    {
        private static readonly FieldInfo moneyWaves = AccessTools.Field(typeof(CombatDirector), "moneyWaves");
        private static readonly Type waveType = typeof(CombatDirector).GetNestedType("DirectorMoneyWave", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo waveMultiplier = waveType == null ? null : AccessTools.Field(waveType, "multiplier");
        private static readonly FieldInfo accumulatedAward = waveType == null ? null : AccessTools.Field(waveType, "accumulatedAward");
        private CombatDirector director;
        private Func<float> desiredMultiplier;
        private float appliedMultiplier = 1f;

        internal static float Multiplier(int previousEncounters, int required, float increasePercent, float extraMultiplier)
        {
            int regularSteps = Math.Min(Math.Max(0, previousEncounters), Math.Max(0, required - 1));
            int extraSteps = Math.Max(0, previousEncounters - regularSteps);
            return 1f + Math.Max(0f, increasePercent) / 100f * (regularSteps + extraSteps * Math.Max(1f, extraMultiplier));
        }

        internal static void Begin(CombatDirector director, Func<float> multiplier)
        {
            if (!NetworkServer.active || !director) return;
            if (moneyWaves == null || waveMultiplier == null || accumulatedAward == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Holdout credit scaling: native credit-generation fields were not found; leaving the director unchanged.");
                return;
            }
            var settings = director.GetComponents<HoldoutDirectorScaling>().FirstOrDefault(s => s.director == director);
            if (!settings) settings = director.gameObject.AddComponent<HoldoutDirectorScaling>();
            settings.director = director;
            settings.desiredMultiplier = multiplier;
            settings.Apply(multiplier());
        }

        internal static void End(CombatDirector director)
        {
            if (!director) return;
            var settings = director.GetComponents<HoldoutDirectorScaling>().FirstOrDefault(s => s.director == director);
            if (!settings) return;
            settings.desiredMultiplier = null;
            settings.Apply(1f);
        }

        private void FixedUpdate()
        {
            if (NetworkServer.active && director && desiredMultiplier != null)
                Apply(desiredMultiplier());
        }

        private void Apply(float value)
        {
            if (!NetworkServer.active || !director) return;
            float target = Mathf.Max(1f, value);
            if (Mathf.Approximately(target, appliedMultiplier)) return;
            var waves = moneyWaves.GetValue(director) as Array;
            if (waves == null) return; // Awake has not populated the cache yet; retry next tick.
            float ratio = target / appliedMultiplier;
            // Scale both existing budget and future income. Changing creditMultiplier
            // alone does not update the money-wave multipliers cached by native Awake.
            director.monsterCredit *= ratio;
            director.creditMultiplier *= ratio;
            foreach (object wave in waves)
            {
                if (wave == null) continue;
                waveMultiplier.SetValue(wave, (float)waveMultiplier.GetValue(wave) * ratio);
                accumulatedAward.SetValue(wave, (float)accumulatedAward.GetValue(wave) * ratio);
            }
            appliedMultiplier = target;
        }

        private void OnDisable()
        {
            desiredMultiplier = null;
            Apply(1f);
        }

        private void OnDestroy() => Apply(1f);
    }
}
