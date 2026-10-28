using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Other
{
    /// <summary>Configures vanilla world Lunar Coin drops without replacing the drop event.</summary>
    internal static class LunarCoins
    {
        private const string Section = "Other - Lunar Coins";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> initialChancePercent;
        private static ConfigEntry<float> multiplierAfterDrop;
        private static readonly FieldInfo chanceField = AccessTools.Field(typeof(PlayerCharacterMasterController), "lunarCoinChanceMultiplier");
        private static readonly List<ILHook> reductionHooks = new List<ILHook>();
        private static int patchedAssignments;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true,
                "Enable configurable world Lunar Coin drop rates.");
            initialChancePercent = ModConfig.Bind(Section, "Initial Drop Chance (%)", 0.5f,
                "Base chance of a Lunar Coin dropping from an eligible kill, as a percentage. Vanilla: 0.5%.");
            multiplierAfterDrop = ModConfig.Bind(Section, "Multiplier After Successful Drop", 1f,
                "Chance multiplier applied after each successful Lunar Coin drop. 1 = no reduction (default); 0.5 = vanilla halving.");

            if (chanceField == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Lunar Coins: lunarCoinChanceMultiplier field not found; drop changes unavailable.");
                return;
            }

            Harmony harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.LunarCoins");
            MethodInfo awake = AccessTools.Method(typeof(PlayerCharacterMasterController), "Awake");
            if (awake != null)
                harmony.Patch(awake, prefix: new HarmonyMethod(typeof(LunarCoins), nameof(OnControllerAwake)));

            // Current versions register the drop callback from Init in the nested <>c type.
            // Do not depend on the compiler-generated suffix (currently <Init>b__90_0).
            MethodInfo[] methods = ImplementationTypes(typeof(PlayerCharacterMasterController))
                .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() != null)
                .OrderByDescending(method => method.Name.StartsWith("<Init>b__", StringComparison.Ordinal))
                .ToArray();
            foreach (MethodInfo method in methods)
            {
                if (!method.Name.StartsWith("<Init>b__", StringComparison.Ordinal) && !MethodReferencesChance(method)) continue;
                try
                {
                    int previous = patchedAssignments;
                    ILHook hook = new ILHook(method, PatchSuccessfulDrop);
                    if (patchedAssignments == previous) { hook.Dispose(); continue; }
                    reductionHooks.Add(hook);
                    ThreeQsVanillaTweaks.Log.Info("Lunar Coins: successful-drop multiplier hooked in " + method.DeclaringType.FullName + "." + method.Name + ".");
                }
                catch (Exception ex)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Lunar Coins: failed to patch " + method.DeclaringType.FullName + "." + method.Name + ": " + ex.Message);
                }
            }
            if (reductionHooks.Count > 0)
                ThreeQsVanillaTweaks.Log.Info("Lunar Coins: drop-rate patch v3 installed; " + reductionHooks.Count + " successful-drop handler(s) patched.");
            else
                ThreeQsVanillaTweaks.Log.Warning("Lunar Coins: drop-rate patch v3 could not find the successful-drop multiplier; vanilla reduction may remain.");
        }

        private static void OnControllerAwake(PlayerCharacterMasterController __instance)
        {
            if (!ModConfig.Get(enabled) || chanceField == null || __instance == null) return;
            // This field is already a percentage, despite its name. Set it before Fleeting Moon's Awake postfix.
            chanceField.SetValue(__instance, Mathf.Clamp(ModConfig.Get(initialChancePercent), 0f, 100f));
        }

        private static float Reduction(float original) => ModConfig.Get(enabled)
            ? Mathf.Max(0f, ModConfig.Get(multiplierAfterDrop)) : original;

        private static void PatchSuccessfulDrop(ILContext il)
        {
            var cursor = new ILCursor(il);
            string owner = typeof(PlayerCharacterMasterController).FullName;
            // Match only chance = chance * constant. The initial chance and the roll remain untouched.
            // Unlike a search for 0.5 alone, this also works if another patch changes that constant.
            while (cursor.TryGotoNext(MoveType.After,
                instruction => instruction.MatchLdfld(owner, "lunarCoinChanceMultiplier"),
                instruction => instruction.MatchLdcR4(out _),
                instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Mul,
                instruction => instruction.MatchStfld(owner, "lunarCoinChanceMultiplier")))
            {
                cursor.Index -= 2; // After the constant, before multiplication and assignment.
                cursor.EmitDelegate<Func<float, float>>(Reduction);
                patchedAssignments++;
                cursor.Index += 2;
            }
        }

        private static IEnumerable<Type> ImplementationTypes(Type type)
        {
            yield return type;
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (Type implementation in ImplementationTypes(nested)) yield return implementation;
        }

        private static bool MethodReferencesChance(MethodInfo method)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            byte[] token = BitConverter.GetBytes(chanceField.MetadataToken);
            for (int i = 1; i + 3 < il.Length; i++)
                if (il[i - 1] == 0x7B && il[i] == token[0] && il[i + 1] == token[1] && il[i + 2] == token[2] && il[i + 3] == token[3]) return true;
            return false;
        }
    }
}
