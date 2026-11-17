using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.Enemies
{
    // Enemy kill gold scaling, based on RiskyMod's MonsterGoldRewards. Only gold dropped by
    // enemies on death changes; item, barrel and shrine gold keep their own values.
    internal static class MonsterGold
    {
        private const string Section = "Enemies - Gold";
        private static ConfigEntry<bool> enabled, lockToEntry, compensateCredits, compensateStage1;
        private static ConfigEntry<float> inflation, overall;
        private static float goldBank;
        private static GameModeIndex simulacrum = GameModeIndex.Invalid;
        private static bool simulacrumResolved;

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enabled", true,
                "Rescale gold dropped by enemies when killed. Vanilla: false.");
            overall = C.Bind(Section, "Overall Gold Multiplier", .85f, new ConfigDescription(
                "Multiplies all enemy kill gold. Vanilla: 1.", new AcceptableValueRange<float>(0f, 10f)));
            lockToEntry = C.BindBool(Section, "Lock Gold To Stage Entry Difficulty", true,
                "Enemy gold stays at the difficulty you entered the stage with, like chest prices. Vanilla: false.");
            inflation = C.Bind(Section, "Inflation Coefficient", .2f, new ConfigDescription(
                "How quickly enemy gold falls behind as difficulty rises. 0 disables this scaling. Gold at the start of a run is unaffected. RiskyMod: 0.2.",
                new AcceptableValueRange<float>(0f, 5f)));
            compensateCredits = C.BindBool(Section, "Compensate For Enemy Credits", true,
                "Divide enemy gold by the Directors - Enemy Credits multiplier, so its extra enemies do not add extra gold. Vanilla: false.");
            compensateStage1 = C.BindBool(Section, "Compensate On Stage 1", true,
                "Also apply the enemy credits compensation on the first stage. RiskyMod: false.");
            if (!enabled.Value) return;
            Run.onRunStartGlobal += _ => goldBank = 0f;
            new Harmony("3Questionmarks.3QsVanillaTweaks.Enemies.MonsterGold").Patch(A.Method(typeof(CharacterBody), "Start"),
                postfix: new HarmonyMethod(typeof(MonsterGold), nameof(AfterBodyStart)));
        }

        private static bool InSimulacrum(Run run)
        {
            if (!simulacrumResolved)
            {
                simulacrumResolved = true;
                simulacrum = GameModeCatalog.FindGameModeIndex("InfiniteTowerRun");
            }
            return simulacrum != GameModeIndex.Invalid && run.gameModeIndex == simulacrum;
        }

        private static void AfterBodyStart(CharacterBody __instance)
        {
            var run = Run.instance;
            if (!NetworkServer.active || !run || !C.Get(enabled) || InSimulacrum(run)) return;
            var rewards = __instance.GetComponent<DeathRewards>();
            if (!rewards || rewards.goldReward == 0) return;

            float current = Mathf.Max(.0001f, run.difficultyCoefficient);
            float difficulty = C.Get(lockToEntry) && Stage.instance ? Stage.instance.entryDifficultyCoefficient : current;
            float gold = rewards.goldReward * C.Get(overall);
            if (C.Get(lockToEntry) && Stage.instance) gold *= difficulty / current;
            if (C.Get(compensateCredits) && (C.Get(compensateStage1) || run.stageClearCount > 0))
                gold /= Mathf.Max(.01f, Directors.EnemyCredits.IncomeMultiplier);
            float k = C.Get(inflation);
            if (k > 0f)
            {
                // (1 + k) makes the factor exactly 1 at difficulty 1, the start of a run.
                float d = Mathf.Max(.0001f, difficulty);
                gold *= (1f + k) * Mathf.Pow(d, .25f) / (1f + k * d);
            }

            // Carry fractional gold between kills so rounding down loses nothing over a run.
            float whole = Mathf.Floor(gold);
            goldBank += gold - whole;
            if (goldBank >= 1f)
            {
                goldBank -= 1f;
                whole += 1f;
            }
            rewards.goldReward = (uint)Mathf.Max(1f, whole);
        }
    }
}
