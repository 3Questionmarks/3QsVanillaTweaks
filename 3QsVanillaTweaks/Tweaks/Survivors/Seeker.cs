using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
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
    internal static class Seeker
    {
        private const string Root = "Survivors - 14 Seeker", StatsRoot = "Survivor-Stats - 14 Seeker";
        private static readonly string[] Names = { "Spirit Punch", "Unseen Hand", "Soul Spiral", "Sojourn", "Reprieve", "Meditate", "Palm Blast", "Soul Search" };
        private static readonly string[] States = { "SpiritPunch", "UnseenHand", "SoulSpiral", "Sojourn", "Reprieve", "Meditate", "BaseChargePalmBlast", "SoulSearch" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile, Finisher, Charged, Vehicle, Cyclone;
            internal float NativeRadius, ChargedSpeedRatio = 1f;
            internal readonly Dictionary<string, ConfigEntry<float>> F = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<int>> I = new Dictionary<string, ConfigEntry<int>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> B = new Dictionary<string, ConfigEntry<bool>>();
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal Profile PreviousHealing;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class Payment { internal int Cost; }
        private sealed class Shot : MonoBehaviour { internal Profile Profile; internal bool Finisher; internal Vector3 Origin; }
        private sealed class SpawnList { internal readonly List<GameObject> Objects = new List<GameObject>(); }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<int, ConfigEntry<bool>> switches = new Dictionary<int, ConfigEntry<bool>>();
        private static readonly HashSet<MethodBase> hooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<DamageInfo, Shot> slowHits = new ConditionalWeakTable<DamageInfo, Shot>();
        private static readonly ConditionalWeakTable<CharacterBody, SpawnList> cyclones = new ConditionalWeakTable<CharacterBody, SpawnList>();
        private static ConfigEntry<bool> enabled, passiveEnabled, statsEnabled;
        private static ConfigEntry<int> maxStacks;
        private static ConfigEntry<float> statBonus;
        private static CharacterBody prefab;
        private static S.Stats stats;
        private static string nativePassiveDescription;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static Profile activeHealing;
        private static Type GameType(string name) => typeof(EntityState).Assembly.GetType(name);
        private static Type State(string name) => GameType("EntityStates.Seeker." + name);
        private static bool IsSeeker(CharacterBody body) => body && prefab && body.bodyIndex == prefab.bodyIndex;
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
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
        private static float Number(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible value ? Convert.ToSingle(value) : fallback;
        private static GameObject Asset(object obj) => obj as GameObject ?? Read(obj, "gameObject") as GameObject;
        private static Component Component(GameObject obj, string type) => obj && GameType(type) != null ? obj.GetComponent(GameType(type)) : null;
        private static object Sample(Type type) => type == null ? null : EntityStateCatalog.InstantiateState(type);
        private static float F(Profile p, string key) => C.Get(p.F[key]);
        private static int I(Profile p, string key) => C.Get(p.I[key]);
        private static bool B(Profile p, string key) => C.Get(p.B[key]);
        private static void Float(Profile p, string key, float value, float vanilla, string description, float minimum = 0f) => p.F[key] = S.Float(p.Skill.Section, key, value, vanilla, description, minimum);
        private static void Int(Profile p, string key, int value, int vanilla, string description, int minimum = 0) => p.I[key] = S.Int(p.Skill.Section, key, value, vanilla, description, minimum);
        private static void Bool(Profile p, string key, bool value, bool vanilla, string description) => p.B[key] = S.Bool(p.Skill.Section, key, value, vanilla, description);
        private static BuffDef Chakra => DLC2Content.Buffs.ChakraBuff;
        private static int Stacks(CharacterBody body) => body ? body.GetBuffCount(Chakra) : 0;
        private static int StackLimit => maxStacks == null ? 7 : C.Get(maxStacks);
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Seeker changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Tranquility", "Changes Enabled", true, "Enable Tranquility changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Seeker stat changes. Restart to apply. Vanilla: False.");
            for (int i = 0; i < Names.Length; i++) switches[i] = C.BindStartupBool(Root + " - " + Names[i], "Changes Enabled", true, "Enable " + Names[i] + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, string transpiler = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray());
            harmony.Patch(method, prefix: prefix == null ? null : new HarmonyMethod(typeof(Seeker), prefix), postfix: postfix == null ? null : new HarmonyMethod(typeof(Seeker), postfix), transpiler: transpiler == null ? null : new HarmonyMethod(typeof(Seeker), transpiler), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Seeker), finalizer), ilmanipulator: null);
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "Update", "OnExit" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType.Namespace != "EntityStates.Seeker") continue;
                method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray());
                if (hooks.Add(method)) harmony.Patch(method, prefix: new HarmonyMethod(typeof(Seeker), nameof(BeforeState)), finalizer: new HarmonyMethod(typeof(Seeker), nameof(EndScope)));
            }
        }
        private static void Load()
        {
            try
            {
                prefab = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC2/Seeker/SeekerBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Seeker");
                nativePassiveDescription = Language.GetString(prefab.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken);
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefab);
                if (passiveEnabled.Value)
                {
                    maxStacks = C.Bind(Root + " - Tranquility", "Maximum Stacks", 7, new ConfigDescription("Maximum number of Tranquility stacks. Vanilla: 7.", new AcceptableValueRange<int>(1, 255)));
                    statBonus = S.Float(Root + " - Tranquility", "Stat Increase Per Stack (%)", 1f, 0f, "Stat bonus granted by each Tranquility stack.");
                    string token = prefab.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken;
                    S.SetText(token, PassiveDescription);
                    foreach (string method in new[] { "CmdIncrementChakraGate", "UnlockGateEffects", "UnlockPalmBlastEffects", "Start" }) Patch(GameType("RoR2.SeekerController"), method, transpiler: nameof(StackLimitIL));
                    Patch(GameType("RoR2.SeekerController"), "UpdatePetalsUI", nameof(BeforePetals));
                }
                var defs = prefab.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[i].Value) continue;
                    Type state = State(States[i]);
                    SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == state);
                    if (!def && i == 6) def = defs.FirstOrDefault(d => state != null && d.activationState.stateType != null && state.IsAssignableFrom(d.activationState.stateType));
                    if (!def) { if (i != 7) ThreeQsVanillaTweaks.Log.Warning("Seeker skill unavailable: " + Names[i]); continue; }
                    var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, state, switches[i]) };
                    Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                    Hook(state); Hook(def.activationState.stateType); if (i == 6) Hook(State("BaseFirePalmBlast"));
                    if (i != 7) S.SetDescription(p.Skill, def.skillDescriptionToken, () => Description(p));
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? (Func<float>)null : () => C.Get(p.Skill.Proc));
                }
                InstallHooks(); Apply(); C.EffectiveSettingsChanged += Apply;
                foreach (Profile p in profiles.Values)
                    if (HasTranquilityBonus(p)) S.SetText(TranquilityToken(p), () => TranquilityTooltip(p));
                if (profiles.ContainsKey(5) || profiles.ContainsKey(6)) S.SetText("KEYWORD_3QS_SEEKER_SAVING_GRACE", () => "<style=cKeywordName>Saving Grace</style><style=cSub>Increase stats by " + S.Number(profiles.TryGetValue(5, out Profile meditate) ? F(meditate, "Saving Grace Stat Bonus (%)") : 7f) + "%. Revive fallen allies once per stage.</style>");
                if (profiles.ContainsKey(2)) S.SetText("KEYWORD_3QS_SEEKER_EXHILARATE", () => "<style=cKeywordName>Exhilarate</style><style=cSub>Reusing Soul Spiral while at the spiral limit increases existing spirals' rotation speed and damage by " + S.Number(F(profiles[2], "Exhilarate Rotation Speed And Damage Bonus (%)")) + "% per boost.</style>");
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Seeker changes could not be loaded: " + error); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index;
            s.Bind(damage: false, changedAgile: i >= 2 && i <= 4 ? (bool?)true : null);
            object sample = s.Sample;
            p.Projectile = Asset(Read(sample, i == 1 ? "fistProjectilePrefab" : "projectilePrefab"));
            p.Finisher = Asset(Read(sample, "projectileFinisherPrefab")); p.Charged = Asset(Read(sample, "chargedProjectilePrefab"));
            if (i == 6)
            {
                object fire = Sample(State("BaseFirePalmBlast")); p.Projectile = Asset(Read(fire, "projectilePrefab")); p.Charged = Asset(Read(fire, "chargedProjectilePrefab"));
                Float(p, "Charge Time", Number(sample, "baseChargeDuration", 1f), Number(sample, "baseChargeDuration", 1f), "Time to fully charge, before attack speed is applied.", 0.001f);
            }
            if (i == 3 || i == 4)
            {
                p.Vehicle = Asset(Read(sample, "vehiclePrefab"));
                object vehicle = Component(p.Vehicle, i == 3 ? "RoR2.SojournVehicle" : "RoR2.ReprieveVehicle");
                p.Cyclone = Asset(Read(vehicle, "cyclonePrefab"));
                s.Duration = S.Float(s.Section, "Duration", -1f, -1f, "Maximum flight duration in seconds. -1 lasts until cancelled or forced to end.", -1f);
                Float(p, "Turn Speed (%)", Number(vehicle, "turnSpeedCoeficient", 1f) * 100f, Number(vehicle, "turnSpeedCoeficient", 1f) * 100f, "How quickly flight turns towards your aim. 100 turns instantly.");
                if (i == 3)
                {
                    Float(p, "Flight Acceleration Time", Number(vehicle, "afterGraceLerpDownTime", 1f), Number(vehicle, "afterGraceLerpDownTime", 1f), "Time for flight speed to adjust after the initial grace period.", 0.001f);
                    Float(p, "Healing Reduction Per Second (%)", 10f, 10f, "Healing penalty added for each second spent in Sojourn.");
                    float tick = Number(vehicle, "agePerTenthSecondsCheck", 0.1f);
                    Float(p, "Health Cost Per Tick (%)", Number(vehicle, "incrementalFlightDamage", 0.0075f) * 100f, Number(vehicle, "incrementalFlightDamage", 0.0075f) * 100f, "Health consumed by each flight damage tick.");
                    Float(p, "Health Cost Interval", tick, tick, "Seconds between flight damage ticks.", 0.001f);
                    Float(p, "Health Cost Increase (%)", Number(vehicle, "delayedIncreasedFlightDamage", 0.0005f) * 100f, Number(vehicle, "delayedIncreasedFlightDamage", 0.0005f) * 100f, "Additional health consumed per tick after each increase.");
                    Float(p, "Health Cost Increase Delay", Number(vehicle, "thresholdToStartExtraFlightDamage", 50f) * tick, Number(vehicle, "thresholdToStartExtraFlightDamage", 50f) * tick, "Time before the flight damage starts increasing.");
                    Float(p, "Health Cost Increase Interval", Number(vehicle, "delayedSecondsToIncreaseFlightDamage", 50f) * tick, Number(vehicle, "delayedSecondsToIncreaseFlightDamage", 50f) * tick, "Seconds between increases in flight damage.", 0.001f);
                }
                else
                {
                    Float(p, "Barrier Gained (%)", 20f, Number(sample, "barrierOnReprieveEnter", 0.4f) * 100f, "Maximum health granted as barrier when entering Reprieve.");
                    Float(p, "Extra Barrier Per Tranquility Stack (%)", Number(sample, "barrierTranquilityGrowth", 0.1f) * 100f, Number(sample, "barrierTranquilityGrowth", 0.1f) * 100f, "Extra barrier granted when entering Reprieve per Tranquility stack.");
                    Float(p, "Flight Acceleration (%)", Number(vehicle, "speedIncreasePercent", 0.05f) * 100f, Number(vehicle, "speedIncreasePercent", 0.05f) * 100f, "Flight speed gained at each acceleration interval.");
                    Float(p, "Flight Acceleration Interval", Number(vehicle, "speedIncreaseFrenquency", 0.5f), Number(vehicle, "speedIncreaseFrenquency", 0.5f), "Seconds between increases in flight speed.", 0.001f);
                    object cyclone = Component(p.Cyclone, "RoR2.SeekerCycloneController");
                    Float(p, "Cyclone Duration", 4f, Number(cyclone, "baseLifetime", 5f), "Cyclone duration in seconds.", 0.001f);
                    Float(p, "Cyclone Healing (%)", 1f, Number(cyclone, "allyHealingPercent", 0.01f) * 100f, "Allies' maximum health restored by each cyclone pulse.");
                    Float(p, "Cyclone Damage Increase Per Second Of Flight (%)", 50f, Number(cyclone, "damageBonusPerSecondOfFlight", 0.5f) * 100f, "Extra cyclone damage for each second spent flying.");
                    Float(p, "Cyclone Healing Increase Per Second Of Flight (%)", 1f, Number(cyclone, "healingBonusPerSecondOfFlight", 0.01f) * 100f, "Extra healing per pulse for each second spent flying.");
                    Float(p, "Cyclone Duration Per Tranquility Stack", 0.5f, Number(cyclone, "durationBonusPerStack", 0.25f), "Extra cyclone duration granted by each Tranquility stack.");
                    Int(p, "Max Instances Of AOE", -1, -1, "Maximum active cyclones per player. The oldest is removed when exceeded. -1 is unlimited.", -1);
                }
            }
            object palm = Component(p.Projectile, "PalmBlastProjectileController");
            object chargedPalm = Component(p.Charged, "PalmBlastProjectileController");
            object vehicleDamage = Component(p.Vehicle, i == 3 ? "RoR2.SojournVehicle" : "RoR2.ReprieveVehicle");
            float nativeDamage = i == 1 ? Number(sample, "fistDamageCoefficient", 1f) : i == 2 ? Number(sample, "dmgCoefficient", 1f) :
                i == 3 ? Number(vehicleDamage, "blastDamageCoefficient", 1f) : i == 4 ? Number(Component(p.Cyclone, "RoR2.SeekerCycloneController"), "damageCoefficient", 1f) : i == 6 ? Number(palm, "damageCoefficient", 3f) : Number(sample, "damageCoefficient", 1f);
            s.BindDamage(nativeDamage * 100f, i == 4 ? (float?)100f : i == 6 ? (float?)400f : null,
                i == 1 ? 1f : Number(sample, i == 5 ? "blastProcCoefficient" : "procCoefficient", 1f), i == 4);
            if (p.Projectile)
            {
                float speed = i == 2 ? Number(Component(p.Projectile, "RoR2.SoulSpiralProjectile"), "degreesPerSecond") * Number(Component(p.Projectile, "RoR2.SoulSpiralProjectile"), "radius") * Mathf.PI / 180f : i == 0 ? 72f : i == 6 ? Number(palm, "projectileSpeed", 60f) : ProjectileSpeed(p.Projectile);
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", speed, speed, "Projectile speed in metres per second.");
                s.NativeProjectileSpeed = speed;
                if (i == 6 && speed > 0f) p.ChargedSpeedRatio = Number(chargedPalm, "projectileSpeed", speed) / speed;
            }
            GameObject areaPrefab = i == 0 ? p.Finisher : i == 4 ? p.Cyclone : p.Projectile;
            ProjectileExplosion explosion = areaPrefab ? areaPrefab.GetComponentInChildren<ProjectileExplosion>(true) : null;
            float radius = i == 2 ? Number(Component(p.Projectile, "RoR2.SoulSpiralProjectile"), "radius") : i == 3 ? Number(vehicleDamage, "baseMaxBlastSize", 10f) : i == 4 ? Number(Component(p.Cyclone, "RoR2.SeekerCycloneController"), "allyHealingRadius", 8.4f) :
                i == 5 ? Number(sample, "blastRadius", 10f) : i == 6 ? Number(palm, "healRadius", 4f) : explosion ? explosion.blastRadius : Number(sample, "blastRadius");
            p.NativeRadius = radius;
            s.Radius = S.Float(s.Section, "AOE Radius", i == 0 ? 7f : i == 4 ? 20f : radius, radius, i == 0 ? "Explosion radius of the third punch in metres." : i == 2 ? "Orbit radius in metres." : i == 6 ? "Healing radius of Palm Blast in metres." : "Effect radius in metres.");
            S.AreaFalloff falloff = explosion ? (S.AreaFalloff)Enum.Parse(typeof(S.AreaFalloff), explosion.falloffModel.ToString()) : i == 5 ? S.AreaFalloff.Linear : S.AreaFalloff.None;
            if (i != 2 && i != 6) s.Area = C.Bind(s.Section, "AOE Damage Falloff", falloff, "Damage falloff from the centre of the effect. Vanilla: " + falloff + ".");
            if (i == 0)
            {
                Float(p, "3rd Hit Damage (%)", 350f, Number(sample, "comboDamageCoefficient", 3.5f) * 100f, "Third punch damage as a percentage of base damage.");
                Float(p, "3rd Hit Bonus Damage Per Tranquility Stack (%)", 50f, Number(sample, "dmgBuffIncrease", 0.2f) * 100f, "Extra third punch damage granted by each Tranquility stack.");
                p.I["Slow Power On Hit (%)"] = C.Bind(s.Section, "Slow Power On Hit (%)", 50, new ConfigDescription("Slow applied on hit. Vanilla: 50.", new AcceptableValueList<int>(30, 50, 60, 80)));
                Float(p, "Bonus Projectile Speed Per 100% Attack Speed", 2f, 2f, "Projectile speed gained per 100% additional attack speed, in metres per second.");
            }
            if (i == 1)
            {
                object healing = Component(p.Projectile, "RoR2.Projectile.UnseenHandHealingProjectile");
                Float(p, "Base Healing (%)", 3.5f, Number(healing, "fractionOfDamage") * 100f, "Damage dealt returned as healing.");
                Float(p, "Extra Healing Per Tranquility Stack (%)", 4f, Number(healing, "chakraIncrease") * 100f, "Extra damage returned as healing per Tranquility stack.");
            }
            if (i == 2)
            {
                object spiral = Component(p.Projectile, "RoR2.SoulSpiralProjectile");
                object overlap = Component(p.Projectile, "RoR2.Projectile.ProjectileOverlapLimitHits");
                object healing = Component(p.Projectile, "RoR2.Projectile.UnseenHandHealingProjectile");
                float nativeBoost = Number(spiral, "speedBoostCoefficient", 2f);
                Float(p, "Exhilarate Rotation Speed And Damage Bonus (%)", 66f, nativeBoost / (nativeBoost + 1f) * 100f, "Rotation speed and damage gained from each Exhilarate boost.");
                Int(p, "Base Hits", 11, (int)Number(overlap, "hitLimit", 11f), "Hits each spiral can deal before disappearing.", 1);
                Int(p, "Extra Hits Per Tranquility Stack", 3, (int)Number(spiral, "hitStackPerBuff", 1f), "Extra hits granted by each Tranquility stack.");
                Float(p, "Last Hit Damage Multiplier", 2f, Number(spiral, "lastHitDamageMultiplier", 2f), "Damage multiplier of the final hit.");
                Float(p, "Barrier On Hit (%)", 3f, Number(healing, "barrierPercent") * 100f, "Maximum health granted as barrier per hit.");
                Float(p, "Extra Barrier Per Tranquility Stack (%)", 1f, Number(healing, "chakraIncrease") * 100f, "Extra barrier per hit granted by each Tranquility stack.");
                Bool(p, "Scale With Max HP", true, false, "Scale barrier with current maximum health. Otherwise use level-one health.");
                float tick = Number(Component(p.Projectile, "RoR2.Projectile.ProjectileOverlapAttack"), "resetInterval", -1f);
                if (tick >= 0f) s.Interval = S.Float(s.Section, "Damage Interval", Math.Max(0.001f, tick), tick, "Minimum time between hits against the same target.", 0.001f);
                float lifetime = Number(p.Projectile ? p.Projectile.GetComponent<ProjectileSimple>() : null, "lifetime", -1f);
                s.Duration = S.Float(s.Section, "Duration", lifetime, lifetime, "Spiral lifetime in seconds. -1 lasts until all hits are used.", -1f);
                object manager = Read(Component(prefab.gameObject, "RoR2.SeekerController"), "soulSpiralManager");
                int limit = (int)Number(manager, "soulSpiralLimit", 6f);
                Int(p, "Max Instances Of AOE", limit, limit, "Maximum active spirals per player. Recasting at the limit triggers Exhilarate. -1 is unlimited.", -1);
            }
            if (i == 4) s.Interval = S.Float(s.Section, "Cyclone Interval", 0.5f, Number(Component(p.Cyclone, "RoR2.SeekerCycloneController"), "allyHealingFrequency", 0.5f), "Seconds between cyclone damage and healing pulses.", 0.001f);
            if (i == 5)
            {
                Float(p, "Saving Grace Stat Bonus (%)", 7f, 7f, "Stat bonus granted by Saving Grace.");
                Float(p, "Ally Healing (%)", 10f, 4f, "Allies' maximum health restored by a successful meditation.");
                Float(p, "Barrier On Cast (%)", 25f, Number(sample, "startingOverShieldPercentage", 0.15f) * 100f, "Maximum health granted as barrier when meditation begins.");
                Float(p, "Damage Bonus Per Tranquility Stack (%)", 200f, Number(sample, "percentPerGate", 0.2f) * 100f, "Extra damage granted by each Tranquility stack.");
                Float(p, "Healing Bonus Per Tranquility Stack (%)", 5f, Number(sample, "healingExplosionAmount", 0.01f) * 100f, "Extra healing granted by each Tranquility stack.");
                Bool(p, "Multiply Radius Per Tranquility Stack", true, true, "Scale the healing and damage radius with Tranquility stacks.");
                s.Duration = S.Float(s.Section, "Duration", Number(sample, "duration", 10f), Number(sample, "duration", 10f), "Time available to complete the meditation.", 0.001f);
            }
            if (i == 6)
            {
                Bool(p, "Grants Saving Grace At Max Tranquility", false, false, "Reaching maximum Tranquility through Palm Blast grants Saving Grace and revives fallen allies.");
                Int(p, "Multihit Threshold", 3, 3, "Targets hit to gain a Tranquility stack and barrier.", 1);
                Float(p, "Barrier On Multihit (%)", 20f, Number(palm, "barrierPercent", 0.25f) * 100f, "Maximum health granted as barrier on a multihit.");
                Bool(p, "Barrier Based On Actual Max HP", true, true, "Scale multihit barrier with current maximum health. Otherwise use level-one health.");
                Bool(p, "Teleporter Boss Instantly Grants Tranquility", true, true, "Hitting the active teleporter boss immediately grants a Tranquility stack.");
                Float(p, "Base Healing (%)", 10f, Number(palm, "allyHealingPercent", 0.03f) * 100f, "Allies' maximum health restored by an uncharged blast.");
                Float(p, "Base Size (%)", 100f, Number(palm, "projectileSize", 1f) * 100f, "Size of an uncharged blast.");
                Float(p, "Charged Damage (%)", 700f, Number(chargedPalm, "damageCoefficient", 7f) * 100f, "Fully charged damage as a percentage of base damage.");
                Float(p, "Charged Healing (%)", 15f, Number(chargedPalm, "allyHealingPercent", 0.15f) * 100f, "Allies' maximum health restored by a fully charged blast.");
                Float(p, "Charged Size (%)", 150f, Number(chargedPalm, "projectileSize", 1.5f) * 100f, "Size of a fully charged blast.");
                Float(p, "Size Growth Per Tranquility (%)", 15f, Number(palm, "bonusProjectileSizePerStack", 0.15f) * 100f, "Extra blast size granted by each Tranquility stack.");
                Float(p, "Damage Bonus Per Tranquility (%)", 250f, Number(palm, "bonusDamageCoefficientPerStack", 3.5f) * 100f, "Extra damage granted by each Tranquility stack.");
                Float(p, "Healing Bonus Per Tranquility (%)", 5f, Number(palm, "bonusAllyHealingPercentPerStack", 0.06f) * 100f, "Extra healing granted by each Tranquility stack.");
                Bool(p, "Excess Healing Is Converted To Barrier", true, true, "Convert healing beyond an ally's missing health into barrier.");
            }
        }
        private static float ProjectileSpeed(GameObject obj)
        {
            foreach (Component component in obj.GetComponents<Component>())
            {
                object value = Read(component, "desiredForwardSpeed") ?? Read(component, "projectileSpeed") ?? Read(component, "velocity");
                if (value is float number && number >= 0f) return number;
            }
            return 0f;
        }
        private static void InstallHooks()
        {
            if (stats != null) Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody));
            if (passiveEnabled.Value) Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterStats));
            if (profiles.Count > 0)
            {
                Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillTick));
                Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                Patch(typeof(ProjectileController), "Start", postfix: nameof(AfterProjectileStart));
                Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), nameof(AfterDamage));
                Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
            }
            if (profiles.ContainsKey(1) || profiles.ContainsKey(2))
            {
                Patch(GameType("RoR2.Projectile.UnseenHandHealingProjectile"), "OnDamageInflictedServer", nameof(BeforeHealingProjectile), finalizer: nameof(EndScope), transpiler: nameof(SpiralHealthIL));
            }
            if (profiles.ContainsKey(2))
            {
                Patch(GameType("RoR2.SoulSpiralProjectile"), "AssignSpiralOwner", nameof(BeforeSpiralOwner), nameof(AfterSpiralOwner));
                Patch(GameType("RoR2.SoulSpiralOrbiter"), "UpdateCurrentSpeedAndDamage", nameof(BeforeSpiralOrbit));
                Patch(GameType("RoR2.SeekerSoulSpiralManager"), "CheckOurSpiralBudget", nameof(BeforeSpiralBudget));
            }
            if (profiles.ContainsKey(3) || profiles.ContainsKey(4))
            {
                Patch(GameType("RoR2.SojournVehicleBase"), "OnPassengerEnter", nameof(BeforeVehicleEnter), nameof(AfterVehicleEnter));
                Patch(GameType("RoR2.SojournVehicleBase"), "FixedUpdate", nameof(BeforeVehicleTick));
                Patch(GameType("RoR2.SojournVehicleBase"), "FixedUpdateMovement", nameof(BeforeVehicleMovement));
            }
            if (profiles.ContainsKey(3)) Patch(typeof(HealthComponent), "Heal", transpiler: nameof(SojournHealingIL), args: new[] { typeof(float), typeof(ProcChainMask), typeof(bool) });
            if (profiles.ContainsKey(4)) Patch(GameType("RoR2.SeekerCycloneController"), "HandleChargeBonuses", nameof(BeforeCyclone), nameof(AfterCyclone));
            if (profiles.ContainsKey(5))
            {
                Patch(State("Meditate"), "Update", transpiler: nameof(MeditateHealingIL));
                Patch(State("Meditate"), "CalculateBlastEffectScale", postfix: nameof(AfterMeditateScale));
                Patch(typeof(CharacterBody), "RecalculateStats", transpiler: nameof(SavingGraceIL));
            }
            if (passiveEnabled.Value || profiles.ContainsKey(5)) Patch(State("Meditate"), "Update", transpiler: nameof(StackLimitIL));
            if (profiles.ContainsKey(6))
            {
                Patch(GameType("PalmBlastProjectileController"), "Init", nameof(BeforePalmInit));
                Patch(GameType("PalmBlastProjectileController"), "IncreaseCharactersHit", nameof(BeforePalmCount));
                Patch(GameType("PalmBlastProjectileController"), "ApplyDamageToEnemy", nameof(BeforePalmEnemy), finalizer: nameof(AfterPalmEnemy));
                Patch(GameType("PalmBlastProjectileController"), "ApplyHealingToAlly", nameof(BeforePalmHealing), finalizer: nameof(EndScope), transpiler: nameof(PalmOverhealIL));
                Patch(GameType("RoR2.SeekerController"), "UnlockPalmBlastEffects", postfix: nameof(AfterPalmUnlock));
            }
        }
        private static void Apply()
        {
            stats?.Apply(prefab);
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (HasTranquilityBonus(p))
                {
                    // Replace only this skill's Tranquility tooltip; keep its other keywords.
                    p.Skill.Definition.keywordTokens = (p.Skill.Definition.keywordTokens ?? Array.Empty<string>())
                        .Where(token => !IsTranquilityKeyword(token)).ToArray();
                    p.Skill.SetKeyword(TranquilityToken(p), true);
                }
                if (p.Index == 5 || p.Index == 6 && B(p, "Grants Saving Grace At Max Tranquility")) p.Skill.SetKeyword("KEYWORD_3QS_SEEKER_SAVING_GRACE", true);
                if (p.Index == 2)
                {
                    p.Skill.Definition.keywordTokens = (p.Skill.Definition.keywordTokens ?? Array.Empty<string>())
                        .Where(token => !IsKeyword(token, "EXHILARATE", "Exhilarate")).ToArray();
                    p.Skill.SetKeyword("KEYWORD_3QS_SEEKER_EXHILARATE", true);
                }
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (IsSeeker(body)) { stats?.Apply(body); body.MarkAllStatsDirty(); }
        }
        private static void AfterBody(CharacterBody __instance) { if (IsSeeker(__instance)) stats?.Apply(__instance); }
        private static void AfterStats(CharacterBody __instance)
        {
            if (!IsSeeker(__instance)) return;
            float bonus = Stacks(__instance) * C.Get(statBonus) / 100f, factor = 1f + bonus;
            foreach (string stat in new[] { "maxHealth", "maxShield", "regen", "damage", "moveSpeed", "attackSpeed", "armor" }) Write(__instance, stat, Number(__instance, stat) * factor);
            Write(__instance, "crit", __instance.crit + bonus * 100f);
        }
        private static string PassiveDescription() => "Gain <style=cIsUtility>Tranquility</style> to empower your skills" +
            (statBonus != null && C.Get(statBonus) > 0f ? " and increase your stats by <style=cIsUtility>" + S.Number(C.Get(statBonus)) + "%</style> per stack" : "") + ". Stacks up to <style=cIsUtility>" + StackLimit + "</style> times.";
        private static Profile ForState(EntityState state)
        {
            if (state == null) return null;
            Type type = state.GetType();
            foreach (Profile p in profiles.Values)
                if (p.Skill.StateType.IsAssignableFrom(type) || p.Index == 6 && State("BaseFirePalmBlast").IsAssignableFrom(type)) return p;
            return null;
        }
        private static void Set(Scope scope, object obj, string name, object value)
        {
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(field.IsStatic ? null : obj, value); return;
            }
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState, PreviousHealing = activeHealing };
            Profile p = ForState(__instance); if (p == null || !IsSeeker(Body(__instance))) return;
            activeState = __instance;
            if (p.Index == 0)
            {
                Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
                Set(__state, __instance, "comboDamageCoefficient", F(p, "3rd Hit Damage (%)") / 100f);
                Set(__state, __instance, "dmgBuffIncrease", F(p, "3rd Hit Bonus Damage Per Tranquility Stack (%)") / 100f);
            }
            if (p.Index == 1) Set(__state, __instance, "fistDamageCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Index == 2) Set(__state, __instance, "dmgCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Index == 4)
            {
                Set(__state, __instance, "barrierOnReprieveEnter", F(p, "Barrier Gained (%)") / 100f);
                Set(__state, __instance, "barrierTranquilityGrowth", F(p, "Extra Barrier Per Tranquility Stack (%)") / 100f);
            }
            if (p.Index == 5)
            {
                Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
                Set(__state, __instance, "percentPerGate", F(p, "Damage Bonus Per Tranquility Stack (%)") / 100f);
                Set(__state, __instance, "healingExplosionAmount", F(p, "Healing Bonus Per Tranquility Stack (%)") / 100f);
                Set(__state, __instance, "startingOverShieldPercentage", F(p, "Barrier On Cast (%)") / 100f);
                Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius));
                Set(__state, __instance, "blastProcCoefficient", C.Get(p.Skill.Proc));
                Set(__state, __instance, "duration", C.Get(p.Skill.Duration));
            }
            if (p.Index == 6 && State("BaseChargePalmBlast").IsAssignableFrom(__instance.GetType())) Set(__state, __instance, "baseChargeDuration", F(p, "Charge Time"));
            if (p.Index == 7) { Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f); Set(__state, __instance, "blastRadius", C.Get(p.Skill.Radius)); }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null)
            {
                for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value);
                activeState = __state.Previous; activeHealing = __state.PreviousHealing;
            }
            return __exception;
        }
        private static bool Matches(Profile p, GameObject source, ProjectileController instance = null)
        {
            foreach (GameObject obj in new[] { p.Projectile, p.Finisher, p.Charged, p.Cyclone })
            {
                if (!obj) continue;
                if (source) { if (source == obj) return true; continue; }
                ProjectileController original = obj.GetComponent<ProjectileController>();
                if (instance && (original && original.catalogIndex >= 0 && original.catalogIndex == instance.catalogIndex || instance.gameObject.name == obj.name + "(Clone)")) return true;
            }
            return false;
        }
        private static Profile ProjectileProfile(ProjectileController pc, GameObject source = null)
        {
            if (!pc || !pc.owner || !IsSeeker(pc.owner.GetComponent<CharacterBody>())) return null;
            return profiles.Values.FirstOrDefault(p => Matches(p, source, pc));
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            CharacterBody body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            if (!IsSeeker(body)) return;
            GameObject source = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(profile => Matches(profile, source)); if (p == null) return;
            if (p.Skill.ProjectileSpeed != null)
                fireProjectileInfo.speedOverride = Math.Max(0f, C.Get(p.Skill.ProjectileSpeed) + (p.Index == 0 ? (body.attackSpeed - 1f) * F(p, "Bonus Projectile Speed Per 100% Attack Speed") : 0f));
            if (p.Index == 0)
            {
                bool finisher = fireProjectileInfo.projectilePrefab == p.Finisher;
                fireProjectileInfo.damage = body.damage * (finisher ? F(p, "3rd Hit Damage (%)") + Stacks(body) * F(p, "3rd Hit Bonus Damage Per Tranquility Stack (%)") : C.Get(p.Skill.Damage)) / 100f;
            }
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController, FireProjectileInfo fireProjectileInfo) => ConfigureProjectile(projectileController, fireProjectileInfo.projectilePrefab);
        private static void AfterProjectileStart(ProjectileController __instance) => ConfigureProjectile(__instance);
        private static void ConfigureProjectile(ProjectileController pc, GameObject source = null)
        {
            if (!pc || pc.GetComponent<Shot>()) return;
            Profile p = ProjectileProfile(pc, source); if (p == null) return;
            Shot marker = pc.gameObject.AddComponent<Shot>(); marker.Profile = p; marker.Origin = pc.transform.position;
            marker.Finisher = p.Finisher && (source ? source == p.Finisher : pc.gameObject.name == p.Finisher.name + "(Clone)");
            pc.procCoefficient = C.Get(p.Skill.Proc);
            var explosion = pc.GetComponentInChildren<ProjectileExplosion>(true);
            if (explosion && (p.Index != 0 || marker.Finisher)) { explosion.blastRadius = C.Get(p.Skill.Radius); explosion.falloffModel = Area(p); }
            object healing = Component(pc.gameObject, "RoR2.Projectile.UnseenHandHealingProjectile");
            if (healing != null && p.Index == 1)
            { Write(healing, "fractionOfDamage", F(p, "Base Healing (%)") / 100f); Write(healing, "chakraIncrease", F(p, "Extra Healing Per Tranquility Stack (%)") / 100f); }
        }
        private static BlastAttack.FalloffModel Area(Profile p) => p.Skill.Area == null ? BlastAttack.FalloffModel.None : (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), C.Get(p.Skill.Area).ToString());
        private static BuffDef Slow(int amount) => amount == 30 ? JunkContent.Buffs.Slow30 : amount == 60 ? RoR2Content.Buffs.Slow60 : amount == 80 ? RoR2Content.Buffs.Slow80 : RoR2Content.Buffs.Slow50;
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            Shot marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<Shot>() : null;
            Profile p = marker ? marker.Profile : ForState(activeState);
            CharacterBody caster = Body(activeState);
            if (p == null || !marker && (!IsSeeker(caster) || damageInfo.attacker != caster.gameObject)) return;
            if (marker && p.Index == 0 && (marker.Finisher || (damageInfo.damageType & DamageType.SlowOnHit) != DamageType.Generic))
            {
                damageInfo.damageType &= ~DamageType.SlowOnHit;
                slowHits.Remove(damageInfo); slowHits.Add(damageInfo, marker);
            }
            damageInfo.damage *= Falloff(C.Get(p.Skill.Falloff), Vector3.Distance(marker ? marker.Origin : caster.corePosition, damageInfo.position));
            if (marker && p.Index == 4) damageInfo.damage *= AreaFactor(C.Get(p.Skill.Area), Vector3.Distance(marker.transform.position, damageInfo.position), C.Get(p.Skill.Radius));
        }
        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (NetworkServer.active && !damageInfo.rejected && __instance.alive && __instance.body && slowHits.TryGetValue(damageInfo, out Shot shot)) __instance.body.AddTimedBuff(Slow(I(shot.Profile, "Slow Power On Hit (%)")), 2f);
        }
        internal static float Falloff(S.BulletFalloff type, float distance) => type == S.BulletFalloff.Default ? 0.5f + Mathf.Clamp01((60f - distance) / 35f) * 0.5f : type == S.BulletFalloff.Buckshot ? 0.3f + Mathf.Clamp01((40f - distance) / 28f) * 0.7f : 1f;
        internal static float AreaFactor(S.AreaFalloff type, float distance, float radius)
        {
            float ratio = Mathf.Clamp01(distance / Math.Max(0.001f, radius));
            if (type == S.AreaFalloff.Linear) return 1f - ratio;
            if (type == S.AreaFalloff.HalfLinear) return 1f - ratio * 0.5f;
            if (type == S.AreaFalloff.QuarterLinear) return 1f - ratio * 0.75f;
            if (type == S.AreaFalloff.SweetSpot) return distance > radius / 2f ? 0.25f : 1f;
            return 1f;
        }
        private static void BeforeHealingProjectile(Component __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState, PreviousHealing = activeHealing };
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            Profile p = ProjectileProfile(pc); activeHealing = p;
            if (p?.Index == 1) { Write(__instance, "fractionOfDamage", F(p, "Base Healing (%)") / 100f); Write(__instance, "chakraIncrease", F(p, "Extra Healing Per Tranquility Stack (%)") / 100f); }
            if (p?.Index == 2) { Write(__instance, "barrierPercent", F(p, "Barrier On Hit (%)") / 100f); Write(__instance, "chakraIncrease", F(p, "Extra Barrier Per Tranquility Stack (%)") / 100f); }
        }
        private static float SpiralHealth(CharacterBody body) => activeHealing?.Index == 2 && B(activeHealing, "Scale With Max HP") ? body.maxHealth : body.baseMaxHealth;
        private static IEnumerable<CodeInstruction> SpiralHealthIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldfld && instruction.operand is FieldInfo field && field.DeclaringType == typeof(CharacterBody) && field.Name == "baseMaxHealth")
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Seeker), nameof(SpiralHealth)); }
                yield return instruction;
            }
        }
        private static void BeforeSpiralOwner(Component __instance)
        {
            if (!profiles.TryGetValue(2, out Profile p)) return;
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            if (!pc || !pc.owner || !IsSeeker(pc.owner.GetComponent<CharacterBody>())) return;
            Write(__instance, "baseHitLimit", I(p, "Base Hits"));
            Write(__instance, "hitStackPerBuff", I(p, "Extra Hits Per Tranquility Stack"));
            Write(__instance, "lastHitDamageMultiplier", F(p, "Last Hit Damage Multiplier"));
            Write(__instance, "radius", C.Get(p.Skill.Radius));
        }
        private static void AfterSpiralOwner(Component __instance)
        {
            if (!profiles.TryGetValue(2, out Profile p)) return;
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            CharacterBody body = pc && pc.owner ? pc.owner.GetComponent<CharacterBody>() : null; if (!IsSeeker(body)) return;
            Write(Component(__instance.gameObject, "RoR2.Projectile.ProjectileOverlapLimitHits"), "hitLimit", I(p, "Base Hits") + Stacks(body) * I(p, "Extra Hits Per Tranquility Stack"));
            ProjectileOverlapAttack overlap = __instance.GetComponent<ProjectileOverlapAttack>();
            if (overlap) { if (p.Skill.Interval != null) overlap.resetInterval = C.Get(p.Skill.Interval); overlap.overlapProcCoefficient = C.Get(p.Skill.Proc); }
            ProjectileSimple mover = pc.GetComponent<ProjectileSimple>();
            if (mover) mover.lifetime = C.Get(p.Skill.Duration) < 0f ? float.PositiveInfinity : C.Get(p.Skill.Duration);
            ConfigureProjectile(pc);
        }
        private static bool BeforeSpiralOrbit(object __instance, float _currentTime)
        {
            Component controller = Read(__instance, "seekerController") as Component;
            if (!controller || !IsSeeker(controller.GetComponent<CharacterBody>()) || !profiles.TryGetValue(2, out Profile p)) return true;
            float elapsed = _currentTime - Number(__instance, "boostChangedTimeStamp");
            object[] args = { _currentTime, elapsed };
            AccessTools.Method(__instance.GetType(), "HandleBoostExpiring").Invoke(__instance, args); elapsed = (float)args[1];
            float delta = _currentTime - Number(__instance, "prevTimeStamp");
            float bonus = Number(__instance, "boostCount") * F(p, "Exhilarate Rotation Speed And Damage Bonus (%)") / 100f;
            float baseSpeed = C.Get(p.Skill.ProjectileSpeed) / Math.Max(0.001f, C.Get(p.Skill.Radius)) * 180f / Mathf.PI;
            Write(__instance, "baseDegreesPerSecond", baseSpeed);
            float target = baseSpeed * bonus;
            float additional = Mathf.Lerp(Number(__instance, "currentAdditionalDegreesPerSecond"), target, elapsed / Math.Max(0.001f, Number(__instance, "boostEaseDuration")));
            Write(__instance, "targetAdditionalDegreesPerSecond", target); Write(__instance, "currentAdditionalDegreesPerSecond", additional);
            Write(__instance, "degreesRotated", Number(__instance, "degreesRotated") + delta * (Number(__instance, "baseDegreesPerSecond") + additional));
            Write(__instance, "currentDamageMultiplier", Number(__instance, "baseProjectileDamage", 1f) * (1f + bonus));
            Write(__instance, "prevTimeStamp", _currentTime); return false;
        }
        private static Profile VehicleProfile(Component vehicle, CharacterBody body = null)
        {
            body = body ?? Read(vehicle, "characterBody") as CharacterBody;
            if (!IsSeeker(body)) return null;
            int index = vehicle.GetType().Name == "ReprieveVehicle" ? 4 : 3;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void BeforeVehicleEnter(Component __instance, GameObject passenger)
        {
            Profile p = VehicleProfile(__instance, passenger ? passenger.GetComponent<CharacterBody>() : null); if (p == null) return;
            Write(__instance, "blastDamageCoefficient", C.Get(p.Skill.Damage) / 100f); Write(__instance, "blastProcCoefficient", C.Get(p.Skill.Proc)); Write(__instance, "blastFalloffModel", Area(p));
            if (p.Index == 3)
            {
                Write(__instance, "baseMaxBlastSize", C.Get(p.Skill.Radius));
                Write(__instance, "afterGraceLerpDownTime", F(p, "Flight Acceleration Time"));
                Write(__instance, "incrementalFlightDamage", F(p, "Health Cost Per Tick (%)") / 100f);
                float tick = F(p, "Health Cost Interval");
                Write(__instance, "agePerTenthSecondsCheck", tick); Write(__instance, "increaseAgeCheck", tick);
                Write(__instance, "thresholdToStartExtraFlightDamage", F(p, "Health Cost Increase Delay") / tick);
                Write(__instance, "delayedSecondsToIncreaseFlightDamage", F(p, "Health Cost Increase Interval") / tick);
                Write(__instance, "delayedIncreasedFlightDamage", F(p, "Health Cost Increase (%)") / 100f);
            }
            else
            {
                Write(__instance, "speedIncreasePercent", F(p, "Flight Acceleration (%)") / 100f);
                Write(__instance, "speedIncreaseFrenquency", F(p, "Flight Acceleration Interval"));
                Write(__instance, "turnSpeedCoeficient", Mathf.Clamp01(F(p, "Turn Speed (%)") / 100f));
            }
        }
        private static void AfterVehicleEnter(Component __instance, GameObject passenger)
        {
            Profile p = VehicleProfile(__instance); if (p == null) return;
            Write(__instance, "overlapProcCoefficient", C.Get(p.Skill.Proc));
            OverlapAttack overlap = Read(__instance, "overlapAttack") as OverlapAttack; if (overlap != null) overlap.procCoefficient = C.Get(p.Skill.Proc);
            Shot shot = __instance.gameObject.GetComponent<Shot>() ?? __instance.gameObject.AddComponent<Shot>(); shot.Profile = p; shot.Origin = __instance.transform.position;
        }
        private static void BeforeVehicleTick(Component __instance)
        {
            Profile p = VehicleProfile(__instance); if (p == null) return;
            float duration = C.Get(p.Skill.Duration);
            if (NetworkServer.active && duration >= 0f && Number(__instance, "age") >= duration)
            {
                object seat = Read(__instance, "vehicleSeat");
                AccessTools.Method(seat.GetType(), "CallCmdEjectPassenger").Invoke(seat, null);
            }
        }
        private static bool BeforeVehicleMovement(Component __instance)
        {
            Profile p = VehicleProfile(__instance); if (p == null || p.Index != 3 || F(p, "Turn Speed (%)") >= 100f) return true;
            object seat = Read(__instance, "vehicleSeat"); InputBankTest input = Read(seat, "currentPassengerInputBank") as InputBankTest;
            Rigidbody rigidbody = Read(__instance, "vehicleRigidbody") as Rigidbody; if (!input || !rigidbody) return true;
            Ray ray = CameraRigController.ModifyAimRayIfApplicable(input.GetAimRay(), __instance.gameObject, out float extraDistance);
            Vector3 direction = Vector3.Slerp(__instance.transform.forward, ray.direction, Mathf.Clamp01(F(p, "Turn Speed (%)") / 100f));
            rigidbody.MoveRotation(Quaternion.LookRotation(direction)); rigidbody.velocity = direction * Number(__instance, "currentSpeed"); return false;
        }
        private static void BeforeCyclone(Component __instance)
        {
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            if (!pc || !pc.owner || !IsSeeker(pc.owner.GetComponent<CharacterBody>()) || !profiles.TryGetValue(4, out Profile p)) return;
            Write(__instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            Write(__instance, "damageBonusPerSecondOfFlight", F(p, "Cyclone Damage Increase Per Second Of Flight (%)") / 100f);
            Write(__instance, "baseLifetime", F(p, "Cyclone Duration")); Write(__instance, "durationBonusPerStack", F(p, "Cyclone Duration Per Tranquility Stack"));
            Write(__instance, "allyHealingRadius", C.Get(p.Skill.Radius)); Write(__instance, "allyHealingPercent", F(p, "Cyclone Healing (%)") / 100f);
            Write(__instance, "allyHealingFrequency", C.Get(p.Skill.Interval)); Write(__instance, "allyHealingRefreshTimer", C.Get(p.Skill.Interval));
            Write(__instance, "healingBonusPerSecondOfFlight", F(p, "Cyclone Healing Increase Per Second Of Flight (%)") / 100f);
            ProjectileOverlapAttack overlap = __instance.GetComponent<ProjectileOverlapAttack>();
            if (overlap) { overlap.resetInterval = C.Get(p.Skill.Interval); overlap.overlapProcCoefficient = C.Get(p.Skill.Proc); }
        }
        private static void AfterCyclone(Component __instance)
        {
            ProjectileController pc = __instance.GetComponent<ProjectileController>();
            Profile p = ProjectileProfile(pc); if (p?.Index != 4) return;
            ConfigureProjectile(pc);
            Shot shot = pc.GetComponent<Shot>(); if (shot && shot.Finisher) return;
            if (shot) shot.Finisher = true;
            if (p.NativeRadius > 0f) __instance.transform.localScale *= C.Get(p.Skill.Radius) / p.NativeRadius;
            CharacterBody body = pc.owner.GetComponent<CharacterBody>();
            SpawnList list = cyclones.GetOrCreateValue(body); list.Objects.RemoveAll(g => !g); list.Objects.Add(pc.gameObject);
            int maximum = I(p, "Max Instances Of AOE");
            if (NetworkServer.active && maximum >= 0) while (list.Objects.Count > maximum) { UnityEngine.Object.Destroy(list.Objects[0]); list.Objects.RemoveAt(0); }
        }
        private static bool BeforeSpiralBudget(object __instance, ref bool __result)
        {
            CharacterBody body = Read(__instance, "seekerBody") as CharacterBody;
            if (!IsSeeker(body) || !profiles.TryGetValue(2, out Profile p)) return true;
            Array array = Read(__instance, "soulSpiralArray") as Array;
            if (array == null) return true;
            int limit = (int)Number(__instance, "soulSpiralLimit", 6f);
            int count = Math.Max(0, (int)Number(__instance, "spiralsRequested"));
            // Match the native budget: pending spawns plus live authoritative slots.
            // The second half of the array is reserved for predicted projectiles.
            for (int i = 0; i < Math.Min(limit, array.Length); i++)
                if (array.GetValue(i) is UnityEngine.Object spiral && spiral) count++;
            int maximum = I(p, "Max Instances Of AOE");
            int required = maximum < 0 ? count + 1 : maximum;
            limit = Math.Max(limit, required);
            if (array.Length < limit * 2)
            {
                Array expanded = Array.CreateInstance(array.GetType().GetElementType(), limit * 2);
                Array.Copy(array, expanded, array.Length); Write(__instance, "soulSpiralArray", expanded);
            }
            Write(__instance, "soulSpiralLimit", limit);
            // FireSoulSpiral calls vanilla BoostSpirals when no spawn slot remains.
            __result = maximum < 0 || count < maximum;
            return false;
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(activeState); CharacterBody body = Body(activeState);
            if (p == null || !IsSeeker(body) || __instance.attacker != body.gameObject) return;
            __instance.falloffModel = Area(p); __instance.procCoefficient = C.Get(p.Skill.Proc);
        }
        private static void BeforePalmInit(Component __instance, CharacterBody body)
        {
            if (!IsSeeker(body) || !profiles.TryGetValue(6, out Profile p)) return;
            bool charged = p.Charged && __instance.gameObject.name == p.Charged.name + "(Clone)";
            Write(__instance, "damageCoefficient", (charged ? F(p, "Charged Damage (%)") : C.Get(p.Skill.Damage)) / 100f);
            Write(__instance, "allyHealingPercent", F(p, charged ? "Charged Healing (%)" : "Base Healing (%)") / 100f);
            Write(__instance, "projectileSize", F(p, charged ? "Charged Size (%)" : "Base Size (%)") / 100f);
            Write(__instance, "bonusProjectileSizePerStack", F(p, "Size Growth Per Tranquility (%)") / 100f);
            Write(__instance, "bonusDamageCoefficientPerStack", F(p, "Damage Bonus Per Tranquility (%)") / 100f);
            Write(__instance, "bonusAllyHealingPercentPerStack", F(p, "Healing Bonus Per Tranquility (%)") / 100f);
            Write(__instance, "barrierPercent", F(p, "Barrier On Multihit (%)") / 100f);
            Write(__instance, "healRadius", C.Get(p.Skill.Radius)); Write(__instance, "procCoefficient", C.Get(p.Skill.Proc));
            Write(__instance, "projectileSpeed", C.Get(p.Skill.ProjectileSpeed) * (charged ? p.ChargedSpeedRatio : 1f));
            Shot shot = __instance.gameObject.GetComponent<Shot>() ?? __instance.gameObject.AddComponent<Shot>(); shot.Profile = p; shot.Origin = __instance.transform.position;
        }
        [ThreadStatic] private static CharacterBody palmEnemy;
        private static void BeforePalmEnemy(Component __instance, GameObject overlappedObject, out CharacterBody __state)
        { __state = palmEnemy; palmEnemy = overlappedObject ? overlappedObject.GetComponent<CharacterBody>() : null; }
        private static Exception AfterPalmEnemy(CharacterBody __state, Exception __exception) { palmEnemy = __state; return __exception; }
        private static bool IsTeleporterBoss(CharacterBody body)
        {
            if (!body || !body.master || !TeleporterInteraction.instance) return false;
            object boss = Read(TeleporterInteraction.instance, "bossGroup");
            object squad = Read(boss, "combatSquad");
            var members = Read(squad, "membersList") as IEnumerable<CharacterMaster>;
            return members != null && members.Contains(body.master);
        }
        private static bool BeforePalmCount(Component __instance, bool isSpecialEnemy, ref bool __result)
        {
            CharacterBody body = Read(__instance, "ownerBody") as CharacterBody;
            if (!IsSeeker(body) || !profiles.TryGetValue(6, out Profile p)) return true;
            bool instant = isSpecialEnemy && B(p, "Teleporter Boss Instantly Grants Tranquility") && IsTeleporterBoss(palmEnemy);
            int count = (int)Number(__instance, "charactersHit");
            if (!instant) count++;
            bool multi = !instant && count >= I(p, "Multihit Threshold");
            __result = instant || multi;
            if (__result)
            {
                object controller = Read(__instance, "seekerController");
                AccessTools.Method(controller.GetType(), "CallCmdIncrementChakraGate").Invoke(controller, null);
                if (multi)
                {
                    count = 0;
                    float health = B(p, "Barrier Based On Actual Max HP") ? body.maxHealth : body.baseMaxHealth;
                    body.healthComponent.AddBarrierAuthority(health * F(p, "Barrier On Multihit (%)") / 100f);
                }
            }
            Write(__instance, "charactersHit", count);
            Write(Read(__instance, "projectileSimple"), "desiredForwardSpeed", 0f);
            Write(__instance, "hitDelayTimer", Number(__instance, "hitDelay")); return false;
        }
        private static void BeforePalmHealing(Component __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState, PreviousHealing = activeHealing };
            CharacterBody body = Read(__instance, "ownerBody") as CharacterBody;
            activeHealing = IsSeeker(body) && profiles.TryGetValue(6, out Profile p) ? p : null;
        }
        private static bool BeforeOverhealBarrier() => activeHealing?.Index != 6 || B(activeHealing, "Excess Healing Is Converted To Barrier");
        private static void AddPalmOverheal(HealthComponent health, float amount)
        { if (BeforeOverhealBarrier()) health.AddBarrier(amount); }
        private static IEnumerable<CodeInstruction> PalmOverhealIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Call) && instruction.operand is MethodInfo method && method.DeclaringType == typeof(HealthComponent) && method.Name == "AddBarrier")
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Seeker), nameof(AddPalmOverheal)); }
                yield return instruction;
            }
        }
        private static void AfterPalmUnlock(Component __instance, byte chakraGate)
        {
            if (!NetworkServer.active || !profiles.TryGetValue(6, out Profile p) || !B(p, "Grants Saving Grace At Max Tranquility") || chakraGate < StackLimit) return;
            AccessTools.Method(__instance.GetType(), "UnlockGateEffects").Invoke(__instance, new object[] { chakraGate });
        }
        private static void BeforePetals(ref byte value)
        {
            if (StackLimit != 7) value = (byte)Mathf.Clamp(Mathf.CeilToInt(value * 7f / StackLimit), 0, 7);
        }
        private static int MaximumStacks() => StackLimit;
        private static IEnumerable<CodeInstruction> StackLimitIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsConstant(7))
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Seeker), nameof(MaximumStacks)); }
                yield return instruction;
            }
        }
        private static float MeditationBaseHealing() => profiles.TryGetValue(5, out Profile p) ? F(p, "Ally Healing (%)") / 100f : 0.04f;
        private static IEnumerable<CodeInstruction> MeditateHealingIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value - 0.04f) < 0.00001f)
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Seeker), nameof(MeditationBaseHealing)); }
                yield return instruction;
            }
        }
        private static void AfterMeditateScale(EntityState __instance, int chakraAmount, ref float __result)
        { if (profiles.TryGetValue(5, out Profile p) && IsSeeker(Body(__instance))) __result = B(p, "Multiply Radius Per Tranquility Stack") ? Math.Max(1, chakraAmount) : 1f; }
        private static float HealingPenalty(float original, HealthComponent health) => profiles.TryGetValue(3, out Profile p) && IsSeeker(health.body) ? F(p, "Healing Reduction Per Second (%)") / 100f : original;
        private static IEnumerable<CodeInstruction> SojournHealingIL(IEnumerable<CodeInstruction> instructions)
        {
            int remaining = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.Name == "SojournHealing") remaining = 24;
                else if (remaining > 0) remaining--;
                yield return instruction;
                if (remaining > 0 && instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value - 0.1f) < 0.00001f)
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Seeker), nameof(HealingPenalty))); remaining = 0; }
            }
        }
        private static int LocalIndex(CodeInstruction instruction, bool store)
        {
            OpCode op = instruction.opcode;
            if (op == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0)) return 0;
            if (op == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1)) return 1;
            if (op == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2)) return 2;
            if (op == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3)) return 3;
            if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
            return instruction.operand is LocalBuilder builder ? builder.LocalIndex : Convert.ToInt32(instruction.operand);
        }
        private static float SavingGraceBonus(float original) => profiles.TryGetValue(5, out Profile p) ? F(p, "Saving Grace Stat Bonus (%)") / (original > 1f ? 1f : 100f) : original;
        private static IEnumerable<CodeInstruction> SavingGraceIL(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList(); int local = -1;
            for (int i = 0; i < code.Count; i++)
                if (code[i].opcode == OpCodes.Ldsfld && code[i].operand is FieldInfo field && field.Name == "RevitalizeBuff")
                    for (int j = i + 1; j < Math.Min(code.Count, i + 7); j++) { int found = LocalIndex(code[j], true); if (found >= 0) { local = found; break; } }
            int replaced = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction instruction = code[i]; yield return instruction;
                if (local < 0 || instruction.opcode != OpCodes.Ldc_R4 || !(instruction.operand is float value) || (Math.Abs(value - 0.07f) > 0.00001f && Math.Abs(value - 7f) > 0.00001f)) continue;
                if (!code.Skip(Math.Max(0, i - 4)).Take(Math.Min(code.Count, i + 5) - Math.Max(0, i - 4)).Any(c => LocalIndex(c, false) == local)) continue;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Seeker), nameof(SavingGraceBonus))); replaced++;
            }
            if (replaced == 0) ThreeQsVanillaTweaks.Log.Warning("Seeker: Saving Grace stat scaling could not be located.");
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        {
            __state = null;
            if (IsSeeker(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll)
            { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; }
        }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception)
        { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (IsSeeker(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (IsSeeker(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (IsSeeker(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.CooldownAfterDuration && skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
        }
        private static string Percent(float value) => S.Number(value) + "%";
        private static string Damage(float value) => S.Damage(Percent(value) + " damage");
        private static string Healing(float value) => "<style=cIsHealing>" + Percent(value) + "</style>";
        private static string Tranquility => "<style=cIsUtility>Tranquility</style>";
        private static bool HasTranquilityBonus(Profile p) => p.Index == 0 || p.Index == 1 || p.Index == 2 || p.Index == 4 || p.Index == 5 || p.Index == 6;
        private static string TranquilityToken(Profile p) => "KEYWORD_3QS_SEEKER_TRANQUILITY_" + p.Index;
        private static bool IsTranquilityKeyword(string token) => IsKeyword(token, "TRANQUIL", "Tranquility");
        private static bool IsKeyword(string token, string match, string title)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (token.IndexOf(match, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return System.Text.RegularExpressions.Regex.IsMatch(Language.GetString(token) ?? "",
                @"<style=cKeywordName>\s*\[?\s*" + System.Text.RegularExpressions.Regex.Escape(title) + @"\s*\]?\s*</style>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        private static string TranquilityTooltip(Profile p)
        {
            string bonus;
            switch (p.Index)
            {
                case 0: bonus = "Each stack adds " + Damage(F(p, "3rd Hit Bonus Damage Per Tranquility Stack (%)")) + " to the third hit."; break;
                case 1: bonus = "Each stack restores an additional " + Healing(F(p, "Extra Healing Per Tranquility Stack (%)")) + " of damage dealt as health."; break;
                case 2: bonus = "Each stack grants " + S.Utility(I(p, "Extra Hits Per Tranquility Stack").ToString()) + " additional hits per spiral and " + Healing(F(p, "Extra Barrier Per Tranquility Stack (%)")) + " additional barrier per hit."; break;
                case 4:
                    bonus = "Each stack " +
                        (F(p, "Extra Barrier Per Tranquility Stack (%)") > 0f
                            ? "grants " + Healing(F(p, "Extra Barrier Per Tranquility Stack (%)")) + " additional barrier on entering flight and "
                            : "") +
                        "extends the cyclone by " + S.Utility(S.Number(F(p, "Cyclone Duration Per Tranquility Stack")) + " seconds") + ".";
                    break;
                case 5: bonus = "Each stack adds " + Damage(F(p, "Damage Bonus Per Tranquility Stack (%)")) + " and " + Healing(F(p, "Healing Bonus Per Tranquility Stack (%)")) + " healing." +
                    (B(p, "Multiply Radius Per Tranquility Stack") ? " The blast radius is multiplied by your stack count, with a minimum of 1x." : ""); break;
                case 6: bonus = "Each stack adds " + Damage(F(p, "Damage Bonus Per Tranquility (%)")) + ", " + Healing(F(p, "Healing Bonus Per Tranquility (%)")) + " healing and " + S.Utility(Percent(F(p, "Size Growth Per Tranquility (%)"))) + " blast size."; break;
                default: bonus = passiveEnabled.Value ? PassiveDescription() : nativePassiveDescription; break;
            }
            return "<style=cKeywordName>Tranquility</style><style=cSub>" + bonus + "</style>";
        }
        private static string Description(Profile p)
        {
            string agile = p.Skill.AgileText; float damage = C.Get(p.Skill.Damage);
            switch (p.Index)
            {
                case 0: return agile + "Fire spirit orbs for " + Damage(damage) + ". Every third hit explodes for " + Damage(F(p, "3rd Hit Damage (%)")) + " and <style=cIsUtility>slows</style> enemies by " + S.Utility(Percent(I(p, "Slow Power On Hit (%)"))) + ".";
                case 1: return agile + "Crush enemies with a massive hand for " + Damage(damage) + ". Heal for " + Healing(F(p, "Base Healing (%)")) + " of damage dealt.";
                case 2: return agile + "<style=cIsUtility>Exhilarate</style>. Summon a soul spiral that strikes for " + Damage(damage) + ", granting " + Healing(F(p, "Barrier On Hit (%)")) + " barrier on hit. Its final hit deals " + S.Damage(S.Number(F(p, "Last Hit Damage Multiplier")) + "x damage") + ".";
                case 3: return agile + "Transform into a spirit, gaining <style=cIsUtility>flight</style> and passing through enemies. Consume health while flying and explode for " + Damage(damage) + " when flight ends. Healing received is reduced by " + S.Utility(Percent(F(p, "Healing Reduction Per Second (%)"))) + " for each second spent flying.";
                case 4: return agile + "Gain " + Healing(F(p, "Barrier Gained (%)")) + " barrier and take flight. Leave behind a healing cyclone, dealing " + S.DamageText(damage, C.Get(p.Skill.Interval), true) + " and restoring " + Healing(F(p, "Cyclone Healing (%)")) + " health per pulse. Longer flights strengthen the cyclone, and " + Tranquility + " extends its duration.";
                case 5: return agile + "<style=cIsUtility>Meditate</style> to gain " + Tranquility + ". Grant " + Healing(F(p, "Barrier On Cast (%)")) + " barrier when you begin. Complete the sequence to blast enemies for " + Damage(damage) + " and heal nearby allies for " + Healing(F(p, "Ally Healing (%)")) + " of your maximum health. At maximum " + Tranquility + ", grant <style=cIsHealing>Saving Grace</style> and revive fallen allies.";
                case 6: return agile + "Fire a palm blast for " + Damage(damage) + " and heal allies for " + Healing(F(p, "Base Healing (%)")) + ". Fully charge it to deal " + Damage(F(p, "Charged Damage (%)")) + " and heal for " + Healing(F(p, "Charged Healing (%)")) + ". Hit " + I(p, "Multihit Threshold") + " targets" + (B(p, "Teleporter Boss Instantly Grants Tranquility") ? " or the teleporter boss" : "") + " to gain " + Tranquility + "." + (B(p, "Excess Healing Is Converted To Barrier") ? " Excess healing becomes <style=cIsHealing>barrier</style>." : "") + (B(p, "Grants Saving Grace At Max Tranquility") ? " Reaching maximum " + Tranquility + " grants <style=cIsHealing>Saving Grace</style> and revives fallen allies." : "");
                default: return agile + "Fire a spirit orb for " + Damage(damage) + ".";
            }
        }
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) text += "\nDuration: " + S.Utility(C.Get(p.Skill.Duration) < 0f ? "Unlimited" : S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.Index == 0) text += "\nPer Tranquility: " + Damage(F(p, "3rd Hit Bonus Damage Per Tranquility Stack (%)")) + " on the third hit";
            if (p.Index == 1) text += "\nPer Tranquility: " + Healing(F(p, "Extra Healing Per Tranquility Stack (%)")) + " extra healing from damage dealt";
            if (p.Index == 2) text += "\nHits: " + S.Utility(I(p, "Base Hits") + " (+" + I(p, "Extra Hits Per Tranquility Stack") + " per Tranquility)") + "\nExtra Barrier: " + Healing(F(p, "Extra Barrier Per Tranquility Stack (%)")) + " per Tranquility";
            if (p.Index == 4) text += "\nCyclone Duration: " + S.Utility(S.Number(F(p, "Cyclone Duration")) + "s (+" + S.Number(F(p, "Cyclone Duration Per Tranquility Stack")) + "s per Tranquility)");
            if (p.Index == 5) text += "\nPer Tranquility: " + Damage(F(p, "Damage Bonus Per Tranquility Stack (%)")) + ", " + Healing(F(p, "Healing Bonus Per Tranquility Stack (%)")) + " healing";
            if (p.Index == 6) text += "\nPer Tranquility: " + Damage(F(p, "Damage Bonus Per Tranquility (%)")) + ", " + Healing(F(p, "Healing Bonus Per Tranquility (%)")) + " healing, " + S.Utility(Percent(F(p, "Size Growth Per Tranquility (%)"))) + " size";
            return text;
        }
    }
}
