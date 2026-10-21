using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using R2API;
using RoR2;
using ModConfig = ThreeQsVanillaTweaks.Config.ConfigManager;

namespace ThreeQsVanillaTweaks.Tweaks.Items.Uncommon
{
    internal static class RegeneratingScrap
    {
        private const string Section = "Items - 02 Uncommon - Regenerating Scrap";
        private static ConfigEntry<bool> enabled, common, uncommon, legendary, boss;
        private static bool installed;
        private static ILHook paymentHook;

        // IsItemDisabled is present in the game assembly but absent from some public reference assemblies.
        private static readonly MethodInfo IsItemDisabledMethod = typeof(Inventory).GetMethod(
            "IsItemDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(ItemIndex) }, null);

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Enabled", true, "Enable changes to Regenerating Scrap.");
            common = ModConfig.BindBool(Section, "Common Printers", true, "Allow Regenerating Scrap in white printers.");
            uncommon = ModConfig.BindBool(Section, "Uncommon Printers", true, "Allow Regenerating Scrap in green printers.");
            legendary = ModConfig.BindBool(Section, "Legendary Printers", false, "Allow Regenerating Scrap in red printers.");
            boss = ModConfig.BindBool(Section, "Boss Printers", false, "Allow Regenerating Scrap in yellow printers.");
            // Register before the logbook/UI can cache the original description.
            UpdateDescription("ITEM_REGENERATINGSCRAP_DESC");
            UpdatePickupDescription();
            RoR2Application.onLoad += Install;
        }

        private static bool Allows(ItemTier tier)
        {
            if (!enabled.Value) return tier == ItemTier.Tier2;
            switch (tier)
            {
                case ItemTier.Tier1: return common.Value;
                case ItemTier.Tier2: return uncommon.Value;
                case ItemTier.Tier3: return legendary.Value;
                case ItemTier.Boss: return boss.Value;
                default: return false;
            }
        }

        private static void Install()
        {
            if (installed) return;
            installed = true;

            CostTypeDef green = CostTypeCatalog.GetCostTypeDef(CostTypeIndex.GreenItem);
            if (green == null || green.payCost == null)
            {
                Log.Warning("Regenerating Scrap: could not locate the vanilla printer payment method.");
                return;
            }

            // All four printer tiers use the same underlying vanilla payment method.
            // Hook it once; do not modify the global ItemDef tier.
            try
            {
                paymentHook = new ILHook(green.payCost.Method, PatchPayment);
            }
            catch (Exception exception)
            {
                Log.Warning("Regenerating Scrap: payment IL hook failed: " + exception);
                return;
            }

            InstallAffordability(CostTypeIndex.WhiteItem);
            InstallAffordability(CostTypeIndex.GreenItem);
            InstallAffordability(CostTypeIndex.RedItem);
            InstallAffordability(CostTypeIndex.BossItem);
            UpdateDescription(DLC1Content.Items.RegeneratingScrap?.descriptionToken);
            Log.Info("Regenerating Scrap tweak installed.");
        }

        private static void PatchPayment(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);

            // The vanilla payment loop loads ItemDef.tier, then the printer tier,
            // then branches away if the tiers differ. Match the comparison
            // structurally rather than depending on generated display-class names.
            if (!cursor.TryGotoNext(MoveType.Before,
                i => i.MatchCallvirt<ItemDef>("get_tier"),
                i => i.MatchLdloc(0),
                i => i.OpCode == OpCodes.Ldfld,
                i => i.MatchLdfld<CostTypeDef.PayCostContext>("costTypeDef"),
                i => i.MatchLdfld<CostTypeDef>("itemTier"),
                i => i.OpCode == OpCodes.Bne_Un_S || i.OpCode == OpCodes.Bne_Un))
            {
                throw new InvalidOperationException(
                    "Regenerating Scrap: vanilla printer tier comparison not found.");
            }

            cursor.Index += 5;
            Instruction branch = cursor.Next;
            ILLabel rejectTarget = branch.Operand as ILLabel;
            if (rejectTarget == null || il.Body.Variables.Count <= 11 ||
                il.Body.Variables[11].VariableType.FullName != typeof(ItemDef).FullName)
            {
                throw new InvalidOperationException(
                    "Regenerating Scrap: unexpected printer payment IL layout.");
            }

            cursor.Remove();

            // Stack already contains (item tier, printer tier).
            // Append the ItemDef from the vanilla loop, then test eligibility.
            cursor.Emit(OpCodes.Ldloc, il.Body.Variables[11]);
            cursor.EmitDelegate<Func<ItemTier, ItemTier, ItemDef, bool>>(IsEligibleForPrinter);
            cursor.Emit(OpCodes.Brfalse, rejectTarget);
            Log.Info("Regenerating Scrap: printer payment IL comparison patched.");
        }

