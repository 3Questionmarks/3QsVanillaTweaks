using System;
using BepInEx.Configuration;
using Mono.Cecil;
using Mono.Cecil.Cil;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using System.Linq;
using System.Reflection;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// Shorten equipment recharge durations in the Bazaar and Computational Exchange, without affecting
    /// equipment activation, charges or equipment cooldowns on other stages.
    /// </summary>
    internal static class BazaarEquipmentCooldown
    {
        private const string Section = "Bazaar - Equipment Cooldown";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> cooldownSeconds;
        private static ConfigEntry<bool> computationalExchangeEnabled;
        private static ILHook overrideHook;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true,
                "Limit equipment recharge time to the configured duration in the Bazaar Between Time.");
            cooldownSeconds = ModConfig.Bind(Section, "Equipment Cooldown (Seconds)", 1f,
                "Maximum equipment recharge duration while in the Bazaar (default: 1 second). " +
                "Only shortens cooldowns; existing reductions can make them faster.");

            computationalExchangeEnabled = ModConfig.BindBool(Section, "Enable in Computational Exchange", true,
                "Apply the same reduced equipment recharge time in Computational Exchange.");

            IL.RoR2.Inventory.UpdateEquipment += PatchNormalRecharge;
            // Resolve the two-argument overload without referencing the version-dependent
            // EquipmentLocation type in project source.
            MethodInfo executed = typeof(EquipmentSlot).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "OnEquipmentExecuted" && m.GetParameters().Length == 2);
            if (executed != null)
                overrideHook = new ILHook(executed, PatchExplicitCooldownOverride);
            else
                Debug.LogWarning("[3QsVanillaTweaks] Bazaar equipment: OnEquipmentExecuted overload not found.");
        }

        private static bool InApplicableScene()
        {
            SceneDef scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (scene != null)
                return (string.Equals(scene.baseSceneName, "bazaar", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(scene.baseSceneName, "bazaarbetween", StringComparison.OrdinalIgnoreCase)) ||
                    (ModConfig.Get(computationalExchangeEnabled) &&
                     string.Equals(scene.baseSceneName, "computationalexchange", StringComparison.OrdinalIgnoreCase));

            string name = SceneManager.GetActiveScene().name;
            return string.Equals(name, "bazaar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "bazaarbetween", StringComparison.OrdinalIgnoreCase) ||
                (ModConfig.Get(computationalExchangeEnabled) &&
                 string.Equals(name, "computationalexchange", StringComparison.OrdinalIgnoreCase));
        }

        private static float LimitCooldown(float original)
        {
            if (!ModConfig.Get(enabled) || !InApplicableScene()) return original;
            return Mathf.Min(original, Mathf.Max(0.01f, ModConfig.Get(cooldownSeconds)));
        }

        private static void PatchNormalRecharge(ILContext il)
        {
            var c = new ILCursor(il);
            // EquipmentDef.cooldown * Inventory.CalculateEquipmentCooldownScale()
            // is used to schedule each next charge in Inventory.UpdateEquipment.
            if (c.TryGotoNext(MoveType.After,
                    x => x.MatchLdfld<EquipmentDef>("cooldown"),
                    x => x.MatchLdloc(2),
                    x => x.MatchMul()))
            {
                c.EmitDelegate<Func<float, float>>(LimitCooldown);
                Debug.Log("[3QsVanillaTweaks] Bazaar equipment: normal recharge hook installed.");
            }
            else
            {
                Debug.LogWarning("[3QsVanillaTweaks] Bazaar equipment: normal recharge pattern not found.");
            }
        }

        private static void PatchExplicitCooldownOverride(ILContext il)
        {
            var c = new ILCursor(il);
            // A few equipment behaviours explicitly override their cooldown.
            // Limit those values too, after the nullable cooldown is unwrapped.
            if (c.TryGotoNext(MoveType.After,
                    x => x.OpCode == OpCodes.Call && x.Operand is MethodReference method &&
                         method.Name == "get_Value" && method.DeclaringType.FullName.Contains("Nullable")))
            {
                c.EmitDelegate<Func<float, float>>(LimitCooldown);
                Debug.Log("[3QsVanillaTweaks] Bazaar equipment: override recharge hook installed.");
            }
            else
            {
                Debug.LogWarning("[3QsVanillaTweaks] Bazaar equipment: override recharge pattern not found.");
            }
        }
    }
}
