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
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;
namespace Tweaks.Survivors
{
    internal static class Drifter
    {
        private const string Root = "Survivors - 18 Drifter", StatsRoot = "Survivor-Stats - 18 Drifter";
        private static readonly string[] Names = { "Blunt Force", "Cleanup", "Junk Cube", "Repossess", "Bludgeon", "Discard", "Tornado Slam", "Salvage", "Tinker" };
        private static readonly string[] States = { "BluntForceEntry", "AimMagicBag", "CubeSlam", "AimRepossess", "SuffocateSlam", "EmptyBag", "ChargeTornadoSlam", "Salvage", "CastTinker" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal string NativeText;
            internal float NativeDamage, NativeThird;
            internal int NativeCount;
            internal GameObject Projectile;
            internal readonly Dictionary<string, ConfigEntry<float>> Values = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> Ints = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> Bools = new Dictionary<string, ConfigEntry<bool>>();
            internal ConfigEntry<string> JunkUsed;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<Tuple<FieldInfo, object>> Saved = new List<Tuple<FieldInfo, object>>();
        }
        private sealed class Execution { internal Profile Previous; internal int PreviousCost; }
        private sealed class SalvageRun { internal int Items; }
        private sealed class Entered { internal bool Value; }
        private static readonly ConditionalWeakTable<EntityState, Entered> entered = new ConditionalWeakTable<EntityState, Entered>();
        private sealed class Shot { internal Profile Profile; internal bool Applied; }
        private sealed class StoredBag
        {
            internal GameObject Object;
            internal int OriginScene;
            internal bool Placed;
            internal float NextRequest;
        }
        private sealed class TransitObject : MonoBehaviour
        {
            internal bool InTransit;
            private readonly List<KeyValuePair<Collider, bool>> colliders = new List<KeyValuePair<Collider, bool>>();
            private readonly List<KeyValuePair<Renderer, bool>> renderers = new List<KeyValuePair<Renderer, bool>>();
            private readonly List<KeyValuePair<Rigidbody, bool>> rigidbodies = new List<KeyValuePair<Rigidbody, bool>>();
            internal void Freeze()
            {
                InTransit = true;
                foreach (Collider value in GetComponentsInChildren<Collider>(true)) { colliders.Add(new KeyValuePair<Collider, bool>(value, value.enabled)); value.enabled = false; }
                foreach (Renderer value in GetComponentsInChildren<Renderer>(true)) { renderers.Add(new KeyValuePair<Renderer, bool>(value, value.enabled)); value.enabled = false; }
                foreach (Rigidbody value in GetComponentsInChildren<Rigidbody>(true)) { rigidbodies.Add(new KeyValuePair<Rigidbody, bool>(value, value.isKinematic)); value.isKinematic = true; }
            }
            internal void Thaw()
            {
                foreach (var pair in colliders) if (pair.Key) pair.Key.enabled = pair.Value;
                foreach (var pair in renderers) if (pair.Key) pair.Key.enabled = pair.Value;
                foreach (var pair in rigidbodies) if (pair.Key) pair.Key.isKinematic = pair.Value;
                colliders.Clear(); renderers.Clear(); rigidbodies.Clear(); InTransit = false;
            }
        }
        private sealed class RestoreBag : MonoBehaviour
        {
            private int ticks;
            private void FixedUpdate()
            {
                if (++ticks < 3) return;
                CharacterBody body = GetComponent<CharacterBody>();
                if (Restore(body)) { UnityEngine.Object.Destroy(this); return; }
                if (ticks >= 500)
                {
                    ReleaseStored(body);
                    UnityEngine.Object.Destroy(this);
                }
            }
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly HashSet<MethodBase> hooks = new HashSet<MethodBase>();
        private static readonly ConfigEntry<bool>[] switches = new ConfigEntry<bool>[9];
        private static readonly ConditionalWeakTable<EntityState, SalvageRun> salvageStates = new ConditionalWeakTable<EntityState, SalvageRun>();
        private static readonly ConditionalWeakTable<CharacterBody, SalvageRun> salvagePlans = new ConditionalWeakTable<CharacterBody, SalvageRun>();
        private static readonly ConditionalWeakTable<ProjectileController, Shot> shots = new ConditionalWeakTable<ProjectileController, Shot>();
        private static readonly Dictionary<CharacterMaster, StoredBag> carried = new Dictionary<CharacterMaster, StoredBag>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled;
        private static ConfigEntry<float> white, green, red, yellow;
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState active;
        [ThreadStatic] private static Profile executing;
        [ThreadStatic] private static int junkCost;
        [ThreadStatic] private static object bagAction;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.Drifter." + name);
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
        private static bool IsDrifter(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static float F(Profile p, string key) => C.Get(p.Values[key]);
        private static void Float(Profile p, string key, float value, float vanilla, string text, float minimum = 0f) => p.Values[key] = S.Float(p.Skill.Section, key, value, vanilla, text, minimum);
        private static int I(Profile p, string key) => C.Get(p.Ints[key]);
        private static bool B(Profile p, string key) => C.Get(p.Bools[key]);
        private static void Int(Profile p, string key, int value, int vanilla, string text, int minimum = 0) => p.Ints[key] = S.Int(p.Skill.Section, key, value, vanilla, text, minimum);
        private static void Bool(Profile p, string key, bool value, bool vanilla, string text) => p.Bools[key] = S.Bool(p.Skill.Section, key, value, vanilla, text);
        private static Profile Definition(SkillDef def) => def && definitions.TryGetValue(def, out Profile p) ? p : null;
        private static object Sample(string state) => State(state) == null ? null : EntityStateCatalog.InstantiateState(State(state));
        private static Component Controller(CharacterBody body, string name) => body && GameType(name) != null ? body.GetComponent(GameType(name)) : null;
        private static Profile ForState(EntityState state)
        {
            if (state == null) return null;
            string ns = state.GetType().Namespace ?? "", name = state.GetType().Name;
            if (ns == "EntityStates.JunkCube")
            {
                Component cube = state.outer ? state.outer.GetComponent(GameType("JunkCubeController")) : null;
                ProjectileController projectile = cube ? cube.GetComponent<ProjectileController>() : null;
                GameObject owner = projectile ? projectile.owner : null;
                return owner && IsDrifter(owner.GetComponent<CharacterBody>()) && profiles.TryGetValue(2, out Profile cubeProfile) ? cubeProfile : null;
            }
            if (!IsDrifter(Body(state))) return null;
            int i = name == "Cleanup" ? 1 : name == "JunkCube" ? 2 : name == "BaggedObject" || name == "Repossess" || name == "RepossessExit" ? 3 :
                name == "BluntForceTornado" || name == "TornadoSlam" ? 6 :
                name.StartsWith("BluntForce", StringComparison.Ordinal) ? 0 : Array.IndexOf(States, name);
            return profiles.TryGetValue(i, out Profile p) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Drifter changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Drifter stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Trash To Treasure", "Changes Enabled", true, "Enable Trash to Treasure changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Load()
        {
            try
            {
                prefab = BodyCatalog.FindBodyPrefab("DrifterBody")?.GetComponent<CharacterBody>();
                if (!prefab) throw new InvalidOperationException("Drifter body was not found.");
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Drifter");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefab);
                var defs = prefab.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToList();
                object bag = Sample("Bag.BaggedObject");
                foreach (string key in new[] { "primaryOverride", "utilityOverride" }) if (Read(bag, key) is SkillDef def && def && !defs.Contains(def)) defs.Add(def);
                foreach (SkillDef def in SkillCatalog.allSkillDefs)
                    if (def && def.activationState.stateType != null && (def.activationState.stateType == State("SuffocateSlam") || def.activationState.stateType == State("EmptyBag")) && !defs.Contains(def)) defs.Add(def);
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    try
                    {
                        SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == State(States[i]));
                        if (!def) def = SkillCatalog.allSkillDefs.FirstOrDefault(d => d && Regex.Replace(Language.GetString(d.skillNameToken), "[^A-Za-z0-9]", "").Equals(Regex.Replace(Names[i], "[^A-Za-z0-9]", ""), StringComparison.OrdinalIgnoreCase));
                        if (!def) { ThreeQsVanillaTweaks.Log.Warning("Drifter skill unavailable: " + Names[i]); continue; }
                        var p = new Profile { Index = i, NativeText = Language.GetString(def.skillDescriptionToken), Skill = new S.Skill(Root + " - " + Names[i], def, def.activationState.stateType, switches[i]) };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                        S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => p.Skill.Proc == null ? 1f : C.Get(p.Skill.Proc));
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Drifter " + Names[i] + " changes could not be loaded: " + e); }
                }
                if (passiveEnabled.Value)
                {
                    string section = Root + " - Trash To Treasure";
                    white = S.Float(section, "White Scrap Speed Bonus (%)", 6f, 6f, "Movement speed gained per common scrap.");
                    green = S.Float(section, "Green Scrap Regen Bonus", 3f, 3f, "Health regeneration gained per uncommon scrap.");
                    red = S.Float(section, "Red Scrap Attack Speed Bonus (%)", 30f, 30f, "Attack speed gained per legendary scrap.");
                    yellow = C.Bind(section, "Yellow Scrap Cooldown Reduction (%)", 15f, new ConfigDescription("Skill cooldown reduction per boss scrap. Vanilla: 15.", new AcceptableValueRange<float>(0f, 100f)));
                    SkillLocator locator = prefab.GetComponent<SkillLocator>();
                    S.SetText(locator.passiveSkill.skillDescriptionToken, PassiveDescription);
                    foreach (string token in defs.SelectMany(d => d.keywordTokens ?? Array.Empty<string>()).Where(t => t.IndexOf("RECYCLE", StringComparison.OrdinalIgnoreCase) >= 0).Distinct()) S.SetText(token, RecycleDescription);
                }
                Install(); Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Drifter changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(changedCooldown: i == 6 ? (float?)7f : i == 8 ? (float?)20f : null, changedAgile: i == 2 ? (bool?)true : null, damage: false);
            object attack = i == 0 ? Sample("BluntForceHit1") : i == 6 ? Sample("TornadoSlam") : sample;
            if (i == 1) p.Projectile = Asset(Read(sample, "shotgunShotPrefab"));
            if (i == 5) p.Projectile = Asset(Read(sample, "projectilePrefab"));
            p.NativeDamage = Number(attack, i == 1 ? "shotgunShotDamageCoefficient" : "damageCoefficient", 1f) * 100f;
            if (i == 0 || i == 1 || i == 2 || i == 4 || i == 5 || i == 6)
                s.BindDamage(p.NativeDamage, i == 4 ? (float?)350f : i == 5 || i == 6 ? (float?)300f : null, p.Projectile ? Number(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f) : Number(attack, "procCoefficient", 1f), periodic: i == 6);
            if (i == 0)
            {
                p.NativeThird = Number(Sample("BluntForceHit3"), "damageCoefficient", 3.5f) * 100f;
                Float(p, "Third Hit Damage (%)", 350f, p.NativeThird, "Damage of the third hit as a percentage of base damage.");
                Float(p, "Junk Drop Chance On Hit (%)", 40f, Number(attack, "junkDropChance"), "Chance to drop junk on hit, multiplied by proc coefficient.");
            }
            if (i == 1)
            {
                p.NativeCount = (int)Number(sample, "numShotgunProjectiles");
                Int(p, "Number Of Debris", 4, p.NativeCount, "Number of debris projectiles fired alongside the main projectile.", 0);
                Int(p, "Junk To Bypass Cooldown", 2, (int)Number(s.Definition, "minimumJunkCost"), "Junk spent to use Cleanup while it is on cooldown.");
                p.Projectile = Asset(Read(sample, "shotgunShotPrefab"));
                float speed = (Number(sample, "minSpeed") + Number(sample, "maxSpeed")) / 2f;
                s.NativeProjectileSpeed = speed;
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", speed, speed, "Average launch speed in metres per second. Keeps the usual speed variation.");
            }
            if (i == 2)
            {
                object death = EntityStateCatalog.InstantiateState(GameType("EntityStates.JunkCube.DeathStateSmall"));
                p.NativeThird = Number(death, "explosionDamageCoefficient", 8f) * 100f;
                Float(p, "Explosion Damage (%)", 800f, p.NativeThird, "Cube explosion damage as a percentage of base damage.");
                Int(p, "Junk To Bypass Cooldown", 4, (int)Number(s.Definition, "minimumJunkCost"), "Junk spent to place a cube while the skill is on cooldown.");
                Bool(p, "Generate Junk From Bagged Cubes", false, true, "Allow breaking a bagged junk cube to generate junk.");
                Int(p, "Maximum Active Cubes", 4, 4, "Maximum active cubes per player. Use 0 for unlimited.");
                float radius = Number(death, "explosionRadius", 1f);
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Cube explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
            }
            if (i == 3)
            {
                object bag = Sample("Bag.BaggedObject");
                Bool(p, "Bagged Items Persist", true, false, "Keep the object in the bag when travelling to the next stage. Creatures are not carried between stages.");
                Float(p, "Creature Escape Time", Number(bag, "baseBreakoutTime", 10f), Number(bag, "baseBreakoutTime", 10f), "Base time before a creature attempts to escape, in seconds. Mass and elite status still affect this.", .001f);
                p.Values["Allied Player Escape Time"] = C.Bind(s.Section, "Allied Player Escape Time", 12f, new ConfigDescription("Seconds before a bagged allied player is released. Use 0 to allow immediate escape. Vanilla: No forced escape; players can leave immediately.", new AcceptableValueRange<float>(0f, 1000000f)));
            }
            if (i == 4)
            {
                Component bag = Controller(prefab, "RoR2.DrifterBagController");
                Int(p, "Creature Escape Hits", 3, (int)Number(bag, "maxSmacks", 3f), "Bludgeon hits before a surviving bagged creature escapes.", 1);
                Int(p, "Junk Per Hit", 1, 1 + (int)Number(sample, "ExtraJunk"), "Junk generated per hit on the bagged object.");
                Int(p, "Bonus Junk When Destroyed", 4, 4, "Additional junk generated when the bagged creature is destroyed.");
            }
            if (i == 5)
            {
                Int(p, "Junk On Landing", 4, 4, "Junk dropped where a thrown monster lands.");
                p.Projectile = Asset(Read(sample, "projectilePrefab"));
                float speed = Number(sample, "projectileBaseSpeed", -1f);
                if (speed >= 0f) { s.NativeProjectileSpeed = speed; s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", speed, speed, "Throw speed before object mass is applied, in metres per second."); }
            }
            if (i == 6)
            {
                Float(p, "Slam Damage (%)", 900f, Number(Sample("BluntForceTornado"), "damageCoefficient", 9f) * 100f, "Finishing slam damage as a percentage of base damage.");
                Bool(p, "Fix Attack Speed Scaling", true, false, "Make attack speed increase the tornado's hit rate.");
                Float(p, "Junk Drop Chance On Hit (%)", 50f, Number(attack, "junkDropChance", 50f), "Chance to drop junk on hit, multiplied by proc coefficient.");
                Float(p, "Crit Junk Chance (%)", 100f, 100f, "Chance for a critical hit to generate one additional junk.");
                float interval = 1f / Math.Max(.001f, Number(attack, "baseFireFrequency", 1f));
                s.Interval = S.Float(s.Section, "Damage Interval", interval, interval, "Seconds between tornado hits before attack speed is applied.", .001f);
                s.Duration = S.Float(s.Section, "Duration", Number(attack, "baseDuration", 1f), Number(attack, "baseDuration", 1f), "Base tornado duration in seconds.", .001f);
            }
            if (i == 7 || i == 8)
            {
                Int(p, "Junk Required", i == 7 ? 4 : 8, (int)Number(s.Definition, "minimumJunkCost"), "Minimum junk needed to use the skill.");
                p.JunkUsed = C.Bind(s.Section, "Junk Used", i == 7 ? "All" : "8", "Junk consumed per use. Use a whole number or All. Vanilla: " + S.Number(Number(s.Definition, "minimumJunkCost")) + ".");
                if (i == 7)
                {
                    Int(p, "Items Made", 0, 4, "Flat number of temporary items created.");
                    Int(p, "Items Per X Junk Consumed", 2, 0, "Create one additional item per this much junk consumed. Use 0 to disable.");
                    Bool(p, "Usable In Bazaar Or Computation Exchange", false, true, "Allow Salvage in the Bazaar Between Time and Computation Exchange.");
                }
                else
                {
                    Int(p, "Maximum Rerolls", 2, 2, "Maximum rerolls per interactable. Use -1 for unlimited.", -1);
                    Int(p, "Scrap Made", 1, 1, "Scrap created from a loose item. Printer items create this many temporary items instead.");
                    Int(p, "Temporary Items Made", 1, 1, "Temporary items created from a targeted loose item.");
                    Bool(p, "Can Downgrade Scrap", true, false, "Turn scrap into two of the next lower tier. Boss scrap becomes two uncommon scrap. Common scrap is unchanged.");
                    p.Projectile = Asset(Read(sample, "projectilePrefab"));
                    if (p.Projectile) s.BindProjectileSpeed(p.Projectile);
                }
            }
            if (p.Projectile && (i == 1 || i == 5))
            {
                ProjectileImpactExplosion explosion = p.Projectile.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    s.Radius = S.Float(s.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Impact radius in metres.");
                    s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the impact. Vanilla: None.");
                }
            }
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null, Type[] args = null)
        {
            MethodInfo m = type == null ? null : AccessTools.Method(type, name, args);
            if (m == null) throw new MissingMethodException(type?.FullName, name);
            harmony.Patch(m, prefix: prefix == null ? null : new HarmonyMethod(typeof(Drifter), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(Drifter), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Drifter), finalizer), transpiler: transpiler == null ? null : new HarmonyMethod(typeof(Drifter), transpiler), ilmanipulator: null);
        }
        private static void Hook(Type type)
        {
            if (type == null) return;
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(type))
            {
                if (m.IsStatic || m.IsAbstract || m.ContainsGenericParameters || m.Name.StartsWith("get_") || m.Name.StartsWith("set_") || m.Name.Contains("Serialize")) continue;
                if (hooks.Add(m)) harmony.Patch(m, prefix: new HarmonyMethod(typeof(Drifter), nameof(BeforeState)), postfix: new HarmonyMethod(typeof(Drifter), nameof(AfterState)), finalizer: new HarmonyMethod(typeof(Drifter), nameof(EndScope)), ilmanipulator: null);
            }
        }
        private static void Install()
        {
            if (stats != null || passiveEnabled.Value) Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats), transpiler: passiveEnabled.Value ? nameof(StatsIL) : null);
            foreach (Profile p in profiles.Values) Hook(p.Skill.StateType);
            if (profiles.ContainsKey(0) || profiles.ContainsKey(2) || profiles.ContainsKey(4) || profiles.ContainsKey(6))
            {
                Hook(typeof(BasicMeleeAttack)); Hook(State("BluntForceBase")); Hook(State("BluntForceHit3"));
                if (profiles.ContainsKey(0)) { Hook(State("BluntForceHit1")); Hook(State("BluntForceHit2")); }
            }
            if (profiles.ContainsKey(1)) Hook(typeof(AimThrowableBase));
            if (profiles.ContainsKey(2))
            {
                foreach (Type t in typeof(EntityState).Assembly.GetTypes().Where(t => t.Namespace == "EntityStates.JunkCube" && typeof(EntityState).IsAssignableFrom(t))) Hook(t);
                Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", postfix: nameof(AfterCubeLimit));
            }
            if (profiles.ContainsKey(3))
            {
                Hook(State("Repossess")); Hook(State("RepossessExit")); Hook(State("Bag.BaggedObject"));
                Patch(State("Bag.BaggedObject"), "UpdateBreakoutTimer", nameof(BeforeBreakout));
                Patch(State("Bag.BaggedObject"), "HoldsBreakoutBody", postfix: nameof(AfterCanBreakout));
                Patch(State("Bag.BaggedObject"), "Breakout", nameof(BeforeTransitBreakout));
                Patch(GameType("RoR2.DrifterBagController"), "HandlePlayerBagExitRequest", nameof(BeforePlayerExit));
                Patch(typeof(SceneExitController), "SetState", nameof(BeforeSceneExit));
                Patch(typeof(Run), "AdvanceStage", nameof(BeforeAdvanceStage));
                Patch(GameType("RoR2.DrifterBagController"), "OnSyncBaggedObject", nameof(BeforeBagSync), nameof(AfterBagSync));
                Patch(GameType("RoR2.DrifterBagController"), "AssignPassenger", postfix: nameof(AfterAssignPassenger));
                CharacterBody.onBodyStartGlobal += OnBodyStart;
                Stage.onServerStageBegin += OnStageBegin;
                Run.onRunDestroyGlobal += CleanupStored;
            }
            if (profiles.ContainsKey(3) || profiles.ContainsKey(5))
                Patch(typeof(CharacterBody), "set_isSprinting", nameof(BeforeBagSkillSprint));
            if (profiles.ContainsKey(4) || profiles.ContainsKey(2))
            {
                Patch(GameType("RoR2.DrifterBagController"), "CmdDamageBaggedObject", nameof(BeforeBagDamage), finalizer: nameof(EndBagDamage));
                Patch(GameType("RoR2.DrifterBagController"), "ExecuteBody", prefix: nameof(BeforeExecuteBody), finalizer: nameof(EndBagDamage));
            }
            if (profiles.ContainsKey(5)) { Hook(typeof(AimThrowableBase)); Patch(GameType("RoR2.Projectile.ThrownObjectProjectileController"), "ImpactBehavior", nameof(BeforeImpact), finalizer: nameof(EndBagDamage)); }
            if (profiles.ContainsKey(6))
            {
                Hook(State("TornadoSlam")); Hook(State("BluntForceTornado"));
                Patch(State("TornadoSlam"), "ModifyOverlapInfo", transpiler: nameof(CritJunkIL));
            }
            if (profiles.ContainsKey(7))
            {
                Patch(State("Salvage"), "ModifyNextState", postfix: nameof(AfterSalvageNext));
                Patch(State("Salvage"), "OnSerialize", postfix: nameof(AfterSalvageSerialize));
                Patch(State("Salvage"), "OnDeserialize", postfix: nameof(AfterSalvageDeserialize));
                Patch(State("Salvage"), "DropTempItemServer", nameof(BeforeSalvageDrop));
            }
            if (profiles.ContainsKey(8))
            {
                Hook(typeof(GenericProjectileBaseState));
                Patch(GameType("RoR2.Projectile.TinkerProjectile"), "TransmuteTargetObject", nameof(BeforeTransmute));
                Patch(GameType("RoR2.DrifterTracker"), "IsWhitelist", postfix: nameof(AfterWhitelist));
                Patch(GameType("RoR2.TinkerableObjectAttributes"), "get_IsTinkerable", nameof(BeforeTinkerable), finalizer: nameof(AfterTinkerable));
            }
            if (profiles.Count > 0)
            {
                Patch(GameType("RoR2.Skills.DrifterSkillDef"), "OnExecute", nameof(BeforeSkillExecute), finalizer: nameof(AfterSkillExecute));
                Patch(GameType("RoR2.Skills.DrifterTrackingSkillDef"), "OnExecute", nameof(BeforeSkillExecute), finalizer: nameof(AfterSkillExecute));
                foreach (string type in new[] { "RoR2.Skills.DrifterSkillDef", "RoR2.Skills.DrifterTrackingSkillDef" })
                    foreach (string name in new[] { "IsReady", "CanExecute" })
                        Patch(GameType(type), name, postfix: nameof(AfterReady));
                Patch(GameType("RoR2.JunkController"), "ConsumeJunk", nameof(BeforeConsume));
                Patch(GameType("RoR2.JunkController"), "GenerateJunk", nameof(BeforeGenerateJunk));
                Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                MethodInfo initialized = AccessTools.Method(typeof(ProjectileController), "DispatchOnInitialized");
                if (initialized != null) harmony.Patch(initialized, postfix: new HarmonyMethod(typeof(Drifter), nameof(AfterProjectileInitialized)), ilmanipulator: null);
                Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast), args: Type.EmptyTypes);
            }
        }
        private static void Apply()
        {
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 3 || p.Index == 5)
                {
                    p.Skill.Definition.cancelSprintingOnActivation = true;
                    p.Skill.Definition.canceledFromSprinting = false;
                    p.Skill.Definition.forceSprintDuringState = false;
                    p.Skill.SetKeyword("KEYWORD_AGILE", false);
                }
                if (p.Index == 1 || p.Index == 2) Write(p.Skill.Definition, "minimumJunkCost", (float)I(p, "Junk To Bypass Cooldown"));
                if (p.Index == 7 || p.Index == 8)
                {
                    Write(p.Skill.Definition, "minimumJunkCost", (float)I(p, "Junk Required"));
                    Write(p.Skill.Definition, "requireJunkForActivation", true);
                }
                if (passiveEnabled.Value) ReplaceKeyword(p.Skill, "RECYCLE", "KEYWORD_3QS_DRIFTER_RECYCLE", RecycleDescription);
            }
            if (passiveEnabled.Value) S.SetText("KEYWORD_RECYCLE", RecycleDescription);
        }
        private static void ReplaceKeyword(S.Skill skill, string match, string token, Func<string> description)
        {
            string[] tokens = skill.Definition.keywordTokens ?? Array.Empty<string>();
            if (!tokens.Any(t => t.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0 || Regex.IsMatch(Language.GetString(t), "<style=cKeywordName>\\s*\\[?\\s*" + Regex.Escape(match) + "\\s*\\]?\\s*</style>", RegexOptions.IgnoreCase))) return;
            skill.Definition.keywordTokens = tokens.Where(t => t.IndexOf(match, StringComparison.OrdinalIgnoreCase) < 0 && !Regex.IsMatch(Language.GetString(t), "<style=cKeywordName>\\s*\\[?\\s*" + Regex.Escape(match) + "\\s*\\]?\\s*</style>", RegexOptions.IgnoreCase)).ToArray();
            skill.SetKeyword(token, true); S.SetText(token, description);
        }
        private static bool BlocksSprint(EntityState state)
        {
            if (state == null) return false;
            Type type = state.GetType();
            return profiles.ContainsKey(3) && State("AimRepossess")?.IsAssignableFrom(type) == true
                || profiles.ContainsKey(5) && State("EmptyBag")?.IsAssignableFrom(type) == true;
        }
        private static void BeforeBagSkillSprint(CharacterBody __instance, ref bool value)
        {
            if (value && IsDrifter(__instance) && __instance.GetComponents<EntityStateMachine>().Any(machine => BlocksSprint(machine.state))) value = false;
        }
        private static void BeforeStats(CharacterBody __instance) { if (IsDrifter(__instance)) stats?.Apply(__instance); }
        private static void Set(Scope scope, object obj, string name, object value)
        {
            FieldInfo f = obj == null ? null : AccessTools.Field(obj.GetType(), name); if (f == null) return;
            if (f.IsStatic) scope.Saved.Add(Tuple.Create(f, f.GetValue(null)));
            f.SetValue(f.IsStatic ? null : obj, Convert.ChangeType(value, f.FieldType, CultureInfo.InvariantCulture));
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = new Scope { Previous = active }; Profile p = ForState(__instance); active = p == null ? null : __instance;
            if (p == null) return;
            if (BlocksSprint(__instance)) Body(__instance).isSprinting = false;
            int i = p.Index; string name = __instance.GetType().Name; S.Skill s = p.Skill;
            // Melee states cache their attack values during OnEnter. Do not overwrite mass scaling later in the animation.
            bool enter = __originalMethod.Name == "OnEnter" && !entered.GetOrCreateValue(__instance).Value;
            if (enter) entered.GetOrCreateValue(__instance).Value = true;
            if (enter && s.Damage != null)
            {
                float damage = C.Get(s.Damage);
                if (i == 0 && name == "BluntForceHit3") damage = F(p, "Third Hit Damage (%)");
                if (i == 6 && name == "BluntForceTornado") damage = F(p, "Slam Damage (%)");
                Set(__state, __instance, i == 1 ? "shotgunShotDamageCoefficient" : "damageCoefficient", damage / 100f);
                Set(__state, __instance, "procCoefficient", C.Get(s.Proc));
            }
            if (i == 0) Set(__state, __instance, "junkDropChance", F(p, "Junk Drop Chance On Hit (%)"));
            if (i == 1) Set(__state, __instance, "numShotgunProjectiles", (float)I(p, "Number Of Debris"));
            if (i == 2 && __instance.GetType().Namespace == "EntityStates.JunkCube")
            {
                Set(__state, __instance, "explosionDamageCoefficient", F(p, "Explosion Damage (%)") / 100f);
                Set(__state, __instance, "explosionProcCoefficient", C.Get(s.Proc));
                Set(__state, __instance, "explosionRadius", C.Get(s.Radius));
            }
            if (i == 3 && name == "BaggedObject") Set(__state, __instance, "baseBreakoutTime", F(p, "Creature Escape Time"));
            if (i == 4) Set(__state, __instance, "ExtraJunk", I(p, "Junk Per Hit") - 1);
            if (i == 5 && enter && s.ProjectileSpeed != null) Set(__state, __instance, "projectileBaseSpeed", C.Get(s.ProjectileSpeed));
            if (i == 6)
            {
                Set(__state, __instance, "junkDropChance", F(p, "Junk Drop Chance On Hit (%)"));
                if (enter && name == "TornadoSlam")
                {
                    Set(__state, __instance, "baseFireFrequency", 1f / C.Get(s.Interval));
                    Set(__state, __instance, "baseDuration", C.Get(s.Duration));
                }
            }
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod)
        {
            Profile p = ForState(__instance); if (p == null) return;
            string method = __originalMethod.Name, name = __instance.GetType().Name;
            if (p.Index == 6 && name == "TornadoSlam" && method == "OnEnter" && __originalMethod.DeclaringType == State("TornadoSlam"))
                Write(__instance, "fireFrequency", TornadoFrequency(C.Get(p.Skill.Interval), Number(__instance, "attackSpeedStat", 1f), B(p, "Fix Attack Speed Scaling")));
            if (p.Index == 7 && method == "OnEnter")
            {
                if (!salvageStates.TryGetValue(__instance, out SalvageRun run))
                {
                    if (!salvagePlans.TryGetValue(Body(__instance), out run)) run = new SalvageRun { Items = SalvageItems(I(p, "Items Made"), I(p, "Items Per X Junk Consumed"), UsedJunk(C.Get(p.JunkUsed), Junk(Body(__instance)))) };
                    salvageStates.Add(__instance, run); salvagePlans.Remove(Body(__instance));
                }
                Write(__instance, "itemsToDrop", run.Items);
                if (run.Items == 0) Write(__instance, "droppedItemServer", true);
            }
        }
        private static Exception EndScope(Exception __exception, Scope __state)
        {
            if (__state != null) { for (int i = __state.Saved.Count - 1; i >= 0; i--) __state.Saved[i].Item1.SetValue(null, __state.Saved[i].Item2); active = __state.Previous; }
            return __exception;
        }
        internal static int UsedJunk(string value, int available) => string.Equals(value?.Trim(), "All", StringComparison.OrdinalIgnoreCase) ? Math.Max(0, available) : int.TryParse(value, out int n) ? Math.Min(Math.Max(n, 0), Math.Max(available, 0)) : 0;
        internal static int SalvageItems(int flat, int per, int consumed) => Math.Max(0, flat) + (per > 0 ? Math.Max(0, consumed) / per : 0);
        internal static float TornadoFrequency(float interval, float attackSpeed, bool fix) => fix ? Math.Max(.001f, attackSpeed) / Math.Max(.001f, interval) : 1f / (Math.Max(.001f, interval) * Math.Max(.001f, attackSpeed));
        private static int Junk(CharacterBody body) => body && body.inventory ? body.inventory.GetItemCountEffective(DLC3Content.Items.Junk) : 0;
        private static void BeforeSkillExecute(SkillDef __instance, GenericSkill __0, out Execution __state)
        {
            __state = new Execution { Previous = executing, PreviousCost = junkCost }; executing = Definition(__instance);
            if (executing == null || executing.JunkUsed == null) return;
            junkCost = UsedJunk(C.Get(executing.JunkUsed), Junk(__0.characterBody));
            if (executing.Index == 7)
            {
                salvagePlans.Remove(__0.characterBody);
                salvagePlans.Add(__0.characterBody, new SalvageRun { Items = SalvageItems(I(executing, "Items Made"), I(executing, "Items Per X Junk Consumed"), junkCost) });
            }
        }
        private static Exception AfterSkillExecute(Execution __state, Exception __exception) { if (__state != null) { executing = __state.Previous; junkCost = __state.PreviousCost; } return __exception; }
        private static void BeforeConsume(ref int __0) { if (executing?.JunkUsed != null) __0 = junkCost; }
        private static void AfterReady(SkillDef __instance, GenericSkill __0, ref bool __result)
        {
            Profile p = Definition(__instance); if (p?.JunkUsed == null || !__result) return;
            int needed = I(p, "Junk Required");
            if (!string.Equals(C.Get(p.JunkUsed)?.Trim(), "All", StringComparison.OrdinalIgnoreCase) && int.TryParse(C.Get(p.JunkUsed), out int cost)) needed = Math.Max(needed, cost);
            if (Junk(__0.characterBody) < needed || p.Index == 7 && !B(p, "Usable In Bazaar Or Computation Exchange") && IsShop()) __result = false;
        }
        private static bool IsShop()
        {
            SceneDef scene = SceneInfo.instance ? SceneInfo.instance.sceneDef : null; if (!scene) return false;
            MethodInfo translate = AccessTools.Method(typeof(Language), "GetString", new[] { typeof(string), typeof(string) });
            string english = translate?.Invoke(null, new object[] { scene.nameToken, "en" }) as string ?? Language.GetString(scene.nameToken);
            string name = Regex.Replace(scene.cachedName + " " + scene.nameToken + " " + english, "[^A-Za-z]", "").ToLowerInvariant();
            return name.Contains("bazaar") || name.Contains("computationexchange") || name.Contains("computationalexchange");
        }
        private static void AfterSalvageNext(EntityState __instance, EntityState __0)
        {
            if (__0 != null && __0.GetType() == State("Salvage") && salvageStates.TryGetValue(__instance, out SalvageRun run)) { salvageStates.Remove(__0); salvageStates.Add(__0, run); }
        }
        private static void AfterSalvageSerialize(EntityState __instance, NetworkWriter __0)
        {
            if (salvageStates.TryGetValue(__instance, out SalvageRun run)) AccessTools.Method(typeof(NetworkWriter), "Write", new[] { typeof(int) }).Invoke(__0, new object[] { run.Items });
            else AccessTools.Method(typeof(NetworkWriter), "Write", new[] { typeof(int) }).Invoke(__0, new object[] { 0 });
        }
        private static void AfterSalvageDeserialize(EntityState __instance, NetworkReader __0)
        {
            int items = (int)AccessTools.Method(typeof(NetworkReader), "ReadInt32", Type.EmptyTypes).Invoke(__0, null);
            salvageStates.Remove(__instance); salvageStates.Add(__instance, new SalvageRun { Items = Math.Max(0, items) });
        }
        private static bool BeforeSalvageDrop(EntityState __instance) => !salvageStates.TryGetValue(__instance, out SalvageRun run) || run.Items > 0;
        private sealed class BagAction { internal object Controller; internal int Kind; internal object Previous; }
        private static void BeforeBagDamage(Component __instance, out object __state)
        {
            __state = bagAction;
            if (!IsDrifter(__instance.GetComponent<CharacterBody>())) return;
            bagAction = new BagAction { Controller = __instance, Kind = 4, Previous = __state };
            if (profiles.TryGetValue(4, out Profile p)) Write(__instance, "maxSmacks", I(p, "Creature Escape Hits"));
        }
        private static void BeforeExecuteBody(Component __instance, out object __state)
        {
            __state = bagAction;
            if (IsDrifter(__instance.GetComponent<CharacterBody>())) bagAction = new BagAction { Controller = __instance, Kind = 40, Previous = __state };
        }
        private static void BeforeImpact(Component __instance, out object __state)
        {
            __state = bagAction;
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            if (pc && pc.owner && IsDrifter(pc.owner.GetComponent<CharacterBody>())) bagAction = new BagAction { Controller = __instance, Kind = 5, Previous = __state };
        }
        private static Exception EndBagDamage(object __state, Exception __exception) { bagAction = __state; return __exception; }
        private static bool CubeInBag(object controller)
        {
            GameObject obj = Read(controller, "baggedObject") as GameObject;
            return obj && obj.GetComponent(GameType("JunkCubeController")) != null;
        }
        private static bool BeforeGenerateJunk(Component __instance, ref int __1)
        {
            if (!IsDrifter(__instance.GetComponent<CharacterBody>())) return true;
            Profile activeProfile = ForState(active);
            BagAction context = bagAction as BagAction;
            object bag = context?.Kind == 4 || context?.Kind == 40 ? context.Controller : Controller(__instance.GetComponent<CharacterBody>(), "RoR2.DrifterBagController");
            SkillDef primary = __instance.GetComponent<SkillLocator>()?.primary?.skillDef;
            bool bludgeoning = primary && primary.activationState.stateType == State("SuffocateSlam");
            if (profiles.TryGetValue(2, out Profile cube) && !B(cube, "Generate Junk From Bagged Cubes") && (context?.Kind == 4 || context?.Kind == 40 || activeProfile?.Index == 4 || context == null && bludgeoning) && CubeInBag(bag)) return false;
            if (context?.Kind == 5 && profiles.TryGetValue(5, out Profile discard)) __1 = I(discard, "Junk On Landing");
            if (profiles.TryGetValue(4, out Profile bludgeon))
            {
                if (context?.Kind == 40 || context?.Kind == 4 && Read(context.Controller, "baggedAttributes") is object attributes && Number(attributes, "durability") <= 1f) __1 = I(bludgeon, "Bonus Junk When Destroyed");
            }
            return __1 > 0;
        }
        private static void AfterCubeLimit(CharacterMaster __instance, DeployableSlot __0, ref int __result)
        {
            if (__0.ToString() != "DrifterJunkCube" || !IsDrifter(__instance.GetBody())) return;
            int limit = I(profiles[2], "Maximum Active Cubes"); __result = limit == 0 ? int.MaxValue : limit;
        }
        private static void AfterCanBreakout(EntityState __instance, ref bool __result)
        {
            if (__result || ForState(__instance)?.Index != 3) return;
            CharacterBody target = Read(__instance, "targetBody") as CharacterBody;
            if (target && target.isPlayerControlled && target.teamComponent.teamIndex == Body(__instance).teamComponent.teamIndex) __result = true;
        }
        private static bool BeforeBreakout(EntityState __instance)
        {
            if (ForState(__instance)?.Index != 3) return true;
            CharacterBody target = Read(__instance, "targetBody") as CharacterBody;
            if (!target || !target.isPlayerControlled || target.teamComponent.teamIndex != Body(__instance).teamComponent.teamIndex) return true;
            float timer = Number(__instance, "breakoutTimer") + Time.fixedDeltaTime;
            Write(__instance, "breakoutTimer", timer);
            if (timer >= F(profiles[3], "Allied Player Escape Time") && (bool)(Read(__instance, "isAuthority") ?? false))
            {
                AccessTools.Method(__instance.GetType(), "Breakout").Invoke(__instance, null); __instance.outer.SetNextStateToMain();
            }
            return false;
        }
        private static bool BeforePlayerExit(Component __instance, GameObject __0, ref bool? __1)
        {
            CharacterBody owner = __instance.GetComponent<CharacterBody>();
            if (!IsDrifter(owner) || !__0) return true;
            CharacterBody target = __0.GetComponent<CharacterBody>();
            if (!target || target.teamComponent.teamIndex != owner.teamComponent.teamIndex) return true;
            EntityStateMachine machine = EntityStateMachine.FindByCustomName(owner.gameObject, "Bag");
            if (machine && Number(machine.state, "fixedAge") < F(profiles[3], "Allied Player Escape Time")) { __1 = false; return false; }
            return true;
        }
        private static void BeforeProjectile(ref FireProjectileInfo __0)
        {
            Profile p = ForState(active); if (p == null) return;
            if (p.Index == 1 && p.Skill.ProjectileSpeed != null && p.Skill.NativeProjectileSpeed > 0f)
                __0.speedOverride *= C.Get(p.Skill.ProjectileSpeed) / p.Skill.NativeProjectileSpeed;
            else if (p.Index == 8 && p.Skill.ProjectileSpeed != null) __0.speedOverride = p.Skill.LaunchSpeed(__0.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController __instance)
        {
            Profile p = ForState(active); if (p != null && (p.Index == 1 || p.Index == 5 || p.Index == 8)) shots.GetOrCreateValue(__instance).Profile = p;
            BeforeProjectileStart(__instance);
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            if (!__instance || !__instance.owner || !IsDrifter(__instance.owner.GetComponent<CharacterBody>())) return;
            Shot shot = shots.GetOrCreateValue(__instance);
            Profile p = shot.Profile ?? profiles.Values.FirstOrDefault(x => x.Projectile && (x.Projectile.name + "(Clone)" == __instance.gameObject.name || __instance.catalogIndex >= 0 && __instance.catalogIndex == Number(x.Projectile.GetComponent<ProjectileController>(), "catalogIndex", -2f)));
            if (p == null || shot.Applied) return; shot.Applied = true; shot.Profile = p;
            if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
            ProjectileImpactExplosion explosion = __instance.GetComponent<ProjectileImpactExplosion>();
            if (explosion)
            {
                if (p.Skill.Radius != null) explosion.SetExplosionRadius(C.Get(p.Skill.Radius));
                if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area));
            }
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(active);
            if (p?.Index != 2 || active.GetType().Namespace != "EntityStates.JunkCube") return;
            if (active.GetType().Name.StartsWith("DeathState", StringComparison.Ordinal))
            {
                CharacterBody owner = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null;
                if (owner) __instance.baseDamage = owner.damage * F(p, "Explosion Damage (%)") / 100f;
                if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
            }
            if (p.NativeDamage > 0f) __instance.baseDamage *= C.Get(p.Skill.Damage) / p.NativeDamage;
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff v) => v == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : v == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : v == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : v == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : BlastAttack.FalloffModel.None;
        private static bool BeforeTransmute(Component __instance, GameObject __0)
        {
            if (!NetworkServer.active || !__0) return true;
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            if (!pc || !pc.owner || !IsDrifter(pc.owner.GetComponent<CharacterBody>())) return true;
            GenericPickupController pickup = __0.GetComponent<GenericPickupController>();
            if (!pickup || __0.GetComponent(GameType("RoR2.TinkerableObjectAttributes"))) return true;
            IList processed = Read(__instance, "tinkeredObjects") as IList;
            if (processed?.Contains(__0) == true) return false;
            object tracker = Read(__instance, "ownerTracker");
            if (tracker == null) return true;
            object[] args = { pickup.pickup };
            if (!(bool)AccessTools.Method(tracker.GetType(), "IsWhitelist").Invoke(tracker, args)) return false;
            ItemDef item = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(pickup.pickup.pickupIndex).itemIndex);
            if (!item) return false;
            Profile p = profiles[8]; processed?.Add(__0);
            Vector3 pos = __0.transform.position;
            ItemIndex lower = Downgrade(item.itemIndex);
            if (lower != ItemIndex.None && B(p, "Can Downgrade Scrap"))
                for (int i = 0; i < 2; i++) Drop(new UniquePickup(PickupCatalog.FindPickupIndex(lower)), pos, i, __instance.transform.forward);
            else
            {
                int scraps = I(p, "Scrap Made"), temps = I(p, "Temporary Items Made");
                if (!pickup.Duplicated)
                    for (int i = 0; i < scraps; i++) Drop(new UniquePickup(PickupCatalog.FindScrapIndexForItemTier(item.tier)), pos, i, __instance.transform.forward);
                else temps += scraps;
                UniquePickup temporary = pickup.pickup;
                if (!item.ContainsTag(ItemTag.CanBeTemporary))
                {
                    ItemIndex replacement = Util.RollTemporaryItemFromItemIndex(item.itemIndex);
                    temporary = replacement == ItemIndex.None ? default : new UniquePickup(PickupCatalog.FindPickupIndex(replacement));
                }
                temporary.decayValue = 1f;
                if (temporary.isValid) for (int i = 0; i < temps; i++) Drop(temporary, pos, i, __instance.transform.forward);
            }
            if (Read(__instance, "effectPrefab") is GameObject effect && effect) EffectManager.SimpleEffect(effect, pos, Quaternion.identity, true);
            UnityEngine.Object.Destroy(__0); return false;
        }
        private static void Drop(UniquePickup pickup, Vector3 pos, int index, Vector3 forward)
        {
            Vector3 velocity = Quaternion.AngleAxis(index * 137.5f, Vector3.up) * forward * 3f + Vector3.up * 5f;
            PickupDropletController.CreatePickupDroplet(new GenericPickupController.CreatePickupInfo { pickup = pickup, position = pos }, pos, velocity);
        }
        private static ItemIndex Downgrade(ItemIndex item) => item == RoR2Content.Items.ScrapGreen.itemIndex ? RoR2Content.Items.ScrapWhite.itemIndex : item == RoR2Content.Items.ScrapRed.itemIndex || item == RoR2Content.Items.ScrapYellow.itemIndex ? RoR2Content.Items.ScrapGreen.itemIndex : ItemIndex.None;
        private static void AfterWhitelist(Component __instance, UniquePickup __0, ref bool __result)
        {
            if (__result || !B(profiles[8], "Can Downgrade Scrap") || !IsDrifter(__instance.GetComponent<CharacterBody>()) || __0.isTempItem || (bool)(S.Read(__0, "isRental") ?? false)) return;
            __result = Downgrade(PickupCatalog.GetPickupDef(__0.pickupIndex)?.itemIndex ?? ItemIndex.None) != ItemIndex.None;
        }
        private static void BeforeTinkerable(object __instance, out int __state)
        {
            __state = (int)Number(__instance, "maxTinkers", 2f); int max = I(profiles[8], "Maximum Rerolls"); Write(__instance, "maxTinkers", max < 0 ? int.MaxValue : max);
        }
        private static Exception AfterTinkerable(object __instance, int __state, Exception __exception) { Write(__instance, "maxTinkers", __state); return __exception; }
        private static bool IsCarryableObject(GameObject obj)
        {
            if (!obj) return false;
            CharacterBody body = obj.GetComponent<CharacterBody>();
            if (!body) return true;
            if (body.isPlayerControlled || body.master) return false;
            Component attributes = obj.GetComponent(GameType("RoR2.SpecialObjectAttributes"));
            return (bool)(Read(attributes, "overrideBody") ?? false) || (body.bodyFlags & CharacterBody.BodyFlags.Masterless) != 0;
        }
        private static void MarkPersistent(GameObject obj)
        {
            if (!IsCarryableObject(obj)) return;
            obj.transform.SetParent(null, true);
            UnityEngine.Object.DontDestroyOnLoad(obj);
        }
        private static bool BeforeTransitBreakout(EntityState __instance)
        {
            GameObject obj = Read(__instance, "targetObject") as GameObject;
            TransitObject transit = obj ? obj.GetComponent<TransitObject>() : null;
            return !transit || !transit.InTransit;
        }
        private static void BeforeBagSync(Component __instance, out GameObject __state) { __state = Read(__instance, "baggedObject") as GameObject; }
        private static void AfterBagSync(Component __instance, GameObject __0, GameObject __state)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (!IsDrifter(body) || !B(profiles[3], "Bagged Items Persist")) return;
            // Preserve the same object on the host and remote clients while it is held.
            if (__0) MarkPersistent(__0);
            if (NetworkServer.active && IsCarryableObject(__state) && __state != __0 && !carried.Values.Any(item => item.Object == __state)) ReturnToScene(__state);
        }
        private static void AfterAssignPassenger(Component __instance, GameObject __0)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (IsDrifter(body) && B(profiles[3], "Bagged Items Persist") && Read(__instance, "baggedObject") as GameObject == __0) MarkPersistent(__0);
        }
        private static void ReturnToScene(GameObject obj)
        {
            if (!obj) return;
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            obj.transform.SetParent(null, true);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(obj, scene);
        }
        private static void BeforeSceneExit(SceneExitController.ExitState __0)
        {
            if (__0 == SceneExitController.ExitState.TeleportOut) StoreBags();
        }
        private static void BeforeAdvanceStage() { StoreBags(); }
        private static void StoreBags()
        {
            if (!NetworkServer.active || !B(profiles[3], "Bagged Items Persist")) return;
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList.ToArray())
            {
                if (!IsDrifter(body) || !body.master || carried.ContainsKey(body.master)) continue;
                Component bag = Controller(body, "RoR2.DrifterBagController");
                GameObject obj = Read(bag, "baggedObject") as GameObject;
                if (!IsCarryableObject(obj)) continue;
                // Record before detaching: the bag's reset callback must not return it to the old scene.
                carried[body.master] = new StoredBag { Object = obj, OriginScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle };
                MarkPersistent(obj);
                Component seat = Read(bag, "vehicleSeat") as Component;
                if (seat) AccessTools.Method(seat.GetType(), "EjectPassenger", Type.EmptyTypes).Invoke(seat, null);
                AccessTools.Method(bag.GetType(), "ResetBaggedObject").Invoke(bag, null);
                EntityStateMachine machine = EntityStateMachine.FindByCustomName(body.gameObject, "Bag"); if (machine) machine.SetNextStateToMain();
                MarkPersistent(obj);
                TransitObject transit = obj.GetComponent<TransitObject>() ?? obj.AddComponent<TransitObject>(); transit.Freeze();
                ThreeQsVanillaTweaks.Log.Info("Drifter: stored " + obj.name + " for the next stage.");
            }
        }
        private static void QueueRestore(CharacterBody body)
        {
            if (body && body.master && carried.ContainsKey(body.master) && !body.GetComponent<RestoreBag>()) body.gameObject.AddComponent<RestoreBag>();
        }
        private static void OnBodyStart(CharacterBody body) { if (NetworkServer.active) QueueRestore(body); }
        private static void OnStageBegin(Stage stage)
        {
            if (!NetworkServer.active) return;
            foreach (var entry in carried.ToArray())
            {
                if (!entry.Key || !entry.Value.Object)
                {
                    if (entry.Key && !entry.Value.Object) ThreeQsVanillaTweaks.Log.Warning("Drifter: the stored object was destroyed during stage travel.");
                    if (entry.Value.Object) UnityEngine.Object.Destroy(entry.Value.Object);
                    carried.Remove(entry.Key); continue;
                }
                QueueRestore(entry.Key.GetBody());
            }
        }
        private static bool Restore(CharacterBody body)
        {
            if (!body || !body.master) return true;
            if (!carried.TryGetValue(body.master, out StoredBag stored)) return true;
            GameObject obj = stored.Object;
            if (!obj) { carried.Remove(body.master); ThreeQsVanillaTweaks.Log.Warning("Drifter: the stored object was destroyed during stage travel."); return true; }
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || scene.handle == stored.OriginScene || !body.healthComponent || !body.healthComponent.alive) return false;
            if (!stored.Placed)
            {
                obj.transform.position = body.corePosition;
                ProjectileController projectile = obj.GetComponent<ProjectileController>();
                if (projectile && obj.GetComponent(GameType("JunkCubeController"))) projectile.Networkowner = body.gameObject;
                obj.GetComponent<TransitObject>()?.Thaw(); stored.Placed = true;
            }
            if (!IsDrifter(body) || !B(profiles[3], "Bagged Items Persist")) { ReleaseStored(body); return true; }
            Component bag = Controller(body, "RoR2.DrifterBagController");
            if (!bag || !EntityStateMachine.FindByCustomName(body.gameObject, "Bag")) return false;
            GameObject held = Read(bag, "baggedObject") as GameObject;
            if (held == obj)
            {
                carried.Remove(body.master);
                ThreeQsVanillaTweaks.Log.Info("Drifter: restored " + obj.name + " to the bag.");
                return true;
            }
            if (held) { ReleaseStored(body); return true; }
            // Remote-authority assignment is asynchronous. Keep the entry until the server sees it in the bag.
            if (Time.unscaledTime >= stored.NextRequest)
            {
                stored.NextRequest = Time.unscaledTime + 1f;
                AccessTools.Method(bag.GetType(), "AssignPassengerAndBaggedStateExternally").Invoke(bag, new object[] { obj });
            }
            return false;
        }
        private static void ReleaseStored(CharacterBody body)
        {
            if (!body || !body.master || !carried.TryGetValue(body.master, out StoredBag stored)) return;
            if (stored.Object)
            {
                stored.Object.transform.position = body.corePosition;
                stored.Object.GetComponent<TransitObject>()?.Thaw(); ReturnToScene(stored.Object);
                ThreeQsVanillaTweaks.Log.Warning("Drifter: could not restore the stored object to the bag; released it beside the player.");
            }
            carried.Remove(body.master);
        }
        private static void CleanupStored(Run run)
        {
            foreach (StoredBag item in carried.Values) if (item.Object) UnityEngine.Object.Destroy(item.Object);
            carried.Clear();
        }
        private static int Local(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0) return 0;
            if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1) return 1;
            if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2) return 2;
            if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3) return 3;
            if (instruction.operand is LocalBuilder builder) return builder.LocalIndex;
            if (instruction.operand is LocalVariableInfo variable) return variable.LocalIndex;
            return instruction.operand is byte b ? b : instruction.operand is short s ? s : instruction.operand is int n ? n : -1;
        }
        private static bool Loads(CodeInstruction instruction, int local) => local >= 0 && instruction.opcode.Name.StartsWith("ldloc", StringComparison.Ordinal) && Local(instruction) == local;
        private static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); string[] fields = { "ScrapWhite", "ScrapGreen", "ScrapRed", "ScrapYellow" };
            int[] locals = Enumerable.Repeat(-1, 4).ToArray();
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is FieldInfo field)
                {
                    int k = Array.IndexOf(fields, field.Name); if (k < 0) continue;
                    for (int j = i + 1; j < Math.Min(code.Count, i + 35); j++)
                        if (code[j].opcode.Name.StartsWith("stloc", StringComparison.Ordinal)) { locals[k] = Local(code[j]); break; }
                }
            int[] matched = new int[4];
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction current = code[i]; yield return current;
                if (current.opcode != OpCodes.Ldc_R4 || !(current.operand is float value)) continue;
                int kind = -1;
                for (int k = 0; k < 3; k++)
                    if (Math.Abs(value - (k == 0 ? .06f : k == 1 ? 3f : .3f)) < .000001f && code.Skip(Math.Max(0, i - 5)).Take(Math.Min(5, i)).Any(c => Loads(c, locals[k]))) kind = k;
                if (Math.Abs(value - .85f) < .000001f && code.Skip(i + 1).Take(18).Any(c => Loads(c, locals[3]))) kind = 3;
                if (kind < 0) continue;
                matched[kind]++;
                yield return new CodeInstruction(OpCodes.Ldc_I4, kind); yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Drifter), nameof(ScrapCoefficient)));
            }
            if (matched.Any(n => n != 1)) throw new InvalidOperationException("Drifter scrap stat anchors did not match: " + string.Join(",", matched));
        }
        private static float ScrapCoefficient(float vanilla, int kind, CharacterBody body) => !IsDrifter(body) ? vanilla : kind == 0 ? C.Get(white) / 100f : kind == 1 ? C.Get(green) : kind == 2 ? C.Get(red) / 100f : 1f - C.Get(yellow) / 100f;
        private static IEnumerable<CodeInstruction> CritJunkIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int changed = 0;
            for (int i = 0; i < code.Count; i++)
            {
                yield return code[i];
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo field && field.Name == "isCrit" && i + 1 < code.Count && code[i + 1].opcode.FlowControl == FlowControl.Cond_Branch)
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Drifter), nameof(CritJunk))); changed++; }
            }
            if (changed != 1) throw new InvalidOperationException("Drifter critical junk anchor did not match.");
        }
        private static bool CritJunk(bool crit) => crit && (ForState(active)?.Index != 6 || Util.CheckRoll(F(profiles[6], "Crit Junk Chance (%)"), Body(active).master));
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string text = p.NativeText ?? "";
            if (s.Damage != null)
                text = Regex.Replace(text, @"(?<![\d.])([\d.]+)%", m =>
                {
                    if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) return m.Value;
                    if (p.Index == 2 && Math.Abs(value - p.NativeThird) < .0001f) return S.Number(F(p, "Explosion Damage (%)")) + "%";
                    if (p.Index == 0 && Math.Abs(value - p.NativeThird) < .0001f) return S.Number(F(p, "Third Hit Damage (%)")) + "%";
                    return Math.Abs(value - p.NativeDamage) < .0001f ? S.Number(C.Get(s.Damage)) + "%" : m.Value;
                });
            if (p.Index == 1) text = Regex.Replace(text, @"(?<![\d.])" + p.NativeCount + @"(?=(?:</style>)? (?:pieces of |random )?(?:debris|projectiles))", I(p, "Number Of Debris").ToString(), RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"(?:<style=cIsUtility>)?Agile\.(?:</style>)?\s*", "", RegexOptions.IgnoreCase);
            if (p.Index == 6) text = "Spin forward, dealing " + S.DamageText(C.Get(s.Damage), C.Get(s.Interval), true) + ". Finish with a slam for " + S.Damage(S.Number(F(p, "Slam Damage (%)")) + "% damage") + ".";
            if (p.Index == 7) text = "Turn " + S.Utility(string.Equals(C.Get(p.JunkUsed)?.Trim(), "All", StringComparison.OrdinalIgnoreCase) ? "all your junk" : C.Get(p.JunkUsed) + " junk") + " into " + S.Utility("temporary items") + ".";
            if (p.Index == 8) text = "Tinker with interactables to " + S.Utility("reroll their contents") + ", or break down items into " + S.Utility("scrap and temporary items") + "." + (B(p, "Can Downgrade Scrap") ? " Downgrade scrap into " + S.Utility("two lower-tier scrap") + "." : "");
            return (p.Index == 3 || p.Index == 5 ? "" : s.AgileText) + text;
        }
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Index == 0) text += "\nThird hit damage: " + S.Number(F(p, "Third Hit Damage (%)")) + "%\nJunk chance: " + S.Number(F(p, "Junk Drop Chance On Hit (%)")) + "%";
            if (p.Index == 1) text += "\nDebris: " + I(p, "Number Of Debris");
            if (p.Index == 2) text += "\nExplosion damage: " + S.Number(F(p, "Explosion Damage (%)")) + "%\nMaximum cubes: " + (I(p, "Maximum Active Cubes") == 0 ? "Unlimited" : I(p, "Maximum Active Cubes").ToString());
            if (p.Index == 1 || p.Index == 2) text += "\nJunk to bypass cooldown: " + I(p, "Junk To Bypass Cooldown");
            if (p.Index == 3) text += "\nCreature escape timer: " + S.Number(F(p, "Creature Escape Time")) + "s\nAllied player escape timer: " + S.Number(F(p, "Allied Player Escape Time")) + "s";
            if (p.Index == 4) text += "\nJunk per hit: " + I(p, "Junk Per Hit") + "\nBonus junk on destruction: " + I(p, "Bonus Junk When Destroyed") + "\nHits before escape: " + I(p, "Creature Escape Hits");
            if (p.Index == 5) text += "\nJunk on landing: " + I(p, "Junk On Landing");
            if (p.Index == 7 || p.Index == 8) text += "\nJunk required: " + I(p, "Junk Required") + "\nJunk consumed: " + C.Get(p.JunkUsed);
            if (p.Index == 7) text += "\nItems made: " + I(p, "Items Made") + (I(p, "Items Per X Junk Consumed") > 0 ? " + 1 per " + I(p, "Items Per X Junk Consumed") + " junk consumed" : "");
            if (p.Index == 8) text += "\nMaximum rerolls: " + (I(p, "Maximum Rerolls") < 0 ? "Unlimited" : I(p, "Maximum Rerolls").ToString());
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Number(C.Get(p.Skill.Radius)) + "m";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile speed: " + S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s";
            return text;
        }
        private static string RecycleDescription() =>
            "<style=cKeywordName>Recycle</style><style=cSub>" +
            "<color=#FFFFFF>Common scrap</color> grants " + S.Utility(S.Number(C.Get(white)) + "% movement speed") + " per stack.\n" +
            "<color=#77FF66>Uncommon scrap</color> grants " + S.Utility(S.Number(C.Get(green)) + " health regeneration") + " per stack.\n" +
            "<color=#FF4C4C>Legendary scrap</color> grants " + S.Damage(S.Number(C.Get(red)) + "% attack speed") + " per stack.\n" +
            "<color=#FFFF00>Boss scrap</color> reduces skill cooldowns by " + S.Utility(S.Number(C.Get(yellow)) + "%") + " per stack.</style>";
        private static string PassiveDescription() => "Gain bonuses from " + S.Utility("scrap") + ". " + S.Utility("Recycle") + " to make the most of your trash.";
    }
}
