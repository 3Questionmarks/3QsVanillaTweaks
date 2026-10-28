using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using HarmonyLib;
using RoR2;
using RoR2.ExpansionManagement;
using Tweaks.Stages;

namespace Tweaks
{
    // Shared run-time requirements. No Init(): this helper does not register a module.
    internal static class SpawnRequirements
    {
        internal static bool CommandActive => RunArtifactManager.instance &&
            RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.Command);

        internal static bool ExpansionEnabled(string expansionName)
        {
            if (!Run.instance) return false;
            // Resolve content fields at runtime: older GameLibs omit some expansion fields.
            Type content = typeof(Run).Assembly.GetType("RoR2." + expansionName + "Content");
            var expansion = content == null ? null : AccessTools.Field(content, expansionName)?.GetValue(null) as ExpansionDef;
            if (expansion) return Run.instance.IsExpansionEnabled(expansion);
            return StageFeatureSpawnHelper.ExpansionEnabled("RoR2/" + expansionName + "/Common/" + expansionName + ".asset");
        }

        internal static bool AddressAllowed(string address)
        {
            if (!Run.instance || string.IsNullOrEmpty(address)) return false;
            string[] parts = address.Split('/');
            if (parts.Length < 2 || !parts[0].Equals("RoR2", StringComparison.OrdinalIgnoreCase) ||
                !parts[1].StartsWith("DLC", StringComparison.OrdinalIgnoreCase)) return true;
            return ExpansionEnabled(parts[1].ToUpperInvariant());
        }

        internal static bool ItemAllowed(ItemDef item)
        {
            return Run.instance && item && (!item.requiredExpansion || Run.instance.IsExpansionEnabled(item.requiredExpansion));
        }

        internal static bool SceneAllowed(SceneDef scene)
        {
            return Run.instance && scene && (!scene.requiredExpansion || Run.instance.IsExpansionEnabled(scene.requiredExpansion));
        }

        internal static bool PrinterTierAvailable(ItemTier tier)
        {
            Run run = Run.instance;
            if (!run) return false;
            switch (tier)
            {
                case ItemTier.Tier1: return run.availableTier1DropList.Count > 0;
                case ItemTier.Tier2: return run.availableTier2DropList.Count > 0;
                case ItemTier.Tier3: return run.availableTier3DropList.Count > 0;
                case ItemTier.Boss: return run.availableBossDropList.Count > 0;
                case ItemTier.Lunar: return run.availableLunarItemDropList.Count > 0;
                case ItemTier.VoidTier1: return ExpansionEnabled("DLC1") && run.availableVoidTier1DropList.Count > 0;
                case ItemTier.VoidTier2: return ExpansionEnabled("DLC1") && run.availableVoidTier2DropList.Count > 0;
                case ItemTier.VoidTier3: return ExpansionEnabled("DLC1") && run.availableVoidTier3DropList.Count > 0;
                case ItemTier.VoidBoss: return ExpansionEnabled("DLC1") && run.availableVoidBossDropList.Count > 0;
                default: return false;
            }
        }
    }
}
