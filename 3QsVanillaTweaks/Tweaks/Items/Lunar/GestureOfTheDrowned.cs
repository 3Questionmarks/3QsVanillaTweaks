using System;
using BepInEx.Configuration;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Items.Lunar
{
    internal static class GestureOfTheDrowned
    {
        private const string Section = "Items - Lunar - Gesture of the Drowned";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<bool> automaticUse;
        private static ConfigEntry<float> firstCooldown;
        private static ConfigEntry<float> extraCooldown;
        private static ConfigEntry<bool> pauseBazaar;
        private static ConfigEntry<bool> pauseExchange;

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true, "Enable configurable Gesture effects.");
            automaticUse = ModConfig.BindBool(Section, "Automatically Use Equipment", true,
                "Automatically activate ready equipment outside excluded stages (vanilla: true).");
            firstCooldown = ModConfig.Bind(Section, "First Stack Cooldown Multiplier", 0.5f,
                "Cooldown scale of the first Gesture (vanilla: 0.5).");
            extraCooldown = ModConfig.Bind(Section, "Additional Stack Cooldown Multiplier", 0.85f,
                "Additional multiplicative cooldown scale for each extra Gesture (vanilla: 0.85).");

            // Keep the existing config keys so users do not lose their settings.
            // Despite the legacy names, these suppress automatic use, NOT cooldown progression.
            pauseBazaar = ModConfig.BindBool(Section, "Pause Cooldowns in Bazaar Between Time", true,
                "Prevent Gesture from automatically using ready equipment in the Bazaar. ");
            pauseExchange = ModConfig.BindBool(Section, "Pause Cooldowns in Computational Exchange", true,
                "Prevent Gesture from automatically using ready equipment in Computational Exchange. ");

            var scale = AccessTools.Method(typeof(Inventory), "CalculateEquipmentCooldownScale");
            if (scale != null)
                new Harmony("3Questionmarks.3QsVanillaTweaks.GestureOfTheDrowned")
                    .Patch(scale, postfix: new HarmonyMethod(typeof(GestureOfTheDrowned), nameof(CooldownScalePostfix)));
            else
                Debug.LogError("[3QsVanillaTweaks] Gesture: CalculateEquipmentCooldownScale method not found.");

            // Auto activation actually occurs in MyFixedUpdate, not FixedUpdate.
            // Never intercept Inventory.UpdateEquipment: cooldowns must keep ticking.
            IL.RoR2.EquipmentSlot.MyFixedUpdate += PatchAutomaticUse;
        }

        private static bool IsAutoUseBlockedHere()
        {
            string scene = SceneManager.GetActiveScene().name.ToLowerInvariant();
            if (ModConfig.Get(pauseBazaar) && (scene == "bazaar" || scene == "bazaarbetween")) return true;
            return ModConfig.Get(pauseExchange) && scene == "computationalexchange";
        }

        private static void CooldownScalePostfix(Inventory __instance, ref float __result)
        {
            if (!ModConfig.Get(enabled) || !__instance) return;
            int count = __instance.GetItemCountEffective(RoR2Content.Items.AutoCastEquipment);
            if (count <= 0) return;
            float vanilla = 0.5f * Mathf.Pow(0.85f, count - 1);
            float configured = Mathf.Max(0.001f, ModConfig.Get(firstCooldown)) *
                Mathf.Pow(Mathf.Max(0.001f, ModConfig.Get(extraCooldown)), count - 1);
            __result *= configured / vanilla;
        }

        private static void PatchAutomaticUse(ILContext il)
        {
            var cursor = new ILCursor(il);
            int patched = 0;
            while (cursor.TryGotoNext(MoveType.After,
                x => x.MatchLdsfld(typeof(RoR2Content.Items), "AutoCastEquipment"),
                x => x.MatchCallvirt<Inventory>("GetItemCountEffective") ||
                     x.MatchCall<Inventory>("GetItemCountEffective")))
            {
                // A zero count disables Gesture's automatic-use checks while preserving
                // manual justPressed / requestEquipmentFire checks and normal cooldowns.
                cursor.EmitDelegate<Func<int, int>>(count =>
                    ModConfig.Get(enabled) &&
                    (!ModConfig.Get(automaticUse) || IsAutoUseBlockedHere()) ? 0 : count);
                patched++;
            }
            if (patched < 2)
                Debug.LogWarning("[3QsVanillaTweaks] Gesture: expected two auto-use sites in " +
                    "EquipmentSlot.MyFixedUpdate, patched " + patched + ".");
        }
    }
}
