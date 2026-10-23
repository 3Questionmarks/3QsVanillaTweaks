using BepInEx.Configuration;
using R2API;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;
using Tweaks.ModCompatibility;

namespace Tweaks.Items.Legendary
{
    internal static class LaserScope
    {
        private const string Section = "Items - 03 Legendary - Laser Scope";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> baseCritChance;
        private static ConfigEntry<float> critChancePerStack;

        private static ConfigEntry<float> baseCritDamage;
        private static ConfigEntry<float> critDamagePerStack;

        internal static void Init()
        {
            enabled = ModConfig.Bind(Section, "Enabled", true,
                "Give Laser Scope additional critical strike chance without changing its original critical damage bonus.");
            baseCritChance = ModConfig.Bind(Section, "Base Crit Chance", 10f,
                "Percentage points of critical strike chance granted by the first Laser Scope.");
            critChancePerStack = ModConfig.Bind(Section, "Crit Chance Per Stack", 0f,
                "Additional percentage points of critical strike chance for each Laser Scope after the first.");

            baseCritDamage = ModConfig.Bind(Section, "Base Crit Damage (%)", 100f,
                "Extra critical strike damage granted by the first Laser Scope. Vanilla: 100 percentage points.");
            critDamagePerStack = ModConfig.Bind(Section, "Crit Damage Per Stack (%)", 100f,
                "Extra critical strike damage per additional Laser Scope. Vanilla: 100 percentage points.");

            On.RoR2.CharacterBody.RecalculateStats += CharacterBody_RecalculateStats;
            UpdateDescriptions();
            ModConfig.EffectiveSettingsChanged += UpdateDescriptions;
            LookingGlassStatRegistry.Register(() => DLC1Content.Items.CritDamage != null ? DLC1Content.Items.CritDamage.itemIndex : ItemIndex.None, "Bonus Crit Damage:", FormatLookingGlass);
        }

        private static void CharacterBody_RecalculateStats(
            On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self)
        {
            orig(self);

            if (!ModConfig.Get(enabled) || !self || !self.inventory)
                return;

            int stacks = self.inventory.GetItemCountPermanent(DLC1Content.Items.CritDamage.itemIndex);
            if (stacks <= 0)
                return;

            self.crit += ModConfig.Get(baseCritChance) + ModConfig.Get(critChancePerStack) * (stacks - 1);
            // Vanilla has already added +100% critical damage per scope in orig().
            // Apply only the difference to avoid double-counting the vanilla bonus.
            float desiredExtra = (ModConfig.Get(baseCritDamage) + ModConfig.Get(critDamagePerStack) * (stacks - 1)) / 100f;
            self.critMultiplier += desiredExtra - stacks;
        }

        private static string FormatLookingGlass(int stacks, string text)
        {
            if (!ModConfig.Get(enabled)) return text;
            float critDamage = ModConfig.Get(baseCritDamage) + (stacks - 1) * ModConfig.Get(critDamagePerStack);
            float critChance = ModConfig.Get(baseCritChance) + (stacks - 1) * ModConfig.Get(critChancePerStack);
            text = LookingGlassStatRegistry.ReplaceValue(text, "Bonus Crit Damage:", critDamage, "#E5C962");
            return LookingGlassStatRegistry.AddValue(text, "Bonus Crit Chance", critChance, "%", "#E5C962", "+");
        }

        private static void UpdateDescriptions()
        {
            if (!ModConfig.Get(enabled))
                return;

            string baseBonus = FormatPercent(ModConfig.Get(baseCritChance));
            string damageBase = FormatPercent(ModConfig.Get(baseCritDamage));
            string damageStack = FormatPercent(ModConfig.Get(critDamagePerStack));
            string stackBonus = FormatPercent(ModConfig.Get(critChancePerStack));
            string critText = "Gain <style=cIsDamage>" + baseBonus + " critical strike chance</style>";

            if (Mathf.Approximately(ModConfig.Get(critChancePerStack), 0f))
                critText += ".";
            else
                critText += " <style=cStack>(+" + stackBonus + " per stack)</style>.";

            // Preserve the vanilla critical-damage effect in both descriptions.
            LanguageAPI.Add("ITEM_CRITDAMAGE_DESC",
                critText + " Critical strikes deal an additional <style=cIsDamage>" + damageBase + " damage</style> " +
                "<style=cStack>(" + damageStack + " per stack)</style>.");

            LanguageAPI.Add("ITEM_CRITDAMAGE_PICKUP",
                critText + " Critical strikes deal more damage.");
        }

        private static string FormatPercent(float value)
        {
            return (value >= 0f ? "+" : "") + value.ToString("0.##") + "%";
        }
    }
}
