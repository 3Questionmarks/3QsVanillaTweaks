using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
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
    internal static class Chef
    {
        private const string Root = "Survivors - 16 CHEF", StatsRoot = "Survivor-Stats - 16 CHEF";
        private static readonly string[] Names = { "Dice", "Boosted Dice", "Sear", "Boosted Sear", "Ice Box", "Boosted Ice Box", "Roll", "Boosted Roll", "Oil Spill", "Boosted Oil Spill", "Glaze", "Yes CHEF" };
        private static readonly string[] States = { "Dice", "Dice", "Sear", "Sear", "IceBox", "IceBox", "RolyPoly", "RolyPoly", "OilSpillBase", "OilSpillBase", "Glaze", "YesChef" };
        private static readonly string[] Overrides = { "primarySkillOverrideDef", "secondarySkillOverrideDef", "secondaryAltSkillOverrideDef", "utilitySkillOverrideDef", "utilityAltSkillOverrideDef" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal readonly List<GameObject> Pools = new List<GameObject>();
            internal readonly List<GameObject> Globs = new List<GameObject>();
            internal readonly Dictionary<string, ConfigEntry<float>> F = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> I = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> B = new Dictionary<string, ConfigEntry<bool>>();
            internal bool SharedDefinition;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class Mode { internal bool Boosted; internal float NativeBaseline; }
        private sealed class Shot : MonoBehaviour
        {
            internal Profile Profile;
            internal Vector3 Origin;
            internal bool Pool, IdleHit;
            internal int BornOrder;
        }
        private sealed class Payment { internal int Cost; }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<int, ConfigEntry<bool>> switches = new Dictionary<int, ConfigEntry<bool>>();
        private static readonly HashSet<MethodBase> hooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<EntityState, Mode> modes = new ConditionalWeakTable<EntityState, Mode>();
        private static readonly ConditionalWeakTable<CharacterBody, List<Shot>> pools = new ConditionalWeakTable<CharacterBody, List<Shot>>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled;
        private static ConfigEntry<int> mealDebuffs;
        private static ConfigEntry<float> mealFraction, mealFlat, mealRange;
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        private static int order;
        [ThreadStatic] private static EntityState activeState;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.Chef." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsChef(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
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
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable CHEF changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable CHEF stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - CHEFs Kiss", "Changes Enabled", true, "Enable CHEFs Kiss changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(Chef), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(Chef), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Chef), finalizer), transpiler: transpiler == null ? null : new HarmonyMethod(typeof(Chef), transpiler), ilmanipulator: null);
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "OnExit", "FixedUpdate", "Update", "TrySpawnCleavers", "FirePrimaryAttack", "FireBoostedFireball", "FireIceCube", "FireBoostedIceCube", "StartRolyPoly", "PerformAttack", "GearShift", "GetIdealVelocity", "PerformBounce", "PerformLanding", "HandleExtraBounce", "DetonateAuthority", "DropOilPoolAuthority", "DropBoostedOilPoolAuthority", "FireMeatballs", "FireGrenade", "GetMinimumInterruptPriority" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType.Namespace != "EntityStates.Chef" || !hooks.Add(method)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Chef), nameof(BeforeState)), postfix: new HarmonyMethod(typeof(Chef), nameof(AfterState)), finalizer: new HarmonyMethod(typeof(Chef), nameof(EndScope)), ilmanipulator: null);
            }
        }
        private static void Load()
        {
            try
            {
                GameObject obj = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Chef/ChefBody.prefab").WaitForCompletion();
                if (!obj) throw new InvalidOperationException("CHEF body could not be loaded.");
                prefab = obj.GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Chef");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefab);
                if (passiveEnabled.Value)
                {
                    string section = Root + " - CHEFs Kiss";
                    mealDebuffs = S.Int(section, "Unique Buffs To Trigger Meal Drop", 2, 2, "Unique cooking debuffs needed on a killed enemy to drop a meal.", 1);
                    mealFraction = S.Float(section, "Max Health Per Buff (%)", 4f, 4f, "Maximum health restored by a meal per unique cooking debuff.");
                    mealFlat = S.Float(section, "Flat Health Per Buff", 2f, 2f, "Flat health restored by a meal per unique cooking debuff.");
                    mealRange = S.Float(section, "Range Increase Per Buff (%)", 75f, 75f, "Meal pickup range multiplier contributed by each unique cooking debuff.");
                    SkillLocator locator = prefab.GetComponent<SkillLocator>();
                    S.SetText(locator.passiveSkill.skillDescriptionToken, PassiveDescription);
                }
                Component controller = Component(obj, "ChefController");
                var boosted = Overrides.Select(key => Read(controller, key) as SkillDef).ToArray();
                var defs = obj.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        bool boost = i < 10 && i % 2 == 1;
                        Type state = State(States[i]);
                        SkillDef def = boost ? boosted[i / 2] : defs.FirstOrDefault(d => d.activationState.stateType != null && state.IsAssignableFrom(d.activationState.stateType) && !boosted.Contains(d));
                        if (!def && !boost) def = defs.FirstOrDefault(d => d.activationState.stateType != null && state.IsAssignableFrom(d.activationState.stateType));
                        if (!def) { ThreeQsVanillaTweaks.Log.Warning("CHEF skill unavailable: " + Names[i]); continue; }
                        bool shared = boost && profiles.Values.Any(p => p.Skill.Definition == def);
                        if (boost && !shared)
                        {
                            // Boosted and normal variants may reuse language tokens. Give each its own lookup.
                            string title = Language.GetString(def.skillNameToken);
                            if (profiles.TryGetValue(i - 1, out Profile normal) && title == Language.GetString(normal.Skill.Definition.skillNameToken)) title = Names[i];
                            def.skillNameToken = "3QS_CHEF_SKILL_" + i + "_NAME";
                            def.skillDescriptionToken = "3QS_CHEF_SKILL_" + i + "_DESCRIPTION";
                            S.SetText(def.skillNameToken, () => title);
                        }
                        var p = new Profile { Index = i, SharedDefinition = shared, Skill = new S.Skill(Root + " - " + Names[i], def, def.activationState.stateType, switches[i]) };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p;
                        if (!shared) definitions[def] = p;
                        Hook(def.activationState.stateType); Hook(state);
                        if (!shared)
                        {
                            S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                        }
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("CHEF " + Names[i] + " changes could not be loaded: " + e); }
                }
                InstallHooks(); Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("CHEF changes could not be loaded: " + e); }
        }
        private static bool PredictBoost(CharacterBody body)
        {
            Component controller = Component(body.gameObject, "ChefController");
            return !(bool)(Read(controller, "isYesChefBeingConsumed") ?? false) && (body.HasBuff(DLC2Content.Buffs.Boosted) || (bool)(Read(controller, "isInYesChef") ?? false));
        }
        private static int BaseIndex(EntityState state)
        {
            if (state == null || state.GetType().Namespace != "EntityStates.Chef") return -1;
            string name = state.GetType().Name;
            if (name.StartsWith("OilSpill", StringComparison.Ordinal)) return 8;
            return Array.IndexOf(States, name);
        }
        private static Profile ForState(EntityState state)
        {
            int index = BaseIndex(state); if (index < 0) return null;
            if (index < 10)
            {
                bool boost = modes.TryGetValue(state, out Mode mode) ? mode.Boosted : (bool)(Read(state, "hasBoost") ?? false);
                if (boost) index++;
            }
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static float NativeProc(GameObject projectile, float fallback = 1f)
        {
            if (!projectile) return fallback;
            return Number(projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f) * Number(projectile.GetComponent<ProjectileOverlapAttack>(), "overlapProcCoefficient", 1f);
        }
        private static void Bind(Profile p)
        {
            int i = p.Index; bool boost = i < 10 && i % 2 == 1; S.Skill s = p.Skill; object sample = s.Sample;
            s.Bind(changedCooldown: i == 10 ? (float?)10f : null, changedAgile: i >= 2 && i <= 9 || i == 11 ? (bool?)true : null, damage: false, nativeConsumeOverride: i == 0 ? (int?)1 : null);
            string projectileField = i <= 1 ? boost ? "projectileEnhancedPrefab" : "projectilePrefab" : i <= 3 ? "boostedSearProjectilePrefab" : i <= 5 ? boost ? "boostedIceCubeProjectilePrefab" : "projectilePrefab" : "projectilePrefab";
            p.Projectile = Asset(Read(sample, projectileField));
            float nativeDamage = i <= 1 ? Number(sample, boost ? "boostedDamageCoefficient" : "damageCoefficient", 2.5f) : i <= 3 ? Number(sample, "totalDamageCoefficient", 6f) + (boost ? Number(sample, "additionalBoostedDamageCoefficient") : 0f) : i <= 5 ? Number(sample, boost ? "boostedProjectileDmgCoefficient" : "damageCoefficient", 1.5f) : i <= 7 ? Number(sample, "chargeDamageCoefficient", 5f) : i <= 9 ? Number(sample, "blastDamageCoefficient", 5f) : i == 11 ? Number(sample, "explosionDamageCoefficient", 6f) : Number(sample, "damageCoefficient", 3f);
            float interval = i <= 3 && i >= 2 ? 1f / Math.Max(0.001f, Number(sample, "tickFrequency", 5f)) : 0f;
            float duration = i <= 3 && i >= 2 ? Number(sample, "baseFlamethrowerDuration", 2f) : i <= 7 && i >= 6 ? Number(sample, "baseDuration", 1f) : 0f;
            if (i == 2 || i == 3) nativeDamage /= Math.Max(1f, Mathf.Ceil(duration / interval));
            float proc = i == 2 || i == 3 ? Number(sample, "procCoefficientPerTick", 1f) : i >= 8 ? Number(sample, "blastProcCoefficient", 1f) : NativeProc(p.Projectile);
            float? changedDamage = i == 2 || i == 3
                ? (float?)(Mathf.Round(nativeDamage * 100f * 0.2f / interval / 5f) * 5f)
                : i == 4 ? (float?)150f
                : i == 5 ? (float?)2000f
                : i == 10 ? (float?)300f
                : i == 11 ? (float?)600f
                : null;
            if (i == 1)
            {
                s.Damage = S.Float(s.Section, "Damage (%)", nativeDamage * 100f, nativeDamage * 100f, "Damage per cleaver hit as a percentage of base damage.");
                s.Proc = BindProc(s, 2f, proc);
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.None, "Damage falloff over distance. Vanilla: None.");
            }
            else s.BindDamage(nativeDamage * 100f, changedDamage, proc, periodic: i == 2 || i == 3);
            if (i == 0)
            {
                object cleaver = Component(p.Projectile, "RoR2.Projectile.CleaverProjectile");
                object idle = Read(cleaver, "IdleOverlapAttack");
                Float(p, "Suspended Damage (%)", 40f, Number(idle, "damageCoefficient", 0.16f) * nativeDamage * 100f, "Damage per hit while the cleaver is suspended, as a percentage of base damage.");
                Float(p, "Suspended Interval", 0.4f, Number(idle, "resetInterval", 0.4f), "Seconds between suspended cleaver hits.", 0.001f);
                Float(p, "Suspended Proc Coefficient", 0.8f, Number(idle, "overlapProcCoefficient", 0.8f) * Number(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f), "Proc coefficient of each suspended hit.");
                Float(p, "Suspended Charged Time", 0.5f, Number(cleaver, "holdChargeTime", 1f), "Time a suspended cleaver needs to become charged.");
                Float(p, "Charged Damage (%)", 80f, Number(idle, "damageCoefficient", 0.16f) * nativeDamage * Number(cleaver, "chargedDamageCoefficient", 2f) * 100f, "Damage per hit while the suspended cleaver is charged.");
                Float(p, "Charged Proc Coefficient", 1f, 1.5f * Number(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f), "Proc coefficient of charged cleaver hits.");
                Bool(p, "Fix Suspended Bug", true, false, "Use suspended damage on the first suspended hit instead of full impact damage.");
            }
            if (i <= 5 || i == 10)
            {
                if (i == 2) p.Projectile = null;
                if (p.Projectile)
                {
                    if (i <= 1)
                    {
                        float speed = Number(Component(p.Projectile, "RoR2.Projectile.CleaverProjectile"), "maxTravelDistance", 55f) * 2f;
                        s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", speed, speed, "Cleaver travel speed in metres per second.");
                    }
                    else s.BindProjectileSpeed(p.Projectile);
                }
            }
            if (i == 2 || i == 3)
            {
                s.Interval = S.Float(s.Section, "Damage Interval", 0.2f, interval, "Seconds between flame hits before attack speed is applied.", 0.001f);
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Flamethrower duration in seconds.", 0.001f);
                Float(p, "Range", Number(sample, "maxDistance", 16f), Number(sample, "maxDistance", 16f), "Flamethrower range in metres.");
                if (i == 2) Float(p, "Boosted Damage (%)", 700f, Number(sample, "boostedProjectileDmgCoefficient", 7f) * 100f, "Damage per fireball fired by boosted Sear, as a percentage of base damage.");
                if (i == 3)
                {
                    Int(p, "Number Of Fireballs", 3, (int)Number(sample, "boostedProjectileCount", 3f), "Fireballs fired before the boosted flamethrower.");
                    if (!profiles.ContainsKey(2)) Float(p, "Fireball Damage (%)", 700f, Number(sample, "boostedProjectileDmgCoefficient", 7f) * 100f, "Damage per boosted fireball as a percentage of base damage.");
                }
            }
            if (i == 4) Int(p, "Number Of Ice Cubes", 3, (int)Number(Component(prefab.gameObject, "ChefController"), "maxIceCubesToFire", 3f), "Ice cubes fired per use.", 1);
            if (i == 6 || i == 7)
            {
                Float(p, "Extra Damage Per Charge Level (%)", 100f, Number(sample, "additionalChargeDamageCoefficientPerGear", 1f) * 100f, "Extra roll damage per charge level as a percentage of base damage.");
                Float(p, "Extra Duration Per Charge Level", 0.5f, Number(sample, "baseDurationLvlUp", 0.5f), "Extra roll duration per charge level, in seconds.");
                Float(p, "Charge Time", 2f, Number(sample, "baseChargeDuration", 2f), "Full charge time in seconds before attack speed is applied.", 0.001f);
                Float(p, "Armour Boost While Active", 200f, 200f, "Armour gained while charging or rolling.");
                Float(p, "Movement Speed Distance Scaling (%)", 50f, 100f, "Effect of movement speed changes on travel distance. 0 uses base movement speed.");
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Base roll duration in seconds.", 0.001f);
                if (i == 7) Float(p, "Spinning Blade Damage (%)", 50f, Number(sample, "whirlwindDamageCo", 0.5f) * 100f, "Damage per spinning blade hit as a percentage of base damage.");
            }
            if (i == 8 || i == 9)
            {
                Int(p, "Maximum Reactivations", 3, (int)Number(sample, "extraBounces", 3f), "Extra launches allowed before Oil Spill ends.");
                Float(p, "Oil Pool Ignite Proc Coefficient", 0.5f, NativePoolProc(sample), "Proc coefficient of burning oil pool hits.");
                Int(p, "Dropped Oil Globs At Full High Launch", 2, (int)Number(sample, "numberOfShotsInAirBoosted", 2f), "Oil globs dropped during each high launch.");
                float extra = prefab.baseMoveSpeed * prefab.sprintingSpeedMultiplier / (1.421f * Math.Max(1f, Number(sample, "movementSpeedReqCoefficient", 1f)));
                Float(p, "Additional Globs Per 100% Movement Speed", extra, extra, "Extra globs per 100% bonus movement speed, rounded down.");
                Float(p, "Movement Speed Distance Scaling (%)", 50f, 100f, "Effect of movement speed changes on launch distance. 0 uses base movement speed.");
                foreach (string key in new[] { "projectilePrefab", "projectilePrefabBoosted", "projectilePrefabBoostedFrozen" }) { GameObject pool = Asset(Read(sample, key)); if (pool && !p.Pools.Contains(pool)) p.Pools.Add(pool); }
                foreach (string key in new[] { "meatballProjectile", "meatballProjectileBoostedIgnite", "meatballProjectileFrozen" }) { GameObject glob = Asset(Read(sample, key)); if (glob && !p.Globs.Contains(glob)) p.Globs.Add(glob); }
                GameObject poolObj = p.Pools.FirstOrDefault(); Component oil = Component(poolObj, "OilController");
                float lifetime = Number(oil, "lifetime", 5f);
                Float(p, "AOE Duration", lifetime, lifetime, "Oil pool lifetime in seconds. -1 lasts until removed.", -1f);
                Int(p, "Max Instances Of AOE", -1, -1, "Maximum active oil pools per player. The oldest is removed when exceeded. -1 is unlimited.", -1);
            }
            if (i == 10) Int(p, "Number Of Oil Globs", 7, (int)Number(sample, "grenadeCountMax", 3f), "Oil globs fired per use.", 1);
            if (i == 6 || i == 7 || i == 8 || i == 9 || i == 10 || i == 11 || i == 5 || i == 3)
            {
                float radius = i == 6 || i == 7 ? Number(sample, "explosionRadius", 5f) : i == 11 ? Number(sample, "explosionRadius", 10f) : i == 8 || i == 9 ? Number(sample, "blastRadius", 8f) : Number(p.Projectile ? p.Projectile.GetComponent<ProjectileImpactExplosion>() : null, "blastRadius", 5f);
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of an explosion. Vanilla: None.");
            }
        }
        private static ConfigEntry<float> BindProc(S.Skill s, float value, float vanilla) => S.Float(s.Section, "Proc Coefficient", value, vanilla, "Proc coefficient per hit.");
        private static float NativePoolProc(object sample)
        {
            GameObject obj = Asset(Read(sample, "projectilePrefab"));
            return Number(obj ? obj.GetComponent<ProjectileDotZone>() : null, "overlapProcCoefficient", 0.5f);
        }
        private static void InstallHooks()
        {
            if (stats != null)
            {
                Patch(typeof(CharacterBody), "Start", postfix: nameof(AfterBody));
                Patch(typeof(CharacterBody), "RecalculateStats", prefix: nameof(BeforeStats));
            }
            if (passiveEnabled.Value) Patch(typeof(GlobalEventManager), "OnCharacterDeath", transpiler: nameof(MealIL));
            if (profiles.Count == 0) return;
            Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
            Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
            Patch(typeof(SkillDef), "CanExecute", postfix: nameof(AfterRequired));
            Patch(typeof(SkillDef), "OnFixedUpdate", prefix: nameof(BeforeSkillTick));
            Patch(typeof(ProjectileManager), "FireProjectile", prefix: nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
            Patch(typeof(ProjectileController), "Start", postfix: nameof(AfterProjectile));
            MethodInfo initialized = typeof(ProjectileController).GetMethod("DispatchOnInitialized", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (initialized != null) harmony.Patch(initialized, postfix: new HarmonyMethod(typeof(Chef), nameof(AfterProjectile)), ilmanipulator: null);
            Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap), nameof(AfterOverlap), args: new[] { typeof(List<HurtBox>) });
            Patch(typeof(BulletAttack), "Fire", prefix: nameof(BeforeBullet), args: Type.EmptyTypes);
            Patch(typeof(BlastAttack), "Fire", prefix: nameof(BeforeBlast), args: Type.EmptyTypes);
            Patch(typeof(HealthComponent), "TakeDamageProcess", prefix: nameof(BeforeDamage));
            if (profiles.ContainsKey(0))
            {
                Patch(typeof(GenericSkill), "DeductStock", prefix: nameof(BeforeDiceDeduct));
                Patch(State("Dice"), "TrySpawnCleavers", prefix: nameof(BeforeCleaverThrow));
                Patch(GameType("RoR2.Projectile.CleaverProjectile"), "Start", postfix: nameof(AfterCleaverStart));
                Patch(GameType("RoR2.Projectile.CleaverProjectile"), "ChargeCleaver", postfix: nameof(AfterCleaverCharge));
            }
            if (profiles.ContainsKey(6) || profiles.ContainsKey(7))
            {
                Patch(State("RolyPoly"), "GetIdealVelocity", postfix: nameof(AfterRollVelocity));
                Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterArmour));
            }
            if (profiles.ContainsKey(8) || profiles.ContainsKey(9))
            {
                Patch(State("OilSpillBase"), "PerformBounce", transpiler: nameof(OilMoveIL));
                Patch(State("OilSpillBase"), "get_canPerformExtraBounce", prefix: nameof(BeforeExtraBounceReady));
                Patch(GameType("OilController"), "IgniteOil", postfix: nameof(AfterIgnite));
            }
        }
        private static void Apply()
        {
            stats?.Apply(prefab);
            foreach (Profile p in profiles.Values) if (!p.SharedDefinition) p.Skill.ApplyDefinition();
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (IsChef(body)) { stats?.Apply(body); body.MarkAllStatsDirty(); }
        }
        private static void AfterBody(CharacterBody __instance) { if (IsChef(__instance)) stats?.Apply(__instance); }
        private static void BeforeStats(CharacterBody __instance) { if (IsChef(__instance)) stats?.Apply(__instance); }
        private static float MealThreshold() => C.Get(mealDebuffs) - 1f;
        private static float MealFlat() => C.Get(mealFlat);
        private static float MealFraction() => C.Get(mealFraction) / 100f;
        private static float MealRange() => C.Get(mealRange) / 100f;
        private static IEnumerable<CodeInstruction> MealIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int start = code.FindIndex(c => c.operand is FieldInfo f && f.Name == "CookingChopped");
            int end = start < 0 ? -1 : code.FindIndex(start, c => c.opcode == OpCodes.Ldstr && Equals(c.operand, "Chef/ChefFoodPickup"));
            if (start < 0 || end < 0) { ThreeQsVanillaTweaks.Log.Warning("CHEF: meal drop calculation could not be located."); return code; }
            int threshold = -1;
            for (int i = start; i < end; i++) if (code[i].opcode == OpCodes.Ldc_R4 && code[i].operand is float value && value == 1f) threshold = i;
            for (int i = start; i < end; i++)
            {
                if (code[i].opcode != OpCodes.Ldc_R4 || !(code[i].operand is float value)) continue;
                string method = i == threshold ? nameof(MealThreshold) : value == 2f ? nameof(MealFlat) : Math.Abs(value - 0.04f) < 0.000001f ? nameof(MealFraction) : value == 0.75f ? nameof(MealRange) : null;
                if (method != null) { code[i].opcode = OpCodes.Call; code[i].operand = AccessTools.Method(typeof(Chef), method); }
            }
            return code;
        }
        private static void Set(Scope scope, object instance, string field, object value)
        {
            FieldInfo info = AccessTools.Field(instance.GetType(), field);
            if (info == null) { Write(instance, field, value); return; }
            if (info.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(info, info.GetValue(null)));
            info.SetValue(info.IsStatic ? null : instance, value);
        }
        private static float FireballDamage(Profile boosted)
        {
            return profiles.TryGetValue(2, out Profile normal) ? F(normal, "Boosted Damage (%)") : F(boosted, "Fireball Damage (%)");
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = null; CharacterBody body = Body(__instance); if (!IsChef(body)) return;
            int index = BaseIndex(__instance); if (index < 0) return;
            if (__originalMethod.Name == "OnEnter" && index < 10)
            {
                Mode mode = modes.GetOrCreateValue(__instance); mode.Boosted = PredictBoost(body);
                mode.NativeBaseline = (body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f)) * (body.isSprinting ? body.sprintingSpeedMultiplier : 1f);
            }
            Profile p = ForState(__instance); if (p == null) return;
            __state = new Scope { Previous = activeState }; activeState = __instance;
            int i = p.Index;
            if (i <= 1)
            {
                Set(__state, __instance, i == 1 ? "boostedDamageCoefficient" : "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            }
            if (i == 2 || i == 3)
            {
                Set(__state, __instance, "tickFrequency", 1f / C.Get(p.Skill.Interval));
                Set(__state, __instance, "baseFlamethrowerDuration", C.Get(p.Skill.Duration));
                float total = C.Get(p.Skill.Damage) / 100f * Mathf.Ceil(C.Get(p.Skill.Duration) / C.Get(p.Skill.Interval));
                Set(__state, __instance, "totalDamageCoefficient", total);
                if (i == 3)
                {
                    Set(__state, __instance, "additionalBoostedDamageCoefficient", 0f);
                    Set(__state, __instance, "boostedProjectileDmgCoefficient", FireballDamage(p) / 100f);
                    Set(__state, __instance, "boostedProjectileCount", (float)I(p, "Number Of Fireballs"));
                }
                Set(__state, __instance, "procCoefficientPerTick", C.Get(p.Skill.Proc));
                Set(__state, __instance, "maxDistance", F(p, "Range"));
            }
            if (i == 4 || i == 5)
            {
                Set(__state, __instance, i == 4 ? "damageCoefficient" : "boostedProjectileDmgCoefficient", C.Get(p.Skill.Damage) / 100f);
                if (i == 4)
                {
                    FieldInfo count = AccessTools.Field(GameType("ChefController"), "maxIceCubesToFire");
                    __state.Fields.Add(new KeyValuePair<FieldInfo, object>(count, count.GetValue(null))); count.SetValue(null, I(p, "Number Of Ice Cubes"));
                }
            }
            if (i == 6 || i == 7)
            {
                Set(__state, __instance, "chargeDamageCoefficient", C.Get(p.Skill.Damage) / 100f);
                Set(__state, __instance, "additionalChargeDamageCoefficientPerGear", F(p, "Extra Damage Per Charge Level (%)") / 100f);
                Set(__state, __instance, "baseDurationLvlUp", F(p, "Extra Duration Per Charge Level"));
                Set(__state, __instance, "baseChargeDuration", F(p, "Charge Time"));
                Set(__state, __instance, "baseDuration", C.Get(p.Skill.Duration));
                Set(__state, __instance, "explosionRadius", C.Get(p.Skill.Radius));
                if (i == 7) Set(__state, __instance, "whirlwindDamageCo", F(p, "Spinning Blade Damage (%)") / 100f);
            }
            if (i == 8 || i == 9)
            {
                Set(__state, __instance, "extraBounces", I(p, "Maximum Reactivations"));
                Set(__state, __instance, "numberOfShotsInAirBoosted", I(p, "Dropped Oil Globs At Full High Launch"));
                Set(__state, __instance, "blastDamageCoefficient", C.Get(p.Skill.Damage) / 100f);
                Set(__state, __instance, "blastProcCoefficient", C.Get(p.Skill.Proc));
                Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius));
            }
            if (i == 10) { Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f); Set(__state, __instance, "grenadeCountMax", I(p, "Number Of Oil Globs")); }
            if (i == 11) { Set(__state, __instance, "explosionDamageCoefficient", C.Get(p.Skill.Damage) / 100f); Set(__state, __instance, "explosionRadius", C.Get(p.Skill.Radius)); Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius)); }
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod, object[] __args)
        {
            CharacterBody body = Body(__instance); if (!IsChef(body)) return;
            int index = BaseIndex(__instance); if (index < 0) return;
            if (__originalMethod.Name == "OnEnter" && index < 10) modes.GetOrCreateValue(__instance).Boosted = (bool)(Read(__instance, "hasBoost") ?? false);
            Profile p = ForState(__instance); if (p == null) return;
            if (p.Index >= 6 && p.Index <= 7 && (__originalMethod.Name == "OnEnter" || __originalMethod.Name == "OnExit")) body.MarkAllStatsDirty();
            if ((p.Index == 8 || p.Index == 9) && __originalMethod.Name == "PerformBounce" && __args.Length > 1 && __args[1] is bool high && high)
                Write(__instance, "numberOfShotsInAirMax", OilGlobCount(body.moveSpeed, body.baseMoveSpeed * body.sprintingSpeedMultiplier, I(p, "Dropped Oil Globs At Full High Launch"), F(p, "Additional Globs Per 100% Movement Speed")));
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
        private static float ScaleSpeed(float speed, float baseline, float percentage) => Math.Max(0f, baseline + (speed - baseline) * percentage / 100f);
        private static void AfterRollVelocity(EntityState __instance, ref Vector3 __result)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p == null || !IsChef(body)) return;
            float baseline = (body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f)) * (body.isSprinting ? body.sprintingSpeedMultiplier : 1f);
            float speed = ScaleSpeed(body.moveSpeed, baseline, F(p, "Movement Speed Distance Scaling (%)"));
            if (body.moveSpeed > 0.0001f) __result *= speed / body.moveSpeed;
            else if (body.characterDirection) __result = body.characterDirection.forward * speed * body.sprintingSpeedMultiplier * Number(__instance, "speedMultiplierTemp");
        }
        private static float OilMovementSpeed(EntityState state)
        {
            Profile p = ForState(state); CharacterBody body = Body(state);
            float speed = Number(state, "moveSpeedStat");
            if (p == null || !IsChef(body)) return speed;
            float baseline = modes.TryGetValue(state, out Mode mode) && mode.NativeBaseline > 0f ? mode.NativeBaseline : body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
            // Oil Spill reads the stat captured when the launch state began.
            return ScaleSpeed(speed, baseline, F(p, "Movement Speed Distance Scaling (%)"));
        }
        private static IEnumerable<CodeInstruction> OilMoveIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.Name == "moveSpeedStat")
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Chef), nameof(OilMovementSpeed)); }
                yield return instruction;
            }
        }
        private static int OilGlobCount(float speed, float baseline, int count, float extra) => count + Mathf.FloorToInt(Math.Max(0f, speed / Math.Max(0.001f, baseline) - 1f) * extra);
        private static bool BeforeExtraBounceReady(EntityState __instance, ref bool __result)
        {
            Profile p = ForState(__instance); if (p == null || !IsChef(Body(__instance))) return true;
            __result = Number(__instance, "extraBouncesUsed") < I(p, "Maximum Reactivations"); return false;
        }
        private static void AfterArmour(CharacterBody __instance)
        {
            if (!IsChef(__instance) || !__instance.HasBuff(RoR2Content.Buffs.ArmorBoost)) return;
            EntityState roll = __instance.GetComponents<EntityStateMachine>().Select(m => m.state).FirstOrDefault(s => BaseIndex(s) == 6);
            Profile p = ForState(roll); if (p != null) Write(__instance, "armor", __instance.armor + F(p, "Armour Boost While Active") - 200f);
        }
        private static bool Match(GameObject original, ProjectileController instance)
        {
            if (!original || !instance) return false;
            ProjectileController pc = original.GetComponent<ProjectileController>();
            return pc && pc.catalogIndex >= 0 && pc.catalogIndex == instance.catalogIndex || instance.gameObject.name == original.name + "(Clone)";
        }
        private static bool Matches(Profile p, ProjectileController instance) => Match(p.Projectile, instance) || p.Pools.Any(obj => Match(obj, instance)) || p.Globs.Any(obj => Match(obj, instance));
        private static bool Matches(Profile p, GameObject source) => p.Projectile == source || p.Pools.Contains(source) || p.Globs.Contains(source);
        private static Profile ProjectileProfile(ProjectileController pc)
        {
            if (!pc || !pc.owner || !IsChef(pc.owner.GetComponent<CharacterBody>())) return null;
            Shot previous = pc.GetComponent<Shot>(); if (previous && previous.Profile != null) return previous.Profile;
            Profile active = ForState(activeState);
            if (active != null && Matches(active, pc)) return active;
            Component oil = Component(pc.gameObject, "OilController");
            if (oil)
            {
                bool boost = (bool)(Read(oil, "isBoosted") ?? false);
                if (profiles.TryGetValue(boost ? 9 : 8, out Profile pool) && Matches(pool, pc)) return pool;
            }
            return profiles.Values.FirstOrDefault(p => Matches(p, pc));
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsChef(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            GameObject source = fireProjectileInfo.projectilePrefab;
            Profile p = ForState(activeState);
            if (p == null || !Matches(p, source)) p = profiles.Values.FirstOrDefault(v => v.Projectile && v.Projectile == source);
            if (p == null) return;
            CharacterBody body = fireProjectileInfo.owner.GetComponent<CharacterBody>();
            int i = p.Index;
            if (i <= 1 || i == 4 || i == 5 || i == 10) fireProjectileInfo.damage = body.damage * C.Get(p.Skill.Damage) / 100f;
            if (i == 3) fireProjectileInfo.damage = body.damage * FireballDamage(p) / 100f;
            if (i == 7) fireProjectileInfo.damage = body.damage * F(p, "Spinning Blade Damage (%)") / 100f;
            if (p.Skill.ProjectileSpeed != null)
            {
                if (i <= 1)
                {
                    float nativeSpeed = Number(Component(p.Projectile, "RoR2.Projectile.CleaverProjectile"), "maxTravelDistance", 55f) * 2f;
                    fireProjectileInfo.speedOverride *= C.Get(p.Skill.ProjectileSpeed) / Math.Max(0.001f, nativeSpeed);
                }
                else fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
            }
        }
        private static void AfterProjectile(ProjectileController __instance)
        {
            Profile p = ProjectileProfile(__instance); if (p == null) return;
            Shot shot = __instance.GetComponent<Shot>() ?? __instance.gameObject.AddComponent<Shot>();
            bool first = shot.Profile == null;
            shot.Profile = p; shot.Origin = __instance.owner.transform.position;
            shot.Pool = Component(__instance.gameObject, "OilController");
            if (!shot.Pool)
            {
                __instance.procCoefficient = C.Get(p.Skill.Proc);
                foreach (ProjectileOverlapAttack overlap in __instance.GetComponents<ProjectileOverlapAttack>()) overlap.overlapProcCoefficient = 1f;
            }
            if (p.Skill.Radius != null && __instance.TryGetComponent<ProjectileImpactExplosion>(out var explosion))
            { explosion.blastRadius = C.Get(p.Skill.Radius); explosion.falloffModel = Area(C.Get(p.Skill.Area)); }
            if (shot.Pool && (p.Index == 8 || p.Index == 9))
            {
                __instance.procCoefficient = 1f;
                Component oil = Component(__instance.gameObject, "OilController");
                float life = F(p, "AOE Duration");
                Write(oil, "lifetime", life < 0f ? float.PositiveInfinity : life);
                ProjectileDotZone zone = __instance.GetComponent<ProjectileDotZone>();
                if (zone) { zone.lifetime = life < 0f ? float.PositiveInfinity : life; Write(zone, "overlapProcCoefficient", F(p, "Oil Pool Ignite Proc Coefficient")); }
                if (first)
                {
                    shot.BornOrder = ++order;
                    var list = pools.GetOrCreateValue(__instance.owner.GetComponent<CharacterBody>()); list.RemoveAll(s => !s); list.Add(shot);
                    int maximum = I(p, "Max Instances Of AOE");
                    if (NetworkServer.active && maximum >= 0)
                        while (list.Count > maximum) { UnityEngine.Object.Destroy(list[0].gameObject); list.RemoveAt(0); }
                }
            }
        }
        private static void AfterIgnite(Component __instance)
        {
            ProjectileController pc = __instance.GetComponent<ProjectileController>(); Profile p = ProjectileProfile(pc);
            if (p == null || (p.Index != 8 && p.Index != 9)) return;
            pc.procCoefficient = 1f;
            Component zone = Component(__instance.gameObject, "RoR2.Projectile.ProjectileDotZone");
            Write(zone, "overlapProcCoefficient", F(p, "Oil Pool Ignite Proc Coefficient"));
        }
        private static void RefreshOverlap(ProjectileOverlapAttack overlap)
        {
            MethodInfo refresh = overlap ? AccessTools.Method(overlap.GetType(), "UpdateAttackValues", Type.EmptyTypes) : null;
            refresh?.Invoke(overlap, null);
        }
        private static void AfterCleaverStart(Component __instance)
        {
            ProjectileController pc = __instance.GetComponent<ProjectileController>(); Profile p = ProjectileProfile(pc);
            if (p?.Index != 0) return;
            Write(__instance, "holdChargeTime", F(p, "Suspended Charged Time"));
            float suspended = F(p, "Suspended Damage (%)");
            if (suspended > 0f) Write(__instance, "chargedDamageCoefficient", F(p, "Charged Damage (%)") / suspended);
            if (Read(__instance, "IdleOverlapAttack") is ProjectileOverlapAttack idle)
            {
                idle.resetInterval = F(p, "Suspended Interval");
                if (B(p, "Fix Suspended Bug"))
                {
                    idle.damageCoefficient = suspended / Math.Max(0.0001f, C.Get(p.Skill.Damage));
                    idle.overlapProcCoefficient = F(p, "Suspended Proc Coefficient") / Math.Max(0.0001f, pc.procCoefficient);
                    if (Read(idle, "attack") != null) RefreshOverlap(idle);
                }
            }
        }
        private static void AfterCleaverCharge(Component __instance)
        {
            ProjectileController pc = __instance.GetComponent<ProjectileController>(); Profile p = ProjectileProfile(pc);
            if (p?.Index != 0) return;
            foreach (ProjectileOverlapAttack overlap in __instance.GetComponents<ProjectileOverlapAttack>())
            {
                overlap.overlapProcCoefficient = F(p, "Charged Proc Coefficient") / Math.Max(0.0001f, pc.procCoefficient);
                if (Read(overlap, "attack") != null) RefreshOverlap(overlap);
            }
        }
        private static Shot OverlapShot(OverlapAttack attack) => attack.inflictor ? attack.inflictor.GetComponentInParent<Shot>() : null;
        private static void BeforeOverlap(OverlapAttack __instance, out Shot __state)
        {
            __state = null; Shot shot = OverlapShot(__instance);
            if (shot)
            {
                Profile p = shot.Profile;
                if (p.Index == 0)
                {
                    Component cleaver = Component(shot.gameObject, "RoR2.Projectile.CleaverProjectile");
                    bool charged = (bool)(Read(cleaver, "charged") ?? false);
                    object idle = Read(cleaver, "IdleOverlapAttack");
                    if (ReferenceEquals(Read(idle, "attack"), __instance))
                    {
                        __state = shot;
                        if (B(p, "Fix Suspended Bug") || shot.IdleHit) __instance.damage = __instance.attacker.GetComponent<CharacterBody>().damage * F(p, charged ? "Charged Damage (%)" : "Suspended Damage (%)") / 100f;
                        __instance.procCoefficient = F(p, charged ? "Charged Proc Coefficient" : "Suspended Proc Coefficient");
                    }
                    else __instance.procCoefficient = charged ? F(p, "Charged Proc Coefficient") : C.Get(p.Skill.Proc);
                }
                else if (shot.Pool && (p.Index == 8 || p.Index == 9)) __instance.procCoefficient = F(p, "Oil Pool Ignite Proc Coefficient");
                else __instance.procCoefficient = C.Get(p.Skill.Proc);
                return;
            }
            Profile active = ForState(activeState);
            if (active != null && IsChef(Body(activeState)) && __instance.attacker == Body(activeState).gameObject) __instance.procCoefficient = C.Get(active.Skill.Proc);
        }
        private static void AfterOverlap(Shot __state, bool __result) { if (__state && __result) __state.IdleHit = true; }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p == null || !IsChef(body) || __instance.owner != body.gameObject || (p.Index != 2 && p.Index != 3)) return;
            __instance.damage = body.damage * C.Get(p.Skill.Damage) / 100f;
            __instance.procCoefficient = C.Get(p.Skill.Proc); __instance.falloffModel = BulletFalloff(C.Get(p.Skill.Falloff));
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p == null || !IsChef(body) || __instance.attacker != body.gameObject) return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
            if (p.Index == 11) __instance.baseDamage = body.damage * C.Get(p.Skill.Damage) / 100f;
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff v) => v == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : v == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : v == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : v == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static BulletAttack.FalloffModel BulletFalloff(S.BulletFalloff v) => v == S.BulletFalloff.Default ? BulletAttack.FalloffModel.DefaultBullet : v == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.None;
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            Shot shot = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<Shot>() : null;
            if (!shot || shot.Profile.Skill.Falloff == null || shot.Pool) return;
            float distance = Vector3.Distance(shot.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(shot.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(25f, 60f, distance));
            if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(7f, 25f, distance));
        }
        private static bool BeforeCleaverThrow(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p?.Index != 0 || !IsChef(body) || (bool)(Read(__instance, "isRecall") ?? false)) return true;
            GenericSkill slot = body.GetComponent<SkillLocator>().primary;
            int required = p.Skill.RequireAll ? slot.maxStock : p.Skill.Definition.requiredStock;
            return slot.stock >= required;
        }
        private static void BeforeDiceDeduct(GenericSkill __instance, ref int __0)
        {
            Profile p = ForState(activeState);
            if (p?.Index != 0 || !IsChef(__instance.characterBody) || __instance != __instance.characterBody.GetComponent<SkillLocator>().primary) return;
            __0 = p.Skill.ConsumeAll ? __instance.stock : p.Skill.Definition.stockToConsume;
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        {
            __state = null;
            if (!IsChef(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Index == 0 || p.Skill.ConsumeAll)
            { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = p.Index == 0 ? 0 : skillSlot.stock; }
        }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception)
        { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (IsChef(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (IsChef(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (IsChef(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.CooldownAfterDuration && skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
        }
        private static string Percent(float value) => S.Number(value) + "%";
        private static string Damage(float value) => S.Damage(Percent(value) + " damage");
        private static string PassiveDescription() => "Killing enemies with at least " + S.Utility(C.Get(mealDebuffs).ToString()) + " unique cooking debuffs drops a meal. Each unique cooking debuff adds <style=cIsHealing>" + S.Number(C.Get(mealFraction)) + "% maximum health and " + S.Number(C.Get(mealFlat)) + " health</style> to its healing and increases pickup range.";
        private static string Description(Profile p)
        {
            string agile = p.Skill.AgileText; float damage = C.Get(p.Skill.Damage);
            switch (p.Index)
            {
                case 0: return agile + "Throw a cleaver for " + Damage(damage) + ". Suspend it to deal " + S.DamageText(F(p, "Suspended Damage (%)"), F(p, "Suspended Interval"), true) + ". A suspended cleaver charges after " + S.Utility(S.Number(F(p, "Suspended Charged Time")) + " seconds") + ", dealing " + S.DamageText(F(p, "Charged Damage (%)"), F(p, "Suspended Interval"), true) + ". Recall your cleavers to strike enemies again.";
                case 1: return agile + "Throw a barrage of enhanced cleavers for " + Damage(damage) + " each. They return to CHEF automatically.";
                case 2: return agile + "Sear enemies in front of you for " + S.DamageText(damage, C.Get(p.Skill.Interval), true) + ", <style=cIsDamage>igniting</style> them.";
                case 3: return agile + "Launch " + S.Utility(I(p, "Number Of Fireballs").ToString()) + " fireballs for " + Damage(FireballDamage(p)) + " each, then sear enemies for " + S.DamageText(damage, C.Get(p.Skill.Interval), true) + ", <style=cIsDamage>igniting</style> them.";
                case 4: return agile + "Fire " + S.Utility(I(p, "Number Of Ice Cubes").ToString()) + " ice cubes for " + Damage(damage) + " each, inflicting <style=cIsUtility>Frost</style>. Oiled enemies are <style=cIsUtility>Frozen</style>.";
                case 5: return agile + "Launch a massive ice cube for " + Damage(damage) + ", <style=cIsUtility>freezing</style> enemies.";
                case 6:
                case 7: return agile + "Charge up and roll forward for " + Damage(damage) + ". Each charge level adds " + Damage(F(p, "Extra Damage Per Charge Level (%)")) + " and " + S.Utility(S.Number(F(p, "Extra Duration Per Charge Level")) + " seconds") + " of rolling." + (p.Index == 7 ? " Surround yourself with spinning blades for " + Damage(F(p, "Spinning Blade Damage (%)")) + " per hit." : "");
                case 8:
                case 9: return agile + "Launch forward, leaving oil behind and dealing " + Damage(damage) + " on landing. Reactivate up to " + S.Utility(I(p, "Maximum Reactivations").ToString()) + " times." + (p.Index == 9 ? " Leave enhanced oil that burns or freezes enemies." : " Ignite oil with Sear to burn enemies.");
                case 10: return agile + "Fire " + S.Utility(I(p, "Number Of Oil Globs").ToString()) + " oil globs for " + Damage(damage) + " each, <style=cIsUtility>Weakening</style> and coating enemies in oil.";
                default: return agile + "Unleash a blast for " + Damage(damage) + " and <style=cIsUtility>enhance your next skill</style> with Yes, CHEF!";
            }
        }
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) text += "\nDuration: " + S.Utility(S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.Index == 0) text += "\nSuspended Proc Coefficient: " + S.Utility(S.Number(F(p, "Suspended Proc Coefficient"))) + "\nCharged Proc Coefficient: " + S.Utility(S.Number(F(p, "Charged Proc Coefficient")));
            if (p.Index == 6 || p.Index == 7) text += "\nCharge Time: " + S.Utility(S.Number(F(p, "Charge Time")) + "s") + "\nArmour: " + S.Utility(S.Number(F(p, "Armour Boost While Active")));
            if (p.Index == 8 || p.Index == 9) text += "\nHigh Launch Globs: " + S.Utility(I(p, "Dropped Oil Globs At Full High Launch").ToString()) + "\nExtra Globs Per 100% Movement Speed: " + S.Utility(S.Number(F(p, "Additional Globs Per 100% Movement Speed"))) + "\nBurning Oil Proc Coefficient: " + S.Utility(S.Number(F(p, "Oil Pool Ignite Proc Coefficient")));
            return text;
        }
    }
}
