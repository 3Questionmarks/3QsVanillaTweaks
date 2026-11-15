using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.CharacterAI;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Enforcer
    {
        private const string Root = "Survivors - 19 Enforcer", StatsRoot = "Survivor-Stats - 19 Enforcer";
        private static readonly string[] Names = { "Trenchbroom", "Shrapnel Grenade", "Steel Protection", "Surge Protection", "Shield Tackle", "Ripcord", "Rally Point", "Crash Point" };
        private static readonly string[] StateNames = { "FireRiotShotgun", "FireClusterGrenade", "ProtectAndServeActivate", "KineticCharge2Activate", "ShieldDashPrepChargeLight", "FireStunNet", "CrowdControlLeap", "ShieldLeap" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal SkillDef Stance;
            internal object StanceSample;
            internal string Text;
            internal float NativeDamage;
            internal GameObject Projectile, StanceProjectile, StanceChild;
            internal ConfigEntry<S.AreaFalloff> StanceArea;
            internal readonly Dictionary<string, ConfigEntry<float>> Floats = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> Ints = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> Bools = new Dictionary<string, ConfigEntry<bool>>();
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<Tuple<FieldInfo, object>> Saved = new List<Tuple<FieldInfo, object>>();
        }
        private sealed class Taunt { internal GameObject Target; internal float Until; }
        private sealed class ContextScope { internal object Context; internal bool Previous; }
        private sealed class TauntDamageScope { internal DamageInfo Info; internal GameObject Inflictor; }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly HashSet<MethodBase> patched = new HashSet<MethodBase>();
        private static readonly ConfigEntry<bool>[] switches = new ConfigEntry<bool>[8];
        private static readonly ConditionalWeakTable<BaseAI, Taunt> taunts = new ConditionalWeakTable<BaseAI, Taunt>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled;
        private static ConfigEntry<float> minDamage, maxDamage, minRadius, maxRadius, fallResistance, passiveProc;
        private static ConfigEntry<S.AreaFalloff> passiveFalloff;
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState active;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.Enforcer." + name);
        // Optional members are read without Harmony's missing-member warnings.
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
        private static float N(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible n ? n.ToSingle(CultureInfo.InvariantCulture) : fallback;
        private static object Sample(string name) => State(name) is Type t ? EntityStateCatalog.InstantiateState(t) : null;
        private static GameObject Asset(object obj) => obj as GameObject ?? Read(obj, "gameObject") as GameObject;
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsEnforcer(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static float F(Profile p, string key) => C.Get(p.Floats[key]);
        private static int I(Profile p, string key) => C.Get(p.Ints[key]);
        private static bool B(Profile p, string key) => C.Get(p.Bools[key]);
        private static void Float(Profile p, string key, float value, float vanilla, string text, float minimum = 0f) => p.Floats[key] = S.Float(p.Skill.Section, key, value, vanilla, text, minimum);
        private static void Int(Profile p, string key, int value, int vanilla, string text) => p.Ints[key] = S.Int(p.Skill.Section, key, value, vanilla, text);
        private static void Bool(Profile p, string key, bool value, bool vanilla, string text) => p.Bools[key] = S.Bool(p.Skill.Section, key, value, vanilla, text);
        private static Profile ProfileFor(EntityState state)
        {
            if (state == null) return null;
            CharacterBody body = Body(state);
            if (!IsEnforcer(body))
            {
                if (state.GetType().Name.StartsWith("VehicleShieldDash", StringComparison.Ordinal))
                {
                    VehicleSeat seat = state.outer ? state.outer.GetComponent<VehicleSeat>() : null;
                    if (seat && IsEnforcer(seat.currentPassengerBody) && profiles.TryGetValue(4, out Profile tackle)) return tackle;
                }
                return null;
            }
            foreach (Profile p in profiles.Values) if (p.Skill.StateType == state.GetType()) return p;
            string name = state.GetType().Name;
            int index = name.Contains("RiotShotgun") ? 0 : name.Contains("ClusterGrenade") ? 1 :
                name.Contains("ProtectAndServe") ? 2 : name.StartsWith("KineticCharge", StringComparison.Ordinal) ? 3 :
                name.Contains("ShieldDash") ? 4 : name.Contains("StunNet") ? 5 : name.Contains("CrowdControl") ? 6 :
                name == "ShieldSlam" || name == "ShieldSlamImpact" ? 7 : -1;
            return profiles.TryGetValue(index, out Profile result) ? result : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Enforcer changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Enforcer stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Big Man", "Changes Enabled", true, "Enable Big Man changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static string Title(SkillDef def) => Regex.Replace(Language.GetString(def.skillNameToken), "[^A-Za-z0-9]", "").ToLowerInvariant();
        private static void Load()
        {
            try
            {
                prefab = BodyCatalog.FindBodyPrefab("EnforcerBody")?.GetComponent<CharacterBody>();
                if (!prefab) prefab = BodyCatalog.FindBodyPrefab("Enforcer2Body")?.GetComponent<CharacterBody>();
                if (!prefab) throw new InvalidOperationException("Enforcer body was not found.");
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Enforcer");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefab); Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats)); }
                SkillDef[] defs = prefab.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                SkillDef[] all = SkillCatalog.allSkillDefs.ToArray();
                if (passiveEnabled.Value) BindPassive();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        string title = Regex.Replace(Names[i], "[^A-Za-z0-9]", "").ToLowerInvariant();
                        SkillDef def = defs.FirstOrDefault(d => Title(d) == title) ?? defs.FirstOrDefault(d => d.activationState.stateType == State(StateNames[i]));
                        if (!def || def.activationState.stateType == null) throw new InvalidOperationException("Skill definition was not found.");
                        Profile p = new Profile { Index = i, Text = Language.GetString(def.skillDescriptionToken), Skill = new S.Skill(Root + " - " + Names[i], def, def.activationState.stateType, switches[i]) };
                        if (i < 2)
                        {
                            Type shielded = State(i == 0 ? "FireRiotShotgunShielded" : "FireClusterGrenadeShield");
                            p.Stance = all.FirstOrDefault(d => d && d.activationState.stateType == shielded);
                            if (!p.Stance) throw new InvalidOperationException("Defensive stance skill was not found.");
                            p.StanceSample = EntityStateCatalog.InstantiateState(shielded);
                        }
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p;
                        S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => p.Skill.Proc == null ? 1f : C.Get(p.Skill.Proc));
                        if (p.Stance)
                        {
                            // Keep the transformed skills' tooltip tokens distinct from their unshielded versions.
                            string token = "3QS_ENFORCER_" + i + "_DEFENSIVE_DESCRIPTION";
                            p.Stance.skillDescriptionToken = token;
                            string stanceName = Language.GetString(p.Stance.skillNameToken);
                            p.Stance.skillNameToken = "3QS_ENFORCER_" + i + "_DEFENSIVE_NAME";
                            S.SetText(p.Stance.skillNameToken, () => stanceName);
                            S.Skill stanceSkill = new S.Skill(p.Skill.Section, p.Stance, p.Stance.activationState.stateType, switches[i]);
                            S.SetDescription(stanceSkill, token, () => StanceDescription(p));
                            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(p.Stance, () => Extra(p), () => F(p, "Defensive Stance Proc Coefficient"));
                        }
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Enforcer " + Names[i] + " changes could not be loaded: " + e); }
                }
                Install(); Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Enforcer changes could not be loaded: " + e); }
        }
        private static void BindPassive()
        {
            string section = Root + " - Big Man";
            minDamage = S.Float(section, "Minimum Damage (%)", 400f, 400f, "Minimum landing damage as a percentage of base damage.");
            maxDamage = S.Float(section, "Maximum Damage (%)", 3000f, 3000f, "Landing damage at a falling speed of 1000 metres per second.");
            minRadius = S.Float(section, "Minimum Radius", 6f, 6f, "Minimum landing blast radius in metres.");
            maxRadius = S.Float(section, "Maximum Radius", 40f, 40f, "Maximum landing blast radius in metres.");
            fallResistance = S.Float(section, "Fall Damage Resistance (%)", 0f, 0f, "Reduction to fall damage taken.");
            passiveProc = S.Float(section, "Proc Coefficient", 1f, 1f, "Proc coefficient of the landing blast.");
            passiveFalloff = C.Bind(section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the blast. Vanilla: None.");
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            if (locator && locator.passiveSkill.enabled)
                S.SetText(locator.passiveSkill.skillDescriptionToken, () => "<style=cIsUtility>Massive.</style> Reduce incoming knockback. Falling onto enemies <style=cIsDamage>stuns</style> and deals " + S.Damage(S.Number(C.Get(minDamage)) + "%–" + S.Number(C.Get(maxDamage)) + "% damage") + "." + (C.Get(fallResistance) > 0f ? " Take " + S.Utility(S.Number(Mathf.Min(100f, C.Get(fallResistance))) + "% less fall damage") + "." : ""));
            Patch(GameType("RoR2.Items.BigmanController"), "Start", nameof(BeforeBigMan));
            Patch(GameType("RoR2.Items.BigmanController"), "TryTriggerLandingBlast", nameof(BeforeBigMan));
            Patch(typeof(HealthComponent), "TakeDamageProcess", nameof(BeforeFallDamage));
        }
        private static void Bind(Profile p)
        {
            int i = p.Index; S.Skill s = p.Skill;
            s.Bind(changedCooldown: i == 6 ? (float?)45f : null, damage: false);
            object attack = i == 2 ? Sample("ProtectAndServeDeactivate") : i == 3 ? Sample("KineticChargeExplode") : i == 4 ? Sample("ProxyShieldDashLight") : i == 6 ? Sample("CrowdControlSlamGround") : i == 7 ? Sample("ShieldSlamImpact") : s.Sample;
            if (i == 1 || i == 5) p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
            float nativeProc = p.Projectile ? N(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f) : N(attack, i == 6 ? "blastProcCoefficient" : "procCoefficient", 1f);
            p.NativeDamage = N(attack, i == 6 ? "blastDamageCoefficient" : "damageCoefficient", 1f) * 100f;
            if (i == 0)
            {
                s.Damage = S.Float(s.Section, "Damage (%)", p.NativeDamage, p.NativeDamage, "Damage per pellet as a percentage of base damage.");
                float proc = N(attack, "procCoefficient", 1f);
                s.Proc = S.Float(s.Section, "Proc Coefficient", proc, proc, "Proc coefficient per pellet.");
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff over distance. Vanilla: Default.");
            }
            else
                s.BindDamage(p.NativeDamage, i == 3 ? 250f : i == 4 ? 150f : i == 7 ? 700f : (float?)null, nativeProc);
            if (i < 2)
            {
                Bool(p, "Tracer", true, Read(s.Sample, "canBeEmpowered") is bool tracer && tracer, "Reloading to full loads the final shot with a guaranteed critical strike.");
                Float(p, "Defensive Stance Damage (%)", i == 0 ? 70f : 300f, N(p.StanceSample, "damageCoefficient", 1f) * 100f, "Damage per shot or pellet while in defensive stance.");
                Float(p, "Defensive Stance Proc Coefficient", .7f, N(p.StanceSample, "procCoefficient", 1f), "Proc coefficient while in defensive stance.");
                Int(p, "Defensive Stance Stocks", i == 0 ? 9 : 5, p.Stance.baseMaxStock, "Maximum shells while in defensive stance.");
                if (i == 0)
                {
                    Int(p, "Pellets", (int)N(s.Sample, "bulletCount", 1), (int)N(s.Sample, "bulletCount", 1), "Pellets fired per shot.");
                    Int(p, "Defensive Stance Pellets", 6, (int)N(p.StanceSample, "bulletCount", 1), "Pellets fired per shot while in defensive stance.");
                }
                else
                {
                    p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
                    p.StanceProjectile = Asset(Read(p.StanceSample, "projectilePrefab"));
                    if (!p.Projectile || !p.StanceProjectile) throw new InvalidOperationException("Grenade projectile was not found.");
                    s.BindProjectileSpeed(p.Projectile);
                    ProjectileImpactExplosion explosion = p.Projectile.GetComponent<ProjectileImpactExplosion>();
                    ProjectileImpactExplosion shield = p.StanceProjectile.GetComponent<ProjectileImpactExplosion>();
                    if (!explosion || !shield) throw new InvalidOperationException("Grenade explosion settings were not found.");
                    Int(p, "Bomblets", (int)N(explosion, "childrenCount"), (int)N(explosion, "childrenCount"), "Bomblets released by each grenade.");
                    Int(p, "Defensive Stance Bomblets", 3, (int)N(shield, "childrenCount"), "Bomblets released while in defensive stance.");
                    p.StanceChild = Asset(Read(shield, "childrenProjectilePrefab"));
                    ProjectileImpactExplosion child = p.StanceChild ? p.StanceChild.GetComponent<ProjectileImpactExplosion>() : null;
                    if (!child) throw new InvalidOperationException("Defensive grenade bomblet was not found.");
                    Float(p, "Defensive Stance AOE Radius", N(child, "blastRadius"), N(child, "blastRadius"), "Bomblet explosion radius in metres while in defensive stance.");
                    if (!Enum.TryParse(child.falloffModel.ToString(), out S.AreaFalloff childFalloff)) childFalloff = S.AreaFalloff.None;
                    p.StanceArea = C.Bind(s.Section, "Defensive Stance AOE Damage Falloff", childFalloff, "Bomblet damage falloff while in defensive stance. Vanilla: " + childFalloff + ".");
                    
                    BindArea(s, N(explosion, "blastRadius"), nativeFalloff: explosion.falloffModel);
                }
            }
            if (i == 2)
            {
                object parry = Sample("ProtectAndServeDeactivate");
                Float(p, "Parry Damage Multiplier", 6f, N(parry, "deflectDamageMultiplier", 1f), "Damage multiplier for reflected attacks.");
                Float(p, "Move Speed (%)", 32f, (1f - N(s.Sample, "walkSpeedPenalty")) * 100f, "Movement speed while in defensive stance.");
            }
            if (i == 3)
            {
                Component controller = prefab.GetComponent(GameType("RoR2.EnforcerSurvivorController"));
                Float(p, "Barrier Gained From Block (%)", 50f, N(controller, "kineticChargeBarrierConversionRate") * 100f, "Blocked damage converted into barrier.");
                Float(p, "Move Speed (%)", 20f, (1f - N(s.Sample, "walkSpeedPenalty")) * 100f, "Movement speed while shielding.");
                s.Duration = S.Float(s.Section, "Duration", 8f, N(s.Sample, "durationLimit", N(Sample("KineticChargeShieldActive"), "duration")), "Maximum shield duration in seconds.", .001f);
                Float(p, "Damage Per Barrier And Shield", 2f, N(attack, "damageGrowth", 1f), "Flat damage added per point of barrier and shield consumed.");
                BindArea(s, N(attack, "radius"), 20f);
                Float(p, "Extra Radius Per 50 Barrier And Shield", 5f, N(attack, "radiusGrowth"), "Extra explosion radius per 50 barrier and shield consumed.");
                Float(p, "Maximum Radius", 100f, N(attack, "maxRadius"), "Maximum explosion radius in metres. -1 for unlimited.", -1f);
            }
            if (i == 4)
            {
                Float(p, "Enemy Hit Cooldown", .25f, N(Sample("VehicleShieldDashLight"), "repeatMeleeInterval", .25f), "Seconds before the same enemy can be hit again.", .001f);
                Float(p, "Movement Speed Distance Effectiveness (%)", 45f, N(attack, "normalizedBoostFromSpeedItems", .45f) * 100f, "How much movement speed bonuses and penalties affect tackle distance.");
                float duration = N(s.Sample, "baseDuration", 1f);
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Base tackle charge duration in seconds.", .001f);
            }
            if (i == 5)
            {
                p.Projectile = Asset(Read(s.Sample, "projectilePrefab"));
                if (p.Projectile)
                {
                    s.BindProjectileSpeed(p.Projectile);
                    BindArea(s, N(p.Projectile.GetComponent<ProjectileImpactExplosion>(), "blastRadius"));
                }
                Float(p, "Taunt Duration", 4f, 1f, "Seconds enemies remain taunted.");
                Bool(p, "Fix Taunt", true, false, "Taunted enemies target you and use their normal pathfinding.");
            }
            if (i == 6)
            {
                p.Projectile = Asset(Read(attack, "supportBeaconPrefab"));
                Component beacon = p.Projectile ? p.Projectile.GetComponent(GameType("RoR2.SupportBeaconController")) : null;
                if (!beacon) throw new InvalidOperationException("Rally Point beacon was not found.");
                HoldoutZoneController holdout = p.Projectile.GetComponent<HoldoutZoneController>();
                s.Duration = S.Float(s.Section, "Duration", 30f, holdout.baseChargeDuration, "Beacon duration in seconds.", .001f);
                s.Radius = S.Float(s.Section, "AOE Radius", 40f, holdout.baseRadius, "Beacon radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the landing blast. Vanilla: None.");
                Float(p, "Barrier On Place (%)", 20f, N(beacon, "initialBarrierAmount") * 100f, "Barrier granted to allies on placement, based on your maximum health.");
                Float(p, "Barrier On Kill (%)", 10f, N(beacon, "barrierOnKillAmount") * 100f, "Barrier granted to allies on kills in the beacon, based on your maximum health.");
            }
            if (i == 7)
            {
                Float(p, "Hover Duration", 1f, N(s.Sample, "baseDuration") + N(s.Sample, "extraDurationFromButtonHeld"), "Maximum aiming time before diving, in seconds.", .001f);
                Float(p, "During Dive Damage (%)", 100f, N(Sample("ShieldSlam"), "divingDamageCoefficient", 1f) * 100f, "Damage dealt to enemies hit during the dive.");
                BindArea(s, N(attack, "slamRadius"), 15f);
                Bool(p, "Fix Landing Damage Category", true, false, "Count landing damage as special skill damage.");
            }
        }
        private static void BindArea(S.Skill s, float native, float? changed = null, BlastAttack.FalloffModel nativeFalloff = BlastAttack.FalloffModel.None)
        {
            s.Radius = S.Float(s.Section, "AOE Radius", changed ?? native, native, "Explosion radius in metres.");
            if (!Enum.TryParse(nativeFalloff.ToString(), out S.AreaFalloff falloff)) falloff = S.AreaFalloff.None;
            s.Area = C.Bind(s.Section, "AOE Damage Falloff", falloff, "Damage falloff from the centre of the explosion. Vanilla: " + falloff + ".");
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
            if (method == null) { ThreeQsVanillaTweaks.Log.Warning("Enforcer hook unavailable: " + type?.FullName + "." + name); return; }
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(Enforcer), prefix), postfix == null ? null : new HarmonyMethod(typeof(Enforcer), postfix), null, finalizer == null ? null : new HarmonyMethod(typeof(Enforcer), finalizer), null);
        }
        private static void Install()
        {
            if (profiles.Count == 0) return;
            if (profiles.ContainsKey(0) || profiles.ContainsKey(1))
                Patch(GameType("RoR2.EnforcerEmpoweredShotController"), "ConsumeEmpoweredAuthority", nameof(BeforeConsumeTracer));
            foreach (Type type in typeof(EntityState).Assembly.GetTypes().Where(t => t.Namespace == "EntityStates.Enforcer" && !t.IsAbstract && !t.ContainsGenericParameters && typeof(EntityState).IsAssignableFrom(t)))
            {
                // Only hook state families belonging to an enabled skill.
                string name = type.Name;
                bool wanted = profiles.Values.Any(p => p.Skill.StateType == type) ||
                    profiles.ContainsKey(0) && name.Contains("RiotShotgun") || profiles.ContainsKey(1) && name.Contains("ClusterGrenade") ||
                    profiles.ContainsKey(2) && name.Contains("ProtectAndServe") || profiles.ContainsKey(3) && name.StartsWith("KineticCharge", StringComparison.Ordinal) ||
                    profiles.ContainsKey(4) && name.Contains("ShieldDash") || profiles.ContainsKey(5) && name.Contains("StunNet") ||
                    profiles.ContainsKey(6) && name.Contains("CrowdControl") || profiles.ContainsKey(7) && name.StartsWith("ShieldSlam", StringComparison.Ordinal);
                if (!wanted) continue;
                for (Type t = type; t != null && t != typeof(EntityState); t = t.BaseType)
                    foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name == "OnEnter" || m.Name == "FixedUpdate" || m.Name == "FireBullet" || m.Name == "FireProjectile" || m.Name == "Detonate" || m.Name == "OnActiveEnter" || m.Name == "InitializeStateServer" || m.Name == "OnActiveFixedUpdate"))
                        if (!m.ContainsGenericParameters && patched.Add(m)) harmony.Patch(m, new HarmonyMethod(typeof(Enforcer), nameof(BeforeState)), null, null, new HarmonyMethod(typeof(Enforcer), nameof(EndState)), null);
            }
            Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast), args: Type.EmptyTypes);
            Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), args: Type.EmptyTypes);
            Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
            Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
            MethodInfo init = Method(typeof(ProjectileController), "DispatchOnInitialized");
            if (init != null) harmony.Patch(init, postfix: new HarmonyMethod(typeof(Enforcer), nameof(BeforeProjectileStart)));
            if (profiles.ContainsKey(3)) Patch(GameType("RoR2.EnforcerSurvivorController"), "OnIncomingDamageServerKinetic", nameof(BeforeKineticBlock));
            if (profiles.ContainsKey(5))
            {
                Patch(typeof(SetStateOnHurt), "OnTakeDamageServer", nameof(BeforeTauntDamage), finalizer: nameof(EndTauntDamage));
                Patch(GameType("EntityStates.TauntState"), "OnEnter", nameof(BeforeTaunt), nameof(AfterTaunt));
                Patch(typeof(BaseAI), "EvaluateSkillDrivers", nameof(BeforeAITarget));
                Patch(typeof(BaseAI), "UpdateBodyInputs", nameof(BeforeAITarget));
                Patch(typeof(BaseAI), "UpdateTargets", postfix: nameof(BeforeAITarget));
                Type context = typeof(BaseAI).GetNestedType("SkillDriverEvaluationContext");
                if (context != null) Patch(typeof(BaseAI), "EvaluateSingleSkillDriver", nameof(BeforeAIContext), finalizer: nameof(EndAIContext), args: new[] { typeof(AISkillDriver), context });
            }
            if (profiles.ContainsKey(6)) Patch(GameType("RoR2.SupportBeaconController"), "Start", nameof(BeforeBeacon));
        }
        private static void Apply()
        {
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index < 2)
                {
                    p.Skill.Definition.keywordTokens = (p.Skill.Definition.keywordTokens ?? Array.Empty<string>()).Where(t => t.IndexOf("TRACER", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
                    p.Skill.SetKeyword("KEYWORD_3QS_ENFORCER_TRACER", B(p, "Tracer"));
                }
                if (p.Stance) p.Stance.baseMaxStock = I(p, "Defensive Stance Stocks");
            }
            if (profiles.ContainsKey(0) || profiles.ContainsKey(1)) S.SetText("KEYWORD_3QS_ENFORCER_TRACER", () => "<style=cKeywordName>Tracer</style><style=cSub>Reloading to full loads the final shot with a guaranteed <style=cIsDamage>Critical Strike</style>.</style>");
        }
        private static void BeforeStats(CharacterBody __instance) { if (IsEnforcer(__instance)) stats?.Apply(__instance); }
        private static void Set(Scope scope, object obj, string name, object value)
        {
            FieldInfo f = obj == null ? null : Field(obj.GetType(), name);
            if (f == null) return;
            if (f.IsStatic) scope.Saved.Add(Tuple.Create(f, f.GetValue(null)));
            f.SetValue(f.IsStatic ? null : obj, f.FieldType.IsInstanceOfType(value) ? value : Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture));
        }
        private static bool Shielded(EntityState state) => state.GetType().Name.Contains("Shielded") || state.GetType().Name == "FireClusterGrenadeShield";
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = active }; Profile p = ProfileFor(__instance); active = p == null ? null : __instance;
            if (p == null) return;
            S.Skill s = p.Skill; int i = p.Index; string name = __instance.GetType().Name;
            if (s.Damage != null) Set(__state, __instance, "damageCoefficient", C.Get(s.Damage) / 100f);
            if (s.Proc != null) Set(__state, __instance, "procCoefficient", C.Get(s.Proc));
            if (i < 2 && Shielded(__instance))
            {
                Set(__state, __instance, "damageCoefficient", F(p, "Defensive Stance Damage (%)") / 100f);
                Set(__state, __instance, "procCoefficient", F(p, "Defensive Stance Proc Coefficient"));
            }
            if (i == 0) Set(__state, __instance, "bulletCount", I(p, Shielded(__instance) ? "Defensive Stance Pellets" : "Pellets"));
            if (i < 2) Set(__state, __instance, "canBeEmpowered", B(p, "Tracer"));
            if (i == 2)
            {
                Set(__state, __instance, "walkSpeedPenalty", 1f - F(p, "Move Speed (%)") / 100f);
                Set(__state, __instance, "deflectDamageMultiplier", F(p, "Parry Damage Multiplier"));
            }
            if (i == 3)
            {
                Set(__state, __instance, "walkSpeedPenalty", 1f - F(p, "Move Speed (%)") / 100f);
                Set(__state, __instance, "durationLimit", C.Get(s.Duration)); Set(__state, __instance, "duration", C.Get(s.Duration));
                if (name == "KineticChargeExplode")
                {
                    Set(__state, __instance, "damageGrowth", F(p, "Damage Per Barrier And Shield"));
                    Set(__state, __instance, "radius", C.Get(s.Radius));
                    Set(__state, __instance, "radiusGrowth", F(p, "Extra Radius Per 50 Barrier And Shield"));
                    float maximum = F(p, "Maximum Radius"); Set(__state, __instance, "maxRadius", maximum < 0f ? float.PositiveInfinity : maximum);
                }
            }
            if (i == 4)
            {
                Set(__state, __instance, "normalizedBoostFromSpeedItems", F(p, "Movement Speed Distance Effectiveness (%)") / 100f);
                Set(__state, __instance, "repeatMeleeInterval", F(p, "Enemy Hit Cooldown"));
                Set(__state, __instance, "baseDuration", C.Get(s.Duration));
            }
            if (i == 6 && name == "CrowdControlSlamGround")
            {
                Set(__state, __instance, "blastDamageCoefficient", C.Get(s.Damage) / 100f);
                Set(__state, __instance, "blastProcCoefficient", C.Get(s.Proc));
            }
            if (i == 7)
            {
                if (name == "ShieldLeap")
                {
                    // Manual aiming normally lasts baseDuration / attack-speed scaling + extra hold time.
                    // Keep the ascent animation intact and put the remaining time into the hold window.
                    float total = F(p, "Hover Duration");
                    float scale = 1f + (Body(__instance).attackSpeed - 1f) * N(__instance, "attackSpeedScalingCoefficient", .3f);
                    float ascent = Mathf.Min(N(__instance, "baseDuration", .8f), total * Mathf.Max(.001f, scale));
                    Set(__state, __instance, "baseDuration", ascent);
                    Set(__state, __instance, "baseMinimumDuration", Mathf.Min(N(__instance, "baseMinimumDuration", .6f), ascent));
                    Set(__state, __instance, "extraDurationFromButtonHeld", Mathf.Max(0f, total - ascent / Mathf.Max(.001f, scale)));
                    Set(__state, __instance, "indicatorRadius", C.Get(s.Radius));
                }
                Set(__state, __instance, "divingDamageCoefficient", F(p, "During Dive Damage (%)") / 100f);
                Set(__state, __instance, "slamRadius", C.Get(s.Radius));
            }
        }
        private static Exception EndState(Exception __exception, Scope __state)
        {
            if (__state != null)
            {
                for (int i = __state.Saved.Count - 1; i >= 0; i--) __state.Saved[i].Item1.SetValue(null, __state.Saved[i].Item2);
                active = __state.Previous;
            }
            return __exception;
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff falloff) => falloff == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : falloff == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : falloff == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : falloff == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : BlastAttack.FalloffModel.None;
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ProfileFor(active); if (p?.Index != 0) return;
            bool shield = Shielded(active);
            __instance.damage = Body(active).damage * (shield ? F(p, "Defensive Stance Damage (%)") : C.Get(p.Skill.Damage)) / 100f;
            __instance.procCoefficient = shield ? F(p, "Defensive Stance Proc Coefficient") : C.Get(p.Skill.Proc);
            __instance.bulletCount = (uint)I(p, shield ? "Defensive Stance Pellets" : "Pellets");
            __instance.falloffModel = C.Get(p.Skill.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : shield ? (BulletAttack.FalloffModel)Enum.Parse(typeof(BulletAttack.FalloffModel), "RiotShotgun") : (BulletAttack.FalloffModel)Enum.Parse(typeof(BulletAttack.FalloffModel), "Slug");
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ProfileFor(active); if (p == null || p.Skill.Damage == null) return;
            S.Skill s = p.Skill;
            if (s.Proc != null) __instance.procCoefficient = C.Get(s.Proc);
            if (s.Area != null) __instance.falloffModel = Area(C.Get(s.Area));
            if (p.Index == 7)
            {
                __instance.baseDamage = Body(active).damage * C.Get(s.Damage) / 100f;
                __instance.radius = C.Get(s.Radius);
                if (B(p, "Fix Landing Damage Category")) __instance.damageType.damageSource = DamageSource.Special;
            }
            if (p.Index == 5 && s.Radius != null) __instance.radius = C.Get(s.Radius);
        }
        private static void BeforeProjectile(ref FireProjectileInfo __0)
        {
            Profile p = ProfileFor(active); if (p == null || p.Projectile == null || p.Index == 6) return;
            if (p.Index != 1 && p.Index != 5) return;
            __0.damage = Body(active).damage * (p.Index == 1 && Shielded(active) ? F(p, "Defensive Stance Damage (%)") : C.Get(p.Skill.Damage)) / 100f;
            if (p.Skill.ProjectileSpeed != null) __0.speedOverride = p.Skill.LaunchSpeed(__0.speedOverride);
        }
        private static bool SameProjectile(GameObject instance, GameObject source) => instance && source && instance.name.Replace("(Clone)", "").Trim() == source.name;
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            if (!IsEnforcer(body)) return;
            foreach (Profile p in profiles.Values.Where(p => p.Index == 1 || p.Index == 5))
            {
                bool child = p.Index == 1 && SameProjectile(__instance.gameObject, p.StanceChild);
                bool shield = p.Index == 1 && (child || SameProjectile(__instance.gameObject, p.StanceProjectile));
                bool normal = SameProjectile(__instance.gameObject, p.Projectile);
                if (!shield && !normal) continue;
                __instance.procCoefficient = shield ? F(p, "Defensive Stance Proc Coefficient") : C.Get(p.Skill.Proc);
                ProjectileImpactExplosion explosion = __instance.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    if (p.Index == 1 && !child)
                    {
                        Write(explosion, "childrenCount", I(p, shield ? "Defensive Stance Bomblets" : "Bomblets"));
                        if (shield) Write(explosion, "childrenDamageCoefficient", 1f);
                    }
                    if (child)
                    {
                        Write(explosion, "blastRadius", F(p, "Defensive Stance AOE Radius"));
                        Write(explosion, "falloffModel", Area(C.Get(p.StanceArea)));
                    }
                    else if (normal)
                    {
                        if (p.Skill.Radius != null) Write(explosion, "blastRadius", C.Get(p.Skill.Radius));
                        if (p.Skill.Area != null) Write(explosion, "falloffModel", Area(C.Get(p.Skill.Area)));
                    }
                }
            }
        }
        private static bool BeforeConsumeTracer(ref bool __result)
        {
            Profile p = ProfileFor(active);
            if (p?.Index != 1 || !B(p, "Tracer")) return true;
            GenericSkill slot = Read(active, "activatorSkillSlot") as GenericSkill;
            if (!slot || slot.stock == 0) return true;
            __result = false;
            return false;
        }
        private static void BeforeKineticBlock(Component __instance)
        {
            if (IsEnforcer(__instance.GetComponent<CharacterBody>()) && profiles.TryGetValue(3, out Profile p)) Write(__instance, "kineticChargeBarrierConversionRate", F(p, "Barrier Gained From Block (%)") / 100f);
        }
        private static void BeforeBigMan(Component __instance)
        {
            if (!IsEnforcer(__instance.GetComponent<CharacterBody>())) return;
            Write(__instance, "damageCoefficientMin", C.Get(minDamage) / 100f); Write(__instance, "damageCoefficientMax", C.Get(maxDamage) / 100f);
            Write(__instance, "craterSizeRangeMin", C.Get(minRadius)); Write(__instance, "craterSizeRangeMax", C.Get(maxRadius));
            Write(__instance, "craterBlastProcCoefficient", C.Get(passiveProc));
            FieldInfo f = Field(__instance.GetType(), "overrideFalloff");
            if (f != null) f.SetValue(__instance, Area(C.Get(passiveFalloff)));
        }
        private static void BeforeFallDamage(HealthComponent __instance, DamageInfo __0)
        {
            if (IsEnforcer(__instance.body) && (__0.damageType & DamageType.FallDamage) != DamageType.Generic) __0.damage *= 1f - Mathf.Clamp01(C.Get(fallResistance) / 100f);
        }
        internal static float RallyPointRadius(float fallback) =>
            profiles.TryGetValue(6, out Profile p) ? C.Get(p.Skill.Radius) : fallback;

        private static void BeforeBeacon(Component __instance)
        {
            GenericOwnership owner = __instance.GetComponent<GenericOwnership>();
            if (!owner || !owner.ownerObject || !IsEnforcer(owner.ownerObject.GetComponent<CharacterBody>()) || !profiles.TryGetValue(6, out Profile p)) return;
            Write(__instance, "initialBarrierAmount", F(p, "Barrier On Place (%)") / 100f); Write(__instance, "barrierOnKillAmount", F(p, "Barrier On Kill (%)") / 100f);
            HoldoutZoneController zone = __instance.GetComponent<HoldoutZoneController>();
            if (!zone) return;
            float nativeDuration = zone.baseChargeDuration;
            zone.baseRadius = C.Get(p.Skill.Radius);
            zone.baseChargeDuration = C.Get(p.Skill.Duration);
            zone.gameObject.AddComponent<RallyPointAnimation>().Initialize(zone, nativeDuration);
        }

        private sealed class RallyPointAnimation : MonoBehaviour
        {
            private static readonly int Idle = Animator.StringToHash("Base Layer.idle");
            private static readonly int Deactivate = Animator.StringToHash("Base Layer.Deactivate");
            private const float TransitionDuration = .25f;
            private HoldoutZoneController zone;
            private Animator[] animators;

            internal void Initialize(HoldoutZoneController holdout, float nativeDuration)
            {
                zone = holdout;
                animators = zone.GetComponentsInChildren<Animator>().Where(animator =>
                    animator.isActiveAndEnabled && animator.runtimeAnimatorController &&
                    animator.HasState(0, Idle) && animator.HasState(0, Deactivate)).ToArray();

                float retirementTime = animators.SelectMany(animator => animator.runtimeAnimatorController.animationClips)
                    .Where(clip => clip).Select(clip => clip.length + TransitionDuration).DefaultIfEmpty(0f).Max();
                foreach (DestroyOnTimer timer in zone.GetComponentsInChildren<DestroyOnTimer>(true))
                {
                    if (timer.transform != zone.transform &&
                        !animators.Any(animator => animator.transform.IsChildOf(timer.transform))) continue;
                    float nativeTail = timer.duration - nativeDuration;
                    timer.duration = zone.baseChargeDuration + Mathf.Max(retirementTime, nativeTail);
                }
                enabled = animators.Length > 0;
            }

            private void LateUpdate()
            {
                if (!zone) { enabled = false; return; }
                bool finished = zone.charge >= zone.maxCharge;
                foreach (Animator animator in animators)
                {
                    if (!animator || !animator.isActiveAndEnabled) continue;
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    bool deactivating = state.fullPathHash == Deactivate ||
                        (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == Deactivate);
                    if (finished)
                    {
                        if (!deactivating) animator.CrossFadeInFixedTime(Deactivate, TransitionDuration, 0, 0f);
                    }
                    else if (deactivating || (state.fullPathHash == Idle && state.normalizedTime >= 1f))
                    {
                        // The native idle state exits after a fixed number of loops.
                        animator.Play(Idle, 0, deactivating ? 0f : Mathf.Repeat(state.normalizedTime, 1f));
                        animator.Update(0f);
                    }
                }
                if (finished) enabled = false;
            }
        }
        private static bool IsTaunt(DamageInfo info) => info != null && info.damageType.damageTypeExtended.ToString().Split(',').Any(s => s.Trim() == "Taunt");
        private static void BeforeTauntDamage(DamageReport __0, out TauntDamageScope __state)
        {
            __state = null;
            if (!profiles.TryGetValue(5, out Profile p) || !B(p, "Fix Taunt") || !IsEnforcer(__0.attackerBody) || !IsTaunt(__0.damageInfo)) return;
            __state = new TauntDamageScope { Info = __0.damageInfo, Inflictor = __0.damageInfo.inflictor };
            // Native code uses the inflictor as the taunt target, which is normally a transient projectile.
            __0.damageInfo.inflictor = __0.attacker;
        }
        private static Exception EndTauntDamage(Exception __exception, TauntDamageScope __state)
        {
            if (__state != null) __state.Info.inflictor = __state.Inflictor;
            return __exception;
        }
        private static void BeforeTaunt(EntityState __instance)
        {
            GameObject target = Read(__instance, "tauntTarget") as GameObject;
            ProjectileController projectile = target ? target.GetComponent<ProjectileController>() : null;
            GameObject owner = projectile ? projectile.owner : target;
            if (owner && IsEnforcer(owner.GetComponent<CharacterBody>()) && profiles.TryGetValue(5, out Profile p)) Write(__instance, "tauntDuration", F(p, "Taunt Duration"));
        }
        private static void AfterTaunt(EntityState __instance)
        {
            GameObject target = Read(__instance, "tauntTarget") as GameObject; CharacterBody victim = Body(__instance);
            if (!target || !IsEnforcer(target.GetComponent<CharacterBody>()) || !victim || !victim.master || !profiles.TryGetValue(5, out Profile p) || !B(p, "Fix Taunt") || !NetworkServer.active) return;
            foreach (BaseAI ai in victim.master.GetComponents<BaseAI>())
            {
                Taunt taunt = taunts.GetOrCreateValue(ai); taunt.Target = target; taunt.Until = Time.fixedTime + F(p, "Taunt Duration");
                ai.currentEnemy.gameObject = target; ai.customTarget.gameObject = target; ai.enemyAttention = F(p, "Taunt Duration");
            }
        }
        private static bool ActiveTaunt(BaseAI ai, out Taunt taunt) => taunts.TryGetValue(ai, out taunt) && taunt.Target && Time.fixedTime < taunt.Until;
        private static void BeforeAITarget(BaseAI __instance)
        {
            if (!ActiveTaunt(__instance, out Taunt taunt)) return;
            __instance.currentEnemy.gameObject = taunt.Target;
            __instance.enemyAttention = Mathf.Max(__instance.enemyAttention, taunt.Until - Time.fixedTime);
        }
        private static void BeforeAIContext(BaseAI __instance, object __1, out ContextScope __state)
        {
            __state = null;
            if (!ActiveTaunt(__instance, out _)) return;
            __state = new ContextScope { Context = __1, Previous = Read(__1, "hasTauntedDebuff") is bool value && value };
            // Retain normal movement and attack drivers; only their current enemy changes.
            Write(__1, "hasTauntedDebuff", false);
        }
        private static Exception EndAIContext(Exception __exception, ContextScope __state)
        {
            if (__state != null) Write(__state.Context, "hasTauntedDebuff", __state.Previous);
            return __exception;
        }
        private static string StanceDescription(Profile p) => p.Skill.AgileText + (p.Index == 0 ? "Fire " + S.Damage(I(p, "Defensive Stance Pellets") + " pellets") + " for " : "Fire a shrapnel grenade for ") + S.Damage(S.Number(F(p, "Defensive Stance Damage (%)")) + "% damage") + (p.Index == 1 ? ", releasing " + S.Utility(I(p, "Defensive Stance Bomblets") + " bomblets") : " per pellet") + ".";
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill;
            string tracer = p.Index < 2 && B(p, "Tracer") ? "<style=cIsHealing>Tracer.</style> " : "";
            if (p.Index == 0) return s.AgileText + tracer + "Fire " + (I(p, "Pellets") == 1 ? "a slug" : S.Damage(I(p, "Pellets") + " pellets")) + " for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + (I(p, "Pellets") == 1 ? "" : " each") + ". In <style=cIsUtility>Defensive Stance</style>, rapidly fire a <style=cIsUtility>piercing shell</style> for " + S.Damage(I(p, "Defensive Stance Pellets") + "x" + S.Number(F(p, "Defensive Stance Damage (%)")) + "% damage") + ".";
            if (p.Index == 1) return s.AgileText + tracer + "Fire an arcing grenade for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + (I(p, "Bomblets") > 0 ? ", releasing " + S.Utility(I(p, "Bomblets") + " bomblets") : "") + ". In <style=cIsUtility>Defensive Stance</style>, fire a cluster grenade for " + S.Damage(I(p, "Defensive Stance Bomblets") + "x" + S.Number(F(p, "Defensive Stance Damage (%)")) + "% damage") + ".";
            if (p.Index == 2) return s.AgileText + "Take a <style=cIsUtility>Defensive Stance</style>. Reactivate to exit your shield, <style=cIsDamage>knocking away</style>, <style=cIsDamage>stunning</style>, and <style=cIsUtility>parrying</style> enemies for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + ". Reflect attacks for " + S.Damage(S.Number(F(p, "Parry Damage Multiplier")) + "x damage") + ".";
            if (p.Index == 3) return s.AgileText + "Raise your shield for " + S.Utility(S.Number(C.Get(s.Duration)) + " seconds") + ", converting " + S.Utility(S.Number(F(p, "Barrier Gained From Block (%)")) + "% of blocked damage into barrier") + ". Detonate your barrier and shield for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + " plus " + S.Damage(S.Number(F(p, "Damage Per Barrier And Shield")) + " damage per point consumed") + ".";
            if (p.Index == 7) return s.AgileText + "Hover, then dive down, <style=cIsDamage>stunning</style> enemies hit for " + S.Damage(S.Number(F(p, "During Dive Damage (%)")) + "% damage") + ". Crash into the ground, <style=cIsDamage>stunning</style> nearby enemies for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + ".";
            string text = Regex.Replace(p.Text, @"(?:<style=cIsUtility>)?Agile\.(?:</style>)?\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?<![\d.])" + Regex.Escape(S.Number(p.NativeDamage)) + "%", S.Number(C.Get(s.Damage)) + "%");
            if (p.Index == 5) text = Regex.Replace(text, @"\b\d+(?:\.\d+)?(?=(?:</style>)? seconds?)", S.Number(F(p, "Taunt Duration")));
            if (p.Index == 6) text += " Grant allies barrier equal to " + S.Utility(S.Number(F(p, "Barrier On Place (%)")) + "% of your maximum health") + " on placement and " + S.Utility(S.Number(F(p, "Barrier On Kill (%)")) + "% on kills") + ".";
            return s.AgileText + text;
        }
        private static string Extra(Profile p)
        {
            string text = ""; S.Skill s = p.Skill;
            if (s.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(s.Radius)) + "m");
            if (s.Duration != null) text += "\nDuration: " + S.Utility(S.Number(C.Get(s.Duration)) + "s");
            if (s.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (p.Index < 2) text += "\nDefensive Stance Shells: " + S.Utility(I(p, "Defensive Stance Stocks").ToString());
            if (p.Index == 2 || p.Index == 3) text += "\nDefensive Movement Speed: " + S.Utility(S.Number(F(p, "Move Speed (%)")) + "%");
            if (p.Index == 7) text += "\nAiming Time: " + S.Utility(S.Number(F(p, "Hover Duration")) + "s");
            return text;
        }
    }
}
