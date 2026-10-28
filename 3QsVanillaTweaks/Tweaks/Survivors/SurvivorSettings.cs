using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.Survivors
{
    // Shared survivor settings and descriptions.
    internal static class SurvivorSettings
    {
        internal static ConfigEntry<bool> TickDescriptions;
        private static readonly Dictionary<string, Func<string>> descriptions = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Func<string>> skillDescriptions = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        private static bool descriptionHookInstalled;
        internal static void SetDescription(Skill skill, string token, Func<string> description, params string[] alternateTokens)
        {
            // Some tooltip providers retain the original token rather than the SkillDef's current token.
            if (!string.IsNullOrEmpty(skill.NativeDescriptionToken)) descriptions[skill.NativeDescriptionToken] = description;
            foreach (string alternate in alternateTokens)
                if (!string.IsNullOrEmpty(alternate)) descriptions[alternate] = description;
            if (!string.IsNullOrEmpty(skill.Definition.skillNameToken)) skillDescriptions[skill.Definition.skillNameToken] = description;
            SetText(token, description);
        }
        internal static void SetText(string token, Func<string> description)
        {
            if (string.IsNullOrEmpty(token) || description == null) return;
            descriptions[token] = description;
            R2API.LanguageAPI.Add(token, description());
            if (descriptionHookInstalled) return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.SurvivorDescriptions");
            // Patch the lookup itself; callers may already have inlined the short GetString wrapper.
            harmony.Patch(
                AccessTools.Method(typeof(Language), "GetLocalizedStringByToken", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(SurvivorSettings), nameof(BeforeDescription)));
            // LookingGlass can store a complete translated tooltip rather than a description token.
            // Refresh that text at the UI boundary, preserving its appended stats.
            harmony.Patch(AccessTools.Property(typeof(RoR2.UI.TooltipProvider), "bodyText").GetGetMethod(),
                postfix: new HarmonyMethod(typeof(SurvivorSettings), nameof(AfterTooltipBody)));
            harmony.Patch(AccessTools.Method(typeof(RoR2.UI.TooltipProvider), "SetContent"),
                postfix: new HarmonyMethod(typeof(SurvivorSettings), nameof(AfterTooltipContent)));
            descriptionHookInstalled = true;
        }
        private static bool BeforeDescription(string __0, ref string __result)
        {
            if (!descriptions.TryGetValue(__0 ?? "", out Func<string> description)) return true;
            __result = description();
            return false;
        }
        private static string RefreshTooltip(RoR2.UI.TooltipProvider tooltip, string body)
        {
            if (!skillDescriptions.TryGetValue(tooltip.titleToken ?? "", out Func<string> description) &&
                !descriptions.TryGetValue(tooltip.bodyToken ?? "", out description))
            {
                // LoadoutPanelController.Row.AddButton fills overrideTitleText, leaving titleToken empty.
                if (string.IsNullOrEmpty(tooltip.overrideTitleText)) return body;
                foreach (var skill in skillDescriptions)
                    if (string.Equals(tooltip.overrideTitleText, Language.GetString(skill.Key), StringComparison.Ordinal))
                    { description = skill.Value; break; }
                if (description == null) return body;
            }
            // LookingGlass separates the skill description from its stats with a blank line.
            int stats = (body ?? "").IndexOf("\n\n", StringComparison.Ordinal);
            if (stats < 0) stats = (body ?? "").IndexOf("\r\n\r\n", StringComparison.Ordinal);
            return description() + (stats < 0 ? "" : body.Substring(stats));
        }
        private static void AfterTooltipBody(RoR2.UI.TooltipProvider __instance, ref string __result) =>
            __result = RefreshTooltip(__instance, __result);
        private static void AfterTooltipContent(RoR2.UI.TooltipProvider __instance)
        {
            string body = string.IsNullOrEmpty(__instance.overrideBodyText)
                ? (__instance.bodyToken == null ? null : Language.GetString(__instance.bodyToken)) : __instance.overrideBodyText;
            string refreshed = RefreshTooltip(__instance, body);
            if (refreshed != body) __instance.overrideBodyText = refreshed;
        }
        internal static void BindGeneral()
        {
            if (TickDescriptions == null) TickDescriptions = C.BindBool("Survivors - 00 General", "Show Damage Per Tick", true,
                "Show damage per tick and the time between ticks instead of damage per second. Vanilla: False.", true);
        }
        internal static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        internal static ConfigEntry<float> Float(string section, string key, float value, float vanilla, string description, float minimum = 0f) =>
            C.Bind(section, key, value, new ConfigDescription(description + " Vanilla: " + Number(vanilla) + ".", new AcceptableValueRange<float>(minimum, 1000000f)));
        internal static ConfigEntry<int> Int(string section, string key, int value, int vanilla, string description, int minimum = 0) =>
            C.Bind(section, key, value, new ConfigDescription(description + " Vanilla: " + vanilla + ".", new AcceptableValueRange<int>(minimum, 1000000)));
        internal static ConfigEntry<bool> Bool(string section, string key, bool value, bool vanilla, string description) =>
            C.BindBool(section, key, value, description + " Vanilla: " + (vanilla ? "True" : "False") + ".");
        internal static object Read(object obj, string field) => obj == null ? null : AccessTools.Field(obj.GetType(), field)?.GetValue(obj) ?? AccessTools.Property(obj.GetType(), field)?.GetValue(obj, null);
        internal static float Value(object obj, string field, float fallback = 0f) => Read(obj, field) is IConvertible n ? n.ToSingle(CultureInfo.InvariantCulture) : fallback;
        internal static void Write(object obj, string field, object value)
        {
            FieldInfo info = obj == null ? null : AccessTools.Field(obj.GetType(), field);
            if (info != null) info.SetValue(obj, Convert.ChangeType(value, info.FieldType, CultureInfo.InvariantCulture));
        }
        internal static float CookRemaining(float held, float timer) => Math.Max(0f, timer - Math.Max(0f, held - 0.5f));
        internal static float PierceMultiplier(float bonus, int previousEnemies) => (float)Math.Pow(1f + bonus / 100f, previousEnemies);
        internal static int Stock(string value, int maximum, int fallback)
        {
            if (string.Equals(value?.Trim(), "All", StringComparison.OrdinalIgnoreCase)) return maximum;
            return int.TryParse(value, out int count) ? Math.Max(0, Math.Min(maximum, count)) : Math.Min(maximum, fallback);
        }
        internal static int Reload(string value, int maximum, int fallback)
        {
            value = value?.Trim() ?? "Vanilla";
            if (string.Equals(value, "Vanilla", StringComparison.OrdinalIgnoreCase)) return fallback;
            if (value.EndsWith("%") && float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float percentage) && !float.IsNaN(percentage) && !float.IsInfinity(percentage))
                return (int)Math.Floor(maximum * Math.Max(0f, Math.Min(100f, percentage)) / 100f);
            return int.TryParse(value, out int count) ? Math.Max(0, Math.Min(maximum, count)) : fallback;
        }
        internal static string Damage(string text) => "<style=cIsDamage>" + text + "</style>";
        internal static string Utility(string text) => "<style=cIsUtility>" + text + "</style>";
        internal static string DamageText(float percent, float interval, bool periodic)
        {
            if (!periodic || interval <= 0f) return Damage(Number(percent) + "% damage");
            return C.Get(TickDescriptions) ? Damage(Number(percent) + "% damage") + " every " + Utility(Number(interval) + " seconds") : Damage(Number(percent / interval) + "% damage per second");
        }
        internal enum BulletFalloff { None, Default, Buckshot }
        internal enum AreaFalloff { None, Linear, HalfLinear, QuarterLinear, SweetSpot }

        internal sealed class Skill
        {
            internal readonly string Section;
            internal readonly SkillDef Definition;
            internal readonly string NativeDescriptionToken;
            internal bool Periodic;
            internal readonly Type StateType;
            internal readonly object Sample;
            internal readonly ConfigEntry<bool> Enabled;
            private readonly string[] nativeKeywords;
            internal ConfigEntry<float> Damage, Interval, Proc, Duration, Radius, ProjectileSpeed;
            internal float NativeProjectileSpeed;
            private bool variableProjectileSpeed;
            internal ConfigEntry<BulletFalloff> Falloff;
            internal ConfigEntry<AreaFalloff> Area;
            internal bool RechargeAll => string.Equals(C.Get(recharge)?.Trim(), "All", StringComparison.OrdinalIgnoreCase);
            internal bool ConsumeAll => string.Equals(C.Get(consumed)?.Trim(), "All", StringComparison.OrdinalIgnoreCase);
            internal bool RequireAll => ConsumeAll || string.Equals(C.Get(required)?.Trim(), "All", StringComparison.OrdinalIgnoreCase);
            private ConfigEntry<int> stocks;
            private ConfigEntry<string> recharge, consumed, required;
            private ConfigEntry<float> cooldown;
            private ConfigEntry<bool> agile, cancelSprint, combat, cooldownAfter;
            private int nativeRecharge, nativeConsume, nativeRequired;
            internal bool Loaded;
            internal float DamageFactor = 1f;
            internal float AirDurationRatio = 1f;
            internal Skill(string section, SkillDef definition, Type type, ConfigEntry<bool> enabled)
            { Section = section; Definition = definition; NativeDescriptionToken = definition.skillDescriptionToken; StateType = type; Enabled = enabled; nativeKeywords = (definition.keywordTokens ?? Array.Empty<string>()).Where(token => token != "KEYWORD_AGILE").ToArray(); Sample = EntityStateCatalog.InstantiateState(type); }
            internal void Bind(float? changedCooldown = null, int? changedStocks = null, bool? changedAgile = null, bool explosive = false,
                bool damage = true, bool interval = false, string durationField = null, bool area = false, float nativeProc = 1f, int? nativeConsumeOverride = null, bool? changedCombat = null)
            {
                SkillDef d = Definition;
                stocks = Int(Section, "Base Stocks", changedStocks ?? d.baseMaxStock, d.baseMaxStock, "Maximum number of stocks.");
                nativeRecharge = d.rechargeStock; nativeConsume = nativeConsumeOverride ?? d.stockToConsume; nativeRequired = d.requiredStock;
                recharge = C.Bind(Section, "Stocks Recharged Per Cooldown", explosive ? "1" : d.rechargeStock.ToString(), "Stocks restored per cooldown. Use a whole number or All. Vanilla: " + d.rechargeStock + ".");
                consumed = C.Bind(Section, "Stocks Consumed On Use", explosive ? "1" : nativeConsume.ToString(), "Stocks consumed per use. Use a whole number or All. Vanilla: " + nativeConsume + ".");
                required = C.Bind(Section, "Stocks Required To Use", explosive ? "1" : d.requiredStock.ToString(), "Stocks needed to use the skill. Use a whole number or All. Cannot be lower than the stocks consumed. Vanilla: " + d.requiredStock + ".");
                cooldown = Float(Section, "Cooldown", changedCooldown ?? d.baseRechargeInterval, d.baseRechargeInterval, "Cooldown in seconds.");
                bool nativeAgile = !d.cancelSprintingOnActivation && !d.canceledFromSprinting;
                agile = Bool(Section, "Agile", changedAgile ?? nativeAgile, nativeAgile, "Allow the skill to be used while sprinting.");
                cancelSprint = Bool(Section, "Cancelled From Sprinting", d.canceledFromSprinting, d.canceledFromSprinting, "Cancel the skill when sprinting. Always false while Agile is enabled.");
                combat = Bool(Section, "Combat Skill", changedCombat ?? d.isCombatSkill, d.isCombatSkill, "Count the skill as combat.");
                cooldownAfter = Bool(Section, "Cooldown Starts After Duration", d.beginSkillCooldownOnSkillEnd, d.beginSkillCooldownOnSkillEnd, "Wait until the skill ends before starting its cooldown.");
                if (damage)
                {
                    BindDamage(Value(Sample, "damageCoefficient", 1f) * DamageFactor * 100f, null,
                        Value(Sample, "procCoefficient", nativeProc));
                }
                if (interval)
                {
                    string field = StateType.Name == "FireBarrage" ? "baseDurationBetweenShots" : StateType.Name == "FireShotgunBlast" ? "delayBetweenShotgunBlasts" : "baseDuration";
                    float v = Value(Sample, field, 0.1f);
                    Interval = Float(Section, "Damage Interval", v, v, "Seconds between attacks before attack speed is applied.", 0.001f);
                }
                if (durationField != null)
                {
                    float v = Value(Sample, durationField, 1f);
                    Duration = Float(Section, "Duration", v, v, "Duration in seconds.", 0.001f);
                    if (StateType.Name == "SlideState") AirDurationRatio = Value(Sample, "jumpDuration", v) / Math.Max(0.001f, v);
                }
                if (area)
                {
                    float nativeRadius = Value(Sample, "blastRadius", 0f);
                    Area = C.Bind(Section, "AOE Damage Falloff", AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
                    Radius = Float(Section, "AOE Radius", nativeRadius, nativeRadius, "Explosion radius in metres.");
                }
            }
            internal bool CooldownAfterDuration => C.Get(cooldownAfter);
            internal void BindDamage(float nativePercent, float? defaultPercent = null, float nativeProc = 1f, bool periodic = false)
            {
                Periodic = periodic;
                Damage = Float(Section, "Damage (%)", defaultPercent ?? nativePercent, nativePercent,
                    periodic ? "Damage per tick as a percentage of base damage." : "Damage per hit as a percentage of base damage.");
                Proc = Float(Section, "Proc Coefficient", StateType.Name == "ThrowCycloneKickable" ? 0.5f : nativeProc, nativeProc, "Proc coefficient per hit.");
                var nativeFalloff = StateType.Name == "FireShotgunBlast" ? BulletFalloff.Buckshot :
                    StateType.Name == "FirePistol2" || StateType.Name == "FireBarrage" || StateType.Name == "FireShotgun2" ? BulletFalloff.Default : BulletFalloff.None;
                Falloff = C.Bind(Section, "Damage Falloff", nativeFalloff, "Damage falloff over distance. Vanilla: " + nativeFalloff + ".");
            }
            internal void BindProjectileSpeed(GameObject prefab, float defaultMultiplier = 1f)
            {
                float minimum = Value(Sample, "projectileMinSpeed", -1f);
                float maximum = Value(Sample, "projectileMaxSpeed", -1f);
                variableProjectileSpeed = minimum >= 0f && maximum >= 0f;
                float native = variableProjectileSpeed ? (minimum + maximum) * 0.5f : -1f;
                if (native < 0f)
                {
                    foreach (Component component in prefab.GetComponents<Component>())
                    {
                        object speed = Read(component, "desiredForwardSpeed");
                        if (speed is float value) { native = value; break; }
                    }
                }
                if (native < 0f || float.IsNaN(native) || float.IsInfinity(native))
                    throw new InvalidOperationException("Could not read the projectile speed for " + Section + ".");
                NativeProjectileSpeed = native;
                string description = variableProjectileSpeed && minimum != maximum
                    ? "Average launch speed in metres per second. Keeps the usual variation in launch speed."
                    : "Initial projectile speed in metres per second.";
                ProjectileSpeed = Float(Section, "Projectile Speed (m/s)", native * defaultMultiplier, native, description);
            }
            internal float LaunchSpeed(float original)
            {
                float configured = C.Get(ProjectileSpeed);
                return variableProjectileSpeed && NativeProjectileSpeed > 0f && original >= 0f
                    ? original * configured / NativeProjectileSpeed : configured;
            }
            internal void ApplyDefinition()
            {
                if (!Loaded) return;
                int max = C.Get(stocks);
                Definition.baseMaxStock = max;
                Definition.rechargeStock = Stock(C.Get(recharge), max, nativeRecharge);
                Definition.stockToConsume = Stock(C.Get(consumed), max, nativeConsume);
                Definition.requiredStock = Math.Max(Definition.stockToConsume, Stock(C.Get(required), max, nativeRequired));
                Definition.baseRechargeInterval = C.Get(cooldown);
                Definition.cancelSprintingOnActivation = !C.Get(agile);
                Definition.canceledFromSprinting = !C.Get(agile) && C.Get(cancelSprint);
                Definition.isCombatSkill = C.Get(combat);
                Definition.beginSkillCooldownOnSkillEnd = C.Get(cooldownAfter);
                Definition.keywordTokens = nativeKeywords;
                SetKeyword("KEYWORD_AGILE", C.Get(agile));
            }
            internal void SetKeyword(string token, bool enabled)
            {
                if (!Loaded || string.IsNullOrEmpty(token)) return;
                IEnumerable<string> tokens = Definition.keywordTokens ?? Array.Empty<string>();
                Definition.keywordTokens = (enabled ? tokens.Concat(new[] { token }) : tokens.Where(value => value != token))
                    .Distinct().ToArray();
            }
            internal string AgileText => C.Get(agile) ? "<style=cIsUtility>Agile.</style> " : "";
        }

        internal sealed class Stats
        {
            private readonly Dictionary<string, ConfigEntry<float>> values = new Dictionary<string, ConfigEntry<float>>();
            internal Stats(string section, CharacterBody body)
            {
                string[,] fields = { { "Health", "MaxHealth" }, { "Shield", "MaxShield" }, { "Health Regen", "Regen" }, { "Damage", "Damage" },
                    { "Armour", "Armor" }, { "Move Speed", "MoveSpeed" }, { "Attack Speed", "AttackSpeed" }, { "Critical Chance", "Crit" },
                    { "Jump Power", "JumpPower" }, { "Acceleration", "Acceleration" }, { "Sprint Speed Multiplier", "SprintingSpeedMultiplier" } };
                foreach (string prefix in new[] { "base", "level" }) for (int i = 0; i < fields.GetLength(0); i++)
                {
                    string field = prefix + fields[i, 1];
                    if (AccessTools.Field(typeof(CharacterBody), field) == null) continue;
                    float v = Value(body, field);
                    values[field] = Float(section, fields[i, 0] + (prefix == "base" ? "" : " Per Level"), v, v,
                        prefix == "base" ? "Base " + fields[i, 0].ToLowerInvariant() + "." : fields[i, 0] + " gained per level.", field.Contains("Armor") ? -10000f : 0f);
                }
                if (AccessTools.Field(typeof(CharacterBody), "baseJumpCount") != null)
                {
                    float v = Value(body, "baseJumpCount");
                    values["baseJumpCount"] = Float(section, "Jump Count", v, v, "Number of jumps before landing.");
                }
            }
            internal void Apply(CharacterBody body) { foreach (var pair in values) Write(body, pair.Key, C.Get(pair.Value)); }
        }
    }
}
