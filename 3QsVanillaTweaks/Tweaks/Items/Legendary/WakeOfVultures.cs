using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using ThreeQsVanillaTweaks;
using C = Config.ConfigManager;
using Object = UnityEngine.Object;

namespace Tweaks.Items.Legendary
{
    internal static class WakeOfVultures
    {
        private const string Section = "Items - 03 Legendary - Wake of Vultures";
        private static ConfigEntry<bool> enabled, crafting;
        private static ConfigEntry<float> duration, durationStack, chance, chanceStack, eliteDamage, eliteDamageStack;
        private sealed class RecipeSettings
        {
            internal string Label, Equipment;
            internal ConfigEntry<bool> Enabled;
            internal ConfigEntry<string> Ingredients;
            internal ConfigEntry<int> WakeCount, OtherCount, Output;
        }

        private static readonly List<RecipeSettings> settings = new List<RecipeSettings>();
        private static readonly List<CraftableCatalog.RecipeEntry> owned = new List<CraftableCatalog.RecipeEntry>();
        private static readonly Dictionary<CraftableCatalog.RecipeEntry, RecipeSettings> recipeSettings = new Dictionary<CraftableCatalog.RecipeEntry, RecipeSettings>();
        private static readonly List<Object> assets = new List<Object>();
        private static bool Zet => Chainloader.PluginInfos.ContainsKey("com.TPDespair.ZetAspects");

