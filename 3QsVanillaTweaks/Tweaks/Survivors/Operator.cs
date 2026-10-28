using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Operator
    {
        private const string Root = "Survivors - 17 Operator", StatsRoot = "Survivor-Stats - 17 Operator";
        private static readonly string[] Names = { "H3-11 OCR Custom", "Admin-Override", "CMD-SWARM", "Ascent Protocol", "Firewall", "Ejection Core", "Amp Core" };
        private static readonly string[] StateNames = { "Weapon.FireNanoPistol", "Weapon.Activate", "Weapon.Paint.Paint", "DroneLeap", "Weapon.ShieldFormation", "Weapon.ThrowDroneBall", "Weapon.ThrowDroneBallShootable" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal string NativeText;
            internal float NativeDamage, NativeMax, NativeRadius;
            internal GameObject Projectile;
            internal readonly List<GameObject> Projectiles = new List<GameObject>();
            internal readonly Dictionary<string, ConfigEntry<float>> Values = new Dictionary<string, ConfigEntry<float>>();
            internal SkillDef Repeat;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<Tuple<FieldInfo, object, object>> Saved = new List<Tuple<FieldInfo, object, object>>();
        }
        private sealed class Capacity { internal int Previous = -1; }
        private sealed class Grant { internal GenericSkill Slot; internal int Previous; }
        private sealed class ProjectileTag { internal Profile Profile; internal bool Applied; }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly HashSet<MethodBase> hooks = new HashSet<MethodBase>();
        private static readonly ConfigEntry<bool>[] switches = new ConfigEntry<bool>[7];
        private static readonly ConditionalWeakTable<ProjectileController, ProjectileTag> projectiles = new ConditionalWeakTable<ProjectileController, ProjectileTag>();
        private static readonly ConditionalWeakTable<GenericSkill, Capacity> capacities = new ConditionalWeakTable<GenericSkill, Capacity>();
        private static ConfigEntry<bool> enabled, statsEnabled;
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState active;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.DroneTech." + name);
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(obj);
                PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p?.GetGetMethod(true) != null) return p.GetValue(obj, null);
            }
            return null;
        }
        private static void Write(object obj, string name, object value)
        {
            if (obj == null) return;
            FieldInfo f = AccessTools.Field(obj.GetType(), name);
            if (f != null) { f.SetValue(obj, Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture)); return; }
            AccessTools.Property(obj.GetType(), name)?.GetSetMethod(true)?.Invoke(obj, new[] { value });
        }
        private static float Number(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible n ? Convert.ToSingle(n, CultureInfo.InvariantCulture) : fallback;
        private static GameObject Asset(object obj) => obj as GameObject ?? Read(obj, "gameObject") as GameObject;
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsOperator(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static float F(Profile p, string key) => C.Get(p.Values[key]);
        private static void Float(Profile p, string key, float value, float vanilla, string text, float minimum = 0f) => p.Values[key] = S.Float(p.Skill.Section, key, value, vanilla, text, minimum);
        private static Profile Definition(SkillDef def) => def && definitions.TryGetValue(def, out Profile p) ? p : null;
        private static Profile CommandProfile(EntityState state)
        {
            CharacterBody owner = Read(state, "leaderBody") as CharacterBody;
            if (!owner)
            {
                CharacterBody drone = Body(state);
                Component receiver = drone && GameType("RoR2.DroneCommandReceiver") != null ? drone.GetComponent(GameType("RoR2.DroneCommandReceiver")) : null;
                owner = Read(receiver, "leaderBody") as CharacterBody;
            }
            if (!IsOperator(owner)) return null;
            Profile p = Definition(owner.GetComponent<SkillLocator>()?.secondary?.skillDef);
            if (p == null || p.Index != 1 && p.Index != 2) return null;
            // Headbutt is CMD-SWARM; other drone commands belong to Admin-Override.
            return state.GetType().Name == "CommandHeadbutt" ? p.Index == 2 ? p : null : p.Index == 1 ? p : null;
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null) return null;
            string ns = state.GetType().Namespace ?? "";
            if (ns == "EntityStates.Drone.Command") return CommandProfile(state);
            if (!IsOperator(Body(state))) return null;
            string name = state.GetType().FullName;
            int i = name.StartsWith("EntityStates.DroneTech.Weapon.Paint.", StringComparison.Ordinal) ? 2 :
                name == "EntityStates.DroneTech.Weapon.FireShield" ? 4 :
                name == "EntityStates.DroneTech.DroneCarry" || name == "EntityStates.DroneTech.DroneCarryRepeat" || name == "EntityStates.DroneTech.DroneLeapRepeat" ? 3 :
                Array.FindIndex(StateNames, n => "EntityStates.DroneTech." + n == name);
            return profiles.TryGetValue(i, out Profile p) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Operator changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Operator stat changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Load()
        {
            try
            {
                prefab = BodyCatalog.FindBodyPrefab("DroneTechBody")?.GetComponent<CharacterBody>();
                if (!prefab) throw new InvalidOperationException("Operator body was not found.");
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Operator");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefab);
                var defs = prefab.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        Type type = State(StateNames[i]);
                    SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == type);
                    if (!def) def = SkillCatalog.allSkillDefs.FirstOrDefault(d => d && NormalName(Language.GetString(d.skillNameToken)) == NormalName(Names[i]));
                    if (!def) { ThreeQsVanillaTweaks.Log.Warning("Operator skill unavailable: " + Names[i]); continue; }
                    var p = new Profile { Index = i, NativeText = Language.GetString(def.skillDescriptionToken), Skill = new S.Skill(Root + " - " + Names[i], def, def.activationState.stateType, switches[i]) };
                    Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                    S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => p.Skill.Proc == null ? 1f : C.Get(p.Skill.Proc));
                }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Operator " + Names[i] + " changes could not be loaded: " + e); }
                }
                Install(); Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Operator changes could not be loaded: " + e); }
        }
        private static string NormalName(string text) => Regex.Replace(text ?? "", "[^A-Za-z0-9]", "").ToLowerInvariant();
        private static object Sample(Type type) => type == null ? null : EntityStateCatalog.InstantiateState(type);
        private static void Bind(Profile p)
        {
            int i = p.Index; S.Skill s = p.Skill; object sample = s.Sample;
            s.Bind(damage: false, changedAgile: i == 1 || i == 3 || i == 5 || i == 6 ? (bool?)true : null);
            object attack = i == 4 ? Sample(State("Weapon.FireShield")) : sample;
            p.NativeDamage = i == 1 || i == 2 ? 100f : Number(attack, "damageCoefficient", 1f) * 100f;
            p.NativeMax = Number(attack, "maxDamageCoefficient", p.NativeDamage / 100f) * 100f;
            if (i == 0) s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff with distance. Fully charged shots have no falloff. Vanilla: Default.");
            if (i != 3)
            {
                GameObject initialProjectile = Asset(Read(attack, "projectilePrefab"));
                float nativeProc = initialProjectile ? Number(initialProjectile.GetComponent<ProjectileController>(), "procCoefficient", 1f) : Number(attack, "procCoefficient", 1f);
                if (i == 4)
                {
                    GameObject shieldProjectile = Asset(Read(attack, "projectile100"));
                    if (shieldProjectile) nativeProc = Number(shieldProjectile.GetComponent<ProjectileController>(), "procCoefficient", 1f);
                }
                if (i == 1 || i == 2)
                {
                    s.Damage = S.Float(s.Section, "Damage (%)", 100f, 100f, "Command damage as a percentage of the drone's normal command damage.");
                    s.Proc = S.Float(s.Section, "Proc Coefficient", 1f, 1f, "Multiplier applied to the drone command's proc coefficient.");
                }
                else s.BindDamage(p.NativeDamage, null, nativeProc);
            }
            if (i == 0) Float(p, "Maximum Damage (%)", p.NativeMax, p.NativeMax, "Damage at full charge as a percentage of base damage.");
            if (i == 1 || i == 2)
            {
                // For commands, Damage and Proc Coefficient multiply the drone's own attack values.
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.None, "Damage falloff with distance. Vanilla: None.");
                Float(p, "Extra Stocks Per Drone", 1f, 1f, "Stocks provided by each owned drone. Fractional stocks are rounded down.");
                Float(p, "Extra Damage Per Drone Tier (%)", 25f, 25f, "Additional command damage per drone tier.");
            }
            if (i == 2)
            {
                object headbutt = Sample(GameType("EntityStates.Drone.Command.CommandHeadbutt"));
                float radius = Number(headbutt, "blastRadius", 1f);
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Drone impact radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the impact. Vanilla: None.");
            }
            if (i == 3)
            {
                p.Repeat = Read(Sample(State("ParkourState")), "overrideSkill") as SkillDef;
                if (p.Repeat) Float(p, "Mid-Air Relaunch Cooldown", p.Repeat.baseRechargeInterval, p.Repeat.baseRechargeInterval, "Cooldown between relaunches while airborne, in seconds.");
                else throw new InvalidOperationException("Operator mid-air relaunch skill was not found.");
                object carry = Sample(State("DroneCarry"));
                float duration = Number(carry, "flightDuration", 1f);
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Initial flight duration in seconds.", 0.001f);
            }
            if (i == 4)
            {
                Float(p, "Maximum Damage (%)", 600f, p.NativeMax, "Damage at full shield charge as a percentage of base damage.");
                Float(p, "Shield HP (%)", 100f, 100f, "Shield health as a percentage of the drones' available health.", 0.001f);
                float duration = Number(sample, "shieldDuration", 5f);
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Maximum shield duration in seconds.", 0.001f);
                foreach (FieldInfo field in AccessTools.GetDeclaredFields(State("Weapon.FireShield")))
                    if (field.Name.StartsWith("projectile", StringComparison.Ordinal))
                    { GameObject obj = Asset(field.GetValue(field.IsStatic ? null : attack)); if (obj) p.Projectiles.Add(obj); }
            }
            if (i == 5)
            {
                p.Projectile = Asset(Read(sample, "projectilePrefab"));
                GameObject obj = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/DLC3/Drone Tech/NanoBugDebuffController.prefab").WaitForCompletion();
                Component bug = obj && GameType("RoR2.NanoBugDebuffController") != null ? obj.GetComponent(GameType("RoR2.NanoBugDebuffController")) : null;
                if (!bug) throw new InvalidOperationException("Nano-Bug controller was not found.");
                Float(p, "Nano-Bug Extra Skill Damage (%)", 100f, Number(bug, "damageMultiplier", .5f) * 100f, "Additional damage dealt by Nano-Bugs from skill damage.");
                Float(p, "Nano-Bug Split Hit Damage (%)", 120f, Number(bug, "orbDamageCoefficient", 1f) * 100f, "Maximum damage of each split hit as a percentage of the attacker's base damage.", .001f);
            }
            if (i == 6)
            {
                p.Projectile = Asset(Read(sample, "projectilePrefab"));
                GameObject empty = Asset(Read(sample, "emptyProjectilePrefab")); if (empty) p.Projectiles.Add(empty);
                Component amp = p.Projectile && GameType("RoR2.Projectile.DroneBallShootableController") != null ? p.Projectile.GetComponent(GameType("RoR2.Projectile.DroneBallShootableController")) : null;
                if (!amp)
                {
                    Type ampType = GameType("RoR2.Projectile.DroneBallShootableController");
                    for (int projectileIndex = 0; projectileIndex < ProjectileCatalog.projectilePrefabCount; projectileIndex++)
                    {
                        GameObject candidate = ProjectileCatalog.GetProjectilePrefab(projectileIndex);
                        Component component = candidate && ampType != null ? candidate.GetComponent(ampType) : null;
                        if (component) { p.Projectile = candidate; amp = component; break; }
                    }
                }
                if (!amp) throw new InvalidOperationException("Amp Core controller was not found.");
                p.NativeMax = Number(amp, "maxDamageCoefficient", 10f) * 100f;
                Float(p, "Maximum Damage (%)", 2000f, p.NativeMax, "Maximum stored damage as a percentage of base damage.");
                Float(p, "Maximum Radius", 20f, Number(amp, "maxRadius", 20f), "Maximum explosion radius in metres.");
                s.Radius = S.Float(s.Section, "AOE Radius", Number(amp, "minRadius", 8f), Number(amp, "minRadius", 8f), "Explosion radius before absorbing damage, in metres.");
            }
            if (p.Projectile) p.Projectiles.Add(p.Projectile);
            if (p.Projectiles.Count > 0)
            {
                if (!p.Projectile) p.Projectile = p.Projectiles[p.Projectiles.Count - 1];
                if (p.Index != 6 || p.Projectile.GetComponents<Component>().Any(c => Read(c, "desiredForwardSpeed") is float)) s.BindProjectileSpeed(p.Projectile);
                else if (p.Projectile.GetComponent<ProjectileCharacterController>() is ProjectileCharacterController motion && motion)
                {
                    s.NativeProjectileSpeed = motion.velocity;
                    s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", motion.velocity, motion.velocity, "Projectile speed in metres per second.");
                }
                // A stationary core has no launch-speed setting; its other changes still load.
                ProjectileImpactExplosion explosion = p.Projectile.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    if (s.Radius == null) s.Radius = S.Float(s.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
                    p.NativeRadius = explosion.blastRadius;
                    S.AreaFalloff falloff = AreaName(explosion.falloffModel);
                    s.Area = C.Bind(s.Section, "AOE Damage Falloff", falloff, "Damage falloff from the centre of the explosion. Vanilla: " + falloff + ".");

                }
            }
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(Operator), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(Operator), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Operator), finalizer), ilmanipulator: null);
        }
        private static void Hook(Type type)
        {
            if (type == null) return;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.IsStatic || m.IsAbstract || m.ContainsGenericParameters || m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.Contains("Serialize")) continue;
                if (!hooks.Add(m)) continue;
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(Operator), nameof(BeforeState)), finalizer: new HarmonyMethod(typeof(Operator), nameof(AfterState)), ilmanipulator: null);
            }
        }
        private static void Install()
        {
            if (stats != null) Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats));
            foreach (Profile p in profiles.Values)
            {
                Hook(p.Skill.StateType);
                if (p.Index == 0) Hook(typeof(GenericBulletBaseState));
                if (p.Index == 2) foreach (string n in new[] { "Weapon.Paint.Paint", "Weapon.Paint.Fire", "Weapon.Paint.Finish", "Weapon.Paint.BaseDroneHeadbuttPaintState" }) Hook(State(n));
                if (p.Index == 3) foreach (string n in new[] { "DroneLeapRepeat", "DroneCarry", "DroneCarryRepeat" }) Hook(State(n));
                if (p.Index == 4) Hook(State("Weapon.FireShield"));
                if (p.Index == 5) Hook(State("Weapon.ThrowDroneBall"));
                if (p.Index == 6) Hook(State("Weapon.ThrowDroneBall"));
            }
            if (profiles.ContainsKey(1) || profiles.ContainsKey(2))
            {
                Type def = GameType("RoR2.Skills.DroneTechDroneSkillDef");
                Patch(def, "GetMaxStock", postfix: nameof(AfterMaxStock));
                Patch(def, "OnFixedUpdate", nameof(BeforeDroneUpdate), nameof(AfterDroneUpdate));
                Type command = GameType("EntityStates.Drone.Command.BaseCommandState");
                Patch(command, "OnEnter", postfix: nameof(AfterCommandEnter));
                foreach (Type type in typeof(EntityState).Assembly.GetTypes().Where(t => !t.IsAbstract && command.IsAssignableFrom(t))) Hook(type);
            }
            if (profiles.ContainsKey(1) || profiles.ContainsKey(2)) Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast), args: Type.EmptyTypes);
            if (profiles.Count > 0) Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), args: Type.EmptyTypes);
            if (profiles.ContainsKey(4)) Patch(typeof(CharacterBody), "set_isSprinting", nameof(BeforeFirewallSprint));
            if (profiles.ContainsKey(4)) Patch(GameType("DroneTechShieldController"), "OnIncomingDamageServer", nameof(BeforeShieldDamage), finalizer: nameof(AfterShieldDamage));
            if (profiles.ContainsKey(5))
            {
                Patch(GameType("RoR2.NanoBugDebuffController"), "OnVictimDamaged", nameof(BeforeBug));
                Patch(GameType("RoR2.NanoBugDebuffController"), "FireOrb", nameof(BeforeBug));
            }
            if (profiles.Values.Any(p => p.Projectiles.Count > 0) || profiles.ContainsKey(1) || profiles.ContainsKey(2))
            {
                Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                MethodInfo initialized = AccessTools.Method(typeof(ProjectileController), "DispatchOnInitialized");
                if (initialized != null) harmony.Patch(initialized, postfix: new HarmonyMethod(typeof(Operator), nameof(AfterProjectileInitialized)), ilmanipulator: null);
            }
            if (profiles.ContainsKey(6)) Patch(GameType("RoR2.Projectile.DroneBallShootableController"), "Start", nameof(BeforeAmp));
        }
        private static void Apply()
        {
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 4)
                {
                    p.Skill.Definition.cancelSprintingOnActivation = true;
                    p.Skill.Definition.canceledFromSprinting = false;
                    p.Skill.Definition.forceSprintDuringState = false;
                    p.Skill.SetKeyword("KEYWORD_AGILE", false);
                }
                if (p.Repeat && p.Values.ContainsKey("Mid-Air Relaunch Cooldown")) p.Repeat.baseRechargeInterval = F(p, "Mid-Air Relaunch Cooldown");
                if (p.Index == 5)
                {
                    const string token = "KEYWORD_3QS_OPERATOR_NANOBUG";
                    p.Skill.Definition.keywordTokens = (p.Skill.Definition.keywordTokens ?? Array.Empty<string>()).Where(t => !t.Replace("_", "").Replace("-", "").ToUpperInvariant().Contains("NANOBUG") && !Regex.IsMatch(Language.GetString(t), @"<style=cKeywordName>\s*\[?\s*Nano.?Bug\s*\]?\s*</style>", RegexOptions.IgnoreCase)).ToArray();
                    p.Skill.SetKeyword(token, true); S.SetText(token, () => BugDescription(p));
                }
            }
        }
        private static void BeforeFirewallSprint(CharacterBody __instance, ref bool value)
        {
            if (value && IsOperator(__instance) && __instance.GetComponents<EntityStateMachine>().Any(machine => machine.state != null && State("Weapon.ShieldFormation").IsAssignableFrom(machine.state.GetType()))) value = false;
        }
        private static void BeforeStats(CharacterBody __instance) { if (IsOperator(__instance)) stats?.Apply(__instance); }
        private static void Set(Scope scope, object obj, string field, float value)
        {
            FieldInfo f = obj == null ? null : AccessTools.Field(obj.GetType(), field);
            if (f == null || f.FieldType != typeof(float)) return;
            // Instance values belong to this skill; shared static fields are restored at the end of the call.
            if (f.IsStatic) scope.Saved.Add(Tuple.Create(f, (object)null, f.GetValue(null)));
            f.SetValue(f.IsStatic ? null : obj, value);
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = new Scope { Previous = active }; Profile p = ForState(__instance);
            active = p == null ? null : __instance;
            if (p == null) return; S.Skill s = p.Skill; int i = p.Index;
            if (i == 4 && __instance.GetType() == State("Weapon.ShieldFormation")) Body(__instance).isSprinting = false;
            if (i == 1 || i == 2)
            {
                // Drone command damage is applied at the attack boundary so repeated calls cannot compound it.
                return;
            }
            if (s.Damage != null) Set(__state, __instance, "damageCoefficient", C.Get(s.Damage) / 100f);
            if (s.Proc != null) Set(__state, __instance, "procCoefficient", C.Get(s.Proc));
            if (p.Values.ContainsKey("Maximum Damage (%)")) Set(__state, __instance, "maxDamageCoefficient", F(p, "Maximum Damage (%)") / 100f);
            if (s.Duration != null) Set(__state, __instance, i == 3 ? "flightDuration" : "shieldDuration", C.Get(s.Duration));
        }
        private static Exception AfterState(Exception __exception, Scope __state)
        {
            if (__state != null) { for (int n = __state.Saved.Count - 1; n >= 0; n--) { var v = __state.Saved[n]; v.Item1.SetValue(v.Item2, v.Item3); } active = __state.Previous; }
            return __exception;
        }
        internal static int DroneStocks(int combined, float perDrone, int minimum) => Math.Max(minimum, (int)Math.Floor(combined * (double)perDrone));
        internal static float TierMultiplier(int tier, float percent) => 1f + Math.Max(0, tier) * percent / 100f;
        private static int Combined(GenericSkill slot)
        {
            object tracker = Read(slot.skillInstanceData, "tracker");
            if (!(Read(tracker, "AllDroneBodies") is IList)) return 0;
            object result = AccessTools.Method(GameType("RoR2.Skills.DroneTechDroneSkillDef"), "GetCombinedDrones")?.Invoke(slot.skillDef, new[] { Read(tracker, "AllDroneBodies") });
            return result is int n ? n : 0;
        }
        private static int CapacityFor(GenericSkill slot, Profile p) => DroneStocks(Combined(slot), F(p, "Extra Stocks Per Drone"), slot.skillDef.baseMaxStock);
        private static void AfterMaxStock(GenericSkill __0, ref int __result)
        {
            Profile p = Definition(__0?.skillDef); if (p == null || p.Index != 1 && p.Index != 2) return;
            __result = CapacityFor(__0, p);
        }
        private static void BeforeDroneUpdate(GenericSkill __0, out Grant __state)
        {
            __state = null; Profile p = Definition(__0?.skillDef); if (p == null || p.Index != 1 && p.Index != 2) return;
            object data = __0.skillInstanceData; object tracker = Read(data, "tracker");
            if (!(Read(tracker, "AllDroneBodies") is IList bodies)) return;
            Write(data, "previousDroneCount", bodies.Count); // Replace the native whole-stock grant, retaining normal recharge.
            Capacity c = capacities.GetOrCreateValue(__0); int next = CapacityFor(__0, p);
            __state = new Grant { Slot = __0, Previous = Math.Max(0, next - (c.Previous < 0 ? __0.skillDef.baseMaxStock : c.Previous)) };
            if (c.Previous != next) __0.RecalculateValues();
            c.Previous = next;
        }
        private static void AfterDroneUpdate(Grant __state)
        {
            if (__state == null) return;
            __state.Slot.stock = Math.Min(__state.Slot.maxStock, __state.Slot.stock + __state.Previous);
        }
        private static void AfterCommandEnter(EntityState __instance)
        {
            Profile p = CommandProfile(__instance); if (p != null) Write(__instance, "tierDamageMultiplier", TierMultiplier((int)Number(__instance, "droneTier"), F(p, "Extra Damage Per Drone Tier (%)")));
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(active); if (p == null) return;
            if (p.Index == 1 || p.Index == 2) { __instance.damage *= C.Get(p.Skill.Damage) / 100f; __instance.procCoefficient *= C.Get(p.Skill.Proc); }
            else if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Index == 0 && C.Get(p.Skill.Falloff) == S.BulletFalloff.Default && Number(active, "charge") >= 1f) return;
            __instance.falloffModel = C.Get(p.Skill.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(active); if (p == null || p.Index != 1 && p.Index != 2) return;
            __instance.baseDamage *= C.Get(p.Skill.Damage) / 100f;
            __instance.procCoefficient *= C.Get(p.Skill.Proc);
            if (p.Skill.Radius != null) __instance.radius = C.Get(p.Skill.Radius);
            if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
        }
        private static void BeforeProjectile(ref FireProjectileInfo __0)
        {
            Profile p = ForState(active); if (p == null) return;
            if (p.Index == 1 || p.Index == 2) __0.damage *= C.Get(p.Skill.Damage) / 100f;
            if (p.Index == 6 && p.Projectile) __0.projectilePrefab = p.Projectile;
            if (p.Skill.ProjectileSpeed != null) __0.speedOverride = p.Skill.LaunchSpeed(__0.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController __instance)
        {
            Profile p = ForState(active);
            if (p != null && (p.Index == 1 || p.Index == 2)) projectiles.GetOrCreateValue(__instance).Profile = p;
            BeforeProjectileStart(__instance);
        }
        private static Profile ProjectileProfile(ProjectileController pc)
        {
            if (pc && projectiles.TryGetValue(pc, out ProjectileTag tag) && tag.Profile != null) return tag.Profile;
            if (!pc || !pc.owner || !IsOperator(pc.owner.GetComponent<CharacterBody>())) return null;
            return profiles.Values.FirstOrDefault(p => p.Projectiles.Any(obj => obj && (pc.catalogIndex >= 0 && pc.catalogIndex == Number(obj.GetComponent<ProjectileController>(), "catalogIndex", -2f) || pc.gameObject.name == obj.name + "(Clone)")));
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            Profile p = ProjectileProfile(__instance); if (p == null) return;
            ProjectileTag tag = projectiles.GetOrCreateValue(__instance); if (tag.Applied) return; tag.Applied = true;
            __instance.procCoefficient = p.Index == 1 || p.Index == 2 ? __instance.procCoefficient * C.Get(p.Skill.Proc) : C.Get(p.Skill.Proc);
            if (p.Index == 6 && p.Skill.ProjectileSpeed != null && __instance.GetComponent<ProjectileCharacterController>() is ProjectileCharacterController motion && motion) motion.velocity = C.Get(p.Skill.ProjectileSpeed);
            ProjectileImpactExplosion explosion = __instance.GetComponent<ProjectileImpactExplosion>();
            if (explosion)
            {
                if (p.Skill.Radius != null) explosion.SetExplosionRadius(p.Index == 4 && p.NativeRadius > 0f ? explosion.blastRadius * C.Get(p.Skill.Radius) / p.NativeRadius : C.Get(p.Skill.Radius));
                if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area));
            }
        }
        private static void BeforeAmp(Component __instance)
        {
            Profile p = profiles[6]; ProjectileController pc = __instance.GetComponent<ProjectileController>();
            if (ProjectileProfile(pc) != p) return;
            Write(__instance, "maxDamageCoefficient", F(p, "Maximum Damage (%)") / 100f);
            Write(__instance, "maxRadius", F(p, "Maximum Radius"));
            Write(__instance, "minRadius", Math.Min(F(p, "Maximum Radius"), C.Get(p.Skill.Radius)));
        }
        private static void BeforeBug(object __instance)
        {
            Profile p = profiles[5]; Write(__instance, "damageMultiplier", F(p, "Nano-Bug Extra Skill Damage (%)") / 100f); Write(__instance, "orbDamageCoefficient", F(p, "Nano-Bug Split Hit Damage (%)") / 100f);
        }
        private static void BeforeShieldDamage(Component __instance, DamageInfo __0, out float __state)
        {
            __state = __0.damage;
            if (!(Read(__instance, "Drones") is IEnumerable drones)) return;
            foreach (object drone in drones)
            {
                CharacterBody body = Read(drone, "characterBody") as CharacterBody;
                Component receiver = body ? body.GetComponent(GameType("RoR2.DroneCommandReceiver")) : null;
                if (IsOperator(Read(receiver, "leaderBody") as CharacterBody)) { __0.damage /= F(profiles[4], "Shield HP (%)") / 100f; break; }
            }
        }
        private static Exception AfterShieldDamage(DamageInfo __0, float __state, Exception __exception) { __0.damage = __state; return __exception; }
        private static S.AreaFalloff AreaName(BlastAttack.FalloffModel v) => v == BlastAttack.FalloffModel.Linear ? S.AreaFalloff.Linear : v == BlastAttack.FalloffModel.SweetSpot ? S.AreaFalloff.SweetSpot : v == BlastAttack.FalloffModel.HalfLinear ? S.AreaFalloff.HalfLinear : v == BlastAttack.FalloffModel.QuarterLinear ? S.AreaFalloff.QuarterLinear : S.AreaFalloff.None;
        private static BlastAttack.FalloffModel Area(S.AreaFalloff v) => v == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : v == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : v == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : v == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : BlastAttack.FalloffModel.None;
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string text = p.NativeText;
            if (s.Damage != null && p.Index != 1 && p.Index != 2)
                text = Regex.Replace(text ?? "", @"(?<![\d.])([\d.]+)%", match =>
                {
                    if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return match.Value;
                    if (p.Values.ContainsKey("Maximum Damage (%)") && Math.Abs(value - p.NativeMax) < .0001f) return S.Number(F(p, "Maximum Damage (%)")) + "%";
                    return Math.Abs(value - p.NativeDamage) < .0001f ? S.Number(C.Get(s.Damage)) + "%" : match.Value;
                });
            if (p.Index == 1 || p.Index == 2)
            {
                text = Regex.Replace(text ?? "", @"(?<![\d.])25%", S.Number(F(p, "Extra Damage Per Drone Tier (%)")) + "%");
                text = Regex.Replace(text, @"(?<![\d.])1(?= (?:extra |additional )?stocks?\b)", S.Number(F(p, "Extra Stocks Per Drone")), RegexOptions.IgnoreCase);
            }
            // Preserve the native wording and keyword placement while adding the missing Agile label.
            text = Regex.Replace(text ?? "", @"(?:<style=cIsUtility>)?Agile\.(?:</style>)?\s*", "", RegexOptions.IgnoreCase);
            return (p.Index == 4 ? "" : s.AgileText) + text;
        }
        private static string BugDescription(Profile p) => "<style=cKeywordName>Nano-Bug</style><style=cSub>Skill hits deal an additional " + S.Damage(S.Number(F(p, "Nano-Bug Extra Skill Damage (%)")) + "% damage") + ", split into hits of up to " + S.Damage(S.Number(F(p, "Nano-Bug Split Hit Damage (%)")) + "% base damage") + ". Nano-Bugs juggle enemies and expire after they remain on the ground.</style>";
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Index == 1 || p.Index == 2) text += "\nCommand damage: " + S.Number(C.Get(p.Skill.Damage)) + "%\nStocks per drone: " + S.Number(F(p, "Extra Stocks Per Drone")) + "\nDamage per drone tier: +" + S.Number(F(p, "Extra Damage Per Drone Tier (%)")) + "%";
            if (p.Index == 3) text += "\nMid-air relaunch cooldown: " + S.Number(F(p, "Mid-Air Relaunch Cooldown")) + "s";
            if (p.Index == 4) text += "\nShield HP: " + S.Number(F(p, "Shield HP (%)")) + "% of drone health";
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Number(C.Get(p.Skill.Radius)) + "m";
            if (p.Index == 6) text += "\nMaximum radius: " + S.Number(F(p, "Maximum Radius")) + "m";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile speed: " + S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s";
            return text;
        }
    }
}
