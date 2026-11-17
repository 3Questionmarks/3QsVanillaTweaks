using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
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
    internal static class Loader
    {
        private const string Root = "Survivors - 09 Loader", StatsRoot = "Survivor-Stats - 09 Loader";
        private static readonly string[] Names = { "Knuckleboom", "Grapple Fist", "Spiked Fist", "Charged Gauntlet", "Thunder Gauntlet", "M551 Pylon", "Thunderslam" };
        private static readonly string[] Activations = { "SwingComboFist", "FireHook", "FireYankHook", "ChargeFist", "ChargeZapFist", "ThrowPylon", "PreGroundSlam" };
        private static readonly string[] Attacks = { "SwingComboFist", "FireHook", "FireYankHook", "SwingChargedFist", "SwingZapFist", "ThrowPylon", "GroundSlam" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal ConfigEntry<float> Range, ChargeTime, MinimumSpeed, ChargeSpeed, VelocityDamage;
            internal ConfigEntry<bool> SimpleDescription;
            internal float NativeConeRadius;
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<string> patched = new HashSet<string>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled;
        private static ConfigEntry<int> barrier, pylonTargets, basePylons, maxPylons;
        private static ConfigEntry<float> coneDamage, coneProc, inactiveTime;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static GameObject conePrefab;
        [ThreadStatic] private static EntityState activeState;
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class Marker : MonoBehaviour
        {
            internal Profile Profile;
            internal Vector3 Origin;
            internal bool Cone;
            internal float Born;
            internal ProjectileProximityBeamController Beam;
            // Live markers, so skill updates need not search the whole scene each tick.
            internal static readonly HashSet<Marker> Active = new HashSet<Marker>();
            private void OnEnable() => Active.Add(this);
            private void OnDisable() => Active.Remove(this);
            private void FixedUpdate()
            {
                if (Profile?.Index != 5 || !NetworkServer.active) return;
                if (Beam) Beam.enabled = Time.fixedTime - Born >= C.Get(inactiveTime);
                if (Time.fixedTime - Born >= C.Get(Profile.Skill.Duration)) NetworkServer.Destroy(gameObject);
            }
        }
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Loader." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsLoader(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static object Sample(string name) => EntityStateCatalog.InstantiateState(State(name));
        private static Profile ForState(EntityState state)
        {
            if (state == null || state.GetType().Namespace != "EntityStates.Loader") return null;
            string name = state.GetType().Name;
            int index = Array.IndexOf(Attacks, name);
            if (index < 0) index = Array.IndexOf(Activations, name);
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        // Optional asset fields vary between game versions; probing them should not log warnings.
        private static object Read(object instance, string name)
        {
            if (instance == null) return null;
            for (Type type = instance.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags); if (field != null) return field.GetValue(instance);
                PropertyInfo property = type.GetProperty(name, flags); if (property != null) return property.GetValue(instance);
            }
            return null;
        }
        private static float Number(object instance, string name, float fallback = 0f) => Read(instance, name) is IConvertible value ? Convert.ToSingle(value) : fallback;
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Loader changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Loader stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Scrap Barrier", "Changes Enabled", true, "Enable Scrap Barrier changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (!patched.Add(target.MethodHandle.Value + ":" + prefix + ":" + postfix + ":" + finalizer)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Loader), prefix), postfix == null ? null : new HarmonyMethod(typeof(Loader), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Loader), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "OnExit", "OnMeleeHitAuthority" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType == typeof(EntityState) || method.DeclaringType == typeof(BaseState)) continue;
                Patch(type, name, nameof(BeforeState), finalizer: nameof(EndScope));
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Loader/LoaderBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Loader");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefabBody);
                if (passiveEnabled.Value)
                {
                    float native = Number(Sample("SwingComboFist"), "barrierPercentagePerHit", 0.05f) * 100f;
                    barrier = S.Int(Root + " - Scrap Barrier", "Barrier Gain (%)", 5, (int)Math.Round(native), "Maximum health gained as temporary barrier when a gauntlet attack hits.");
                    Patch(State("LoaderMeleeAttack"), "OnMeleeHitAuthority", nameof(BeforeBarrier), finalizer: nameof(EndScope));
                    S.SetText(prefabBody.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken, () => "Loader is <style=cIsUtility>immune to fall damage</style>. Striking enemies with Loader's gauntlets grants <style=cIsHealing>" + C.Get(barrier) + "% temporary barrier</style>.");
                }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    try
                    {
                        Type type = State(Attacks[i]);
                        SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == State(Activations[i]));
                        if (!def || type == null) throw new MissingMemberException("Loader skill unavailable: " + Names[i]);
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                        Hook(type); if (Activations[i] != Attacks[i]) Hook(State(Activations[i]));
                        S.SetDescription(p.Skill, "3QS_LOADER_SKILL_" + i, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? (Func<float>)null : () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Loader skill could not be loaded: " + Names[i] + ": " + error); }
                }
                if (stats != null) Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody));
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillUpdate));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                }
                if (profiles.ContainsKey(5))
                {
                    Patch(typeof(ProjectileProximityBeamController), "UpdateServer", nameof(BeforePylonUpdate));
                    Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", postfix: nameof(AfterPylonLimit));
                }
                if (profiles.ContainsKey(6)) Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Loader changes could not be loaded: " + error); }
        }
        private static void Bind(Profile p)
        {
            int i = p.Index; S.Skill s = p.Skill; object sample = s.Sample;
            s.Bind(changedStocks: i == 1 ? 2 : (int?)null, damage: false);
            float nativeDamage = Number(sample, "damageCoefficient") * 100f, nativeProc = Number(sample, "procCoefficient", 1f);
            p.Projectile = Read(sample, "projectilePrefab") as GameObject;
            if (i == 1 || i == 2)
            {
                if (!p.Projectile) throw new MissingMemberException("Loader hook projectile is unavailable.");
                var grapple = p.Projectile.GetComponent<ProjectileGrappleController>();
                if (!grapple) throw new MissingMemberException("Loader hook controller is unavailable.");
                p.Range = S.Float(s.Section, "Range", 80f, grapple.maxTravelDistance, "Maximum hook travel distance in metres.");
                nativeProc = p.Projectile.GetComponent<ProjectileController>().procCoefficient;
            }
            if (i == 3 || i == 4)
            {
                object charge = Sample(Activations[i]);
                p.ChargeTime = S.Float(s.Section, "Max Charge Time", i == 4 ? 0.4f : 2.5f, Number(charge, "baseChargeDuration", 1f), "Seconds needed to fully charge, before attack speed is applied. 0 is instant.", 0f);
                p.VelocityDamage = S.Float(s.Section, "Extra Damage Per 1m/s Velocity (%)", 30f, Number(sample, "velocityDamageCoefficient", 0.3f) * 100f, "Extra damage per metre per second of punch velocity, as a percentage of base damage.");
                p.SimpleDescription = C.BindBool(s.Section, "Simplify Description", true, "Show the normal punch damage instead of the full velocity damage formula. Vanilla: True.", true);
                if (i == 3)
                {
                    float min = Number(sample, "minLungeSpeed", 20f), max = Number(sample, "maxLungeSpeed", 90f);
                    p.MinimumSpeed = S.Float(s.Section, "Minimum Launch Speed (m/s)", min, min, "Velocity gained from an uncharged punch, in metres per second.");
                    p.ChargeSpeed = S.Float(s.Section, "Velocity Gained From Charging (m/s)", Math.Max(0f, max - min), Math.Max(0f, max - min), "Extra launch velocity gained at full charge, in metres per second.");
                }
                else
                {
                    p.MinimumSpeed = S.Float(s.Section, "Velocity (m/s)", 70f, Number(sample, "maxLungeSpeed", 70f), "Launch velocity added by Thunder Gauntlet, in metres per second.");
                    conePrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Loader/LoaderZapCone.prefab").WaitForCompletion();
                    if (!conePrefab) throw new MissingMemberException("Thunder Gauntlet lightning cone prefab is unavailable.");
                    var beam = conePrefab.GetComponentInChildren<ProjectileProximityBeamController>(true);
                    var overlap = conePrefab.GetComponentInChildren<ProjectileOverlapAttack>(true);
                    if (!beam && !overlap) throw new MissingMemberException("Thunder Gauntlet lightning cone has no supported attack controller.");
                    float nativeConeDamage = beam ? beam.damageCoefficient : overlap.damageCoefficient;
                    float nativeConeProc = beam ? beam.procCoefficient : Number(conePrefab.GetComponent<ProjectileController>(), "procCoefficient", 1f) * overlap.overlapProcCoefficient;
                    coneDamage = S.Float(s.Section, "Lightning Cone Damage (%)", 900f, nativeConeDamage * 100f, "Lightning cone damage as a percentage of base damage.");
                    coneProc = S.Float(s.Section, "Lightning Cone Proc Coefficient", nativeConeProc, nativeConeProc, "Proc coefficient of the lightning cone.");
                    p.NativeConeRadius = beam ? beam.attackRange : Radius(conePrefab);
                    s.Radius = S.Float(s.Section, "AOE Radius", p.NativeConeRadius, p.NativeConeRadius, "Lightning cone hitbox radius in metres.");
                }
            }
            if (i == 5)
            {
                if (!p.Projectile) throw new MissingMemberException("Loader pylon projectile is unavailable.");
                var beam = p.Projectile.GetComponentInChildren<ProjectileProximityBeamController>(true);
                if (!beam) throw new MissingMemberException("Loader pylon beam controller is unavailable.");
                nativeDamage *= beam.damageCoefficient; nativeProc = beam.procCoefficient;
                inactiveTime = S.Float(s.Section, "Inactive Time", 1.65f, 1.65f, "Seconds before a newly deployed pylon starts zapping.");
                float lifetime = Number(p.Projectile.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "DestroyOnTimer"), "duration", Number(p.Projectile.GetComponent<ProjectileSimple>(), "lifetime", 15f));
                s.Duration = S.Float(s.Section, "Lifetime", 16.65f, lifetime, "Total pylon lifetime in seconds, including its inactive time.", 0.001f);
                s.Interval = S.Float(s.Section, "Zap Interval", 0.7f, beam.attackInterval, "Seconds between zaps after the first zap.", 0.001f);
                s.Radius = S.Float(s.Section, "Zap Range", 25f, beam.attackRange, "Maximum zap range in metres.");
                pylonTargets = S.Int(s.Section, "Zap Targets", 10, beam.attackFireCount, "Maximum targets per zap. 0 hits every target in range.");
                basePylons = S.Int(s.Section, "Default Maximum", 3, 3, "Pylons that can be active at base. Each extra special stock adds one pylon.");
                maxPylons = S.Int(s.Section, "Absolute Maximum", 3, 3, "Absolute maximum active pylons per player.");
            }
            if (i == 6)
            {
                nativeDamage = Number(sample, "blastDamageCoefficient") * 100f; nativeProc = Number(sample, "blastProcCoefficient", 1f);
                s.Radius = S.Float(s.Section, "AOE Radius", Number(sample, "blastRadius"), Number(sample, "blastRadius"), "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
            }
            if (i != 1 || nativeDamage > 0f) s.BindDamage(nativeDamage, null, nativeProc, i == 5);
            if (p.Projectile && p.Projectile.GetComponents<Component>().Any(c => c && Number(c, "desiredForwardSpeed", -1f) > 0f)) s.BindProjectileSpeed(p.Projectile);
        }
        private static void Apply()
        {
            if (!prefabBody) return;
            stats?.Apply(prefabBody);
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (IsLoader(body)) { stats?.Apply(body); body.MarkAllStatsDirty(); }
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 3 || p.Index == 4) p.Skill.SetKeyword("KEYWORD_HEAVY", true);
            }
        }
        private static void AfterBody(CharacterBody __instance) { if (IsLoader(__instance)) stats?.Apply(__instance); }
        private static void Set(Scope scope, object target, string name, object value)
        {
            FieldInfo field = null;
            for (Type type = target.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null) return;
            if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
            field.SetValue(field.IsStatic ? null : target, Convert.ChangeType(value, field.FieldType));
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            Profile p = ForState(__instance); if (p == null || !IsLoader(Body(__instance))) return;
            activeState = __instance; S.Skill skill = p.Skill;
            if (skill.Damage != null) Set(__state, __instance, p.Index == 6 ? "blastDamageCoefficient" : "damageCoefficient", C.Get(skill.Damage) / 100f);
            if (skill.Proc != null) Set(__state, __instance, p.Index == 6 ? "blastProcCoefficient" : "procCoefficient", C.Get(skill.Proc));
            if (p.Index == 3 || p.Index == 4)
            {
                Set(__state, __instance, "baseChargeDuration", Math.Max(0.0001f, C.Get(p.ChargeTime)));
                Set(__state, __instance, "minLungeSpeed", C.Get(p.MinimumSpeed));
                Set(__state, __instance, "maxLungeSpeed", C.Get(p.MinimumSpeed) + (p.ChargeSpeed == null ? 0f : C.Get(p.ChargeSpeed)));
                Set(__state, __instance, "velocityDamageCoefficient", C.Get(p.VelocityDamage) / 100f);
            }
            if (p.Index == 6) Set(__state, __instance, "blastRadius", C.Get(skill.Radius));
        }
        private static void BeforeBarrier(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            if (IsLoader(Body(__instance))) Set(__state, __instance, "barrierPercentagePerHit", C.Get(barrier) / 100f);
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null) { for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value); activeState = __state.Previous; }
            return __exception;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsLoader(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            if (profiles.ContainsKey(4) && conePrefab && fireProjectileInfo.projectilePrefab == conePrefab)
            { fireProjectileInfo.damage = fireProjectileInfo.owner.GetComponent<CharacterBody>().damage * C.Get(coneDamage) / 100f; return; }
            GameObject projectile = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(profile => profile.Projectile && profile.Projectile == projectile);
            if (p?.Skill.ProjectileSpeed != null) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => Configure(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance) => Configure(__instance);
        private static bool Matches(ProjectileController pc, GameObject prefab) => prefab && (pc.gameObject.name == prefab.name + "(Clone)" || pc.catalogIndex >= 0 && prefab.GetComponent<ProjectileController>() && pc.catalogIndex == prefab.GetComponent<ProjectileController>().catalogIndex);
        private static void Configure(ProjectileController pc)
        {
            if (!pc || pc.GetComponent<Marker>() || !pc.owner || !IsLoader(pc.owner.GetComponent<CharacterBody>())) return;
            bool cone = profiles.ContainsKey(4) && conePrefab && Matches(pc, conePrefab);
            Profile p = cone ? profiles[4] : profiles.Values.FirstOrDefault(profile => profile.Projectile && Matches(pc, profile.Projectile));
            if (p == null) return;
            var marker = pc.gameObject.AddComponent<Marker>(); marker.Profile = p; marker.Cone = cone; marker.Born = Time.fixedTime; marker.Origin = pc.transform.position;
            if (p.Skill.Proc != null) pc.procCoefficient = C.Get(p.Skill.Proc);
            if (cone)
            {
                pc.procCoefficient = C.Get(coneProc);
                var beam = pc.GetComponentInChildren<ProjectileProximityBeamController>(true);
                if (beam) { beam.damageCoefficient = 1f; beam.procCoefficient = C.Get(coneProc); beam.attackRange = C.Get(p.Skill.Radius); }
                var overlap = pc.GetComponentInChildren<ProjectileOverlapAttack>(true); if (overlap) { overlap.damageCoefficient = overlap.overlapProcCoefficient = 1f; }
                if (!beam)
                {
                    var group = pc.GetComponent<HitBoxGroup>(); if (group) foreach (HitBox box in group.hitBoxes) if (box) box.transform.localScale *= C.Get(p.Skill.Radius) / Math.Max(0.001f, p.NativeConeRadius);
                }
            }
            if (p.Index == 1 || p.Index == 2)
            {
                var grapple = pc.GetComponent<ProjectileGrappleController>(); if (grapple) grapple.maxTravelDistance = C.Get(p.Range);
            }
            if (p.Index == 5)
            {
                marker.Beam = pc.GetComponentInChildren<ProjectileProximityBeamController>(true);
                if (marker.Beam) { marker.Beam.damageCoefficient = 1f; marker.Beam.procCoefficient = C.Get(p.Skill.Proc); marker.Beam.attackRange = C.Get(p.Skill.Radius); marker.Beam.attackInterval = C.Get(p.Skill.Interval); marker.Beam.listClearInterval = C.Get(p.Skill.Interval); marker.Beam.enabled = NetworkServer.active && C.Get(inactiveTime) <= 0f; }
                var mover = pc.GetComponent<ProjectileSimple>(); if (mover) mover.lifetime = C.Get(p.Skill.Duration);
                foreach (Component component in pc.GetComponentsInChildren<Component>(true))
                    if (component && component.GetType().Name == "DestroyOnTimer") S.Write(component, "duration", C.Get(p.Skill.Duration));
            }
        }
        private static bool BeforePylonUpdate(ProjectileProximityBeamController __instance)
        {
            Marker marker = __instance.GetComponentInParent<Marker>();
            if (!marker || marker.Profile.Index != 5) return true;
            if (Time.fixedTime - marker.Born < C.Get(inactiveTime)) return false;
            __instance.attackInterval = __instance.listClearInterval = C.Get(marker.Profile.Skill.Interval);
            __instance.attackRange = C.Get(marker.Profile.Skill.Radius); __instance.procCoefficient = C.Get(marker.Profile.Skill.Proc);
            int count = C.Get(pylonTargets);
            if (count == 0)
            {
                var pc = marker.GetComponent<ProjectileController>();
                var search = new BullseyeSearch { searchOrigin = marker.transform.position, maxDistanceFilter = __instance.attackRange, maxAngleFilter = 180f, teamMaskFilter = TeamMask.GetEnemyTeams(TeamComponent.GetObjectTeam(pc.owner)), filterByLoS = false };
                search.RefreshCandidates();
                count = search.GetResults().Where(h => h && h.healthComponent && h.healthComponent.alive).Select(h => h.healthComponent).Distinct().Count();
            }
            __instance.attackFireCount = count;
            return true;
        }
        private static void AfterPylonLimit(CharacterMaster __instance, DeployableSlot slot, ref int __result)
        {
            if (slot != DeployableSlot.LoaderPylon) return;
            CharacterBody body = __instance.GetBody();
            if (!IsLoader(body) || !body.skillLocator || !body.skillLocator.special || !definitions.TryGetValue(body.skillLocator.special.skillDef, out Profile p) || p.Index != 5) return;
            __result = Math.Min(C.Get(maxPylons), C.Get(basePylons) + Math.Max(0, body.skillLocator.special.maxStock - 1));
        }
        private static void BeforeBlast(BlastAttack __instance)
        { if (ForState(activeState)?.Index == 6 && IsLoader(Body(activeState))) __instance.falloffModel = Area(C.Get(profiles[6].Skill.Area)); }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            Marker marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<Marker>() : null;
            if (!marker || marker.Profile.Skill.Falloff == null || !damageInfo.attacker || damageInfo.attacker == __instance.gameObject) return;
            float distance = Vector3.Distance(marker.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(marker.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(25f, 60f, distance));
            if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(7f, 25f, distance));
        }
        private static float Radius(GameObject prefab)
        {
            HitBox box = prefab.GetComponent<HitBoxGroup>()?.hitBoxes.FirstOrDefault(); if (!box) return 1f;
            var sphere = box.GetComponent<SphereCollider>(); if (sphere) return sphere.radius * box.transform.lossyScale.x;
            var capsule = box.GetComponent<CapsuleCollider>(); if (capsule) return capsule.radius * box.transform.lossyScale.x;
            var cube = box.GetComponent<BoxCollider>(); return (cube ? cube.size.x : 1f) * box.transform.lossyScale.x * 0.5f;
        }
        private sealed class Payment { internal int Cost; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static readonly List<EntityStateMachine> machines = new List<EntityStateMachine>();
        private static void BeforeSkillUpdate(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || !p.Skill.CooldownAfterDuration || !skillSlot.characterBody) return;
            skillSlot.characterBody.GetComponents(machines);
            bool active = false;
            foreach (EntityStateMachine machine in machines) if (machine && ForState(machine.state) == p) { active = true; break; }
            machines.Clear();
            if (p.Index == 5 && !active)
                foreach (Marker m in Marker.Active)
                {
                    if (!m || m.Profile != p) continue;
                    ProjectileController controller = m.GetComponent<ProjectileController>();
                    if (controller && controller.owner == skillSlot.gameObject) { active = true; break; }
                }
            if (active) deltaTime = 0f;
        }
        internal static float PunchDamage(float baseDamage, float velocityDamage, float speed) => baseDamage + velocityDamage * speed;
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill;
            string damage = s.Damage == null ? "" : S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), s.Periodic);
            string text;
            if (p.Index == 0) text = "Swing at nearby enemies for " + damage + ".";
            else if (p.Index == 1 || p.Index == 2) text = "Fire your gauntlet forward" + (p.Index == 2 ? " for " + damage + ", pulling small enemies to you" : ", pulling you to the target") + ".";
            else if (p.Index == 3 || p.Index == 4)
            {
                if (C.Get(p.SimpleDescription))
                {
                    float min = PunchDamage(C.Get(s.Damage), C.Get(p.VelocityDamage), C.Get(p.MinimumSpeed));
                    float max = PunchDamage(C.Get(s.Damage), C.Get(p.VelocityDamage), C.Get(p.MinimumSpeed) + (p.ChargeSpeed == null ? 0f : C.Get(p.ChargeSpeed)));
                    text = (p.Index == 3 ? "Charge a piercing punch" : "Charge a single-target punch") + " for " + S.Damage(S.Number(min) + (max == min ? "" : "–" + S.Number(max)) + "% damage") + ".";
                }
                else text = "Charge a punch for " + damage + ", plus " + S.Damage(S.Number(C.Get(p.VelocityDamage)) + "% damage") + " per <style=cIsUtility>1m/s of velocity</style>.";
                if (p.Index == 4) text += " On hit, release lightning for " + S.Damage(S.Number(C.Get(coneDamage)) + "% damage") + ".";
            }
            else if (p.Index == 5) text = "Throw a floating pylon that zaps nearby enemies for " + damage + ". Lasts <style=cIsUtility>" + S.Number(C.Get(s.Duration)) + " seconds</style>. Can place up to <style=cIsUtility>" + Math.Min(C.Get(basePylons), C.Get(maxPylons)) + "</style>.";
            else text = "Slam your fists into the ground, dealing " + damage + " and <style=cIsUtility>stunning</style> nearby enemies.";
            return s.AgileText + (p.Index == 3 || p.Index == 4 ? "<style=cIsUtility>Heavy.</style> " : "") + text;
        }
        private static string Extra(Profile p)
        {
            var text = new StringBuilder(); Action<string, string> add = (key, value) => text.Append("\n").Append(key).Append(": <style=cIsUtility>").Append(value).Append("</style>");
            if (p.Range != null) add("Range", S.Number(C.Get(p.Range)) + "m");
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.ChargeTime != null) { add("Max Charge Time", S.Number(C.Get(p.ChargeTime)) + "s"); add("Launch Velocity", S.Number(C.Get(p.MinimumSpeed)) + (p.ChargeSpeed == null ? "" : "–" + S.Number(C.Get(p.MinimumSpeed) + C.Get(p.ChargeSpeed))) + "m/s"); }
            if (p.Skill.Radius != null) add(p.Index == 5 ? "Zap Range" : "Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Index == 5) { add("Inactive Time", S.Number(C.Get(inactiveTime)) + "s"); add("Zap Targets", C.Get(pylonTargets) == 0 ? "All" : C.Get(pylonTargets).ToString()); add("Absolute Maximum", C.Get(maxPylons).ToString()); }
            return text.ToString();
        }
    }
}