        internal static void Init()
        {
            MigrateConfigSection();
            enabled = C.BindStartupBool(Section, "Enabled", true, "Enable configurable Wake of Vultures and aspect recipes. Restart to apply.");
            crafting = C.BindBool(Section, "Aspect Crafting", true, "Add Wake of Vultures aspect recipes at the Wandering Chef. Requires the Chef's expansion. Recipe ingredients and quantities are configurable below.");
            duration = Number("Elite Power Duration", 10f, "First-stack elite power duration in seconds. Vanilla: 8. Applies to ZetAspects Wake durations when its duration hook is available.");
            durationStack = Number("Elite Power Duration Per Additional Stack", 5f, "Extra duration per additional Wake. Vanilla: 5. Also applies to compatible ZetAspects Wake duration hooks.");
            chance = Number("Elite Power Trigger Chance (%)", 100f, "Chance to steal powers from a slain elite. Vanilla: 100%. Also applies to compatible ZetAspects Wake duration hooks.", 100f);
            chanceStack = Number("Elite Power Trigger Chance Per Additional Stack (%)", 0f, "Additional percentage points per extra Wake. Total capped at 100%. Vanilla: 0.", 100f);
            eliteDamage = Number("Bonus Elite Damage (%)", 30f, "Additional damage against elites. Vanilla: 0%.");
            eliteDamageStack = Number("Bonus Elite Damage Per Additional Stack (%)", 0f, "Additional elite damage per extra Wake. Vanilla: 0%.");
            if (enabled.Value)
            {
                Tweaks.Items.Uncommon.UncommonItemSupport.Ensure();
                Tweaks.Items.Uncommon.UncommonItemSupport.BeforeDamage += EliteDamage;
                WakeZetCompatibility.Install(() => Active, () => C.Get(duration), () => C.Get(durationStack));
            }

            Add("Aurelionite's Blessing", "EliteAurelioniteEquipment", "TitanGoldDuringTP,Pearl,ShinyPearl");
            Add("Her Biting Embrace", "EliteIceEquipment", "IceRing,Icicle");
            Add("His Reassurance", "EliteEarthEquipment", "IncreaseHealing,HealAndRevive,TPHealingNova");
            Add("His Spiteful Boon", "EliteBeadEquipment", "");
            Add("Ifrit's Distinction", "EliteFireEquipment", "FireRing,StrengthenBurn,Molotov");
            Add("N'kuhana's Rebirth", "EliteDeathEquipment", "@Hallowed");
            Add("N'kuhana's Retort", "ElitePoisonEquipment", "NovaOnHeal,@Hex");
            Add("Of One Mind", "EliteCollectiveEquipment", "DronesDropDynamite,PhysicsProjectile,RoboBallBuddy");
            Add("Shared Design", "EliteLunarEquipment", "@Lunar");
            Add("Silence Between Two Strikes", "EliteLightningEquipment", "Lightning,ShockNearby,ShockDamageAura");
            Add("Spectral Circlet", "EliteHauntedEquipment", "TeleportOnLowHealth,Phasing,PageTeamInvisibility");
            Add("Entropic Fracture", "EliteVoidEquipment", "@Void");
            if (!enabled.Value)
                return;
            var h = new Harmony("3Questionmarks.3QsVanillaTweaks.WakeOfVultures");
            // Resolve the item's current token at lookup time, including tokens
            // replaced later by ZetAspects or other integrations.
            h.Patch(AccessTools.Method(typeof(Language), "GetLocalizedStringByToken", new[] { typeof(string) }), postfix: new HarmonyMethod(typeof(WakeOfVultures), nameof(WakeText)) { priority = Priority.Last });
            if (!Zet)
            {
                h.Patch(AccessTools.Method(typeof(GlobalEventManager), "OnCharacterDeath"), transpiler: new HarmonyMethod(typeof(WakeOfVultures), nameof(WakeIL)));
                Tweaks.Survivors.SurvivorSettings.SetText("ITEM_HEADHUNTER_DESC", () => "Killing an elite has a <style=cIsUtility>" + C.Get(chance) + "%</style> <style=cStack>(+" + C.Get(chanceStack) + "% per stack)</style> chance to grant its powers for <style=cIsUtility>" + C.Get(duration) + "s</style> <style=cStack>(+" + C.Get(durationStack) + "s per stack)</style>.");
            }

            h.Patch(AccessTools.Method(typeof(CraftableCatalog), "FilterByEntitlement"), postfix: new HarmonyMethod(typeof(WakeOfVultures), nameof(FilterRecipes)));
            h.Patch(AccessTools.Method(typeof(CraftableCatalog.RecipeEntry), "ValidateSelection", new[] { typeof(Inventory) }), postfix: new HarmonyMethod(typeof(WakeOfVultures), nameof(CheckRecipeCost)));
            var meal = typeof(Run).Assembly.GetType("RoR2.MealPrepController");
            if (meal != null)
                h.Patch(AccessTools.Method(meal, "BeginCookingServer"), prefix: new HarmonyMethod(typeof(WakeOfVultures), nameof(BeforeCooking)) { priority = Priority.First });
            RoR2Application.onLoad += () =>
            {
                var item = RoR2Content.Items.HeadHunter;
                string native = item.descriptionToken;
                string pickup = item.pickupToken;
                string old = Language.GetString(native), oldPickup = Language.GetString(pickup);
                const string descriptionToken = "3QS_HEADHUNTER_DESC", pickupToken = "3QS_HEADHUNTER_PICKUP";
                Tweaks.Items.Common.CommonItemSupport.Text(descriptionToken, () => Active ? Description() : old);
                Tweaks.Items.Common.CommonItemSupport.Text(pickupToken, () => Active ? PickupDescription() : oldPickup);
                item.descriptionToken = descriptionToken;
                item.pickupToken = pickupToken;
                Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => item.itemIndex, (master, n, text) => "Bonus Elite Damage: <style=cIsDamage>" + (C.Get(eliteDamage) + (n - 1) * C.Get(eliteDamageStack)) + "%</style>\nElite Power Duration: <style=cIsUtility>" + (C.Get(duration) + (n - 1) * C.Get(durationStack)) + "s</style>", () => Active);
            };
            RoR2Application.onLoad += Rebuild;
            C.EffectiveSettingsChanged += Rebuild;
            Run.onRunStartGlobal += run => Rebuild();
        }

