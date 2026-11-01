using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Acolyte
    {
        private const string Root = "Survivors - 20 Acolyte", StatsRoot = "Survivor-Stats - 20 Acolyte";
        private static readonly string[] Names = { "Blade Slice", "Torrential Slash", "Bola Throw", "Soul Jar", "Harrowing Chains", "Blade Sling", "Javelin", "Blade Dash", "Soul Storm", "Sacrifice", "Banish", "Dark Summons", "Anima Echo" };
        private static readonly string[] StateNames = { "EntityStates.Acolyte.Weapon.BladeCombo", "EntityStates.LilReaper.SlashStorm", "EntityStates.Acolyte.BolaWhip", "EntityStates.Acolyte.AimSoulJar", "EntityStates.LilReaper.Weapon.ChainStorm", "EntityStates.Acolyte.BladeSling", "EntityStates.Acolyte.ThrowJavelin", "EntityStates.Acolyte.BladeDash", "EntityStates.LilReaper.Weapon.AimSoulStorm", "EntityStates.Acolyte.Weapon.AimSacrifice", "EntityStates.LilReaper.EnterBanish", "EntityStates.Acolyte.EnterBodySwap", "EntityStates.Acolyte.EnterTransformBackpack" };
        private sealed class Settings
        {
            internal string Section;
            internal string RecoveryWord = "<style=cIsHealing>recoverable health</style>";
            internal readonly Dictionary<string, ConfigEntry<float>> F = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> I = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> B = new Dictionary<string, ConfigEntry<bool>>();
        }
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal Settings Options;
            internal string Text;
            internal float NativeDamage;
            internal GameObject Projectile, EmpoweredProjectile;
            internal readonly List<SkillDef> Aliases = new List<SkillDef>();
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<Tuple<FieldInfo, object>> Saved = new List<Tuple<FieldInfo, object>>();
        }
        private sealed class ProjectileList { internal readonly List<ProjectileController> Values = new List<ProjectileController>(); }
        private sealed class AreaMarker : MonoBehaviour { internal int Index; internal bool Registered; }
        private static readonly ConditionalWeakTable<CharacterBody, ProjectileList> areas = new ConditionalWeakTable<CharacterBody, ProjectileList>();
        private sealed class Payment { internal SkillDef Def; internal int Consume, Required; }
        private sealed class BodyData { internal float DamageWithoutEcho; internal bool DecaySuppressed; }
        private sealed class HauntTick { internal CharacterBody Attacker; internal object Stack; internal float Timer; }
        private sealed class HauntData { internal readonly List<HauntTick> Ticks = new List<HauntTick>(); }
        private sealed class HauntApplication { internal CharacterBody Attacker; }
        private sealed class HauntEvaluation { internal readonly List<object> Hidden = new List<object>(); }
        private static readonly ConditionalWeakTable<DotController, HauntData> haunts = new ConditionalWeakTable<DotController, HauntData>();
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly HashSet<MethodBase> hooked = new HashSet<MethodBase>();
        private static readonly List<ILHook> ilHooks = new List<ILHook>();
        private static readonly ConditionalWeakTable<CharacterBody, BodyData> bodyData = new ConditionalWeakTable<CharacterBody, BodyData>();
        private static readonly ConfigEntry<bool>[] switches = new ConfigEntry<bool>[13];
        private static readonly Dictionary<string, ConfigEntry<float>> darkStats = new Dictionary<string, ConfigEntry<float>>();
        private static ConfigEntry<bool> enabled, statsEnabled, generalEnabled, graceEnabled, favourEnabled;
        private static Settings general, grace, favour;
        private static int passiveDescriptions;
        private static readonly List<Tuple<S.Skill, Settings, string>> recoveryPassives = new List<Tuple<S.Skill, Settings, string>>();
        private static readonly Dictionary<SkillDef, string> nativeRecoveryTooltips = new Dictionary<SkillDef, string>();
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        private static ItemIndex graceItem, favourItem;
        private static BuffDef recoverBuff, attackBuff, hauntBuff;
        [ThreadStatic] private static EntityState active;
        [ThreadStatic] private static bool convertingRegen, awardingKill;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static object Sample(string name) => GameType(name) is Type t ? EntityStateCatalog.InstantiateState(t) : null;
        private static float N(object obj, string key, float fallback = 0f) => Read(obj, key) is IConvertible n ? n.ToSingle(CultureInfo.InvariantCulture) : fallback;
        private static GameObject Asset(object obj) => obj as GameObject ?? Read(obj, "gameObject") as GameObject;
        private static Component Controller(CharacterBody body, string name) => body && GameType(name) is Type t ? body.GetComponent(t) : null;
        private static Component Form(CharacterBody body) => Controller(body, "RoR2.AcolyteFormController");
        private static bool IsAcolyte(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static bool IsDark(CharacterBody body) => Read(Form(body), "isReaperForm") is bool b && b;
        private static bool IsEcho(CharacterBody body) => Read(Form(body), "isEchoForm") is bool b && b;
        private static bool Has(CharacterBody body, ItemIndex item) => body && body.inventory && item != ItemIndex.None && body.inventory.GetItemCountEffective(item) > 0;
        private static CharacterBody Body(EntityState state)
        {
            if (state?.outer == null) return null;
            CharacterBody body = state.outer.GetComponent<CharacterBody>();
            if (IsAcolyte(body)) return body;
            Component tether = state.outer.GetComponent(GameType("RoR2.ReaperTetherController"));
            GameObject owner = Read(tether, "ownerRoot") as GameObject;
            ProjectileController pc = state.outer.GetComponent<ProjectileController>();
            if (!owner && pc) owner = pc.owner;
            return owner ? owner.GetComponent<CharacterBody>() : body;
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null || !IsAcolyte(Body(state))) return null;
            return ForType(state.GetType());
        }
        private static Profile ForType(Type type)
        {
            foreach (Profile p in profiles.Values) if (p.Skill.StateType == type) return p;
            string name = type.Name, ns = type.Namespace ?? "";
            int i = ns.Contains("BackpackWeapon") ? 12 : ns.Contains("Acolyte.Backpack") ? (name == "ReaperActivate" ? 11 : 12) :
                ns.Contains("Acolyte.Weapon") && name.Contains("Blade") ? 0 : ns.Contains("Acolyte") && (name == "BladeCatch" || name == "JavelinCatch" || name == "JavelinDash") ? 7 :
                name == "SoulStorm" || name == "ReactivateSoulStorm" || name == "SoulStormDash" || name == "SoulStormDashArrival" ? 8 :
                name == "Sacrifice" || ns.Contains("Acolyte.Sacrifice") ? 9 : name == "TransformBackpack" ? 12 :
                ns.Contains("ReaperTetherController") || type.DeclaringType?.Name == "ReaperTetherController" ? 4 :
                type.DeclaringType?.Name == "BolaProjectileController" ? 2 : -1;
            return profiles.TryGetValue(i, out Profile result) ? result : null;
        }
        private static float F(Settings s, string key) => C.Get(s.F[key]);
        private static int I(Settings s, string key) => C.Get(s.I[key]);
        private static bool B(Settings s, string key) => C.Get(s.B[key]);
        private static void Float(Settings s, string key, float value, float native, string text, float minimum = 0f) => s.F[key] = S.Float(s.Section, key, value, native, text, minimum);
        private static void Int(Settings s, string key, int value, int native, string text, int minimum = 0) => s.I[key] = S.Int(s.Section, key, value, native, text, minimum);
        private static void Bool(Settings s, string key, bool value, bool native, string text) => s.B[key] = S.Bool(s.Section, key, value, native, text);
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Acolyte changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Acolyte stat changes. Restart to apply. Vanilla: False.");
            generalEnabled = C.BindStartupBool(Root + " - Recoverable Health", "Changes Enabled", true, "Enable recoverable health changes. Restart to apply. Vanilla: False.");
            graceEnabled = C.BindStartupBool(Root + " - Nkuhanas Grace", "Changes Enabled", true, "Enable N'kuhana's Grace changes. Restart to apply. Vanilla: False.");
            favourEnabled = C.BindStartupBool(Root + " - Nkuhanas Favour", "Changes Enabled", true, "Enable N'kuhana's Favour changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static string Title(string token) => Regex.Replace(Language.GetString(token), "[^A-Za-z0-9]", "").ToLowerInvariant();
        private static void Load()
        {
            try
            {
                prefab = BodyCatalog.FindBodyPrefab("AcolyteBody")?.GetComponent<CharacterBody>();
                if (!prefab) throw new InvalidOperationException("Acolyte body was not found.");
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Acolyte");
                graceItem = ItemCatalog.FindItemIndex("StealBackHealth"); favourItem = ItemCatalog.FindItemIndex("ConvertHealingToAttackSpeed");
                recoverBuff = FindBuff("AcoStealBackHealthBuff"); attackBuff = FindBuff("AcoAttackSpeed"); hauntBuff = FindBuff("Haunted");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefab); BindDarkStats(); }
                if (generalEnabled.Value)
                {
                    general = new Settings { Section = Root + " - Recoverable Health" };
                    Float(general, "Decay Per Second (%)", 1f, 1f, "Maximum health lost from recoverable health each second.");
                    Bool(general, "Allow Healing From Other Sources", false, false, "Allow normal healing to restore recoverable health.");
                }
                if (graceEnabled.Value) grace = BindPassive(false);
                if (favourEnabled.Value) favour = BindPassive(true);
                SkillDef[] all = SkillCatalog.allSkillDefs.Where(d => d && d.activationState.stateType != null).ToArray();
                foreach (int i in Enumerable.Range(0, Names.Length))
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        Type type = GameType(StateNames[i]);
                        string title = Regex.Replace(Names[i], "[^A-Za-z0-9]", "").ToLowerInvariant();
                        SkillDef def = all.FirstOrDefault(d => d.activationState.stateType == type && Title(d.skillNameToken) == title) ?? all.FirstOrDefault(d => d.activationState.stateType == type);
                        if (!def && i == 7)
                        {
                            def = ScriptableObject.CreateInstance<SkillDef>(); def.activationState = new SerializableEntityStateType(type);
                            def.skillNameToken = "3QS_ACOLYTE_BLADE_DASH_NAME"; def.skillDescriptionToken = "3QS_ACOLYTE_BLADE_DASH_DESCRIPTION";
                            S.SetText(def.skillNameToken, () => "Blade Dash");
                        }
                        if (!def) throw new InvalidOperationException("Skill definition was not found: " + StateNames[i]);
                        Profile p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[i]), Text = Language.GetString(def.skillDescriptionToken), Options = new Settings { Section = Root + " - " + Names[i] } };
                        p.Options.RecoveryWord = RecoveryLabel(p.Text);
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p;
                        S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => p.Skill.Proc == null ? 1f : C.Get(p.Skill.Proc));
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Acolyte " + Names[i] + " changes could not be loaded: " + e); }
                }
                Install(); Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Acolyte changes could not be loaded: " + e); }
        }
        private static BuffDef FindBuff(string name) => BuffCatalog.buffDefs.FirstOrDefault(b => b && b.name == name);
        private static Settings BindPassive(bool isFavour)
        {
            Settings s = new Settings { Section = Root + (isFavour ? " - Nkuhanas Favour" : " - Nkuhanas Grace") };
            Float(s, "Damage Taken Added To Recoverable Health (%)", isFavour ? 0f : 40f, isFavour ? 0f : 40f, "Damage taken added to recoverable health.");
            Float(s, "Attack Speed Per Recoverable Health (%)", isFavour ? 0f : 1f, isFavour ? 0f : 1f, "Attack speed gained per 1% of maximum health held as recoverable health.");
            Float(s, "Armour Increase", 0f, isFavour ? 20f : 0f, "Additional armour while using this passive.", -10000f);
            if (!isFavour) Float(s, "Damage To Healing (%)", 6.5f, 6.5f, "Skill damage converted into healing from stored recoverable health, scaled by proc coefficient. Requires and consumes that reserve; cannot heal more than the stored amount. Does not grant lifesteal.");
            else
            {
                Int(s, "Flat Attack Speed Stacks From Healing", 1, 1, "Extra attack speed stacks granted by each heal.");
                Float(s, "Stacks Per 1% Maximum Health Healed", 1.5f, 1.5f, "Attack speed stacks gained per 1% of maximum health healed, before diminishing returns.");
                Float(s, "Health Regen To Permanent Stacks", 2.5f, 2.5f, "Health regeneration multiplier used to calculate permanent stacks, before diminishing returns.");
            }
            SkillDef[] passiveDefs = SkillCatalog.allSkillDefs.Where(d => d && (isFavour ? Title(d.skillNameToken).Contains("nkuhanasfavour") || Title(d.skillNameToken).Contains("nkuhanasfavor") : Title(d.skillNameToken).Contains("nkuhanasgrace"))).ToArray();
            if (passiveDefs.Length == 0) ThreeQsVanillaTweaks.Log.Warning("Acolyte: could not find the " + (isFavour ? "Favour" : "Grace") + " passive description.");
            foreach (SkillDef def in passiveDefs)
            {
                s.RecoveryWord = RecoveryLabel(Language.GetString(def.skillDescriptionToken));
                S.Skill passive = new S.Skill(s.Section, def, def.activationState.stateType ?? typeof(EntityStates.Idle), isFavour ? favourEnabled : graceEnabled);
                S.SetDescription(passive, def.skillDescriptionToken, () => PassiveDescription(s, isFavour));
                passiveDescriptions++;
                recoveryPassives.Add(Tuple.Create(passive, s, isFavour ? "FAVOUR" : "GRACE"));
                Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => isFavour ? "\nBase Stacks Per Heal: " + S.Utility(I(s, "Flat Attack Speed Stacks From Healing").ToString()) + "\nStacks Per 1% Healed: " + S.Utility(S.Number(F(s, "Stacks Per 1% Maximum Health Healed"))) + "\nRegen Conversion: " + S.Utility(S.Number(F(s, "Health Regen To Permanent Stacks")) + "x") : RecoveryExtra(s, false));
            }
            return s;
        }
        private static void BindDarkStats()
        {
            Component swap = Controller(prefab, "RoR2.AcolyteBodySwapController");
            string[,] fields = { { "MaxHealth", "Health" }, { "MaxShield", "Shield" }, { "Regen", "Health Regen" }, { "Damage", "Damage" }, { "Armor", "Armour" }, { "MoveSpeed", "Move Speed" }, { "AttackSpeed", "Attack Speed" }, { "Crit", "Critical Chance" }, { "JumpPower", "Jump Power" }, { "Acceleration", "Acceleration" } };
            foreach (string prefix in new[] { "base", "level" }) for (int i = 0; i < fields.GetLength(0); i++)
            {
                string field = prefix + fields[i, 0]; float native = N(swap, field, N(prefab, field));
                darkStats[field] = S.Float(StatsRoot + " - Anima Harvester", fields[i, 1] + (prefix == "base" ? "" : " Per Level"), native, native, "Anima Harvester " + fields[i, 1].ToLowerInvariant() + (prefix == "base" ? "." : " gained per level."), field.EndsWith("Armor") ? -10000f : 0f);
            }
        }
        private static void Bind(Profile p)
        {
            int i = p.Index; S.Skill s = p.Skill; Settings o = p.Options;
            if (i == 1 || i == 2 || i == 3 || i == 5 || i == 6) p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
            s.Bind(changedCooldown: i == 3 ? (float?)5f : i == 0 ? (float?)8f : null, changedStocks: i == 0 ? (int?)3 : null, damage: false);
            object attack = i == 8 ? Sample("EntityStates.LilReaper.Weapon.SoulStorm") : i == 9 ? Sample("EntityStates.Acolyte.Weapon.Sacrifice") : i == 7 ? Sample("EntityStates.Acolyte.BladeCatch") : s.Sample;
            p.NativeDamage = N(attack, "damageCoefficient", 1f) * 100f;
            if (i < 10 && i != 4)
            {
                s.BindDamage(p.NativeDamage, i == 2 ? 100f : i == 3 ? 200f : i == 5 ? 40f : i == 6 ? 400f : i == 7 ? 400f : i == 8 ? 100f : (float?)null, i == 0 ? 1f : N(attack, "procCoefficient", 1f), i == 5 || i == 8);
                if (i == 0) { Float(o, "Third Slash Damage (%)", N(s.Sample, "comboFinisherDamageCoefficient", 1.5f) * 100f, N(s.Sample, "comboFinisherDamageCoefficient", 1.5f) * 100f, "Damage dealt by the third slash."); }
            }
            if (i == 0)
            {
                object quick = Sample("EntityStates.Acolyte.Weapon.BladeSprintStrike");
                Float(o, "Quickstrike Damage (%)", 200f, N(quick, "damageCoefficient", 2f) * 100f, "Damage per Quickstrike hit.");
                Float(o, "Quickstrike Proc Coefficient", .7f, N(quick, "procCoefficient", .7f), "Proc coefficient per Quickstrike hit.");
            }
            if (i == 1) Float(o, "Execution Threshold (%)", 25f, 25f, "Execute enemies below this percentage of health.");
            if (i == 2)
            {
                Component bolaController = p.Projectile ? p.Projectile.GetComponent(GameType("RoR2.Projectile.BolaProjectileController")) : null;
                GameObject soulPrefab = Asset(Read(bolaController, "soulPrefab"));
                Component soul = soulPrefab ? soulPrefab.GetComponent(GameType("RoR2.AcolyteExtractedSoulController")) : null;
                Int(o, "Maximum Targets", 5, (int)N(Sample("RoR2.Projectile.BolaProjectileController+GatherState"), "maxTargets", 5f), "Maximum enemies gathered by a bola.");
                Float(o, "Extracted Soul HP (%)", 30f, 30f, "Extracted soul health as a percentage of the original body's maximum health.");
                Float(o, "Extracted Soul Damage (%)", 200f, N(soul, "returnDamageCoefficient", .5f) * 100f, "Damage returned to the original body as a percentage of damage dealt to its soul.");
                Float(o, "Soul Extraction Duration", 12f, N(soul, "soulDuration", 3f), "Seconds an extracted soul remains separated.");
                Float(o, "Soul Damage Proc Coefficient Modifier", 1f, N(soul, "returnProcCoefficient", .5f), "Proc coefficient multiplier for damage returned through a soul.");
            }
            if (i == 2 || i == 3 || i == 5 || i == 6 || i == 1)
            {
                p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
                if (!p.Projectile && i == 3) p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
                if (p.Projectile) BindSpeed(s, p.Projectile, i == 6 ? (float?)120f : null);
            }
            if (i == 3)
            {
                Float(o, "Haunt Duration", 8f, 8f, "Haunt duration in seconds.");
                Float(o, "Haunt Damage (%)", 25f, 16.66f, "Damage per Haunt tick as a percentage of base damage.");
                Float(o, "Haunt Damage Interval", .5f, .333f, "Seconds between Haunt damage ticks.", .001f);
                Bool(o, "Haunt Can Be Lethal", false, false, "Allow Haunt damage to kill enemies.");
                Float(o, "Wight Seeking Range", 128f, 40f, "Maximum distance Wights can seek a target, in metres.");
                Float(o, "Wight Damage (%)", 50f, 100f, "Wight damage as a percentage of base damage.");
                Bool(o, "Wight Can Be Lethal", false, false, "Allow Wights to kill enemies.");
            }
            if (i == 4)
            {
                Int(o, "Chains Released", 5, (int)N(s.Sample, "maxChains", 5f), "Maximum chains released per cast.");
                s.BindDamage(250f, 250f, 1f); o.F["Impact Damage (%)"] = s.Damage;
            }
            if (i == 5)
            {
                s.Interval = S.Float(s.Section, "Damage Interval", .2f, .2f, "Seconds between blade damage hits.", .001f);
                Bool(o, "Stun On Hit", true, false, "Stun enemies damaged by the spinning blade.");
                float lifetime = N(projectileLifetime(p.Projectile), "lifetime", -1f);
                Float(o, "AOE Duration", lifetime, lifetime, "Blade lifetime in seconds. -1 lasts until the blade is caught or removed.", -1f);
                Int(o, "Maximum AOE Instances", -1, -1, "Maximum active blades per player. Remove the oldest when exceeded. -1 for unlimited.", -1);
            }
            if (i == 6 || i == 8)
            {
                GameObject projectile = i == 8 ? Asset(Read(attack, "projectilePrefab")) : p.Projectile;
                if (i == 8) { p.Projectile = projectile; if (projectile) BindSpeed(s, projectile); }
                ProjectileImpactExplosion explosion = projectile ? projectile.GetComponent<ProjectileImpactExplosion>() : null;
                float radius = i == 6 ? N(explosion, "blastRadius", 11f) : N(attack, "blastRadius", 10f);
                s.Radius = S.Float(s.Section, "AOE Radius", i == 6 ? 11f : radius, radius, "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre. Vanilla: None.");
            }
            if (i == 8)
            {
                Float(o, "Cast Damage (%)", 200f, N(attack, "damageCoefficient", 2f) * 100f, "Damage dealt when the storm is cast.");
                s.Interval = S.Float(s.Section, "Damage Interval", .3333f, .3333f, "Seconds between storm damage ticks.", .001f);
                Float(o, "Reactivation Blast Damage (%)", 300f, N(Sample("EntityStates.LilReaper.Weapon.ReactivateSoulStorm"), "damageCoefficient", 3f) * 100f, "Damage dealt when the storm is reactivated.");
                Float(o, "Empowered Storm Damage (%)", 200f, 200f, "Damage per empowered storm tick.");
                Float(o, "Empowered Storm Interval", .1667f, .1667f, "Seconds between empowered storm damage ticks.", .001f);
                Float(o, "Empowered Storm Duration", 3.2f, 3.2f, "Empowered storm duration in seconds.", .001f);
                p.EmpoweredProjectile = Asset(Read(Sample("EntityStates.LilReaper.Weapon.ReactivateSoulStorm"), "projectilePrefab"));
                Int(o, "Maximum AOE Instances", -1, -1, "Maximum active storms per player. Remove the oldest when exceeded. -1 for unlimited.", -1);
                Float(o, "AOE Duration", N(projectileLifetime(p.Projectile), "lifetime", 6f), N(projectileLifetime(p.Projectile), "lifetime", 6f), "Storm duration in seconds.", .001f);
            }
            if (i == 9)
            {
                Int(o, "Convictions Gained", 2, (int)N(attack, "soulValue", 2f), "Convictions gained when Sacrifice hits.");
                Int(o, "Extra Conviction On Kill", 1, 1, "Additional Convictions gained when Sacrifice kills its target.");
                Int(o, "Litany Threshold", 7, 7, "Convictions needed to unlock a Litany.", 1);
            }
            if (i == 11 || i == 12)
            {
                Float(o, "Damage To Healing (%)", i == 11 ? 38.5f : 15f, i == 11 ? 38.5f : 25f, "Damage returned as recoverable health healing during this Litany.");
                Float(o, "Seconds Per Conviction Lost", i == 11 ? 1.29f : 2f, i == 11 ? 1.29f : 2f, "Seconds between losing Convictions during this Litany.", .001f);
            }
            if (i == 11) Bool(o, "Pause Recoverable Health Decay", true, true, "Pause recoverable health decay during Dark Summons.");
            if (i == 12)
            {
                s.Proc = S.Float(s.Section, "Proc Coefficient", N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeCombo"), "procCoefficient", 1f), N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeCombo"), "procCoefficient", 1f), "Proc coefficient of echo attacks.");
                Float(o, "Echo Slash Damage (%)", 150f, N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeCombo"), "damageCoefficient", 1.5f) * 100f, "Damage dealt by each echo slash.");
                Float(o, "Echo Third Slash Damage (%)", 150f, N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeCombo"), "comboFinisherDamageCoefficient", 1.5f) * 100f, "Damage dealt by the echo's third slash.");
                Float(o, "Echo Quickstrike Damage Per Hit (%)", 200f, N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeSprintStrike"), "damageCoefficient", 2f) * 100f, "Damage per echo Quickstrike hit.");
                Float(o, "Echo Blade Dash Damage (%)", 400f, N(Sample("EntityStates.Acolyte.BackpackWeapon.BackBladeCatch"), "damageCoefficient", 4f) * 100f, "Damage dealt by an echo Blade Dash.");
                Float(o, "Armour Gained", 20f, 0f, "Additional armour during Anima Echo.", -10000f);
                Float(o, "Damage Gained (%)", 50f, 0f, "Additional personal damage during Anima Echo. Does not increase echo damage.");
                foreach (string stat in new[] { "Health", "Shield", "Attack Speed", "Move Speed", "Jump Power", "Acceleration", "Sprint Speed" }) Float(o, stat + " Gained (%)", 0f, 0f, "Additional " + stat.ToLowerInvariant() + " during Anima Echo.");
                Float(o, "Health Regen Gained", 0f, 0f, "Additional health regeneration during Anima Echo.");
                Float(o, "Critical Chance Gained (%)", 0f, 0f, "Additional critical strike chance during Anima Echo.");
                Int(o, "Extra Jumps", 0, 0, "Additional jumps during Anima Echo.");
                Bool(o, "Refresh Cooldowns On Cast", true, false, "Refresh skill stocks and cooldowns when Anima Echo activates.");
            }
        }
        private static void BindSpeed(S.Skill s, GameObject projectile, float? changed = null)
        {
            Component speed = projectile.GetComponents<Component>().FirstOrDefault(c => c && Read(c, "desiredForwardSpeed") is float);
            if (speed)
            {
                float native = N(speed, "desiredForwardSpeed");
                s.NativeProjectileSpeed = native;
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", changed ?? native, native, "Projectile speed in metres per second.");
                return;
            }
            Component custom = projectile.GetComponents<Component>().FirstOrDefault(c => c && Read(c, "maxVelocity") is float);
            if (custom)
            {
                float native = N(custom, "maxVelocity");
                s.NativeProjectileSpeed = native;
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", changed ?? native, native, "Projectile speed in metres per second.");
            }
        }
        private static Component projectileLifetime(GameObject obj) => obj ? obj.GetComponents<Component>().FirstOrDefault(c => c && Field(c.GetType(), "lifetime") != null) : null;
        private static FieldInfo Field(Type type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }
            return null;
        }
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            FieldInfo f = Field(obj.GetType(), name);
            if (f != null) return f.GetValue(f.IsStatic ? null : obj);
            return obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj, null);
        }
        private static void Write(object obj, string name, object value)
        {
            if (obj == null) return;
            FieldInfo f = Field(obj.GetType(), name);
            if (f != null) f.SetValue(f.IsStatic ? null : obj, f.FieldType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture));
            else obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true)?.Invoke(obj, new[] { value });
        }
        private static MethodInfo Method(Type type, string name, Type[] args = null)
        {
            for (; type != null; type = type.BaseType)
            {
                MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).FirstOrDefault(m => m.Name == name && (args == null || m.GetParameters().Select(p => p.ParameterType).SequenceEqual(args)));
                if (method != null) return method;
            }
            return null;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo method = Method(type, name, args);
            if (method == null) { ThreeQsVanillaTweaks.Log.Warning("Acolyte hook unavailable: " + type?.FullName + "." + name); return; }
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(Acolyte), prefix), postfix == null ? null : new HarmonyMethod(typeof(Acolyte), postfix), null, finalizer == null ? null : new HarmonyMethod(typeof(Acolyte), finalizer), null);
        }
        private static void Install()
        {
            Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats), nameof(AfterStats));
            Patch(typeof(CharacterBody), "FixedUpdate", postfix: nameof(AfterBodyUpdate));
            if (general != null)
            {
                MethodInfo serverTick = Method(typeof(HealthComponent), "ServerFixedUpdate");
                if (serverTick != null) ilHooks.Add(new ILHook(serverTick, DecayIL));
                MethodInfo heal = typeof(HealthComponent).GetMethods(BindingFlags.Instance | BindingFlags.Public).First(m => m.Name == "Heal" && m.GetParameters().Length == 3);
                harmony.Patch(heal, prefix: new HarmonyMethod(typeof(Acolyte), nameof(BeforeHeal)));
            }
            if (grace != null)
                foreach (string method in new[] { "OnDamageDealtServer", "OnTakeDamageServer", "FixedUpdate" })
                    ilHooks.Add(new ILHook(Method(GameType("RoR2.Items.StealBackHealthBehaviour"), method), GraceIL));
            if (favour != null)
            {
                Patch(GameType("RoR2.Items.ConvertHealingToAttackSpeedBehavior"), "GetAttackSpeedBuffCountForHeal", nameof(BeforeHealStacks));
                Patch(GameType("RoR2.Items.ConvertHealingToAttackSpeedBehavior"), "RecalculateBaseAttackSpeedBuffCount", nameof(BeforeRegenStacks), finalizer: nameof(EndRegenStacks));
                GlobalEventManager.onServerDamageDealt += OnDamageDealt;
            }
            if (profiles.ContainsKey(11) || profiles.ContainsKey(12))
            {
                Patch(GameType("RoR2.AcolyteFormController"), "get_regainPercentOfDealtDamage", postfix: nameof(AfterFormHealing));
                Patch(GameType("RoR2.AcolyteBodySwapController"), "SwapBodyInternal", postfix: nameof(AfterSwap));
                Patch(GameType("RoR2.AcolyteBodySwapController"), "UnswapBodyInternal", nameof(BeforeUnswap));
            }
            if (profiles.ContainsKey(9))
            {
                Patch(GameType("RoR2.AcolyteFormController"), "Awake", nameof(BeforeForm));
                Patch(GameType("RoR2.AcolyteFormController"), "AddSouls", nameof(BeforeAddSouls));
                Patch(GameType("RoR2.AcolyteFormController"), "OnKilledOtherServer", nameof(BeforeSoulKill), finalizer: nameof(EndSoulKill));
            }
            foreach (Type type in typeof(EntityState).Assembly.GetTypes().Where(t => !t.IsAbstract && !t.ContainsGenericParameters && typeof(EntityState).IsAssignableFrom(t) && (t.Namespace?.StartsWith("EntityStates.Acolyte", StringComparison.Ordinal) == true || t.Namespace?.StartsWith("EntityStates.LilReaper", StringComparison.Ordinal) == true || t.DeclaringType?.Name == "BolaProjectileController" || t.DeclaringType?.Name == "ReaperTetherController")))
            {
                if (ForType(type) == null) continue;
                for (Type t = type; t != null && t != typeof(EntityState); t = t.BaseType)
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == "OnEnter" || m.Name == "OnExit" || m.Name == "FixedUpdate" || m.Name == "Fire" || m.Name == "FireProjectile" || m.Name == "OnProjectileFiredLocal" || m.Name == "ModifyOverlapAttack"))
                        if (!m.ContainsGenericParameters && hooked.Add(m)) harmony.Patch(m, new HarmonyMethod(typeof(Acolyte), nameof(BeforeState)), new HarmonyMethod(typeof(Acolyte), nameof(AfterState)), null, new HarmonyMethod(typeof(Acolyte), nameof(EndState)), null);
            }
            if (profiles.Count > 0)
            {
                Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(EndPayment));
                Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                MethodInfo init = Method(typeof(ProjectileController), "DispatchOnInitialized");
                if (init != null) harmony.Patch(init, postfix: new HarmonyMethod(typeof(Acolyte), nameof(BeforeProjectileStart)));
                Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap), args: new[] { typeof(List<HurtBox>) });
                Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast), args: Type.EmptyTypes);
            }
            if (profiles.ContainsKey(2))
            {
                Patch(GameType("RoR2.AcolyteExtractedSoulController"), "OnOriginBodyChanged", nameof(BeforeSoul));
                Patch(GameType("RoR2.AcolyteExtractedSoulController"), "OnIncomingDamageServer", nameof(BeforeSoul));
                CharacterBody.onBodyStartGlobal += OnBodyStart;
            }
            if (profiles.ContainsKey(3))
            {
                MethodInfo add = typeof(DotController).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.Name == "AddDot").OrderByDescending(m => m.GetParameters().Length).First();
                harmony.Patch(add, prefix: new HarmonyMethod(typeof(Acolyte), nameof(BeforeHaunt)), postfix: new HarmonyMethod(typeof(Acolyte), nameof(AfterHaunt)));
                Patch(typeof(DotController), "FixedUpdate", nameof(BeforeHauntTick));
                Patch(typeof(DotController), "EvaluateDotStacksForType", nameof(BeforeHauntEvaluation), nameof(AfterHauntEvaluation), nameof(EndHauntEvaluation));
                Patch(GameType("RoR2.WightLauncher"), "SpawnWightProjectile", nameof(BeforeWight));
            }
            if (profiles.ContainsKey(1)) ilHooks.Add(new ILHook(Method(typeof(HealthComponent), "TakeDamageProcess"), ExecuteIL));
        }
        private static Profile ForDefinition(SkillDef def) => profiles.Values.FirstOrDefault(p => p.Skill.Definition == def || p.Aliases.Contains(def));
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        {
            Profile p = ForDefinition(__instance);
            if (p != null && IsAcolyte(skillSlot.characterBody) && p.Skill.RechargeAll) __result = skillSlot.maxStock;
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            Profile p = ForDefinition(__instance);
            if (p != null && IsAcolyte(skillSlot.characterBody) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false;
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        {
            __state = null; Profile p = ForDefinition(__instance);
            if (p == null || !IsAcolyte(skillSlot.characterBody) || !p.Skill.ConsumeAll && !p.Skill.RequireAll) return;
            __state = new Payment { Def = __instance, Consume = __instance.stockToConsume, Required = __instance.requiredStock };
            if (p.Skill.ConsumeAll) __instance.stockToConsume = skillSlot.maxStock;
            if (p.Skill.RequireAll) __instance.requiredStock = skillSlot.maxStock;
        }
        private static Exception EndPayment(Exception __exception, Payment __state)
        {
            if (__state != null) { __state.Def.stockToConsume = __state.Consume; __state.Def.requiredStock = __state.Required; }
            return __exception;
        }
        private static void Apply()
        {
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 11 || p.Index == 12)
                    RecoveryKeyword(p.Skill, p.Options, p.Index == 11 ? "DARK_SUMMONS" : "ANIMA_ECHO");
                if (p.Index == 3)
                {
                    Keyword(p.Skill, "HAUNT", "KEYWORD_3QS_ACOLYTE_HAUNT", () => "<style=cKeywordName>Haunt</style><style=cSub>Deal " + S.DamageText(F(p.Options, "Haunt Damage (%)"), F(p.Options, "Haunt Damage Interval"), true) + " for " + S.Utility(S.Number(F(p.Options, "Haunt Duration")) + " seconds") + (B(p.Options, "Haunt Can Be Lethal") ? "." : ". Cannot kill.") + "</style>");
                    Keyword(p.Skill, "WIGHT", "KEYWORD_3QS_ACOLYTE_WIGHT", () => "<style=cKeywordName>Wight</style><style=cSub>A seeking spirit that deals " + S.Damage(S.Number(F(p.Options, "Wight Damage (%)")) + "% damage") + (B(p.Options, "Wight Can Be Lethal") ? "." : ". Cannot kill.") + "</style>");
                }
                if (p.Index == 0) Keyword(p.Skill, "QUICKSTRIKE", "KEYWORD_3QS_ACOLYTE_QUICKSTRIKE", () => "<style=cKeywordName>Quickstrike</style><style=cSub>While holding Primary, jump or sprint to expend a stock and perform a Quickstrike for " + S.Damage(S.Number(F(p.Options, "Quickstrike Damage (%)")) + "% damage per hit") + ".</style>");
            }
            foreach (var entry in recoveryPassives)
                if (entry.Item3 == "GRACE" || F(entry.Item2, "Damage Taken Added To Recoverable Health (%)") > 0f || F(entry.Item2, "Attack Speed Per Recoverable Health (%)") > 0f)
                    RecoveryKeyword(entry.Item1, entry.Item2, entry.Item3);
            if (profiles.TryGetValue(0, out Profile blade))
                foreach (SkillDef d in SkillCatalog.allSkillDefs.Where(d => d && d.activationState.stateType?.FullName?.StartsWith("EntityStates.Acolyte.Weapon.Blade", StringComparison.Ordinal) == true))
                {
                    d.baseMaxStock = blade.Skill.Definition.baseMaxStock; d.baseRechargeInterval = blade.Skill.Definition.baseRechargeInterval;
                }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (IsAcolyte(body)) body.MarkAllStatsDirty();
        }
        private static string RecoveryLabel(string original)
        {
            Match match = Regex.Match(original ?? "", @"<style=(?<style>[^>]+)>recoverable health</style>", RegexOptions.IgnoreCase);
            return "<style=" + (match.Success ? match.Groups["style"].Value : "cIsHealing") + ">recoverable health</style>";
        }
        private static void RecoveryKeyword(S.Skill skill, Settings options, string variant)
        {
            string[] tokens = skill.Definition.keywordTokens ?? Array.Empty<string>();
            if (!nativeRecoveryTooltips.ContainsKey(skill.Definition))
            {
                string original = tokens.Where(IsRecoveryKeyword).Select(Language.GetString).FirstOrDefault(text => !string.IsNullOrEmpty(text));
                nativeRecoveryTooltips[skill.Definition] = original;
            }
            skill.Definition.keywordTokens = tokens.Where(token => !IsRecoveryKeyword(token)).ToArray();
            string custom = "KEYWORD_3QS_ACOLYTE_RECOVERY_" + variant;
            // Passive SkillDefs have no activation state to apply; their keyword list still needs updating.
            skill.Definition.keywordTokens = skill.Definition.keywordTokens.Concat(new[] { custom }).Distinct().ToArray();
            S.SetText(custom, () => RecoveryTooltip(options, variant, nativeRecoveryTooltips[skill.Definition]));
        }
        private static bool IsRecoveryKeyword(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.StartsWith("KEYWORD_3QS_ACOLYTE_RECOVERY_", StringComparison.Ordinal)) return true;
            if (token.IndexOf("REGAIN", StringComparison.OrdinalIgnoreCase) >= 0 || token.IndexOf("RECOVERABLE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            string text = Language.GetString(token);
            return Regex.IsMatch(text ?? "", @"<style=cKeywordName>\s*Recoverable Health\s*</style>", RegexOptions.IgnoreCase);
        }
        private static string RecoveryTooltip(Settings options, string variant, string original)
        {
            bool form = variant == "DARK_SUMMONS" || variant == "ANIMA_ECHO";
            bool paused = variant == "ANIMA_ECHO" || variant == "DARK_SUMMONS" && B(options, "Pause Recoverable Health Decay");
            string text = original;
            if (string.IsNullOrEmpty(text)) text = "<style=cKeywordName>Recoverable Health</style><style=cSub>A temporary reserve of health that is converted into healing by dealing damage with any skill. Depletes over time.</style>";
            if (form) text = text.Replace("by dealing damage with any skill", "by dealing damage");
            if (paused) text = text.Replace("Depletes over time.", "Does not deplete during this Litany.");
            return text;
        }
        private static string RecoveryExtra(Settings options, bool form)
        {
            string text = "\nRecoverable Health Conversion: <style=cIsHealing>" + S.Number(F(options, "Damage To Healing (%)")) + "% of " + (form ? "damage dealt" : "skill damage dealt") + "</style>";
            if (!form) text += " × proc coefficient";
            text += " (requires and consumes recoverable health)";
            if (!form) text += "\nRecoverable Health Decay: " + S.Utility(S.Number(general == null ? 1f : F(general, "Decay Per Second (%)")) + "% max HP/s");
            return text;
        }

        private static void Keyword(S.Skill skill, string match, string token, Func<string> description)
        {
            skill.Definition.keywordTokens = (skill.Definition.keywordTokens ?? Array.Empty<string>()).Where(t => t.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            skill.SetKeyword(token, true); S.SetText(token, description);
        }
        private static void Set(Scope scope, object obj, string key, object value)
        {
            FieldInfo field = obj == null ? null : Field(obj.GetType(), key); if (field == null) return;
            if (field.IsStatic) scope.Saved.Add(Tuple.Create(field, field.GetValue(null)));
            field.SetValue(field.IsStatic ? null : obj, field.FieldType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = new Scope { Previous = active }; Profile p = ForState(__instance); active = p == null ? null : __instance;
            if (p == null) return;
            S.Skill s = p.Skill; Settings o = p.Options; int i = p.Index; string name = __instance.GetType().Name;
            if (s.Damage != null) Set(__state, __instance, "damageCoefficient", C.Get(s.Damage) / 100f);
            if (s.Proc != null) Set(__state, __instance, "procCoefficient", C.Get(s.Proc));
            if (i == 0)
            {
                bool quick = name.Contains("SprintStrike") || name.Contains("JumpStrike");
                if (quick) { Set(__state, __instance, "damageCoefficient", F(o, "Quickstrike Damage (%)") / 100f); Set(__state, __instance, "procCoefficient", F(o, "Quickstrike Proc Coefficient")); }
                Set(__state, __instance, "comboFinisherDamageCoefficient", F(o, "Third Slash Damage (%)") / 100f);
            }
            if (i == 2) Set(__state, __instance, "maxTargets", I(o, "Maximum Targets"));
            if (i == 4) { Set(__state, __instance, "maxChains", I(o, "Chains Released")); Set(__state, __instance, "damageCoefficient", F(o, "Impact Damage (%)") / 100f); }
            if (i == 8)
            {
                bool empowered = name == "ReactivateSoulStorm";
                Set(__state, __instance, "damageCoefficient", F(o, empowered ? "Reactivation Blast Damage (%)" : "Cast Damage (%)") / 100f);
                Set(__state, __instance, "projectileDamageCoefficient", (empowered ? F(o, "Empowered Storm Damage (%)") : C.Get(s.Damage)) / 100f);
                if (s.Radius != null) Set(__state, __instance, "blastRadius", C.Get(s.Radius));
            }
            if (i == 9) Set(__state, __instance, "soulValue", I(o, "Convictions Gained"));
            if (i == 11 || i == 12)
            {
                if (__instance.GetType().Namespace?.Contains("BackpackWeapon") == true)
                {
                    string key = name.Contains("Catch") ? "Echo Blade Dash Damage (%)" : name.Contains("SprintStrike") || name.Contains("JumpStrike") ? "Echo Quickstrike Damage Per Hit (%)" : "Echo Slash Damage (%)";
                    Set(__state, __instance, "damageCoefficient", F(o, key) / 100f);
                    Set(__state, __instance, "comboFinisherDamageCoefficient", F(o, "Echo Third Slash Damage (%)") / 100f);
                }
                else if (__instance.GetType().Namespace == "EntityStates.Acolyte.Backpack")
                {
                    Component form = Form(Body(__instance));
                    Set(__state, __instance, "duration", F(o, "Seconds Per Conviction Lost") * N(form, "maxSoul", 7f));
                }
            }
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod)
        {
            Profile p = ForState(__instance); if (p == null || __originalMethod.Name != "OnEnter") return;
            if (p.Index == 12 && __instance.GetType().Name == "BackpackActivate" && __originalMethod.DeclaringType == GameType("EntityStates.Acolyte.Backpack.BackpackActivate"))
            {
                CharacterBody body = Body(__instance); body.MarkAllStatsDirty();
                if (B(p.Options, "Refresh Cooldowns On Cast") && body.hasEffectiveAuthority)
                    foreach (GenericSkill skill in body.GetComponents<GenericSkill>()) skill.Reset();
            }
        }
        private static Exception EndState(Exception __exception, Scope __state)
        {
            if (__state != null) { for (int i = __state.Saved.Count - 1; i >= 0; i--) __state.Saved[i].Item1.SetValue(null, __state.Saved[i].Item2); active = __state.Previous; }
            return __exception;
        }
        private static void BeforeStats(CharacterBody __instance)
        {
            if (!IsAcolyte(__instance) || stats == null) return;
            if (IsDark(__instance)) foreach (var pair in darkStats) Write(__instance, pair.Key, C.Get(pair.Value));
            else stats.Apply(__instance);
        }
        private static void AfterStats(CharacterBody __instance)
        {
            Component soul = Controller(__instance, "RoR2.AcolyteExtractedSoulController");
            if (soul && profiles.TryGetValue(2, out Profile bola) && OwnedSoul(soul)) __instance.maxHealth *= F(bola.Options, "Extracted Soul HP (%)") / 30f;
            if (!IsAcolyte(__instance)) return;
            if (grace != null && Has(__instance, graceItem)) __instance.armor += F(grace, "Armour Increase");
            if (favour != null && Has(__instance, favourItem)) __instance.armor += F(favour, "Armour Increase") - 20f;
            BodyData data = bodyData.GetOrCreateValue(__instance); data.DamageWithoutEcho = __instance.damage;
            if (!IsEcho(__instance) || !profiles.TryGetValue(12, out Profile echo)) return;
            Settings o = echo.Options;
            __instance.damage *= 1f + F(o, "Damage Gained (%)") / 100f;
            __instance.armor += F(o, "Armour Gained"); __instance.regen += F(o, "Health Regen Gained"); __instance.crit += F(o, "Critical Chance Gained (%)");
            __instance.maxHealth *= 1f + F(o, "Health Gained (%)") / 100f;
            __instance.maxShield *= 1f + F(o, "Shield Gained (%)") / 100f;
            __instance.moveSpeed *= 1f + F(o, "Move Speed Gained (%)") / 100f;
            __instance.attackSpeed *= 1f + F(o, "Attack Speed Gained (%)") / 100f;
            __instance.jumpPower *= 1f + F(o, "Jump Power Gained (%)") / 100f;
            __instance.acceleration *= 1f + F(o, "Acceleration Gained (%)") / 100f;
            Write(__instance, "sprintingSpeedMultiplier", N(__instance, "sprintingSpeedMultiplier", 1.45f) * (1f + F(o, "Sprint Speed Gained (%)") / 100f));
            __instance.maxJumpCount += I(o, "Extra Jumps");
        }
        private static void AfterBodyUpdate(CharacterBody __instance)
        {
            if (!IsAcolyte(__instance) || !NetworkServer.active) return;
            if (favour != null && Has(__instance, favourItem) && recoverBuff)
            {
                float fraction = N(__instance.healthComponent, "regainableHealth") / Mathf.Max(1f, __instance.healthComponent.fullHealth);
                __instance.SetBuffCount(recoverBuff.buffIndex, Mathf.CeilToInt(100f * fraction * F(favour, "Attack Speed Per Recoverable Health (%)")));
            }
        }
        private static float DecayRate(float original, HealthComponent health) => IsAcolyte(health.body) ? F(general, "Decay Per Second (%)") : original;
        private static float GraceDamageHeal(float original, Component component) => IsAcolyte(component.GetComponent<CharacterBody>()) ? F(grace, "Damage To Healing (%)") / 100f : original;
        private static float GraceDamageTaken(float original, Component component) => IsAcolyte(component.GetComponent<CharacterBody>()) ? F(grace, "Damage Taken Added To Recoverable Health (%)") / 100f : original;
        private static float GraceAttackSpeed(float original, Component component) => IsAcolyte(component.GetComponent<CharacterBody>()) ? F(grace, "Attack Speed Per Recoverable Health (%)") : original;
        private static float ExecuteThreshold(float original, DamageInfo info) => info.attacker && IsAcolyte(info.attacker.GetComponent<CharacterBody>()) && info.damageType.damageTypeExtended.ToString().Split(',').Any(t => t.Trim() == "ExecuteLowHealth") ? Mathf.Clamp01(F(profiles[1].Options, "Execution Threshold (%)") / 100f) : original;
        private static void DecayIL(ILContext il)
        {
            var cursor = new ILCursor(il);
            if (!cursor.TryGotoNext(MoveType.After, x => x.MatchLdsfld(typeof(HealthComponent).FullName, "regainableHealthPercentReductionPerSecond")))
                throw new InvalidOperationException("Acolyte recoverable health decay anchor was not found.");
            cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<float, HealthComponent, float>>(DecayRate);
        }
        private static void GraceIL(ILContext il)
        {
            foreach (string field in new[] { "regainPercentOfDealtDamage", "regainPercentOfReceivedDamage", "attackSpeedIncreasePerPercentRecoverableHealth" })
            {
                var cursor = new ILCursor(il);
                while (cursor.TryGotoNext(MoveType.After, x => x.MatchLdsfld("RoR2.Items.StealBackHealthBehaviour", field)))
                {
                    cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_0);
                    Func<float, Component, float> replacement = field == "regainPercentOfDealtDamage" ? GraceDamageHeal : field == "regainPercentOfReceivedDamage" ? GraceDamageTaken : GraceAttackSpeed;
                    cursor.EmitDelegate(replacement);
                }
            }
        }
        private static void BeforeHeal(HealthComponent __instance, ref ProcChainMask procChainMask)
        {
            if (!IsAcolyte(__instance.body) || !B(general, "Allow Healing From Other Sources")) return;
            if (Enum.TryParse("BypassConvertHealing", out ProcType bypass) && !procChainMask.HasProc(bypass) && (Has(__instance.body, graceItem) || Has(__instance.body, favourItem))) procChainMask.AddProc(bypass);
        }
        private static void BeforeRegenStacks(out bool __state) { __state = convertingRegen; convertingRegen = true; }
        private static Exception EndRegenStacks(Exception __exception, bool __state) { convertingRegen = __state; return __exception; }
        private static bool BeforeHealStacks(float __0, CharacterBody __1, ref int __result)
        {
            if (!IsAcolyte(__1)) return true;
            float amount = convertingRegen ? __0 * F(favour, "Health Regen To Permanent Stacks") / 2.5f : __0;
            float raw = amount / Mathf.Max(1f, __1.healthComponent.fullHealth) * 100f * F(favour, "Stacks Per 1% Maximum Health Healed");
            float existing = attackBuff ? __1.GetBuffCount(attackBuff) : 0;
            float divisor = 1f + Mathf.Max(0f, (existing + raw / 2f - 50f) * .05f);
            float gain = raw / divisor;
            if (gain <= 1f && raw > 1f) gain = 1f;
            __result = amount <= Mathf.Epsilon ? 0 : Mathf.FloorToInt(gain);
            if (!convertingRegen) __result += I(favour, "Flat Attack Speed Stacks From Healing") - 1;
            return false;
        }
        private static void OnDamageDealt(DamageReport report)
        {
            CharacterBody victim = report.victimBody;
            if (!IsAcolyte(victim) || !Has(victim, favourItem) || !victim.healthComponent.alive || IsEcho(victim) || IsDark(victim)) return;
            float amount = report.damageDealt * F(favour, "Damage Taken Added To Recoverable Health (%)") / 100f;
            Method(GameType("RoR2.HealthComponent"), "GrantRegainableHealthServer")?.Invoke(victim.healthComponent, new object[] { amount });
        }
        private static void AfterFormHealing(Component __instance, ref float __result)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>(); if (!IsAcolyte(body)) return;
            if (IsDark(body) && profiles.TryGetValue(11, out Profile dark)) __result = F(dark.Options, "Damage To Healing (%)") / 100f;
            else if (IsEcho(body) && profiles.TryGetValue(12, out Profile echo)) __result = F(echo.Options, "Damage To Healing (%)") / 100f;
        }
        private static void AfterSwap(Component __instance)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>(); if (!IsAcolyte(body)) return;
            if (profiles.TryGetValue(11, out Profile dark) && !B(dark.Options, "Pause Recoverable Health Decay"))
            {
                HealthComponent health = body.healthComponent;
                Write(health, "regainableHealthDepleteDisabledCounter", Mathf.Max(0, (int)N(health, "regainableHealthDepleteDisabledCounter") - 1));
                bodyData.GetOrCreateValue(body).DecaySuppressed = true;
            }
            body.MarkAllStatsDirty();
        }
        private static void BeforeUnswap(Component __instance)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>(); if (!IsAcolyte(body)) return;
            BodyData data = bodyData.GetOrCreateValue(body);
            if (data.DecaySuppressed) { Write(body.healthComponent, "regainableHealthDepleteDisabledCounter", (int)N(body.healthComponent, "regainableHealthDepleteDisabledCounter") + 1); data.DecaySuppressed = false; }
        }
        private static void BeforeForm(Component __instance)
        {
            if (IsAcolyte(__instance.GetComponent<CharacterBody>()) && profiles.TryGetValue(9, out Profile p)) Write(__instance, "maxSoul", I(p.Options, "Litany Threshold"));
        }
        private static void BeforeAddSouls(Component __instance, ref int __0)
        {
            if (!IsAcolyte(__instance.GetComponent<CharacterBody>())) return;
            BeforeForm(__instance);
            if (awardingKill && profiles.TryGetValue(9, out Profile p)) __0 = I(p.Options, "Extra Conviction On Kill");
        }
        private static void BeforeSoulKill(out bool __state) { __state = awardingKill; awardingKill = true; }
        private static Exception EndSoulKill(Exception __exception, bool __state) { awardingKill = __state; return __exception; }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static void BeforeProjectile(ref FireProjectileInfo __0)
        {
            Profile p = ForState(active); if (p == null || p.Skill.Damage == null) return;
            CharacterBody body = Body(active); string name = active.GetType().Name;
            float damage = C.Get(p.Skill.Damage);
            if (p.Index == 8) damage = name == "ReactivateSoulStorm" ? F(p.Options, "Empowered Storm Damage (%)") : C.Get(p.Skill.Damage);
            __0.damage = body.damage * damage / 100f;
            if (p.Skill.ProjectileSpeed != null) __0.speedOverride = p.Skill.LaunchSpeed(__0.speedOverride);
            if (p.Index == 5 && B(p.Options, "Stun On Hit"))
            {
                DamageTypeCombo type = __0.damageTypeOverride ?? DamageTypeCombo.GenericUtility;
                type |= DamageType.Stun1s; __0.damageTypeOverride = type;
            }
        }
        private static bool Same(GameObject instance, GameObject source) => instance && source && instance.name.Replace("(Clone)", "").Trim() == source.name;
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            if (!IsAcolyte(body)) return;
            foreach (Profile p in profiles.Values)
            {
                bool empowered = Same(__instance.gameObject, p.EmpoweredProjectile);
                if (!Same(__instance.gameObject, p.Projectile) && !empowered) continue;
                if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
                if (p.Skill.ProjectileSpeed != null)
                    foreach (Component component in __instance.GetComponents<Component>()) if (component && Read(component, "maxVelocity") is float) Write(component, "maxVelocity", C.Get(p.Skill.ProjectileSpeed));
                ProjectileImpactExplosion explosion = __instance.GetComponent<ProjectileImpactExplosion>();
                if (explosion && p.Skill.Radius != null) { explosion.blastRadius = C.Get(p.Skill.Radius); if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area)); }
                ProjectileOverlapAttack overlap = __instance.GetComponentInChildren<ProjectileOverlapAttack>();
                ProjectileDotZone zone = __instance.GetComponentInChildren<ProjectileDotZone>();
                if (p.Index == 5 || p.Index == 8)
                {
                    float interval = empowered ? F(p.Options, "Empowered Storm Interval") : C.Get(p.Skill.Interval);
                    if (overlap) { Write(overlap, "fireFrequency", 1f / interval); overlap.resetInterval = interval; Write(overlap, "damageCoefficient", 1f); }
                    if (zone) { zone.fireFrequency = 1f / interval; zone.resetFrequency = 1f / interval; Write(zone, "damageCoefficient", 1f); }
                    if (p.Index == 5 && B(p.Options, "Stun On Hit"))
                    {
                        ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
                        if (damage) damage.damageType |= DamageType.Stun1s;
                        OverlapAttack liveAttack = Read(overlap, "attack") as OverlapAttack ?? Read(zone, "attack") as OverlapAttack;
                        if (liveAttack != null) liveAttack.damageType |= DamageType.Stun1s;
                    }
                    float duration = p.Index == 8 && empowered ? F(p.Options, "Empowered Storm Duration") : F(p.Options, "AOE Duration");
                    if (duration < 0f) duration = float.PositiveInfinity;
                    Component lifetime = projectileLifetime(__instance.gameObject); if (lifetime) Write(lifetime, "lifetime", duration);
                    ProjectileSimple simple = __instance.GetComponent<ProjectileSimple>(); if (simple) simple.lifetime = duration;
                    if (NetworkServer.active)
                    {
                        AreaMarker marker = __instance.GetComponent<AreaMarker>(); if (!marker) marker = __instance.gameObject.AddComponent<AreaMarker>();
                        if (!marker.Registered)
                        {
                            marker.Registered = true; marker.Index = p.Index;
                            List<ProjectileController> list = areas.GetOrCreateValue(body).Values;
                            list.RemoveAll(c => !c); list.Add(__instance);
                            int maximum = I(p.Options, "Maximum AOE Instances");
                            if (maximum >= 0)
                            {
                                var matching = list.Where(c => c && c.GetComponent<AreaMarker>()?.Index == p.Index).ToList();
                                while (matching.Count > maximum) { ProjectileController oldest = matching[0]; matching.RemoveAt(0); list.Remove(oldest); if (oldest) NetworkServer.Destroy(oldest.gameObject); }
                            }
                        }
                    }
                }
            }
            if (profiles.TryGetValue(3, out Profile jar))
            {
                Component finder = __instance.GetComponent(GameType("RoR2.Projectile.ProjectileWightSphereTargetFinder"));
                if (finder)
                {
                    Write(finder, "lookRange", F(jar.Options, "Wight Seeking Range"));
                    Component seeking = __instance.GetComponent(GameType("RoR2.Projectile.SoulSearchController"));
                    if (seeking) Write(seeking, "maxSeekDistance", F(jar.Options, "Wight Seeking Range"));
                    ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
                    if (damage)
                    {
                        damage.damage = body.damage * F(jar.Options, "Wight Damage (%)") / 100f;
                        if (B(jar.Options, "Wight Can Be Lethal")) damage.damageType &= ~DamageType.NonLethal; else damage.damageType |= DamageType.NonLethal;
                    }
                }
            }
        }
        private static void BeforeOverlap(OverlapAttack __instance)
        {
            Profile p = ForState(active); if (p == null) return;
            CharacterBody body = Body(active); string name = active.GetType().Name;
            if (p.Index == 0)
            {
                bool quick = name.Contains("SprintStrike") || name.Contains("JumpStrike");
                float coefficient = quick ? F(p.Options, "Quickstrike Damage (%)") : (int)N(active, "step") == 2 ? F(p.Options, "Third Slash Damage (%)") : C.Get(p.Skill.Damage);
                __instance.damage = body.damage * coefficient / 100f;
                __instance.procCoefficient = quick ? F(p.Options, "Quickstrike Proc Coefficient") : C.Get(p.Skill.Proc);
            }
            else if (p.Index == 7) { __instance.damage = body.damage * C.Get(p.Skill.Damage) / 100f; __instance.procCoefficient = C.Get(p.Skill.Proc); }
            else if (p.Index == 12 && active.GetType().Namespace?.Contains("BackpackWeapon") == true)
            {
                float baseDamage = bodyData.TryGetValue(body, out BodyData data) && data.DamageWithoutEcho > 0f ? data.DamageWithoutEcho : body.damage / (1f + F(p.Options, "Damage Gained (%)") / 100f);
                string key = name.Contains("Catch") ? "Echo Blade Dash Damage (%)" : name.Contains("SprintStrike") || name.Contains("JumpStrike") ? "Echo Quickstrike Damage Per Hit (%)" : (int)N(active, "step") == 2 ? "Echo Third Slash Damage (%)" : "Echo Slash Damage (%)";
                __instance.damage = baseDamage * F(p.Options, key) / 100f;
                __instance.procCoefficient = C.Get(p.Skill.Proc);
            }
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(active); if (p == null) return;
            if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
            if (p.Index == 8)
            {
                __instance.baseDamage = Body(active).damage * F(p.Options, active.GetType().Name == "ReactivateSoulStorm" ? "Reactivation Blast Damage (%)" : "Cast Damage (%)") / 100f;
                __instance.radius = C.Get(p.Skill.Radius);
            }
        }
        private static bool OwnedSoul(Component soul)
        {
            Component extracted = soul.GetComponent(GameType("RoR2.ExtractedSoulController"));
            GenericOwnership ownership = extracted ? extracted.GetComponent<GenericOwnership>() : null;
            return ownership && ownership.ownerObject && IsAcolyte(ownership.ownerObject.GetComponent<CharacterBody>());
        }
        private static void BeforeSoul(Component __instance)
        {
            if (!profiles.TryGetValue(2, out Profile p)) return;
            // This controller belongs exclusively to souls extracted by Acolyte's bola.
            Write(__instance, "soulDuration", F(p.Options, "Soul Extraction Duration"));
            Write(__instance, "healthFractionFromOrigin", F(p.Options, "Extracted Soul HP (%)") / 100f);
            Write(__instance, "returnDamageCoefficient", F(p.Options, "Extracted Soul Damage (%)") / 100f);
            Write(__instance, "returnProcCoefficient", F(p.Options, "Soul Damage Proc Coefficient Modifier"));
        }
        private static void OnBodyStart(CharacterBody body)
        {
            Component soul = Controller(body, "RoR2.AcolyteExtractedSoulController");
            if (soul && OwnedSoul(soul)) { BeforeSoul(soul); body.RecalculateStats(); if (NetworkServer.active) body.healthComponent.Networkhealth = body.maxHealth; }
        }
        private static void BeforeWight(Component __instance)
        {
            GameObject owner = Read(__instance, "owner") as GameObject;
            if (owner && IsAcolyte(owner.GetComponent<CharacterBody>()) && profiles.TryGetValue(3, out Profile p)) Write(__instance, "damageCoefficient", F(p.Options, "Wight Damage (%)") / 100f);
        }
        private static IList DotStacks(DotController controller) => Read(controller, "dotStackList") as IList;
        private static void BeforeHaunt(DotController __instance, object[] __args, MethodBase __originalMethod, out HauntApplication __state)
        {
            __state = null; var parameters = __originalMethod.GetParameters();
            int dot = Array.FindIndex(parameters, p => p.ParameterType == typeof(DotController.DotIndex));
            int attacker = Array.FindIndex(parameters, p => p.Name == "attackerObject");
            int duration = Array.FindIndex(parameters, p => p.Name == "duration");
            if (dot < 0 || attacker < 0 || duration < 0 || __args[dot].ToString() != "Haunt" || !(__args[attacker] is GameObject source) || !source || !profiles.TryGetValue(3, out Profile profile)) return;
            CharacterBody body = source.GetComponent<CharacterBody>(); if (!IsAcolyte(body)) return;
            __args[duration] = F(profile.Options, "Haunt Duration");
            __state = new HauntApplication { Attacker = body };
        }
        private static void AfterHaunt(DotController __instance, HauntApplication __state)
        {
            if (__state == null) return;
            IList stacks = DotStacks(__instance); if (stacks == null) return;
            HauntData data = haunts.GetOrCreateValue(__instance);
            data.Ticks.RemoveAll(t => !stacks.Contains(t.Stack));
            foreach (object stack in stacks)
                if (Read(stack, "dotIndex")?.ToString() == "Haunt" && Read(stack, "attackerObject") as GameObject == __state.Attacker.gameObject && !data.Ticks.Any(t => ReferenceEquals(t.Stack, stack)))
                    data.Ticks.Add(new HauntTick { Stack = stack, Attacker = __state.Attacker });
        }
        private static void BeforeHauntTick(DotController __instance)
        {
            if (!NetworkServer.active || !haunts.TryGetValue(__instance, out HauntData data) || !profiles.TryGetValue(3, out Profile p)) return;
            IList stacks = DotStacks(__instance); if (stacks == null) return;
            GameObject target = Read(__instance, "victimObject") as GameObject;
            HealthComponent health = target ? target.GetComponent<HealthComponent>() : null;
            if (!health || !health.alive) return;
            for (int i = data.Ticks.Count - 1; i >= 0; i--)
            {
                HauntTick tick = data.Ticks[i];
                if (!stacks.Contains(tick.Stack)) { data.Ticks.RemoveAt(i); continue; }
                float remaining = N(tick.Stack, "timer") - Time.fixedDeltaTime;
                Write(tick.Stack, "timer", remaining); tick.Timer += Time.fixedDeltaTime;
                float interval = F(p.Options, "Haunt Damage Interval");
                while (tick.Timer >= interval && health.alive)
                {
                    tick.Timer -= interval;
                    if (tick.Attacker) health.TakeDamage(new DamageInfo { attacker = tick.Attacker.gameObject, inflictor = tick.Attacker.gameObject, damage = tick.Attacker.damage * F(p.Options, "Haunt Damage (%)") / 100f, procCoefficient = 0f, damageType = B(p.Options, "Haunt Can Be Lethal") ? DamageType.Generic : DamageType.NonLethal, position = health.body.corePosition, damageColorIndex = DamageColorIndex.SuperBleed, dotIndex = (DotController.DotIndex)Enum.Parse(typeof(DotController.DotIndex), "Haunt") });
                }
                if (remaining <= 0f && stacks.Contains(tick.Stack)) Method(typeof(DotController), "RemoveDotStackAtServer").Invoke(__instance, new object[] { stacks.IndexOf(tick.Stack) });
            }
        }
        private static void BeforeHauntEvaluation(DotController __instance, DotController.DotIndex dotIndex, out HauntEvaluation __state)
        {
            __state = null;
            if (dotIndex.ToString() != "Haunt" || !haunts.TryGetValue(__instance, out HauntData data)) return;
            IList stacks = DotStacks(__instance); if (stacks == null) return;
            __state = new HauntEvaluation();
            foreach (HauntTick tick in data.Ticks) if (stacks.Contains(tick.Stack)) { __state.Hidden.Add(tick.Stack); stacks.Remove(tick.Stack); }
        }
        private static void RestoreHauntEvaluation(DotController controller, HauntEvaluation state)
        {
            if (state == null) return; IList stacks = DotStacks(controller);
            if (stacks != null) foreach (object stack in state.Hidden) if (!stacks.Contains(stack)) stacks.Add(stack);
            state.Hidden.Clear();
        }
        private static void AfterHauntEvaluation(DotController __instance, HauntEvaluation __state, ref int remainingActive)
        {
            if (__state != null) remainingActive += __state.Hidden.Count;
            RestoreHauntEvaluation(__instance, __state);
        }
        private static Exception EndHauntEvaluation(DotController __instance, HauntEvaluation __state, Exception __exception)
        {
            RestoreHauntEvaluation(__instance, __state); return __exception;
        }
        private static void ExecuteIL(ILContext il)
        {
            var cursor = new ILCursor(il); int count = 0;
            while (cursor.TryGotoNext(MoveType.After, x => x.MatchLdsfld(typeof(HealthComponent).FullName, "lowHealthFraction")))
            {
                cursor.Emit(Mono.Cecil.Cil.OpCodes.Ldarg_1);
                cursor.EmitDelegate<Func<float, DamageInfo, float>>(ExecuteThreshold);
                count++;
            }
            if (count == 0) throw new InvalidOperationException("Acolyte execution threshold anchors were not found.");
        }
        private static string PassiveDescription(Settings s, bool favourPassive)
        {
            string text;
            if (favourPassive)
            {
                text = "Ordinary healing grants " + S.Damage("temporary attack speed") + " instead of restoring HP. ";
                int flat = I(s, "Flat Attack Speed Stacks From Healing");
                float scaling = F(s, "Stacks Per 1% Maximum Health Healed");
                if (flat > 0 || scaling > 0f)
                {
                    text += "Each heal grants ";
                    if (flat > 0) text += S.Damage(flat + "% attack speed");
                    if (flat > 0 && scaling > 0f) text += ", plus ";
                    if (scaling > 0f) text += S.Damage(S.Number(scaling) + "% attack speed") + " per " + S.Utility("1% of maximum health") + " it would restore, with diminishing returns";
                    text += ". ";
                }
                text += "Regeneration grants " + S.Damage("persistent attack speed") + " instead of restoring HP.";
            }
            else
            {
                bool healDirectly = general != null && B(general, "Allow Healing From Other Sources");
                text = healDirectly ? "Healing and regeneration can restore " + s.RecoveryWord + "." : "Convert healing and regeneration into " + s.RecoveryWord + ".";
                text += " Skill damage converts this reserve into healing, up to " + "<style=cIsHealing>" + S.Number(F(s, "Damage To Healing (%)")) + "% of damage dealt</style>" + ", limited by the amount stored.";
            }
            if (F(s, "Damage Taken Added To Recoverable Health (%)") > 0f)
                text += " Store " + S.Utility(S.Number(F(s, "Damage Taken Added To Recoverable Health (%)")) + "% of damage taken") + " as " + s.RecoveryWord + ".";
            if (F(s, "Attack Speed Per Recoverable Health (%)") > 0f)
                text += " Gain " + S.Damage(S.Number(F(s, "Attack Speed Per Recoverable Health (%)")) + "% attack speed") + " for each " + S.Utility("1% of maximum health") + " held as " + s.RecoveryWord + ".";
            if (F(s, "Armour Increase") != 0f) text += " Gain " + S.Utility(S.Number(F(s, "Armour Increase")) + " armour") + ".";
            return text;
        }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; Settings o = p.Options; string damage = s.Damage == null ? "" : S.Damage(S.Number(C.Get(s.Damage)) + "% damage");
            string text;
            switch (p.Index)
            {
                case 0: text = "Slash in a three-hit combo for " + damage + ". <style=cIsUtility>Quickstrike</style> while sprinting or jumping for " + S.Damage(S.Number(F(o, "Quickstrike Damage (%)")) + "% damage per hit") + "."; break;
                case 1: text = "Unleash torrents of slashes for " + damage + ", <style=cIsDamage>executing</style> enemies below " + S.Damage(S.Number(F(o, "Execution Threshold (%)")) + "% health") + "."; break;
                case 2: text = "Throw a <style=cIsDamage>stunning</style> bola for " + damage + ", gathering up to " + S.Utility(I(o, "Maximum Targets") + " enemies") + " and extracting their souls. Damage dealt to a soul is returned to its body for " + S.Damage(S.Number(F(o, "Extracted Soul Damage (%)")) + "% damage") + "."; break;
                case 3: text = "Throw a soul jar for " + damage + ", inflicting <style=cIsDamage>Haunt</style>. Release <style=cIsDamage>Wights</style> to seek nearby enemies."; break;
                case 4: text = "Release up to " + S.Utility(I(o, "Chains Released") + " chains") + ", pulling enemies and striking for " + S.Damage(S.Number(F(o, "Impact Damage (%)")) + "% damage") + "."; break;
                case 5: text = "Sling a spinning blade, dealing " + S.DamageText(C.Get(s.Damage), C.Get(s.Interval), true) + (B(o, "Stun On Hit") ? " and <style=cIsDamage>stunning</style> enemies hit" : "") + ". Reactivate to <style=cIsUtility>Blade Dash</style>, striking for " + S.Damage(DashDamage() + "% damage") + "."; break;
                case 6: text = "Throw an explosive javelin for " + damage + ". Reactivate to <style=cIsUtility>Blade Dash</style>, striking for " + S.Damage(DashDamage() + "% damage") + "."; break;
                case 7: text = "Dash to your thrown blade or javelin, striking enemies for " + damage + "."; break;
                case 8: text = "Call a soul storm, striking for " + S.Damage(S.Number(F(o, "Cast Damage (%)")) + "% damage") + " and dealing " + S.DamageText(C.Get(s.Damage), C.Get(s.Interval), true) + ". Reactivate for a " + S.Damage(S.Number(F(o, "Reactivation Blast Damage (%)")) + "% damage") + " blast and an empowered storm dealing " + S.DamageText(F(o, "Empowered Storm Damage (%)"), F(o, "Empowered Storm Interval"), true) + "."; break;
                case 9: text = "Sacrifice an enemy for " + damage + ", gaining " + S.Utility(I(o, "Convictions Gained") + " Convictions") + " and " + S.Utility(I(o, "Extra Conviction On Kill") + " extra on kill") + ". At " + S.Utility(I(o, "Litany Threshold") + " Convictions") + ", unleash your <style=cIsHealth>Litany</style>."; break;
                case 11: text = "Become the <style=cIsUtility>Anima Harvester</style>. Your health cannot fall below " + S.Utility("1 HP") + "."; break;
                case 12: text = "Summon an <style=cIsUtility>Anima Echo</style> to repeat your blade attacks. Gain " + S.Damage(S.Number(F(o, "Damage Gained (%)")) + "% damage") + " and " + S.Utility(S.Number(F(o, "Armour Gained")) + " armour") + "."; break;
                default: text = Regex.Replace(p.Text, @"(?:<style=cIsUtility>)?Agile\.(?:</style>)?\s*", "", RegexOptions.IgnoreCase); break;
            }
            return s.AgileText + text;
        }
        private static string DashDamage() => profiles.TryGetValue(7, out Profile p) ? S.Number(C.Get(p.Skill.Damage)) : "300";
        private static string Extra(Profile p)
        {
            string text = ""; S.Skill s = p.Skill; Settings o = p.Options;
            if (s.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(s.Radius)) + "m");
            if (s.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (p.Index == 2) text += "\nSoul Health: " + S.Utility(S.Number(F(o, "Extracted Soul HP (%)")) + "%") + "\nSoul Duration: " + S.Utility(S.Number(F(o, "Soul Extraction Duration")) + "s");
            if (p.Index == 8) text += "\nEmpowered Duration: " + S.Utility(S.Number(F(o, "Empowered Storm Duration")) + "s");
            if (p.Index == 11 || p.Index == 12)
                text += "\nConviction Loss: " + S.Utility("1 every " + S.Number(F(o, "Seconds Per Conviction Lost")) + "s") + RecoveryExtra(o, true);
            if (p.Index == 12) text += "\nEcho Slash: " + S.Damage(S.Number(F(o, "Echo Slash Damage (%)")) + "%") + "\nEcho Third Slash: " + S.Damage(S.Number(F(o, "Echo Third Slash Damage (%)")) + "%") + "\nEcho Quickstrike: " + S.Damage(S.Number(F(o, "Echo Quickstrike Damage Per Hit (%)")) + "% per hit") + "\nEcho Blade Dash: " + S.Damage(S.Number(F(o, "Echo Blade Dash Damage (%)")) + "%");
            return text;
        }
    }
}
