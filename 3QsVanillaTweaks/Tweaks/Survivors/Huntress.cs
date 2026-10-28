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
    internal static class Huntress
    {
        private const string Root = "Survivors - 02 Huntress";
        private static readonly string[] Names = { "Strafe", "Flurry", "Laser Glaive", "Laser Cyclone", "Blink", "Phase Blink", "Arrow Rain", "Ballista" };
        private static readonly string[] StateNames = { "FireSeekingArrow", "FireFlurrySeekingArrow", "ThrowGlaive", "ThrowCycloneKickable", "BlinkState", "MiniBlinkState", "ArrowRain", "FireArrowSnipe" };
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<string, S.Skill> skills = new Dictionary<string, S.Skill>();
        private static readonly Dictionary<SkillDef, S.Skill> definitions = new Dictionary<SkillDef, S.Skill>();
        private static readonly Dictionary<S.Skill, AimSettings> aiming = new Dictionary<S.Skill, AimSettings>();
        private static ConfigEntry<bool> enabled, statsEnabled, momentumEnabled, resetCrit, resetHit, includeFall, barrierGate, shieldGate, nectar;
        private static ConfigEntry<int> critPerStack, maxStacks, gainStacks, loseStacks, volley, critVolley, bounces, cycloneLimit, rainLimit, ballistaShots;
        private static ConfigEntry<float> buffDelay, buffInterval, grace, lossInterval, bounceBonus, juggleBonus, juggleGrace, returnDelay, blinkScale, phaseBlinkScale, rainHoldDuration, ballistaHoldDuration;
        private static ConfigEntry<bool> primaryAim, juggling;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static SkillDef ballistaPrimaryDefinition;
        private static BuffDef momentumBuff;
        private static Harmony harmony;
        private static GameObject rainPrefab, cyclonePrefab;
        private static string nativeCycloneDescription;
        private static float nativeRainRadius, nativeCycloneRadius, nativeSlowDuration, nativeFlurryReload, nativeCritReload;
        private static int nativeFlurryVolley, nativeCritVolley;
        private static readonly List<AreaInstance> areas = new List<AreaInstance>();
        [ThreadStatic] private static S.Skill aimOverride;
        [ThreadStatic] private static Orb arrivingOrb;
        [ThreadStatic] private static S.Skill ballistaBullet;
        [ThreadStatic] private static object cycloneThrow;
        private static readonly HashSet<RuntimeMethodHandle> entered = new HashSet<RuntimeMethodHandle>();
        private static readonly ConditionalWeakTable<object, BlinkData> blinks = new ConditionalWeakTable<object, BlinkData>();
        private static readonly ConditionalWeakTable<object, StopData> stops = new ConditionalWeakTable<object, StopData>();

        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Huntress changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool("Survivor-Stats - 02 Huntress", "Changes Enabled", true, "Enable Huntress stat changes. Restart to apply. Vanilla: False.");
            momentumEnabled = C.BindStartupBool(Root + " - Momentum", "Changes Enabled", true, "Enable Momentum. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true,
                "Enable changes to " + name + ". Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            if (momentumEnabled.Value)
            {
                BindMomentum();
                var loader = new GameObject("3QsHuntressMomentumContent");
                UnityEngine.Object.DontDestroyOnLoad(loader);
                loader.AddComponent<ContentLoader>();
            }
            RoR2Application.onLoad += Load;
        }
        // Start runs after BepInEx has registered the plugin, before content catalogues are built.
        private sealed class ContentLoader : MonoBehaviour
        {
            private void Start()
            {
                try
                {
                    momentumBuff = ScriptableObject.CreateInstance<BuffDef>();
                    momentumBuff.name = "3QS_HUNTRESS_MOMENTUM";
                    momentumBuff.canStack = true;
                    momentumBuff.isDebuff = false;
                    momentumBuff.buffColor = new Color(1f, 0.82f, 0.25f);
                    try
                    {
                        var original = Addressables.LoadAssetAsync<BuffDef>("RoR2/Base/CritOnUse/bdFullCrit.asset").WaitForCompletion();
                        if (original) momentumBuff.iconSprite = original.iconSprite;
                    }
                    catch (Exception iconError) { ThreeQsVanillaTweaks.Log.Warning("Momentum icon could not be loaded: " + iconError.Message); }
                    S.Write(momentumBuff, "ignoreGrowthNectar", !C.Get(nectar));
                    R2API.ContentAddition.AddBuffDef(momentumBuff);
                }
                catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Momentum buff could not be registered: " + error); }
                UnityEngine.Object.Destroy(gameObject);
            }
        }
        private static void BindMomentum()
        {
            string section = Root + " - Momentum";
            critPerStack = S.Int(section, "Crit Per Stack (%)", 1, 0, "Critical strike chance gained per stack.");
            maxStacks = S.Int(section, "Maximum Stacks", 20, 0, "Maximum stacks of Momentum. -1 is unlimited.", -1);
            buffDelay = S.Float(section, "Buff Delay", 3f, 0f, "Seconds of sprinting before gaining the first stacks.");
            buffInterval = S.Float(section, "Buff Interval", 0.5f, 0f, "Seconds between stack gains while sprinting.", 0.001f);
            gainStacks = S.Int(section, "Stacks Granted Per Interval", 1, 0, "Stacks gained each interval.");
            grace = S.Float(section, "Grace Period", 3f, 0f, "Seconds without sprinting before losing stacks. -1 prevents stacks being lost this way.", -1f);
            lossInterval = S.Float(section, "Loss Interval", 0.2f, 0f, "Seconds between stack losses while not sprinting.", 0.001f);
            loseStacks = S.Int(section, "Stacks Lost Per Interval", 1, 0, "Stacks lost each interval.");
            resetCrit = S.Bool(section, "Reset On Crit", false, false, "Reset Momentum when a critical hit deals damage.");
            resetHit = S.Bool(section, "Reset On Hit", true, false, "Reset Momentum when you take damage.");
            includeFall = S.Bool(section, "Including Fall Damage", false, false, "Allow fall damage to reset Momentum.");
            barrierGate = S.Bool(section, "Barrier Gates Hit Reset", true, false, "Keep Momentum if the damage is completely absorbed by barrier.");
            shieldGate = S.Bool(section, "Shield Gates Hit Reset", false, false, "Keep Momentum if the damage is completely absorbed by shield.");
            nectar = S.Bool(section, "Growth Nectar", true, false, "Count Momentum as one buff towards Growth Nectar.");
        }
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Huntress.HuntressWeapon." + name) ??
            typeof(EntityState).Assembly.GetType("EntityStates.Huntress." + name) ?? typeof(EntityState).Assembly.GetType("EntityStates.Huntress.Weapon." + name);
        private static Type CycloneState(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Huntress.Cyclone." + name);
        private static object Sample(Type type) => EntityStateCatalog.InstantiateState(type);
        private static bool IsHuntress(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static S.Skill Skill(object state)
        {
            if (state == null) return null;
            for (Type type = state.GetType(); type != null; type = type.BaseType)
                if (skills.TryGetValue(type.Name, out S.Skill skill)) return skill;
            return null;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            if (name == "OnEnter" && !entered.Add(method.MethodHandle)) return;
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(Huntress), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Huntress), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Huntress), finalizer));
        }
        private static GameObject ProjectileAsset(object value)
        {
            if (value is GameObject gameObject) return gameObject;
            if (value == null) return null;
            return S.Read(value, "gameObject") as GameObject;
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Huntress/HuntressBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Huntress");
                if (statsEnabled.Value) { stats = new S.Stats("Survivor-Stats - 02 Huntress", prefabBody); stats.Apply(prefabBody); }
                if (stats != null || momentumEnabled.Value) Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                if (momentumEnabled.Value)
                {
                    Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterStats));
                    Stage.onStageStartGlobal += AfterStageStart;
                }
                var defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily)
                    .SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(StateNames[i]);
                    SkillDef definition = defs.FirstOrDefault(d => d.activationState.stateType == type ||
                        i == 6 && d.activationState.stateType?.Name == "BeginArrowRain" || i == 7 && d.activationState.stateType?.Name == "BeginArrowSnipe");
                    if (type == null || !definition) { ThreeQsVanillaTweaks.Log.Warning("Huntress skill unavailable: " + Names[i]); continue; }
                    var skill = new S.Skill(Root + " - " + Names[i], definition, type, switches[Names[i]]);
                    BindSkill(skill, i);
                    skill.Loaded = true; skills[StateNames[i]] = skill; definitions[definition] = skill;
                    skill.ApplyDefinition();
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(definition, () => ExtraStats(skill), skill.Proc == null ? (Func<float>)null : () => C.Get(skill.Proc));
                    Patch(i == 1 ? State("FireSeekingArrow") : type, "OnEnter", nameof(BeforeEnter), nameof(AfterEnter));
                }
                InstallHooks();
                C.EffectiveSettingsChanged += ApplySettings;
                ApplySettings();
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Huntress changes could not be loaded: " + error); }
        }
        private static void BindSkill(S.Skill s, int index)
        {
            s.Bind(index == 3 ? 7f : index == 4 ? 6f : (float?)null, index == 3 ? 1 : (int?)null,
                index == 2 || index == 3 || index == 4 ? true : (bool?)null, damage: false,
                durationField: index == 4 || index == 5 ? "duration" : null, nativeConsumeOverride: index == 3 ? 1 : (int?)null);
            if (index < 2)
            {
                s.BindDamage(S.Value(s.Sample, "orbDamageCoefficient") * 100f, index == 0 ? 170f : (float?)null, S.Value(s.Sample, "orbProcCoefficient", 1f));
                float interval = S.Value(s.Sample, "baseArrowReloadDuration", 0.1f);
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", index == 0 ? 120f : 80f, index == 0 ? 120f : 80f, "Arrow travel speed in metres per second.", 0.01f);
                aiming[s] = new AimSettings(s.Section, index == 0 ? 80f : 60f);
                if (index == 1)
                {
                    nativeFlurryVolley = (int)S.Value(s.Sample, "maxArrowCount", 3);
                    nativeCritVolley = (int)S.Value(s.Sample, "critMaxArrowCount", 6);
                    nativeFlurryReload = interval; nativeCritReload = S.Value(s.Sample, "critBaseArrowReloadDuration", interval);
                    volley = S.Int(s.Section, "Volley", 3, nativeFlurryVolley, "Arrows fired per volley.", 1);
                    critVolley = S.Int(s.Section, "Crit Volley", 6, nativeCritVolley, "Arrows fired per critical volley.", 1);
                }
            }
            if (index == 2)
            {
                s.BindDamage(S.Value(s.Sample, "damageCoefficient") * 100f, null, S.Value(s.Sample, "glaiveProcCoefficient", 0.8f));
                bounces = S.Int(s.Section, "Bounces", 6, (int)S.Value(s.Sample, "maxBounceCount"), "Number of glaive bounces.");
                float native = (S.Value(s.Sample, "damageCoefficientPerBounce", 1.1f) - 1f) * 100f;
                bounceBonus = S.Float(s.Section, "Damage Increase Per Bounce (%)", 20f, native, "Increase the glaive's current damage per bounce.");
                float speed = S.Value(s.Sample, "glaiveTravelSpeed", 120f);
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", speed, speed, "Glaive travel speed in metres per second.", 0.01f);
                aiming[s] = new AimSettings(s.Section, 60f);
                primaryAim = S.Bool(s.Section, "(Client) Use Primary Lock On Values", true, false, "Use the equipped primary's targeting settings.");
            }
            if (index == 3)
            {
                nativeCycloneDescription = S.Read(s.Definition, "projectileSkillDescriptionToken") as string;
                cyclonePrefab = ProjectileAsset(S.Read(s.Sample, "projectilePrefab"));
                var overlap = cyclonePrefab.GetComponent<ProjectileOverlapAttack>();
                s.DamageFactor = overlap.damageCoefficient;
                s.BindDamage(S.Value(s.Sample, "damageCoefficient") * s.DamageFactor * 100f, 200f,
                    cyclonePrefab.GetComponent<ProjectileController>().procCoefficient * overlap.overlapProcCoefficient, true);
                float interval = overlap.resetInterval > 0f ? overlap.resetInterval : 1f / overlap.fireFrequency;
                s.Interval = S.Float(s.Section, "Damage Interval", 0.2f, interval, "Seconds between damage ticks. 0.2 seconds is 5 ticks per second.", 0.001f);
                nativeCycloneRadius = RadiusOf(cyclonePrefab);
                s.Radius = S.Float(s.Section, "AOE Radius", nativeCycloneRadius, nativeCycloneRadius, "Cyclone radius in metres.", 0.01f);
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the cyclone. Vanilla: None.");
                juggling = S.Bool(s.Section, "Enable Juggling", false, true, "Allow a returning cyclone to be juggled by using the skill again.");
                juggleBonus = S.Float(s.Section, "Damage Increase Per Juggle (%)", 400f, S.Value(s.Sample, "damageCoefficientPerCombo") * s.DamageFactor * 100f, "Extra base damage per tick for each successful juggle.");
                juggleGrace = S.Float(s.Section, "Juggle Grace Period", 0.5f, S.Value(Sample(State("CatchCyclone")), "baseDuration"), "Seconds after a catch in which a juggle can be performed.");
                cycloneLimit = S.Int(s.Section, "Maximum Cyclones", 0, 1, "Maximum active cyclones per player. 0 is unlimited. Always 1 while juggling is enabled.");
                returnDelay = S.Float(s.Section, "Delay Before Returning", 2f, 0f, "Seconds the cyclone lingers after stopping before returning.");
                nativeSlowDuration = S.Value(Sample(CycloneState("SlowDown")), "duration", 1f);
                float speed = S.Value(Sample(CycloneState("FlyOut")), "speed", 40f);
                s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", 90f, speed, "Outbound cyclone speed in metres per second.", 0.01f);
            }
            if (index == 4 || index == 5)
            {
                var entry = S.Float(s.Section, "Scale Distance With Movement Speed (%)", 50f, 100f, "How much movement speed changes blink distance. 0 keeps the base distance; 100 uses full movement speed scaling.");
                if (index == 4) blinkScale = entry; else phaseBlinkScale = entry;
            }
            if (index == 6 || index == 7)
            {
                object aimingState = index == 6 ? s.Sample : Sample(State("AimArrowSnipe"));
                float nativeHold = S.Value(aimingState, "maxDuration", 5f);
                var hold = S.Float(s.Section, "Aiming Time", Math.Max(5f, nativeHold), nativeHold,
                    "Seconds you can aim in the air before automatically firing. -1 allows unlimited aiming.", -1f);
                if (index == 6) rainHoldDuration = hold; else ballistaHoldDuration = hold;
            }
            if (index == 6)
            {
                rainPrefab = ProjectileAsset(S.Read(s.Sample, "projectilePrefab"));
                var zone = rainPrefab.GetComponent<ProjectileDotZone>();
                float interval = 1f / Math.Min(zone.resetFrequency, zone.fireFrequency);
                s.DamageFactor = zone.damageCoefficient;
                s.Interval = S.Float(s.Section, "Damage Interval", 0.25f, interval, "Seconds between damage ticks.", 0.001f);
                s.BindDamage(S.Value(s.Sample, "damageCoefficient") * s.DamageFactor * 100f, 100f,
                    rainPrefab.GetComponent<ProjectileController>().procCoefficient * zone.overlapProcCoefficient, true);
                s.Duration = S.Float(s.Section, "AOE Duration", 8f, zone.lifetime, "Seconds the rain lasts. -1 lasts until the effect is removed.", -1f);
                nativeRainRadius = S.Value(s.Sample, "arrowRainRadius", RadiusOf(rainPrefab));
                s.Radius = S.Float(s.Section, "AOE Radius", 11.5f, nativeRainRadius, "Rain radius in metres.", 0.01f);
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the rain. Vanilla: None.");
                rainLimit = S.Int(s.Section, "Maximum Instances Of AOE", -1, -1, "Maximum active rain areas per player. The oldest is removed when exceeded. -1 is unlimited.", -1);
            }
            if (index == 7)
            {
                ballistaPrimaryDefinition = S.Read(Sample(State("AimArrowSnipe")), "primarySkillDef") as SkillDef;
                ballistaShots = S.Int(s.Section, "Shots", 3, ballistaPrimaryDefinition ? ballistaPrimaryDefinition.baseMaxStock : 3,
                    "Number of shots available each time Ballista is used.", 1);
                s.BindDamage(S.Value(s.Sample, "damageCoefficient") * 100f, null, S.Value(s.Sample, "procCoefficient", 1f));
                float interval = S.Value(s.Sample, "baseDuration", 0.1f);
                s.Interval = S.Float(s.Section, "Damage Interval", interval, interval, "Seconds between shots before attack speed is applied.", 0.001f);
            }
        }
        private static void InstallHooks()
        {
            if (skills.Count > 0)
            {
                Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                Patch(typeof(SkillDef), "OnExecute", nameof(BeforeStockPayment), finalizer: nameof(AfterStockPayment));
            }
            if (momentumEnabled.Value || skills.Values.Any(s => s.Damage != null))
                Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), nameof(AfterDamage));
            if (aiming.Count > 0)
            {
                Patch(typeof(HuntressTracker), "SearchForTarget", nameof(BeforeSearch), finalizer: nameof(AfterSearch));
                Patch(typeof(SkillDef).Assembly.GetType("RoR2.Skills.HuntressTrackingSkillDef"), "HasTarget", nameof(BeforeHasTarget));
            }
            if (skills.ContainsKey("FireSeekingArrow") || skills.ContainsKey("FireFlurrySeekingArrow") || skills.ContainsKey("ThrowGlaive"))
            {
                Patch(typeof(OrbManager), "AddOrb", nameof(BeforeOrbAdded));
                Patch(typeof(GenericDamageOrb), "Begin", nameof(BeforeArrowBegin));
                if (skills.ContainsKey("FireFlurrySeekingArrow")) Patch(typeof(HuntressFlurryArrowOrb), "Begin", postfix: nameof(BeforeArrowBegin));
                Patch(typeof(GenericDamageOrb), "OnArrival", nameof(BeforeOrbArrival), finalizer: nameof(AfterOrbArrival));
                Patch(typeof(LightningOrb), "Begin", nameof(BeforeGlaiveBegin));
                Patch(typeof(LightningOrb), "OnArrival", nameof(BeforeOrbArrival), finalizer: nameof(AfterOrbArrival));
            }
            if (skills.ContainsKey("ThrowCycloneKickable") || skills.ContainsKey("ArrowRain"))
            {
                var launch = AccessTools.Method(typeof(ProjectileManager), "FireProjectile", new[] { typeof(FireProjectileInfo) });
                harmony.Patch(launch, prefix: new HarmonyMethod(typeof(Huntress), nameof(BeforeProjectileLaunch)));
                Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
            }
            if (skills.ContainsKey("ThrowCycloneKickable"))
            {
                Type def = typeof(SkillDef).Assembly.GetType("RoR2.Skills.HuntressCycloneSkillDef");
                Patch(def, "CanExecute", nameof(BeforeCycloneCanExecute));
                Patch(def, "GetCurrentIcon", postfix: nameof(AfterCycloneIcon));
                Patch(def, "GetCurrentDescriptionToken", postfix: nameof(AfterCycloneDescription));
                Patch(def, "GetRechargeInterval", postfix: nameof(AfterCycloneRecharge));
                Patch(def, "GetRechargeRate", postfix: nameof(AfterCycloneRechargeRate));
                Patch(State("ThrowCycloneKickable"), "FireLaserCyclone", nameof(BeforeCycloneFire), finalizer: nameof(AfterCycloneFire));
                Patch(typeof(GenericSkill), "DeductStock", nameof(BeforeCycloneDeduct));
                Patch(State("CatchCyclone"), "OnEnter", nameof(BeforeCatch));
                Patch(State("CatchCyclone"), "FixedUpdate", nameof(BeforeCatchUpdate));
                Patch(CycloneState("BaseCycloneState"), "OnEnter", postfix: nameof(AfterCycloneStateEnter));
                Patch(CycloneState("FlyOut"), "OnEnter", nameof(BeforeFlyOut));
                Patch(CycloneState("SlowDown"), "OnEnter", postfix: nameof(AfterSlowEnter));
                Patch(CycloneState("SlowDown"), "FixedUpdate", nameof(BeforeSlowUpdate));
            }
            if (skills.ContainsKey("FireArrowSnipe"))
            {
                Patch(typeof(SkillDef), "GetMaxStock", postfix: nameof(AfterBallistaMaxStock));
                Patch(typeof(GenericBulletBaseState), "FireBullet", nameof(BeforeBulletScope), finalizer: nameof(AfterBulletScope));
                Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet));
            }
            if (skills.ContainsKey("BlinkState") || skills.ContainsKey("MiniBlinkState"))
                Patch(State("BlinkState"), "FixedUpdate", nameof(BeforeBlinkUpdate));
            if (skills.ContainsKey("ArrowRain") || skills.ContainsKey("FireArrowSnipe"))
                Patch(State("BaseArrowBarrage"), "OnEnter", nameof(BeforeBarrageEnter));
        }
        private static void ApplySettings()
        {
            if (stats != null)
            {
                stats.Apply(prefabBody);
                foreach (CharacterBody body in UnityEngine.Object.FindObjectsOfType<CharacterBody>())
                    if (IsHuntress(body)) { stats.Apply(body); body.RecalculateStats(); }
            }
            if (momentumBuff) S.Write(momentumBuff, "ignoreGrowthNectar", !C.Get(nectar));
            if (momentumEnabled.Value)
            {
                R2API.LanguageAPI.Add("3QS_HUNTRESS_MOMENTUM_NAME", "Momentum");
                R2API.LanguageAPI.Add("3QS_HUNTRESS_MOMENTUM_DESC", MomentumDescription());
                // CharacterBody caches skillLocator in Awake; asset prefabs have not run Awake.
                SkillLocator locator = prefabBody.GetComponent<SkillLocator>();
                if (locator)
                {
                    var passive = locator.passiveSkill;
                    passive.enabled = true; passive.skillNameToken = "3QS_HUNTRESS_MOMENTUM_NAME";
                    passive.skillDescriptionToken = "3QS_HUNTRESS_MOMENTUM_DESC";
                    if (momentumBuff) passive.icon = momentumBuff.iconSprite;
                    locator.passiveSkill = passive;
                }
            }
            foreach (S.Skill skill in skills.Values)
            {
                skill.ApplyDefinition();
                string token = "3QS_HUNTRESS_" + skill.StateType.Name.ToUpperInvariant() + "_DESC";
                S.SetDescription(skill, token, () => Description(skill), skill.StateType.Name == "ThrowCycloneKickable" ? nativeCycloneDescription : null); skill.Definition.skillDescriptionToken = token;
                if (skill.StateType.Name == "ThrowCycloneKickable")
                {
                    if (!C.Get(juggling))
                        skill.Definition.keywordTokens = skill.Definition.keywordTokens.Where(keyword =>
                            keyword.IndexOf("JUGGL", StringComparison.OrdinalIgnoreCase) < 0 &&
                            Language.GetString(keyword).IndexOf("juggl", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
                    AccessTools.Field(skill.Definition.GetType(), "projectileSkillNameToken")?.SetValue(skill.Definition, skill.Definition.skillNameToken);
                    S.Write(skill.Definition, "projectileSkillRechargeInterval", -1f);
                    S.Write(skill.Definition, "projectileSkillRechargeSpeed", -1f);
                    var field = AccessTools.Field(skill.Definition.GetType(), "projectileSkillDescriptionToken");
                    field?.SetValue(skill.Definition, token);
                }
            }
        }
        private static string MomentumDescription()
        {
            string text = "While sprinting, gradually builds up a buff that increases your " + S.Damage("crit chance");
            if (C.Get(maxStacks) >= 0) text += " by up to " + S.Damage(S.Number((float)C.Get(maxStacks) * C.Get(critPerStack)) + "%");
            text += ".";
            if (C.Get(resetCrit)) text += " Landing a " + S.Damage("critical hit") + " resets the buff.";
            if (C.Get(resetHit)) text += " If you are hit, the buff resets.";
            return text;
        }
        private static string Description(S.Skill s)
        {
            string damage = s.Damage == null ? "" : S.DamageText(C.Get(s.Damage), s.Interval == null ? 0 : C.Get(s.Interval), s.StateType.Name == "ArrowRain" || s.StateType.Name == "ThrowCycloneKickable");
            string text;
            switch (s.StateType.Name)
            {
                case "FireSeekingArrow": text = "Fire a seeking arrow for " + damage + "."; break;
                case "FireFlurrySeekingArrow": text = "Draw back a volley of " + S.Damage(C.Get(volley).ToString()) + " seeking arrows for " + damage + " each. Critical strikes fire " + S.Damage(C.Get(critVolley).ToString()) + " arrows."; break;
                case "ThrowGlaive": text = "Throw a seeking glaive that bounces " + S.Damage(C.Get(bounces).ToString()) + " times for " + damage + ". Damage increases by " + S.Damage(S.Number(C.Get(bounceBonus)) + "%") + " per bounce."; break;
                case "ThrowCycloneKickable": text = "Throw a cyclone that deals " + damage + " to nearby enemies before returning." + (C.Get(juggling) ? " Catch and juggle it to add " + S.Damage(S.Number(C.Get(juggleBonus)) + "% damage") + " per tick." : ""); break;
                case "BlinkState": text = S.Utility("Disappear and teleport") + " forward."; break;
                case "MiniBlinkState": text = S.Utility("Disappear and teleport") + " a short distance. Can store multiple charges."; break;
                case "ArrowRain": text = "Teleport into the sky. Target an area to rain arrows, " + S.Utility("slowing") + " enemies and dealing " + damage + "."; break;
                case "FireArrowSnipe":
                    int shots = C.Get(ballistaShots);
                    text = "Teleport into the sky and fire " + S.Damage(shots.ToString()) + " powerful " + (shots == 1 ? "arrow" : "arrows") + " for " + damage + " each."; break;
                default: text = "Teleport into the sky and fire powerful arrows for " + damage + "."; break;
            }
            return s.AgileText + text;
        }
        private static string ExtraStats(S.Skill s)
        {
            var text = new StringBuilder();
            Action<string, string> add = (name, value) => text.Append("\n").Append(name).Append(": ").Append(S.Utility(value));
            if (s.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (s.Radius != null) add("AOE Radius", S.Number(C.Get(s.Radius)) + "m");
            if (s.Periodic && s.Interval != null) add("Damage Interval", S.Number(C.Get(s.Interval)) + "s");
            ConfigEntry<float> hold = s.StateType.Name == "ArrowRain" ? rainHoldDuration : s.StateType.Name == "FireArrowSnipe" ? ballistaHoldDuration : null;
            if (hold != null) add("Aiming Time", C.Get(hold) < 0f ? "Unlimited" : S.Number(C.Get(hold)) + "s");
            if (s.Duration != null) add("Duration", C.Get(s.Duration) < 0 ? "Unlimited" : S.Number(C.Get(s.Duration)) + "s");
            if (aiming.TryGetValue(s, out AimSettings aim))
            {
                if (s.StateType.Name == "ThrowGlaive" && C.Get(primaryAim) && primaryAim.Value) add("Lock On", "Equipped primary");
                else { add("Lock On Angle", S.Number(aim.Angle) + "°"); add("Lock On Distance", S.Number(aim.Distance) + "m"); }
            }
            if (s.StateType.Name == "ThrowCycloneKickable") { add("Return Delay", S.Number(C.Get(returnDelay)) + "s"); add("Maximum Cyclones", C.Get(juggling) ? "1" : C.Get(cycloneLimit) == 0 ? "Unlimited" : C.Get(cycloneLimit).ToString()); }
            return text.ToString();
        }
        private static void AfterBodyAwake(CharacterBody __instance)
        {
            if (!IsHuntress(__instance)) return;
            if (stats != null) stats.Apply(__instance);
            if (momentumEnabled.Value && !__instance.GetComponent<Momentum>()) __instance.gameObject.AddComponent<Momentum>().Body = __instance;
        }
        private static void AfterStats(CharacterBody __instance)
        {
            if (momentumEnabled.Value && momentumBuff && IsHuntress(__instance)) __instance.crit += __instance.GetBuffCount(momentumBuff.buffIndex) * (float)C.Get(critPerStack);
        }
        private static void AfterStageStart(Stage stage)
        {
            if (NetworkServer.active) foreach (Momentum momentum in UnityEngine.Object.FindObjectsOfType<Momentum>()) momentum.Clear();
        }
        // Plain timing model shared by all instances of this one passive.
        internal sealed class MomentumClock
        {
            internal int Stacks;
            private bool wasSprinting, gainStarted, lossStarted;
            private float sprintTime, idleTime, gainTime, lossTime;
            internal void Clear() { Stacks = 0; wasSprinting = gainStarted = lossStarted = false; sprintTime = idleTime = gainTime = lossTime = 0f; }
            internal int Tick(bool sprinting, float delta, float delay, float interval, int granted, int maximum, float idleGrace, float lossEvery, int lost)
            {
                if (sprinting)
                {
                    if (!wasSprinting) { sprintTime = gainTime = 0f; gainStarted = false; }
                    idleTime = lossTime = 0f; lossStarted = false;
                    sprintTime += delta;
                    if (!gainStarted && sprintTime + 0.00001f >= delay)
                    {
                        gainStarted = true; Stacks = AddStacks(Stacks, granted, maximum);
                        gainTime = Math.Max(0f, sprintTime - delay);
                    }
                    else if (gainStarted) gainTime += delta;
                    if (gainStarted)
                    {
                        int gains = (int)Math.Floor((gainTime + 0.000001f) / interval);
                        if (gains > 0) { Stacks = AddStacks(Stacks, (long)granted * gains, maximum); gainTime -= gains * interval; }
                    }
                }
                else
                {
                    sprintTime = gainTime = 0f; gainStarted = false;
                    idleTime += delta;
                    if (idleGrace >= 0f && idleTime + 0.00001f >= idleGrace)
                    {
                        if (!lossStarted) { lossStarted = true; Stacks = Math.Max(0, Stacks - lost); lossTime = Math.Max(0f, idleTime - idleGrace); }
                        else lossTime += delta;
                        int losses = (int)Math.Floor((lossTime + 0.000001f) / lossEvery);
                        if (losses > 0) { Stacks = (int)Math.Max(0L, (long)Stacks - (long)lost * losses); lossTime -= losses * lossEvery; }
                    }
                }
                wasSprinting = sprinting;
                if (maximum >= 0) Stacks = Math.Min(Stacks, maximum);
                return Stacks;
            }
            private static int AddStacks(int stacks, long add, int max) => (int)Math.Min(max < 0 ? int.MaxValue : max, (long)stacks + add);
        }
        private sealed class Momentum : MonoBehaviour
        {
            internal CharacterBody Body;
            private readonly MomentumClock clock = new MomentumClock();
            internal void Clear() { clock.Clear(); if (NetworkServer.active && Body && momentumBuff) Body.SetBuffCount(momentumBuff.buffIndex, 0); }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !Body || !momentumBuff || momentumBuff.buffIndex == BuffIndex.None) return;
                if (!Body.healthComponent || !Body.healthComponent.alive) { Clear(); return; }
                int count = clock.Tick(Body.isSprinting, Time.fixedDeltaTime, C.Get(buffDelay), C.Get(buffInterval), C.Get(gainStacks), C.Get(maxStacks), C.Get(grace), C.Get(lossInterval), C.Get(loseStacks));
                if (Body.GetBuffCount(momentumBuff.buffIndex) != count) Body.SetBuffCount(momentumBuff.buffIndex, count);
            }
        }
        private sealed class HitSnapshot { internal float Health, Shield, Barrier; }
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out HitSnapshot __state)
        {
            bool trackMomentum = momentumEnabled.Value && NetworkServer.active && (__instance.GetComponent<Momentum>() ||
                C.Get(resetCrit) && damageInfo.crit && damageInfo.attacker && damageInfo.attacker.GetComponent<Momentum>());
            __state = trackMomentum ? new HitSnapshot { Health = __instance.health, Shield = __instance.shield, Barrier = __instance.barrier } : null;
            if (arrivingOrb != null && arrivingOrb.target && arrivingOrb.target.healthComponent == __instance)
            {
                S.Skill s = SkillForOrb(arrivingOrb);
                if (s != null) damageInfo.damage *= BulletFalloff(C.Get(s.Falloff), (arrivingOrb.target.transform.position - arrivingOrb.origin).magnitude);
            }
            if (damageInfo.inflictor && damageInfo.inflictor.GetComponent<AreaInstance>() is AreaInstance area)
            {
                S.Skill s = area.Skill;
                float distance = Vector3.Distance(damageInfo.position, area.transform.position);
                if (s.Area != null) damageInfo.damage *= AreaFalloff(C.Get(s.Area), distance, C.Get(s.Radius));
                if (s.Falloff != null) damageInfo.damage *= BulletFalloff(C.Get(s.Falloff), distance);
            }
        }
        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo, HitSnapshot __state)
        {
            if (__state == null || !momentumEnabled.Value || damageInfo.rejected) return;
            float taken = __state.Health + __state.Shield + __state.Barrier - __instance.health - __instance.shield - __instance.barrier;
            if (taken <= 0f) return;
            Momentum victim = __instance.GetComponent<Momentum>();
            if (victim && !__instance.alive) victim.Clear();
            if (victim && C.Get(resetHit))
            {
                object combo = damageInfo.damageType;
                object raw = S.Read(combo, "damageType") ?? combo;
                bool fall = raw is DamageType type && (type & DamageType.FallDamage) != DamageType.Generic;
                bool protectedByBarrier = C.Get(barrierGate) && __instance.health >= __state.Health && __instance.shield >= __state.Shield;
                bool protectedByShield = C.Get(shieldGate) && __instance.health >= __state.Health && __instance.shield < __state.Shield;
                if ((!fall || C.Get(includeFall)) && !protectedByBarrier && !protectedByShield) victim.Clear();
            }
            if (damageInfo.crit && C.Get(resetCrit) && damageInfo.attacker)
            { Momentum attacker = damageInfo.attacker.GetComponent<Momentum>(); if (attacker) attacker.Clear(); }
        }
        internal static float BulletFalloff(S.BulletFalloff mode, float distance) => mode == S.BulletFalloff.Default ?
            0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f : mode == S.BulletFalloff.Buckshot ? 0.3f + Mathf.Clamp01(Mathf.InverseLerp(40f, 12f, distance)) * 0.7f : 1f;
        internal static float AreaFalloff(S.AreaFalloff mode, float distance, float radius)
        {
            float t = Mathf.Clamp01(distance / Math.Max(0.001f, radius));
            switch (mode) { case S.AreaFalloff.Linear: return 1f - t; case S.AreaFalloff.HalfLinear: return 1f - t * 0.5f;
                case S.AreaFalloff.QuarterLinear: return 1f - t * 0.75f; case S.AreaFalloff.SweetSpot: return t > 0.5f ? 0.25f : 1f; default: return 1f; }
        }
        private sealed class AimSettings
        {
            private readonly ConfigEntry<float> angle, distance;
            internal readonly ConfigEntry<bool> Crosshair;
            internal float Angle => Math.Min(C.Get(angle), angle.Value);
            internal float Distance => Math.Min(C.Get(distance), distance.Value);
            internal AimSettings(string section, float defaultDistance)
            {
                var tracker = prefabBody.GetComponent<HuntressTracker>();
                float nativeAngle = tracker.maxTrackingAngle, nativeDistance = tracker.maxTrackingDistance;
                float changedAngle = Math.Min(180f, nativeAngle * 1.3f);
                angle = C.Bind(section, "(Client) Lock On Angle", changedAngle, new ConfigDescription("Lock-on angle in degrees, capped at the host's value. Vanilla: " + S.Number(nativeAngle) + ".", new AcceptableValueRange<float>(0f, 180f)));
                distance = S.Float(section, "(Client) Lock On Distance", defaultDistance, nativeDistance, "Lock-on distance in metres, capped at the host's value.");
                Crosshair = C.BindBool(section, "Target Closest To Crosshair (Client)", true, "Prefer the enemy closest to the crosshair instead of the closest enemy. Vanilla: False.", true);
            }
        }
        private static AimSettings ResolveAim(CharacterBody body, S.Skill skill)
        {
            if (skill != null && skill.StateType.Name == "ThrowGlaive" && C.Get(primaryAim) && primaryAim.Value)
            {
                skill = body && body.skillLocator.primary && definitions.TryGetValue(body.skillLocator.primary.skillDef, out S.Skill primary) ? primary : null;
            }
            return skill != null && aiming.TryGetValue(skill, out AimSettings aim) ? aim : null;
        }
        private static void SearchNow(CharacterBody body, S.Skill skill)
        {
            HuntressTracker tracker = body.GetComponent<HuntressTracker>();
            InputBankTest input = body.GetComponent<InputBankTest>();
            if (!tracker || !input) return;
            S.Skill previous = aimOverride;
            try
            {
                aimOverride = skill;
                AccessTools.Method(typeof(HuntressTracker), "SearchForTarget").Invoke(tracker, new object[] { new Ray(input.aimOrigin, input.aimDirection) });
            }
            finally { aimOverride = previous; }
        }
        private sealed class AimSnapshot { internal float Angle, Distance, Priority; }
        private static bool BeforeSearch(HuntressTracker __instance, Ray aimRay, out AimSnapshot __state)
        {
            __state = null;
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            if (!IsHuntress(body)) return true;
            S.Skill skill = aimOverride;
            if (skill == null && body.skillLocator.primary) definitions.TryGetValue(body.skillLocator.primary.skillDef, out skill);
            AimSettings aim = ResolveAim(body, skill);
            if (aim == null) return true;
            __state = new AimSnapshot { Angle = __instance.maxTrackingAngle, Distance = __instance.maxTrackingDistance, Priority = S.Value(__instance, "maxPriorityTargetAngle", 10f) };
            __instance.maxTrackingAngle = aim.Angle; __instance.maxTrackingDistance = aim.Distance;
            S.Write(__instance, "maxPriorityTargetAngle", Math.Min(__state.Priority, aim.Angle));
            if (!aim.Crosshair.Value) return true;
            TeamMask mask = TeamMask.all;
            mask.RemoveTeam(body.teamComponent.teamIndex);
            var search = new BullseyeSearch { teamMaskFilter = mask, filterByLoS = true, searchOrigin = aimRay.origin,
                searchDirection = aimRay.direction, sortMode = BullseyeSearch.SortMode.Angle, maxDistanceFilter = aim.Distance, maxAngleFilter = aim.Angle };
            search.RefreshCandidates(); search.FilterOutGameObject(body.gameObject);
            AccessTools.Field(typeof(HuntressTracker), "trackingTarget").SetValue(__instance, search.GetResults().FirstOrDefault());
            return false;
        }
        private static Exception AfterSearch(HuntressTracker __instance, AimSnapshot __state, Exception __exception)
        {
            if (__state != null) { __instance.maxTrackingAngle = __state.Angle; __instance.maxTrackingDistance = __state.Distance; S.Write(__instance, "maxPriorityTargetAngle", __state.Priority); }
            return __exception;
        }
        private static bool BeforeHasTarget(GenericSkill skillSlot, ref bool __result)
        {
            if (!skillSlot || !definitions.TryGetValue(skillSlot.skillDef, out S.Skill skill) || !aiming.ContainsKey(skill)) return true;
            var body = skillSlot.characterBody;
            HuntressTracker tracker = body.GetComponent<HuntressTracker>();
            if (!tracker) return true;
            var previous = tracker.GetTrackingTarget();
            try { SearchNow(body, skill); __result = tracker.GetTrackingTarget(); }
            finally { AccessTools.Field(typeof(HuntressTracker), "trackingTarget").SetValue(tracker, previous); }
            return false;
        }
        private static void AfterBallistaMaxStock(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        {
            // The contextual primary is assigned and restocked by AimArrowSnipe.OnEnter.
            // Leave the special skill's own stocks and the normal primary unchanged.
            if (skillSlot && IsHuntress(skillSlot.characterBody) &&
                (__instance == ballistaPrimaryDefinition || __instance.activationState.stateType == skills["FireArrowSnipe"].StateType))
                __result = C.Get(ballistaShots);
        }
        private static void BeforeBarrageEnter(EntityState __instance)
        {
            if (!__instance.outer || !IsHuntress(__instance.outer.GetComponent<CharacterBody>())) return;
            string name = __instance.GetType().Name;
            ConfigEntry<float> hold = name == "ArrowRain" ? rainHoldDuration : name == "AimArrowSnipe" ? ballistaHoldDuration : null;
            if (hold == null) return;
            float duration = C.Get(hold);
            S.Write(__instance, "maxDuration", duration < 0f ? float.PositiveInfinity : duration);
        }
        private static void BeforeEnter(EntityState __instance)
        {
            S.Skill s = Skill(__instance);
            if (s == null || !__instance.outer) return;
            CharacterBody body = __instance.outer.GetComponent<CharacterBody>();
            if (!IsHuntress(body)) return;
            if (aiming.ContainsKey(s)) SearchNow(body, s);
            string name = __instance.GetType().Name;
            if (name == "FireSeekingArrow" || name == "FireFlurrySeekingArrow")
            {
                S.Write(__instance, "orbDamageCoefficient", C.Get(s.Damage) / 100f);
                S.Write(__instance, "orbProcCoefficient", C.Get(s.Proc));
                if (name == "FireFlurrySeekingArrow")
                {
                    S.Write(__instance, "maxArrowCount", C.Get(volley)); S.Write(__instance, "critMaxArrowCount", C.Get(critVolley));
                }
            }
            if (name == "ThrowGlaive")
            {
                S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f); S.Write(__instance, "glaiveProcCoefficient", C.Get(s.Proc));
                S.Write(__instance, "maxBounceCount", C.Get(bounces)); S.Write(__instance, "damageCoefficientPerBounce", 1f + C.Get(bounceBonus) / 100f);
                S.Write(__instance, "glaiveTravelSpeed", C.Get(s.ProjectileSpeed));
            }
            if (name == "ThrowCycloneKickable")
            {
                S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f / Math.Max(0.00001f, s.DamageFactor));
                S.Write(__instance, "damageCoefficientPerCombo", C.Get(juggleBonus) / 100f / Math.Max(0.00001f, s.DamageFactor));
                if (!C.Get(juggling)) S.Write(__instance, "comboCounter", (byte)0);
            }
            if (name == "BlinkState" || name == "MiniBlinkState")
            {
                S.Write(__instance, "duration", C.Get(s.Duration));
                blinks.Remove(__instance); blinks.Add(__instance, new BlinkData { Coefficient = S.Value(s.Sample, "speedCoefficient", 25f) });
            }
            if (name == "ArrowRain") { S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f / Math.Max(0.00001f, s.DamageFactor)); S.Write(__instance, "arrowRainRadius", C.Get(s.Radius)); }
            if (name == "FireArrowSnipe") { S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f); S.Write(__instance, "procCoefficient", C.Get(s.Proc)); S.Write(__instance, "baseDuration", C.Get(s.Interval)); }
        }
        private static void AfterEnter(EntityState __instance)
        {
            S.Skill s = Skill(__instance); if (s == null) return;
            if (s.StateType.Name == "FireFlurrySeekingArrow")
            {
                bool critical = S.Read(__instance, "isCrit") is bool crit && crit;
                int arrows = critical ? C.Get(critVolley) : C.Get(volley), native = critical ? nativeCritVolley : nativeFlurryVolley;
                float interval = critical ? nativeCritReload : nativeFlurryReload;
                float speed = __instance.outer.GetComponent<CharacterBody>().attackSpeed;
                float duration = S.Value(__instance, "duration");
                S.Write(__instance, "duration", duration + Math.Max(0, arrows - native) * interval / Math.Max(0.01f, speed));
            }
        }
        private sealed class BlinkData { internal float Coefficient; }
        private static void BeforeBlinkUpdate(EntityState __instance)
        {
            S.Skill skill = Skill(__instance);
            if (skill == null || !__instance.outer || !blinks.TryGetValue(__instance, out BlinkData data)) return;
            CharacterBody body = __instance.outer.GetComponent<CharacterBody>();
            float percent = C.Get(skill.StateType.Name == "MiniBlinkState" ? phaseBlinkScale : blinkScale);
            float baseSpeed = body.baseMoveSpeed + body.levelMoveSpeed * (body.level - 1f);
            if (body.isSprinting) baseSpeed *= body.sprintingSpeedMultiplier;
            float scaled = baseSpeed + (body.moveSpeed - baseSpeed) * percent / 100f;
            S.Write(__instance, "speedCoefficient", data.Coefficient * scaled / Math.Max(0.001f, body.moveSpeed));
        }
        private static S.Skill SkillForOrb(Orb orb)
        {
            if (orb is LightningOrb glaive && glaive.lightningType == LightningOrb.LightningType.HuntressGlaive &&
                glaive.attacker && IsHuntress(glaive.attacker.GetComponent<CharacterBody>()))
                return skills.TryGetValue("ThrowGlaive", out S.Skill g) ? g : null;
            if (orb is GenericDamageOrb arrow && arrow.attacker && IsHuntress(arrow.attacker.GetComponent<CharacterBody>()))
            {
                string name = orb.GetType().Name == "HuntressFlurryArrowOrb" ? "FireFlurrySeekingArrow" : orb.GetType().Name == "HuntressArrowOrb" ? "FireSeekingArrow" : "";
                return skills.TryGetValue(name, out S.Skill s) ? s : null;
            }
            return null;
        }
        private static void BeforeOrbAdded(Orb orb)
        {
            S.Skill skill = SkillForOrb(orb); if (skill == null) return;
            S.Write(orb, "procCoefficient", C.Get(skill.Proc));
            if (skill.ProjectileSpeed != null) S.Write(orb, "speed", C.Get(skill.ProjectileSpeed));
        }
        private static void BeforeArrowBegin(GenericDamageOrb __instance)
        { S.Skill s = SkillForOrb(__instance); if (s != null) __instance.speed = C.Get(s.ProjectileSpeed); }
        private static void BeforeGlaiveBegin(LightningOrb __instance)
        { S.Skill s = SkillForOrb(__instance); if (s != null) __instance.speed = C.Get(s.ProjectileSpeed); }
        private static void BeforeOrbArrival(Orb __instance, out Orb __state) { __state = arrivingOrb; arrivingOrb = SkillForOrb(__instance) == null ? null : __instance; }
        private static Exception AfterOrbArrival(Orb __state, Exception __exception) { arrivingOrb = __state; return __exception; }
        private static void BeforeBulletScope(object __instance, out S.Skill __state) { __state = ballistaBullet; ballistaBullet = Skill(__instance); }
        private static Exception AfterBulletScope(S.Skill __state, Exception __exception) { ballistaBullet = __state; return __exception; }
        private static void BeforeBullet(BulletAttack __instance)
        {
            if (ballistaBullet == null || ballistaBullet.StateType.Name != "FireArrowSnipe") return;
            __instance.procCoefficient = C.Get(ballistaBullet.Proc);
            __instance.falloffModel = C.Get(ballistaBullet.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(ballistaBullet.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        }
        private sealed class StockPayment { internal int Cost; }
        private static void BeforeStockPayment(SkillDef __instance, GenericSkill skillSlot, out StockPayment __state)
        {
            __state = null;
            if (definitions.TryGetValue(__instance, out S.Skill s) && s.ConsumeAll) { __state = new StockPayment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; }
        }
        private static Exception AfterStockPayment(SkillDef __instance, StockPayment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (definitions.TryGetValue(__instance, out S.Skill s) && s.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (definitions.TryGetValue(__instance, out S.Skill s) && s.RechargeAll) __result = skillSlot.maxStock; }
        private static bool Matches(ProjectileController controller, GameObject prefab) => controller && prefab && controller.owner && IsHuntress(controller.owner.GetComponent<CharacterBody>()) &&
            (controller.catalogIndex >= 0 && prefab.GetComponent<ProjectileController>() && controller.catalogIndex == prefab.GetComponent<ProjectileController>().catalogIndex || controller.gameObject.name == prefab.name + "(Clone)");
        private static void BeforeProjectileLaunch(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsHuntress(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            if (fireProjectileInfo.projectilePrefab == cyclonePrefab && skills.TryGetValue("ThrowCycloneKickable", out S.Skill cyclone)) fireProjectileInfo.speedOverride = C.Get(cyclone.ProjectileSpeed);
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            bool rain = Matches(__instance, rainPrefab), cyclone = Matches(__instance, cyclonePrefab);
            if (!rain && !cyclone) return;
            S.Skill skill = skills[rain ? "ArrowRain" : "ThrowCycloneKickable"];
            __instance.procCoefficient = C.Get(skill.Proc);
            var zone = __instance.GetComponent<ProjectileDotZone>();
            var overlap = __instance.GetComponent<ProjectileOverlapAttack>();
            float interval = C.Get(skill.Interval);
            if (zone)
            {
                zone.lifetime = C.Get(skill.Duration); zone.resetFrequency = 1f / interval;
                zone.fireFrequency = Math.Max(zone.fireFrequency, 1f / interval); zone.overlapProcCoefficient = 1f;
            }
            if (overlap) { overlap.resetInterval = interval; overlap.fireFrequency = Math.Max(overlap.fireFrequency, 1f / interval); overlap.overlapProcCoefficient = 1f; }
            float original = rain ? nativeRainRadius : nativeCycloneRadius;
            float scale = C.Get(skill.Radius) / Math.Max(0.001f, original);
            if (rain) __instance.transform.localScale *= scale;
            else foreach (HitBox box in __instance.GetComponent<HitBoxGroup>().hitBoxes) if (box) box.transform.localScale *= scale;
            if (rain)
            {
                foreach (Component component in __instance.GetComponents<Component>())
                {
                    if (component == zone || component == __instance) continue;
                    float duration = C.Get(skill.Duration) > 0f ? C.Get(skill.Duration) : float.MaxValue;
                    if (component.GetType().Name == "DestroyOnTimer") S.Write(component, "duration", duration);
                    if (component is ProjectileSimple) S.Write(component, "lifetime", duration);
                }
            }
            if (NetworkServer.active)
            {
                AreaInstance marker = __instance.gameObject.AddComponent<AreaInstance>(); marker.Skill = skill; marker.Owner = __instance.owner;
                marker.IsRain = rain; areas.RemoveAll(a => !a); areas.Add(marker);
                int limit = rain ? C.Get(rainLimit) : C.Get(juggling) ? 1 : C.Get(cycloneLimit) == 0 ? -1 : C.Get(cycloneLimit);
                if (limit >= 0)
                {
                    var active = areas.Where(a => a && a.IsRain == rain && a.Owner == marker.Owner).ToList();
                    for (int i = 0; i < active.Count - limit; i++) NetworkServer.Destroy(active[i].gameObject);
                }
                if (rain && C.Get(skill.Duration) == 0f) NetworkServer.Destroy(marker.gameObject);
                if (rain && skill.CooldownAfterDuration && marker.Owner.GetComponent<CharacterBody>().skillLocator.special is GenericSkill slot)
                    slot.SetBlockedCooldownSkillState(true);
            }
        }
        private static float RadiusOf(GameObject prefab)
        {
            var group = prefab.GetComponent<HitBoxGroup>();
            if (!group || group.hitBoxes.Length == 0 || !group.hitBoxes[0]) return 1f;
            var box = group.hitBoxes[0];
            var sphere = box.GetComponent<SphereCollider>();
            if (sphere) return sphere.radius * box.transform.lossyScale.x;
            var capsule = box.GetComponent<CapsuleCollider>();
            if (capsule) return capsule.radius * box.transform.lossyScale.x;
            var cube = box.GetComponent<BoxCollider>();
            return (cube ? cube.size.x : 1f) * box.transform.lossyScale.x * 0.5f;
        }
        private sealed class AreaInstance : MonoBehaviour
        {
            internal S.Skill Skill;
            internal GameObject Owner;
            internal bool IsRain;
            private void OnDestroy()
            {
                areas.Remove(this);
                if (NetworkServer.active && IsRain && Skill != null && Skill.CooldownAfterDuration && Owner && !areas.Any(a => a && a.IsRain && a.Owner == Owner))
                {
                    GenericSkill slot = Owner.GetComponent<CharacterBody>().skillLocator.special;
                    if (slot && slot.skillDef == Skill.Definition) slot.SetBlockedCooldownSkillState(false);
                }
            }
        }
        private static bool BeforeCycloneCanExecute(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (!definitions.TryGetValue(__instance, out S.Skill s)) return true;
            bool stock = skillSlot.stock >= __instance.requiredStock && (!s.RequireAll || skillSlot.stock >= skillSlot.maxStock);
            __result = stock && __instance.IsReady(skillSlot) && skillSlot.stateMachine && !skillSlot.stateMachine.HasPendingState() && skillSlot.stateMachine.CanInterruptState(__instance.interruptPriority);
            return false;
        }
        private static void AfterCycloneDescription(SkillDef __instance, ref string __result)
        {
            if (definitions.ContainsKey(__instance) && !C.Get(juggling)) __result = __instance.skillDescriptionToken;
        }
        private static void AfterCycloneIcon(SkillDef __instance, ref Sprite __result) { if (definitions.ContainsKey(__instance) && !C.Get(juggling)) __result = __instance.icon; }
        private static void AfterCycloneRecharge(SkillDef __instance, ref float __result) { if (definitions.ContainsKey(__instance)) __result = __instance.baseRechargeInterval; }
        private static void AfterCycloneRechargeRate(SkillDef __instance, ref float __result) { if (definitions.ContainsKey(__instance)) __result = 1f; }
        private static void BeforeCycloneFire(object __instance, out object __state) { __state = cycloneThrow; cycloneThrow = __instance; }
        private static Exception AfterCycloneFire(object __state, Exception __exception) { cycloneThrow = __state; return __exception; }
        private static void BeforeCycloneDeduct(GenericSkill __instance, ref int __0)
        {
            if (cycloneThrow == null || !skills.TryGetValue("ThrowCycloneKickable", out S.Skill s) || __instance.skillDef != s.Definition) return;
            bool kick = S.Value(cycloneThrow, "comboCounter") > 0f;
            __0 = kick ? s.ConsumeAll ? __instance.stock : s.Definition.stockToConsume : 0;
        }
        private static void BeforeCatch(EntityState __instance)
        {
            if (!skills.ContainsKey("ThrowCycloneKickable") || !__instance.outer || !IsHuntress(__instance.outer.GetComponent<CharacterBody>())) return;
            S.Write(__instance, "baseDuration", C.Get(juggleGrace));
        }
        private static bool BeforeCatchUpdate(EntityState __instance)
        {
            if (!skills.TryGetValue("ThrowCycloneKickable", out S.Skill s) || !__instance.outer) return true;
            CharacterBody body = __instance.outer.GetComponent<CharacterBody>();
            if (!IsHuntress(body)) return true;
            GenericSkill slot = body.skillLocator.secondary;
            bool affordable = slot && slot.stock >= Math.Max(s.Definition.requiredStock, s.Definition.stockToConsume) && (!s.RequireAll || slot.stock >= slot.maxStock);
            if (!C.Get(juggling) || !affordable)
            {
                if (Util.HasEffectiveAuthority(body.gameObject)) __instance.outer.SetNextStateToMain();
                return false;
            }
            return true;
        }
        private static bool OurCyclone(EntityState state) => state.outer && Matches(state.outer.GetComponent<ProjectileController>(), cyclonePrefab);
        private static void AfterCycloneStateEnter(EntityState __instance)
        {
            if (!OurCyclone(__instance)) return;
            var controller = __instance.outer.GetComponent<ProjectileController>();
            var overlap = __instance.outer.GetComponent<ProjectileOverlapAttack>();
            controller.procCoefficient = C.Get(skills["ThrowCycloneKickable"].Proc);
            if (overlap) overlap.overlapProcCoefficient = 1f;
        }
        private static void BeforeFlyOut(EntityState __instance)
        {
            if (OurCyclone(__instance)) S.Write(__instance, "speed", C.Get(skills["ThrowCycloneKickable"].ProjectileSpeed));
        }
        private sealed class StopData { internal float Since = -1f; }
        private static void AfterSlowEnter(EntityState __instance) { if (OurCyclone(__instance)) { stops.Remove(__instance); stops.Add(__instance, new StopData()); } }
        private static bool BeforeSlowUpdate(EntityState __instance)
        {
            if (!OurCyclone(__instance) || !stops.TryGetValue(__instance, out StopData stop) || S.Value(__instance, "fixedAge") + Time.fixedDeltaTime < nativeSlowDuration) return true;
            var rigidbody = __instance.outer.GetComponent<Rigidbody>(); if (rigidbody) rigidbody.velocity = Vector3.zero;
            if (stop.Since < 0f) stop.Since = Time.fixedTime;
            if (Time.fixedTime - stop.Since >= C.Get(returnDelay) && Util.HasEffectiveAuthority(__instance.outer.gameObject))
                __instance.outer.SetNextState(EntityStateCatalog.InstantiateState(CycloneState("Return")));
            return false;
        }
    }
}