        private static string Value(float value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        private static string StackText(float amount, string unit) => amount == 0 ? "" : " <style=cStack>(+" + Value(amount) + unit + " per stack)</style>";
        private static string Description()
        {
            string damage = C.Get(eliteDamage) == 0 && C.Get(eliteDamageStack) == 0 ? "" : "Deal <style=cIsDamage>" + Value(C.Get(eliteDamage)) + "%</style>" + StackText(C.Get(eliteDamageStack), "%") + " more damage to <style=cIsDamage>elites</style>. ";
            string trigger = !Zet && C.Get(chance) < 100 ? "Killing an elite has a <style=cIsUtility>" + Value(C.Get(chance)) + "%</style>" + StackText(C.Get(chanceStack), "%") + " chance to grant its powers for " : "Killing an elite grants its powers for ";
            return damage + trigger + "<style=cIsUtility>" + Value(C.Get(duration)) + "s</style>" + StackText(C.Get(durationStack), "s") + ".";
        }

        private static string PickupDescription() => (C.Get(eliteDamage) == 0 && C.Get(eliteDamageStack) == 0 ? "" : "Deal more damage to elites. ") + "Steal their powers on kill.";
        private static void WakeText(string __0, ref string __result)
        {
            var item = RoR2Content.Items.HeadHunter;
            if (!Active || !item)
                return;
            if (__0 == item.descriptionToken || __0 == "ITEM_HEADHUNTER_DESC" || __0 == "3QS_HEADHUNTER_DESC")
                __result = Description();
            else if (__0 == item.pickupToken || __0 == "ITEM_HEADHUNTER_PICKUP" || __0 == "3QS_HEADHUNTER_PICKUP")
                __result = PickupDescription();
        }

        private static void MigrateConfigSection()
        {
            const string oldPrefix = "Items - Legendary - Wake of Vultures";
            var files = AccessTools.Field(typeof(C), "moduleFiles")?.GetValue(null) as Dictionary<string, ConfigFile>;
            if (files == null || !files.TryGetValue("Items", out var file))
                return;
            var property = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            var orphaned = property?.GetValue(file, null) as Dictionary<ConfigDefinition, string>;
            if (orphaned == null)
                return;
            foreach (var pair in orphaned.Where(p => p.Key.Section == oldPrefix || p.Key.Section.StartsWith(oldPrefix + " - ", StringComparison.Ordinal)).ToArray())
            {
                var target = new ConfigDefinition(Section + pair.Key.Section.Substring(oldPrefix.Length), pair.Key.Key);
                if (!orphaned.ContainsKey(target) && !file.ContainsKey(target))
                    orphaned[target] = pair.Value;
                orphaned.Remove(pair.Key);
            }
        }

        private static void EliteDamage(HealthComponent victim, DamageInfo info)
        {
            if (!Active || !victim.body || !victim.body.isElite || !info.attacker)
                return;
            var b = info.attacker.GetComponent<CharacterBody>();
            int n = b && b.inventory ? b.inventory.GetItemCountEffective(RoR2Content.Items.HeadHunter) : 0;
            if (n > 0)
                info.damage *= 1 + (C.Get(eliteDamage) + (n - 1) * C.Get(eliteDamageStack)) / 100f;
        }

        private static ConfigEntry<float> Number(string key, float value, string desc, float max = 100000f) => C.Bind(Section, key, value, new ConfigDescription(desc, new AcceptableValueRange<float>(0f, max)));
        internal static string SafeSectionName(string label)
        {
            // BepInEx rejects these characters even inside a human-readable section name.
            const string forbidden = "=\n\t\\\"'[]";
            return new string (label.Where(c => forbidden.IndexOf(c) < 0).ToArray());
        }

        private static void Add(string label, string equipment, string ingredients)
        {
            string section = Section + " - Crafting - " + SafeSectionName(label);
            settings.Add(new RecipeSettings { Label = label, Equipment = equipment, Enabled = C.BindBool(section, "Enabled", ingredients.Length > 0, "Enable this recipe. His Spiteful Boon is unassigned until a second ingredient is configured; Entropic Fracture requires ZetAspects."), Ingredients = C.Bind(section, "Alternative Ingredients", ingredients, "Comma-separated internal ItemDef or EquipmentDef names. Any ONE alternative is accepted. Categories: @Hallowed (Cursed tier), @Hex, @Lunar, @Void. Empty disables this recipe."), WakeCount = C.Bind(section, "Wake of Vultures Required", 1, new ConfigDescription("Copies of Wake consumed per craft.", new AcceptableValueRange<int>(1, 10))), OtherCount = C.Bind(section, "Other Ingredient Required", 1, new ConfigDescription("Copies of ONE chosen alternative consumed per craft. Equipment alternatives require this to be 1.", new AcceptableValueRange<int>(1, 10))), Output = C.Bind(section, "Aspects Produced", 1, new ConfigDescription("Number of resulting aspects.", new AcceptableValueRange<int>(1, 100))) });
        }

        private static bool Active => enabled != null && C.Get(enabled);

        private static int CountForEffect(int count, DamageReport report)
        {
            if (!Active || count <= 0)
                return count;
            float percent = Mathf.Min(100f, C.Get(chance) + (count - 1) * C.Get(chanceStack));
            return Util.CheckRoll(percent, report.attackerMaster) ? count : 0;
        }

        private static float Duration(float original, int count) => Active ? C.Get(duration) + (count - 1) * C.Get(durationStack) : original;
        private static IEnumerable<CodeInstruction> WakeIL(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            int anchor = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "HeadHunter");
            if (anchor < 0)
                throw new InvalidOperationException("Wake item anchor missing.");
            int call = anchor + 1;
            while (call < list.Count && !list[call].Calls(AccessTools.Method(typeof(Inventory), "GetItemCountEffective", new[] { typeof(ItemDef) })))
                call++;
            if (call >= list.Count)
                throw new InvalidOperationException("Wake item count call missing.");
            var store = list[call + 1];
            var load = new CodeInstruction(store.opcode == OpCodes.Stloc_0 ? OpCodes.Ldloc_0 : store.opcode == OpCodes.Stloc_1 ? OpCodes.Ldloc_1 : store.opcode == OpCodes.Stloc_2 ? OpCodes.Ldloc_2 : store.opcode == OpCodes.Stloc_3 ? OpCodes.Ldloc_3 : store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, store.operand);
            list.InsertRange(call + 1, new[] { new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WakeOfVultures), nameof(CountForEffect))) });
            int durationIndex = -1;
            for (int i = call + 4; i < Math.Min(call + 50, list.Count - 5); i++)
                if (list[i].LoadsConstant(3f) && list[i + 1].LoadsConstant(5f) && list[i + 5].opcode == OpCodes.Add)
                {
                    durationIndex = i + 5;
                    break;
                }

            if (durationIndex < 0)
                throw new InvalidOperationException("Wake duration expression missing; refusing a partial patch.");
            list.InsertRange(durationIndex + 1, new[] { load, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WakeOfVultures), nameof(Duration))) });
            return list;
        }

        private static readonly Dictionary<string, string[]> Categories = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            {
                "@Hallowed",
                new[]
                {
                    "Cursed"
                }
            },
            {
                "@Hex",
                new[]
                {
                    "Hex"
                }
            },
            {
                "@Lunar",
                new[]
                {
                    "Lunar"
                }
            },
            {
                "@Void",
                new[]
                {
                    "VoidTier1",
                    "VoidTier2",
                    "VoidTier3",
                    "VoidBoss"
                }
            }
        };
        private static List<PickupIndex> Ingredients(string specification)
        {
            var output = new HashSet<PickupIndex>();
            foreach (string raw in specification.Split(','))
            {
                string token = raw.Trim();
                if (token.Length == 0)
                    continue;
                if (Categories.TryGetValue(token, out var tiers))
                {
                    foreach (var index in ItemCatalog.allItems)
                    {
                        var item = ItemCatalog.GetItemDef(index);
                        if (item && !item.hidden && tiers.Contains(item.tier.ToString()))
                            output.Add(PickupCatalog.FindPickupIndex(index));
                    }

                    continue;
                }

                ItemIndex itemIndex = ItemCatalog.FindItemIndex(token);
                if (itemIndex != ItemIndex.None)
                {
                    output.Add(PickupCatalog.FindPickupIndex(itemIndex));
                    continue;
                }

                EquipmentIndex equipment = EquipmentCatalog.FindEquipmentIndex(token);
                if (equipment != EquipmentIndex.None)
                {
                    output.Add(PickupCatalog.FindPickupIndex(equipment));
                    continue;
                }

                Log.Warning("Aspect crafting ingredient unavailable: " + token);
            }

            output.Remove(PickupIndex.none);
            return output.OrderBy(x => x.value).ToList();
        }

        private static PickupIndex Result(EquipmentDef equipment)
        {
            if (Zet)
            {
                var plugin = Chainloader.PluginInfos["com.TPDespair.ZetAspects"].Instance;
                var assembly = plugin.GetType().Assembly;
                var hooks = assembly.GetType("TPDespair.ZetAspects.DropHooks");
                var can = hooks == null ? null : AccessTools.Method(hooks, "CanObtainItem");
                var equipmentMode = hooks == null ? null : AccessTools.Method(hooks, "CanObtainEquipment");
                if (equipmentMode != null && (bool)equipmentMode.Invoke(null, null))
                    return PickupCatalog.FindPickupIndex(equipment.equipmentIndex);
                var catalog = assembly.GetType("TPDespair.ZetAspects.Catalog");
                var method = catalog == null ? null : AccessTools.Method(catalog, "ItemizeEliteEquipment");
                if (can != null && method != null && (bool)can.Invoke(null, null))
                {
                    var item = (ItemIndex)method.Invoke(null, new object[] { equipment.equipmentIndex });
                    if (item != ItemIndex.None && ItemCatalog.GetItemDef(item) && !ItemCatalog.GetItemDef(item).hidden)
                        return PickupCatalog.FindPickupIndex(item);
                }
            }

            return PickupCatalog.FindPickupIndex(equipment.equipmentIndex);
        }

        private static void Rebuild()
        {
            if (!CraftableCatalog.availability.available)
                return;
            RemoveOwned();
            if (!Active || !C.Get(crafting))
                return;
            var all = (List<CraftableCatalog.RecipeEntry>)AccessTools.Field(typeof(CraftableCatalog), "allRecipes").GetValue(null);
            var wake = RoR2Content.Items.HeadHunter;
            if (!wake)
                return;
            int total = 0;
            foreach (var setting in settings)
            {
                if (!C.Get(setting.Enabled) || setting.Equipment == "EliteVoidEquipment" && !Zet)
                    continue;
                var equipment = EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(setting.Equipment));
                if (!equipment)
                {
                    Log.Warning("Aspect crafting result unavailable: " + setting.Equipment);
                    continue;
                }

                foreach (var other in Ingredients(C.Get(setting.Ingredients)))
                {
                    if (PickupCatalog.GetPickupDef(other).equipmentIndex != EquipmentIndex.None && C.Get(setting.OtherCount) != 1)
                        continue;
                    var result = Result(equipment);
                    var inputs = new[]
                    {
                        PickupCatalog.FindPickupIndex(wake.itemIndex),
                        other
                    };
                    if (all.Any(e => e.result == result && e.amountToDrop == C.Get(setting.Output) && e.possibleIngredients.Length == inputs.Length && e.possibleIngredients.SelectMany(x => x.pickups).OrderBy(x => x.value).SequenceEqual(inputs.OrderBy(x => x.value))))
                        continue;
                    var def = ScriptableObject.CreateInstance<CraftableDef>();
                    def.name = "3Qs_Aspect_" + setting.Equipment + "_" + other.value;
                    def.pickup = PickupAsset(result);
                    var recipe = new Recipe
                    {
                        amountToDrop = C.Get(setting.Output),
                        priority = 100,
                        craftableDef = def,
                        indexInCraftableDef = 0,
                        ingredients = inputs.Select(x => new RecipeIngredient { type = IngredientTypeIndex.AssetReference, pickupIndex = x, pickup = PickupAsset(x), requiredTags = Array.Empty<ItemTag>(), forbiddenTags = Array.Empty<ItemTag>() }).ToArray()
                    };
                    def.recipes = new[]
                    {
                        recipe
                    };
                    assets.Add(def);
                    var entry = new CraftableCatalog.RecipeEntry
                    {
                        recipe = recipe,
                        result = result,
                        amountToDrop = recipe.amountToDrop,
                        possibleIngredients = inputs.Select((x, i) => new CraftableCatalog.IngredientSlotEntry(i) { type = IngredientTypeIndex.AssetReference, pickups = new[] { x } }).ToArray()
                    };
                    all.Add(entry);
                    owned.Add(entry);
                    recipeSettings[entry] = setting;
                    Index(CraftableCatalog.resultToRecipeSearchTable, result, entry);
                    foreach (var input in inputs.Distinct())
                        Index(CraftableCatalog.pickupToIngredientSearchTable, input, entry);
                    total++;
                }
            }

            Log.Info("Wake of Vultures: registered " + total + " aspect recipes" + (Zet ? " with ZetAspects output handling." : "."));
        }

        private static void Index(Dictionary<PickupIndex, List<CraftableCatalog.RecipeEntry>> table, PickupIndex key, CraftableCatalog.RecipeEntry entry)
        {
            if (!table.TryGetValue(key, out var list))
                table[key] = list = new List<CraftableCatalog.RecipeEntry>();
            list.Add(entry);
        }

        private static void RemoveOwned()
        {
            var all = AccessTools.Field(typeof(CraftableCatalog), "allRecipes").GetValue(null) as List<CraftableCatalog.RecipeEntry>;
            foreach (var entry in owned)
            {
                all?.Remove(entry);
                foreach (var table in new[]
                {
                    CraftableCatalog.resultToRecipeSearchTable,
                    CraftableCatalog.pickupToIngredientSearchTable
                }

                )
                    foreach (var key in table.Keys.ToArray())
                    {
                        table[key].Remove(entry);
                        if (table[key].Count == 0)
                            table.Remove(key);
                    }
            }

            owned.Clear();
            recipeSettings.Clear();
            foreach (var asset in assets)
                if (asset)
                    Object.Destroy(asset);
            assets.Clear();
        }

        private static PickupIndex[] ExpandedCost(CraftableCatalog.RecipeEntry entry, RecipeSettings setting)
        {
            return Enumerable.Repeat(entry.possibleIngredients[0].pickups[0], C.Get(setting.WakeCount)).Concat(Enumerable.Repeat(entry.possibleIngredients[1].pickups[0], C.Get(setting.OtherCount))).ToArray();
        }

        private static bool Affordable(Inventory inventory, PickupIndex[] cost)
        {
            if (!inventory)
                return false;
            foreach (var group in cost.GroupBy(x => x))
            {
                var pickup = PickupCatalog.GetPickupDef(group.Key);
                if (pickup.itemIndex != ItemIndex.None)
                {
                    if (inventory.GetItemCountPermanent(pickup.itemIndex) < group.Count())
                        return false;
                }
                else if (group.Count() != 1 || !inventory.HasEquipment(pickup.equipmentIndex))
                    return false;
            }

            return true;
        }

        private static void CheckRecipeCost(CraftableCatalog.RecipeEntry __instance, Inventory inventory, ref bool __result)
        {
            if (__result && recipeSettings.TryGetValue(__instance, out var setting))
                __result = Affordable(inventory, ExpandedCost(__instance, setting));
        }

        private static bool BeforeCooking(Interactor activator, ref PickupIndex[] itemsToTake, PickupIndex reward, int count)
        {
            if (itemsToTake == null || itemsToTake.Length != 2)
                return true;
            foreach (var entry in owned)
                if (entry.result == reward && entry.amountToDrop == count && CraftableCatalog.ValidateRecipe(entry, itemsToTake))
                {
                    var body = activator ? activator.GetComponent<CharacterBody>() : null;
                    var cost = ExpandedCost(entry, recipeSettings[entry]);
                    if (!body || !Active || !C.Get(crafting) || !Affordable(body.inventory, cost))
                        return false;
                    itemsToTake = cost;
                    return true;
                }

            return true;
        }

        private static Object PickupAsset(PickupIndex index)
        {
            var pickup = PickupCatalog.GetPickupDef(index);
            if (pickup == null)
                return null;
            return pickup.itemIndex != ItemIndex.None ? (Object)ItemCatalog.GetItemDef(pickup.itemIndex) : EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
        }

        private static bool Allowed(PickupIndex index)
        {
            var pickup = PickupCatalog.GetPickupDef(index);
            if (pickup == null)
                return false;
            if (pickup.itemIndex != ItemIndex.None)
                return Tweaks.SpawnRequirements.ItemAllowed(ItemCatalog.GetItemDef(pickup.itemIndex));
            var e = EquipmentCatalog.GetEquipmentDef(pickup.equipmentIndex);
            return e && (!e.requiredExpansion || Run.instance.IsExpansionEnabled(e.requiredExpansion));
        }

        private static void FilterRecipes(ref CraftableCatalog.RecipeEntry[] source)
        {
            if (source == null || !Run.instance)
                return;
            source = source.Where(e => !owned.Contains(e) || Active && C.Get(crafting) && Tweaks.SpawnRequirements.ExpansionEnabled("DLC3") && Allowed(e.result) && e.possibleIngredients.All(s => s.pickups.Any(Allowed))).ToArray();
        }
    }
}
