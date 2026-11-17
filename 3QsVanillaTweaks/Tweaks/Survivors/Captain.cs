using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Captain.Weapon;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
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
    internal static class Captain
    {
        private const string Root = "Survivors - 11 Captain", StatsRoot = "Survivor-Stats - 11 Captain";
        private const string ChargedMachine = "3QSCaptainChargedShotgun";
        private static readonly string[] Names = { "Vulcan Shotgun", "Charged Shotgun", "Power Tazer", "Orbital Probe", "OGM-72 Diablo Strike", "Orbital Supply Beacon" };
        private static readonly string[] Activations = { "ChargeCaptainShotgun", "", "FireTazer", "SetupAirstrike", "SetupAirstrikeAlt", "SetupSupplyDrop" };
        private static readonly string[] Calls = { "CallSupplyDropHealing", "CallSupplyDropShocking", "CallSupplyDropEquipmentRestock", "CallSupplyDropHacking" };
        private static readonly string[] BeaconNames = { "Healing Beacon", "Shocking Beacon", "Resupply Beacon", "Hacking Beacon" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal ConfigEntry<int> Pellets;
            internal ConfigEntry<float> MinSpread, MaxSpread, Pitch, Yaw, FullSpread, FullDamage, MinCharge, MaxCharge;
            internal ConfigEntry<bool> NoFalloff, ShareReload;
        }
        private sealed class BeaconOptions
        {
            internal int Kind;
            internal ConfigEntry<bool> Enabled;
            internal ConfigEntry<float> Duration;
            internal GameObject Prefab;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
            internal object Target;
            internal GenericSkill Slot;
            internal int StockBefore;
        }
        private sealed class PlacementInput { internal bool Released; }
        private static readonly ConditionalWeakTable<EntityState, PlacementInput> placementInputs = new ConditionalWeakTable<EntityState, PlacementInput>();
        private sealed class BodyData
        {
            internal float PrimaryReady, SecondaryReady, BeaconReady;
            internal bool BeaconCancelHeld;
            internal Stage Stage;
            internal readonly Dictionary<GenericSkill, int> Uses = new Dictionary<GenericSkill, int>();
        }
        private sealed class ProjectileMarker : MonoBehaviour { internal Profile Profile; }
        private sealed class TazerChain { internal GameObject Owner; internal readonly List<HealthComponent> Hit = new List<HealthComponent>(); }
        private sealed class DropContext { internal HackedPickup Previous; }
        private sealed class HackedPickup : MonoBehaviour { internal bool Rolled, Command; internal CharacterMaster Buyer; }
        private sealed class SupportData
        {
            internal readonly Dictionary<BeaconMarker, float> Sources = new Dictionary<BeaconMarker, float>();
            internal int Count;
            internal float HealTimer;
        }
        private sealed class BeaconMarker : MonoBehaviour
        {
            internal BeaconOptions Options;
            internal GameObject Owner;
            internal CharacterMaster Master;
            internal TeamIndex Team;
            internal float Damage, Crit;
            internal bool Active;
            internal float Born, HackProgress;
            internal HoldoutZoneController Holdout;
            internal GameObject RangeVisual;
            internal GameObject LifetimeBox;
            internal GameObject LifetimeCanvas, LifetimeContainer;
            internal Behaviour LifetimeEnergy;
            internal HealingWard NativeHealingWard;
            internal RectTransform LifetimeFill, HackFill;
            internal int DaisyPulses, DaisyCycle;
            internal bool WarnedDaisy, WarnedLifetime;
            internal readonly List<Material> RangeMaterials = new List<Material>();
            private void OnDestroy() { foreach (var material in RangeMaterials) if (material) UnityEngine.Object.Destroy(material); }
            private void FixedUpdate()
            {
                if (!Active || !Options.Enabled.Value) return;
                float duration = C.Get(Options.Duration);
                if (Holdout) { Holdout.enabled = C.Get(holdouts); Holdout.baseRadius = C.Get(beaconRadius); }
                UpdateRangeCircle(this);
                if (!NetworkServer.active) return;
                bool hacking = Options.Kind == 3 && GetComponents<EntityStateMachine>().Any(m => m.state?.GetType().Name == "HackingInProgressState" || m.state?.GetType().Name == "UnlockTargetState");
                if (BeaconShouldExpire(ActiveAge(), duration, hacking)) { NetworkServer.Destroy(gameObject); return; }
                if (Options.Kind == 0)
                    foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                        if (body && body.healthComponent && body.healthComponent.alive && TeamComponent.GetObjectTeam(body.gameObject) == Team && Vector3.Distance(body.corePosition, transform.position) <= EffectiveRadius())
                        {
                            SupportData data = support.GetOrCreateValue(body);
                            data.Sources[this] = Time.fixedTime + Math.Max(0.05f, C.Get(supportDecay));
                        }
                UpdateBeaconDaisy(this);
            }
            internal float ActiveAge() => Math.Max(0f, Time.fixedTime - Born);
            internal float EffectiveRadius() => Holdout && Holdout.enabled && Holdout.currentRadius > 0.01f ? Holdout.currentRadius : C.Get(beaconRadius);
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<SkillDef, BeaconOptions> beaconDefs = new Dictionary<SkillDef, BeaconOptions>();
        private static readonly Dictionary<SkillDef, string[]> beaconKeywords = new Dictionary<SkillDef, string[]>();
        private static readonly Dictionary<int, BeaconOptions> beacons = new Dictionary<int, BeaconOptions>();
        private static readonly HashSet<MethodBase> stateHooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<CharacterBody, BodyData> bodies = new ConditionalWeakTable<CharacterBody, BodyData>();
        private static readonly ConditionalWeakTable<CharacterBody, SupportData> support = new ConditionalWeakTable<CharacterBody, SupportData>();
        private static readonly ConditionalWeakTable<LightningOrb, TazerChain> tazerOrbs = new ConditionalWeakTable<LightningOrb, TazerChain>();
        private static readonly ConditionalWeakTable<ProjectileMarker, TazerChain> tazerProjectiles = new ConditionalWeakTable<ProjectileMarker, TazerChain>();
        private static ConfigEntry<bool> enabled, statsEnabled, microEnabled, rework, hiddenRealms;
        private static CharacterBody prefabBody;
        private static SkillDef chargedDef;
        private static BuffDef supportBuff;
        private static S.Stats stats;
        private static Harmony harmony;
        private static object shotgunSample;
        private static ConfigEntry<int> probes, tazerBounces, microBase, microStack, microSelf, microAllies, stageLimit, beaconMaximum, resupplyUses;
        private static ConfigEntry<float> microRadius, microRecharge, microFrequency, tazerShock, tazerRange, diabloDelay, friendlyDamage;
        private static ConfigEntry<bool> showBeaconIndicators, showBeaconSelectionIndicators;
        private static ConfigEntry<float> indicatorOffsetX, indicatorOffsetY;
        private static ConfigEntry<float> beaconRadius, beaconDamage, supportHeal, supportAttack, supportArmour, supportDamage, supportDecay, shockDuration, shockInterval, shockDamage, rechargeTime, barrier, hackDiscount, hackSpeed, hackCommand;
        private static ConfigEntry<bool> microAttackSpeed, tazerSlow, friendlyFire, sharedCooldown, beaconCrit, extraMaximum, holdouts, renameSupport, nectar, stackSupport, shockSlow, shockCrit;
        private static ConfigEntry<bool> rechargeUses, rechargeEquipment, rechargePrimary, rechargeSecondary, rechargeUtility, rechargeSpecial, resupplyBeacons, hackUse, hackAll, hackPrinters, breakHackedPrinters;
        private static string holdoutKeyword = "KEYWORD_HOLDOUTBEACON";
        private static int beaconConsume = 1, beaconRequired = 1, beaconRecharge = 1;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static TazerChain activeTazer;
        [ThreadStatic] private static bool tazerHitPending;
        [ThreadStatic] private static HackedPickup activeDrop;
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Captain.Weapon." + name);
        private static Type DropState(string name) => typeof(EntityState).Assembly.GetType("EntityStates.CaptainSupplyDrop." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsCaptain(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = t.GetField(name, f); if (field != null) return field.GetValue(obj);
                PropertyInfo property = t.GetProperty(name, f); if (property != null) return property.GetValue(obj, null);
            }
            return null;
        }
        private static float Number(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible value ? Convert.ToSingle(value) : fallback;
        private static void SetValue(object obj, string name, object value)
        {
            if (obj == null) return;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo f = t.GetField(name, flags); if (f != null) { f.SetValue(obj, ConvertValue(value, f.FieldType)); return; }
                PropertyInfo p = t.GetProperty(name, flags); if (p?.GetSetMethod(true) != null) { p.SetValue(obj, ConvertValue(value, p.PropertyType), null); return; }
            }
        }
        private static object ConvertValue(object value, Type type) => value == null || type.IsInstanceOfType(value) ? value : type.IsEnum ? Enum.ToObject(type, value) : Convert.ChangeType(value, type);
        private static GameObject Prefab(object value)
        {
            if (value is GameObject go) return go;
            if (value == null) return null;
            MethodInfo m = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == "op_Implicit" && x.ReturnType == typeof(GameObject) && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType.IsInstanceOfType(value));
            return m?.Invoke(null, new[] { value }) as GameObject;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Captain changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Captain stat changes. Restart to apply. Vanilla: False.");
            microEnabled = C.BindStartupBool(Root + " - Defensive Microbots", "Changes Enabled", true, "Enable Defensive Microbots changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names.Concat(BeaconNames)) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            rework = C.BindStartupBool(Root + " - Vulcan Shotgun", "Rework Primary", true, "Fire uncharged shots while held and add Charged Shotgun as a secondary. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            hiddenRealms = S.Bool(Root + " - General", "Orbital Skills In Hidden Realms", true, false, "Allow orbital skills to be used in Hidden Realms.");
            ContentManager.collectContentPackProviders += add => add(new CaptainContent());
            RoR2Application.onLoad += Load;
        }
        // Blue copy of the primary's icon, so Charged Shotgun is distinguishable in the loadout.
        private static Sprite ChargedIcon(Sprite original)
        {
            if (!original) return original;
            try { return S.RecolourIcon(original, "3QSChargedShotgunIcon", .58f, true); }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("3QSChargedShotgunIcon: retaining the primary's icon because conversion failed: " + e.Message);
                return original;
            }
        }
        private sealed class CaptainContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.Captain";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                if (switches[Names[0]].Value && switches[Names[1]].Value && rework.Value)
                {
                    SkillDef primary = AllSkills().First(d => d.activationState.stateType == State("ChargeCaptainShotgun"));
                    chargedDef = UnityEngine.Object.Instantiate(primary);
                    ((ScriptableObject)chargedDef).name = chargedDef.skillName = "3QSChargedShotgun";
                    chargedDef.skillNameToken = "3QS_CAPTAIN_CHARGED_NAME"; chargedDef.skillDescriptionToken = "3QS_CAPTAIN_CHARGED_DESCRIPTION";
                    chargedDef.activationState = new SerializableEntityStateType(typeof(ChargedShotgunState));
                    chargedDef.baseRechargeInterval = 2f; chargedDef.baseMaxStock = chargedDef.rechargeStock = chargedDef.stockToConsume = chargedDef.requiredStock = 1;
                    chargedDef.beginSkillCooldownOnSkillEnd = true; chargedDef.mustKeyPress = true;
                    chargedDef.icon = ChargedIcon(primary.icon);
                    var machine = prefabBody.gameObject.AddComponent<EntityStateMachine>(); machine.customName = ChargedMachine;
                    machine.initialStateType = machine.mainStateType = new SerializableEntityStateType(typeof(Idle));
                    var network = prefabBody.GetComponent<NetworkStateMachine>();
                    if (network) SetValue(network, "stateMachines", ((Read(network, "stateMachines") as EntityStateMachine[]) ?? Array.Empty<EntityStateMachine>()).Concat(new[] { machine }).ToArray());
                    GenericSkill secondary = prefabBody.GetComponents<GenericSkill>().First(s => s.skillFamily && s.skillFamily.variants.Any(v => v.skillDef.activationState.stateType == State("FireTazer")));
                    var variants = secondary.skillFamily.variants.ToList(); variants.Add(new SkillFamily.Variant { skillDef = chargedDef }); secondary.skillFamily.variants = variants.ToArray();
                    R2API.LanguageAPI.Add(chargedDef.skillNameToken, "Charged Shotgun");
                    content.skillDefs.Add(new[] { chargedDef });
                    content.entityStateTypes.Add(new[] { typeof(QuickShotgunState), typeof(ChargedShotgunState), typeof(ChargedShotgunFireState) });
                }
                else if (switches[Names[0]].Value && rework.Value) content.entityStateTypes.Add(new[] { typeof(QuickShotgunState) });
                if (switches[Names[5]].Value)
                {
                    BindBeaconGeneral();
                    if (switches[BeaconNames[0]].Value)
                    {
                        supportBuff = ScriptableObject.CreateInstance<BuffDef>(); supportBuff.name = "3QSCaptainSupport";
                        supportBuff.canStack = false; supportBuff.isDebuff = false; supportBuff.buffColor = new Color(0.3f, 0.85f, 0.55f);
                        supportBuff.iconSprite = RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null;
                        content.buffDefs.Add(new[] { supportBuff });
                    }
                    foreach (SkillDef def in AllSkills())
                    {
                        int kind = Array.FindIndex(Calls, n => def.activationState.stateType == State(n));
                        if (kind < 0 || !switches[BeaconNames[kind]].Value) continue;
                        GameObject beacon = Prefab(Read(EntityStateCatalog.InstantiateState(def.activationState.stateType), "supplyDropPrefab"));
                        if (!beacon) continue;
                        if (!beacon.GetComponent<HoldoutZoneController>()) beacon.AddComponent<HoldoutZoneController>();
                        ConfigureHoldout(beacon.GetComponent<HoldoutZoneController>(), 60f);
                        beacon.GetComponent<HoldoutZoneController>().enabled = false;
                    }
                }
                args.ReportProgress(1f); yield break;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args) { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }
        private static SkillDef[] AllSkills() => prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Captain), prefix), postfix == null ? null : new HarmonyMethod(typeof(Captain), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Captain), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "OnExit", "FixedUpdate", "Fire" })
                for (Type t = type; t != null && t != typeof(BaseState) && t != typeof(EntityState); t = t.BaseType)
                {
                    MethodInfo m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (m == null) continue;
                    if (stateHooks.Add(m)) harmony.Patch(m, prefix: new HarmonyMethod(typeof(Captain), nameof(BeforeState)), postfix: new HarmonyMethod(typeof(Captain), nameof(AfterState)), finalizer: new HarmonyMethod(typeof(Captain), nameof(EndScope)));
                    break;
                }
        }
        private static void Load()
        {
            try
            {
                if (!prefabBody) prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Captain/CaptainBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Captain");
                shotgunSample = EntityStateCatalog.InstantiateState(State("FireCaptainShotgun"));
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefabBody); Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody)); }
                Patch(typeof(CaptainOrbitalSkillDef), "get_isAvailable", postfix: nameof(AfterOrbitalAvailable));
                Patch(typeof(CaptainSupplyDropController), "get_canUseOrbitalSkills", postfix: nameof(AfterControllerAvailable));
                SkillDef[] defs = AllSkills();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value || i == 1 && !chargedDef) continue;
                    try
                    {
                        SkillDef def = i == 1 ? chargedDef : defs.FirstOrDefault(d => d.activationState.stateType == State(Activations[i]));
                        if (!def) throw new MissingMemberException("Captain skill unavailable: " + Names[i]);
                        Type attack = i <= 1 ? State("FireCaptainShotgun") : i == 3 ? State("CallAirstrike1") : i == 4 ? State("CallAirstrikeAlt") : def.activationState.stateType;
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, attack, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                        Hook(attack); if (attack != def.activationState.stateType && i != 1) Hook(def.activationState.stateType);
                        S.SetDescription(p.Skill, "3QS_CAPTAIN_SKILL_" + i, () => Description(p), Read(def, "exhaustedDescriptionToken") as string);
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? (Func<float>)null : () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Captain skill could not be loaded: " + Names[i] + ": " + e); }
                }
                if (microEnabled.Value) LoadMicrobots();
                Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                Patch(typeof(SkillDef), "GetMaxStock", postfix: nameof(AfterMaxStock));
                Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillTick));
                Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet));
                Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), nameof(AfterDamage), nameof(AfterDamageScope));
                Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                if (profiles.ContainsKey(5)) LoadBeacons(defs);
                if (profiles.ContainsKey(2))
                {
                    Patch(typeof(OrbManager), "AddOrb", nameof(BeforeOrb));
                    Patch(typeof(LightningOrb), "OnArrival", nameof(BeforeTazerArrival), finalizer: nameof(AfterTazerArrival));
                }
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Captain changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(changedCooldown: i == 1 ? 2f : i == 2 ? 4f : i == 3 ? 9f : i == 5 ? 60f : (float?)null, damage: false, nativeConsumeOverride: i == 5 ? 1 : (int?)null);
            if (i <= 1)
            {
                C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.Default, "Damage falloff over distance. Vanilla: Default.");
                s.BindDamage(Number(sample, "damageCoefficient") * 100f, 120f, Number(sample, "procCoefficient", 1f));
                p.Pellets = S.Int(s.Section, "Pellet Count", 10, (int)Number(sample, "bulletCount", 8), "Pellets fired per shot.", 1);
                p.MinSpread = S.Float(s.Section, "Minimum Spread", Number(sample, "minSpread"), Number(sample, "minSpread"), "Minimum pellet spread in degrees.");
                float nativeBloom = prefabBody.spreadBloomCurve.Evaluate(0f);
                float nativeSpread = Number(sample, "maxSpread") + nativeBloom;
                p.MaxSpread = S.Float(s.Section, "Maximum Spread", nativeSpread, nativeSpread, "Maximum pellet spread in degrees.");
                p.Pitch = S.Float(s.Section, "Spread Pitch Scale", Number(sample, "spreadPitchScale", 0.5f), Number(sample, "spreadPitchScale", 0.5f), "Vertical pellet spread multiplier.");
                p.Yaw = S.Float(s.Section, "Spread Yaw Scale", Number(sample, "spreadYawScale", 1f), Number(sample, "spreadYawScale", 1f), "Horizontal pellet spread multiplier.");
                if (i == 1)
                {
                    object charge = EntityStateCatalog.InstantiateState(State("ChargeCaptainShotgun"));
                    p.MinCharge = S.Float(s.Section, "Min Charge Time", 0.3f, Number(charge, "baseMinChargeDuration"), "Minimum charge time in seconds, before attack speed is applied.");
                    p.MaxCharge = S.Float(s.Section, "Max Charge Time", 1.5f, Number(charge, "baseChargeDuration"), "Full charge time in seconds, before attack speed is applied.", 0.001f);
                    p.FullDamage = S.Float(s.Section, "Damage At Max Charge (%)", 200f, Number(sample, "damageCoefficient") * 100f, "Damage per pellet at full charge as a percentage of base damage.");
                    float tight = Number(sample, "maxSpread") + prefabBody.spreadBloomCurve.Evaluate(1f);
                    p.FullSpread = S.Float(s.Section, "Minimum Spread At Max Charge", tight * 0.5f, tight, "Maximum pellet spread at full charge, in degrees.");
                    p.NoFalloff = S.Bool(s.Section, "No Damage Falloff At Max Charge", true, false, "Remove damage falloff when fully charged.");
                    p.ShareReload = S.Bool(s.Section, "Share Reload With Primary", true, true, "Use the primary's reload animation between shots.");
                }
                return;
            }
            if (i == 5) { BindBeaconGeneral(); return; }
            p.Projectile = Prefab(Read(sample, "projectilePrefab"));
            if (!p.Projectile) throw new MissingMemberException("Captain attack projectile unavailable: " + Names[i]);
            float coefficient = Number(sample, "damageCoefficient", 1f), proc = Number(p.Projectile.GetComponent<ProjectileController>(), "procCoefficient", 1f);
            Component explosion = p.Projectile.GetComponent<ProjectileExplosion>();
            if (explosion) { coefficient *= Number(explosion, "blastDamageCoefficient", 1f); proc *= Number(explosion, "blastProcCoefficient", 1f); }
            s.BindDamage(coefficient * 100f, null, proc);
            if (i == 2)
            {
                tazerBounces = S.Int(s.Section, "Number Of Bounces", 3, 0, "Additional enemies the shock can bounce to.");
                tazerRange = S.Float(s.Section, "Bounce Range", 20f, 0f, "Maximum bounce distance in metres.");
                tazerSlow = S.Bool(s.Section, "Inflict Slow", true, false, "Slow enemies for the same duration as the shock.");
                tazerShock = S.Float(s.Section, "Shock Duration", 5f, 5f, "Shock duration in seconds at a proc coefficient of 1.");
            }
            else
            {
                s.Radius = S.Float(s.Section, "AOE Radius", Number(explosion, "blastRadius", Number(sample, "airstrikeRadius")), Number(explosion, "blastRadius", Number(sample, "airstrikeRadius")), "Explosion radius in metres.");
                S.AreaFalloff native = AreaFrom(Read(explosion, "falloffModel"));
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", i == 4 ? S.AreaFalloff.None : native, "Damage falloff from the centre of the explosion. Vanilla: " + native + ".");
                if (i == 3) probes = S.Int(s.Section, "Number Of Probes", 3, 3, "Probes available each time Orbital Probe is used.", 1);
                else
                {
                    diabloDelay = S.Float(s.Section, "Delay Before Hit", 20f, Number(explosion, "lifetime", 20f), "Seconds before the strike lands.", 0.001f);
                    friendlyFire = S.Bool(s.Section, "Does Friendly Fire", true, true, "Allow the strike to damage allies and Captain.");
                    friendlyDamage = S.Float(s.Section, "Friendly Fire Damage (%)", 20000f, coefficient * 50f, "Damage dealt to allies as a percentage of base damage.");
                }
            }
            if (p.Projectile.GetComponents<Component>().Any(c => c && Number(c, "desiredForwardSpeed", -1f) > 0f)) s.BindProjectileSpeed(p.Projectile);
        }
        private static void BindBeaconGeneral()
        {
            if (stageLimit != null) return;
            string s = Root + " - " + Names[5];
            showBeaconIndicators = C.BindBool(s, "Show Beacon Indicators", true, "Show beacon icons and cooldowns above the special skill.", clientSide: true);
            showBeaconSelectionIndicators = C.BindBool(s, "Show Beacon Selection Indicators", true, "Show beacon icons and cooldowns in the beacon selection menu.", clientSide: true);
            indicatorOffsetX = C.Bind(s, "Beacon Indicator Horizontal Offset", -20f, "Horizontal position relative to the centre of the special skill icon. Positive values move right.", clientSide: true);
            indicatorOffsetY = C.Bind(s, "Beacon Indicator Vertical Offset", 80f, "Height above the special skill icon. Positive values move up.", clientSide: true);
            stageLimit = S.Int(s, "Stage Limit", 0, 1, "Uses of each equipped beacon per stage. 0 is unlimited. Each extra stock adds one use when limited.");
            sharedCooldown = S.Bool(s, "Both Beacons Share Cooldown", false, false, "Share stocks and cooldown between the two beacons when unlimited. Limited beacons share the cooldown between uses.");
            beaconRadius = S.Float(s, "Radius", 15f, 10f, "Beacon effect radius in metres.");
            object sample = EntityStateCatalog.InstantiateState(State("CallSupplyDropHealing"));
            beaconDamage = S.Float(s, "Enemy Hit Damage (%)", 4000f, Number(sample, "impactDamageCoefficient", 20f) * 100f, "Damage dealt when a beacon lands on an enemy, as a percentage of base damage.");
            beaconCrit = S.Bool(s, "Can Crit", true, true, "Allow beacon impacts to critically strike.");
            beaconMaximum = S.Int(s, "Maximum Amount", 3, 2, "Maximum active beacons per Captain. Removes the oldest when exceeded. 0 is unlimited.");
            extraMaximum = S.Bool(s, "Extra Stocks Increase Amount", true, false, "Increase the active beacon limit by one per extra special stock.");
            holdouts = S.Bool(s, "Supply Beacons Count As Holdout Zones", true, false, "Allow supply beacons to trigger holdout zone items.");
        }
        private static void LoadMicrobots()
        {
            string s = Root + " - Defensive Microbots";
            Type type = typeof(EntityState).Assembly.GetType("EntityStates.CaptainDefenseMatrixItem.DefenseMatrixOn");
            object sample = EntityStateCatalog.InstantiateState(type);
            microRadius = S.Float(s, "Projectile Eraser Radius", Number(sample, "projectileEraserRadius", 20f), Number(sample, "projectileEraserRadius", 20f), "Maximum distance at which projectiles are intercepted, in metres.");
            microRecharge = S.Float(s, "Recharge Frequency", Number(sample, "baseRechargeFrequency", 0.5f), Number(sample, "baseRechargeFrequency", 0.5f), "Interceptions recharged per second before attack speed is applied.", 0.001f);
            microFrequency = S.Float(s, "Minimum Fire Frequency", Number(sample, "minimumFireFrequency", 10f), Number(sample, "minimumFireFrequency", 10f), "Minimum number of projectile checks each second.", 0.001f);
            microAttackSpeed = S.Bool(s, "Recharge Scales With Attack Speed", true, true, "Increase interception recharge speed with attack speed.");
            microBase = S.Int(s, "Projectiles Per Interception", 1, 1, "Projectiles destroyed per interception at one item stack.");
            microStack = S.Int(s, "Projectiles Per Extra Stack", 1, 1, "Additional projectiles destroyed per extra item stack.");
            var controller = prefabBody.GetComponent<CaptainDefenseMatrixController>();
            microSelf = S.Int(s, "Microbots Granted To Captain", controller ? controller.defenseMatrixToGrantPlayer : 1, controller ? controller.defenseMatrixToGrantPlayer : 1, "Microbot items granted to Captain at the start of a run.");
            microAllies = S.Int(s, "Microbots Granted To Mechanical Allies", controller ? controller.defenseMatrixToGrantMechanicalAllies : 1, controller ? controller.defenseMatrixToGrantMechanicalAllies : 1, "Microbot items granted to newly purchased mechanical allies.");
            Patch(type, "FixedUpdate", nameof(BeforeMicrobots), finalizer: nameof(EndScope));
            Patch(type, "GetItemStack", postfix: nameof(AfterMicrobotStack));
            Patch(type, "get_rechargeFrequency", postfix: nameof(AfterMicrobotRecharge));
            Patch(typeof(CaptainDefenseMatrixController), "Start", nameof(BeforeMicrobotGrant));
            Patch(typeof(CaptainDefenseMatrixController), "OnServerMasterSummonGlobal", nameof(BeforeMicrobotGrant));
            S.SetText(RoR2Content.Items.CaptainDefenseMatrix.descriptionToken, () => "Shoot down <style=cIsUtility>" + C.Get(microBase) + "</style> nearby projectiles <style=cStack>(+" + C.Get(microStack) + " per stack)</style> every <style=cIsUtility>" + S.Number(1f / C.Get(microRecharge)) + " seconds</style>" + (C.Get(microAttackSpeed) ? ". Recharge rate scales with <style=cIsDamage>attack speed</style>." : "."));
            var locator = prefabBody.GetComponent<SkillLocator>();
            if (locator) S.SetText(locator.passiveSkill.skillDescriptionToken, () => "Gain <style=cIsUtility>" + C.Get(microSelf) + " Defensive Microbots</style> that shoot down nearby projectiles." + (C.Get(microAllies) > 0 ? " Mechanical allies also gain <style=cIsUtility>" + C.Get(microAllies) + "</style>." : ""));
        }
        private static void BeforeMicrobots(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            Set(__state, __instance, "projectileEraserRadius", C.Get(microRadius));
            Set(__state, __instance, "baseRechargeFrequency", C.Get(microRecharge));
            Set(__state, __instance, "minimumFireFrequency", C.Get(microFrequency));
        }
        private static void AfterMicrobotStack(ref int __result) => __result = C.Get(microBase) + Math.Max(0, __result - 1) * C.Get(microStack);
        private static void AfterMicrobotRecharge(ref float __result) { if (!C.Get(microAttackSpeed)) __result = C.Get(microRecharge); }
        private static void BeforeMicrobotGrant(CaptainDefenseMatrixController __instance)
        { if (IsCaptain(__instance.GetComponent<CharacterBody>())) { __instance.defenseMatrixToGrantPlayer = C.Get(microSelf); __instance.defenseMatrixToGrantMechanicalAllies = C.Get(microAllies); } }
        private static void AfterOrbitalAvailable(ref bool __result) { if (C.Get(hiddenRealms)) __result = true; }
        private static void AfterControllerAvailable(CaptainSupplyDropController __instance, ref bool __result) { if (IsCaptain(__instance.GetComponent<CharacterBody>()) && C.Get(hiddenRealms)) __result = true; }
        private static void Apply()
        {
            if (!prefabBody) return;
            stats?.Apply(prefabBody);
            foreach (CharacterBody b in CharacterBody.readOnlyInstancesList) if (IsCaptain(b)) { stats?.Apply(b); b.MarkAllStatsDirty(); }
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 2) p.Skill.SetKeyword("KEYWORD_SHOCKING", true);
                if (p.Index == 3 || p.Index == 4) p.Skill.SetKeyword("KEYWORD_STUNNING", true);
            }
            if (profiles.TryGetValue(0, out Profile primary) && rework.Value)
            { primary.Skill.Definition.activationState = new SerializableEntityStateType(typeof(QuickShotgunState)); primary.Skill.Definition.mustKeyPress = false; }
            if (profiles.TryGetValue(1, out Profile charged)) charged.Skill.Definition.activationStateMachineName = C.Get(charged.ShareReload) ? profiles[0].Skill.Definition.activationStateMachineName : ChargedMachine;
            if (profiles.TryGetValue(3, out Profile orbital))
            {
                var call = Read(EntityStateCatalog.InstantiateState(State("SetupAirstrike")), "primarySkillDef") as SkillDef;
                if (call) call.baseMaxStock = C.Get(probes);
            }
            if (profiles.TryGetValue(5, out Profile supply))
            {
                beaconConsume = supply.Skill.Definition.stockToConsume; beaconRequired = supply.Skill.Definition.requiredStock; beaconRecharge = supply.Skill.Definition.rechargeStock;
                foreach (SkillDef def in beaconDefs.Keys)
                {
                    def.baseRechargeInterval = supply.Skill.Definition.baseRechargeInterval; def.baseMaxStock = Math.Max(1, supply.Skill.Definition.baseMaxStock);
                    def.rechargeStock = beaconRecharge; def.stockToConsume = 0; def.requiredStock = beaconRequired; def.dontAllowPastMaxStocks = false;
                    def.isCooldownBlockedUntilManuallyReset = false; def.beginSkillCooldownOnSkillEnd = supply.Skill.CooldownAfterDuration;
                    def.hideCooldown = false; def.hideStockCount = false;
                    def.cancelSprintingOnActivation = supply.Skill.Definition.cancelSprintingOnActivation; def.canceledFromSprinting = supply.Skill.Definition.canceledFromSprinting; def.isCombatSkill = supply.Skill.Definition.isCombatSkill;
                    def.keywordTokens = beaconKeywords[def].Concat(C.Get(holdouts) ? new[] { holdoutKeyword } : Array.Empty<string>()).Distinct().ToArray();
                }
                supply.Skill.SetKeyword(holdoutKeyword, C.Get(holdouts));
                supply.Skill.Definition.stockToConsume = 0; supply.Skill.Definition.requiredStock = 1;
            }
            if (supportBuff) { SetValue(supportBuff, "ignoreGrowthNectar", !C.Get(nectar)); }
        }
        private static void AfterBody(CharacterBody __instance) { if (IsCaptain(__instance)) stats?.Apply(__instance); }
        private static Profile ForState(EntityState state)
        {
            if (state is QuickShotgunState) return profiles.TryGetValue(0, out Profile primary) ? primary : null;
            if (state is ChargedShotgunState || state is ChargedShotgunFireState) return profiles.TryGetValue(1, out Profile charged) ? charged : null;
            if (state == null || state.GetType().Namespace != "EntityStates.Captain.Weapon") return null;
            string name = state.GetType().Name; int index = Array.IndexOf(Activations, name);
            if (name == "FireCaptainShotgun") index = 0;
            if (name.StartsWith("CallSupplyDrop", StringComparison.Ordinal)) index = 5;
            if (name == "CallAirstrikeAlt") index = 4;
            else if (name.StartsWith("CallAirstrike", StringComparison.Ordinal)) index = 3;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void Set(Scope scope, object target, string name, object value)
        {
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                if (f == null) continue;
                if (f.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(f, f.GetValue(null)));
                f.SetValue(f.IsStatic ? null : target, ConvertValue(value, f.FieldType)); return;
            }
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            BeaconMarker marker = Beacon(__instance);
            if (marker)
            {
                activeState = __instance;
                if (__instance.GetType().Name.StartsWith("CallSupplyDrop", StringComparison.Ordinal)) Set(__state, __instance, "impactDamageCoefficient", C.Get(beaconDamage) / 100f);
                if (marker.Options.Kind == 1) { Set(__state, __instance, "shockRadius", C.Get(beaconRadius)); Set(__state, __instance, "shockFrequency", 1f / C.Get(shockInterval)); }
                if (marker.Options.Kind == 3)
                {
                    if (__instance.GetType().Name == "HackingMainState") Set(__state, __instance, "baseRadius", C.Get(beaconRadius));
                    if (__instance.GetType().Name == "HackingInProgressState") Set(__state, __instance, "baseDuration", C.Get(hackSpeed));
                }
                if (__instance.GetType().Name == "UnlockTargetState")
                { __state.Target = Read(__instance, "target"); SetValue(__instance, "target", null); }
            }
            Profile p = ForState(__instance); if (p == null || !IsCaptain(Body(__instance))) return;
            activeState = __instance;
            if (p.Index <= 1)
            {
                Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
                Set(__state, __instance, "procCoefficient", C.Get(p.Skill.Proc)); Set(__state, __instance, "bulletCount", C.Get(p.Pellets));
            }
            else if (p.Skill.Damage != null) Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Index == 5) Set(__state, __instance, "impactDamageCoefficient", C.Get(beaconDamage) / 100f);
            if (p.Index == 5 && __originalMethod.Name == "OnEnter" && __instance.GetType().Name.StartsWith("CallSupplyDrop", StringComparison.Ordinal))
            { __state.Slot = Read(__instance, "activatorSkillSlot") as GenericSkill; if (__state.Slot) __state.StockBefore = __state.Slot.stock; }
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod, Scope __state)
        {
            if (__state?.Target != null) { SetValue(__instance, "target", __state.Target); if (__originalMethod.Name == "OnEnter" && NetworkServer.active) CompleteHack(Beacon(__instance), __state.Target as PurchaseInteraction); __state.Target = null; }
            if (__originalMethod.Name != "OnEnter") return;
            string name = __instance.GetType().Name;
            if (name.StartsWith("CallSupplyDrop", StringComparison.Ordinal) && IsCaptain(Body(__instance)))
            {
                object placement = Read(__instance, "placementInfo");
                if (Read(placement, "ok") is bool valid && valid && Read(__instance, "activatorSkillSlot") is GenericSkill slot)
                {
                    if (__state?.Slot && Read(__instance, "isAuthority") is bool authority && authority)
                        slot.stock = Math.Max(0, __state.StockBefore - (profiles[5].Skill.ConsumeAll ? __state.StockBefore : beaconConsume));
                    BeaconUsed(Body(__instance), slot);
                }
            }
            BeaconMarker marker = Beacon(__instance); if (!marker) return;
            if (marker.Options.Kind == 3 && name == "HackingInProgressState") marker.HackProgress = 0f;
            if (marker.Options.Kind == 3 && name == "UnlockTargetState") marker.HackProgress = 1f;
            if (__instance.GetType().Name == "HackingInProgressState" && Read(__instance, "target") is PurchaseInteraction printer && IsPrinter(printer))
            {
                var energy = marker.GetComponent<GenericEnergyComponent>();
                if (energy) SetValue(energy, "normalizedChargeRate", 1f / PrinterHackDuration(printer));
            }
            if (typeof(EntityState).Assembly.GetType("EntityStates.CaptainSupplyDrop.BaseMainState").IsInstanceOfType(__instance)) ActivateBeacon(marker, __instance);
        }
        private static Exception EndScope(EntityState __instance, Scope __state, Exception __exception)
        {
            if (__state != null)
            {
                if (__state.Target != null) SetValue(__instance, "target", __state.Target);
                for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value);
                activeState = __state.Previous;
            }
            return __exception;
        }
        private static void CopyShotgun(EntityState state)
        {
            for (Type t = shotgunSample.GetType(); t != null && t != typeof(BaseState); t = t.BaseType)
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!field.IsInitOnly && (field.IsPublic || field.IsDefined(typeof(SerializeField), true))) field.SetValue(state, field.GetValue(shotgunSample));
        }
        private static void StartShot(EntityState state, int index)
        {
            CopyShotgun(state); CharacterBody body = Body(state); if (!body) return;
            Profile p = profiles[index];
            SetValue(state, "damageCoefficient", C.Get(p.Skill.Damage) / 100f); SetValue(state, "procCoefficient", C.Get(p.Skill.Proc)); SetValue(state, "bulletCount", C.Get(p.Pellets));
            float reload = Number(shotgunSample, "baseDuration", 0.5f) / Math.Max(0.001f, body.attackSpeed);
            BodyData data = bodies.GetOrCreateValue(body);
            if (index == 0 || C.Get(p.ShareReload)) data.PrimaryReady = Time.fixedTime + reload;
            if (index == 1) data.SecondaryReady = Time.fixedTime + reload;
        }
        public sealed class QuickShotgunState : FireCaptainShotgun
        {
            public override void OnEnter() { StartShot(this, 0); base.OnEnter(); }
            public override InterruptPriority GetMinimumInterruptPriority() => fixedAge < Number(shotgunSample, "baseDuration", 0.5f) / Math.Max(0.001f, characterBody.attackSpeed) ? InterruptPriority.PrioritySkill : base.GetMinimumInterruptPriority();
        }
        public sealed class ChargedShotgunState : ChargeCaptainShotgun
        {
            private bool shotReleased, fullVisual;
            public override void OnEnter()
            {
                var scope = new Scope { Previous = activeState };
                try { Set(scope, this, "baseMinChargeDuration", C.Get(profiles[1].MinCharge)); Set(scope, this, "baseChargeDuration", Math.Max(C.Get(profiles[1].MinCharge), C.Get(profiles[1].MaxCharge))); base.OnEnter(); }
                finally { EndScope(this, scope, null); }
            }
            public override void FixedUpdate()
            {
                // The native charge update reads primary input. Advance its clock here,
                // then handle the secondary input without running that update.
                fixedAge += Time.fixedDeltaTime;
                characterBody.SetAimTimer(1f);
                float speed = Math.Max(0.001f, characterBody.attackSpeed), min = C.Get(profiles[1].MinCharge) / speed, max = Math.Max(C.Get(profiles[1].MinCharge), C.Get(profiles[1].MaxCharge)) / speed;
                if (!fullVisual && fixedAge >= max)
                {
                    fullVisual = true; var effect = Read(this, "chargeupVfxGameObject") as GameObject; if (effect) UnityEngine.Object.Destroy(effect);
                    var muzzle = Read(this, "muzzleTransform") as Transform; var prefab = Read(this, "holdChargeVfxPrefab") as GameObject;
                    if (muzzle && prefab) SetValue(this, "holdChargeVfxGameObject", UnityEngine.Object.Instantiate(prefab, muzzle));
                }
                if (!isAuthority) return;
                shotReleased |= !inputBank || !inputBank.skill2.down;
                if (shotReleased && fixedAge >= min) outer.SetNextState(new ChargedShotgunFireState { Charge = ChargeFraction(fixedAge, min, max) });
            }
        }
        public sealed class ChargedShotgunFireState : FireCaptainShotgun
        {
            public float Charge;
            public override void OnEnter() { StartShot(this, 1); base.OnEnter(); }
            public override InterruptPriority GetMinimumInterruptPriority() => fixedAge < Number(shotgunSample, "baseDuration", 0.5f) / Math.Max(0.001f, characterBody.attackSpeed) ? InterruptPriority.PrioritySkill : base.GetMinimumInterruptPriority();
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Charge); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Charge = reader.ReadSingle(); }
        }
        internal static float ChargeFraction(float age, float minimum, float maximum) => maximum <= minimum ? (age >= maximum ? 1f : 0f) : Mathf.Clamp01((age - minimum) / (maximum - minimum));
        private static void ConfigureBullet(BulletAttack bullet, Profile p, float charge)
        {
            bullet.bulletCount = (uint)C.Get(p.Pellets); bullet.procCoefficient = C.Get(p.Skill.Proc);
            CharacterBody body = bullet.owner ? bullet.owner.GetComponent<CharacterBody>() : null;
            if (!body) return;
            bullet.damage = body.damage * (p.Index == 1 ? Mathf.Lerp(C.Get(p.Skill.Damage), C.Get(p.FullDamage), charge) : C.Get(p.Skill.Damage)) / 100f;
            bullet.minSpread = p.Index == 1 ? Mathf.Lerp(C.Get(p.MinSpread), C.Get(p.FullSpread), charge) : C.Get(p.MinSpread);
            bullet.maxSpread = p.Index == 1 ? Mathf.Lerp(C.Get(p.MaxSpread), C.Get(p.FullSpread), charge) : C.Get(p.MaxSpread);
            if (p.Index == 0 && !rework.Value)
            {
                float wide = prefabBody.spreadBloomCurve.Evaluate(0f), tight = prefabBody.spreadBloomCurve.Evaluate(1f);
                float progress = Mathf.InverseLerp(wide, tight, body.spreadBloomAngle);
                bullet.maxSpread = Mathf.Lerp(C.Get(p.MaxSpread), Number(shotgunSample, "maxSpread") + tight, progress);
            }
            bullet.spreadPitchScale = C.Get(p.Pitch); bullet.spreadYawScale = C.Get(p.Yaw);
            bullet.falloffModel = BulletFalloff(C.Get(p.Skill.Falloff));
            if (p.Index == 1 && charge >= 0.9999f && C.Get(p.NoFalloff)) bullet.falloffModel = BulletAttack.FalloffModel.None;
            bullet.damageType.damageSource = p.Index == 1 ? DamageSource.Secondary : DamageSource.Primary;
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(activeState);
            if (p?.Index <= 1 && IsCaptain(__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null))
                ConfigureBullet(__instance, p, activeState is ChargedShotgunFireState fire ? fire.Charge : 0f);
            BeaconMarker marker = Beacon(activeState);
            if (marker && activeState.GetType().Name == "HitGroundState") { CharacterBody owner = OwnerBody(marker); __instance.damage = (owner ? owner.damage : marker.Damage) * C.Get(beaconDamage) / 100f; __instance.isCrit = C.Get(beaconCrit) && (owner ? owner.RollCrit() : Util.CheckRoll(marker.Crit, marker.Master)); }
        }
        private static BulletAttack.FalloffModel BulletFalloff(S.BulletFalloff value) => value == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : value == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        private static S.AreaFalloff AreaFrom(object value) => Enum.TryParse(value?.ToString(), out S.AreaFalloff result) ? result : S.AreaFalloff.None;
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), value.ToString());
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsCaptain(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            GameObject prefab = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(x => x.Projectile && x.Projectile == prefab); if (p == null) return;
            fireProjectileInfo.damage = fireProjectileInfo.owner.GetComponent<CharacterBody>().damage * C.Get(p.Skill.Damage) / 100f;
            if (p.Skill.ProjectileSpeed != null) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => ConfigureProjectile(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance) => ConfigureProjectile(__instance);
        private static bool Matches(GameObject instance, GameObject prefab) => prefab && (instance.name == prefab.name + "(Clone)" || instance.GetComponent<ProjectileController>() && prefab.GetComponent<ProjectileController>() && instance.GetComponent<ProjectileController>().catalogIndex >= 0 && instance.GetComponent<ProjectileController>().catalogIndex == prefab.GetComponent<ProjectileController>().catalogIndex);
        private static bool InProjectileFamily(GameObject instance, GameObject prefab, HashSet<GameObject> visited = null)
        {
            if (!prefab) return false;
            if (Matches(instance, prefab)) return true;
            if (visited == null) visited = new HashSet<GameObject>();
            if (!visited.Add(prefab)) return false;
            foreach (Component c in prefab.GetComponents<Component>())
            {
                GameObject child = Prefab(Read(c, "childrenProjectilePrefab"));
                if (child && InProjectileFamily(instance, child, visited)) return true;
            }
            return false;
        }
        private static void ConfigureProjectile(ProjectileController pc)
        {
            if (!pc || !pc.owner || !IsCaptain(pc.owner.GetComponent<CharacterBody>()) || pc.GetComponent<ProjectileMarker>()) return;
            Profile p = profiles.Values.FirstOrDefault(x => x.Projectile && InProjectileFamily(pc.gameObject, x.Projectile)); if (p == null) return;
            pc.gameObject.AddComponent<ProjectileMarker>().Profile = p;
            pc.procCoefficient = C.Get(p.Skill.Proc);
            foreach (ProjectileExplosion explosion in pc.GetComponentsInChildren<ProjectileExplosion>(true))
            {
                explosion.blastDamageCoefficient = explosion.blastProcCoefficient = 1f;
                explosion.blastRadius = p.Skill.Radius == null ? explosion.blastRadius : C.Get(p.Skill.Radius);
                if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area));
                if (p.Index == 4 && Matches(pc.gameObject, p.Projectile) && explosion is ProjectileImpactExplosion impact)
                { impact.lifetime = C.Get(diabloDelay); impact.lifetimeRandomOffset = 0f; }
            }
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            ProjectileMarker marker = __instance.inflictor ? __instance.inflictor.GetComponentInParent<ProjectileMarker>() : null;
            if (!marker || marker.Profile.Skill.Area == null) return;
            __instance.falloffModel = Area(C.Get(marker.Profile.Skill.Area));
            if (marker.Profile.Index == 4 && C.Get(friendlyFire)) { __instance.teamIndex = TeamIndex.None; __instance.attackerFiltering = AttackerFiltering.AlwaysHit; }
        }
        private sealed class DamageScope { internal TazerChain Previous, Chain; internal ProjectileMarker Marker; internal bool Skip; }
        private static bool BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out DamageScope __state)
        {
            var marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<ProjectileMarker>() : null;
            __state = new DamageScope { Previous = activeTazer, Marker = marker };
            if (marker?.Profile.Index == 2)
            {
                activeTazer = tazerProjectiles.GetValue(marker, m => new TazerChain { Owner = damageInfo.attacker });
                __state.Chain = activeTazer;
            }
            else if (activeTazer != null && tazerHitPending && damageInfo.attacker == activeTazer.Owner && damageInfo.dotIndex == DotController.DotIndex.None)
            { __state.Chain = activeTazer; tazerHitPending = false; }
            if (__state.Chain != null) damageInfo.damageType &= ~DamageType.Shock5s;
            if (marker?.Profile.Index == 4 && damageInfo.attacker && TeamComponent.GetObjectTeam(damageInfo.attacker) == TeamComponent.GetObjectTeam(__instance.gameObject))
            {
                if (!C.Get(friendlyFire)) { __state.Skip = true; return false; }
                CharacterBody owner = damageInfo.attacker.GetComponent<CharacterBody>();
                var team = TeamCatalog.GetTeamDef(TeamComponent.GetObjectTeam(damageInfo.attacker));
                float nativeScale = team == null ? 1f : team.friendlyFireScaling;
                damageInfo.damage = owner.damage * C.Get(friendlyDamage) / 100f / Math.Max(0.0001f, nativeScale);
            }
            return true;
        }
        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo, DamageScope __state)
        {
            if (__state == null || __state.Skip || !NetworkServer.active || damageInfo.rejected) return;
            if (__state.Chain != null)
            {
                Shock(__instance, C.Get(tazerShock) * damageInfo.procCoefficient, C.Get(tazerSlow));
                if (__state.Marker && !activeTazer.Hit.Contains(__instance))
                {
                    activeTazer.Hit.Add(__instance);
                    if (C.Get(tazerBounces) > 0)
                    {
                        HurtBox next = SearchEnemy(damageInfo.position, C.Get(tazerRange), activeTazer.Owner, activeTazer.Hit);
                        if (next)
                        {
                            var orb = new LightningOrb { attacker = activeTazer.Owner, origin = damageInfo.position, target = next, damageValue = damageInfo.damage,
                                isCrit = damageInfo.crit, procCoefficient = damageInfo.procCoefficient, damageType = damageInfo.damageType,
                                lightningType = LightningOrb.LightningType.Ukulele, bouncesRemaining = C.Get(tazerBounces) - 1, range = C.Get(tazerRange),
                                bouncedObjects = activeTazer.Hit, teamIndex = TeamComponent.GetObjectTeam(activeTazer.Owner) };
                            tazerOrbs.Add(orb, activeTazer); OrbManager.instance.AddOrb(orb);
                        }
                    }
                }
            }
        }
        private static Exception AfterDamageScope(DamageScope __state, Exception __exception) { if (__state != null) activeTazer = __state.Previous; return __exception; }
        private static void Shock(HealthComponent target, float duration, bool slow)
        {
            if (!target || !target.alive || duration <= 0f) return;
            var hurt = target.GetComponent<SetStateOnHurt>(); if (hurt) hurt.SetShock(duration);
            if (slow && target.body) target.body.AddTimedBuff(RoR2Content.Buffs.Slow50, duration);
        }
        private static HurtBox SearchEnemy(Vector3 origin, float radius, GameObject owner, IList<HealthComponent> excluded)
        {
            var search = new BullseyeSearch { searchOrigin = origin, maxDistanceFilter = radius, maxAngleFilter = 180f, teamMaskFilter = TeamMask.GetEnemyTeams(TeamComponent.GetObjectTeam(owner)), sortMode = BullseyeSearch.SortMode.Distance, filterByLoS = true };
            search.RefreshCandidates(); return search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive && !excluded.Contains(h.healthComponent));
        }
        private static void BeforeOrb(Orb orb)
        { if (activeTazer != null && orb is LightningOrb lightning && !tazerOrbs.TryGetValue(lightning, out _)) tazerOrbs.Add(lightning, activeTazer); }
        private sealed class TazerArrival { internal TazerChain Previous; internal bool Pending; }
        private static void BeforeTazerArrival(LightningOrb __instance, out TazerArrival __state) { __state = new TazerArrival { Previous = activeTazer, Pending = tazerHitPending }; if (tazerOrbs.TryGetValue(__instance, out TazerChain chain)) { activeTazer = chain; tazerHitPending = true; } }
        private static Exception AfterTazerArrival(TazerArrival __state, Exception __exception) { activeTazer = __state.Previous; tazerHitPending = __state.Pending; return __exception; }
        private sealed class Payment { internal int Cost; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Index != 5 && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            CharacterBody body = skillSlot.characterBody; if (!IsCaptain(body)) return;
            BodyData data = bodies.GetOrCreateValue(body);
            if (definitions.TryGetValue(__instance, out Profile p))
            {
                if (p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false;
                if (p.Index == 5 && data.BeaconCancelHeld) __result = false;
                if (p.Index == 0 && rework.Value && Time.fixedTime < data.PrimaryReady) __result = false;
                if (p.Index == 1 && Time.fixedTime < (C.Get(p.ShareReload) ? data.PrimaryReady : data.SecondaryReady)) __result = false;
            }
            if (beaconDefs.ContainsKey(__instance) && C.Get(stageLimit) > 0 && data.Uses.TryGetValue(skillSlot, out int used) && used >= C.Get(stageLimit) + Math.Max(0, skillSlot.bonusStockFromBody)) __result = false;
            if (beaconDefs.ContainsKey(__instance) && profiles[5].Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false;
            if (beaconDefs.ContainsKey(__instance) && C.Get(stageLimit) > 0 && C.Get(sharedCooldown) && Time.fixedTime < data.BeaconReady) __result = false;
        }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll || beaconDefs.ContainsKey(__instance) && profiles[5].Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void AfterMaxStock(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (beaconDefs.ContainsKey(__instance) && C.Get(stageLimit) > 0 && IsCaptain(skillSlot.characterBody)) __result = 1 - skillSlot.bonusStockFromBody; }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!IsCaptain(skillSlot.characterBody)) return;
            if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.CooldownAfterDuration && skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
            if (beaconDefs.ContainsKey(__instance) && C.Get(sharedCooldown) && C.Get(stageLimit) == 0)
            {
                var controller = skillSlot.characterBody.GetComponent<CaptainSupplyDropController>();
                if (controller && skillSlot == controller.supplyDrop2Skill) deltaTime = 0f;
            }
        }
        private static void LoadBeacons(SkillDef[] defs)
        {
            for (int i = 0; i < Calls.Length; i++)
            {
                if (!switches[BeaconNames[i]].Value) continue;
                var option = new BeaconOptions { Kind = i, Enabled = switches[BeaconNames[i]] };
                string section = Root + " - " + BeaconNames[i];
                option.Duration = S.Float(section, "Duration", i == 0 ? 30f : i == 1 ? 30f : i == 2 ? 45f : 30f, 0f, "Active duration in seconds. 0 is unlimited.");
                beacons[i] = option;
                foreach (SkillDef def in defs.Where(d => d.activationState.stateType == State(Calls[i])))
                {
                    option.Prefab = Prefab(Read(EntityStateCatalog.InstantiateState(def.activationState.stateType), "supplyDropPrefab"));
                    // State configuration is available here, after the state catalog has loaded.
                    // The earlier content callback can run before supplyDropPrefab is populated.
                    if (option.Prefab)
                    {
                        var zone = option.Prefab.GetComponent<HoldoutZoneController>() ?? option.Prefab.AddComponent<HoldoutZoneController>();
                        ConfigureHoldout(zone, C.Get(option.Duration)); zone.enabled = false;
                    }
                    else ThreeQsVanillaTweaks.Log.Warning("Captain beacon prefab unavailable: " + BeaconNames[i]);
                    beaconDefs[def] = option; beaconKeywords[def] = def.keywordTokens ?? Array.Empty<string>(); Hook(def.activationState.stateType);
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => BeaconExtra(option), () => 0f);
                }
            }
            if (beacons.ContainsKey(0))
            {
                string s = Root + " - " + BeaconNames[0];
                supportHeal = S.Float(s, "Heal Amount (%)", 10f, 10f, "Maximum health healed each second.");
                supportAttack = S.Float(s, "Attack Speed Buff (%)", 30f, 0f, "Attack speed gained while supported.");
                supportArmour = S.Float(s, "Armour Buff", 25f, 0f, "Armour gained while supported.", -10000f);
                supportDamage = S.Float(s, "Damage Buff (%)", 0f, 0f, "Damage gained while supported.");
                renameSupport = S.Bool(s, "Rename Beacon", true, false, "Rename Healing Beacon to Support Beacon.");
                supportDecay = S.Float(s, "Buff Decay", 1.5f, 0f, "Seconds that healing and buffs persist after leaving the area.");
                nectar = S.Bool(s, "Count As Growth Nectar Buff", true, false, "Count the support buff towards Growth Nectar.");
                stackSupport = S.Bool(s, "Stack Buffs", false, false, "Stack healing and buffs from overlapping support beacons.");
                foreach (var pair in beaconDefs.Where(p => p.Value.Kind == 0)) S.SetText(pair.Key.skillNameToken, () => C.Get(renameSupport) ? "Orbital Supply Beacon: Support" : "Orbital Supply Beacon: Healing");
                Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterSupportStats));
                Patch(typeof(HealthComponent), "ServerFixedUpdate", postfix: nameof(AfterSupportHealthTick));
            }
            if (beacons.ContainsKey(1))
            {
                string s = Root + " - " + BeaconNames[1];
                shockSlow = S.Bool(s, "Inflict Slow", true, false, "Slow enemies for the same duration as the shock.");
                shockDuration = S.Float(s, "Shock Duration", 5f, 5f, "Shock duration in seconds.");
                shockInterval = S.Float(s, "Interval", 3f, 3f, "Seconds between shocks.", 0.001f);
                shockDamage = S.Float(s, "Damage (%)", 350f, 0f, "Damage dealt by each shock as a percentage of Captain's base damage.");
                shockCrit = S.Bool(s, "Can Crit", true, false, "Allow shock damage to critically strike.");
                Patch(DropState("ShockZoneMainState"), "Shock", nameof(BeforeBeaconShock));
            }
            if (beacons.ContainsKey(2))
            {
                string s = Root + " - " + BeaconNames[2];
                resupplyUses = S.Int(s, "Uses", 3, 3, "Uses available before the beacon is depleted.", 1);
                rechargeUses = S.Bool(s, "Recharge Uses", true, false, "Recharge spent uses instead of destroying the beacon when depleted.");
                rechargeTime = S.Float(s, "Recharge Time", 10f, 0f, "Seconds needed to recharge one use.", 0.001f);
                rechargeEquipment = S.Bool(s, "Recharge Equipment", true, true, "Restore equipment charges when used.");
                rechargePrimary = S.Bool(s, "Recharge Primary", true, false, "Restore primary skill stocks when used.");
                rechargeSecondary = S.Bool(s, "Recharge Secondary", true, false, "Restore secondary skill stocks when used.");
                rechargeUtility = S.Bool(s, "Recharge Utility", true, false, "Restore utility skill stocks when used.");
                rechargeSpecial = S.Bool(s, "Recharge Special", true, false, "Restore special skill stocks when used.");
                resupplyBeacons = S.Bool(s, "Beacons Can Be Resupplied", false, false, "Allow resupply to restore Captain's supply beacons.");
                barrier = S.Float(s, "Barrier Granted (%)", 50f, 0f, "Maximum health granted as temporary barrier when used.");
                Patch(DropState("EquipmentRestockMainState"), "GetInteractability", nameof(BeforeResupplyAvailable));
                Patch(DropState("EquipmentRestockMainState"), "OnInteractionBegin", nameof(BeforeResupply));
            }
            if (beacons.ContainsKey(3))
            {
                string s = Root + " - " + BeaconNames[3];
                hackDiscount = C.Bind(s, "Cost Reduction (%)", 50f, new ConfigDescription("Cost removed after hacking. 100 makes the interactable free; 0 leaves its cost unchanged. Vanilla: 100.", new AcceptableValueRange<float>(0f, 100f)));
                hackUse = S.Bool(s, "Use On Hack", false, true, "Use the hacked interactable for free when hacking finishes.");
                hackPrinters = S.Bool(s, "Are 3D Printers Hackable", true, false, "Allow each 3D printer to be hacked once for a free item. Takes twice the small chest hacking time, multiplied by 1 for common or lunar, 2 for uncommon, or 3 for legendary or boss.");
                breakHackedPrinters = S.Bool(s, "Break Printers After Hacking", true, false, "Remove the displayed item and disable the printer after its free print.");
                hackAll = S.Bool(s, "Hack All Multishop Segments", true, false, "Apply the hack to every terminal of a multishop.");
                object sample = EntityStateCatalog.InstantiateState(DropState("HackingInProgressState"));
                hackSpeed = S.Float(s, "Hacking Time For A Small Chest", Number(sample, "baseDuration", 15f) * 3f, Number(sample, "baseDuration", 15f), "Seconds needed to hack an interactable costing as much as a small chest. Default: three times vanilla. Beacon lifetime continues counting down while hacking; an active hack can finish after expiry.", 0.001f);
                hackCommand = C.Bind(s, "Chance To Replace With Command (%)", 15f, new ConfigDescription("Chance for an item from a hacked interactable to become a choice of the same rarity. Vanilla: 0.", new AcceptableValueRange<float>(0f, 100f)));
                Patch(DropState("HackingMainState"), "PurchaseInteractionIsValidTarget", postfix: nameof(AfterHackTarget));
                Patch(DropState("HackingMainState"), "FixedUpdate", nameof(BeforeHackingScan));
                Patch(typeof(ChestBehavior), "ItemDrop", nameof(BeforeHackedChest), finalizer: nameof(AfterHackedDrop));
                foreach (MethodInfo drop in typeof(ShopTerminalBehavior).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == "DropPickup"))
                    Patch(typeof(ShopTerminalBehavior), "DropPickup", nameof(BeforeHackedTerminal), finalizer: nameof(AfterHackedDrop), args: drop.GetParameters().Select(p => p.ParameterType).ToArray());
                Patch(typeof(PickupDropletController), "CreatePickupDroplet", nameof(BeforeHackedDroplet), args: new[] { typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3) });
            }
            // Register only after all settings are bound, including the text cached by LookingGlass.
            foreach (var pair in beaconDefs)
            {
                var descriptionSkill = new S.Skill(Root + " - " + BeaconNames[pair.Value.Kind], pair.Key, pair.Key.activationState.stateType, pair.Value.Enabled);
                BeaconOptions option = pair.Value;
                S.SetDescription(descriptionSkill, pair.Key.skillDescriptionToken, () => BeaconDescription(option));
            }
            Patch(State("SetupSupplyDrop"), "FixedUpdate", prefix: nameof(BeforeBeaconPlacement));
            Patch(DropState("BaseCaptainSupplyDropState"), "Update", postfix: nameof(AfterBeaconUpdate));
            Patch(typeof(RoR2.UI.HUD), "Update", postfix: nameof(AfterHudUpdate));
            Patch(typeof(RoR2.UI.CrosshairManager), "UpdateCrosshair", postfix: nameof(AfterBeaconCrosshair));
            foreach (string name in new[] { "EntryState", "HitGroundState", "HealZoneMainState", "ShockZoneMainState", "EquipmentRestockMainState", "HackingMainState", "HackingInProgressState", "UnlockTargetState" }) Hook(DropState(name));
            Patch(typeof(CaptainSupplyDropController), "FixedUpdate", postfix: nameof(AfterSupplyTick));
            Patch(typeof(CaptainSupplyDropController), "UpdateSkillOverrides", nameof(BeforeSupplyOverrides));
            Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", postfix: nameof(AfterBeaconLimit));
            string native = Language.GetString(holdoutKeyword);
            if (native == holdoutKeyword && Language.GetString("KEYWORD_HOLDOUT_BEACON") != "KEYWORD_HOLDOUT_BEACON") holdoutKeyword = "KEYWORD_HOLDOUT_BEACON";
            else if (native == holdoutKeyword) S.SetText(holdoutKeyword, () => "<style=cKeywordName>Holdout Beacon</style><style=cSub>Counts as a holdout zone for items.</style>");
        }
        private static BeaconMarker Beacon(EntityState state)
        {
            if (state?.outer == null || state.GetType().Namespace != "EntityStates.CaptainSupplyDrop") return null;
            GameObject go = state.outer.gameObject;
            var existing = go.GetComponent<BeaconMarker>(); if (existing) return existing;
            var ownership = go.GetComponent<GenericOwnership>();
            if (!ownership || !ownership.ownerObject || !IsCaptain(ownership.ownerObject.GetComponent<CharacterBody>())) return null;
            BeaconOptions option = beacons.Values.FirstOrDefault(b => b.Prefab && go.name == b.Prefab.name + "(Clone)");
            if (option == null) return null;
            var marker = go.AddComponent<BeaconMarker>(); marker.Options = option; marker.Owner = ownership.ownerObject; marker.Holdout = go.GetComponent<HoldoutZoneController>();
            CharacterBody owner = marker.Owner.GetComponent<CharacterBody>(); marker.Master = owner.master; marker.Team = TeamComponent.GetObjectTeam(marker.Owner); marker.Damage = owner.damage; marker.Crit = owner.crit;
            return marker;
        }
        private static CharacterBody OwnerBody(BeaconMarker marker) => !marker ? null : marker.Owner ? marker.Owner.GetComponent<CharacterBody>() : marker.Master ? marker.Master.GetBody() : null;
        private static void ConfigureHoldout(HoldoutZoneController zone, float duration)
        {
            zone.baseRadius = C.Get(beaconRadius); zone.baseChargeDuration = duration > 0f ? duration : 60f;
            SetValue(zone, "minimumRadius", 0f); zone.chargeRadiusDelta = 0f; zone.radiusSmoothTime = 0.1f; zone.playerCountScaling = 0f;
            zone.showObjective = false; zone.applyHealingNova = false; zone.applyFocusConvergence = true;
            SetValue(zone, "applyDevotedEvolution", false); SetValue(zone, "applyDelusionResetChests", false);
            zone.healingNovaRoot = zone.transform;
        }
        private static void ActivateBeacon(BeaconMarker marker, EntityState state)
        {
            if (!marker.Active)
            {
                marker.Active = true; marker.Born = Time.fixedTime;
                if (marker.Holdout) { ConfigureHoldout(marker.Holdout, C.Get(marker.Options.Duration)); marker.Holdout.enabled = C.Get(holdouts); }
                if (marker.Options.Kind == 2 && NetworkServer.active)
                {
                    var energy = marker.GetComponent<GenericEnergyComponent>();
                    if (energy) { energy.capacity = C.Get(resupplyUses); energy.chargeRate = C.Get(rechargeUses) ? 1f / C.Get(rechargeTime) : 0f; energy.energy = energy.capacity; }
                }
                if (NetworkServer.active && C.Get(holdouts)) SpawnWarbanner(marker);
            }
            if (marker.Options.Kind == 0 && NetworkServer.active)
            {
                var wardObject = Read(state, "healZoneInstance") as GameObject;
                if (wardObject)
                {
                    foreach (Component component in wardObject.GetComponents<Component>())
                        if (component && component.GetType().Name == "HealingWard")
                        {
                            SetValue(component, "Networkradius", marker.EffectiveRadius());
                            SetValue(component, "healPoints", 0f); SetValue(component, "healFraction", 0f);
                            if (component is Behaviour behaviour) behaviour.enabled = true;
                            marker.NativeHealingWard = component as HealingWard;
                        }
                }
            }
        }
        internal static float BeaconLifetimeFraction(float age, float duration) => duration <= 0f ? 1f : Mathf.Clamp01(1f - Math.Max(0f, age) / duration);
        private static Component BeaconPanel(string name, Transform parent, Color colour, out RectTransform rect)
        {
            Type imageType = AccessTools.TypeByName("UnityEngine.UI.Image");
            rect = null;
            if (imageType == null) return null;
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = go.AddComponent(imageType);
            SetValue(image, "color", colour); SetValue(image, "raycastTarget", false);
            return image;
        }
        private static bool BeforeBeaconPlacement(EntityState __instance)
        {
            CharacterBody body = Body(__instance);
            var bank = body ? body.GetComponent<InputBankTest>() : null;
            if (!(Read(__instance, "isAuthority") is bool authority && authority) || !IsCaptain(body) || !bank) return true;
            PlacementInput input = placementInputs.GetOrCreateValue(__instance);
            if (!bank.skill4.down) input.Released = true;
            else if (input.Released && !(Read(__instance, "beginExit") is bool exiting && exiting))
            {
                // Native OnExit restores the weapons and removes the placement preview.
                // Only an actual CallSupplyDrop pays stocks or records a beacon use.
                bodies.GetOrCreateValue(body).BeaconCancelHeld = true;
                __instance.outer.SetNextStateToMain();
                return false;
            }
            return true;
        }
        private static void AfterBeaconUpdate(EntityState __instance)
        {
            BeaconMarker marker = Beacon(__instance);
            if (!marker) return;
            if (!marker.Active && typeof(EntityState).Assembly.GetType("EntityStates.CaptainSupplyDrop.BaseMainState").IsInstanceOfType(__instance)) ActivateBeacon(marker, __instance);
            if (!marker.Active) return;
            UpdateRangeCircle(marker);
            AfterBeaconEnergyIndicator(__instance);
        }
        private static GameObject FindBeaconChild(GameObject root, string name)
        {
            if (!root) return null;
            // FindModelChild uses ChildLocator aliases, which need not match GameObject names.
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
                if (component && component.GetType().Name == "ChildLocator")
                {
                    var method = AccessTools.Method(component.GetType(), "FindChild", new[] { typeof(string) });
                    var child = method?.Invoke(component, new object[] { name }) as Transform;
                    if (child) return child.gameObject;
                }
            return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name)?.gameObject;
        }
        private static bool IsBeaconDescendant(Transform child, Transform root)
        {
            for (Transform t = child; t; t = t.parent) if (t == root) return true;
            return false;
        }
        private static bool PrepareLifetimeCanvas(BeaconMarker marker)
        {
            if (marker.LifetimeContainer) return true;
            var prefab = Prefab(Read(EntityStateCatalog.InstantiateState(State("CallSupplyDropEquipmentRestock")), "supplyDropPrefab"));
            GameObject source = FindBeaconChild(prefab, "EnergyIndicatorContainer");
            GameObject sourceEnergy = FindBeaconChild(prefab, "EnergyIndicator");
            var canvas = sourceEnergy ? sourceEnergy.GetComponentInParent<Canvas>(true) : null;
            if (!canvas && source) canvas = source.GetComponentsInChildren<Canvas>(true).FirstOrDefault();
            if (!canvas || !sourceEnergy)
            {
                if (!marker.WarnedLifetime)
                {
                    marker.WarnedLifetime = true;
                    ThreeQsVanillaTweaks.Log.Warning("Captain: resupply lifetime UI unavailable (prefab=" + (bool)prefab + ", container=" + (bool)source + ", canvas=" + (bool)canvas + ", image=" + (bool)sourceEnergy + ").");
                }
                return false;
            }
            // The native container can wrap its Canvas rather than be a child of it.
            // Copy their common ancestor so the display retains both its Canvas and container.
            Transform displayRoot = source.transform;
            while (displayRoot && (!IsBeaconDescendant(canvas.transform, displayRoot) || !IsBeaconDescendant(sourceEnergy.transform, displayRoot))) displayRoot = displayRoot.parent;
            if (!displayRoot) return false;
            Transform[] sourceTransforms = displayRoot.gameObject.GetComponentsInChildren<Transform>(true);
            int containerIndex = Array.IndexOf(sourceTransforms, source.transform);
            int energyIndex = Array.IndexOf(sourceTransforms, sourceEnergy.transform);
            if (containerIndex < 0 || energyIndex < 0) return false;
            marker.LifetimeCanvas = UnityEngine.Object.Instantiate(displayRoot.gameObject, marker.transform);
            marker.LifetimeCanvas.name = "3QSBeaconLifetimeCanvas";
            marker.LifetimeCanvas.SetActive(true);
            foreach (var clonedCanvas in marker.LifetimeCanvas.GetComponentsInChildren<Canvas>(true)) clonedCanvas.enabled = true;
            Transform[] clonedTransforms = marker.LifetimeCanvas.GetComponentsInChildren<Transform>(true);
            marker.LifetimeContainer = clonedTransforms[containerIndex].gameObject;
            GameObject energyObject = clonedTransforms[energyIndex].gameObject;
            marker.LifetimeCanvas.transform.position = marker.transform.TransformPoint(prefab.transform.InverseTransformPoint(displayRoot.position));
            for (Transform t = marker.LifetimeContainer.transform; t && t != marker.LifetimeCanvas.transform.parent; t = t.parent) t.gameObject.SetActive(true);
            for (Transform t = energyObject.transform; t && t != marker.LifetimeCanvas.transform.parent; t = t.parent) t.gameObject.SetActive(true);
            Type imageType = AccessTools.TypeByName("UnityEngine.UI.Image");
            marker.LifetimeEnergy = imageType != null ? energyObject.GetComponent(imageType) as Behaviour : null;
            return true;
        }
        private static void AfterBeaconEnergyIndicator(EntityState __instance)
        {
            BeaconMarker marker = Beacon(__instance);
            if (!marker) return;
            if (!marker.Active && typeof(EntityState).Assembly.GetType("EntityStates.CaptainSupplyDrop.BaseMainState").IsInstanceOfType(__instance)) ActivateBeacon(marker, __instance);
            if (!marker.Active) return;
            var container = Read(__instance, "energyIndicatorContainer") as GameObject;
            var energy = Read(__instance, "energyIndicator") as Behaviour;
            if (!container) return;
            if (marker.Options.Kind != 2)
            {
                if (!PrepareLifetimeCanvas(marker)) return;
                container.SetActive(false);
                container = marker.LifetimeContainer;
                Behaviour nativeEnergy = energy;
                energy = marker.LifetimeEnergy;
                if (energy && nativeEnergy) SetValue(energy, "fillAmount", Read(nativeEnergy, "fillAmount"));
            }
            container.SetActive(true);
            // Resupply keeps its charge display; hacking uses its per-target progress below.
            if (energy) energy.enabled = marker.Options.Kind == 2;
            if (!marker.LifetimeBox)
            {
                var nativeBar = energy ? energy.GetComponent<RectTransform>() : null;
                Transform parent = nativeBar && nativeBar.parent ? nativeBar.parent : container.transform;
                var black = BeaconPanel("3QSBeaconLifetime", parent, new Color(0f, 0f, 0f), out RectTransform outer);
                if (!black) return;
                marker.LifetimeBox = black.gameObject;
                if (nativeBar)
                {
                    outer.anchorMin = nativeBar.anchorMin; outer.anchorMax = nativeBar.anchorMax;
                    outer.pivot = nativeBar.pivot; outer.anchoredPosition = nativeBar.anchoredPosition;
                    outer.localScale = nativeBar.localScale;
                    outer.sizeDelta = nativeBar.sizeDelta + new Vector2(8f, 8f);
                }
                else outer.sizeDelta = new Vector2(8f, 8f);
                outer.SetAsFirstSibling();
                Color colour = marker.Options.Kind == 0 ? new Color(0.3f, 1f, 0.45f) :
                    marker.Options.Kind == 1 ? new Color(0.3f, 0.65f, 1f) :
                    marker.Options.Kind == 2 ? new Color(1f, 0.42f, 0.05f) : new Color(1f, 0.3f, 0.5f);
                if (marker.Options.Kind == 2 || marker.Options.Kind == 3)
                {
                    Color edgeColour = marker.Options.Kind == 3 ? new Color(colour.r * 0.4f, colour.g * 0.4f, colour.b * 0.4f) : colour;
                    BeaconPanel("LifetimeRemaining", outer, edgeColour, out marker.LifetimeFill);
                    BeaconPanel("LifetimeInterior", outer, new Color(0f, 0f, 0f), out RectTransform inner);
                    if (inner)
                    {
                        inner.offsetMin = new Vector2(4f, 4f); inner.offsetMax = new Vector2(-4f, -4f);
                        if (marker.Options.Kind == 3) BeaconPanel("HackingProgress", inner, colour, out marker.HackFill);
                    }
                }
                else
                {
                    SetValue(black, "color", new Color(colour.r * 0.4f, colour.g * 0.4f, colour.b * 0.4f));
                    BeaconPanel("LifetimeInterior", outer, new Color(0f, 0f, 0f), out RectTransform inner);
                    if (inner)
                    {
                        inner.offsetMin = new Vector2(4f, 4f); inner.offsetMax = new Vector2(-4f, -4f);
                        BeaconPanel("LifetimeRemaining", inner, colour, out marker.LifetimeFill);
                    }
                }
            }
            if (marker.Options.Kind == 3 && __instance.GetType().Name == "HackingInProgressState")
            {
                var hackingEnergy = marker.GetComponent<GenericEnergyComponent>();
                if (hackingEnergy) marker.HackProgress = Mathf.Clamp01(hackingEnergy.normalizedEnergy);
            }
            if (marker.LifetimeFill)
                marker.LifetimeFill.anchorMax = new Vector2(BeaconLifetimeFraction(marker.ActiveAge(), C.Get(marker.Options.Duration)), 1f);
            if (marker.HackFill) marker.HackFill.anchorMax = new Vector2(marker.HackProgress, 1f);
        }
        private static void SpawnWarbanner(BeaconMarker marker)
        {
            CharacterBody owner = OwnerBody(marker); if (!owner || !owner.inventory) return;
            int count = owner.inventory.GetItemCountEffective(RoR2Content.Items.WardOnLevel); if (count <= 0) return;
            var prefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/WardOnLevel/WarbannerWard.prefab").WaitForCompletion(); if (!prefab) return;
            GameObject ward = UnityEngine.Object.Instantiate(prefab, marker.transform.position, Quaternion.identity);
            ward.GetComponent<TeamFilter>().teamIndex = TeamComponent.GetObjectTeam(marker.Owner);
            var buff = ward.GetComponent<BuffWard>(); buff.Networkradius = 8f + 8f * count; buff.expires = true;
            buff.expireDuration = C.Get(marker.Options.Duration) > 0f ? C.Get(marker.Options.Duration) : 60f;
            NetworkServer.Spawn(ward);
        }
        private static void AfterSupportHealthTick(HealthComponent __instance)
        {
            if (NetworkServer.active && __instance.body) UpdateSupport(__instance.body);
        }
        private static void UpdateSupport(CharacterBody body)
        {
            if (!body || !support.TryGetValue(body, out SupportData data)) return;
            foreach (var pair in data.Sources.Where(p => p.Value <= Time.fixedTime).ToArray()) data.Sources.Remove(pair.Key);
            int count = C.Get(stackSupport) ? data.Sources.Count : Math.Min(1, data.Sources.Count);
            if (count != data.Count)
            {
                if (supportBuff)
                {
                    if (data.Count == 0 && count > 0) ChangeSupportBuff(body, true);
                    if (data.Count > 0 && count == 0) ChangeSupportBuff(body, false);
                }
                data.Count = count; body.MarkAllStatsDirty();
            }
            if (count <= 0 || !body.healthComponent || !body.healthComponent.alive) { data.HealTimer = 0f; return; }
            data.HealTimer += Time.fixedDeltaTime;
            if (data.HealTimer + 0.00001f >= 1f)
            {
                int pulses = Math.Max(1, (int)(data.HealTimer + 0.00001f));
                data.HealTimer = Math.Max(0f, data.HealTimer - pulses);
                body.healthComponent.Heal(body.healthComponent.fullHealth * C.Get(supportHeal) / 100f * count * pulses, default(ProcChainMask), nonRegen: true);
            }
        }
        private static void ChangeSupportBuff(CharacterBody body, bool add)
        {
            string name = add ? "AddUnmanagedBuff" : "RemoveUnmanagedBuff";
            MethodInfo method = typeof(CharacterBody).GetMethods(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(m => m.Name == name && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(BuffDef));
            if (method == null) { method = AccessTools.Method(typeof(CharacterBody), add ? "AddBuff" : "RemoveBuff", new[] { typeof(BuffIndex) }); method?.Invoke(body, new object[] { supportBuff.buffIndex }); return; }
            object[] args = method.GetParameters().Select((p, i) => i == 0 ? (object)supportBuff : p.HasDefaultValue ? p.DefaultValue : (object)1).ToArray(); method.Invoke(body, args);
        }
        private static void AfterSupportStats(CharacterBody __instance)
        {
            if (!support.TryGetValue(__instance, out SupportData data) || data.Count <= 0) return;
            float level = Math.Max(0f, __instance.level - 1f);
            SetValue(__instance, "armor", __instance.armor + C.Get(supportArmour) * data.Count);
            SetValue(__instance, "attackSpeed", __instance.attackSpeed + (__instance.baseAttackSpeed + __instance.levelAttackSpeed * level) * C.Get(supportAttack) / 100f * data.Count);
            SetValue(__instance, "damage", __instance.damage + (__instance.baseDamage + __instance.levelDamage * level) * C.Get(supportDamage) / 100f * data.Count);
        }
        private static bool BeforeBeaconShock(EntityState __instance)
        {
            BeaconMarker marker = Beacon(__instance); if (!marker || marker.Options.Kind != 1) return true;
            if (!NetworkServer.active) return false;
            CharacterBody owner = OwnerBody(marker);
            var search = new BullseyeSearch { searchOrigin = marker.transform.position, maxDistanceFilter = marker.EffectiveRadius(), maxAngleFilter = 180f, teamMaskFilter = TeamMask.GetEnemyTeams(marker.Team), filterByLoS = false };
            search.RefreshCandidates();
            foreach (HealthComponent target in search.GetResults().Where(h => h && h.healthComponent).Select(h => h.healthComponent).Distinct())
            {
                var info = new DamageInfo { attacker = marker.Owner, inflictor = marker.gameObject, position = target.body ? target.body.corePosition : target.transform.position,
                    damage = (owner ? owner.damage : marker.Damage) * C.Get(shockDamage) / 100f, crit = C.Get(shockCrit) && (owner ? owner.RollCrit() : Util.CheckRoll(marker.Crit, marker.Master)), procCoefficient = 1f, damageType = DamageType.Generic };
                info.damageType.damageSource = DamageSource.Special;
                target.TakeDamage(info);
                if (!info.rejected)
                {
                    Shock(target, C.Get(shockDuration), C.Get(shockSlow));
                    if (info.damage > 0f && GlobalEventManager.instance) { GlobalEventManager.instance.OnHitEnemy(info, target.gameObject); GlobalEventManager.instance.OnHitAll(info, target.gameObject); }
                }
            }
            GameObject effect = Prefab(Read(__instance, "shockEffectPrefab"));
            if (effect) EffectManager.SpawnEffect(effect, new EffectData { origin = marker.transform.position, scale = marker.EffectiveRadius() }, true);
            return false;
        }
        private static bool BeforeResupplyAvailable(EntityState __instance, Interactor activator, ref Interactability __result)
        {
            BeaconMarker marker = Beacon(__instance); if (!marker || marker.Options.Kind != 2) return true;
            var body = activator ? activator.GetComponent<CharacterBody>() : null; var energy = marker.GetComponent<GenericEnergyComponent>();
            __result = !body || !energy ? Interactability.Disabled : energy.energy + 0.0001f < 1f ? Interactability.ConditionsNotMet : Interactability.Available;
            return false;
        }
        private static void Restock(GenericSkill skill)
        {
            if (!skill) return;
            if (!C.Get(resupplyBeacons) && (beaconDefs.ContainsKey(skill.baseSkill) || skill.baseSkill.activationState.stateType == State("SetupSupplyDrop"))) return;
            skill.Reset();
        }
        private static bool BeforeResupply(EntityState __instance, Interactor activator)
        {
            BeaconMarker marker = Beacon(__instance); if (!marker || marker.Options.Kind != 2) return true;
            if (!NetworkServer.active || !activator) return false;
            CharacterBody body = activator.GetComponent<CharacterBody>(); var energy = marker.GetComponent<GenericEnergyComponent>();
            if (!body || !energy || energy.energy + 0.0001f < 1f) return false;
            energy.TakeEnergy(1f);
            if (C.Get(rechargeEquipment) && body.inventory) body.inventory.RestockActiveEquipmentCharges(Math.Max(1, (int)Number(body.equipmentSlot, "maxStock", 1f)));
            var locator = body.skillLocator;
            if (locator)
            {
                if (C.Get(rechargePrimary)) Restock(locator.primary); if (C.Get(rechargeSecondary)) Restock(locator.secondary);
                if (C.Get(rechargeUtility)) Restock(locator.utility); if (C.Get(rechargeSpecial)) Restock(locator.special);
                if (C.Get(rechargeSpecial) && C.Get(resupplyBeacons) && body.GetComponent<CaptainSupplyDropController>() is CaptainSupplyDropController captain)
                { Restock(captain.supplyDrop1Skill); Restock(captain.supplyDrop2Skill); bodies.GetOrCreateValue(body).Uses.Clear(); }
            }
            if (body.healthComponent) body.healthComponent.AddBarrierAuthority(body.healthComponent.fullHealth * C.Get(barrier) / 100f);
            if (!C.Get(rechargeUses) && energy.energy < 1f) NetworkServer.Destroy(marker.gameObject);
            return false;
        }
        private sealed class BeaconHud : MonoBehaviour
        {
            internal readonly RoR2.UI.SkillIcon[] Icons = new RoR2.UI.SkillIcon[2];
        }
        private static void AfterHudUpdate(RoR2.UI.HUD __instance)
        {
            CharacterBody body = __instance.targetBodyObject ? __instance.targetBodyObject.GetComponent<CharacterBody>() : null;
            var display = __instance.GetComponent<BeaconHud>();
            if (!IsCaptain(body) || !C.Get(showBeaconIndicators))
            {
                if (display) foreach (var icon in display.Icons) if (icon) icon.gameObject.SetActive(false);
                return;
            }
            if (__instance.skillIcons == null || __instance.skillIcons.Length < 4 || !__instance.skillIcons[3]) return;
            var controller = body.GetComponent<CaptainSupplyDropController>();
            if (!controller) return;
            if (!display) display = __instance.gameObject.AddComponent<BeaconHud>();
            GenericSkill[] slots = { controller.supplyDrop1Skill, controller.supplyDrop2Skill };
            for (int i = 0; i < slots.Length; i++)
            {
                bool visible = slots[i] && beaconDefs.ContainsKey(slots[i].baseSkill);
                var icon = display.Icons[i];
                if (!icon && visible)
                {
                    var template = __instance.skillIcons[3];
                    GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
                    go.name = "3QSCaptainBeacon" + (i + 1);
                    StripBeaconBindings(go);
                    icon = display.Icons[i] = go.GetComponent<RoR2.UI.SkillIcon>();
                    Type layoutType = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
                    if (layoutType != null)
                    {
                        var layout = go.GetComponent(layoutType);
                        if (!layout) layout = go.AddComponent(layoutType);
                        SetValue(layout, "ignoreLayout", true);
                    }
                }
                if (!icon) continue;
                icon.gameObject.SetActive(visible);
                PositionBeaconHudIcon(icon, __instance.skillIcons[3], i);
                icon.targetSkill = slots[i];
                icon.targetSkillSlot = i == 0 ? SkillSlot.Primary : SkillSlot.Secondary;
                icon.playerCharacterMasterController = __instance.skillIcons[3].playerCharacterMasterController;
            }
        }
        private static void StripBeaconBindings(GameObject root)
        {
            foreach (var binding in root.GetComponentsInChildren<InputBindingDisplayController>(true))
            {
                binding.enabled = false;
                if (binding.gameObject != root) binding.gameObject.SetActive(false);
            }
            var icon = root.GetComponent<RoR2.UI.SkillIcon>();
            object cooldown = Read(icon, "cooldownText"), stock = Read(icon, "stockText");
            foreach (var text in root.GetComponentsInChildren<Component>(true))
                if ((text.GetType().Name == "TextMeshProUGUI" || text.GetType().Name == "TextMeshPro") && !ReferenceEquals(text, cooldown) && !ReferenceEquals(text, stock))
                {
                    SetValue(text, "text", "");
                    if (text.gameObject != root) text.gameObject.SetActive(false);
                }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child != root.transform && !string.IsNullOrEmpty(child.name) && (child.name.IndexOf("Binding", StringComparison.OrdinalIgnoreCase) >= 0 || child.name.IndexOf("KeyLabel", StringComparison.OrdinalIgnoreCase) >= 0)) child.gameObject.SetActive(false);
        }
        private static void PositionBeaconHudIcon(RoR2.UI.SkillIcon icon, RoR2.UI.SkillIcon template, int index)
        {
            var rect = icon.GetComponent<RectTransform>();
            var source = template.GetComponent<RectTransform>();
            if (!rect || !source) return;
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = source.anchoredPosition + new Vector2(
                (0.5f - source.pivot.x) * source.rect.width + C.Get(indicatorOffsetX) + (index == 0 ? -32f : 32f),
                (1f - source.pivot.y) * source.rect.height + C.Get(indicatorOffsetY));
            rect.localScale = source.localScale * 0.65f;
        }
        private static void AfterBeaconCrosshair(RoR2.UI.CrosshairManager __instance, CharacterBody __0)
        {
            var crosshair = __instance.crosshairController;
            if (!crosshair) return;
            var display = crosshair.GetComponent<BeaconHud>();
            bool visible = C.Get(showBeaconSelectionIndicators) && IsCaptain(__0) && __0.GetComponents<EntityStateMachine>().Any(m => m.state?.GetType() == State("SetupSupplyDrop"));
            if (!visible)
            {
                if (display) foreach (var icon in display.Icons) if (icon) icon.gameObject.SetActive(false);
                return;
            }
            var hud = __instance.cameraRigController ? __instance.cameraRigController.hud : null;
            if (!hud || hud.skillIcons == null || hud.skillIcons.Length < 4 || !hud.skillIcons[3]) return;
            var controller = __0.GetComponent<CaptainSupplyDropController>(); if (!controller) return;
            if (!display) display = crosshair.gameObject.AddComponent<BeaconHud>();
            GenericSkill[] slots = { controller.supplyDrop1Skill, controller.supplyDrop2Skill };
            for (int i = 0; i < slots.Length; i++)
            {
                bool hasBeacon = slots[i] && beaconDefs.ContainsKey(slots[i].baseSkill);
                var icon = display.Icons[i];
                if (!icon && hasBeacon)
                {
                    var go = UnityEngine.Object.Instantiate(hud.skillIcons[3].gameObject, crosshair.transform);
                    go.name = "3QSCaptainBeaconSelection" + (i + 1);
                    StripBeaconBindings(go);
                    icon = display.Icons[i] = go.GetComponent<RoR2.UI.SkillIcon>();
                    var rect = go.GetComponent<RectTransform>();
                    if (rect)
                    {
                        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                        rect.anchoredPosition = new Vector2(i == 0 ? -240f : 240f, 0f);
                        rect.localScale = new Vector3(1f, 1f, 1f);
                    }
                }
                if (!icon) continue;
                icon.gameObject.SetActive(hasBeacon); icon.targetSkill = slots[i];
                icon.targetSkillSlot = i == 0 ? SkillSlot.Primary : SkillSlot.Secondary;
                icon.playerCharacterMasterController = hud.skillIcons[3].playerCharacterMasterController;
            }
        }
        private static void UpdateRangeCircle(BeaconMarker marker)
        {
            if (!marker.Active || marker.Options.Kind > 1) return;
            float radius = marker.EffectiveRadius();
            // The native healing ward owns the vanilla animated beacon ring.
            // Keep its Update running; its healing has been disabled separately.
            if (marker.Options.Kind == 0 && marker.NativeHealingWard)
            {
                var ward = marker.NativeHealingWard;
                ward.enabled = true;
                ward.healPoints = 0f; ward.healFraction = 0f;
                if (NetworkServer.active) ward.Networkradius = radius;
                if (ward.rangeIndicator)
                {
                    ward.rangeIndicator.gameObject.SetActive(true);
                    return;
                }
            }
            // Healing already has its own networked ward and ring on every client.
            if (marker.Options.Kind == 0) return;
            if (!marker.RangeVisual)
            {
                var prefab = Prefab(Read(EntityStateCatalog.InstantiateState(DropState("HealZoneMainState")), "healZonePrefab"));
                var ward = prefab ? prefab.GetComponent<HealingWard>() : null;
                Transform template = ward ? ward.rangeIndicator : null;
                if (!template) return;
                marker.RangeVisual = UnityEngine.Object.Instantiate(template.gameObject, marker.transform);
                marker.RangeVisual.name = "3QSBeaconRange";
                marker.RangeVisual.transform.localPosition = template.localPosition;
                marker.RangeVisual.transform.localRotation = template.localRotation;
                marker.RangeVisual.SetActive(true);
                if (marker.Options.Kind == 1)
                    foreach (var renderer in marker.RangeVisual.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled = true;
                        // Preserve the native animated ring material and its opacity.
                        // Only change its colour for the shocking beacon.
                        if (!renderer.sharedMaterial) continue;
                        Material material = new Material(renderer.sharedMaterial);
                        marker.RangeMaterials.Add(material);
                        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.3f, 0.7f, 1f, material.GetColor("_TintColor").a));
                        if (material.HasProperty("_Color")) material.SetColor("_Color", new Color(0.3f, 0.7f, 1f, material.GetColor("_Color").a));
                        renderer.sharedMaterial = material;
                    }
            }
            Vector3 scale = marker.transform.lossyScale;
            marker.RangeVisual.transform.localScale = new Vector3(radius / Math.Max(0.001f, Math.Abs(scale.x)), radius / Math.Max(0.001f, Math.Abs(scale.y)), radius / Math.Max(0.001f, Math.Abs(scale.z)));
        }
        internal static int DaisyPulsesDue(float age, float duration, int stacks)
        {
            if (stacks <= 0) return 0;
            return Math.Min(stacks, Math.Max(0, (int)(Mathf.Clamp01(age / Math.Max(0.001f, duration)) * (stacks + 1))));
        }
        private static void UpdateBeaconDaisy(BeaconMarker marker)
        {
            if (!NetworkServer.active || !C.Get(holdouts)) return;
            int stacks = 0;
            foreach (CharacterMaster master in CharacterMaster.readOnlyInstancesList)
                if (master && master.teamIndex == marker.Team && master.inventory) stacks += master.inventory.GetItemCountEffective(RoR2Content.Items.TPHealingNova);
            float duration = C.Get(marker.Options.Duration), age = marker.ActiveAge();
            if (duration <= 0f)
            {
                int cycle = (int)(age / 60f);
                if (cycle != marker.DaisyCycle) { marker.DaisyCycle = cycle; marker.DaisyPulses = 0; }
                age -= cycle * 60f; duration = 60f;
            }
            int due = DaisyPulsesDue(age, duration, stacks);
            while (marker.DaisyPulses < due)
            {
                marker.DaisyPulses++;
                object sample = EntityStateCatalog.InstantiateState(typeof(EntityState).Assembly.GetType("EntityStates.TeleporterHealNovaController.TeleporterHealNovaGeneratorMain"));
                GameObject prefab = Prefab(Read(sample, "pulsePrefab"));
                if (prefab && marker.Holdout)
                {
                    SetValue(marker.Holdout, "currentRadius", marker.EffectiveRadius());
                    GameObject pulse = UnityEngine.Object.Instantiate(prefab, marker.transform.position, Quaternion.identity, marker.transform);
                    var team = pulse.GetComponent<TeamFilter>(); if (team) team.teamIndex = marker.Team;
                    NetworkServer.Spawn(pulse);
                }
                else
                {
                    if (!marker.WarnedDaisy) { marker.WarnedDaisy = true; ThreeQsVanillaTweaks.Log.Warning("Captain: native Lepton Daisy pulse unavailable; applying its healing directly."); }
                    foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                        if (body && body.healthComponent && body.healthComponent.alive && TeamComponent.GetObjectTeam(body.gameObject) == marker.Team && Vector3.Distance(body.corePosition, marker.transform.position) <= marker.EffectiveRadius())
                            body.healthComponent.Heal(body.healthComponent.fullHealth * 0.5f, default(ProcChainMask), nonRegen: true);
                }
            }
        }
        private static void AfterSupplyTick(CaptainSupplyDropController __instance)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>(); if (!IsCaptain(body) || !profiles.ContainsKey(5)) return;
            var bank = body.GetComponent<InputBankTest>();
            if (!bank || !bank.skill4.down) bodies.GetOrCreateValue(body).BeaconCancelHeld = false;
            int bonus = SpecialBonus(body);
            foreach (GenericSkill slot in new[] { __instance.supplyDrop1Skill, __instance.supplyDrop2Skill })
                if (slot && beaconDefs.ContainsKey(slot.baseSkill) && slot.bonusStockFromBody != bonus) slot.SetBonusStockFromBody(bonus);
            BodyData data = bodies.GetOrCreateValue(body);
            if (data.Stage != Stage.instance)
            { data.Stage = Stage.instance; data.Uses.Clear(); if (__instance.supplyDrop1Skill) __instance.supplyDrop1Skill.Reset(); if (__instance.supplyDrop2Skill) __instance.supplyDrop2Skill.Reset(); }
            if (C.Get(sharedCooldown) && C.Get(stageLimit) == 0 && __instance.supplyDrop1Skill && __instance.supplyDrop2Skill && beaconDefs.ContainsKey(__instance.supplyDrop1Skill.baseSkill) && beaconDefs.ContainsKey(__instance.supplyDrop2Skill.baseSkill))
            { __instance.supplyDrop2Skill.stock = __instance.supplyDrop1Skill.stock; __instance.supplyDrop2Skill.rechargeStopwatch = __instance.supplyDrop1Skill.rechargeStopwatch; }
        }
        private static bool BeforeSupplyOverrides(CaptainSupplyDropController __instance)
        {
            if (!IsCaptain(__instance.GetComponent<CharacterBody>()) || !profiles.ContainsKey(5)) return true;
            // Keep recharging beacon definitions assigned instead of replacing empty slots with an exhausted skill.
            foreach (GenericSkill slot in new[] { __instance.supplyDrop1Skill, __instance.supplyDrop2Skill })
                if (slot && beaconDefs.ContainsKey(slot.baseSkill) && __instance.usedUpSkillDef) slot.UnsetSkillOverride(__instance, __instance.usedUpSkillDef, GenericSkill.SkillOverridePriority.Contextual);
            return false;
        }
        private static void BeaconUsed(CharacterBody body, GenericSkill slot)
        {
            if (!slot || !beaconDefs.ContainsKey(slot.baseSkill)) return;
            BodyData data = bodies.GetOrCreateValue(body); data.Uses[slot] = data.Uses.TryGetValue(slot, out int used) ? used + 1 : 1;
            var controller = body.GetComponent<CaptainSupplyDropController>();
            if (C.Get(sharedCooldown) && C.Get(stageLimit) == 0 && controller && controller.supplyDrop1Skill && slot == controller.supplyDrop2Skill)
            { controller.supplyDrop1Skill.stock = slot.stock; controller.supplyDrop1Skill.rechargeStopwatch = slot.rechargeStopwatch; }
            if (C.Get(sharedCooldown) && C.Get(stageLimit) > 0) data.BeaconReady = Time.fixedTime + slot.CalculateFinalRechargeInterval();
        }
        private static void AfterBeaconLimit(CharacterMaster __instance, DeployableSlot slot, ref int __result)
        {
            if (slot != DeployableSlot.CaptainSupplyDrop || !IsCaptain(__instance.GetBody())) return;
            int maximum = C.Get(beaconMaximum);
            if (maximum == 0) { __result = int.MaxValue; return; }
            int bonus = SpecialBonus(__instance.GetBody());
            __result = maximum + (C.Get(extraMaximum) ? Math.Max(0, bonus) : 0);
        }
        private static int SpecialBonus(CharacterBody body)
        {
            if (!body || !body.skillLocator) return 0;
            GenericSkill source = Read(body.skillLocator, "specialBonusStockSkill") as GenericSkill;
            if (!source) source = body.skillLocator.special;
            return source ? Math.Max(0, source.bonusStockFromBody) : 0;
        }
        internal static int DiscountCost(int cost, float percent) => Math.Max(0, (int)Math.Ceiling(cost * (1d - percent / 100d)));
        private static void CompleteHack(BeaconMarker marker, PurchaseInteraction target)
        {
            if (!marker || !target || !target.available) return;
            var targets = new List<PurchaseInteraction> { target };
            var terminal = target.GetComponent<ShopTerminalBehavior>();
            object multishop = Read(terminal, "serverMultiShopController");
            if (C.Get(hackAll) && Read(multishop, "terminalGameObjects") is IEnumerable terminals)
                foreach (GameObject go in terminals) if (go && go.GetComponent<PurchaseInteraction>() is PurchaseInteraction purchase && !targets.Contains(purchase)) targets.Add(purchase);
            foreach (PurchaseInteraction purchase in targets)
            {
                if (purchase.GetComponent<HackedPickup>()) continue;
                if (IsPrinter(purchase)) { CompletePrinterHack(marker, purchase); continue; }
                purchase.Networkcost = DiscountCost(purchase.cost, C.Get(hackDiscount));
                HackedPickup hacked = purchase.GetComponent<HackedPickup>() ?? purchase.gameObject.AddComponent<HackedPickup>();
                hacked.Buyer = OwnerBody(marker)?.master;
            }
            RefreshHackedMultishopPrice(multishop);
            if (!IsPrinter(target) && C.Get(hackUse) && marker.Owner)
            {
                int cost = target.cost; target.Networkcost = 0;
                try { var interactor = marker.Owner.GetComponent<Interactor>(); if (interactor) interactor.AttemptInteraction(target.gameObject); }
                finally { if (target) target.Networkcost = cost; }
            }
            object sample = EntityStateCatalog.InstantiateState(DropState("UnlockTargetState")); GameObject effect = Prefab(Read(sample, "unlockEffectPrefab"));
            if (effect) EffectManager.SpawnEffect(effect, new EffectData { origin = target.transform.position }, true);
            float duration = C.Get(marker.Options.Duration);
            if (duration > 0f && marker.ActiveAge() >= duration) NetworkServer.Destroy(marker.gameObject);
        }
        private static void RefreshHackedMultishopPrice(object multishop)
        {
            if (!(Read(multishop, "terminalGameObjects") is IEnumerable terminals)) return;
            var purchases = new List<PurchaseInteraction>();
            foreach (GameObject terminal in terminals)
                if (terminal && terminal.GetComponent<PurchaseInteraction>() is PurchaseInteraction purchase) purchases.Add(purchase);
            if (purchases.Count == 0 || purchases.Any(p => !p.GetComponent<HackedPickup>())) return;
            int cost = purchases[0].cost;
            if (purchases.Any(p => p.cost != cost)) return;
            // The overhead hologram reads this separate, networked controller price.
            SetValue(multishop, "Networkcost", cost);
        }
        internal static bool BeaconShouldExpire(float age, float duration, bool finishingHack) => BeaconExpired(age, duration) && !finishingHack;
        internal static bool BeaconExpired(float age, float duration) => duration > 0f && age >= duration;
        private static bool BeforeHackingScan(EntityState __instance)
        {
            BeaconMarker marker = Beacon(__instance);
            if (!marker || !marker.Active || !marker.Options.Enabled.Value || !BeaconExpired(marker.ActiveAge(), C.Get(marker.Options.Duration))) return true;
            // No new target after expiry, regardless of the order Unity updates
            // the beacon marker and its state machine. Existing hacks finish.
            if (NetworkServer.active) NetworkServer.Destroy(marker.gameObject);
            return false;
        }
        private static void AfterHackTarget(PurchaseInteraction purchaseInteraction, ref bool __result)
        {
            if (!purchaseInteraction) return;
            if (IsPrinter(purchaseInteraction)) __result = C.Get(hackPrinters) && purchaseInteraction.available;
            if (purchaseInteraction.GetComponent<HackedPickup>()) __result = false;
        }
        private static bool IsPrinter(PurchaseInteraction purchase)
        {
            if (!purchase || !purchase.GetComponent<ShopTerminalBehavior>()) return false;
            return purchase.GetComponents<EntityStateMachine>().Any(m =>
                m.mainStateType.stateType?.Namespace == "EntityStates.Duplicator" || m.state?.GetType().Namespace == "EntityStates.Duplicator");
        }
        private static int PrinterRarityMultiplier(ShopTerminalBehavior terminal)
        {
            string tier = Read(terminal, "itemTier")?.ToString();
            MethodInfo current = AccessTools.Method(typeof(ShopTerminalBehavior), "CurrentPickup", Type.EmptyTypes);
            object pickup = current?.Invoke(terminal, null);
            object index = Read(pickup, "pickupIndex");
            if (index is PickupIndex pickupIndex)
            {
                var definition = PickupCatalog.GetPickupDef(pickupIndex);
                if (definition != null && definition.itemIndex != ItemIndex.None)
                {
                    var item = ItemCatalog.GetItemDef(definition.itemIndex);
                    if (item) tier = item.tier.ToString();
                }
            }
            return tier == "Tier2" || tier == "VoidTier2" ? 2 : tier == "Tier3" || tier == "Boss" || tier == "VoidTier3" || tier == "VoidBoss" ? 3 : 1;
        }
        private static float PrinterHackDuration(PurchaseInteraction purchase) => Math.Max(0.001f, C.Get(hackSpeed) * 2f * PrinterRarityMultiplier(purchase.GetComponent<ShopTerminalBehavior>()));
        private static void CompletePrinterHack(BeaconMarker marker, PurchaseInteraction purchase)
        {
            var terminal = purchase.GetComponent<ShopTerminalBehavior>();
            if (!terminal || !C.Get(hackPrinters) || purchase.GetComponent<HackedPickup>()) return;
            var hacked = purchase.gameObject.AddComponent<HackedPickup>(); hacked.Buyer = OwnerBody(marker)?.master;
            object oldAmount = Read(terminal, "dropAmount"), oldDebt = Read(terminal, "debt"), oldPurchased = Read(terminal, "hasBeenPurchased");
            BeginDrop(purchase.gameObject, out DropContext dropScope);
            try
            {
                SetValue(terminal, "dropAmount", 1); SetValue(terminal, "debt", 1);
                MethodInfo drop = typeof(ShopTerminalBehavior).GetMethod("DropPickup", new[] { typeof(bool) });
                if (drop != null) drop.Invoke(terminal, new object[] { true });
                else typeof(ShopTerminalBehavior).GetMethod("DropPickup", Type.EmptyTypes).Invoke(terminal, null);
            }
            finally
            {
                if (oldAmount != null) SetValue(terminal, "dropAmount", oldAmount);
                if (oldDebt != null) SetValue(terminal, "debt", oldDebt);
                AfterHackedDrop(dropScope, null);
            }
            if (C.Get(breakHackedPrinters))
            {
                terminal.SetNoPickup();
                purchase.SetAvailable(false);
            }
            else if (oldPurchased is bool purchased) terminal.SetHasBeenPurchased(purchased);
        }
        private static void BeforeHackedChest(ChestBehavior __instance, out DropContext __state) => BeginDrop(__instance.gameObject, out __state);
        private static void BeforeHackedTerminal(ShopTerminalBehavior __instance, out DropContext __state) => BeginDrop(__instance.gameObject, out __state);
        private static void BeginDrop(GameObject go, out DropContext scope)
        {
            scope = new DropContext { Previous = activeDrop }; HackedPickup hacked = go.GetComponent<HackedPickup>() ?? go.GetComponentInParent<HackedPickup>();
            if (!hacked) return;
            if (!hacked.Rolled) { hacked.Rolled = true; hacked.Command = Util.CheckRoll(C.Get(hackCommand), hacked.Buyer); }
            activeDrop = hacked;
        }
        private static Exception AfterHackedDrop(DropContext __state, Exception __exception) { if (__state != null) activeDrop = __state.Previous; return __exception; }
        private static void BeforeHackedDroplet(ref GenericPickupController.CreatePickupInfo pickupInfo)
        {
            if (!activeDrop || !activeDrop.Command) return;
            object info = pickupInfo;
            object index = Read(info, "pickupIndex") ?? Read(Read(info, "pickup"), "pickupIndex");
            if (!(index is PickupIndex pickup)) return;
            var definition = PickupCatalog.GetPickupDef(pickup); if (definition == null || definition.itemIndex == ItemIndex.None) return;
            if (Enum.TryParse("Command", out GenericPickupController.PickupArtifactFlag flag)) { object old = Read(info, "artifactFlag"); SetValue(info, "artifactFlag", (GenericPickupController.PickupArtifactFlag)(Convert.ToInt32(old) | Convert.ToInt32(flag))); pickupInfo = (GenericPickupController.CreatePickupInfo)info; }
        }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string damage = s.Damage == null ? "" : S.Damage(S.Number(C.Get(s.Damage)) + "% damage");
            if (p.Index == 0) return s.AgileText + "Fire a blast of pellets that deals " + S.Damage(C.Get(p.Pellets) + "x" + S.Number(C.Get(s.Damage)) + "% damage") + (rework.Value ? ". Hold to fire repeatedly." : ". Charging the attack narrows the spread.");
            if (p.Index == 1) return s.AgileText + "Charge a blast of pellets for " + S.Damage(C.Get(p.Pellets) + "x" + S.Number(C.Get(s.Damage)) + "–" + S.Number(C.Get(p.FullDamage)) + "% damage") + ". Charging the attack narrows the spread" + (C.Get(p.NoFalloff) ? " and removes damage falloff at full charge." : ".");
            if (p.Index == 2) return s.AgileText + "<style=cIsUtility>Shocking.</style> Fire a fast tazer that deals " + damage + (C.Get(tazerBounces) > 0 ? " and bounces to up to <style=cIsUtility>" + C.Get(tazerBounces) + "</style> additional enemies" : "") + (C.Get(tazerSlow) ? ", <style=cIsUtility>slowing</style> them." : ".");
            if (p.Index == 3) return s.AgileText + "<style=cIsUtility>Stunning.</style> Call down <style=cIsUtility>" + C.Get(probes) + "</style> orbital probes, each dealing " + damage + ".";
            if (p.Index == 4) return s.AgileText + "<style=cIsUtility>Stunning.</style> Request a kinetic strike from the UES Safe Travels. After <style=cIsUtility>" + S.Number(C.Get(diabloDelay)) + " seconds</style>, it deals " + damage + (C.Get(friendlyFire) ? ". <style=cIsHealth>Can damage allies.</style>" : ".");
            return "Request a Supply Beacon" + (C.Get(holdouts) ? " that acts as a <style=cIsUtility>Holdout Beacon</style>" : "") + ". Can be requested" + (C.Get(stageLimit) == 0 ? " repeatedly." : " <style=cIsUtility>" + C.Get(stageLimit) + "</style> times per stage.");
        }
        private static string BeaconDescription(BeaconOptions option)
        {
            string text = option.Kind == 0 ? "Heal all nearby allies for <style=cIsHealing>" + S.Number(C.Get(supportHeal)) + "% of their maximum health</style> every second." : option.Kind == 1 ? "<style=cIsUtility>Shock</style> nearby enemies every <style=cIsUtility>" + S.Number(C.Get(shockInterval)) + " seconds</style>, dealing " + S.Damage(S.Number(C.Get(shockDamage)) + "% damage") + "." : option.Kind == 2 ? "Recharge allies' equipment and skills when used, granting <style=cIsHealing>" + S.Number(C.Get(barrier)) + "% temporary barrier</style>." : "Hack nearby purchasable interactables, reducing their cost by <style=cIsUtility>" + S.Number(C.Get(hackDiscount)) + "%</style>.";
            if (option.Kind == 3) text += " Its lifetime continues while hacking, but it will finish its current hack before expiring.";
            if (option.Kind == 3 && C.Get(hackCommand) > 0f)
                text += " Items from hacked interactables have a <style=cIsUtility>" + S.Number(C.Get(hackCommand)) + "% chance</style> to become a <style=cIsUtility>Command Essence</style> of the same rarity.";
            if (option.Kind == 0)
            {
                var bonuses = new List<string>(); if (C.Get(supportAttack) != 0f) bonuses.Add("<style=cIsDamage>" + S.Number(C.Get(supportAttack)) + "% attack speed</style>");
                if (C.Get(supportArmour) != 0f) bonuses.Add("<style=cIsHealing>" + S.Number(C.Get(supportArmour)) + " armour</style>");
                if (C.Get(supportDamage) != 0f) bonuses.Add(S.Damage(S.Number(C.Get(supportDamage)) + "% damage"));
                if (bonuses.Count > 0) text += " Also grants " + string.Join(" and ", bonuses) + ".";
            }
            return (C.Get(holdouts) ? "<style=cIsUtility>Holdout Beacon.</style> " : "") + text + (C.Get(option.Duration) > 0f ? " Lasts <style=cIsUtility>" + S.Number(C.Get(option.Duration)) + " seconds</style>." : "");
        }
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (k, v) => b.Append("\n").Append(k).Append(": <style=cIsUtility>").Append(v).Append("</style>");
            if (p.Skill.Radius != null) add("Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Index == 1) { add("Min Charge Time", S.Number(C.Get(p.MinCharge)) + "s"); add("Max Charge Time", S.Number(C.Get(p.MaxCharge)) + "s"); }
            if (p.Index == 2) { add("Shock Duration", S.Number(C.Get(tazerShock)) + "s"); add("Bounce Range", S.Number(C.Get(tazerRange)) + "m"); }
            if (p.Index == 5) { add("Radius", S.Number(C.Get(beaconRadius)) + "m"); add("Impact Damage", S.Number(C.Get(beaconDamage)) + "%"); add("Active Beacons", C.Get(beaconMaximum) == 0 ? "Unlimited" : C.Get(beaconMaximum).ToString()); }
            return b.ToString();
        }
        private static string BeaconExtra(BeaconOptions option) => "\nRadius: <style=cIsUtility>" + S.Number(C.Get(beaconRadius)) + "m</style>" + (option.Kind == 0 ? "\nBuff Decay: <style=cIsUtility>" + S.Number(C.Get(supportDecay)) + "s</style>" : option.Kind == 2 ? "\nUses: <style=cIsUtility>" + C.Get(resupplyUses) + "</style>" : "");
    }
}
