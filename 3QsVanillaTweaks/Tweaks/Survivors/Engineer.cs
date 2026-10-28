using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
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
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Engineer
    {
        private const string Root = "Survivors - 05 Engineer", StatsRoot = "Survivor-Stats - 05 Engineer";
        private static readonly string[] Names = { "Bouncing Grenades", "Laser Cutter", "Pressure Mines", "Spider Mines", "Bubble Shield", "Thermal Harpoons", "TR12 Gauss Auto-Turret", "TR58 Carbonizer Turret" };
        private static readonly string[] Activations = { "EngiWeapon.ChargeGrenades", "EngiWeapon.EnterEngiLaserCutter", "EngiWeapon.FireMines", "EngiWeapon.FireSpiderMine", "EngiWeapon.FireBubbleShield", "EngiMissilePainter.Startup", "EngiWeapon.PlaceTurret", "EngiWeapon.PlaceWalkerTurret" };
        private static readonly string[] DamageStates = { "EngiWeapon.FireGrenades", "EngiWeapon.EngiLaserCutter", "EngiWeapon.FireMines", "EngiWeapon.FireSpiderMine", "EngiWeapon.FireBubbleShield", "EngiMissilePainter.Fire", "Turret.EngiTurretWeapon.FireGauss", "Turret.EngiTurretWeapon.FireBeam" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile, MasterPrefab;
            internal CharacterBody Turret;
            internal bool MissileSpeed;
            internal S.Stats Stats;
            internal ConfigEntry<string> Consumed;
        }
        private sealed class LaserStage
        {
            internal ConfigEntry<float> Damage, Interval, Proc;
            internal ConfigEntry<int> Speed, Jump, Heal;
            internal ConfigEntry<float> Turn;
        }
        private sealed class SprintFlags { internal bool Cancel, Cancelled; }
        private static readonly Dictionary<SkillDef, SprintFlags> turretSprintFlags = new Dictionary<SkillDef, SprintFlags>();
        private sealed class StaticValue { internal FieldInfo Field; internal object Value; }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<StaticValue> Values = new List<StaticValue>();
        }
        private sealed class ProjectileMarker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; internal float BaseDamage; }
        private sealed class PaintClock { internal HealthComponent Target, Selected; internal int SelectedCount; internal float Elapsed; }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly List<Profile> allProfiles = new List<Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<string> hooks = new HashSet<string>();
        private static readonly ConditionalWeakTable<object, PaintClock> paintClocks = new ConditionalWeakTable<object, PaintClock>();
        private static readonly ConditionalWeakTable<GenericSkill, Queue<int>> harpoonCosts = new ConditionalWeakTable<GenericSkill, Queue<int>>();
        private static readonly LaserStage[] stages = new LaserStage[3];
        private static ConfigEntry<bool> enabled, statsEnabled, gaussStatsEnabled, walkerStatsEnabled, instantPaint, alwaysSprint, lockWalkerAim, distributePaint;
        private static ConfigEntry<int> maxGrenades, maxShields, maxGauss, maxWalker, walkerSlow;
        private static ConfigEntry<float> chargeTime, firingTime, windUp, laserRange, stageTime, armTime, armedDamage, detection, armedDetection, blastRadius, armedBlastRadius, spiderSpeed, paintTime, paintRange, gaussTarget, gaussRange, walkerTarget, walkerMaxTarget;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static BuffDef slowBuff, shieldSupportBuff;
        private static ConfigEntry<bool> shieldPush, shieldBuffs;
        private static ConfigEntry<float> shieldArmour, shieldRegen, shieldAttackSpeed, shieldPushSpeed;
        private static readonly HashSet<BubbleSupport> bubbles = new HashSet<BubbleSupport>();
        private static readonly HashSet<CharacterBody> shieldRecipients = new HashSet<CharacterBody>();
        private static float nextShieldScan;
        private static PropertyInfo aimConfirmation;
        [ThreadStatic] private static EntityState activeState;
        private static Type State(string suffix) => typeof(EntityState).Assembly.GetType("EntityStates.Engi" + (suffix.StartsWith("Turret.", StringComparison.Ordinal) ? "" : ".") + suffix);
        private static object Sample(string name) => EntityStateCatalog.InstantiateState(State(name));
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsEngineer(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static bool IsTurret(CharacterBody body, int index) => body && profiles.TryGetValue(index, out Profile p) && p.Turret && body.bodyIndex == p.Turret.bodyIndex;
        private static ProjectileController Controller(EntityState state) => state?.outer ? state.outer.GetComponent<ProjectileController>() : null;
        private static bool Owned(EntityState state)
        {
            CharacterBody body = Body(state);
            if (IsEngineer(body) || IsTurret(body, 6) || IsTurret(body, 7)) return true;
            ProjectileController controller = Controller(state);
            return controller && controller.owner && IsEngineer(controller.owner.GetComponent<CharacterBody>());
        }
        private static int StageIndex(EntityState state) => state.GetType().Name == "EngiLaserCutter3" ? 2 : state.GetType().Name == "EngiLaserCutter2" ? 1 : 0;
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            string key = target.MethodHandle.Value + ":" + prefix + ":" + postfix + ":" + finalizer;
            if (!hooks.Add(key)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Engineer), prefix), postfix == null ? null : new HarmonyMethod(typeof(Engineer), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Engineer), finalizer));
        }
        private static void HookState(string name, bool tick = false)
        {
            Patch(State(name), "OnEnter", nameof(BeforeState), nameof(AfterState), nameof(EndScope));
            if (tick) Patch(State(name), "FixedUpdate", nameof(BeforeState), nameof(AfterTick), nameof(EndScope));
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Engineer changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Engineer stat changes. Restart to apply. Vanilla: False.");
            gaussStatsEnabled = C.BindStartupBool(StatsRoot + " - TR12 Gauss Auto-Turret", "Changes Enabled", true, "Enable Gauss turret stat changes. Restart to apply. Vanilla: False.");
            walkerStatsEnabled = C.BindStartupBool(StatsRoot + " - TR58 Carbonizer Turret", "Changes Enabled", true, "Enable Carbonizer turret stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            if (switches[Names[7]].Value || switches[Names[4]].Value)
            {
                var loader = new GameObject("3QsEngineerContent"); UnityEngine.Object.DontDestroyOnLoad(loader); loader.AddComponent<ContentLoader>();
            }
            RoR2Application.onLoad += Load;
        }
        private sealed class ContentLoader : MonoBehaviour
        {
            private void Start()
            {
                if (switches[Names[4]].Value)
                {
                    shieldSupportBuff = ScriptableObject.CreateInstance<BuffDef>();
                    shieldSupportBuff.name = "3QS_ENGINEER_SHIELD_SUPPORT";
                    shieldSupportBuff.canStack = false; shieldSupportBuff.isDebuff = false;
                    shieldSupportBuff.buffColor = new Color(0.35f, 0.7f, 1f);
                    try { shieldSupportBuff.iconSprite = Addressables.LoadAssetAsync<BuffDef>("RoR2/Base/Engi/bdEngiShield.asset").WaitForCompletion().iconSprite; }
                    catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Bubble Shield buff icon unavailable: " + error.Message); }
                    R2API.ContentAddition.AddBuffDef(shieldSupportBuff);
                }
                if (switches[Names[7]].Value)
                {
                slowBuff = ScriptableObject.CreateInstance<BuffDef>(); slowBuff.name = "3QS_CARBONIZER_SLOW"; slowBuff.isDebuff = true; slowBuff.canStack = false;
                slowBuff.buffColor = new Color(0.3f, 0.75f, 1f);
                try { slowBuff.iconSprite = Addressables.LoadAssetAsync<BuffDef>("RoR2/Base/Common/bdSlow50.asset").WaitForCompletion().iconSprite; }
                catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Carbonizer slow icon unavailable: " + e.Message); }
                R2API.ContentAddition.AddBuffDef(slowBuff);
                }
                UnityEngine.Object.Destroy(gameObject);
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Engi/EngiBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Engineer");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefabBody); stats.Apply(prefabBody); }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(slot => slot.skillFamily)
                    .SelectMany(slot => slot.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type activation = State(Activations[i]), damage = State(DamageStates[i]);
                    SkillDef[] variants = defs.Where(d => d.activationState.stateType == activation || d.activationState.stateType == damage ||
                        i == 5 && (d.skillNameToken == "ENGI_SKILL_HARPOON_NAME" || d.activationState.stateType?.Namespace == "EntityStates.Engi.EngiMissilePainter")).ToArray();
                    if (variants.Length == 0 || damage == null) { ThreeQsVanillaTweaks.Log.Warning("Engineer skill unavailable: " + Names[i]); continue; }
                    foreach (SkillDef def in variants)
                    {
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, damage, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; allProfiles.Add(p); definitions[def] = p;
                        if (!profiles.ContainsKey(i)) profiles[i] = p;
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? null : new Func<float>(() => C.Get(p.Skill.Proc)));
                    }
                }
                foreach (Profile p in profiles.Values) HookState(Activations[p.Index]);
                if (profiles.ContainsKey(0)) { HookState("EngiWeapon.ChargeGrenades", true); HookState("EngiWeapon.FireGrenades", true); }
                if (profiles.ContainsKey(1))
                {
                    HookState("EngiWeapon.EngiLaserCutter", true); HookState("EngiWeapon.EngiLaserCutter2", true); HookState("EngiWeapon.EngiLaserCutter3", true);
                    harmony.Patch(AccessTools.Method(State("EngiWeapon.EngiLaserCutter"), "FixedUpdate"), transpiler: new HarmonyMethod(typeof(Engineer), nameof(SprintIL)));
                    MethodInfo macro = AccessTools.Method(State("EngiWeapon.EngiLaserCutter"), "DamageMacroEngiHealMechanical");
                    if (macro != null) harmony.Patch(macro, transpiler: new HarmonyMethod(typeof(Engineer), nameof(HealIL)));
                }
                if (profiles.ContainsKey(2)) { HookState("Mine.MineArmingWeak", true); HookState("Mine.MineArmingFull"); HookState("Mine.Detonate"); }
                if (profiles.ContainsKey(3)) { HookState("SpiderMine.ChaseTarget", true); HookState("SpiderMine.Detonate"); }
                if (profiles.ContainsKey(4))
                {
                    HookState("EngiBubbleShield.Deployed", true);
                    Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterShieldStats));
                }
                if (profiles.ContainsKey(5))
                {
                    HookState("EngiMissilePainter.Paint", true); HookState("EngiMissilePainter.Fire", true);
                    Patch(State("EngiMissilePainter.Paint"), "GetCurrentTargetInfo", postfix: nameof(AfterPaintTarget));
                    Patch(State("EngiMissilePainter.Paint"), "AddTargetAuthority", nameof(BeforePaintTarget));
                    Patch(State("EngiMissilePainter.Paint"), "OnExit", nameof(BeforeState), finalizer: nameof(EndScope));
                    Patch(State("EngiMissilePainter.Fire"), "FireMissile", postfix: nameof(AfterHarpoonFired));
                    Patch(typeof(GenericSkill), "AddOneStock", nameof(BeforeAddStock));
                }
                if (profiles.ContainsKey(6)) HookState("Turret.EngiTurretWeapon.FireGauss");
                if (profiles.ContainsKey(7))
                {
                    HookState("Turret.EngiTurretWeapon.FireBeam", true);
                    aimConfirmation = AccessTools.Property(typeof(RoR2.CharacterAI.BaseAI), "hasAimConfirmation");
                    Patch(typeof(RoR2.CharacterAI.BaseAI), "UpdateBodyAim", postfix: nameof(AfterTurretAim));
                    Patch(State("Turret.EngiTurretWeapon.FireBeam"), "GetLaserRay", postfix: nameof(AfterLaserRay));
                    harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(Engineer), nameof(SlowIL)));
                    GlobalEventManager.onServerDamageDealt += OnDamageDealt;
                    CharacterBody.onBodyStartGlobal += OnBodyStart;
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                    harmony.Patch(AccessTools.Property(typeof(CharacterBody), "isSprinting").GetSetMethod(), prefix: new HarmonyMethod(typeof(Engineer), nameof(BeforeSprint)));
                    Patch(typeof(CharacterBody), "RecalculateStats", prefix: nameof(BeforeStats));
                    Patch(typeof(CharacterMaster), "Awake", postfix: nameof(AfterMasterAwake));
                    Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", postfix: nameof(AfterLimit));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeProjectileDamage));
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), args: Type.EmptyTypes);
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(GenericSkill), "DeductStock", nameof(BeforeDeduct));
                }
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Engineer changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            int i = p.Index; S.Skill s = p.Skill; object sample = s.Sample;
            s.Bind(i == 4 ? 30f : (float?)null, i == 5 ? 4 : i >= 6 ? 2 : (int?)null, i == 0 || i == 4 ? true : (bool?)null,
                damage: false, nativeConsumeOverride: i >= 5 ? 1 : (int?)null);
            if (i == 0 || i == 2 || i == 3 || i == 4 || i == 5)
            {
                p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
                if (!p.Projectile) throw new MissingMemberException("Projectile unavailable for " + Names[i]);
                BindProjectileSpeed(p);
            }
            if (i != 1 && i != 4)
            {
                float native = S.Value(sample, "damageCoefficient", 1f), proc = 1f;
                if (p.Projectile) proc = p.Projectile.GetComponent<ProjectileController>().procCoefficient;
                if (i == 7) { float frequency = Math.Max(0.001f, S.Value(sample, "fireFrequency", 5f)); native /= frequency; proc = S.Value(sample, "procCoefficient", 1f) / frequency; }
                if (i >= 6) s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff over distance. Vanilla: Default.");
                s.BindDamage(native * 100f, i == 5 ? 700f : i == 6 ? 70f : i == 7 ? 40f : (float?)null, proc, i >= 6);
            }
            if (i == 0)
            {
                object charge = Sample("EngiWeapon.ChargeGrenades");
                maxGrenades = S.Int(s.Section, "Max Grenades", 15, (int)S.Value(charge, "maxGrenadeCount", 8), "Maximum number of grenades charged per use.", 1);
                chargeTime = S.Float(s.Section, "Charge Time", 2f, S.Value(charge, "baseMaxChargeTime"), "Seconds to fully charge, before attack speed is applied.", 0.001f);
                firingTime = S.Float(s.Section, "Base Firing Duration", 0.7f, S.Value(sample, "fireDuration"), "Seconds to launch the charged grenades, before attack speed is applied.", 0.001f);
                BindArea(p);
            }
            if (i == 1)
            {
                windUp = S.Float(s.Section, "Wind Up", 0.6f, S.Value(Sample(Activations[1]), "baseDuration"), "Wind-up time in seconds, before attack speed is applied.");
                laserRange = S.Float(s.Section, "Range", 50f, S.Value(sample, "laserDistance"), "Maximum laser range in metres.");
                stageTime = S.Float(s.Section, "Stage Time", 1.2f, S.Value(sample, "baseMaxDuration"), "Seconds to reach the next stage, before attack speed is applied.", 0.001f);
                for (int j = 0; j < 3; j++)
                {
                    object stage = Sample("EngiWeapon.EngiLaserCutter" + (j == 0 ? "" : (j + 1).ToString())); string prefix = "Stage " + (j + 1) + " ";
                    float frequency = Math.Max(0.001f, S.Value(stage, "fireFrequency", 9f));
                    stages[j] = new LaserStage
                    {
                        Damage = S.Float(s.Section, prefix + "Damage (%)", 30f + j * 20f, S.Value(stage, "damageCoefficient") * 100f, "Damage per tick as a percentage of base damage."),
                        Interval = S.Float(s.Section, prefix + "Damage Interval", 1f / frequency, 1f / frequency, "Seconds between damage ticks before attack speed is applied.", 0.001f),
                        Proc = S.Float(s.Section, prefix + "Proc Coefficient", S.Value(stage, "procCoefficient", 1f), S.Value(stage, "procCoefficient", 1f), "Proc coefficient per damage tick."),
                        Speed = S.Int(s.Section, prefix + "Speed (%)", new[] { 100, 70, 40 }[j], (int)Math.Round(S.Value(stage, "walkSpeedCoefficient", 1f) * 100f), "Movement speed as a percentage of normal speed."),
                        Jump = S.Int(s.Section, prefix + "Jump Power (%)", new[] { 100, 75, 70 }[j], (int)Math.Round(S.Value(stage, "jumpPowerCoefficient", 1f) * 100f), "Jump power as a percentage of normal jump power."),
                        Heal = S.Int(s.Section, prefix + "Health Per Tick (%)", 15 + j * 10, (int)Math.Round(S.Value(stage, "healCoefficient") * 100f), "Health restored to allied mechanical characters per tick, as a percentage of base damage."),
                        Turn = S.Float(s.Section, prefix + "Turn Speed", new[] { 100f, 55f, 40f }[j], S.Value(stage, "maxTurnSpeed"), "Laser turn speed in degrees per second.")
                    };
                }
                s.Damage = stages[0].Damage; s.Proc = stages[0].Proc; s.Interval = stages[0].Interval; s.Periodic = true;
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff over distance. Vanilla: Default.");
            }
            if (i == 2)
            {
                object weak = Sample("Mine.MineArmingWeak"), full = Sample("Mine.MineArmingFull");
                float baseRadius = S.Value(Sample("Mine.Detonate"), "blastRadius");
                armTime = S.Float(s.Section, "Arm Time", 3f, S.Value(weak, "duration"), "Seconds after sticking to a surface to fully arm.");
                armedDamage = S.Float(s.Section, "Armed Damage (%)", 1500f, S.Value(sample, "damageCoefficient") * S.Value(full, "damageScale") * 100f, "Damage when fully armed as a percentage of base damage.");
                float nativeDetection = S.Value(weak, "triggerRadius");
                detection = S.Float(s.Section, "Detection Radius", nativeDetection, nativeDetection, "Enemy detection radius before fully arming, in metres.");
                armedDetection = S.Float(s.Section, "Armed Detection Radius", 7.5f, S.Value(full, "triggerRadius"), "Enemy detection radius when fully armed, in metres.");
                blastRadius = S.Float(s.Section, "Explosion Radius", nativeDetection + 1f, baseRadius * S.Value(weak, "blastRadiusScale"), "Explosion radius before fully arming, in metres.");
                armedBlastRadius = S.Float(s.Section, "Armed Explosion Radius", 8.5f, baseRadius * S.Value(full, "blastRadiusScale"), "Explosion radius when fully armed, in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the explosion's centre. Vanilla: None.");
            }
            if (i == 3)
            {
                float speed = S.Value(Sample("SpiderMine.ChaseTarget"), "speed");
                spiderSpeed = S.Float(s.Section, "Move Speed", speed, speed, "Movement speed while chasing an enemy, in metres per second.");
                float radius = S.Value(Sample("SpiderMine.Detonate"), "blastRadius");
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the explosion's centre. Vanilla: None.");
            }
            if (i == 4)
            {
                s.Duration = S.Float(s.Section, "Duration", 25f, S.Value(Sample("EngiBubbleShield.Deployed"), "lifetime"), "Shield duration in seconds.");
                maxShields = S.Int(s.Section, "Maximum Shields", 3, 1, "Maximum active shields per player.", 1);
                shieldPush = S.Bool(s.Section, "Push Enemies On Deployment", true, false, "Push enemies outward once when the dome opens. Does not damage or continuously repel them.");
                shieldPushSpeed = S.Float(s.Section, "Deployment Push Speed", 40f, 0f, "Outward velocity impulse in metres per second. Respects enemies immune to physics forces.");
                shieldBuffs = S.Bool(s.Section, "Ally Buffs Inside Shield", true, false, "Buff living allies inside the dome, including turrets and drones. Overlapping shields do not stack.");
                shieldArmour = S.Float(s.Section, "Armour Inside Shield", 100f, 0f, "Additional armour while inside an allied Bubble Shield.");
                shieldRegen = S.Float(s.Section, "Health Regeneration Inside Shield", 10f, 0f, "Additional flat health regeneration per second while inside an allied Bubble Shield. Does not scale with level.");
                shieldAttackSpeed = S.Float(s.Section, "Attack Speed Inside Shield (%)", 15f, 0f, "Additional attack speed while inside an allied Bubble Shield, additive with other attack-speed bonuses.");
            }
            if (i == 5)
            {
                p.Consumed = C.Bind(s.Section, "Stocks Consumed On Use", "1", "Stocks consumed per harpoon marked. Use a whole number or All. Vanilla: 1.");
                object paint = Sample("EngiMissilePainter.Paint");
                distributePaint = S.Bool(s.Section, "Distribute Targets Evenly", true, false, "Spread harpoon marks evenly across enemies in the targeting area.");
                instantPaint = S.Bool(s.Section, "Hovering An Enemy Instantly Paints A Target", false, true, "Immediately mark newly targeted enemies instead of waiting for the painting duration.");
                paintTime = S.Float(s.Section, "Painting Duration", 0.2f, S.Value(paint, "stackInterval"), "Seconds of aiming at an enemy to mark it, before attack speed is applied.");
                paintRange = S.Float(s.Section, "Painting Range", 250f, S.Value(paint, "maxDistance"), "Maximum targeting range in metres.");
            }
            if (i >= 6)
            {
                GameObject master = S.Read(Sample(Activations[i]), "turretMasterPrefab") as GameObject;
                if (!master) throw new MissingMemberException("Turret master unavailable for " + Names[i]);
                p.MasterPrefab = master; p.Turret = master.GetComponent<CharacterMaster>().bodyPrefab.GetComponent<CharacterBody>();
                string section = StatsRoot + " - " + Names[i];
                if ((i == 6 ? gaussStatsEnabled : walkerStatsEnabled).Value)
                {
                    if (i == 7)
                    {
                        CharacterBody gauss = (S.Read(Sample(Activations[6]), "turretMasterPrefab") as GameObject).GetComponent<CharacterMaster>().bodyPrefab.GetComponent<CharacterBody>();
                        S.Float(section, "Move Speed", 8f, p.Turret.baseMoveSpeed, "Base move speed.");
                        S.Float(section, "Health Regen", gauss.baseRegen, p.Turret.baseRegen, "Base health regen.");
                        S.Float(section, "Health Regen Per Level", gauss.levelRegen, p.Turret.levelRegen, "Health regen gained per level.");
                    }
                    p.Stats = new S.Stats(section, p.Turret);
                }
                p.Consumed = C.Bind(s.Section, "Stocks Consumed On Use", "1", "Stocks consumed per turret placed. Use a whole number or All. Vanilla: 1.");
                float interval = i == 6 ? S.Value(sample, "baseDuration") : 1f / Math.Max(0.001f, S.Value(sample, "fireFrequency", 5f));
                s.Interval = S.Float(s.Section, "Damage Interval", i == 6 ? 0.35f : 0.2f, interval, "Seconds between turret damage ticks before attack speed is applied.", 0.001f);
                float nativeRange = master.GetComponents<RoR2.CharacterAI.AISkillDriver>().Where(d => d.skillSlot == SkillSlot.Primary).Select(d => d.maxDistance).FirstOrDefault();
                if (i == 6)
                {
                    maxGauss = S.Int(s.Section, "Max Turrets", 3, 2, "Absoulte maximum turret limit. Active turrets are limited to the skill's maximum stocks, up to this cap. Vanilla: 3", 1);
                    gaussTarget = S.Float(s.Section, "Targeting Range", 80f, nativeRange, "Maximum distance at which the turret fires at enemies, in metres.");
                    gaussRange = S.Float(s.Section, "Bullet Max Range", 200f, new BulletAttack().maxDistance, "Maximum bullet travel distance in metres.");
                }
                else
                {
                    maxWalker = S.Int(s.Section, "Max Turrets", 3, 2, "Absoulte maximum turret limit. Active turrets are limited to the skill's maximum stocks, up to this cap. Vanilla: 3", 1);
                    alwaysSprint = S.Bool(s.Section, "Always Sprint", true, false, "Turrets sprint while moving, including while firing.");
                    lockWalkerAim = S.Bool(s.Section, "Lock Aim To Target", true, false, "Keep the turret's aim locked to its current enemy while moving.");
                    walkerSlow = S.Int(s.Section, "Slow On Hit (%)", 50, 50, "Movement slow coefficient inflicted on enemies hit by the laser.");
                    walkerTarget = S.Float(s.Section, "Targeting Range", 40f, nativeRange, "Distance at which the turret starts firing instead of chasing, in metres.");
                    walkerMaxTarget = S.Float(s.Section, "Max Targeting Range", 60f, S.Value(sample, "maxDistance"), "Maximum enemy tracking and laser range in metres.");
                }
            }
        }
        private static void BindProjectileSpeed(Profile p)
        {
            foreach (Component component in p.Projectile.GetComponents<Component>())
            {
                FieldInfo forward = component.GetType().GetField("desiredForwardSpeed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (forward?.FieldType == typeof(float)) { p.Skill.BindProjectileSpeed(p.Projectile); return; }
            }
            foreach (Component component in p.Projectile.GetComponents<Component>())
            {
                FieldInfo maximum = component.GetType().GetField("maxVelocity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (maximum?.FieldType != typeof(float)) continue;
                float native = (float)maximum.GetValue(component);
                p.MissileSpeed = true; p.Skill.NativeProjectileSpeed = native;
                p.Skill.ProjectileSpeed = S.Float(p.Skill.Section, "Projectile Speed (m/s)", native, native, "Maximum homing projectile speed in metres per second."); return;
            }
            p.Skill.BindProjectileSpeed(p.Projectile);
        }
        private static void BindArea(Profile p)
        {
            ProjectileExplosion explosion = p.Projectile.GetComponent<ProjectileExplosion>(); if (!explosion) return;
            p.Skill.Radius = S.Float(p.Skill.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
            S.AreaFalloff native = Enum.TryParse(explosion.falloffModel.ToString(), out S.AreaFalloff value) ? value : S.AreaFalloff.None;
            p.Skill.Area = C.Bind(p.Skill.Section, "AOE Damage Falloff", native, "Damage falloff from the explosion's centre. Vanilla: " + native + ".");
        }
        private static void Apply()
        {
            if (shieldSupportBuff)
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                    if (body && body.HasBuff(shieldSupportBuff)) body.RecalculateStats();
            if (NetworkServer.active && shieldBuffs != null) RefreshShieldRecipients(true);
            if (stats != null) stats.Apply(prefabBody);
            foreach (Profile p in allProfiles)
            {
                p.Skill.ApplyDefinition();
                // Non-Agile skills block sprinting during use instead of being interrupted by sprint input.
                p.Skill.Definition.canceledFromSprinting = false;
                if (p.Index == 5) p.Skill.Definition.stockToConsume = 0;
                if (p.Index >= 6) { p.Skill.Definition.stockToConsume = 0; p.Stats?.Apply(p.Turret); ApplyAI(p.MasterPrefab, p); }
                S.SetDescription(p.Skill, "3QS_ENGINEER_" + p.Index + "_DESCRIPTION", () => Description(p));
            }
        }
        private static string DamageText(S.Skill s) => S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), s.Periodic);
        private static string Description(Profile p)
        {
            string text;
            switch (p.Index)
            {
                case 0: text = "Charge up to " + S.Damage(C.Get(maxGrenades) + " bouncing grenades") + " that explode for " + DamageText(p.Skill) + " each."; break;
                case 1: text = "Fire a laser that powers up through 3 stages, dealing " + S.DamageText(C.Get(stages[0].Damage), C.Get(stages[0].Interval), true) + ", scaling up to " + S.DamageText(C.Get(stages[2].Damage), C.Get(stages[2].Interval), true) + ". " + S.Utility("Heals allied mechanical characters."); break;
                case 2: text = "Place a mine that explodes for " + DamageText(p.Skill) + ". Fully arms for " + S.Damage(S.Number(C.Get(armedDamage)) + "% damage") + "."; break;
                case 3: text = "Place a robot mine that chases enemies and explodes for " + DamageText(p.Skill) + "."; break;
                case 4:
                    text = "Place an " + S.Utility("impenetrable shield") + " that blocks incoming damage for " + S.Utility(S.Number(C.Get(p.Skill.Duration)) + " seconds") + ".";
                    if (C.Get(shieldPush)) text += " Push enemies away when the dome opens.";
                    if (C.Get(shieldBuffs)) text += " Allies inside gain " + S.Utility(S.Number(C.Get(shieldArmour)) + " armour") + ", " +
                        S.Utility(S.Number(C.Get(shieldRegen)) + " health regeneration") + " and " + S.Utility(S.Number(C.Get(shieldAttackSpeed)) + "% attack speed") + ". Overlapping shields do not stack.";
                    break;
                case 5: text = (C.Get(distributePaint) ? "Mark enemies evenly across the targeting area" : "Mark enemies") + ", then launch homing harpoons for " + DamageText(p.Skill) + " each."; break;
                default: text = "Place a " + S.Utility(p.Index == 6 ? "stationary turret" : "mobile turret") + " that " + S.Utility("inherits all your items") + ", dealing " + DamageText(p.Skill) + (p.Index == 7 && C.Get(walkerSlow) > 0 ? " and " + S.Utility("slowing enemies") : "") + ". " + S.Utility("Can place up to " + Math.Min(p.Skill.Definition.baseMaxStock, C.Get(p.Index == 6 ? maxGauss : maxWalker)) + "."); break;
            }
            return p.Skill.AgileText + text;
        }
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (key, value) => b.Append("\n").Append(key).Append(": ").Append(S.Utility(value));
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) add("AOE Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Index == 0) { add("Charge Time", S.Number(C.Get(chargeTime)) + "s"); add("Firing Duration", S.Number(C.Get(firingTime)) + "s"); }
            if (p.Index == 1)
            {
                add("Range", S.Number(C.Get(laserRange)) + "m"); add("Wind Up", S.Number(C.Get(windUp)) + "s"); add("Stage Time", S.Number(C.Get(stageTime)) + "s");
            }
            if (p.Index == 2) { add("Arm Time", S.Number(C.Get(armTime)) + "s"); add("Detection Radius", S.Number(C.Get(detection)) + "m / " + S.Number(C.Get(armedDetection)) + "m armed"); add("Explosion Radius", S.Number(C.Get(blastRadius)) + "m / " + S.Number(C.Get(armedBlastRadius)) + "m armed"); }
            if (p.Index == 3) add("Mine Move Speed", S.Number(C.Get(spiderSpeed)) + "m/s");
            if (p.Index == 4)
            {
                add("Maximum Shields", C.Get(maxShields).ToString());
                if (C.Get(shieldPush)) add("Deployment Push", S.Number(C.Get(shieldPushSpeed)) + "m/s");
                if (C.Get(shieldBuffs)) { add("Shield Armour", S.Number(C.Get(shieldArmour))); add("Shield Regen", S.Number(C.Get(shieldRegen)) + "/s"); add("Shield Attack Speed", S.Number(C.Get(shieldAttackSpeed)) + "%"); }
            }
            if (p.Index == 5) { add("Painting Duration", S.Number(C.Get(paintTime)) + "s"); add("Painting Range", S.Number(C.Get(paintRange)) + "m"); }
            if (p.Index >= 6) { add("Damage Interval", S.Number(C.Get(p.Skill.Interval)) + "s"); add("Targeting Range", S.Number(C.Get(p.Index == 6 ? gaussTarget : walkerTarget)) + "m"); if (p.Index == 6) add("Bullet Range", S.Number(C.Get(gaussRange)) + "m"); else { add("Max Targeting Range", S.Number(C.Get(walkerMaxTarget)) + "m"); add("Slow On Hit", C.Get(walkerSlow) + "%"); } }
            return b.ToString();
        }
        private static void Static(Scope scope, object state, string fieldName, object value)
        {
            FieldInfo field = AccessTools.Field(state.GetType(), fieldName); if (field == null || !field.IsStatic) return;
            if (!scope.Values.Any(v => v.Field == field)) scope.Values.Add(new StaticValue { Field = field, Value = field.GetValue(null) });
            field.SetValue(null, value);
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            activeState = Owned(__instance) ? __instance : null; if (activeState == null) return;
            Profile sprintProfile = AnimationProfile(__instance);
            if (IsEngineer(Body(__instance)) && sprintProfile != null && sprintProfile.Skill.Definition.cancelSprintingOnActivation) Body(__instance).isSprinting = false;
            string name = __instance.GetType().Name; string ns = __instance.GetType().Namespace ?? "";
            if (profiles.ContainsKey(0) && name == "ChargeGrenades")
            {
                Static(__state, __instance, "maxGrenadeCount", C.Get(maxGrenades)); Static(__state, __instance, "maxCharges", Math.Max(1, C.Get(maxGrenades) - 1));
                float gap = Math.Max(0f, S.Value(__instance, "baseTotalDuration") - S.Value(__instance, "baseMaxChargeTime"));
                Static(__state, __instance, "baseMaxChargeTime", C.Get(chargeTime)); Static(__state, __instance, "baseTotalDuration", C.Get(chargeTime) + gap);
            }
            if (profiles.TryGetValue(0, out Profile grenades) && name == "FireGrenades")
            {
                Static(__state, __instance, "damageCoefficient", C.Get(grenades.Skill.Damage) / 100f);
                Static(__state, __instance, "fireDuration", C.Get(firingTime)); Static(__state, __instance, "baseDuration", C.Get(firingTime));
                S.Write(__instance, "duration", float.PositiveInfinity);
            }
            if (profiles.ContainsKey(1) && name == "EnterEngiLaserCutter") Static(__state, __instance, "baseDuration", C.Get(windUp));
            if (profiles.ContainsKey(1) && name.StartsWith("EngiLaserCutter", StringComparison.Ordinal))
            {
                int index = StageIndex(__instance); LaserStage s = stages[index];
                S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f); S.Write(__instance, "procCoefficient", C.Get(s.Proc)); S.Write(__instance, "fireFrequency", 1f / C.Get(s.Interval));
                S.Write(__instance, "baseMaxDuration", index == 2 ? -1f : C.Get(stageTime)); S.Write(__instance, "laserDistance", C.Get(laserRange));
                S.Write(__instance, "healCoefficient", C.Get(s.Heal) / 100f); S.Write(__instance, "walkSpeedCoefficient", C.Get(s.Speed) / 100f); S.Write(__instance, "jumpPowerCoefficient", C.Get(s.Jump) / 100f); S.Write(__instance, "maxTurnSpeed", C.Get(s.Turn));
                Static(__state, __instance, "staticHealCoefficient", S.Value(__instance, "staticHealCoefficient"));
            }
            if (profiles.TryGetValue(2, out Profile mines) && ns.EndsWith("Engi.Mine", StringComparison.Ordinal))
            {
                bool full = name == "MineArmingFull";
                if (name == "MineArmingWeak" || full)
                {
                    if (!full) Static(__state, __instance, "duration", C.Get(armTime));
                    S.Write(__instance, "triggerRadius", C.Get(full ? armedDetection : detection));
                    float nativeRadius = S.Value(Sample("Mine.Detonate"), "blastRadius");
                    S.Write(__instance, "blastRadiusScale", C.Get(full ? armedBlastRadius : blastRadius) / Math.Max(0.001f, nativeRadius));
                }
            }
            if (profiles.ContainsKey(3) && name == "ChaseTarget" && ns.EndsWith("SpiderMine", StringComparison.Ordinal)) Static(__state, __instance, "speed", C.Get(spiderSpeed));
            if (profiles.TryGetValue(3, out Profile spider) && name == "Detonate" && ns.EndsWith("SpiderMine", StringComparison.Ordinal)) Static(__state, __instance, "blastRadius", C.Get(spider.Skill.Radius));
            if (profiles.TryGetValue(4, out Profile shield) && name == "Deployed" && ns.EndsWith("EngiBubbleShield", StringComparison.Ordinal))
            {
                float duration = C.Get(shield.Skill.Duration);
                Static(__state, __instance, "lifetime", duration);
                // The blink timer starts on the projectile, before the deployed state begins.
                foreach (var blink in __instance.outer.gameObject.GetComponentsInChildren<BeginRapidlyActivatingAndDeactivating>(true))
                    blink.delayBeforeBeginningBlinking = Math.Max(0f, S.Value(blink, "fixedAge") + duration - S.Value(__instance, "fixedAge") - 5f);
            }
            if (profiles.TryGetValue(5, out Profile harpoons) && ns.EndsWith("EngiMissilePainter", StringComparison.Ordinal))
            {
                if (name == "Paint") { Static(__state, __instance, "stackInterval", C.Get(paintTime)); Static(__state, __instance, "maxDistance", C.Get(paintRange)); }
                if (name == "Fire") Static(__state, __instance, "damageCoefficient", C.Get(harpoons.Skill.Damage) / 100f);
            }
            if (profiles.TryGetValue(6, out Profile gauss) && name == "FireGauss" && IsTurret(Body(__instance), 6))
            { Static(__state, __instance, "damageCoefficient", C.Get(gauss.Skill.Damage) / 100f); Static(__state, __instance, "baseDuration", C.Get(gauss.Skill.Interval)); }
            if (profiles.TryGetValue(7, out Profile walker) && name == "FireBeam" && IsTurret(Body(__instance), 7))
            {
                float frequency = 1f / C.Get(walker.Skill.Interval);
                S.Write(__instance, "fireFrequency", frequency); S.Write(__instance, "damageCoefficient", C.Get(walker.Skill.Damage) / 100f * frequency); S.Write(__instance, "procCoefficient", C.Get(walker.Skill.Proc) * frequency); S.Write(__instance, "maxDistance", C.Get(walkerMaxTarget));
                if (C.Get(alwaysSprint)) Body(__instance).isSprinting = true;
            }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            for (int i = __state.Values.Count - 1; i >= 0; i--) __state.Values[i].Field.SetValue(null, __state.Values[i].Value);
            activeState = __state.Previous; return __exception;
        }
        private static void AfterState(EntityState __instance)
        {
            if (!Owned(__instance)) return;
            if (__instance.GetType().Name == "FireGrenades" && profiles.ContainsKey(0)) S.Write(__instance, "grenadeCountMax", Math.Min(C.Get(maxGrenades), (int)S.Value(__instance, "grenadeCountMax", 1f)));
        }
        private static void AfterTick(EntityState __instance)
        {
            if (NetworkServer.active && profiles.ContainsKey(4) && Owned(__instance) &&
                __instance.GetType().Name == "Deployed" && (__instance.GetType().Namespace ?? "").EndsWith("EngiBubbleShield", StringComparison.Ordinal) &&
                S.Read(__instance, "hasDeployed") is bool deployed && deployed && !__instance.outer.GetComponent<BubbleSupport>())
            {
                var bubble = __instance.outer.gameObject.AddComponent<BubbleSupport>();
                bubble.Initialize(__instance);
            }
            if (!Owned(__instance) || __instance.GetType().Name != "FireGrenades" || !profiles.ContainsKey(0)) return;
            int count = (int)S.Value(__instance, "grenadeCount"), max = (int)S.Value(__instance, "grenadeCountMax", 1f);
            float timer = S.Value(__instance, "fireTimer"), interval = C.Get(firingTime) / Math.Max(0.01f, Body(__instance).attackSpeed) / Math.Max(1, max);
            while (timer <= 0f && count < max)
            {
                AccessTools.Method(__instance.GetType(), "FireGrenade").Invoke(__instance, new object[] { count % 2 == 0 ? "MuzzleLeft" : "MuzzleRight" }); count++; timer += interval;
            }
            S.Write(__instance, "grenadeCount", count); S.Write(__instance, "fireTimer", timer);
            float duration = C.Get(firingTime) / Math.Max(0.01f, Body(__instance).attackSpeed);
            S.Write(__instance, "duration", duration);
            if (count >= max && S.Value(__instance, "fixedAge") >= duration && S.Read(__instance, "isAuthority") is bool authority && authority) __instance.outer.SetNextStateToMain();
        }
        private sealed class BubbleSupport : MonoBehaviour
        {
            private Collider dome;
            private TeamIndex team = TeamIndex.None;
            private bool ready;
            internal void Initialize(EntityState state)
            {
                var controller = GetComponent<ProjectileController>();
                if (!controller || !controller.teamFilter) { enabled = false; return; }
                team = controller.teamFilter.teamIndex;
                var locator = GetComponent<ChildLocator>();
                string childName = S.Read(state, "childLocatorString") as string;
                Transform child = locator && !string.IsNullOrEmpty(childName) ? locator.FindChild(childName) : null;
                // Use the actual active blocking collider, not a guessed world radius.
                if (child) dome = child.GetComponentsInChildren<Collider>().Where(c => c.enabled)
                    .OrderByDescending(c => c.bounds.extents.sqrMagnitude).FirstOrDefault();
                if (!dome)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Bubble Shield support could not find the active dome collider.");
                    enabled = false; return;
                }
                ready = true; bubbles.Add(this);
                if (C.Get(shieldPush)) PushEnemies();
                RefreshShieldRecipients(true);
            }
            internal bool Contains(CharacterBody body)
            {
                if (!ready || !dome || !dome.enabled || !dome.gameObject.activeInHierarchy || !body || !body.healthComponent || !body.healthComponent.alive) return false;
                Vector3 point = body.corePosition;
                // ClosestPoint handles the native dome shape and its transform/scale.
                if (dome is MeshCollider mesh && !mesh.convex)
                {
                    Bounds bounds = dome.bounds;
                    float radius = Math.Max(bounds.extents.x, bounds.extents.z);
                    return (point - bounds.center).sqrMagnitude <= radius * radius;
                }
                return (dome.ClosestPoint(point) - point).sqrMagnitude <= 0.0025f;
            }
            internal bool Covers(CharacterBody body) => body && body.teamComponent && body.teamComponent.teamIndex == team && Contains(body);
            private void PushEnemies()
            {
                if (!NetworkServer.active || C.Get(shieldPushSpeed) <= 0f) return;
                TeamMask enemies = TeamMask.GetEnemyTeams(team);
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                {
                    if (!body || !body.teamComponent || !enemies.HasTeam(body.teamComponent.teamIndex) || !Contains(body)) continue;
                    Vector3 direction = body.corePosition - dome.bounds.center;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
                    direction = direction.normalized;
                    // A small upward component releases grounded motors without teleporting through terrain.
                    var force = new PhysForceInfo
                    {
                        force = (direction + Vector3.up * 0.2f).normalized * C.Get(shieldPushSpeed),
                        massIsOne = true, ignoreGroundStick = true, disableAirControlUntilCollision = true,
                        respectKnockupImmune = true
                    };
                    body.healthComponent.TakeDamageForce(force);
                }
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                var machine = GetComponent<EntityStateMachine>();
                if (!machine || machine.state == null || machine.state.GetType() != State("EngiBubbleShield.Deployed"))
                { Destroy(this); return; }
                RefreshShieldRecipients(false);
            }
            private void OnDestroy()
            {
                bubbles.Remove(this);
                if (NetworkServer.active) RefreshShieldRecipients(true);
            }
        }
        private static void RefreshShieldRecipients(bool immediate)
        {
            if (!NetworkServer.active || !shieldSupportBuff) return;
            if (!immediate && Time.fixedTime < nextShieldScan) return;
            nextShieldScan = Time.fixedTime + 0.1f;
            var covered = new HashSet<CharacterBody>();
            if (C.Get(shieldBuffs))
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                    if (body && bubbles.Any(b => b && b.Covers(body))) covered.Add(body);
            foreach (CharacterBody body in shieldRecipients.ToArray())
                if (!body || !covered.Contains(body))
                {
                    if (body) body.RemoveBuff(shieldSupportBuff);
                    shieldRecipients.Remove(body);
                }
            foreach (CharacterBody body in covered)
                if (shieldRecipients.Add(body)) body.AddBuff(shieldSupportBuff);
        }
        private static void SetShieldStat(CharacterBody body, string name, float value)
        { AccessTools.Property(typeof(CharacterBody), name).GetSetMethod(true).Invoke(body, new object[] { value }); }
        private static void AfterShieldStats(CharacterBody __instance)
        {
            if (!shieldSupportBuff || !__instance.HasBuff(shieldSupportBuff)) return;
            SetShieldStat(__instance, "armor", __instance.armor + C.Get(shieldArmour));
            // Flat, non-scaling regeneration; respect effects that suppress all healing.
            if (!__instance.HasBuff(RoR2Content.Buffs.HealingDisabled))
                SetShieldStat(__instance, "regen", __instance.regen + C.Get(shieldRegen));
            float level = Math.Max(0f, __instance.level - 1f);
            SetShieldStat(__instance, "attackSpeed", __instance.attackSpeed +
                (__instance.baseAttackSpeed + __instance.levelAttackSpeed * level) * C.Get(shieldAttackSpeed) / 100f);
        }
        private static Profile ProjectileProfile(GameObject prefab, CharacterBody body) => IsEngineer(body) ? profiles.Values.FirstOrDefault(p => p.Projectile && p.Projectile == prefab) : null;
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            CharacterBody body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            Profile p = ProjectileProfile(fireProjectileInfo.projectilePrefab, body); if (p == null) return;
            if (!p.MissileSpeed) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
            if (p.Skill.Damage != null && (p.Index == 2 || p.Index == 3)) fireProjectileInfo.damage = body.damage * C.Get(p.Skill.Damage) / 100f;
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null; if (!IsEngineer(body)) return;
            Profile p = profiles.Values.FirstOrDefault(profile => profile.Projectile && profile.Projectile.GetComponent<ProjectileController>().catalogIndex == __instance.catalogIndex); if (p == null) return;
            var marker = __instance.GetComponent<ProjectileMarker>() ?? __instance.gameObject.AddComponent<ProjectileMarker>(); marker.Profile = p; marker.Origin = __instance.transform.position;
            ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>(); marker.BaseDamage = damage && p.Skill.Damage != null && C.Get(p.Skill.Damage) > 0f ? damage.damage / (C.Get(p.Skill.Damage) / 100f) : body.damage;
            if (p.MissileSpeed)
                foreach (Component component in __instance.GetComponents<Component>())
                    if (component.GetType().GetField("maxVelocity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is FieldInfo speed) speed.SetValue(component, C.Get(p.Skill.ProjectileSpeed));
            if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
            ProjectileExplosion explosion = __instance.GetComponent<ProjectileExplosion>(); if (explosion && p.Skill.Radius != null) explosion.blastRadius = C.Get(p.Skill.Radius);
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static void BeforeBlast(BlastAttack __instance)
        {
            ProjectileMarker marker = __instance.inflictor ? __instance.inflictor.GetComponent<ProjectileMarker>() : null; if (!marker) return;
            Profile p = marker.Profile;
            if (p.Index == 2)
            {
                bool full = marker.GetComponents<EntityStateMachine>().Any(machine => machine.state?.GetType().Name == "MineArmingFull");
                __instance.baseDamage = marker.BaseDamage * (full ? C.Get(armedDamage) : C.Get(p.Skill.Damage)) / 100f;
                __instance.radius = C.Get(full ? armedBlastRadius : blastRadius);
            }
            if (p.Skill.Proc != null) __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Skill.Area != null) __instance.falloffModel = Area(C.Get(p.Skill.Area));
        }
        private static void BeforeProjectileDamage(DamageInfo damageInfo)
        {
            ProjectileMarker marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponent<ProjectileMarker>() : null;
            if (!marker || marker.Profile.Skill.Falloff == null || damageInfo.dotIndex != DotController.DotIndex.None) return;
            float distance = Vector3.Distance(marker.Origin, damageInfo.position);
            S.BulletFalloff mode = C.Get(marker.Profile.Skill.Falloff);
            if (mode == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f;
            else if (mode == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.25f + Mathf.Clamp01(Mathf.InverseLerp(25f, 7f, distance)) * 0.75f;
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            if (activeState == null || !__instance.owner || __instance.damageType.damageSource != DamageSource.Primary) return;
            CharacterBody body = __instance.owner.GetComponent<CharacterBody>(); if (body != Body(activeState)) return;
            Profile p = IsTurret(body, 6) ? profiles[6] : IsTurret(body, 7) ? profiles[7] : IsEngineer(body) && activeState.GetType().Name.StartsWith("EngiLaserCutter", StringComparison.Ordinal) && profiles.ContainsKey(1) ? profiles[1] : null;
            if (p == null) return;
            __instance.procCoefficient = p.Index == 1 ? C.Get(stages[StageIndex(activeState)].Proc) : C.Get(p.Skill.Proc);
            __instance.falloffModel = C.Get(p.Skill.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
            if (p.Index == 6) __instance.maxDistance = C.Get(gaussRange);
            if (p.Index == 7) __instance.damageType.damageType &= ~DamageType.SlowOnHit;
        }
        private static void AfterPaintTarget(EntityState __instance, ref HurtBox currentTargetHurtBox, ref HealthComponent currentTargetHealthComponent)
        {
            if (!IsEngineer(Body(__instance)) || !profiles.ContainsKey(5)) return;
            PaintClock clock = paintClocks.GetValue(__instance, _ => new PaintClock());
            if (C.Get(distributePaint) && S.Read(__instance, "search") is BullseyeSearch search)
            {
                // Reuse the native search, including its cone, range, teams, and line-of-sight filters.
                HurtBox[] candidates = search.GetResults().Where(hurt => hurt && hurt.healthComponent && hurt.healthComponent.alive)
                    .GroupBy(hurt => hurt.healthComponent).Select(group => group.First()).ToArray();
                var counts = new Dictionary<HealthComponent, int>();
                if (S.Read(__instance, "targetsList") is List<HurtBox> marked)
                    foreach (HurtBox hurt in marked)
                        if (hurt && hurt.healthComponent) { counts.TryGetValue(hurt.healthComponent, out int count); counts[hurt.healthComponent] = count + 1; }
                int minimum = candidates.Length == 0 ? 0 : candidates.Min(hurt => counts.TryGetValue(hurt.healthComponent, out int count) ? count : 0);
                HurtBox selected = candidates.FirstOrDefault(hurt => (counts.TryGetValue(hurt.healthComponent, out int count) ? count : 0) == minimum);
                // Keep a pending selection steady while painting, even if enemies change their sort order.
                HurtBox pending = candidates.FirstOrDefault(hurt => hurt.healthComponent == clock.Selected);
                int pendingCount = 0;
                if (pending) counts.TryGetValue(pending.healthComponent, out pendingCount);
                if (pending && pendingCount == minimum && pendingCount == clock.SelectedCount) selected = pending;
                currentTargetHurtBox = selected;
                currentTargetHealthComponent = selected ? selected.healthComponent : null;
                clock.Selected = currentTargetHealthComponent; clock.SelectedCount = minimum;
            }
            if (clock.Target != currentTargetHealthComponent || !currentTargetHealthComponent || !Body(__instance).inputBank.skill1.down) clock.Elapsed = 0f;
            else clock.Elapsed += Time.fixedDeltaTime;
            clock.Target = currentTargetHealthComponent;
        }
        private static bool BeforePaintTarget(EntityState __instance)
        {
            CharacterBody body = Body(__instance); if (!IsEngineer(body) || !profiles.ContainsKey(5)) return true;
            GenericSkill slot = S.Read(__instance, "activatorSkillSlot") as GenericSkill;
            if (slot && (slot.stock < Consume(profiles[5], slot) || profiles[5].Skill.RequireAll && slot.stock < slot.maxStock)) return false;
            if (C.Get(instantPaint)) return true;
            PaintClock clock = paintClocks.GetValue(__instance, _ => new PaintClock());
            if (clock.Elapsed + 0.000001f < C.Get(paintTime) / Math.Max(0.01f, body.attackSpeed)) return false;
            clock.Elapsed = 0f; return true;
        }
        private static void AfterBodyAwake(CharacterBody __instance)
        {
            if (IsEngineer(__instance)) stats?.Apply(__instance);
            foreach (Profile p in profiles.Values) if (p.Turret && __instance.bodyIndex == p.Turret.bodyIndex) p.Stats?.Apply(__instance);
        }
        private static void BeforeStats(CharacterBody __instance)
        {
            if (IsTurret(__instance, 6)) S.Write(__instance, "baseVisionDistance", C.Get(gaussTarget));
            if (IsTurret(__instance, 7)) S.Write(__instance, "baseVisionDistance", C.Get(walkerMaxTarget));
        }
        private static void ApplyAI(GameObject master, Profile p)
        {
            if (!master) return;
            foreach (RoR2.CharacterAI.AISkillDriver driver in master.GetComponents<RoR2.CharacterAI.AISkillDriver>())
            {
                if (driver.skillSlot == SkillSlot.Primary) driver.maxDistance = C.Get(p.Index == 6 ? gaussTarget : walkerTarget);
                else if (p.Index == 7 && driver.moveTargetType == RoR2.CharacterAI.AISkillDriver.TargetType.CurrentEnemy) driver.minDistance = C.Get(walkerTarget);
                if (p.Index == 7) driver.shouldSprint = C.Get(alwaysSprint);
            }
            if (p.Index == 7)
                foreach (GenericSkill slot in p.Turret.GetComponents<GenericSkill>()) if (slot.skillFamily)
                    foreach (SkillFamily.Variant variant in slot.skillFamily.variants) if (variant.skillDef)
                    {
                        SkillDef def = variant.skillDef;
                        if (!turretSprintFlags.TryGetValue(def, out SprintFlags flags))
                        { flags = new SprintFlags { Cancel = def.cancelSprintingOnActivation, Cancelled = def.canceledFromSprinting }; turretSprintFlags[def] = flags; }
                        def.cancelSprintingOnActivation = !C.Get(alwaysSprint) && flags.Cancel;
                        def.canceledFromSprinting = !C.Get(alwaysSprint) && flags.Cancelled;
                    }
        }
        private static bool TryWalkerAim(RoR2.CharacterAI.BaseAI ai, CharacterBody body, Vector3 origin, out Vector3 direction)
        {
            direction = default;
            if (!ai || !IsTurret(body, 7) || !C.Get(lockWalkerAim)) return false;
            // Use the enemy already selected by the AI and retain its visibility/memory rules.
            var target = ai.skillDriverEvaluation.aimTarget;
            if (target == null || target != ai.currentEnemy || !target.gameObject ||
                !target.healthComponent || !target.healthComponent.alive || !target.GetBullseyePosition(out Vector3 point)) return false;
            Vector3 offset = point - origin;
            if (offset.sqrMagnitude <= 0.000001f) return false;
            direction = offset.normalized; return true;
        }
        private static void AfterTurretAim(RoR2.CharacterAI.BaseAI __instance)
        {
            CharacterBody body = __instance.body;
            if (!body || !body.inputBank || !TryWalkerAim(__instance, body, body.inputBank.aimOrigin, out Vector3 direction)) return;
            body.inputBank.aimDirection = direction;
            S.Write(__instance, "aimVelocity", 0f);
            aimConfirmation?.SetValue(__instance, true, null);
        }
        private static void AfterLaserRay(EntityState __instance, ref Ray __result)
        {
            CharacterBody body = Body(__instance);
            if (!IsTurret(body, 7) || !C.Get(lockWalkerAim) || !body.master) return;
            foreach (RoR2.CharacterAI.BaseAI ai in body.master.GetComponents<RoR2.CharacterAI.BaseAI>())
                if (ai.body == body && TryWalkerAim(ai, body, __result.origin, out Vector3 direction))
                { __result.direction = direction; return; }
        }
        private static void AfterMasterAwake(CharacterMaster __instance)
        {
            foreach (Profile p in profiles.Values) if (p.Turret && __instance.bodyPrefab && __instance.bodyPrefab.GetComponent<CharacterBody>()?.bodyIndex == p.Turret.bodyIndex) ApplyAI(__instance.gameObject, p);
        }
        private static void AfterLimit(CharacterMaster __instance, DeployableSlot slot, ref int __result)
        {
            CharacterBody body = __instance.GetBody(); if (!IsEngineer(body)) return;
            if (slot == DeployableSlot.EngiBubbleShield && profiles.ContainsKey(4)) __result = C.Get(maxShields);
            if (slot == DeployableSlot.EngiTurret && body.skillLocator && body.skillLocator.special && definitions.TryGetValue(body.skillLocator.special.skillDef, out Profile p) && p.Index >= 6)
                __result = Math.Min(body.skillLocator.special.maxStock, C.Get(p.Index == 6 ? maxGauss : maxWalker));
        }
        private sealed class WalkerSprint : MonoBehaviour
        {
            private void FixedUpdate() { CharacterBody body = GetComponent<CharacterBody>(); if (IsTurret(body, 7) && C.Get(alwaysSprint) && NetworkServer.active) body.isSprinting = true; }
        }
        private static void OnBodyStart(CharacterBody body) { if (IsTurret(body, 7) && !body.GetComponent<WalkerSprint>()) body.gameObject.AddComponent<WalkerSprint>(); }
        private static void OnDamageDealt(DamageReport report)
        {
            if (!NetworkServer.active || !IsTurret(report.attackerBody, 7) || !report.victimBody || !slowBuff || C.Get(walkerSlow) <= 0 || report.damageDealt <= 0f) return;
            if (report.damageInfo.damageType.damageSource == DamageSource.Primary && report.damageInfo.dotIndex == DotController.DotIndex.None && report.damageInfo.inflictor == report.damageInfo.attacker) report.victimBody.AddTimedBuff(slowBuff, 2f);
        }
        private static float SlowDenominator(float original, CharacterBody body) => slowBuff && body.HasBuff(slowBuff) && walkerSlow != null ? original + C.Get(walkerSlow) / 100f : original;
        private static IEnumerable<CodeInstruction> SlowIL(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList(); int anchor = list.FindIndex(code => code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo f && f.Name == "Slow50" && f.DeclaringType == typeof(RoR2Content.Buffs));
            int insertion = -1;
            for (int i = anchor - 1; i >= Math.Max(0, anchor - 60); i--)
                if (list[i].opcode == OpCodes.Ldc_R4 && Equals(list[i].operand, 1f) && i + 1 < list.Count && list[i + 1].opcode.Name.StartsWith("stloc", StringComparison.Ordinal)) { insertion = i + 1; break; }
            if (insertion < 0) { ThreeQsVanillaTweaks.Log.Warning("Engineer: Carbonizer slow calculation could not be located."); return list; }
            CodeInstruction store = list[insertion]; OpCode load = store.opcode == OpCodes.Stloc_0 ? OpCodes.Ldloc_0 : store.opcode == OpCodes.Stloc_1 ? OpCodes.Ldloc_1 : store.opcode == OpCodes.Stloc_2 ? OpCodes.Ldloc_2 : store.opcode == OpCodes.Stloc_3 ? OpCodes.Ldloc_3 : store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc;
            list.InsertRange(insertion + 1, new[] { new CodeInstruction(load, store.operand), new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Engineer), nameof(SlowDenominator))), new CodeInstruction(store.opcode, store.operand) }); return list;
        }
        private static float HealCoefficient(float original, CharacterBody body)
        {
            if (!IsEngineer(body) || !profiles.ContainsKey(1)) return original;
            EntityState state = body.GetComponents<EntityStateMachine>().Select(machine => machine.state).FirstOrDefault(value => value != null && value.GetType().Name.StartsWith("EngiLaserCutter", StringComparison.Ordinal));
            return state == null ? original : C.Get(stages[StageIndex(state)].Heal) / 100f;
        }
        private static IEnumerable<CodeInstruction> HealIL(IEnumerable<CodeInstruction> instructions)
        {
            Type data = typeof(CharacterBody).Assembly.GetType("RoR2.DamageMacroData"); FieldInfo attacker = data == null ? null : AccessTools.Field(data, "attackerBody");
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (attacker != null && code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo f && f.Name == "staticHealCoefficient")
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Ldfld, attacker); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Engineer), nameof(HealCoefficient))); }
            }
        }
        private static Profile AnimationProfile(EntityState state)
        {
            if (state == null) return null;
            string name = state.GetType().Name;
            int index = name == "ChargeGrenades" || name == "FireGrenades" ? 0 :
                name == "EnterEngiLaserCutter" || name.StartsWith("EngiLaserCutter", StringComparison.Ordinal) ? 1 :
                name == "FireMines" ? 2 : name == "FireSpiderMine" ? 3 : name == "FireBubbleShield" ? 4 :
                (state.GetType().Namespace ?? "").EndsWith("EngiMissilePainter", StringComparison.Ordinal) ? 5 :
                name == "PlaceWalkerTurret" ? 7 : name == "PlaceTurret" ? 6 : -1;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void BeforeSprint(CharacterBody __instance, ref bool value)
        {
            if (!value || !IsEngineer(__instance)) return;
            if (__instance.GetComponents<EntityStateMachine>().Any(machine => AnimationProfile(machine.state)?.Skill.Definition.cancelSprintingOnActivation == true)) value = false;
        }
        private static bool CancelSprint(CharacterBody body) => body.isSprinting && (!IsEngineer(body) || !profiles.ContainsKey(1));
        private static IEnumerable<CodeInstruction> SprintIL(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.Property(typeof(CharacterBody), "isSprinting").GetMethod;
            foreach (CodeInstruction code in instructions)
            {
                if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && Equals(code.operand, getter)) { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(Engineer), nameof(CancelSprint)); }
                yield return code;
            }
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (__result && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll) __result = skillSlot.stock >= skillSlot.maxStock; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out int? __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Index < 5 && p.Skill.ConsumeAll) { __state = __instance.stockToConsume; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, int? __state, Exception __exception) { if (__state.HasValue) __instance.stockToConsume = __state.Value; return __exception; }
        private static int Consume(Profile p, GenericSkill slot) => p.Skill.ConsumeAll ? slot.stock : S.Stock(C.Get(p.Consumed), slot.maxStock, 1);
        private static void BeforeDeduct(GenericSkill __instance, ref int count)
        {
            if (!IsEngineer(__instance.characterBody) || !definitions.TryGetValue(__instance.skillDef, out Profile p) || p.Index < 5) return;
            count = Consume(p, __instance);
            if (p.Index == 5) harpoonCosts.GetValue(__instance, _ => new Queue<int>()).Enqueue(count);
        }
        private static bool BeforeAddStock(GenericSkill __instance)
        {
            if (activeState == null || (activeState.GetType().Namespace ?? "").EndsWith("EngiMissilePainter", StringComparison.Ordinal) == false || Body(activeState) != __instance.characterBody || !IsEngineer(__instance.characterBody) || !definitions.TryGetValue(__instance.skillDef, out Profile p) || p.Index != 5 ||
                !harpoonCosts.TryGetValue(__instance, out Queue<int> costs) || costs.Count == 0) return true;
            __instance.stock = Math.Min(__instance.maxStock, __instance.stock + costs.Dequeue()); return false;
        }
        private static void AfterHarpoonFired(EntityState __instance)
        {
            GenericSkill slot = S.Read(__instance, "activatorSkillSlot") as GenericSkill;
            if (slot && IsEngineer(slot.characterBody) && harpoonCosts.TryGetValue(slot, out Queue<int> costs) && costs.Count > 0) costs.Dequeue();
        }
    }
}