        private static bool IsEligibleForPrinter(ItemTier itemTier, ItemTier printerTier, ItemDef item)
        {
            if (item == DLC1Content.Items.RegeneratingScrap)
                return Allows(printerTier);
            return itemTier == printerTier;
        }

        private static void InstallAffordability(CostTypeIndex index)
        {
            CostTypeDef def = CostTypeCatalog.GetCostTypeDef(index);
            if (def == null)
            {
                Log.Warning("Regenerating Scrap: missing cost type " + index);
                return;
            }
            var original = def.isAffordable;
            def.isAffordable = (costDef, context) =>
            {
                bool vanilla = original(costDef, context);
                ItemTier tier = costDef.itemTier;
                if (!enabled.Value) return vanilla;
                if (tier == ItemTier.Tier2 && uncommon.Value) return vanilla;
                if (tier != ItemTier.Tier2 && !Allows(tier)) return vanilla;

                CharacterBody body = context.activator ? context.activator.GetComponent<CharacterBody>() : null;
                Inventory inventory = body ? body.inventory : null;
                if (!inventory || inventory.inventoryDisabled) return false;

                ItemDef scrap = DLC1Content.Items.RegeneratingScrap;
                if (scrap == null || !scrap.canRemove || scrap.ContainsTag(ItemTag.ObjectiveRelated)) return vanilla;
                int scrapCount = inventory.GetItemCountPermanent(scrap.itemIndex);
                if (scrapCount <= 0 || IsDisabled(inventory, scrap.itemIndex)) return vanilla;

                // Query the vanilla counter, retaining its blacklist/removability rules.
                int ordinaryCount = 0;
                for (int amount = 1; amount <= context.cost; amount++)
                {
                    if (!inventory.HasAtLeastXTotalRemovablePermanentItemsOfTier(tier, amount)) break;
                    ordinaryCount = amount;
                }

                if (tier == ItemTier.Tier2)
                {
                    // The original green-tier count already includes Regenerating Scrap.
                    return ordinaryCount - scrapCount >= context.cost;
                }
                return ordinaryCount + scrapCount >= context.cost;
            };
        }

        private static bool IsDisabled(Inventory inventory, ItemIndex index)
        {
            if (IsItemDisabledMethod == null)
            {
                // Fail closed: never grant extra affordability without checking the disabled mask.
                return true;
            }
            try { return (bool)IsItemDisabledMethod.Invoke(inventory, new object[] { index }); }
            catch (Exception exception)
            {
                Log.Warning("Regenerating Scrap: disabled-item check failed: " + exception);
                return true;
            }
        }

        private static void UpdateDescription(string token)
        {
            if (!enabled.Value) return;
            ItemDef scrap = DLC1Content.Items.RegeneratingScrap;
            if (scrap == null) return;
            List<string> tiers = new List<string>();
            if (common.Value) tiers.Add("<color=#FFFFFF>Common</color>");
            if (uncommon.Value) tiers.Add("<color=#77FF66>Uncommon</color>");
            if (legendary.Value) tiers.Add("<color=#FF5555>Legendary</color>");
            if (boss.Value) tiers.Add("<color=#FFFF00>Boss</color>");
            string description = tiers.Count == 0
                ? "Cannot be used in 3D Printers. Regenerates at the start of each stage."
                : "Prioritized when used with " + FormatList(tiers) + " 3D Printers. Can be used once per stage.";
            LanguageAPI.Add(token, description);
        }

        private static void UpdatePickupDescription()
        {
            if (!enabled.Value) return;
            List<string> tiers = new List<string>();
            if (common.Value) tiers.Add("<color=#FFFFFF>Common</color>");
            if (uncommon.Value) tiers.Add("<color=#77FF66>Uncommon</color>");
            if (legendary.Value) tiers.Add("<color=#FF5555>Legendary</color>");
            if (boss.Value) tiers.Add("<color=#FFFF00>Boss</color>");
            string pickup = tiers.Count == 0
                ? "Cannot be used in 3D Printers. <style=cIsUtility>Regenerates each stage.</style>"
                : "Prioritized in " + FormatList(tiers) + " 3D Printers. <style=cIsUtility>Regenerates each stage.</style>";
            LanguageAPI.Add("ITEM_REGENERATINGSCRAP_PICKUP", pickup);
        }

        private static string FormatList(List<string> values)
        {
            if (values.Count == 1) return values[0];
            if (values.Count == 2) return values[0] + " and " + values[1];
            string last = values[values.Count - 1];
            values.RemoveAt(values.Count - 1);
            return string.Join(", ", values) + ", and " + last;
        }
    }
}
