using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using R2API;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;
using Tweaks.ModCompatibility;

namespace Tweaks.Items.Lunar
{
    internal static class Egocentrism
    {
        private const string Section = "Items - 05 Lunar - Egocentrism";
        private const string DefaultOrder = "White Scrap, Green Scrap, Yellow Scrap, Red Scrap, Lunar Items, Other Items";
        private const string ValidEntries =
            "Scrap, White Scrap, Green Scrap, Yellow Scrap, Red Scrap, Normal Items, " +
            "Common Items, Uncommon Items, Legendary Items, Boss Items, Void Items, " +
            "Void Common Items, Void Uncommon Items, Void Legendary Items, Void Boss Items, " +
            "Lunar Items, Other Items";

        private static readonly HashSet<string> Valid = new HashSet<string>(
            ValidEntries.Split(',').Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> interval;
        private static ConfigEntry<float> perStack;
        private static ConfigEntry<float> minimum;
        private static ConfigEntry<string> priority;
        private static ConfigEntry<float> orbBaseDamage;
        private static ConfigEntry<float> orbDamagePerStack;
        private static ConfigEntry<float> orbSpawnTime;
        private static ConfigEntry<float> orbSpawnRatePerStack;
        private static ConfigEntry<bool> pauseConsumption;
        private static readonly PropertyInfo stopwatchPausedProperty = typeof(Run).GetProperty(
            "isRunStopwatchPaused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo stopwatchPausedField = typeof(Run).GetField(
            "isRunStopwatchPaused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo stopwatchPausedMethod = typeof(Run).GetMethod(
            "IsRunStopwatchPaused", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        private static readonly Dictionary<string, ConfigEntry<bool>> permissions =
            new Dictionary<string, ConfigEntry<bool>>(StringComparer.OrdinalIgnoreCase);
        private static string[] order;
        private static ILHook timerHook;
        private static ILHook selectionHook;
        private const string PickupToken = "ITEM_LUNARSUN_PICKUP";
        private const string DescriptionToken = "ITEM_LUNARSUN_DESC";

        internal static void Init()
        {
            enabled = ModConfig.Bind(Section, "Enabled", true,
                "Enable custom Egocentrism consumption priority, permissions, and timing.");
            interval = ModConfig.Bind(Section, "Consumption Interval", 120f,
                new ConfigDescription("Seconds between consumptions for one Egocentrism. Supports decimals.",
                    new AcceptableValueRange<float>(1f, 100000f)));
            perStack = ModConfig.Bind(Section, "Interval Change Per Stack", -1f,
                "Seconds added for each additional Egocentrism. Negative is faster. Supports decimals.");
            minimum = ModConfig.Bind(Section, "Minimum Consumption Interval", 10f,
                new ConfigDescription("Minimum seconds between consumptions, regardless of stacks.",
                    new AcceptableValueRange<float>(1f, 100000f)));
            orbBaseDamage = ModConfig.Bind(Section, "Orb Base Damage (%)", 360f,
                "Orb damage as a percentage of character damage, with one Egocentrism. Vanilla: 360%.");
            orbDamagePerStack = ModConfig.Bind(Section, "Orb Damage Per Stack (%)", 0f,
                "Additional percentage points of orb damage per Egocentrism after the first. Vanilla: 0%.");
            orbSpawnTime = ModConfig.Bind(Section, "Orb Spawn Time", 3f,
                new ConfigDescription("Seconds between spawning orbiting bombs at one stack. Vanilla: 3 seconds.",
                    new AcceptableValueRange<float>(0.01f, 100000f)));
            orbSpawnRatePerStack = ModConfig.Bind(Section, "Orb Spawn Time Scaling Per Stack (%)", 100f,
                new ConfigDescription("Percent increase in orb spawn RATE per extra stack. 100% gives vanilla 3 / stack count; 0% keeps a fixed interval. Formula: spawn time / (1 + extra stacks * percent / 100).",
                    new AcceptableValueRange<float>(0f, 100000f)));
            pauseConsumption = ModConfig.Bind(Section, "Pause Consumption When Run Timer Paused", true,
                "Stop advancing the item-consumption timer when the run stopwatch is paused (such as in the Bazaar).");
            priority = ModConfig.Bind(Section, "Consumption Priority", DefaultOrder,
                "Comma-separated categories, highest priority first. Valid entries (case-insensitive): " +
                ValidEntries + ". Unlisted categories are considered only by Other Items. " +
                "ANY invalid or duplicate entry reverts the ENTIRE list to: " + DefaultOrder + ".");

            AddPermission("White Scrap", true);
            AddPermission("Green Scrap", true);
            AddPermission("Yellow Scrap", true);
            AddPermission("Red Scrap", true);
            AddPermission("Common Items", true);
            AddPermission("Uncommon Items", true);
            AddPermission("Legendary Items", true);
            AddPermission("Boss Items", true);
            AddPermission("Lunar Items", true);
            AddPermission("Void Common Items", false);
            AddPermission("Void Uncommon Items", false);
            AddPermission("Void Legendary Items", false);
            AddPermission("Void Boss Items", false);

            RefreshOrder();
            priority.SettingChanged += (sender, args) => { RefreshOrder(); UpdateDescription(); };
            enabled.SettingChanged += (sender, args) => UpdateDescription();
            interval.SettingChanged += (sender, args) => UpdateDescription();
            perStack.SettingChanged += (sender, args) => UpdateDescription();
            minimum.SettingChanged += (sender, args) => UpdateDescription();
            orbBaseDamage.SettingChanged += (sender, args) => UpdateDescription();
            orbDamagePerStack.SettingChanged += (sender, args) => UpdateDescription();
            orbSpawnTime.SettingChanged += (sender, args) => UpdateDescription();
            orbSpawnRatePerStack.SettingChanged += (sender, args) => UpdateDescription();
            UpdateDescription();
            ModConfig.EffectiveSettingsChanged += () => { RefreshOrder(); UpdateDescription(); };

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            MethodInfo fixedUpdate = typeof(LunarSunBehavior).GetMethod("FixedUpdate", flags);
            MethodInfo transformItem = typeof(LunarSunBehavior).GetMethod("TransformItem", flags);
            if (fixedUpdate == null || transformItem == null)
                throw new MissingMethodException("LunarSunBehavior.FixedUpdate or TransformItem not found.");

            // Apply both patches as a unit; don't leave a partially active tweak.
            try
            {
                selectionHook = new ILHook(transformItem, PatchSelection);
                timerHook = new ILHook(fixedUpdate, PatchTimer);
            }
            catch
            {
                timerHook?.Dispose();
                timerHook = null;
                selectionHook?.Dispose();
                selectionHook = null;
                throw;
            }
            LookingGlassStatRegistry.Register(() => DLC1Content.Items.LunarSun != null ? DLC1Content.Items.LunarSun.itemIndex : ItemIndex.None, "Orb Cooldown:", FormatLookingGlass);
            UnityEngine.Debug.Log("[3QsVanillaTweaks] Egocentrism: selection and timer hooks installed.");
        }

        private static string FormatLookingGlass(int stacks, string text)
        {
            if (!ModConfig.Get(enabled)) return text;
            float conversion = Mathf.Max(1f, Mathf.Max(ModConfig.Get(minimum),
                ModConfig.Get(interval) + (stacks - 1) * ModConfig.Get(perStack)));
            float rate = Mathf.Max(0.0001f, 1f + (stacks - 1) * ModConfig.Get(orbSpawnRatePerStack) / 100f);
            float cooldown = ModConfig.Get(orbSpawnTime) / rate;
            float damage = ModConfig.Get(orbBaseDamage) + (stacks - 1) * ModConfig.Get(orbDamagePerStack);

            text = LookingGlassStatRegistry.ReplaceValue(text, "Orb Cooldown:", cooldown, "#80D8FF");
            text = LookingGlassStatRegistry.AddValue(text, "Orb Damage", damage, "%", "#E5C962");
            return LookingGlassStatRegistry.AddValue(text, "Item Conversion", conversion, " seconds", "#80D8FF");
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Signed(float value) => value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);

        private static void UpdateDescription()
        {
            // The vanilla orb rate is 3 / stack count. Preserve this exactly with
            // the default 100% rate increase per additional stack.
            string vanillaOrb =
                "Every <style=cIsUtility>3</style> <style=cStack>(-50% per stack)</style> seconds, " +
                "gain an <style=cIsDamage>orbiting bomb</style> that detonates on impact for " +
                "<style=cIsDamage>360%</style> damage, up to a maximum of " +
                "<style=cIsUtility>3 bombs</style> <style=cStack>(+1 per stack)</style>. ";
            if (!ModConfig.Get(enabled))
            {
                LanguageAPI.Add(PickupToken,
                    "Gain multiple orbiting bombs. <style=cIsHealth>Every minute, assimilate another item into Egocentrism.</style>");
                LanguageAPI.Add(DescriptionToken, vanillaOrb +
                    "Every <style=cIsUtility>60</style> seconds, a random item is " +
                    "<style=cIsUtility>converted</style> into this item.");
                return;
            }

            string orbStack = Mathf.Approximately(ModConfig.Get(orbSpawnRatePerStack), 100f)
                ? "(+100% spawn rate per stack)"
                : "(+" + Format(ModConfig.Get(orbSpawnRatePerStack)) + "% spawn rate per stack)";
            string damageStack = Mathf.Approximately(ModConfig.Get(orbDamagePerStack), 0f)
                ? "" : " <style=cStack>(" + Signed(ModConfig.Get(orbDamagePerStack)) + "% per stack)</style>";
            string orb = "Every <style=cIsUtility>" + Format(ModConfig.Get(orbSpawnTime)) + "</style> " +
                " seconds <style=cStack>" + orbStack + "</style>, gain an " +
                "<style=cIsDamage>orbiting bomb</style> that detonates on impact for " +
                "<style=cIsDamage>" + Format(ModConfig.Get(orbBaseDamage)) + "%</style> damage" + damageStack +
                ", up to a maximum of <style=cIsUtility>3 bombs</style> " +
                "<style=cStack>(+1 per stack)</style>. ";

            string[] allowed = permissions.Where(pair => ModConfig.Get(pair.Value)).Select(pair => pair.Key).ToArray();
            string allowedText = allowed.Length > 0 ? string.Join(", ", allowed) : "None";
            LanguageAPI.Add(PickupToken,
                "Gain multiple orbiting bombs. <style=cIsHealth>Periodically assimilate another item into Egocentrism.</style>");
            LanguageAPI.Add(DescriptionToken,
                orb + "Every <style=cIsUtility>" + Format(ModConfig.Get(interval)) + "</style> seconds " +
                "<style=cStack>(" + Signed(ModConfig.Get(perStack)) + " " +
                (Mathf.Approximately(Mathf.Abs(ModConfig.Get(perStack)), 1f) ? "second" : "seconds") +
                " per stack, minimum " + Format(Mathf.Max(1f, ModConfig.Get(minimum))) + ")</style>, " +
                "a random eligible item is <style=cIsUtility>converted</style> into this item. " +
                "<style=cStack>(Allowed items: " + allowedText + ")</style>");
        }

        private static void AddPermission(string name, bool defaultValue)
        {
            ConfigEntry<bool> entry = ModConfig.Bind(Section, "Allow " + name, defaultValue,
                "Allow Egocentrism to consume " + name.ToLowerInvariant() + ".");
            permissions[name] = entry;
            entry.SettingChanged += (sender, args) => UpdateDescription();
        }

        private static void RefreshOrder()
        {
            string[] entries = ModConfig.Get(priority).Split(',').Select(s => s.Trim()).ToArray();
            if (entries.Length == 0 || entries.Any(s => !Valid.Contains(s)) ||
                entries.Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
            {
                order = DefaultOrder.Split(',').Select(s => s.Trim()).ToArray();
                // Do not overwrite the user's input; log it so they can correct it.
                UnityEngine.Debug.LogWarning("[3QsVanillaTweaks] Invalid or duplicate Egocentrism Consumption Priority; using default order: " + DefaultOrder);
            }
            else
            {
                order = entries;
            }
        }

        private static void PatchTimer(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (!c.TryGotoNext(MoveType.Before, i => i.MatchLdsfld<LunarSunBehavior>("secondsPerTransform")))
                throw new InvalidOperationException("Could not locate Egocentrism consumption timer constant.");
            c.Index++;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<float, LunarSunBehavior, float>>(GetInterval);

            // Change only the consumption timer's deltaTime, not the orb timer's.
            c.Index = 0;
            if (!c.TryGotoNext(MoveType.After,
                i => i.MatchLdfld<LunarSunBehavior>("transformTimer"),
                i => i.MatchCall(typeof(Time), "get_fixedDeltaTime")))
                throw new InvalidOperationException("Could not locate consumption timer deltaTime.");
            c.EmitDelegate<Func<float, float>>(ConsumptionDeltaTime);

            // Projectile interval: vanilla compares timer to secondsPerProjectile / stack.
            // Feed a stack-compensated value so vanilla's division stays intact.
            c.Index = 0;
            if (!c.TryGotoNext(MoveType.After, i => i.MatchLdsfld<LunarSunBehavior>("secondsPerProjectile")))
                throw new InvalidOperationException("Could not locate orb spawn interval.");
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<float, LunarSunBehavior, float>>(GetOrbIntervalNumerator);

            c.Index = 0;
            if (!c.TryGotoNext(MoveType.After, i => i.MatchLdsfld<LunarSunBehavior>("baseDamageCoefficient")))
                throw new InvalidOperationException("Could not locate orb damage coefficient.");
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<float, LunarSunBehavior, float>>(GetOrbDamageCoefficient);
        }

        private static float ConsumptionDeltaTime(float originalDelta)
        {
            if (!ModConfig.Get(enabled) || !ModConfig.Get(pauseConsumption) || !Run.instance)
                return originalDelta;
            try
            {
                object run = Run.instance;
                bool paused = stopwatchPausedProperty != null
                    ? (bool)stopwatchPausedProperty.GetValue(run, null)
                    : stopwatchPausedField != null
                        ? (bool)stopwatchPausedField.GetValue(run)
                        : stopwatchPausedMethod != null && (bool)stopwatchPausedMethod.Invoke(run, null);
                return paused ? 0f : originalDelta;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[3QsVanillaTweaks] Could not read run stopwatch pause state: " + ex.Message);
                return originalDelta;
            }
        }

        private static float GetOrbIntervalNumerator(float vanillaSeconds, LunarSunBehavior behavior)
        {
            if (!ModConfig.Get(enabled)) return vanillaSeconds;
            int stacks = Math.Max(1, behavior.stack);
            float divisor = 1f + (stacks - 1) * ModConfig.Get(orbSpawnRatePerStack) / 100f;
            return ModConfig.Get(orbSpawnTime) * stacks / Mathf.Max(0.0001f, divisor);
        }

        private static float GetOrbDamageCoefficient(float vanillaCoefficient, LunarSunBehavior behavior)
        {
            if (!ModConfig.Get(enabled)) return vanillaCoefficient;
            int stacks = Math.Max(1, behavior.stack);
            return (ModConfig.Get(orbBaseDamage) + (stacks - 1) * ModConfig.Get(orbDamagePerStack)) / 100f;
        }

        private static float GetInterval(float vanillaInterval, LunarSunBehavior behavior)
        {
            if (!ModConfig.Get(enabled)) return vanillaInterval;
            int stacks = Math.Max(1, behavior.stack);
            float value = ModConfig.Get(interval) + (stacks - 1) * ModConfig.Get(perStack);
            if (float.IsNaN(value)) value = ModConfig.Get(interval);
            return Mathf.Max(1f, ModConfig.Get(minimum), value);
        }

        private static void PatchSelection(ILContext il)
        {
            ILCursor c = new ILCursor(il);
            if (!c.TryGotoNext(MoveType.Before, i => i.MatchLdsfld<LunarSunBehavior>("transformationFilter")))
                throw new InvalidOperationException("Could not locate Egocentrism item selection filter.");
            // Preserve the vanilla filter on the stack, then optionally replace it.
            // This leaves the method's original argument stack intact.
            c.Index++;
            c.Emit(OpCodes.Ldarg_0);
            c.EmitDelegate<Func<Inventory.TryTransformRandomItemArgs.FilterDelegate, LunarSunBehavior, Inventory.TryTransformRandomItemArgs.FilterDelegate>>(BuildFilter);
        }

        private static Inventory.TryTransformRandomItemArgs.FilterDelegate BuildFilter(
            Inventory.TryTransformRandomItemArgs.FilterDelegate vanillaFilter, LunarSunBehavior behavior)
        {
            if (!ModConfig.Get(enabled)) return vanillaFilter;
            CharacterBody body = behavior.GetComponent<CharacterBody>();
            if (!body) body = behavior.GetComponentInParent<CharacterBody>();
            Inventory inventory = body ? body.inventory : null;
            if (inventory == null) return vanillaFilter;

            string[] activeOrder = order;
            int best = int.MaxValue;
            // Determine the first priority group with any eligible items. The game's
            // own TryTransformRandomItem then randomly selects among that group.
            for (int i = 0; i < (int)ItemCatalog.itemCount; i++)
            {
                ItemIndex index = (ItemIndex)i;
                if (inventory.GetItemCountPermanent(index) <= 0) continue;
                ItemDef def = ItemCatalog.GetItemDef(index);
                if (!IsAllowed(def)) continue;
                int rank = Rank(def, activeOrder);
                if (rank < best) best = rank;
            }

            int selectedRank = best;
            return args =>
            {
                ItemDef def = ItemCatalog.GetItemDef(args.itemIndex);
                return selectedRank != int.MaxValue && IsAllowed(def) && Rank(def, activeOrder) == selectedRank
                    ? DLC1Content.Items.LunarSun.itemIndex
                    : ItemIndex.None;
            };
        }

        private static bool IsAllowed(ItemDef def)
        {
            if (def == null || !def.canRemove || def.itemIndex == DLC1Content.Items.LunarSun.itemIndex)
                return false;
            string category = Category(def);
            return category != null && permissions.TryGetValue(category, out ConfigEntry<bool> permission) && ModConfig.Get(permission);
        }

        private static int Rank(ItemDef def, string[] activeOrder)
        {
            string category = Category(def);
            if (category == null) return int.MaxValue;
            for (int i = 0; i < activeOrder.Length; i++)
            {
                string entry = activeOrder[i];
                if (entry.Equals("Other Items", StringComparison.OrdinalIgnoreCase) ||
                    entry.Equals(category, StringComparison.OrdinalIgnoreCase) ||
                    (entry.Equals("Scrap", StringComparison.OrdinalIgnoreCase) && category.EndsWith(" Scrap", StringComparison.OrdinalIgnoreCase)) ||
                    (entry.Equals("Normal Items", StringComparison.OrdinalIgnoreCase) && IsNormal(category)) ||
                    (entry.Equals("Void Items", StringComparison.OrdinalIgnoreCase) && category.StartsWith("Void ", StringComparison.OrdinalIgnoreCase)))
                    return i;
            }
            return int.MaxValue;
        }

        private static bool IsNormal(string category)
        {
            return category == "Common Items" || category == "Uncommon Items" ||
                   category == "Legendary Items" || category == "Boss Items";
        }

        private static string Category(ItemDef def)
        {
            // Check scrap before tier: scrap has its own priority and permission group.
            string name = def.name ?? "";
            if (name == "ScrapWhite") return "White Scrap";
            if (name == "ScrapGreen" || name == "RegeneratingScrap") return "Green Scrap";
            if (name == "ScrapYellow") return "Yellow Scrap";
            if (name == "ScrapRed") return "Red Scrap";

            switch (def.tier)
            {
                case ItemTier.Tier1: return "Common Items";
                case ItemTier.Tier2: return "Uncommon Items";
                case ItemTier.Tier3: return "Legendary Items";
                case ItemTier.Boss: return "Boss Items";
                case ItemTier.Lunar: return "Lunar Items";
                case ItemTier.VoidTier1: return "Void Common Items";
                case ItemTier.VoidTier2: return "Void Uncommon Items";
                case ItemTier.VoidTier3: return "Void Legendary Items";
                case ItemTier.VoidBoss: return "Void Boss Items";
                default: return null; // Excludes special/no-tier items such as artifact keys.
            }
        }
    }
}
