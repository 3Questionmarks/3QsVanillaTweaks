using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class FalseSon
    {
        private const string Root = "Survivors - 15 False Son", StatsRoot = "Survivor-Stats - 15 False Son";
        private static readonly string[] Names = { "Club of the Forsaken", "Lunar Spikes", "Lunar Stakes", "Step of the Brothers", "Meridians Will", "Laser of the Father", "Laser Burst" };
        private static readonly string[] States = { "ClubSwing", "LunarSpikes", "LunarStake", "StepBrothers", "MeridiansWillAim", "LaserFather", "LaserFatherBurstPrep" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile, Lightning;
            internal string NativeMachine;
            internal readonly Dictionary<string, ConfigEntry<float>> F = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> I = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> B = new Dictionary<string, ConfigEntry<bool>>();
            internal ConfigEntry<string> Refill;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class Runtime { internal float LaserTimer; internal bool Refilled; internal int DashHits; }
        private sealed class GrowthCache { internal float Shield = float.NaN; }
        private sealed class Shot : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; internal bool Lightning; }
        private sealed class Payment { internal int Cost; }
        private sealed class DotTimes { internal object Stack; internal float Timer, Duration; }
        private sealed class RuinScope { internal readonly List<DotTimes> Times = new List<DotTimes>(); }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<int, ConfigEntry<bool>> switches = new Dictionary<int, ConfigEntry<bool>>();
        private static readonly HashSet<MethodBase> stateHooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<EntityState, Runtime> runtime = new ConditionalWeakTable<EntityState, Runtime>();
        private static readonly ConditionalWeakTable<CharacterBody, GrowthCache> growthCache = new ConditionalWeakTable<CharacterBody, GrowthCache>();
        private static readonly Dictionary<string, ConfigEntry<float>> passive = new Dictionary<string, ConfigEntry<float>>();
        private static ConfigEntry<bool> enabled, statsEnabled, generalEnabled, passiveEnabled, ruinEnabled, shieldGrowth, ruinRefresh;
        private static ConfigEntry<float> growthRate, shieldEfficiency, ruinDamage, ruinHealing, ruinDuration;
        private static float nativeGrowthRate, nativeGrowthIncrease;
        private static string primaryMachine = "Weapon";
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState activeState;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.FalseSon." + name);
        private static object Sample(string name) => EntityStateCatalog.InstantiateState(State(name));
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsFalseSon(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags); if (field != null) return field.GetValue(obj);
                PropertyInfo property = type.GetProperty(name, flags); if (property?.GetGetMethod(true) != null) return property.GetValue(obj, null);
            }
            return null;
        }
        private static void Write(object obj, string name, object value)
        {
            if (obj == null) return;
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags); if (field != null) { field.SetValue(obj, value); return; }
                PropertyInfo property = type.GetProperty(name, flags); if (property?.GetSetMethod(true) != null) { property.SetValue(obj, value, null); return; }
            }
        }
        private static float Number(object obj, string key, float fallback = 0f) => Read(obj, key) is IConvertible n ? Convert.ToSingle(n, CultureInfo.InvariantCulture) : fallback;
        private static GameObject Asset(object obj) => obj as GameObject ?? Read(obj, "gameObject") as GameObject;
        private static Component Component(GameObject obj, string type) => obj && GameType(type) != null ? obj.GetComponent(GameType(type)) : null;
        private static float F(Profile p, string key) => C.Get(p.F[key]);
        private static int I(Profile p, string key) => C.Get(p.I[key]);
        private static bool B(Profile p, string key) => C.Get(p.B[key]);
        private static void Float(Profile p, string key, float value, float vanilla, string description, float minimum = 0f) => p.F[key] = S.Float(p.Skill.Section, key, value, vanilla, description, minimum);
        private static void Int(Profile p, string key, int value, int vanilla, string description, int minimum = 0) => p.I[key] = S.Int(p.Skill.Section, key, value, vanilla, description, minimum);
        private static void Bool(Profile p, string key, bool value, bool vanilla, string description) => p.B[key] = S.Bool(p.Skill.Section, key, value, vanilla, description);
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable False Son changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable False Son stat changes. Restart to apply. Vanilla: False.");
            generalEnabled = C.BindStartupBool(Root + " - Growth", "Changes Enabled", true, "Enable Growth changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Lunar Tampering", "Changes Enabled", true, "Enable Lunar Tampering changes. Restart to apply. Vanilla: False.");
            ruinEnabled = C.BindStartupBool(Root + " - Lunar Ruin", "Changes Enabled", true, "Enable Lunar Ruin changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(FalseSon), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(FalseSon), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(FalseSon), finalizer), transpiler: transpiler == null ? null : new HarmonyMethod(typeof(FalseSon), transpiler), ilmanipulator: null);
        }
        private static void Hook(Type type)
        {
            if (type == null) return;
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "Update", "OnExit", "DetonateAuthority", "FireBullet", "FireBurstLaser", "ApplyDamage" })
            {
                MethodInfo method = AccessTools.Method(type, name);
                if (method == null || method.DeclaringType.Namespace != "EntityStates.FalseSon" || !stateHooks.Add(method)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(FalseSon), nameof(BeforeState)), postfix: new HarmonyMethod(typeof(FalseSon), nameof(AfterState)), finalizer: new HarmonyMethod(typeof(FalseSon), nameof(EndScope)), ilmanipulator: null);
            }
        }
        private static void Load()
        {
            try
            {
                GameObject bodyObject = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/FalseSon/FalseSonBody.prefab").WaitForCompletion();
                if (!bodyObject) throw new InvalidOperationException("False Son body could not be loaded.");
                prefab = bodyObject.GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.FalseSon");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefab);
                Component controller = Component(bodyObject, "RoR2.FalseSonController");
                if (generalEnabled.Value) BindGrowth(controller);
                if (passiveEnabled.Value) BindPassive();
                if (ruinEnabled.Value) BindRuin();
                var defs = bodyObject.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                primaryMachine = defs.FirstOrDefault(d => d.activationState.stateType == State("ClubSwing"))?.activationStateMachineName ?? "Weapon";
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        Type state = State(States[i]); SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == state);
                        if (!def) { ThreeQsVanillaTweaks.Log.Warning("False Son skill unavailable: " + Names[i]); continue; }
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, state, switches[i]), NativeMachine = def.activationStateMachineName };
                        Bind(p, controller); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                        foreach (string name in RelatedStates(i)) Hook(State(name));
                        S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("False Son " + Names[i] + " changes could not be loaded: " + e); }
                }
                InstallHooks(); Apply(); C.EffectiveSettingsChanged += Apply;
                foreach (Profile p in profiles.Values)
                {
                    if ((p.Index == 1 || p.Index == 2) && ruinEnabled.Value) S.SetText("KEYWORD_3QS_FALSESON_RUIN", RuinDescription);
                    if (p.Index == 2) S.SetText("KEYWORD_3QS_FALSESON_LIGHTNING", () => "<style=cKeywordName>Lunar Lightning</style><style=cSub>Call down lightning on struck enemies for " + S.Number(F(p, "Lightning Strike Damage (%)")) + "% damage in a " + S.Number(C.Get(p.Skill.Radius)) + "m radius.</style>");
                    if (HasGrowth(p)) S.SetText("KEYWORD_3QS_FALSESON_GROWTH_" + p.Index, () => GrowthDescription(p));
                    if (p.Index == 4) S.SetText("KEYWORD_3QS_FALSESON_CORE", () => "<style=cKeywordName>Energized Core</style><style=cSub>Triple Lunar Tampering bonuses for " + S.Number(F(p, "Energized Core Duration")) + " seconds.</style>");
                }
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("False Son changes could not be loaded: " + e); }
        }
        private static IEnumerable<string> RelatedStates(int i)
        {
            switch (i)
            {
                case 0: return new[] { "ClubSwing", "ClubSwing3", "OverheadClubSwing", "ChargedClubSwing", "PreClubGroundSlam", "ClubGroundSlam" };
                case 1: return new[] { "LunarSpikes" };
                case 2: return new[] { "LunarStake" };
                case 3: return new[] { "StepBrothers" };
                case 4: return new[] { "MeridiansWillAim", "MeridiansWillTeleport", "MeridiansWillFire" };
                case 5: return new[] { "LaserFather", "LaserFatherCharged" };
                default: return new[] { "LaserFatherBurstPrep", "LaserFatherBurst" };
            }
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null || state.GetType().Namespace != "EntityStates.FalseSon") return null;
            foreach (Profile p in profiles.Values) if (RelatedStates(p.Index).Contains(state.GetType().Name)) return p;
            return null;
        }
        private static void BindGrowth(Component controller)
        {
            nativeGrowthRate = Number(controller, "growthStartCost", 25f);
            nativeGrowthIncrease = Number(controller, "defaultGrowthLevelCostIncrease", 5f);
            growthRate = S.Float(Root + " - Growth", "Growth Rate", nativeGrowthRate, nativeGrowthRate, "Bonus health needed for the first extra Lunar Spike. Later thresholds increase as normal.", 0.01f);
            shieldGrowth = S.Bool(Root + " - Growth", "Growth Can Use Shield", true, false, "Allow maximum shield to contribute to Growth.");
            shieldEfficiency = S.Float(Root + " - Growth", "Shield Efficiency (%)", 50f, 0f, "Shield effectiveness when contributing to Growth.");
        }
        private static void BindPassive()
        {
            foreach (string stock in new[] { "Unused", "Used" })
                foreach (string stat in new[] { "Attack Speed (%)", "Armour", "Movement Speed (%)", "Health Regeneration" })
                {
                    float native = stock == "Unused" ? stat == "Attack Speed (%)" ? 5f : stat == "Armour" ? 2.5f : 0f : stat == "Movement Speed (%)" ? 5f : stat == "Health Regeneration" ? 2.5f : 0f;
                    string key = stock + " Lunar Spike " + stat;
                    passive[key] = S.Float(Root + " - Lunar Tampering", key, native, native, stat + " gained per " + stock.ToLowerInvariant() + " Lunar Spike.");
                }
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            if (locator && locator.passiveSkill.enabled) S.SetText(locator.passiveSkill.skillDescriptionToken, PassiveDescription);
        }
        private static void BindRuin()
        {
            string section = Root + " - Lunar Ruin";
            ruinDamage = S.Float(section, "Bonus Damage Taken (%)", 10f, 10f, "Extra damage taken per Lunar Ruin stack.");
            ruinHealing = C.Bind(section, "Healing Reduction (%)", 20f, new ConfigDescription("Healing reduction while affected by Lunar Ruin. Vanilla: 20.", new AcceptableValueRange<float>(0f, 100f)));
            ruinDuration = S.Float(section, "Duration", 5f, 5f, "Lunar Ruin duration in seconds.");
            ruinRefresh = S.Bool(section, "Stacks Refresh Duration", true, true, "New Lunar Ruin stacks refresh existing stacks.");
        }
        private static void Bind(Profile p, Component controller)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(damage: false, changedAgile: i == 3 || i == 4 ? (bool?)true : null);
            object hit = i == 0 ? Sample("ChargedClubSwing") : i == 4 ? Sample("MeridiansWillFire") : i == 5 ? Sample("LaserFatherCharged") : i == 6 ? Sample("LaserFatherBurst") : sample;
            float nativeDamage = i == 4 ? Number(hit, "damage", 10f) : Number(i == 0 ? sample : hit, "damageCoefficient", 1f);
            float nativeProc = i == 5 ? Number(hit, "procCoefficientPerTick", 1f) : Number(hit, "procCoefficient", 1f);
            if (i == 5)
            {
                s.Damage = S.Float(s.Section, "Maximum Damage (%)", 175f, nativeDamage * 100f, "Maximum damage per laser tick as a percentage of base damage.");
                s.Proc = S.Float(s.Section, "Proc Coefficient", 0.75f, nativeProc, "Proc coefficient per laser tick.");
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.None, "Damage falloff over distance. Vanilla: Default.");
                s.Periodic = true;
            }
            else if (i == 4)
            {
                s.Damage = S.Float(s.Section, "Damage (%)", nativeDamage * 100f, nativeDamage * 100f, "Damage per hit as a percentage of base damage.");
                s.Proc = S.Float(s.Section, "Proc Coefficient", 0.5f, nativeProc, "Proc coefficient per hit.");
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.None, "Damage falloff over distance. Vanilla: None.");
            }
            else
            {
                if (i == 1 || i == 2)
                {
                    GameObject projectile = Asset(Read(sample, "projectilePrefab"));
                    if (projectile) nativeProc = Number(projectile.GetComponent<ProjectileController>(), "procCoefficient", nativeProc) * Number(projectile.GetComponent<ProjectileOverlapAttack>(), "overlapProcCoefficient", 1f);
                }
                s.BindDamage(nativeDamage * 100f, i == 3 ? (float?)200f : null, nativeProc);
            }
            if (i == 0 || i == 3 || i == 4)
            {
                float radius = i == 0 ? Number(hit, "blastRadius", 8f) : Number(hit, "blastRadius", 10f);
                s.Radius = S.Float(s.Section, "AOE Radius", i == 4 ? 25f : radius, radius, i == 0 ? "Base charged slam radius in metres. Charge increases the final radius." : "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
            }
            if (i == 1 || i == 2)
            {
                p.Projectile = Asset(Read(sample, "projectilePrefab"));
                if (!p.Projectile) throw new InvalidOperationException("Projectile prefab unavailable.");
                s.BindProjectileSpeed(p.Projectile);
            }
            if (i == 0)
            {
                Float(p, "Charged Slam Damage (%)", 1000f, Number(hit, "blastDamageCoefficient", 15f) * 100f, "Damage of a fully charged slam as a percentage of base damage.");
                Float(p, "Charged Slam Charge Time", 1.5f, Number(Sample("ClubSwing3"), "baseChargeDuration", 1.5f), "Time to fully charge a slam before attack speed is applied.", 0.001f);
                object fall = Sample("ClubGroundSlam");
                Float(p, "Falling Slam Proc Coefficient", 1f, Number(fall, "airTimeBonusProcCoefficient", 1f), "Proc coefficient of a falling slam after the airborne threshold.");
                Float(p, "Falling Slam Increased Blast Radius", Number(fall, "airTimeBlastRadiusMultiplier", 1f), Number(fall, "airTimeBlastRadiusMultiplier", 1f), "Extra radius multiplier per second spent falling.");
                Bool(p, "Fix Falling Slam Proc Coefficient Bug", true, false, "Keep the falling slam proc bonus from carrying over to later slams.");
            }
            if (i == 2)
            {
                Int(p, "Counts As Lunar Spikes", 2, (int)Number(controller, "lunarStakeStockStatValue", 2f), "Lunar Spikes represented by each Lunar Stake for Growth and Lunar Tampering.", 1);
                object lightning = Component(p.Projectile, "RoR2.Projectile.LunarStakesLightningController");
                p.Lightning = Asset(Read(lightning, "childProjectilePrefab"));
                Float(p, "Lightning Strike Damage (%)", 200f, Number(lightning, "childDamageCoefficient", 1f) * nativeDamage * 100f, "Lightning strike damage as a percentage of base damage.");
                float radius = Number(Component(p.Lightning, "RoR2.Projectile.ProjectileImpactExplosion"), "blastRadius", 6f);
                s.Radius = S.Float(s.Section, "Lightning Strike Radius", 6f, radius, "Lightning strike radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the lightning strike. Vanilla: None.");
                Int(p, "Maximum Pierces", 3, Math.Max(0, (int)Number(Component(p.Projectile, "RoR2.Projectile.ProjectileOverlapLimitHits"), "hitLimit", 4f) - 1), "Enemies a stake can pass through before its final hit. -1 is unlimited.", -1);
            }
            if (i == 3 || i == 4)
            {
                Float(p, "Movement Speed Range Modifier (%)", 50f, 100f, "Effect of movement speed changes on travel range. 0 uses your base movement speed.");
                Bool(p, "Can Use With Club And Laser", true, p.NativeMachine != primaryMachine, "Allow this skill without interrupting the club charge or laser.");
            }
            if (i == 3)
            {
                Bool(p, "Fix Hidden Attack Proc Coefficient", true, false, "Prevent the hidden enemy detection attack from triggering item effects.");
                float duration = Number(sample, "dashDuration", 0.3f);
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Dash duration in seconds.", 0.001f);
            }
            if (i == 4)
            {
                Float(p, "Energized Core Duration", 4f, Number(hit, "coreBuffDuration", 4f), "Duration of Energized Core in seconds.");
                Float(p, "Growth Extra Duration", 0.33f, Number(hit, "bonusDurationFromGrowth", 0.33f), "Extra Energized Core duration per Growth level.");
                Float(p, "Pull Radius", 15f, Number(hit, "forcePullRadius", 15f), "Radius within which enemies are pulled inward, in metres.");
                Float(p, "Base Teleport Range", 35f, Number(sample, "maxDistanceForIndicator", 35f) + Number(sample, "speedModifier") * prefab.baseMoveSpeed, "Teleport range at your base movement speed, in metres.");
            }
            if (i == 5)
            {
                Float(p, "Minimum Damage (%)", 30f, nativeDamage * 17.5f, "Minimum damage per laser tick as a percentage of base damage.");
                Float(p, "Laser Charge Time", 2f, Number(sample, "baseChargeDuration", 2f), "Time to fully charge the laser before attack speed is applied.", 0.001f);
                s.Interval = S.Float(s.Section, "Damage Interval", 0.125f, 1f / Math.Max(0.001f, Number(hit, "baseFireFrequency", 8f)), "Seconds between laser hits before attack speed is applied.", 0.001f);
                s.Duration = S.Float(s.Section, "Duration", 5f, Number(hit, "maximumDuration", 6f), "Base laser duration in seconds.", 0.001f);
                Bool(p, "Fix Attack Speed Scaling", true, false, "Allow attack speed to increase the laser tick rate normally.");
                Float(p, "Growth Duration Increase", 2f / 3f, 2f / 3f, "Extra laser duration from the first Growth level. Later levels give smaller increases as normal.");
                p.Refill = C.Bind(s.Section, "Recharge Lunar Spikes Or Stakes", "All", "Stocks restored when the laser begins. Use a whole number, percentage, or All. Vanilla: Accelerates secondary recharge during the laser.");
            }
            if (i == 6)
            {
                p.Refill = C.Bind(s.Section, "Recharge Lunar Spikes Or Stakes", "50%", "Stocks restored when the burst begins. Use a whole number, percentage, or All. Vanilla: " + S.Number(Number(sample, "secondaryRefillPercentage", 0.5f) * 100f) + "%.");
                Float(p, "Growth Stock Gain", 0.25f, 1f / Math.Max(1f, Number(controller, "laserBurstStockStatValue", 4f)), "Extra Laser Burst stocks gained per Growth level.");
            }
        }
        private static void InstallHooks()
        {
            Type controller = GameType("RoR2.FalseSonController");
            if (stats != null)
            {
                Patch(typeof(CharacterBody), "Start", postfix: nameof(AfterBody));
                Patch(typeof(CharacterBody), "RecalculateStats", prefix: nameof(BeforeStats));
            }
            if (generalEnabled.Value)
            {
                Patch(controller, "Start", prefix: nameof(BeforeControllerStart));
                Patch(controller, "GetBonusHealth", postfix: nameof(AfterBonusHealth));
                Patch(controller, "Update", prefix: nameof(BeforeControllerUpdate));
            }
            if (passiveEnabled.Value)
            {
                Patch(typeof(CharacterBody), "ComputeTamperedHeartBonus", prefix: nameof(BeforeTampering));
                Patch(typeof(CharacterBody), "RecalculateStats", transpiler: nameof(TamperingGatesIL));
            }
            if (profiles.ContainsKey(2) || profiles.ContainsKey(6))
            {
                Patch(controller, "Start", prefix: nameof(BeforeControllerStocks));
                if (profiles.ContainsKey(6)) Patch(controller, "GetGrowthSpecialStock", prefix: nameof(BeforeBurstStocks));
            }
            if (profiles.ContainsKey(5)) Patch(controller, "GetGrowthLaserBonusDuration", prefix: nameof(BeforeLaserGrowth));
            if (ruinEnabled.Value)
            {
                Patch(typeof(HealthComponent), "Heal", transpiler: nameof(RuinHealIL), args: new[] { typeof(float), typeof(ProcChainMask), typeof(bool) });
                Patch(typeof(HealthComponent), "TakeDamageProcess", transpiler: nameof(RuinDamageIL));
                Patch(typeof(DotController), "InflictDot", prefix: nameof(BeforeRuin), finalizer: nameof(AfterRuin), args: new[] { typeof(InflictDotInfo).MakeByRefType() });
            }
            if (profiles.Count == 0) return;
            Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
            Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
            Patch(typeof(SkillDef), "CanExecute", postfix: nameof(AfterRequired));
            Patch(typeof(SkillDef), "OnFixedUpdate", prefix: nameof(BeforeSkillTick));
            if (profiles.ContainsKey(0) || profiles.ContainsKey(3)) Patch(typeof(BlastAttack), "Fire", prefix: nameof(BeforeBlast), args: Type.EmptyTypes);
            if (profiles.ContainsKey(0) || profiles.ContainsKey(3)) Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap), nameof(AfterOverlap), args: new[] { typeof(List<HurtBox>) });
            if (profiles.ContainsKey(1) || profiles.ContainsKey(2))
            {
                Patch(typeof(ProjectileManager), "FireProjectile", prefix: nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(ProjectileController), "Start", postfix: nameof(AfterProjectile));
            }
            if (profiles.ContainsKey(4) || profiles.ContainsKey(1) || profiles.ContainsKey(2)) Patch(typeof(HealthComponent), "TakeDamageProcess", prefix: nameof(BeforeDamage));
            if (profiles.ContainsKey(5) || profiles.ContainsKey(6)) Patch(typeof(BulletAttack), "Fire", prefix: nameof(BeforeBullet), args: Type.EmptyTypes);
        }
        private static void Apply()
        {
            stats?.Apply(prefab);
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 3 || p.Index == 4)
                {
                    string machine = p.NativeMachine;
                    if (B(p, "Can Use With Club And Laser"))
                    {
                        EntityStateMachine bodyMachine = prefab.GetComponents<EntityStateMachine>().FirstOrDefault(m => m.customName == "Body");
                        if (bodyMachine) machine = bodyMachine.customName;
                    }
                    else
                    {
                        machine = primaryMachine;
                    }
                    p.Skill.Definition.activationStateMachineName = machine;
                }
                if ((p.Index == 1 || p.Index == 2) && ruinEnabled.Value)
                {
                    ReplaceKeyword(p, "LUNARRUIN", "KEYWORD_3QS_FALSESON_RUIN", "Lunar Ruin");
                    ReplaceKeyword(p, "LUNAR_RUIN", "KEYWORD_3QS_FALSESON_RUIN");
                }
                if (p.Index == 2)
                {
                    ReplaceKeyword(p, "LUNARLIGHTNING", "KEYWORD_3QS_FALSESON_LIGHTNING", "Lunar Lightning");
                    ReplaceKeyword(p, "LUNAR_LIGHTNING", "KEYWORD_3QS_FALSESON_LIGHTNING");
                }
                if (HasGrowth(p)) ReplaceKeyword(p, "GROWTH", "KEYWORD_3QS_FALSESON_GROWTH_" + p.Index, "Growth");
                if (p.Index == 4) ReplaceKeyword(p, "ENERGIZEDCORE", "KEYWORD_3QS_FALSESON_CORE", "Energized Core");
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (IsFalseSon(body))
                {
                    stats?.Apply(body); body.MarkAllStatsDirty();
                    object controller = Component(body.gameObject, "RoR2.FalseSonController");
                    if (generalEnabled.Value) Write(controller, "forceUpdateGrowth", true);
                    Write(controller, "forceUpdateSpecialSkillStock", true); Write(controller, "forceUpdateSecondarySkillStock", true);
                }
        }
        private static void ReplaceKeyword(Profile p, string match, string token, string title = null)
        {
            p.Skill.Definition.keywordTokens = (p.Skill.Definition.keywordTokens ?? Array.Empty<string>())
                .Where(t => !IsKeyword(t, match, title)).ToArray();
            p.Skill.SetKeyword(token, true);
        }
        private static bool IsKeyword(string token, string match, string title)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (title == null) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(Language.GetString(token) ?? "",
                @"<style=cKeywordName>\s*\[?\s*" + System.Text.RegularExpressions.Regex.Escape(title) + @"\s*\]?\s*</style>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        private static void AfterBody(CharacterBody __instance) { if (IsFalseSon(__instance)) stats?.Apply(__instance); }
        private static void BeforeStats(CharacterBody __instance) { if (IsFalseSon(__instance)) stats?.Apply(__instance); }
        private static void BeforeControllerStart(Component __instance)
        {
            if (!IsFalseSon(__instance.GetComponent<CharacterBody>())) return;
            Write(__instance, "growthStartCost", C.Get(growthRate));
            Write(__instance, "defaultGrowthLevelCostIncrease", nativeGrowthIncrease * C.Get(growthRate) / Math.Max(0.01f, nativeGrowthRate));
        }
        private static void BeforeControllerUpdate(Component __instance)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (!IsFalseSon(body)) return;
            GrowthCache cache = growthCache.GetOrCreateValue(body);
            float shield = C.Get(shieldGrowth) ? body.maxShield * C.Get(shieldEfficiency) / 100f : 0f;
            if (cache.Shield != shield) { cache.Shield = shield; Write(__instance, "forceUpdateGrowth", true); }
            if (Math.Abs(Number(__instance, "growthStartCost") - C.Get(growthRate)) > 0.0001f)
            {
                BeforeControllerStart(__instance);
                AccessTools.Method(__instance.GetType(), "ExpandGrowthLevelTotalCosts").Invoke(__instance, new object[] { true });
                Write(__instance, "growthLevel", 0); Write(__instance, "forceUpdateGrowth", true);
            }
        }
        private static void AfterBonusHealth(Component __instance, ref float __result)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (IsFalseSon(body) && C.Get(shieldGrowth)) __result += body.maxShield * C.Get(shieldEfficiency) / 100f;
        }
        private static void BeforeControllerStocks(Component __instance)
        {
            if (!IsFalseSon(__instance.GetComponent<CharacterBody>())) return;
            if (profiles.TryGetValue(2, out Profile p)) Write(__instance, "lunarStakeStockStatValue", I(p, "Counts As Lunar Spikes"));
        }
        private static bool BeforeBurstStocks(Component __instance, ref int __result)
        {
            if (!IsFalseSon(__instance.GetComponent<CharacterBody>()) || !profiles.TryGetValue(6, out Profile p)) return true;
            __result = Mathf.FloorToInt(Number(__instance, "growthLevel") * F(p, "Growth Stock Gain")); return false;
        }
        private static float GrowthLaserDuration(int growth, float firstBonus)
        {
            float duration = 0f;
            for (int i = 1; i <= Math.Min(Math.Max(0, growth), 49); i++) duration += 2f / (i + 2f);
            return duration * firstBonus / (2f / 3f);
        }
        private static bool BeforeLaserGrowth(Component __instance, ref float __result)
        {
            if (!IsFalseSon(__instance.GetComponent<CharacterBody>()) || !profiles.TryGetValue(5, out Profile p)) return true;
            __result = GrowthLaserDuration((int)Number(__instance, "growthLevel"), F(p, "Growth Duration Increase")); return false;
        }
        private static bool BeforeTampering(CharacterBody __instance, object __0, ref float __result)
        {
            if (!IsFalseSon(__instance)) return true;
            string stat;
            switch (__0.ToString())
            {
                case "AttackSpeed": stat = "Attack Speed (%)"; break;
                case "Armor": stat = "Armour"; break;
                case "MoveSpeed": stat = "Movement Speed (%)"; break;
                case "HealthRegen": stat = "Health Regeneration"; break;
                default: return true;
            }
            float unused = __instance.GetBuffCount(DLC2Content.Buffs.TamperingASAndArmorBonus);
            float used = __instance.GetBuffCount(DLC2Content.Buffs.TamperingMSAndHRBonus);
            float value = unused * C.Get(passive["Unused Lunar Spike " + stat]) + used * C.Get(passive["Used Lunar Spike " + stat]);
            if (stat.EndsWith("(%)", StringComparison.Ordinal)) value /= 100f;
            __result = value * (__instance.HasBuff(DLC2Content.Buffs.EnergizedCore) ? 3f : 1f); return false;
        }
        private static bool HasTampering(CharacterBody body, BuffDef buff) => IsFalseSon(body) ? body.HasBuff(DLC2Content.Buffs.TamperingASAndArmorBonus) || body.HasBuff(DLC2Content.Buffs.TamperingMSAndHRBonus) : body.HasBuff(buff);
        private static IEnumerable<CodeInstruction> TamperingGatesIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int count = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].operand is FieldInfo field && (field.Name == "TamperingASAndArmorBonus" || field.Name == "TamperingMSAndHRBonus"))
                    for (int j = i + 1; j < Math.Min(i + 4, code.Count); j++)
                        if (code[j].operand is MethodInfo method && method.Name == "HasBuff" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(BuffDef))
                        { code[j].opcode = OpCodes.Call; code[j].operand = AccessTools.Method(typeof(FalseSon), nameof(HasTampering)); count++; break; }
            }
            if (count != 4) ThreeQsVanillaTweaks.Log.Warning("False Son: expected four Lunar Tampering stat checks, found " + count + ".");
            return code;
        }
        private static float RuinDamageBonus() => C.Get(ruinDamage) / 100f;
        private static float RuinHealMultiplier() => 1f - C.Get(ruinHealing) / 100f;
        private static IEnumerable<CodeInstruction> RuinDamageIL(IEnumerable<CodeInstruction> instructions) => RuinConstantIL(instructions, 0.1f, nameof(RuinDamageBonus));
        private static IEnumerable<CodeInstruction> RuinHealIL(IEnumerable<CodeInstruction> instructions) => RuinConstantIL(instructions, 0.8f, nameof(RuinHealMultiplier));
        private static IEnumerable<CodeInstruction> RuinConstantIL(IEnumerable<CodeInstruction> instructions, float original, string replacement)
        {
            var code = instructions.ToList(); bool found = false;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is FieldInfo field && field.Name == "lunarruin")
                    for (int j = i + 1; j < Math.Min(code.Count, i + 24); j++)
                        if (code[j].opcode == OpCodes.Ldc_R4 && code[j].operand is float f && Math.Abs(f - original) < 0.000001f)
                        { code[j].opcode = OpCodes.Call; code[j].operand = AccessTools.Method(typeof(FalseSon), replacement); found = true; break; }
            if (!found) ThreeQsVanillaTweaks.Log.Warning("False Son: Lunar Ruin modifier could not be located: " + replacement);
            return code;
        }
        private static void BeforeRuin(ref InflictDotInfo inflictDotInfo, out RuinScope __state)
        {
            __state = null;
            if (!NetworkServer.active || inflictDotInfo.dotIndex.ToString() != "LunarRuin") return;
            inflictDotInfo.duration = C.Get(ruinDuration);
            if (C.Get(ruinRefresh) || !inflictDotInfo.victimObject) return;
            IDictionary locator = AccessTools.Field(typeof(DotController), "dotControllerLocator")?.GetValue(null) as IDictionary;
            object controller = locator?[inflictDotInfo.victimObject.GetInstanceID()];
            if (!(Read(controller, "dotStackList") is IEnumerable stacks)) return;
            __state = new RuinScope();
            foreach (object stack in stacks)
                if (Read(stack, "dotIndex")?.ToString() == "LunarRuin") __state.Times.Add(new DotTimes { Stack = stack, Timer = Number(stack, "timer"), Duration = Number(stack, "totalDuration") });
        }
        private static Exception AfterRuin(RuinScope __state, Exception __exception)
        {
            if (__state != null) foreach (DotTimes time in __state.Times) { Write(time.Stack, "timer", time.Timer); Write(time.Stack, "totalDuration", time.Duration); }
            return __exception;
        }
        private static void Set(Scope scope, object instance, string field, object value)
        {
            FieldInfo info = AccessTools.Field(instance.GetType(), field);
            if (info == null) { Write(instance, field, value); return; }
            if (info.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(info, info.GetValue(null)));
            info.SetValue(info.IsStatic ? null : instance, value);
        }
        private static float MovementSpeed(CharacterBody body, float percentage)
        {
            float baseline = body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
            return Math.Max(0f, baseline + (body.moveSpeed - baseline) * percentage / 100f);
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = null; Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p == null || !IsFalseSon(body)) return;
            __state = new Scope { Previous = activeState }; activeState = __instance;
            string name = __instance.GetType().Name;
            if (p.Index == 0)
            {
                if (name == "ClubSwing" || name == "OverheadClubSwing") { Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f); Set(__state, __instance, "procCoefficient", C.Get(p.Skill.Proc)); }
                if (name == "ClubSwing3") Set(__state, __instance, "baseChargeDuration", F(p, "Charged Slam Charge Time"));
                if (name == "ChargedClubSwing" || name == "ClubGroundSlam")
                {
                    Set(__state, __instance, "blastDamageCoefficient", F(p, "Charged Slam Damage (%)") / 100f);
                    Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius));
                    if (name == "ChargedClubSwing" || B(p, "Fix Falling Slam Proc Coefficient Bug")) Set(__state, __instance, "blastProcCoefficient", C.Get(p.Skill.Proc));
                    if (name == "ClubGroundSlam")
                    {
                        Set(__state, __instance, "airTimeBonusProcCoefficient", F(p, "Falling Slam Proc Coefficient"));
                        Set(__state, __instance, "airTimeBlastRadiusMultiplier", F(p, "Falling Slam Increased Blast Radius"));
                    }
                }
            }
            if (p.Index == 1 || p.Index == 2) Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Index == 3)
            {
                Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f); Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius));
                Set(__state, __instance, "dashDuration", C.Get(p.Skill.Duration));
                if (__originalMethod.Name == "FixedUpdate") Write(__instance, "moveSpeedStat", MovementSpeed(body, F(p, "Movement Speed Range Modifier (%)")));
            }
            if (p.Index == 4)
            {
                if (name == "MeridiansWillAim" && __originalMethod.Name == "OnEnter")
                {
                    float modifier = Number(__instance, "speedModifier");
                    float effective = MovementSpeed(body, F(p, "Movement Speed Range Modifier (%)"));
                    Set(__state, __instance, "maxDistanceForIndicator", Math.Max(0f, F(p, "Base Teleport Range") + modifier * (effective - body.baseMoveSpeed)) - modifier * body.moveSpeed);
                }
                if (name == "MeridiansWillFire")
                {
                    Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius)); Set(__state, __instance, "forcePullRadius", F(p, "Pull Radius"));
                    Set(__state, __instance, "coreBuffDuration", F(p, "Energized Core Duration")); Set(__state, __instance, "bonusDurationFromGrowth", F(p, "Growth Extra Duration")); Set(__state, __instance, "damage", C.Get(p.Skill.Damage) / 100f);
                }
            }
            if (p.Index == 5)
            {
                if (name == "LaserFather") Set(__state, __instance, "baseChargeDuration", F(p, "Laser Charge Time"));
                if (name == "LaserFatherCharged")
                {
                    Set(__state, __instance, "maximumDuration", C.Get(p.Skill.Duration));
                    Set(__state, __instance, "baseFireFrequency", 1f / C.Get(p.Skill.Interval));
                    Set(__state, __instance, "procCoefficientPerTick", C.Get(p.Skill.Proc));
                    // Secondary stocks are restored explicitly rather than by advancing their recharge timer.
                    Set(__state, __instance, "spikeRefillAmountPerSecond", 0f);
                    if (__originalMethod.Name == "FixedUpdate" && B(p, "Fix Attack Speed Scaling")) Write(__instance, "fireStopwatch", float.NegativeInfinity);
                }
            }
            if (p.Index == 6 && name == "LaserFatherBurstPrep") Set(__state, __instance, "secondaryRefillPercentage", 0f);
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p == null || !IsFalseSon(body)) return;
            string name = __instance.GetType().Name;
            if (__originalMethod.Name == "OnEnter")
            {
                if (p.Index == 3 && Read(__instance, "overlapAttack") is OverlapAttack overlap)
                    overlap.procCoefficient = B(p, "Fix Hidden Attack Proc Coefficient") ? 0f : C.Get(p.Skill.Proc);
                if ((p.Index == 5 && name == "LaserFatherCharged") || (p.Index == 6 && name == "LaserFatherBurstPrep"))
                {
                    Runtime data = runtime.GetOrCreateValue(__instance);
                    if (!data.Refilled && (bool)(Read(__instance, "isAuthority") ?? false))
                    {
                        GenericSkill slot = body.GetComponent<SkillLocator>().secondary;
                        int restored = RefillCount(C.Get(p.Refill), slot.maxStock, slot.maxStock - slot.stock);
                        for (int i = 0; i < restored; i++) slot.AddOneStock();
                        data.Refilled = true;
                    }
                }
            }
            if (p.Index == 5 && name == "LaserFatherCharged" && __originalMethod.Name == "FixedUpdate" && B(p, "Fix Attack Speed Scaling") && (bool)(Read(__instance, "isAuthority") ?? false))
            {
                Runtime data = runtime.GetOrCreateValue(__instance);
                float interval = C.Get(p.Skill.Interval) / Math.Max(0.01f, body.attackSpeed);
                int ticks = TakeLaserTicks(data, Time.fixedDeltaTime, interval);
                for (int tick = 0; tick < ticks; tick++)
                {
                    Ray ray = (Ray)Read(__instance, "aimRay");
                    Transform muzzle = Read(__instance, "muzzleTransform") as Transform;
                    Transform end = Read(__instance, "laserEffectEnd") as Transform;
                    Vector3 origin = muzzle ? muzzle.position : ray.origin;
                    Vector3 target = end ? end.position : ray.GetPoint(Number(__instance, "maxDistance", 100f));
                    Vector3 direction = target - origin;
                    var actualRay = new Ray(origin, direction.sqrMagnitude > 0f ? direction.normalized : ray.direction);
                    AccessTools.Method(__instance.GetType(), "FireBullet").Invoke(__instance, new object[] { Read(__instance, "modelTransform"), actualRay, "MuzzleLaser", direction.magnitude + 0.1f });
                }
            }
        }
        private static int TakeLaserTicks(Runtime data, float deltaTime, float interval)
        {
            data.LaserTimer += Math.Max(0f, deltaTime);
            int count = 0;
            while (data.LaserTimer >= interval && count < 256) { data.LaserTimer -= interval; count++; }
            return count;
        }
        private static int RefillCount(string text, int maximum, int missing)
        {
            int requested = string.Equals(text?.Trim(), "All", StringComparison.OrdinalIgnoreCase) ? maximum : S.Reload(text, maximum, 0);
            return Math.Max(0, Math.Min(missing, requested));
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null)
            {
                for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value);
                activeState = __state.Previous;
            }
            return __exception;
        }
        private static void BeforeOverlap(OverlapAttack __instance, ref List<HurtBox> hitResults)
        {
            Profile p = ForState(activeState);
            if (p == null || !IsFalseSon(Body(activeState)) || __instance.attacker != Body(activeState).gameObject) return;
            if (p.Index == 0) __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Index == 3)
            {
                if (B(p, "Fix Hidden Attack Proc Coefficient")) __instance.procCoefficient = 0f;
                if (hitResults == null) hitResults = new List<HurtBox>();
                runtime.GetOrCreateValue(activeState).DashHits = 0;
            }
        }
        private static void AfterOverlap(OverlapAttack __instance, List<HurtBox> hitResults, bool __result)
        {
            Profile p = ForState(activeState);
            if (p?.Index == 3 && IsFalseSon(Body(activeState)) && __instance.attacker == Body(activeState).gameObject && __result)
                runtime.GetOrCreateValue(activeState).DashHits = Math.Max(1, (hitResults ?? new List<HurtBox>()).Where(h => h && h.healthComponent).Select(h => h.healthComponent).Distinct().Count());
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p == null || !IsFalseSon(body) || __instance.attacker != body.gameObject) return;
            if (p.Index == 0)
            {
                if (activeState.GetType().Name == "ClubGroundSlam" && B(p, "Fix Falling Slam Proc Coefficient Bug"))
                {
                    float airTime = Number(activeState, "timeInAir");
                    __instance.procCoefficient = airTime >= Number(activeState, "airTimeUntilBonusProcCoefficientApplied") ? F(p, "Falling Slam Proc Coefficient") : C.Get(p.Skill.Proc);
                }
                else if (activeState.GetType().Name != "ClubGroundSlam") __instance.procCoefficient = C.Get(p.Skill.Proc);
            }
            if (p.Index == 3)
            {
                __instance.baseDamage = body.damage * C.Get(p.Skill.Damage) / 100f * Math.Max(1, runtime.GetOrCreateValue(activeState).DashHits);
                __instance.procCoefficient = C.Get(p.Skill.Proc);
            }
            if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
        }
        private static bool Matches(GameObject prefabObject, ProjectileController pc)
        {
            if (!prefabObject || !pc) return false;
            ProjectileController original = prefabObject.GetComponent<ProjectileController>();
            return original && original.catalogIndex >= 0 && original.catalogIndex == pc.catalogIndex || pc.gameObject.name == prefabObject.name + "(Clone)";
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            GameObject owner = fireProjectileInfo.owner;
            if (!owner || !IsFalseSon(owner.GetComponent<CharacterBody>())) return;
            GameObject projectile = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(v => v.Projectile && v.Projectile == projectile);
            if (p == null) return;
            fireProjectileInfo.damage = owner.GetComponent<CharacterBody>().damage * C.Get(p.Skill.Damage) / 100f;
            fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectile(ProjectileController __instance)
        {
            if (!__instance.owner || !IsFalseSon(__instance.owner.GetComponent<CharacterBody>())) return;
            Profile p = profiles.Values.FirstOrDefault(v => Matches(v.Projectile, __instance) || Matches(v.Lightning, __instance));
            if (p == null) return;
            bool lightning = Matches(p.Lightning, __instance);
            Shot shot = __instance.GetComponent<Shot>() ?? __instance.gameObject.AddComponent<Shot>();
            shot.Profile = p; shot.Origin = __instance.owner.transform.position; shot.Lightning = lightning;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (__instance.TryGetComponent<ProjectileOverlapAttack>(out var overlap)) overlap.overlapProcCoefficient = 1f;
            if (p.Index == 2)
            {
                object limit = Component(__instance.gameObject, "RoR2.Projectile.ProjectileOverlapLimitHits");
                if (!lightning && limit != null) Write(limit, "hitLimit", I(p, "Maximum Pierces") < 0 ? int.MaxValue : I(p, "Maximum Pierces") + 1);
                if (lightning)
                {
                    ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
                    if (damage) damage.damage = __instance.owner.GetComponent<CharacterBody>().damage * F(p, "Lightning Strike Damage (%)") / 100f;
                    ProjectileImpactExplosion explosion = __instance.GetComponent<ProjectileImpactExplosion>();
                    if (explosion) { explosion.blastRadius = C.Get(p.Skill.Radius); explosion.falloffModel = Area(C.Get(p.Skill.Area)); }
                }
            }
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static BulletAttack.FalloffModel BulletFalloff(S.BulletFalloff value) => value == S.BulletFalloff.Default ? BulletAttack.FalloffModel.DefaultBullet : value == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.None;
        private static float AreaMultiplier(float fraction, S.AreaFalloff falloff)
        {
            fraction = Mathf.Clamp01(fraction);
            switch (falloff)
            {
                case S.AreaFalloff.Linear: return 1f - fraction;
                case S.AreaFalloff.HalfLinear: return 1f - fraction * 0.5f;
                case S.AreaFalloff.QuarterLinear: return 1f - fraction * 0.75f;
                case S.AreaFalloff.SweetSpot: return fraction <= 0.5f ? 1f : 0.25f;
                default: return 1f;
            }
        }
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p?.Index == 4 && IsFalseSon(body) && damageInfo.attacker == body.gameObject)
            {
                damageInfo.procCoefficient = C.Get(p.Skill.Proc);
                if (p.Skill.Area != null) damageInfo.damage *= AreaMultiplier(Vector3.Distance(body.transform.position, __instance.transform.position) / Math.Max(0.001f, C.Get(p.Skill.Radius)), C.Get(p.Skill.Area));
            }
            Shot shot = damageInfo.inflictor ? damageInfo.inflictor.GetComponent<Shot>() : null;
            if (!shot || shot.Lightning || shot.Profile.Skill.Falloff == null) return;
            float distance = Vector3.Distance(shot.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(shot.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(25f, 60f, distance));
            if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(7f, 25f, distance));
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p == null || !IsFalseSon(body) || __instance.owner != body.gameObject) return;
            if (p.Index == 5)
            {
                float charge = Mathf.InverseLerp(0.175f, 1f, Number(activeState, "charge", 1f));
                __instance.damage = body.damage * Mathf.Lerp(F(p, "Minimum Damage (%)"), C.Get(p.Skill.Damage), charge) / 100f;
            }
            else if (p.Index == 6) __instance.damage = body.damage * C.Get(p.Skill.Damage) / 100f;
            else return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            __instance.falloffModel = BulletFalloff(C.Get(p.Skill.Falloff));
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        {
            __state = null;
            if (IsFalseSon(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll)
            { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; }
        }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception)
        { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (IsFalseSon(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (IsFalseSon(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (IsFalseSon(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.CooldownAfterDuration && skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
        }
        private static string Percent(float value) => S.Number(value) + "%";
        private static string Damage(float value) => S.Damage(Percent(value) + " damage");
        private static string Utility(float value) => S.Utility(Percent(value));
        private static string PassiveDescription()
        {
            return "<style=cIsUtility>Growth</style> grants additional Lunar Spikes from bonus health" + (generalEnabled.Value && C.Get(shieldGrowth) ? " and shield" : "") + ". " + PassiveBonuses("Unused") + " " + PassiveBonuses("Used");
        }
        private static string PassiveBonuses(string stock)
        {
            var bonuses = new List<string>();
            foreach (string stat in new[] { "Attack Speed (%)", "Armour", "Movement Speed (%)", "Health Regeneration" })
            {
                float amount = C.Get(passive[stock + " Lunar Spike " + stat]);
                if (amount == 0f) continue;
                bonuses.Add((stat.EndsWith("(%)", StringComparison.Ordinal) ? Utility(amount) : S.Utility(S.Number(amount))) + " " + stat.Replace(" (%)", "").ToLowerInvariant());
            }
            return "Each " + stock.ToLowerInvariant() + " Lunar Spike grants " + (bonuses.Count == 0 ? "no stat bonuses" : string.Join(", ", bonuses)) + ".";
        }
        private static bool HasGrowth(Profile p) => p.Index == 1 || p.Index == 2 || p.Index == 4 || p.Index == 5 || p.Index == 6;
        private static string GrowthDescription(Profile p)
        {
            string text = "";
            switch (p.Index)
            {
                case 1: text = "Each Growth level grants an additional Lunar Spike stock."; break;
                case 2: text = "Every " + I(p, "Counts As Lunar Spikes") + " Growth levels grant an additional Lunar Stake stock. Each stake counts as " + I(p, "Counts As Lunar Spikes") + " Lunar Spikes for Lunar Tampering."; break;
                case 4: text = "Each Growth level extends Energized Core by " + S.Number(F(p, "Growth Extra Duration")) + " seconds."; break;
                case 5: text = "The first Growth level extends the laser by " + S.Number(F(p, "Growth Duration Increase")) + " seconds. Further levels give smaller duration increases."; break;
                case 6: text = "Gain " + S.Number(F(p, "Growth Stock Gain")) + " Laser Burst stocks per Growth level, rounded down."; break;
            }
            if (generalEnabled.Value) text += " Growth begins at " + S.Number(C.Get(growthRate)) + " bonus health, with increasing thresholds." + (C.Get(shieldGrowth) ? " Maximum shield contributes at " + S.Number(C.Get(shieldEfficiency)) + "% effectiveness." : "");
            return "<style=cKeywordName>Growth</style><style=cSub>" + text + "</style>";
        }
        private static string RuinDescription() => "<style=cKeywordName>Lunar Ruin</style><style=cSub>Increase damage taken by " + S.Number(ruinDamage == null ? 10f : C.Get(ruinDamage)) + "% per stack and reduce healing by " + S.Number(ruinHealing == null ? 20f : C.Get(ruinHealing)) + "% for " + S.Number(ruinDuration == null ? 5f : C.Get(ruinDuration)) + " seconds." + (ruinRefresh == null || C.Get(ruinRefresh) ? " New stacks refresh existing stacks." : "") + "</style>";
        private static string Description(Profile p)
        {
            string prefix = p.Skill.AgileText + (HasGrowth(p) ? "<style=cIsUtility>Growth</style>. " : ""); float damage = C.Get(p.Skill.Damage);
            switch (p.Index)
            {
                case 0: return prefix + "Swing your club for " + Damage(damage) + ". Charge a slam for up to " + Damage(F(p, "Charged Slam Damage (%)")) + ". Slamming while airborne crashes into the ground, increasing the blast radius.";
                case 1: return prefix + "Fire a Lunar Spike for " + Damage(damage) + " and inflict <style=cIsUtility>Lunar Ruin</style>.";
                case 2: return prefix + "Fire a piercing Lunar Stake for " + Damage(damage) + " and inflict <style=cIsUtility>Lunar Ruin</style>. Struck enemies call down lightning for " + Damage(F(p, "Lightning Strike Damage (%)")) + ".";
                case 3: return prefix + "Dash forward, becoming <style=cIsUtility>invincible</style>. Hitting enemies creates an explosion for " + Damage(damage) + " per enemy hit.";
                case 4: return prefix + "Teleport to your target location, dealing " + Damage(damage) + " and pulling nearby enemies inward. Gain <style=cIsUtility>Energized Core</style>.";
                case 5: return prefix + "Charge a piercing laser for " + S.DamageText(F(p, "Minimum Damage (%)"), C.Get(p.Skill.Interval), true) + ", increasing to " + S.DamageText(damage, C.Get(p.Skill.Interval), true) + ". " + RefillDescription(p);
                default: return prefix + "Fire a piercing laser blast for " + Damage(damage) + ". " + RefillDescription(p);
            }
        }
        private static string RefillDescription(Profile p)
        {
            string text = C.Get(p.Refill)?.Trim() ?? "0";
            return string.Equals(text, "All", StringComparison.OrdinalIgnoreCase) ? "Restore all Lunar Spike or Lunar Stake stocks." : "Restore " + S.Utility(text) + (text.EndsWith("%", StringComparison.Ordinal) ? " of maximum Lunar Spike or Lunar Stake stocks." : " Lunar Spike or Lunar Stake stocks.");
        }
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) text += "\nDuration: " + S.Utility(S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.Index == 0) text += "\nSlam Charge Time: " + S.Utility(S.Number(F(p, "Charged Slam Charge Time")) + "s") + "\nFalling Slam Proc Coefficient: " + S.Utility(S.Number(F(p, "Falling Slam Proc Coefficient")));
            if (p.Index == 2) text += "\nCounts As Lunar Spikes: " + S.Utility(I(p, "Counts As Lunar Spikes").ToString()) + "\nPierces: " + S.Utility(I(p, "Maximum Pierces") < 0 ? "Unlimited" : I(p, "Maximum Pierces").ToString());
            if (p.Index == 4) text += "\nBase Teleport Range: " + S.Utility(S.Number(F(p, "Base Teleport Range")) + "m") + "\nPull Radius: " + S.Utility(S.Number(F(p, "Pull Radius")) + "m");
            if (p.Index == 5) text += "\nCharge Time: " + S.Utility(S.Number(F(p, "Laser Charge Time")) + "s") + "\nFirst Growth Duration Bonus: " + S.Utility(S.Number(F(p, "Growth Duration Increase")) + "s");
            if (p.Index == 6) text += "\nExtra Stocks Per Growth: " + S.Utility(S.Number(F(p, "Growth Stock Gain")));
            return text;
        }
    }
}
