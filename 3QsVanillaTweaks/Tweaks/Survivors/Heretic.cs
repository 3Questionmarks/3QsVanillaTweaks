using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Heretic
    {
        private const string Root = "Survivors - 21 Heretic";
        private const string StatsRoot = "Survivor-Stats - 21 Heretic";
        private const string PassiveRoot = Root + " - Nevermore";
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled, fixRegen, protectItems, removePenalties;
        private const string CompatRoot = "Mod Compatibility - Characters - Heretic";
        private static ConfigEntry<bool> compatibilityEnabled;
        private static ConfigEntry<float> compatibilityHealth, compatibilityDamage, compatibilityRegen;
        private static bool moffeinInstalled;
        private static string originalMoffeinPassive;
        private static readonly Dictionary<string, Func<string>> compatibilityDescriptions = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        private static bool CompatibilityActive => moffeinInstalled && compatibilityEnabled != null && C.Get(compatibilityEnabled);
        private static CharacterBody prefab;
        private static Harmony harmony;
        private static readonly Dictionary<FieldInfo, ConfigEntry<float>> stats = new Dictionary<FieldInfo, ConfigEntry<float>>();
        private static readonly List<ILHook> hooks = new List<ILHook>();
        private static readonly ConditionalWeakTable<GhostUtilitySkillState, CancelData> cancelStates = new ConditionalWeakTable<GhostUtilitySkillState, CancelData>();
        private static FieldInfo lunarCostItems;
        private static BuffDef disableRegenBuff;

        private sealed class CancelData { internal bool Released; }
        private sealed class PoolScope { internal ItemIndex[] Previous; }
        private sealed class RegenScope
        {
            internal float Base, PerLevel, Negative;
        }

        internal static void Init()
        {
            S.BindGeneral();
            // Use only the plugin GUID: Moffein's assembly is entirely optional.
            moffeinInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.Moffein.Heretic");
            if (C.ModuleEnabled(typeof(Tweaks.ModCompatibility.LookingGlass)))
            {
                compatibilityEnabled = C.Bind(CompatRoot, "Enabled", true,
                    "Integrate with Moffein's Heretic when installed. Base stat overrides take priority over Survivor-Stats; all other settings remain in their normal sections.");
                compatibilityHealth = C.Bind(CompatRoot, "HP", 110f, "Base maximum health with Moffein's Heretic installed.");
                compatibilityDamage = C.Bind(CompatRoot, "Damage", 12f, "Base damage with Moffein's Heretic installed.");
                compatibilityRegen = C.Bind(CompatRoot, "HP Regen", 1f, "Base health regeneration with Moffein's Heretic installed.");
            }
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Heretic changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Heretic stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(PassiveRoot, "Changes Enabled", true, "Enable Nevermore passive changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }

        private static void Load()
        {
            try
            {
                // All plugin Awake methods have completed by onLoad; Init can run before Moffein registers.
                moffeinInstalled = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.Moffein.Heretic");
                GameObject bodyObject = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Heretic/HereticBody.prefab").WaitForCompletion();
                prefab = bodyObject ? bodyObject.GetComponent<CharacterBody>() : null;
                if (!prefab) throw new InvalidOperationException("Heretic body prefab was not found.");
                disableRegenBuff = typeof(CharacterBody).Assembly.GetType("RoR2.DLC4Content+Buffs")?.GetField("DisableRegen", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as BuffDef;
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Heretic");
                fixRegen = C.Bind(Root + " - General", "Fix Regeneration Multipliers", true,
                    "Regeneration multipliers do not affect Heretic's negative base health regeneration. Vanilla: False.");
                if (moffeinInstalled)
                    originalMoffeinPassive = Language.GetString("MOFFEINHERETIC_PASSIVE_DESCRIPTION");
                if (statsEnabled.Value) BindStats();
                Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats), nameof(AfterStats), nameof(RestoreRegen));
                if (passiveEnabled.Value)
                {
                    protectItems = C.Bind(PassiveRoot, "Heresy Items Cannot Be Removed", true,
                        "Heresy items cannot be removed from Heretic, including by cleansing pools, Egocentrism and Shrines of Order. Vanilla: False.");
                    removePenalties = C.Bind(PassiveRoot, "Remove Negative Stacking Effects", true,
                        "Extra Heresy items do not increase skill cooldowns on Heretic. Shadowfade can be cancelled early. Vanilla: False.");
                    InstallProtection();
                    Patch(typeof(LunarPrimaryReplacementSkill), "GetRechargeInterval", postfix: nameof(PrimaryRecharge));
                    Patch(typeof(LunarSecondaryReplacementSkill), "GetRechargeInterval", postfix: nameof(SecondaryRecharge));
                    Patch(typeof(LunarDetonatorSkill), "GetRechargeInterval", postfix: nameof(SpecialRecharge));
                    Patch(typeof(GhostUtilitySkillState), "OnEnter", postfix: nameof(StartShadowfade));
                    Patch(typeof(GhostUtilitySkillState), "FixedUpdate", postfix: nameof(UpdateShadowfade));
                    AddPassive();
                }
                if (moffeinInstalled)
                {
                    FixMoffeinRuinDescription();
                    // Install outside the shared Harmony prefix and the existing language detours.
                    On.RoR2.Language.GetLocalizedStringByToken += CompatibilityLanguage;
                }
                AddOverview();
                C.EffectiveSettingsChanged += Apply;
                Apply();
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Heretic changes could not be loaded: " + error); }
        }

        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] arguments = null)
        {
            MethodInfo target = AccessTools.Method(type, method, arguments);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Heretic), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Heretic), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Heretic), finalizer));
        }
        private static bool IsHeretic(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static bool IsHeretic(Inventory inventory)
        {
            if (!inventory) return false;
            CharacterBody body = inventory.GetComponent<CharacterBody>();
            if (body) return IsHeretic(body);
            CharacterMaster master = inventory.GetComponent<CharacterMaster>();
            if (!master) return false;
            body = master.GetBody();
            if (body) return IsHeretic(body);
            return master.bodyPrefab && IsHeretic(master.bodyPrefab.GetComponent<CharacterBody>());
        }
        private static bool IsHeresy(ItemIndex item) => item != ItemIndex.None &&
            (item == RoR2Content.Items.LunarPrimaryReplacement.itemIndex || item == RoR2Content.Items.LunarSecondaryReplacement.itemIndex ||
             item == RoR2Content.Items.LunarUtilityReplacement.itemIndex || item == RoR2Content.Items.LunarSpecialReplacement.itemIndex);
        private static bool Protected(Inventory inventory, ItemIndex item) => protectItems != null && C.Get(protectItems) && IsHeresy(item) && IsHeretic(inventory);
        internal static bool IgnoreHeresyStackPenalties(CharacterBody body) => NoPenalties(body);
        private static bool NoPenalties(CharacterBody body) => removePenalties != null && C.Get(removePenalties) && IsHeretic(body);

        // Heretic needs negative regeneration defaults, which the shared non-negative stat range excludes.
        private static void BindStats()
        {
            string[,] fields = { { "Health", "MaxHealth" }, { "Shield", "MaxShield" }, { "Health Regen", "Regen" }, { "Damage", "Damage" },
                { "Armour", "Armor" }, { "Move Speed", "MoveSpeed" }, { "Attack Speed", "AttackSpeed" }, { "Critical Chance", "Crit" },
                { "Jump Power", "JumpPower" }, { "Acceleration", "Acceleration" }, { "Sprint Speed Multiplier", "SprintingSpeedMultiplier" } };
            foreach (string prefix in new[] { "base", "level" })
                for (int i = 0; i < fields.GetLength(0); i++)
                {
                    FieldInfo field = AccessTools.Field(typeof(CharacterBody), prefix + fields[i, 1]);
                    if (field == null) continue;
                    float native = (float)field.GetValue(prefab);
                    stats[field] = S.Float(StatsRoot, fields[i, 0] + (prefix == "base" ? "" : " Per Level"), native, native,
                        prefix == "base" ? "Base " + fields[i, 0].ToLowerInvariant() + "." : fields[i, 0] + " gained per level.",
                        fields[i, 1] == "Armor" || fields[i, 1] == "Regen" ? -10000f : 0f);
                }
            stats[AccessTools.Field(typeof(CharacterBody), "baseJumpCount")] = S.Float(StatsRoot, "Jump Count", prefab.baseJumpCount,
                prefab.baseJumpCount, "Number of jumps before landing.");
        }
        private static void ApplyStats(CharacterBody body)
        {
            foreach (var entry in stats)
            {
                float value = C.Get(entry.Value);
                entry.Key.SetValue(body, entry.Key.FieldType == typeof(int) ? (object)(int)value : value);
            }
            ApplyCompatibilityStats(body);
        }
        private static void ApplyCompatibilityStats(CharacterBody body)
        {
            if (!CompatibilityActive) return;
            body.baseMaxHealth = Mathf.Max(1f, C.Get(compatibilityHealth));
            body.baseDamage = Mathf.Max(0f, C.Get(compatibilityDamage));
            body.baseRegen = C.Get(compatibilityRegen);
        }
        private static void Apply()
        {
            if (!prefab) return;
            ApplyStats(prefab);
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (IsHeretic(body)) { ApplyStats(body); body.MarkAllStatsDirty(); }
        }
        private static void BeforeStats(CharacterBody __instance, out RegenScope __state)
        {
            __state = null;
            if (!IsHeretic(__instance)) return;
            ApplyStats(__instance);
            if (!C.Get(fixRegen)) return;
            float negativeBase = Mathf.Min(0f, __instance.baseRegen), negativeLevel = Mathf.Min(0f, __instance.levelRegen);
            if (negativeBase == 0f && negativeLevel == 0f) return;
            __state = new RegenScope { Base = __instance.baseRegen, PerLevel = __instance.levelRegen,
                Negative = negativeBase + negativeLevel * (__instance.level - 1f) };
            // Calculate positive regeneration normally, then add the unmultiplied health drain.
            __instance.baseRegen -= negativeBase;
            __instance.levelRegen -= negativeLevel;
        }
        private static void AfterStats(CharacterBody __instance, RegenScope __state)
        {
            if (__state == null) return;
            // Preserve vanilla effects that suppress regeneration completely or remove negative regen.
            if ((disableRegenBuff && __instance.HasBuff(disableRegenBuff)) || __instance.HasBuff(RoR2Content.Buffs.AffixLunar) ||
                (__instance.inventory && __instance.inventory.GetItemCountEffective(RoR2Content.Items.ShieldOnly) > 0)) return;
            AccessTools.PropertySetter(typeof(CharacterBody), "regen").Invoke(__instance, new object[] { __instance.regen + __state.Negative });
        }
        private static Exception RestoreRegen(CharacterBody __instance, RegenScope __state, Exception __exception)
        {
            if (__state != null) { __instance.baseRegen = __state.Base; __instance.levelRegen = __state.PerLevel; }
            return __exception;
        }

        private static void InstallProtection()
        {
            // The cost helper's internal selection closure also runs inside this scope.
            Type helper = typeof(CostTypeCatalog).GetNestedType("LunarItemOrEquipmentCostTypeHelper", BindingFlags.NonPublic);
            if (helper == null) throw new MissingMemberException("Lunar cleansing pool cost helper was not found.");
            lunarCostItems = AccessTools.Field(helper, "lunarItemIndices");
            if (lunarCostItems == null) throw new MissingFieldException(helper.FullName, "lunarItemIndices");
            Patch(helper, "IsAffordable", nameof(BeforeAffordable), finalizer: nameof(EndPoolSelection));
            Patch(helper, "PayCost", nameof(BeforePay), finalizer: nameof(EndPoolSelection));
            // Filter the mod's priority search as well as the game's final transformation selection.
            // Otherwise an excluded Heresy item could monopolise the highest priority category.
            Type ego = typeof(Heretic).Assembly.GetType("Tweaks.Items.Lunar.Egocentrism");
            MethodInfo priorityFilter = ego == null ? null : AccessTools.Method(ego, "BuildFilter");
            if (priorityFilter != null) hooks.Add(new ILHook(priorityFilter, EgoSelectionIL));
            Patch(typeof(Inventory), "TryTransformRandomItem", nameof(BeforeRandomTransform));
            Patch(typeof(Inventory.ItemTransformation), "CanTake", nameof(BeforeTake), arguments: new[] { typeof(Inventory), typeof(Inventory.ItemTransformation.CanTakeResult).MakeByRefType() });
            Patch(typeof(Inventory), "GiveItemPermanent", nameof(BeforeGive), arguments: new[] { typeof(ItemIndex), typeof(int) });
            Patch(typeof(Inventory), "ResetItemPermanent", nameof(BeforeReset), arguments: new[] { typeof(ItemIndex) });
            hooks.Add(new ILHook(AccessTools.Method(typeof(Inventory), "ShrineRestackInventory"), RestackIL));
        }
        private static PoolScope FilterPoolItems(Inventory inventory)
        {
            if (protectItems == null || !C.Get(protectItems) || !IsHeretic(inventory)) return null;
            ItemIndex[] original = lunarCostItems.GetValue(null) as ItemIndex[];
            if (original == null) return null;
            // Only this synchronous purchase sees the filtered selection; the catalogue and item tags stay intact.
            lunarCostItems.SetValue(null, original.Where(item => !IsHeresy(item)).ToArray());
            return new PoolScope { Previous = original };
        }
        private static void BeforeAffordable(CostTypeDef.IsAffordableContext context, out PoolScope __state)
        {
            CharacterBody body = context.activator ? context.activator.GetComponent<CharacterBody>() : null;
            __state = FilterPoolItems(body ? body.inventory : null);
        }
        private static void BeforePay(CostTypeDef.PayCostContext context, out PoolScope __state) =>
            __state = FilterPoolItems(context.activatorBody ? context.activatorBody.inventory : null);
        private static Exception EndPoolSelection(PoolScope __state, Exception __exception)
        {
            if (__state != null) lunarCostItems.SetValue(null, __state.Previous);
            return __exception;
        }
        private static int ConsumableCount(Inventory inventory, ItemIndex item) => Protected(inventory, item) ? 0 : inventory.GetItemCountPermanent(item);
        private static void EgoSelectionIL(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);
            int matches = 0;
            while (cursor.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt<Inventory>("GetItemCountPermanent")))
            {
                var method = cursor.Next.Operand as Mono.Cecil.MethodReference;
                if (method?.Parameters.Count != 1 || method.Parameters[0].ParameterType.FullName != typeof(ItemIndex).FullName) { cursor.Index++; continue; }
                cursor.Remove();
                cursor.EmitDelegate<Func<Inventory, ItemIndex, int>>(ConsumableCount);
                matches++;
            }
            if (matches == 0) throw new InvalidOperationException("Could not locate Egocentrism's consumption priority count.");
        }
        private static void BeforeRandomTransform(Inventory __instance, ref Inventory.TryTransformRandomItemArgs __0)
        {
            if (protectItems == null || !C.Get(protectItems) || !IsHeretic(__instance)) return;
            Inventory.TryTransformRandomItemArgs.FilterDelegate original = __0.filter;
            if (original != null) __0.filter = args => IsHeresy(args.itemIndex) ? ItemIndex.None : original(args);
        }
        private static bool BeforeTake(Inventory.ItemTransformation __instance, Inventory inventory,
            ref Inventory.ItemTransformation.CanTakeResult result, ref bool __result)
        {
            if (!Protected(inventory, __instance.originalItemIndex)) return true;
            result = default; __result = false; return false;
        }
        private static bool BeforeGive(Inventory __instance, ItemIndex __0, int __1) => __1 >= 0 || !Protected(__instance, __0);
        private static bool BeforeReset(Inventory __instance, ItemIndex __0) => !Protected(__instance, __0);
        private static bool CanRestack(bool original, Inventory inventory, ItemDef item) => original && !(item && Protected(inventory, item.itemIndex));
        private static void RestackIL(ILContext il)
        {
            ILCursor cursor = new ILCursor(il);
            int local = -1, matches = 0;
            while (cursor.TryGotoNext(MoveType.After, i => i.MatchLdloc(out local), i => i.OpCode == OpCodes.Ldc_I4 || i.OpCode == OpCodes.Ldc_I4_S,
                i => i.MatchCallOrCallvirt<ItemDef>("DoesNotContainTag")))
            {
                cursor.Emit(OpCodes.Ldarg_0);
                cursor.Emit(OpCodes.Ldloc, il.Body.Variables[local]);
                cursor.EmitDelegate<Func<bool, Inventory, ItemDef, bool>>(CanRestack);
                matches++;
            }
            if (matches == 0) throw new InvalidOperationException("Could not locate Shrine of Order item eligibility checks.");
        }

        private static void RemoveStackCooldown(GenericSkill slot, ItemIndex item, ref float interval)
        {
            if (Tweaks.Items.Lunar.HeresyItems.OwnsSkill(item == RoR2Content.Items.LunarPrimaryReplacement.itemIndex ? 0 : item == RoR2Content.Items.LunarSecondaryReplacement.itemIndex ? 1 : 3)) return;
            if (slot && NoPenalties(slot.characterBody) && slot.characterBody.inventory)
                interval /= Mathf.Max(1, slot.characterBody.inventory.GetItemCountEffective(item));
        }
        private static void PrimaryRecharge(GenericSkill skillSlot, ref float __result) => RemoveStackCooldown(skillSlot, RoR2Content.Items.LunarPrimaryReplacement.itemIndex, ref __result);
        private static void SecondaryRecharge(GenericSkill skillSlot, ref float __result) => RemoveStackCooldown(skillSlot, RoR2Content.Items.LunarSecondaryReplacement.itemIndex, ref __result);
        private static void SpecialRecharge(GenericSkill skillSlot, ref float __result) => RemoveStackCooldown(skillSlot, RoR2Content.Items.LunarSpecialReplacement.itemIndex, ref __result);
        private static void StartShadowfade(GhostUtilitySkillState __instance)
        {
            if (__instance.outer && NoPenalties(__instance.outer.GetComponent<CharacterBody>())) cancelStates.GetOrCreateValue(__instance).Released = false;
        }
        private static void UpdateShadowfade(GhostUtilitySkillState __instance)
        {
            if (!__instance.outer || !__instance.isAuthority || !NoPenalties(__instance.outer.GetComponent<CharacterBody>())) return;
            InputBankTest input = __instance.outer.GetComponent<InputBankTest>();
            if (!input) return;
            CancelData data = cancelStates.GetOrCreateValue(__instance);
            if (!input.skill3.down) data.Released = true;
            else if (data.Released && input.skill3.justPressed) __instance.outer.SetNextStateToMain();
        }
        private static void AddPassive()
        {
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            if (!locator) return;
            locator.passiveSkill.enabled = true;
            locator.passiveSkill.skillNameToken = "3QS_HERETIC_PASSIVE_NAME";
            locator.passiveSkill.skillDescriptionToken = "3QS_HERETIC_PASSIVE_DESCRIPTION";
            if (!locator.passiveSkill.icon)
            {
                ItemDef iconItem = RoR2Content.Items.LunarPrimaryReplacement;
                locator.passiveSkill.icon = iconItem.GetIcon();
                var iconReference = iconItem.GetIconReference();
                if (!locator.passiveSkill.icon && iconReference != null && iconReference.RuntimeKeyIsValid())
                {
                    // Keep the addressable loaded while this passive uses its sprite.
                    var iconHandle = Addressables.LoadAssetAsync<Sprite>(iconReference.RuntimeKey);
                    iconHandle.Completed += operation =>
                    {
                        if (locator && !locator.passiveSkill.icon && operation.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                            locator.passiveSkill.icon = operation.Result;
                        else
                            Addressables.Release(operation);
                    };
                }
            }
            S.SetText(locator.passiveSkill.skillNameToken, () => "Nevermore");
            S.SetText(locator.passiveSkill.skillDescriptionToken, PassiveDescription);
            if (moffeinInstalled)
            {
                // Some UI content retains Moffein's original tokens from before onLoad.
                S.SetText("HERETIC_DEFAULT_SKILL_NAME", () => "Nevermore");
                compatibilityDescriptions["MOFFEINHERETIC_PASSIVE_DESCRIPTION"] = PassiveDescription;
                compatibilityDescriptions["3QS_HERETIC_PASSIVE_DESCRIPTION"] = PassiveDescription;
                S.SetText("MOFFEINHERETIC_PASSIVE_DESCRIPTION", PassiveDescription);
            }
        }
        private static string CompatibilityLanguage(On.RoR2.Language.orig_GetLocalizedStringByToken orig, Language self, string token)
        {
            if (token != null && compatibilityDescriptions.TryGetValue(token, out Func<string> description)) return description();
            return orig(self, token);
        }
        private static void FixMoffeinRuinDescription()
        {
            if (Tweaks.Items.Lunar.HeresyItems.OwnsSkill(3)) return;
            const string moffeinToken = "MOFFEINHERETIC_SKILL_LUNAR_SPECIAL_REPLACEMENT_DESCRIPTION";
            // Register with our existing description lookup so UI caching and other language hooks
            // cannot bypass the fix. Keep the original wording and numbers.
            SkillDef skill = Addressables.LoadAssetAsync<SkillDef>("RoR2/Base/LunarSkillReplacements/LunarDetonatorSpecialReplacement.asset").WaitForCompletion();
            if (!skill) throw new InvalidOperationException("Ruin skill definition was not found.");
            string selectedToken = skill.skillDescriptionToken;
            string original = Language.GetString(selectedToken);
            if (string.IsNullOrEmpty(original) || original == selectedToken)
                original = Language.GetString(moffeinToken);
            if (string.IsNullOrEmpty(original) || original == moffeinToken) return;
            string corrected = ColourRuinDescription(original);
            foreach (string token in new[] { selectedToken, moffeinToken, "3QS_MOFFEIN_RUIN_DESCRIPTION" })
            {
                if (string.IsNullOrEmpty(token)) continue;
                compatibilityDescriptions[token] = () => corrected;
                S.SetText(token, () => corrected);
            }
            skill.skillDescriptionToken = "3QS_MOFFEIN_RUIN_DESCRIPTION";
        }
        private static string ColourRuinDescription(string text)
        {
            // Explicit colour avoids relying on the original mod's damage style resolving in this UI.
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"<style=cIsDamage>(.*?)</style>",
                match => "<color=#E5C962>" + match.Groups[1].Value + "</color>",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            if (text.IndexOf("<color=", StringComparison.Ordinal) >= 0) return text;
            return System.Text.RegularExpressions.Regex.Replace(text,
                @"detonates all Ruin stacks|Ruin|[0-9]+(?:[.,][0-9]+)?% damage",
                match => "<color=#E5C962>" + match.Value + "</color>");
        }
        private static void AddOverview()
        {
            SurvivorDef survivor = Addressables.LoadAssetAsync<SurvivorDef>("RoR2/Base/Heretic/Heretic.asset").WaitForCompletion();
            if (survivor) S.SetText(survivor.descriptionToken, Overview);
        }
        private static string Overview()
        {
            string text = "The Heretic is a mobile survivor who combines ranged attacks, crowd control, and devastating bursts of damage.";
            text += "\n\n<style=cSub>";
            text += "< ! > Use Hungering Gaze to pressure enemies from a distance, then detonate accumulated Ruin for a powerful finish.";
            text += "\n\n< ! > Slicing Maelstrom can hold groups in place, making them easier to strike with your other skills.";
            text += "\n\n< ! > Shadowfade lets you recover health and reposition safely. Save it for moments when you need breathing room.";
            if (prefab && prefab.baseJumpCount > 1)
                text += "\n\n< ! > Your extra jumps help you reach high ground and stay clear of danger.";
            if (prefab && prefab.baseRegen < 0f)
                text += "\n\n< ! > Your health drains over time. Keep moving and use Shadowfade to sustain yourself.";
            return text + "</style>";
        }
        private static string PassiveDescription()
        {
            string effects = PassiveEffects();
            if (!CompatibilityActive) return effects;
            string original = originalMoffeinPassive;
            if (string.IsNullOrEmpty(original) || original == "MOFFEINHERETIC_PASSIVE_DESCRIPTION")
                original = "The Heretic <style=cIsUtility>moves faster</style> and can <style=cIsUtility>flap her wings twice</style> for extra height while airborne.";
            return original + " " + effects;
        }
        private static string PassiveEffects()
        {
            List<string> lines = new List<string>();
            if (C.Get(protectItems)) lines.Add("<style=cIsUtility>Heresy items</style> cannot be removed.");
            if (C.Get(removePenalties)) lines.Add("Extra <style=cIsUtility>Heresy items</style> do not increase skill cooldowns. <style=cIsUtility>Shadowfade</style> can be cancelled by reactivating it.");
            return lines.Count == 0 ? "Wield the <style=cIsUtility>Heresy items</style>." : string.Join(" ", lines);
        }
    }
}
