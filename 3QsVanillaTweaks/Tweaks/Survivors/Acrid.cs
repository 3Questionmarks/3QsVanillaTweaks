using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Acrid
    {
        private const string Root = "Survivors - 10 Acrid", StatsRoot = "Survivor-Stats - 10 Acrid";
        private static readonly string[] Names = { "Vicious Wounds", "Neurotoxin", "Ravenous Bite", "Caustic Leap", "Frenzied Leap", "Epidemic", "Cloying Breath" };
        private static readonly string[] States = { "Slash", "FireSpit", "Bite", "Leap", "ChainableLeap", "FireDiseaseProjectile", "Breath" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal float NativeRadius;
            internal ConfigEntry<int> MaxInstances;
            internal ConfigEntry<float> MovementScaling;
        }
        private sealed class Toxin
        {
            internal DotController.DotIndex Index;
            internal ConfigEntry<float> Interval, HealthDamage, Damage, Cap, Duration;
            internal ConfigEntry<bool> Armour, Lethal, Refresh;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class Tick
        {
            internal object Stack;
            internal Toxin Toxin;
            internal GameObject Attacker;
            internal float BaseDamage, Multiplier, Next;
        }
        private sealed class DotData { internal readonly List<Tick> Ticks = new List<Tick>(); }
        private sealed class Application
        {
            internal Toxin Toxin;
            internal GameObject Attacker;
            internal float Damage, Multiplier;
            internal HashSet<object> Before;
        }
        private sealed class Evaluation { internal readonly List<object> Hidden = new List<object>(); }
        private sealed class ProjectileMarker : MonoBehaviour
        {
            internal Profile Profile;
            internal GameObject Owner;
            internal Vector3 Origin;
            internal float Born;
            private void FixedUpdate()
            {
                if (NetworkServer.active && Profile?.MaxInstances != null && C.Get(Profile.Skill.Duration) >= 0f && Time.fixedTime - Born >= C.Get(Profile.Skill.Duration)) NetworkServer.Destroy(gameObject);
            }
        }
        private sealed class DiseaseData { }
        private sealed class LeapSpeed { internal float Baseline; }
        private static readonly ConditionalWeakTable<EntityState, LeapSpeed> leapSpeeds = new ConditionalWeakTable<EntityState, LeapSpeed>();
        private sealed class Recovery { internal float Remaining, Rate; }
        private sealed class Recoveries { internal readonly List<Recovery> Active = new List<Recovery>(); }
        private static readonly ConditionalWeakTable<HealthComponent, Recoveries> recoveries = new ConditionalWeakTable<HealthComponent, Recoveries>();
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<DotController.DotIndex, Toxin> toxins = new Dictionary<DotController.DotIndex, Toxin>();
        private static readonly ConditionalWeakTable<DotController, DotData> dotData = new ConditionalWeakTable<DotController, DotData>();
        private static readonly ConditionalWeakTable<LightningOrb, DiseaseData> diseases = new ConditionalWeakTable<LightningOrb, DiseaseData>();
        private static readonly HashSet<MethodBase> stateHooks = new HashSet<MethodBase>();
        private static readonly List<ProjectileMarker> areas = new List<ProjectileMarker>();
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static ConfigEntry<bool> enabled, statsEnabled, regenerativeEnabled, poisonEnabled, blightEnabled, healing;
        private static ConfigEntry<float> healingAmount, healingTime, thirdHit, acidDamage, acidProc, acidInterval, acidHits, refund, spreadRange;
        private static ConfigEntry<int> spreadTargets, spreadRepeats, primaryHitBoxSize;
        private static MethodInfo removeStack;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static LightningOrb activeDisease;
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Croco." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsAcrid(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static object Read(object instance, string name)
        {
            if (instance == null) return null;
            for (Type t = instance.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                FieldInfo f = t.GetField(name, flags); if (f != null) return f.GetValue(instance);
                PropertyInfo p = t.GetProperty(name, flags); if (p != null) return p.GetValue(instance, null);
            }
            return null;
        }
        private static float Number(object instance, string name, float fallback = 0f) => Read(instance, name) is IConvertible n ? Convert.ToSingle(n) : fallback;
        private static GameObject Prefab(object value)
        {
            if (value is GameObject go) return go;
            if (value == null) return null;
            MethodInfo conversion = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(GameObject) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(value));
            return conversion == null ? Read(value, "gameObject") as GameObject : conversion.Invoke(null, new[] { value }) as GameObject;
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null || state.GetType().Namespace != "EntityStates.Croco") return null;
            int index = Array.IndexOf(States, state.GetType().Name);
            if (state.GetType().Name == "Disease") index = 5;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Acrid changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Acrid stat changes. Restart to apply. Vanilla: False.");
            regenerativeEnabled = C.BindStartupBool(Root + " - Regenerative", "Changes Enabled", true, "Enable Regenerative changes. Restart to apply. Vanilla: False.");
            poisonEnabled = C.BindStartupBool(Root + " - Poison", "Changes Enabled", true, "Enable Poison changes. Restart to apply. Vanilla: False.");
            blightEnabled = C.BindStartupBool(Root + " - Blight", "Changes Enabled", true, "Enable Blight changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(Acrid), prefix), postfix == null ? null : new HarmonyMethod(typeof(Acrid), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Acrid), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "OnExit", "OnMeleeHitAuthority", "DoImpactAuthority" })
                for (Type t = type; t != null && t != typeof(BaseState) && t != typeof(EntityState); t = t.BaseType)
                {
                    MethodInfo m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (m == null) continue;
                    if (stateHooks.Add(m)) harmony.Patch(m, prefix: new HarmonyMethod(typeof(Acrid), nameof(BeforeState)), finalizer: new HarmonyMethod(typeof(Acrid), nameof(EndScope)));
                    break;
                }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Croco/CrocoBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Acrid");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefabBody,
                    new Dictionary<string, float> { { "baseMoveSpeed", 8f } }); Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody)); }
                if (regenerativeEnabled.Value) LoadRegenerative();
                if (poisonEnabled.Value) BindToxin(DotController.DotIndex.Poison);
                if (blightEnabled.Value) BindToxin(DotController.DotIndex.Blight);
                if (toxins.Count > 0) HookToxins();
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    try
                    {
                        SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == State(States[i]) || i == 5 && d.activationState.stateType == State("Disease"));
                        if (!def) throw new MissingMemberException("Acrid skill unavailable: " + Names[i]);
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, def.activationState.stateType, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p; Hook(p.Skill.StateType);
                        S.SetDescription(p.Skill, "3QS_ACRID_SKILL_" + i, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Acrid skill could not be loaded: " + Names[i] + ": " + e); }
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillUpdate));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage));
                    Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                }
                if (profiles.ContainsKey(5))
                {
                    Patch(typeof(OrbManager), "AddOrb", nameof(BeforeOrb));
                    Patch(typeof(LightningOrb), "Begin", postfix: nameof(AfterDiseaseBegin));
                    Patch(typeof(LightningOrb), "OnArrival", nameof(BeforeDiseaseArrival), finalizer: nameof(AfterDiseaseArrival));
                }
                if (profiles.ContainsKey(0)) Patch(typeof(OverlapAttack), "Fire", nameof(BeforePrimaryOverlap), finalizer: nameof(AfterPrimaryOverlap));
                if (profiles.ContainsKey(3) || profiles.ContainsKey(4))
                    harmony.Patch(AccessTools.Method(State("BaseLeap"), "OnEnter"), prefix: new HarmonyMethod(typeof(Acrid), nameof(BeforeLeapEnter)), transpiler: new HarmonyMethod(typeof(Acrid), nameof(LeapSpeedIL)));
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Acrid changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; object sample = s.Sample; int i = p.Index;
            s.Bind(changedCooldown: i == 4 ? 8f : i == 6 ? 10f : (float?)null, damage: false);
            p.Projectile = Prefab(Read(sample, "projectilePrefab"));
            // Frenzied Leap inherits the acid prefab field, but never uses that projectile.
            if (i == 4) p.Projectile = null;
            float damage = Number(sample, i == 3 || i == 4 ? "blastDamageCoefficient" : i == 6 ? "baseDamage" : "damageCoefficient", 1f) * 100f;
            float proc = Number(sample, i == 3 || i == 4 ? "blastProcCoefficient" : "procCoefficient", 1f);
            if (i != 3 && i != 4 && p.Projectile && p.Projectile.GetComponent<ProjectileController>()) proc = p.Projectile.GetComponent<ProjectileController>().procCoefficient;
            if (i == 5 && p.Projectile)
            {
                var beam = p.Projectile.GetComponentInChildren<ProjectileProximityBeamController>(true);
                if (beam) { damage *= beam.damageCoefficient; proc = beam.procCoefficient; }
            }
            if (i == 6)
            {
                var zone = p.Projectile ? p.Projectile.GetComponentInChildren<ProjectileDotZone>(true) : null;
                var overlap = p.Projectile ? p.Projectile.GetComponentInChildren<ProjectileOverlapAttack>(true) : null;
                if (!zone && !overlap) throw new MissingMemberException("Cloying Breath cloud has no supported damage zone.");
                damage *= zone ? zone.damageCoefficient : overlap.damageCoefficient; proc *= zone ? zone.overlapProcCoefficient : overlap.overlapProcCoefficient;
                float nativeInterval = zone ? 1f / Math.Max(0.001f, zone.resetFrequency) : overlap.resetInterval;
                float nativeDuration = zone ? zone.lifetime : NativeLifetime(p.Projectile, 6.5f);
                s.Interval = S.Float(s.Section, "Damage Interval", 0.25f, nativeInterval, "Seconds between cloud damage ticks.", 0.001f);
                s.Duration = S.Float(s.Section, "AOE Duration", 6.5f, nativeDuration, "Cloud duration in seconds. -1 lasts until removed.", -1f);
                BindArea(p, p.Projectile);
            }
            if (i == 6) S.Float(s.Section, "Proc Coefficient", 0.6f, proc, "Proc coefficient per hit.");
            s.BindDamage(damage, i == 2 ? 400f : i == 6 ? 80f : (float?)null, proc, i == 6);
            if (i == 0)
            {
                thirdHit = S.Float(s.Section, "3rd Hit Damage (%)", 400f, Number(sample, "comboFinisherDamageCoefficient", 4f) * 100f, "Damage of the third slash as a percentage of base damage.");
                primaryHitBoxSize = S.Int(s.Section, "Hit Box Size (%)", 150, 100, "Size of the attack's hitbox. 100 uses its normal size.", 1);
            }
            if (i == 3 || i == 4)
            {
                p.MovementScaling = C.Bind(s.Section, "Movement Speed Scaling (%)", 50f, new ConfigDescription("How strongly movement speed boosts and slows affect leap distance. 0 ignores them; 100 uses normal scaling. Vanilla: 100.", new AcceptableValueRange<float>(0f, 100f)));
                s.Radius = S.Float(s.Section, "AOE Radius", Number(sample, "blastRadius"), Number(sample, "blastRadius"), "Landing explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the landing explosion. Vanilla: None.");
            }
            if (i == 3)
            {
                var zone = p.Projectile ? p.Projectile.GetComponentInChildren<ProjectileDotZone>(true) : null;
                var overlap = p.Projectile ? p.Projectile.GetComponentInChildren<ProjectileOverlapAttack>(true) : null;
                if (!zone && !overlap) throw new MissingMemberException("Caustic Leap acid pool has no supported damage zone.");
                acidDamage = S.Float(s.Section, "Acid Damage (%)", 25f, (zone ? zone.damageCoefficient : overlap.damageCoefficient) * 100f, "Acid damage per hit as a percentage of base damage.");
                float nativeAcidProc = Number(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f) * (zone ? zone.overlapProcCoefficient : overlap.overlapProcCoefficient);
                acidProc = S.Float(s.Section, "Acid Proc Coefficient", nativeAcidProc, nativeAcidProc, "Proc coefficient of each acid pool hit.");
                acidInterval = S.Float(s.Section, "Acid Pool Damage Interval", 0.1f, 1f / Math.Max(0.001f, zone ? zone.fireFrequency : Number(overlap, "fireFrequency", 60f)), "Seconds between acid pool hit checks.", 0.001f);
                acidHits = S.Float(s.Section, "Acid Pool Max Hits Each Second", 2f, zone ? zone.resetFrequency : 1f / Math.Max(0.001f, overlap.resetInterval), "Maximum hits per second on each enemy from an acid pool.", 0.001f);
                float duration = zone ? zone.lifetime : NativeLifetime(p.Projectile, 5f);
                s.Duration = S.Float(s.Section, "AOE Duration", duration, duration, "Acid pool duration in seconds. -1 lasts until removed.", -1f);
                p.NativeRadius = Radius(p.Projectile);
                poolRadius = S.Float(s.Section, "Acid Pool Radius", p.NativeRadius, p.NativeRadius, "Acid pool radius in metres.");
                p.MaxInstances = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum acid pools per player. Removes the oldest when exceeded. -1 is unlimited.", -1);
            }
            if (i == 4) refund = S.Float(s.Section, "Cooldown Reduction Per Target Hit", 2f, Number(sample, "refundPerHit", 2f), "Cooldown seconds restored for each enemy hit.");
            if (i == 5)
            {
                var beam = p.Projectile ? p.Projectile.GetComponentInChildren<ProjectileProximityBeamController>(true) : null;
                float nativeRange = beam ? new LightningOrb().range : Number(sample, "bounceRange", 20f);
                int nativeRepeats = beam ? beam.bounces : (int)Number(sample, "maxBounces", 20f);
                spreadTargets = S.Int(s.Section, "Targets Per Spread", 2, 2, "Enemies each infection can spread to.");
                spreadRange = S.Float(s.Section, "Spread Range", 30f, nativeRange, "Maximum distance between enemies when spreading, in metres.");
                spreadRepeats = S.Int(s.Section, "Spread Repeats", 20, nativeRepeats, "Number of times an infection can spread. -1 is unlimited.", -1);
            }
            if (p.Projectile && p.Projectile.GetComponents<Component>().Any(c => c && Number(c, "desiredForwardSpeed", -1f) > 0f)) s.BindProjectileSpeed(p.Projectile);
        }
        private static ConfigEntry<float> poolRadius;
        private static float NativeLifetime(GameObject prefab, float fallback) => Number(prefab.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c && c.GetType().Name == "DestroyOnTimer"), "duration", fallback);
        private static void BindArea(Profile p, GameObject prefab)
        {
            p.NativeRadius = Radius(prefab);
            p.Skill.Radius = S.Float(p.Skill.Section, "AOE Radius", p.NativeRadius, p.NativeRadius, "Cloud radius in metres.");
            p.Skill.Area = C.Bind(p.Skill.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the cloud. Vanilla: None.");
            p.MaxInstances = S.Int(p.Skill.Section, "Max Instances Of AOE", -1, -1, "Maximum clouds per player. Removes the oldest when exceeded. -1 is unlimited.", -1);
        }
        private static void Apply()
        {
            if (!prefabBody) return;
            stats?.Apply(prefabBody);
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList) if (IsAcrid(b)) { stats?.Apply(b); b.MarkAllStatsDirty(); }
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 1 || p.Index == 2 || p.Index == 3 || p.Index == 5 || p.Index == 6) p.Skill.SetKeyword("KEYWORD_POISON", true);
                if (p.Index == 0 || p.Index == 2) p.Skill.SetKeyword("KEYWORD_REGENERATIVE", true);
                if (p.Index == 2) p.Skill.SetKeyword("KEYWORD_SLAYER", true);
            }
        }
        private static void AfterBody(CharacterBody __instance) { if (IsAcrid(__instance)) stats?.Apply(__instance); }
        private static void Set(Scope scope, object target, string name, object value)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f == null) continue;
                if (f.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
                f.SetValue(f.IsStatic ? null : target, Convert.ChangeType(value, f.FieldType)); return;
            }
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState }; Profile p = ForState(__instance);
            if (p == null || !IsAcrid(Body(__instance))) return;
            activeState = __instance; S.Skill s = p.Skill;
            Set(__state, __instance, p.Index == 3 || p.Index == 4 ? "blastDamageCoefficient" : p.Index == 6 ? "baseDamage" : "damageCoefficient", C.Get(s.Damage) / 100f);
            Set(__state, __instance, p.Index == 3 || p.Index == 4 ? "blastProcCoefficient" : "procCoefficient", C.Get(s.Proc));
            if (p.Index == 0) { Set(__state, __instance, "comboFinisherDamageCoefficient", C.Get(thirdHit) / 100f); if (Number(__instance, "step") == 2) Set(__state, __instance, "damageCoefficient", C.Get(thirdHit) / 100f); }
            if (p.Index == 3 || p.Index == 4) Set(__state, __instance, "blastRadius", C.Get(s.Radius));
            if (p.Index == 4) Set(__state, __instance, "refundPerHit", C.Get(refund));
            if (p.Index == 5) { Set(__state, __instance, "bounceRange", C.Get(spreadRange)); Set(__state, __instance, "maxBounces", C.Get(spreadRepeats) < 0 ? int.MaxValue : C.Get(spreadRepeats)); }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null) { for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value); activeState = __state.Previous; }
            return __exception;
        }
        private static void BeforeLeapEnter(EntityState __instance)
        {
            CharacterBody body = Body(__instance);
            if (!IsAcrid(body) || ForState(__instance)?.MovementScaling == null) return;
            float baseline = body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
            // BaseState captures speed before the leap forces sprinting, so preserve that initial sprint state.
            if (body.isSprinting) baseline *= body.sprintingSpeedMultiplier;
            leapSpeeds.GetOrCreateValue(__instance).Baseline = baseline;
        }
        private static float LeapMovementSpeed(float original, EntityState state)
        {
            Profile p = ForState(state);
            if (p?.MovementScaling == null || !IsAcrid(Body(state)) || !leapSpeeds.TryGetValue(state, out LeapSpeed speed)) return original;
            return Math.Max(0f, speed.Baseline + (original - speed.Baseline) * C.Get(p.MovementScaling) / 100f);
        }
        private static IEnumerable<CodeInstruction> LeapSpeedIL(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field.Name == "moveSpeedStat" ||
                    (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo method && method.Name == "get_moveSpeedStat")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Acrid), nameof(LeapMovementSpeed)));
                    found = true;
                }
            }
            if (!found) ThreeQsVanillaTweaks.Log.Warning("Acrid leap movement scaling: native movement speed could not be found.");
        }
        private sealed class ScaledBoxes { internal Transform[] Boxes; internal Vector3[] Scales; }
        private static void BeforePrimaryOverlap(OverlapAttack __instance, out ScaledBoxes __state)
        {
            __state = null;
            CharacterBody body = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null;
            if (!IsAcrid(body) || !profiles.TryGetValue(0, out Profile p) || !__instance.hitBoxGroup || __instance.damageType.damageSource != DamageSource.Primary) return;
            if (__instance.inflictor && __instance.inflictor.GetComponent<ProjectileController>()) return;
            if (!string.Equals(Read(__instance.hitBoxGroup, "groupName") as string, Read(p.Skill.Sample, "hitBoxGroupName") as string, StringComparison.Ordinal)) return;
            Transform[] boxes = (__instance.hitBoxGroup.hitBoxes ?? Array.Empty<HitBox>()).Where(b => b).Select(b => b.transform).Distinct().ToArray();
            __state = new ScaledBoxes { Boxes = boxes, Scales = boxes.Select(b => b.localScale).ToArray() };
            for (int i = 0; i < boxes.Length; i++) boxes[i].localScale = __state.Scales[i] * (C.Get(primaryHitBoxSize) / 100f);
        }
        private static Exception AfterPrimaryOverlap(ScaledBoxes __state, Exception __exception)
        {
            if (__state != null) for (int i = 0; i < __state.Boxes.Length; i++) if (__state.Boxes[i]) __state.Boxes[i].localScale = __state.Scales[i];
            return __exception;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsAcrid(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            GameObject prefab = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(x => x.Projectile && x.Projectile == prefab); if (p == null) return;
            if (p.Skill.ProjectileSpeed != null) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
            if (p.Index == 3) fireProjectileInfo.damage = fireProjectileInfo.owner.GetComponent<CharacterBody>().damage * C.Get(acidDamage) / 100f;
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => Configure(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance) => Configure(__instance);
        private static void Configure(ProjectileController pc)
        {
            if (!pc || pc.GetComponent<ProjectileMarker>() || !pc.owner || !IsAcrid(pc.owner.GetComponent<CharacterBody>())) return;
            Profile p = profiles.Values.FirstOrDefault(x => x.Projectile && (pc.gameObject.name == x.Projectile.name + "(Clone)" || pc.catalogIndex >= 0 && x.Projectile.GetComponent<ProjectileController>() && pc.catalogIndex == x.Projectile.GetComponent<ProjectileController>().catalogIndex));
            if (p == null) return;
            var marker = pc.gameObject.AddComponent<ProjectileMarker>(); marker.Profile = p; marker.Owner = pc.owner; marker.Origin = pc.transform.position; marker.Born = Time.fixedTime;
            pc.procCoefficient = p.Index == 3 ? C.Get(acidProc) : C.Get(p.Skill.Proc);
            if (p.Index == 5)
            {
                var beam = pc.GetComponentInChildren<ProjectileProximityBeamController>(true);
                if (beam) { beam.procCoefficient = C.Get(p.Skill.Proc); beam.damageCoefficient = 1f; beam.bounces = C.Get(spreadRepeats) < 0 ? int.MaxValue : C.Get(spreadRepeats); beam.attackRange = C.Get(spreadRange); }
            }
            if (p.Index != 3 && p.Index != 6) return;
            var zone = pc.GetComponentInChildren<ProjectileDotZone>(true);
            var overlap = pc.GetComponentInChildren<ProjectileOverlapAttack>(true);
            if (!zone && !overlap) return;
            if (zone)
            {
                zone.damageCoefficient = zone.overlapProcCoefficient = 1f;
                zone.fireFrequency = 1f / (p.Index == 3 ? C.Get(acidInterval) : C.Get(p.Skill.Interval));
                zone.resetFrequency = p.Index == 3 ? C.Get(acidHits) : 1f / C.Get(p.Skill.Interval);
                zone.lifetime = C.Get(p.Skill.Duration);
            }
            if (overlap)
            {
                overlap.damageCoefficient = overlap.overlapProcCoefficient = 1f;
                S.Write(overlap, "fireFrequency", 1f / (p.Index == 3 ? C.Get(acidInterval) : C.Get(p.Skill.Interval)));
                overlap.resetInterval = p.Index == 3 ? 1f / C.Get(acidHits) : C.Get(p.Skill.Interval);
            }
            foreach (Component c in pc.GetComponentsInChildren<Component>(true)) if (c && c.GetType().Name == "DestroyOnTimer") S.Write(c, "duration", C.Get(p.Skill.Duration) < 0f ? float.PositiveInfinity : C.Get(p.Skill.Duration));
            float radius = p.Index == 3 ? C.Get(poolRadius) : C.Get(p.Skill.Radius);
            var group = pc.GetComponentInChildren<HitBoxGroup>(true);
            if (group?.hitBoxes != null) foreach (HitBox box in group.hitBoxes) if (box) box.transform.localScale *= radius / Math.Max(0.001f, p.NativeRadius);
            if (!NetworkServer.active) return;
            areas.RemoveAll(a => !a);
            int maximum = C.Get(p.MaxInstances);
            var previous = areas.Where(a => a.Profile == p && a.Owner == pc.owner).ToList();
            if (maximum >= 0) while (previous.Count >= maximum && previous.Count > 0) { NetworkServer.Destroy(previous[0].gameObject); areas.Remove(previous[0]); previous.RemoveAt(0); }
            if (maximum == 0) { NetworkServer.Destroy(pc.gameObject); return; }
            areas.Add(marker);
        }
        private static float Radius(GameObject prefab)
        {
            HitBox box = prefab.GetComponentInChildren<HitBoxGroup>(true)?.hitBoxes?.FirstOrDefault();
            if (box)
            {
                var sphere = box.GetComponent<SphereCollider>(); if (sphere) return sphere.radius * box.transform.lossyScale.x;
                var capsule = box.GetComponent<CapsuleCollider>(); if (capsule) return capsule.radius * box.transform.lossyScale.x;
                var cube = box.GetComponent<BoxCollider>(); return (cube ? cube.size.x : 1f) * box.transform.lossyScale.x * 0.5f;
            }
            SphereCollider collider = prefab.GetComponentInChildren<SphereCollider>(true);
            return collider ? collider.radius * collider.transform.lossyScale.x : 1f;
        }
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            ProjectileMarker marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<ProjectileMarker>() : null;
            if (!marker || !damageInfo.attacker || damageInfo.attacker == __instance.gameObject || damageInfo.dotIndex != DotController.DotIndex.None) return;
            float distance = Vector3.Distance(marker.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(marker.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(25f, 60f, distance));
            if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(7f, 25f, distance));
            if (marker.Profile.Index == 6 && C.Get(marker.Profile.Skill.Radius) > 0f) damageInfo.damage *= AreaMultiplier(C.Get(marker.Profile.Skill.Area), distance / C.Get(marker.Profile.Skill.Radius));
        }
        private static float AreaMultiplier(S.AreaFalloff area, float distance)
        {
            float t = Mathf.Clamp01(distance);
            return area == S.AreaFalloff.Linear ? 1f - t : area == S.AreaFalloff.HalfLinear ? 1f - 0.5f * t : area == S.AreaFalloff.QuarterLinear ? 1f - 0.25f * t : area == S.AreaFalloff.SweetSpot ? (t <= 0.5f ? 1f : 0.25f) : 1f;
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(activeState); if (p == null || p.Skill.Area == null || !IsAcrid(Body(activeState))) return;
            S.AreaFalloff a = C.Get(p.Skill.Area);
            __instance.falloffModel = a == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : a == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : a == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : a == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        }
        private static void BeforeOrb(Orb orb)
        {
            var disease = orb as LightningOrb;
            if (disease == null || disease.lightningType != LightningOrb.LightningType.CrocoDisease || !disease.attacker || !IsAcrid(disease.attacker.GetComponent<CharacterBody>())) return;
            if (diseases.TryGetValue(disease, out _)) return;
            diseases.Add(disease, new DiseaseData());
            if (activeDisease == null || !diseases.TryGetValue(activeDisease, out _)) { disease.bouncesRemaining = C.Get(spreadRepeats) < 0 ? int.MaxValue : C.Get(spreadRepeats); disease.damageValue = disease.attacker.GetComponent<CharacterBody>().damage * C.Get(profiles[5].Skill.Damage) / 100f; }
            disease.range = C.Get(spreadRange); disease.procCoefficient = C.Get(profiles[5].Skill.Proc);
        }
        private static void AfterDiseaseBegin(LightningOrb __instance) { if (diseases.TryGetValue(__instance, out _)) { __instance.targetsToFindPerBounce = C.Get(spreadTargets); __instance.range = C.Get(spreadRange); } }
        private static void BeforeDiseaseArrival(LightningOrb __instance, out LightningOrb __state) { __state = activeDisease; if (diseases.TryGetValue(__instance, out _)) activeDisease = __instance; }
        private static Exception AfterDiseaseArrival(LightningOrb __state, Exception __exception) { activeDisease = __state; return __exception; }
        private static void BindToxin(DotController.DotIndex index)
        {
            bool poison = index == DotController.DotIndex.Poison;
            string section = Root + (poison ? " - Poison" : " - Blight");
            DotController.DotDef native = DotController.GetDotDef(index);
            var t = new Toxin { Index = index };
            t.Interval = S.Float(section, "Damage Interval", poison ? 0.25f : 0.5f, native.interval, "Seconds between damage ticks.", 0.001f);
            t.HealthDamage = S.Float(section, "Max Health Damage Per Tick (%)", poison ? 0.25f : 0f, poison ? native.interval : 0f, "Target's maximum health dealt per tick. Uses base damage instead if that is higher.");
            t.Damage = S.Float(section, "Damage Per Tick (%)", poison ? 25f : 50f, native.damageCoefficient * 100f, "Base damage dealt per tick. Uses the target's maximum health percentage instead if that is higher.");
            t.Cap = S.Float(section, "Damage Cap (%)", 0f, poison ? native.damageCoefficient * 5000f : 0f, "Maximum damage per tick as a percentage of base damage. 0 is unlimited.");
            t.Armour = S.Bool(section, "Affected By Armour", false, true, "Allow armour to reduce damage.");
            t.Lethal = S.Bool(section, "Can Be Lethal", !poison, !poison, "Allow the damage to kill enemies.");
            t.Duration = S.Float(section, "Duration", poison ? -1f : 10f, poison ? 10f : 5f, "Duration in seconds. -1 is unlimited.", -1f);
            t.Refresh = S.Bool(section, "Re-applying Resets Duration", true, poison, "Refresh the duration when applied again. Blight still adds a stack when disabled.");
            toxins[index] = t;
            string textToken = poison ? "3QS_ACRID_POISON" : "3QS_ACRID_BLIGHT";
            S.SetText(textToken, () => ToxinDescription(t));
            var controller = prefabBody.GetComponent<CrocoDamageTypeController>();
            SkillDef def = controller ? (poison ? controller.poisonSkillDef : controller.blightSkillDef) : null;
            if (def) S.SetText(def.skillDescriptionToken, () => ToxinDescription(t));
            S.SetText(poison ? "KEYWORD_POISON" : "KEYWORD_BLIGHT", () => "<style=cKeywordName>" + (poison ? "Poisonous" : "Blight") + "</style><style=cSub>" + ToxinDescription(t) + "</style>");
        }
        private static IList Stacks(DotController controller) => Read(controller, "dotStackList") as IList;
        private static Toxin ForDot(object stack) => Read(stack, "dotIndex") is DotController.DotIndex index && toxins.TryGetValue(index, out Toxin t) ? t : null;
        private static void Write(object instance, string name, object value)
        {
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo f = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) { f.SetValue(instance, value); return; }
            }
            throw new MissingFieldException(instance.GetType().FullName, name);
        }
        private static void HookToxins()
        {
            MethodInfo add = typeof(DotController).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == "AddDot").OrderByDescending(m => m.GetParameters().Length).FirstOrDefault();
            removeStack = AccessTools.Method(typeof(DotController), "RemoveDotStackAtServer");
            if (add == null || removeStack == null) throw new MissingMethodException("Acrid damage-over-time hooks are unavailable.");
            harmony.Patch(add, prefix: new HarmonyMethod(typeof(Acrid), nameof(BeforeAddDot)), postfix: new HarmonyMethod(typeof(Acrid), nameof(AfterAddDot)));
            harmony.Patch(removeStack, prefix: new HarmonyMethod(typeof(Acrid), nameof(BeforeRemoveStack)));
            Patch(typeof(DotController), "FixedUpdate", nameof(BeforeDotUpdate));
            Patch(typeof(DotController), "EvaluateDotStacksForType", nameof(BeforeEvaluation), nameof(AfterEvaluation), nameof(FinalizeEvaluation));
        }
        private static bool BeforeAddDot(DotController __instance, object[] __args, MethodBase __originalMethod, out Application __state)
        {
            __state = null;
            if (!NetworkServer.active) return true;
            ParameterInfo[] parameters = __originalMethod.GetParameters();
            int attackerIndex = Array.FindIndex(parameters, p => p.Name == "attackerObject"), dotIndex = Array.FindIndex(parameters, p => p.ParameterType == typeof(DotController.DotIndex));
            int durationIndex = Array.FindIndex(parameters, p => p.Name == "duration"), multiplierIndex = Array.FindIndex(parameters, p => p.Name == "damageMultiplier");
            if (attackerIndex < 0 || dotIndex < 0 || durationIndex < 0 || !(__args[attackerIndex] is GameObject attacker) || !attacker || !IsAcrid(attacker.GetComponent<CharacterBody>()) || !toxins.TryGetValue((DotController.DotIndex)__args[dotIndex], out Toxin t)) return true;
            IList list = Stacks(__instance); if (list == null) return true;
            DotData data = dotData.GetOrCreateValue(__instance);
            data.Ticks.RemoveAll(tick => !list.Contains(tick.Stack));
            float duration = C.Get(t.Duration) < 0f ? float.PositiveInfinity : C.Get(t.Duration);
            // Poison remains a single effect. Native poison from other attackers retains its own behaviour.
            if (t.Index == DotController.DotIndex.Poison)
            {
                Tick existing = data.Ticks.FirstOrDefault(x => x.Toxin == t);
                if (existing != null)
                {
                    if (C.Get(t.Refresh))
                    {
                        Write(existing.Stack, "timer", duration); Write(existing.Stack, "totalDuration", duration);
                        existing.BaseDamage = attacker.GetComponent<CharacterBody>().damage;
                        existing.Multiplier = multiplierIndex < 0 ? 1f : Convert.ToSingle(__args[multiplierIndex]);
                    }
                    return false;
                }
                if (list.Cast<object>().Any(x => Read(x, "dotIndex") is DotController.DotIndex d && d == t.Index)) return true;
            }
            else if (C.Get(t.Refresh))
                foreach (Tick tick in data.Ticks.Where(x => x.Toxin == t && x.Attacker == attacker)) { Write(tick.Stack, "timer", duration); Write(tick.Stack, "totalDuration", duration); }
            __state = new Application { Toxin = t, Attacker = attacker, Damage = attacker.GetComponent<CharacterBody>().damage, Multiplier = multiplierIndex < 0 ? 1f : Convert.ToSingle(__args[multiplierIndex]), Before = new HashSet<object>(list.Cast<object>()) };
            __args[durationIndex] = duration;
            return true;
        }
        private static void AfterAddDot(DotController __instance, Application __state)
        {
            if (__state == null) return;
            IList list = Stacks(__instance); if (list == null) return;
            foreach (object stack in list.Cast<object>().Where(x => !__state.Before.Contains(x) && ForDot(x) == __state.Toxin && Read(x, "attackerObject") as GameObject == __state.Attacker))
            {
                // Keep the native stack, buff counts, cleanse behaviour, and network visuals.
                DotData data = dotData.GetOrCreateValue(__instance);
                data.Ticks.RemoveAll(t => ReferenceEquals(t.Stack, stack));
                data.Ticks.Add(new Tick { Stack = stack, Toxin = __state.Toxin, Attacker = __state.Attacker, BaseDamage = __state.Damage, Multiplier = __state.Multiplier, Next = C.Get(__state.Toxin.Interval) });
                var victim = Read(__instance, "victimHealthComponent") as HealthComponent;
                if (victim) Write(stack, "damage", ToxinDamage(__state.Damage, victim.fullCombinedHealth, C.Get(__state.Toxin.Damage), C.Get(__state.Toxin.HealthDamage), C.Get(__state.Toxin.Cap), __state.Multiplier));
            }
        }
        private static void BeforeRemoveStack(DotController __instance, int __0)
        {
            IList list = Stacks(__instance);
            if (list != null && __0 >= 0 && __0 < list.Count && dotData.TryGetValue(__instance, out DotData data))
            { object stack = list[__0]; data.Ticks.RemoveAll(t => ReferenceEquals(t.Stack, stack)); }
        }
        internal static float ToxinDamage(float baseDamage, float maxHealth, float basePercent, float healthPercent, float capPercent, float multiplier)
        {
            float damage = Math.Max(baseDamage * basePercent / 100f, maxHealth * healthPercent / 100f);
            if (capPercent > 0f) damage = Math.Min(damage, baseDamage * capPercent / 100f);
            return damage * multiplier;
        }
        private static void BeforeDotUpdate(DotController __instance)
        {
            if (!NetworkServer.active || !dotData.TryGetValue(__instance, out DotData data)) return;
            IList list = Stacks(__instance);
            var victim = Read(__instance, "victimHealthComponent") as HealthComponent;
            if (list == null || !victim) return;
            foreach (Tick tick in data.Ticks.ToArray())
            {
                if (!list.Contains(tick.Stack)) { data.Ticks.Remove(tick); continue; }
                float remaining = Number(tick.Stack, "timer");
                float elapsed = Math.Min(Time.fixedDeltaTime, Math.Max(0f, remaining));
                tick.Next -= elapsed;
                Write(tick.Stack, "timer", remaining - Time.fixedDeltaTime);
                float interval = C.Get(tick.Toxin.Interval);
                while (tick.Next <= 0.000001f && victim.alive)
                {
                    tick.Next += interval;
                    DamageType type = DamageType.DoT;
                    if (!C.Get(tick.Toxin.Armour)) type |= DamageType.BypassArmor;
                    if (!C.Get(tick.Toxin.Lethal)) type |= DamageType.NonLethal;
                    var damage = new DamageInfo
                    {
                        attacker = tick.Attacker, inflictor = __instance.gameObject,
                        damage = ToxinDamage(tick.BaseDamage, victim.fullCombinedHealth, C.Get(tick.Toxin.Damage), C.Get(tick.Toxin.HealthDamage), C.Get(tick.Toxin.Cap), tick.Multiplier),
                        damageType = type, dotIndex = tick.Toxin.Index, procCoefficient = 0f,
                        position = victim.body ? victim.body.corePosition : victim.transform.position,
                        damageColorIndex = DotController.GetDotDef(tick.Toxin.Index).damageColorIndex
                    };
                    if (Read(tick.Stack, "procChainMask") is ProcChainMask mask) damage.procChainMask = mask;
                    Write(tick.Stack, "damage", damage.damage);
                    if (damage.damage > 0f) victim.TakeDamage(damage);
                    if (!list.Contains(tick.Stack)) break;
                }
                if (Number(tick.Stack, "timer") <= 0f && list.Contains(tick.Stack)) removeStack.Invoke(__instance, new object[] { list.IndexOf(tick.Stack) });
                if (!list.Contains(tick.Stack)) data.Ticks.Remove(tick);
            }
        }
        private static void BeforeEvaluation(DotController __instance, DotController.DotIndex dotIndex, out Evaluation __state)
        {
            __state = null;
            if (!dotData.TryGetValue(__instance, out DotData data)) return;
            IList list = Stacks(__instance); if (list == null) return;
            __state = new Evaluation();
            foreach (Tick tick in data.Ticks) if (tick.Toxin.Index == dotIndex && list.Contains(tick.Stack)) { __state.Hidden.Add(tick.Stack); list.Remove(tick.Stack); }
        }
        private static void RestoreEvaluation(DotController controller, Evaluation evaluation)
        {
            if (evaluation == null) return;
            IList list = Stacks(controller); if (list != null) foreach (object stack in evaluation.Hidden) if (!list.Contains(stack)) list.Add(stack);
            evaluation.Hidden.Clear();
        }
        private static void AfterEvaluation(DotController __instance, Evaluation __state, ref int remainingActive)
        { if (__state != null) remainingActive += __state.Hidden.Count; RestoreEvaluation(__instance, __state); }
        private static Exception FinalizeEvaluation(DotController __instance, Evaluation __state, Exception __exception)
        { RestoreEvaluation(__instance, __state); return __exception; }
        private static void LoadRegenerative()
        {
            string section = Root + " - Regenerative";
            healing = S.Bool(section, "Counts As Healing Instead Of Regen", true, false, "Apply Regenerative as healing instead of regeneration.");
            healingAmount = S.Float(section, "Healing (%)", 5f, 5f, "Maximum health recovered each time Regenerative is triggered.");
            healingTime = S.Float(section, "Healing Duration", 0.5f, 0.5f, "Seconds over which Regenerative restores health.", 0.001f);
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(Acrid), nameof(RegenerationIL)));
            MethodInfo timedBuff = typeof(CharacterBody).GetMethod("AddTimedBuffInternal", BindingFlags.Instance | BindingFlags.NonPublic) ?? AccessTools.Method(typeof(CharacterBody), "AddTimedBuff", new[] { typeof(BuffDef), typeof(float) });
            if (timedBuff == null) throw new MissingMethodException("Acrid Regenerative buff hook is unavailable.");
            harmony.Patch(timedBuff, prefix: new HarmonyMethod(typeof(Acrid), nameof(BeforeRegenerativeBuff)));
            Patch(typeof(HealthComponent), "ServerFixedUpdate", nameof(BeforeHealing));
            S.SetText("KEYWORD_REGENERATIVE", () => "<style=cKeywordName>Regenerative</style><style=cSub>Recover <style=cIsHealing>" + S.Number(C.Get(healingAmount)) + "% of your maximum health</style> over <style=cIsUtility>" + S.Number(C.Get(healingTime)) + " seconds</style>.</style>");
        }
        private static IEnumerable<CodeInstruction> RegenerationIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); bool found = false;
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].operand is FieldInfo field) || field.Name != "CrocoRegen") continue;
                for (int j = i + 1; j < Math.Min(code.Count, i + 16); j++)
                    if (code[j].opcode == OpCodes.Ldc_R4 && code[j].operand is float value && Math.Abs(value - 0.1f) < 0.0001f)
                    {
                        code[j].opcode = OpCodes.Ldarg_0; code[j].operand = null;
                        code.Insert(j + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Acrid), nameof(RegenerationCoefficient))));
                        found = true; break;
                    }
                if (found) break;
            }
            if (!found) throw new InvalidOperationException("Could not locate Acrid's Regenerative stat contribution.");
            return code;
        }
        private static float RegenerationCoefficient(CharacterBody body) => !IsAcrid(body) ? 0.1f : C.Get(healing) ? 0f : C.Get(healingAmount) / 100f / C.Get(healingTime);
        private static void BeforeRegenerativeBuff(CharacterBody __instance, BuffDef __0, ref float duration)
        {
            if (!NetworkServer.active || !IsAcrid(__instance) || __0 != RoR2Content.Buffs.CrocoRegen) return;
            duration = C.Get(healingTime);
            if (C.Get(healing) && __instance.healthComponent)
                recoveries.GetOrCreateValue(__instance.healthComponent).Active.Add(new Recovery { Remaining = duration, Rate = __instance.healthComponent.fullHealth * C.Get(healingAmount) / 100f / duration });
        }
        private static void BeforeHealing(HealthComponent __instance, float deltaTime)
        {
            CharacterBody body = __instance.body;
            if (!NetworkServer.active || !IsAcrid(body) || !recoveries.TryGetValue(__instance, out Recoveries list)) return;
            if (!__instance.alive || !C.Get(healing)) { list.Active.Clear(); return; }
            float amount = 0f;
            foreach (Recovery recovery in list.Active)
            { float elapsed = Math.Min(Math.Max(0f, deltaTime), recovery.Remaining); amount += elapsed * recovery.Rate; recovery.Remaining -= elapsed; }
            list.Active.RemoveAll(r => r.Remaining <= 0f);
            if (amount > 0f) __instance.Heal(amount, default(ProcChainMask), true);
        }
        private sealed class Payment { internal int Cost; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforeSkillUpdate(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || !p.Skill.CooldownAfterDuration || !skillSlot.characterBody) return;
            bool active = skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p);
            if (p.MaxInstances != null) active |= areas.Any(m => m && m.Profile == p && m.Owner == skillSlot.gameObject);
            if (active) deltaTime = 0f;
        }
        private static string Infection => "<style=cIsUtility>Poisonous.</style> ";
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string damage = S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), s.Periodic), text;
            if (p.Index == 0) text = "Maul an enemy for " + damage + ". Every third hit is <style=cIsHealing>Regenerative</style> and deals " + S.Damage(S.Number(C.Get(thirdHit)) + "% damage") + ".";
            else if (p.Index == 1) text = Infection + "Spit toxic bile for " + damage + ".";
            else if (p.Index == 2) text = Infection + "<style=cIsDamage>Slayer.</style> <style=cIsHealing>Regenerative.</style> Bite an enemy for " + damage + ".";
            else if (p.Index == 3) text = Infection + "<style=cIsUtility>Stunning.</style> Leap into the air, dealing " + damage + ". Leave a pool of acid that deals " + S.Damage(S.Number(C.Get(acidDamage)) + "% damage") + " per hit.";
            else if (p.Index == 4) text = "<style=cIsUtility>Stunning.</style> Leap into the air, dealing " + damage + ". Reduce the cooldown by <style=cIsUtility>" + S.Number(C.Get(refund)) + " seconds</style> for each enemy hit.";
            else if (p.Index == 5) text = Infection + "Release a deadly disease that deals " + damage + ". Spreads to <style=cIsUtility>" + C.Get(spreadTargets) + "</style> nearby enemies" + (C.Get(spreadRepeats) < 0 ? "." : ", up to <style=cIsUtility>" + C.Get(spreadRepeats) + "</style> times.");
            else text = Infection + "Exhale a toxic cloud that deals " + damage + (C.Get(s.Duration) < 0f ? "." : " for <style=cIsUtility>" + S.Number(C.Get(s.Duration)) + " seconds</style>.");
            return s.AgileText + text;
        }
        private static string ToxinDescription(Toxin t)
        {
            bool poison = t.Index == DotController.DotIndex.Poison;
            bool perTick = C.Get(S.TickDescriptions);
            float scale = perTick ? 1f : 1f / C.Get(t.Interval);
            string amount = S.Damage(S.Number(C.Get(t.Damage) * scale) + "% base damage");
            if (C.Get(t.HealthDamage) > 0f) amount += " or " + S.Damage(S.Number(C.Get(t.HealthDamage) * scale) + "% of the target's maximum health") + ", whichever is higher";
            string text = "Deal " + amount + (perTick ? " every <style=cIsUtility>" + S.Number(C.Get(t.Interval)) + " seconds</style>" : " per second") + (C.Get(t.Duration) < 0f ? "." : " for <style=cIsUtility>" + S.Number(C.Get(t.Duration)) + " seconds</style>.");
            text += poison ? " Does not stack." : " Stacks increase damage.";
            if (!C.Get(t.Armour)) text += " <style=cIsDamage>Ignores armour.</style>";
            if (!C.Get(t.Lethal)) text += " <style=cIsUtility>Cannot kill.</style>";
            if (C.Get(t.Cap) > 0f) text += " Capped at " + S.Damage(S.Number(C.Get(t.Cap)) + "% base damage") + " per tick.";
            return text;
        }
        private static string Extra(Profile p)
        {
            var text = new StringBuilder(); Action<string, string> add = (key, value) => text.Append("\n").Append(key).Append(": <style=cIsUtility>").Append(value).Append("</style>");
            if (p.Index == 0) add("Hit Box Size", C.Get(primaryHitBoxSize) + "%");
            if (p.MovementScaling != null) add("Movement Speed Scaling", S.Number(C.Get(p.MovementScaling)) + "%");
            if (p.Skill.Radius != null) add("Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Index == 3) { add("Acid Pool Radius", S.Number(C.Get(poolRadius)) + "m"); add("Acid Hit Check Interval", S.Number(C.Get(acidInterval)) + "s"); add("Acid Max Hits Each Second", S.Number(C.Get(acidHits))); }
            if (p.Index == 5) add("Spread Range", S.Number(C.Get(spreadRange)) + "m");
            return text.ToString();
        }
    }
}
