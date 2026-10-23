using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Other
{
    /// <summary>
    /// Configures the vanilla world Lunar Coin drop chance and its decay.
    /// Fleeting Moon applies its separate multiplier to the same controller field.
    /// </summary>
    internal static class LunarCoins
    {
        private const string Section = "Other - Lunar Coins";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> initialChancePercent;
        private static ConfigEntry<float> multiplierAfterDrop;
        private static readonly FieldInfo chanceField = AccessTools.Field(typeof(PlayerCharacterMasterController), "lunarCoinChanceMultiplier");
        private static bool patchedReduction;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true,
                "Enable configurable world Lunar Coin drop rates.");
            initialChancePercent = ModConfig.File.Bind(Section, "Initial Drop Chance (%)", 0.5f,
                "Base chance of a Lunar Coin dropping from an eligible kill, as a percentage. Vanilla: 0.5%.");
            multiplierAfterDrop = ModConfig.File.Bind(Section, "Multiplier After Successful Drop", 1f,
                "Chance multiplier applied after each successful Lunar Coin drop. 1 = no reduction (default); 0.5 = vanilla halving.");

            if (chanceField == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Lunar Coins: lunarCoinChanceMultiplier field not found; drop changes unavailable.");
                return;
            }

            Harmony harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.LunarCoins");
            MethodInfo awake = AccessTools.Method(typeof(PlayerCharacterMasterController), "Awake");
            if (awake != null)
                harmony.Patch(awake, postfix: new HarmonyMethod(typeof(LunarCoins), nameof(OnControllerAwake)));

            // The vanilla successful-drop halving is expressed as field * 0.5f.
            // Locate that pattern rather than replacing unrelated 0.5 constants.
            foreach (MethodInfo method in typeof(PlayerCharacterMasterController).GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                try
                {
                    if (!MethodReferencesField(method, chanceField)) continue;
                    harmony.Patch(method, transpiler: new HarmonyMethod(typeof(LunarCoins), nameof(ReplaceDropReduction)));
                }
                catch (Exception ex)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Lunar Coins: failed to inspect/patch " + method.Name + ": " + ex.Message);
                }
            }
            ThreeQsVanillaTweaks.Log.Info("Lunar Coins: base drop chance configured; successful-drop reduction patch " +
                (patchedReduction ? "installed" : "NOT FOUND (vanilla reduction may remain)") + ".");
        }

        private static void OnControllerAwake(PlayerCharacterMasterController __instance)
        {
            if (!ModConfig.Get(enabled) || chanceField == null || __instance == null) return;
            // Vanilla's controller multiplier begins at 1 for a 0.5% base chance.
            float initial = Mathf.Clamp(ModConfig.Get(initialChancePercent), 0f, 100f) / 0.5f;
            chanceField.SetValue(__instance, initial);
        }

        private static float Reduction(float original)
        {
            return ModConfig.Get(enabled) ? Mathf.Max(0f, ModConfig.Get(multiplierAfterDrop)) : original;
        }

        private static IEnumerable<CodeInstruction> ReplaceDropReduction(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i + 2 < list.Count; ++i)
            {
                if (list[i].opcode != OpCodes.Ldfld || !Equals(list[i].operand, chanceField)) continue;
                if (list[i + 1].opcode != OpCodes.Ldc_R4 ||
                    Math.Abs(Convert.ToSingle(list[i + 1].operand) - 0.5f) > 0.00001f) continue;
                if (list[i + 2].opcode != OpCodes.Mul) continue;
                // Push the configured multiplier in place of the vanilla literal.
                list[i + 1].opcode = OpCodes.Ldc_R4;
                list[i + 1].operand = 0.5f;
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(LunarCoins), nameof(Reduction))));
                patchedReduction = true;
                i += 3;
            }
            return list;
        }

        private static bool MethodReferencesField(MethodInfo method, FieldInfo field)
        {
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            if (il == null) return false;
            byte[] token = BitConverter.GetBytes(field.MetadataToken);
            for (int i = 1; i + 3 < il.Length; i++)
                if (il[i - 1] == (byte)OpCodes.Ldfld.Value && il[i] == token[0] &&
                    il[i + 1] == token[1] && il[i + 2] == token[2] && il[i + 3] == token[3])
                    return true;
            return false;
        }
    }
}
