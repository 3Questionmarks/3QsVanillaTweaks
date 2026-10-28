using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class ShrineOfBlood
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> goldPerHealth, chestBonus;
        private static int? smallChestCost;
        [ThreadStatic] private static Payment active;

        internal sealed class Payment
        {
            internal ShrineBloodBehavior Shrine;
            internal HealthComponent Health;
            internal float Before;
        }

        internal static void Init()
        {
            const string section = "Interactables - Shrine of Blood";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable changes to Shrine of Blood.");
            goldPerHealth = ModConfig.Bind(section, "Gold Per Health Lost", 1f,
                new ConfigDescription("Gold awarded per point of health lost.", new AcceptableValueRange<float>(0f, 1000f)));
            chestBonus = ModConfig.Bind(section, "Small Chest Bonus", 1f,
                new ConfigDescription("Extra gold flat gold given in multiples of a small chest's current cost.", new AcceptableValueRange<float>(0f, 1000f)));
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.Blood");
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(ShrineOfBlood), nameof(BeforeInteraction)),
                finalizer: new HarmonyMethod(typeof(ShrineOfBlood), nameof(AfterInteraction)));
            harmony.Patch(AccessTools.Method(typeof(ShrineBloodBehavior), "AddShrineStack"),
                transpiler: new HarmonyMethod(typeof(ShrineOfBlood), nameof(ChangePayout)));
        }

        private static void BeforeInteraction(PurchaseInteraction __instance, Interactor activator, out Payment __state)
        {
            __state = active;
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !activator) return;
            ShrineBloodBehavior shrine = __instance.GetComponent<ShrineBloodBehavior>();
            HealthComponent health = activator.GetComponent<HealthComponent>();
            if (shrine && health) active = new Payment { Shrine = shrine, Health = health, Before = health.combinedHealth };
        }

        private static void AfterInteraction(Payment __state) { active = __state; }

        private static IEnumerable<CodeInstruction> ChangePayout(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode != OpCodes.Conv_U4) continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ShrineOfBlood), nameof(Payout)));
                ++replaced;
            }
            if (replaced != 1) throw new InvalidOperationException("Could not find the Shrine of Blood gold calculation.");
        }

        private static uint Payout(uint original, ShrineBloodBehavior shrine, Interactor activator)
        {
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !activator) return original;
            HealthComponent health = activator.GetComponent<HealthComponent>();
            PurchaseInteraction purchase = shrine.GetComponent<PurchaseInteraction>();
            if (!health || !purchase) return original;
            float lost = active != null && active.Shrine == shrine && active.Health == health
                ? Mathf.Max(0f, active.Before - health.combinedHealth)
                : health.fullCombinedHealth * purchase.cost / 100f;
            if (!smallChestCost.HasValue)
            {
                try
                {
                    PurchaseInteraction chest = Stages.StageFeatureSpawnHelper.LoadSpawnCard("iscChest1").prefab.GetComponent<PurchaseInteraction>();
                    smallChestCost = chest ? chest.cost : 25;
                }
                catch (Exception e)
                {
                    smallChestCost = 25;
                    ThreeQsVanillaTweaks.Log.Warning("Could not read the small chest price; using 25: " + e.Message);
                }
            }
            return CalculateGold(lost, ModConfig.Get(goldPerHealth),
                Run.instance.GetDifficultyScaledCost(smallChestCost.Value), ModConfig.Get(chestBonus));
        }

        internal static uint CalculateGold(float healthLost, float perHealth, int chestCost, float bonus)
        {
            double gold = Math.Max(0d, healthLost) * perHealth + Math.Max(0, chestCost) * (double)bonus;
            return (uint)Math.Min(uint.MaxValue, Math.Max(0d, gold));
        }
    }
}
