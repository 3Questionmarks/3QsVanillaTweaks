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
    internal static class Railgunner
    {
        private const string Root = "Survivors - 12 Railgunner", StatsRoot = "Survivor-Stats - 12 Railgunner";
        private static readonly string[] Names = { "XQR Smart Round System", "M99 Sniper", "HH44 Marksman", "Concussion Device", "Polar Field Device", "Supercharge", "Cryocharge" };
        private static readonly string[] Attacks = { "FirePistol", "FireSnipeHeavy", "FireSnipeLight", "FireMineConcussive", "FireMineBlinding", "FireSnipeSuper", "FireSnipeCryo" };
        private static readonly string[] Activations = { "Weapon.FirePistol", "Scope.WindUpScopeHeavy", "Scope.WindUpScopeLight", "Weapon.FireMineConcussive", "Weapon.FireMineBlinding", "Weapon.ChargeSnipeSuper", "Weapon.ChargeSnipeCryo" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal SkillDef AttackDefinition, ActivationDefinition;
            internal ConfigEntry<int> MaxAreas;
            internal GameObject Projectile;
            internal readonly HashSet<GameObject> ProjectileFamily = new HashSet<GameObject>();
            internal readonly Dictionary<SkillDef, string[]> Keywords = new Dictionary<SkillDef, string[]>();
            internal ConfigEntry<string> Magazine;
            internal ConfigEntry<float> PerfectBonus, WeakBonus, Charge, Disable, Tracking, Push;
            internal ConfigEntry<bool> AllPerfect, GroundRecoil, Execute, SelfDamage;
            internal float NativeBlast = 1f, NativeForce, NativeRadius, NativeTracking, NativeInputForce;
        }
        private sealed class StateScope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class DamageMarker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; }
        private sealed class StatsScope { internal CharacterBody Previous; internal bool Convert; }
        private sealed class Payment { internal int Cost; }
        private sealed class DamageScope { internal HealthComponent Previous; }
        private sealed class AreaMarker : MonoBehaviour
        {
            internal Profile Profile;
            internal GameObject Owner;
            internal float NextFrost, Born = -1f;
            internal static readonly List<AreaMarker> Active = new List<AreaMarker>();
            private void OnDestroy() { Active.Remove(this); }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || Profile == null || !Owner) return;
                BuffWard ward = GetComponent<BuffWard>();
                if (!ward || !ward.enabled || !gameObject.activeInHierarchy) return;
                if (Born < 0f)
                {
                    Born = Time.fixedTime; Active.Add(this);
                    int maximum = C.Get(Profile.MaxAreas);
                    if (maximum >= 0)
                    {
                        var areas = Active.Where(m => m && m.Owner == Owner && m.Profile == Profile).OrderBy(m => m.Born).ToList();
                        while (areas.Count > maximum) { var oldest = areas[0]; areas.RemoveAt(0); Active.Remove(oldest); NetworkServer.Destroy(oldest.gameObject); }
                        if (!Active.Contains(this)) return;
                    }
                }
                if (Profile.Skill.Duration != null && C.Get(Profile.Skill.Duration) >= 0f && Time.fixedTime - Born >= C.Get(Profile.Skill.Duration)) { Active.Remove(this); NetworkServer.Destroy(gameObject); return; }
                bool frostTick = Time.fixedTime >= NextFrost;
                if (frostTick) NextFrost = Time.fixedTime + C.Get(frostInterval);
                float radius = C.Get(Profile.Skill.Radius);
                foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                {
                    if (!body || !body.healthComponent || !body.healthComponent.alive || !TeamMask.GetEnemyTeams(TeamComponent.GetObjectTeam(Owner)).HasTeam(TeamComponent.GetObjectTeam(body.gameObject)) || Vector3.Distance(body.corePosition, transform.position) > radius) continue;
                    if (C.Get(weaken)) body.AddTimedBuff(RoR2Content.Buffs.Weak, Time.fixedDeltaTime * 2f);
                    if (C.Get(frost) && frostTick && frostBuff) body.AddTimedBuff(frostBuff, 3f);
                }
            }
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<MethodBase> stateHooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<ProjectileController, HashSet<int>> configuredProjectiles = new ConditionalWeakTable<ProjectileController, HashSet<int>>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled, generalEnabled, guarantee, convert, weaken, frost;
        private static ConfigEntry<float> weakBonus, critPerChance, frostInterval;
        private static ConfigEntry<int> slow;
        private static CharacterBody prefabBody;
        private static BuffDef frostBuff;
        private static S.Stats stats;
        private static Harmony harmony;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static CharacterBody calculatingStats;
        [ThreadStatic] private static HealthComponent cryoExecution;
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Railgunner." + (name.Contains(".") ? name : "Weapon." + name));
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsRailgunner(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags); if (field != null) return field.GetValue(obj);
                PropertyInfo property = type.GetProperty(name, flags); if (property != null) return property.GetValue(obj, null);
            }
            return null;
        }
        private static float Number(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible value ? Convert.ToSingle(value) : fallback;
        private static void Write(object obj, string name, object value)
        {
            if (obj == null) return;
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = type.GetField(name, flags); if (field != null) { field.SetValue(obj, value); return; }
                PropertyInfo property = type.GetProperty(name, flags); if (property?.GetSetMethod(true) != null) { property.SetValue(obj, value, null); return; }
            }
        }
        private static GameObject Prefab(object value)
        {
            if (value is GameObject go) return go;
            if (value == null) return null;
            var method = value.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(GameObject) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.IsInstanceOfType(value));
            return method?.Invoke(null, new[] { value }) as GameObject;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Railgunner changes. Restart to apply. Vanilla: False.");
            generalEnabled = C.BindStartupBool(Root + " - General", "Changes Enabled", true, "Enable Railgunner's general changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Railgunner stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Magnetic Accelerator", "Changes Enabled", true, "Enable Magnetic Accelerator changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Railgunner), prefix), postfix == null ? null : new HarmonyMethod(typeof(Railgunner), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Railgunner), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "OnExit" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType == typeof(EntityState) || method.DeclaringType == typeof(BaseState) || !stateHooks.Add(method)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(Railgunner), nameof(BeforeState)), finalizer: new HarmonyMethod(typeof(Railgunner), nameof(EndScope)));
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/Railgunner/RailgunnerBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Railgunner");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefabBody); Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody)); }
                if (generalEnabled.Value)
                {
                    guarantee = S.Bool(Root + " - General", "Weakpoints Guarantee Crit", true, true, "Weakpoint hits always critically strike.");
                    weakBonus = S.Float(Root + " - General", "Weakpoint Bonus Damage (%)", 0f, 0f, "Extra damage dealt by weakpoint hits.");
                    Hook(State("BaseFireSnipe"));
                }
                if (passiveEnabled.Value)
                {
                    string section = Root + " - Magnetic Accelerator";
                    critPerChance = S.Float(section, "Crit Damage Gained Per Chance (%)", 2f, 1f, "Extra critical damage gained for each percentage point of critical chance.");
                    convert = S.Bool(section, "Convert Crit Chance", true, true, "Remove critical chance when granting its critical damage bonus. Disable to allow random critical strikes.");
                    Patch(typeof(CharacterBody), "RecalculateStats", nameof(BeforeStats), nameof(AfterStats), nameof(EndStats));
                    foreach (var method in typeof(Inventory).GetMethods().Where(m => m.Name == "GetItemCountEffective" && m.GetParameters().Length == 1 && (m.GetParameters()[0].ParameterType == typeof(ItemDef) || m.GetParameters()[0].ParameterType == typeof(ItemIndex))))
                        harmony.Patch(method, prefix: new HarmonyMethod(typeof(Railgunner), nameof(BeforeCritItemCount)));
                    S.SetText(prefabBody.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken, PassiveDescription);
                }
                var defs = new HashSet<SkillDef>(prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d));
                string[] overrides = { "Scope.ActiveScopeHeavy", "Scope.ActiveScopeLight", "Backpack.ChargedSuper", "Backpack.ChargedCryo" };
                int[] overrideProfiles = { 1, 2, 5, 6 };
                for (int j = 0; j < overrides.Length; j++)
                    if (switches[Names[overrideProfiles[j]]].Value && Read(EntityStateCatalog.InstantiateState(State(overrides[j])), "primaryOverride") is SkillDef def) defs.Add(def);
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    try
                    {
                        SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType == State(Activations[i]));
                        SkillDef attackDef = defs.FirstOrDefault(d => d.activationState.stateType == State(Attacks[i]) || i >= 5 && d.activationState.stateType == State(i == 5 ? "WindupSnipeSuper" : "WindupSnipeCryo"));
                        if (!def) throw new MissingMemberException("Railgunner skill unavailable: " + Names[i]);
                        var profile = new Profile { Index = i, ActivationDefinition = def, AttackDefinition = attackDef, Skill = new S.Skill(Root + " - " + Names[i], (i == 1 || i == 2) && attackDef ? attackDef : def, State(Attacks[i]), switches[Names[i]]) };
                        Bind(profile); profile.Skill.Loaded = true; profiles[i] = profile; definitions[def] = profile;
                        if (attackDef) { definitions[attackDef] = profile; profile.Keywords[attackDef] = attackDef.keywordTokens ?? Array.Empty<string>(); }
                        Hook(State(Attacks[i])); Hook(State(Activations[i]));
                        if (i >= 5) { Hook(State("Backpack.Charging" + (i == 5 ? "Super" : "Cryo"))); Hook(State(i == 5 ? "Backpack.Offline" : "Backpack.UseCryo")); }
                        S.Skill descriptionSkill = profile.Skill.Definition == def ? profile.Skill : new S.Skill(profile.Skill.Section, def, State(Attacks[i]), switches[Names[i]]);
                        S.SetDescription(descriptionSkill, "3QS_RAILGUNNER_SKILL_" + i, () => Description(profile));
                        RegisterTooltip(def, profile);
                        if (attackDef && attackDef != def)
                        {
                            var attackTooltip = new S.Skill(profile.Skill.Section, attackDef, State(Attacks[i]), switches[Names[i]]);
                            S.SetDescription(attackTooltip, attackDef.skillDescriptionToken, () => AttackDescription(profile));
                            RegisterTooltip(attackDef, profile);
                        }
                    }
                    catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Railgunner skill could not be loaded: " + Names[i] + ": " + error); }
                }
                if (profiles.Count > 0 || generalEnabled.Value)
                {
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), finalizer: nameof(EndDamage));
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(GenericSkill), "DeductStock", nameof(BeforeDeductStock), args: new[] { typeof(int) });
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "GetMaxStock", postfix: nameof(AfterMaxStock));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillTick));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                }
                if (profiles.ContainsKey(2)) Patch(typeof(GenericSkill), "CalculateFinalRechargeInterval", postfix: nameof(AfterLightRecharge));
                if (profiles.ContainsKey(6))
                    harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(Railgunner), nameof(ExecuteFrozenShot)));
                if (profiles.ContainsKey(1))
                {
                    Patch(State("Reload.Boosted"), "GetBonusDamage", postfix: nameof(AfterPerfectDamage));
                    Patch(State("Reload.Boosted"), "ConsumeBoost", nameof(BeforeConsumeBoost));
                }
                Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Railgunner changes could not be loaded: " + error); }
        }
        private static void RegisterTooltip(SkillDef def, Profile profile) => Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(profile), profile.Skill.Proc == null ? (Func<float>)null : () => C.Get(profile.Skill.Proc));
        private static void CollectProjectiles(Profile p, GameObject prefab)
        {
            if (!prefab || !p.ProjectileFamily.Add(prefab)) return;
            foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
                if (component)
                    foreach (FieldInfo field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        if (field.FieldType == typeof(GameObject) && field.Name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0) CollectProjectiles(p, field.GetValue(component) as GameObject);
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index;
            if (i == 1)
            {
                // The scoped primary uses a sentinel capacity internally. The M99
                // secondary has one usable shot; never use that sentinel as its default.
                int internalCapacity = s.Definition.baseMaxStock;
                s.Definition.baseMaxStock = 1;
                var stocks = S.Int(s.Section, "Base Stocks", 1, 1, "Maximum number of stocks.");
                // Config values are clamped to the accepted maximum when read.
                // int.MaxValue from the scoped primary was therefore saved as 1000000.
                if (internalCapacity >= 100000 && stocks.Value == Math.Min(internalCapacity, 1000000))
                    stocks.Value = 1;
            }
            s.Bind(damage: false, nativeConsumeOverride: i == 1 ? (int?)1 : null, changedAgile: i == 3 || i == 4 ? (bool?)true : null);
            if (i != 4) s.BindDamage(Number(s.Sample, "damageCoefficient", 1f) * 100f, i == 3 ? (float?)50f : i == 6 ? 2000f : null, Number(s.Sample, "procCoefficient", 1f), defaultProc: i == 3 ? (float?)0.1f : null);
            if (i == 0 || i == 3 || i == 4)
            {
                p.Projectile = Prefab(Read(s.Sample, "projectilePrefab"));
                if (!p.Projectile) throw new MissingMemberException("Projectile unavailable: " + Names[i]);
                CollectProjectiles(p, p.Projectile);
                s.BindProjectileSpeed(p.Projectile, i == 0 ? 1.5f : 1f);
            }
            if (i == 0)
            {
                var steer = p.Projectile.GetComponent<ProjectileSteerTowardTarget>();
                float native = steer ? steer.rotationSpeed : 0f; p.NativeTracking = native;
                p.Tracking = S.Float(s.Section, "Tracking Aggression", native, native, "Maximum projectile turn speed in degrees per second.");
                p.GroundRecoil = S.Bool(s.Section, "Disable Pushback On Ground", true, false, "Prevent the attack from pushing you backwards while grounded.");
            }
            if (i == 1 || i == 2)
                p.Magazine = C.Bind(s.Section, "Backup Mag Stacking", i == 2 ? "Damage Multiplier:25" : "Extra Shot", "Effect of Backup Magazine. Use Extra Shot, Extra Damage:<base damage%>, Damage Multiplier:<damage%>, or None. Extra Shot grants an extra shot before reloading for M99, or one extra shot per second for HH44. Extra Damage adds base damage per magazine. Damage Multiplier adds the specified percentage of the configured shot damage per magazine (25 means +25%, +50%, +75% with 1, 2, 3 magazines). Vanilla: Extra Shot for M99; None for HH44.");
            if (i == 1)
            {
                float native = Number(EntityStateCatalog.InstantiateState(State("Reload.Boosted")), "bonusDamageCoefficient", 5f) * 100f;
                p.PerfectBonus = S.Float(s.Section, "Perfect Reload Damage Bonus (%)", 500f, native, "Base damage added to shots after a perfect active reload.");
                p.AllPerfect = S.Bool(s.Section, "Perfect Reload Applies To All Shots", true, false, "Keep the perfect reload bonus for every M99 shot before the next reload.");
            }
            if (i == 3 || i == 4)
            {
                var explosion = p.Projectile.GetComponent<ProjectileExplosion>();
                if (explosion) p.NativeBlast = Math.Max(0.0001f, explosion.blastDamageCoefficient);
                if (i == 3)
                {
                    p.SelfDamage = S.Bool(s.Section, "Can Damage Self", false, false, "Allow Concussion Device to damage its owner. Disabled preserves the launch force while preventing self-damage and self-hit procs.");
                    p.NativeInputForce = Number(s.Sample, "force");
                    p.NativeForce = p.NativeInputForce > 0f ? p.NativeInputForce : explosion ? Vector3.Distance(explosion.bonusBlastForce, Vector3.zero) : 0f;
                    p.Push = S.Float(s.Section, "Push Force", p.NativeForce, p.NativeForce, "Force applied by the concussion blast.");
                }
                BuffWard ward = p.ProjectileFamily.SelectMany(g => g.GetComponentsInChildren<BuffWard>(true)).FirstOrDefault();
                p.NativeRadius = i == 4 && ward ? ward.radius : explosion ? explosion.blastRadius : 0f;
                s.Radius = S.Float(s.Section, "AOE Radius", p.NativeRadius, p.NativeRadius, "Area radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", i == 3 ? S.AreaFalloff.SweetSpot : explosion ? (S.AreaFalloff)Enum.Parse(typeof(S.AreaFalloff), explosion.falloffModel.ToString()) : S.AreaFalloff.None, "Damage falloff from the centre of the area. Vanilla: " + (explosion ? explosion.falloffModel.ToString() : "None") + ".");
                if (i == 4)
                {
                    float lifetime = ward && ward.expires ? ward.expireDuration : 8f;
                    s.Duration = S.Float(s.Section, "AOE Duration", lifetime, lifetime, "Field duration in seconds. -1 lasts until it is removed.", -1f);
                    slow = C.Bind(s.Section, "Slow Amount (%)", 80, new ConfigDescription("Movement speed reduction inside the field. Vanilla: 50.", new AcceptableValueList<int>(30, 50, 60, 80)));
                    weaken = S.Bool(s.Section, "Weaken Enemies In Area", true, false, "Weaken enemies while they remain inside the field.");
                    frost = S.Bool(s.Section, "Apply Frost To Enemies In Area", true, false, "Apply one Frost stack to enemies at each interval.");
                    frostInterval = S.Float(s.Section, "Frost Interval", 1f, 0f, "Seconds between Frost applications.", 0.001f);
                    frostBuff = FindBuff("Frost");
                    p.MaxAreas = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum active fields per player. Removes the oldest field when exceeded. -1 is unlimited.", -1);
                    S.SetText("3QS_KEYWORD_FROST", () => "<style=cKeywordName>Frost</style><style=cSub>Stacks slow enemies. Six stacks freeze the target.</style>");
                }
            }
            if (i >= 5)
            {
                float nativeWeak = (Number(s.Sample, "critDamageMultiplier", 1f) - 1f) * 100f;
                p.WeakBonus = S.Float(s.Section, "Weakpoint Bonus Damage (%)", i == 5 ? 250f : 150f, nativeWeak, "Extra shot damage dealt when hitting a weakpoint.");
                float nativeCharge = Number(EntityStateCatalog.InstantiateState(State("Backpack.Charging" + (i == 5 ? "Super" : "Cryo"))), "baseDuration", 2f);
                p.Charge = S.Float(s.Section, "Charge Time", 2f, nativeCharge, "Seconds needed to charge before attack speed is applied.");
                object disable = EntityStateCatalog.InstantiateState(State(i == 5 ? "Backpack.Offline" : "Backpack.UseCryo"));
                float nativeDisable = Number(disable, i == 5 ? "baseDuration" : "duration");
                p.Disable = S.Float(s.Section, "Disable Duration", i == 5 ? 5f : 0f, nativeDisable, "Seconds weapons are disabled after firing, before attack speed is applied.");
                if (i == 6) p.Execute = S.Bool(s.Section, "Execute With Shot", true, false, "Execute a freezable enemy if the shot lowers it below the frozen execution threshold.");
            }
        }
        private static BuffDef FindBuff(string name)
        {
            foreach (Type type in new[] { typeof(RoR2Content.Buffs), typeof(EntityState).Assembly.GetType("RoR2.DLC2Content+Buffs") })
                if (type != null && AccessTools.Field(type, name)?.GetValue(null) is BuffDef buff) return buff;
            return BuffCatalog.GetBuffDef(BuffCatalog.FindBuffIndex(name));
        }
        internal static int MagazineKind(string value)
        {
            string mode = (value ?? "").Split(':')[0].Replace(" ", "").Trim();
            return string.Equals(mode, "ExtraShot", StringComparison.OrdinalIgnoreCase) ? 0 : string.Equals(mode, "ExtraDamage", StringComparison.OrdinalIgnoreCase) ? 1 : string.Equals(mode, "DamageMultiplier", StringComparison.OrdinalIgnoreCase) ? 3 : 2;
        }
        internal static float MagazineDamage(string value)
        {
            string[] parts = (value ?? "").Split(':');
            return parts.Length == 2 && float.TryParse(parts[1].Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) && !float.IsNaN(result) && !float.IsInfinity(result) ? Math.Max(0f, result) : 50f;
        }
        internal static float MagazineDamageMultiplier(float damage, int magazines, float percent) => damage * (1f + Math.Max(0, magazines) * percent / 100f);
        internal static float LightInterval(float native, int magazines) => 1f / (1f / Math.Max(0.001f, native) + Math.Max(0, magazines));
        private static int Magazines(CharacterBody body) => body && body.inventory ? body.inventory.GetItemCountEffective(RoR2Content.Items.SecondarySkillMagazine) : 0;
        private static void Apply()
        {
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 4) { p.Skill.SetKeyword("KEYWORD_WEAK", C.Get(weaken)); p.Skill.SetKeyword("3QS_KEYWORD_FROST", C.Get(frost)); }
                if (p.Index == 6) p.Skill.SetKeyword("KEYWORD_FREEZING", true);
                if (p.Index == 1 || p.Index == 2)
                {
                    SkillDef source = p.Skill.Definition, scope = p.ActivationDefinition;
                    scope.baseMaxStock = source.baseMaxStock; scope.rechargeStock = source.rechargeStock;
                    // Scoping is free. BaseFireSnipe pays secondary stocks when a shot is fired.
                    scope.stockToConsume = 0; scope.requiredStock = 1;
                    scope.baseRechargeInterval = source.baseRechargeInterval;
                    if (!S.AutoSprintHandles(scope.activationState.stateType))
                    {
                        scope.cancelSprintingOnActivation = source.cancelSprintingOnActivation;
                        scope.canceledFromSprinting = source.canceledFromSprinting;
                    }
                }
                else if (p.AttackDefinition && p.AttackDefinition != p.Skill.Definition)
                {
                    SkillDef source = p.Skill.Definition, target = p.AttackDefinition;
                    // Firing the charged primary must not inherit the special's cooldown or stocks.
                    if (!S.AutoSprintHandles(target.activationState.stateType))
                    {
                        target.cancelSprintingOnActivation = source.cancelSprintingOnActivation;
                        target.canceledFromSprinting = source.canceledFromSprinting;
                    }
                    target.isCombatSkill = source.isCombatSkill;
                    target.keywordTokens = p.Keywords[target].Where(k => k != "KEYWORD_AGILE").Concat(source.keywordTokens.Where(k => k == "KEYWORD_AGILE")).Distinct().ToArray();
                }
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) if (IsRailgunner(body)) { stats?.Apply(body); body.MarkAllStatsDirty(); }
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null) return null;
            string name = state.GetType().Name;
            int index = Array.IndexOf(Attacks, name);
            if (name == "Offline" || name == "ChargingSuper" || name == "ChargedSuper" || name == "ChargeSnipeSuper" || name == "WindupSnipeSuper") index = 5;
            if (name == "UseCryo" || name == "ChargingCryo" || name == "ChargedCryo" || name == "ChargeSnipeCryo" || name == "WindupSnipeCryo") index = 6;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void Set(StateScope scope, object obj, string name, object value)
        {
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(field.IsStatic ? null : obj, value); return;
            }
        }
        private static void BeforeState(EntityState __instance, out StateScope __state)
        {
            __state = new StateScope { Previous = activeState };
            CharacterBody body = Body(__instance); if (!IsRailgunner(body)) return;
            Profile p = ForState(__instance);
            if (generalEnabled.Value && State("BaseFireSnipe").IsInstanceOfType(__instance)) Set(__state, __instance, "critDamageMultiplier", 1f);
            if (p == null) return;
            activeState = __instance;
            string name = __instance.GetType().Name;
            if (p.Skill.Damage != null) Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Skill.Proc != null) Set(__state, __instance, "procCoefficient", C.Get(p.Skill.Proc));
            if (p.Index == 0 && C.Get(p.GroundRecoil) && Read(Read(body, "characterMotor"), "isGrounded") is bool grounded && grounded) Set(__state, __instance, "selfKnockbackForce", 0f);
            if (p.Index == 1 || p.Index == 2)
            {
                int magazines = Magazines(body), mode = MagazineKind(C.Get(p.Magazine));
                if (mode == 1) Set(__state, __instance, "damageCoefficient", (C.Get(p.Skill.Damage) + MagazineDamage(C.Get(p.Magazine)) * magazines) / 100f);
                if (mode == 3) Set(__state, __instance, "damageCoefficient", MagazineDamageMultiplier(C.Get(p.Skill.Damage), magazines, MagazineDamage(C.Get(p.Magazine))) / 100f);
                if (p.Index == 2 && mode == 0) Set(__state, __instance, "baseDuration", Number(p.Skill.Sample, "baseDuration", 0.5f) * LightRateFactor(p, magazines));
            }
            if (p.Index >= 5)
            {
                Set(__state, __instance, "critDamageMultiplier", 1f);
                if (name.StartsWith("Charging", StringComparison.Ordinal)) Set(__state, __instance, "baseDuration", C.Get(p.Charge));
                if (name == "Offline") Set(__state, __instance, "baseDuration", C.Get(p.Disable));
                if (name == "UseCryo") Set(__state, __instance, "duration", C.Get(p.Disable) / Math.Max(0.001f, body.attackSpeed));
            }
        }
        private static Exception EndScope(StateScope __state, Exception __exception)
        {
            if (__state != null) { for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value); activeState = __state.Previous; }
            return __exception;
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            if (!__instance.owner || !IsRailgunner(__instance.owner.GetComponent<CharacterBody>()) || !__instance.sniper) return;
            Profile p = ForState(activeState);
            if (p?.Skill.Falloff != null) __instance.falloffModel = C.Get(p.Skill.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
            if (!generalEnabled.Value && p?.WeakBonus == null) return;
            var previous = __instance.modifyOutgoingDamageCallback;
            __instance.modifyOutgoingDamageCallback = (BulletAttack bullet, ref BulletAttack.BulletHit hit, DamageInfo damage) =>
            {
                if (hit.isSniperHit && generalEnabled.Value) damage.crit = C.Get(guarantee) || bullet.isCrit;
                previous?.Invoke(bullet, ref hit, damage);
                if (hit.isSniperHit) damage.damage *= 1f + ((generalEnabled.Value ? C.Get(weakBonus) : 0f) + (p?.WeakBonus != null ? C.Get(p.WeakBonus) : 0f)) / 100f;
            };
        }
        private static void BeforeStats(CharacterBody __instance, out StatsScope __state)
        {
            __state = new StatsScope { Previous = calculatingStats };
            __state.Convert = IsRailgunner(__instance) && __instance.inventory && __instance.inventory.GetItemCountEffective(DLC1Content.Items.ConvertCritChanceToCritDamage) > 0;
            calculatingStats = __state.Convert ? __instance : null;
        }
        private static bool BeforeCritItemCount(Inventory __instance, object __0, ref int __result)
        {
            if (!calculatingStats || calculatingStats.inventory != __instance) return true;
            if (__0 is ItemDef def && def == DLC1Content.Items.ConvertCritChanceToCritDamage || __0 is ItemIndex index && index == DLC1Content.Items.ConvertCritChanceToCritDamage.itemIndex) { __result = 0; return false; }
            return true;
        }
        private static void AfterStats(CharacterBody __instance, StatsScope __state)
        {
            if (!__state.Convert) return;
            float chance = __instance.crit;
            Write(__instance, "critMultiplier", __instance.critMultiplier + chance * C.Get(critPerChance) / 100f);
            if (C.Get(convert)) Write(__instance, "crit", 0f);
        }
        private static Exception EndStats(StatsScope __state, Exception __exception) { calculatingStats = __state.Previous; return __exception; }
        private static void AfterBody(CharacterBody __instance) { if (IsRailgunner(__instance)) stats?.Apply(__instance); }
        private static void AfterPerfectDamage(EntityState __instance, ref float __result)
        {
            if (IsRailgunner(Body(__instance)) && profiles.TryGetValue(1, out Profile p) && ForState(activeState) == p) __result = Body(__instance).damage * C.Get(p.PerfectBonus) / 100f;
        }
        private static bool BeforeConsumeBoost(EntityState __instance, bool queueReload)
        {
            CharacterBody body = Body(__instance);
            if (!IsRailgunner(body) || !profiles.TryGetValue(1, out Profile p) || !C.Get(p.AllPerfect) || ForState(activeState) != p || !body.skillLocator.secondary) return true;
            return body.skillLocator.secondary.stock <= (p.Skill.ConsumeAll ? body.skillLocator.secondary.stock : p.Skill.Definition.stockToConsume);
        }
        private static void BeforeDeductStock(GenericSkill __instance, ref int count)
        {
            Profile p = ForState(activeState);
            if (p == null || (p.Index != 1 && p.Index != 2) || !IsRailgunner(__instance.characterBody) || __instance.characterBody != Body(activeState) || __instance.characterBody.skillLocator.secondary != __instance) return;
            count = p.Skill.ConsumeAll ? __instance.stock : p.Skill.Definition.stockToConsume;
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        {
            __state = null;
            if (definitions.TryGetValue(__instance, out Profile p) && __instance == p.Skill.Definition && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; }
        }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (IsRailgunner(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && __instance == p.Skill.Definition && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false;
        }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (IsRailgunner(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void AfterMaxStock(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        {
            if (!IsRailgunner(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p) || p.Magazine == null) return;
            bool secondary = skillSlot.characterBody.skillLocator.secondary == skillSlot;
            int magazines = Magazines(skillSlot.characterBody);
            if (secondary && !__instance.dontAllowPastMaxStocks && (p.Index == 2 || MagazineKind(C.Get(p.Magazine)) != 0)) __result -= Math.Min(magazines, Math.Max(0, skillSlot.bonusStockFromBody));
            else if (!secondary && p.Index == 1 && MagazineKind(C.Get(p.Magazine)) == 0) __result += magazines;
        }
        private static float LightRateFactor(Profile p, int magazines)
        {
            float interval = p.Skill.Definition.baseRechargeInterval;
            if (interval <= 0f) interval = Number(p.Skill.Sample, "baseDuration", 0.5f);
            return LightInterval(interval, magazines) / Math.Max(0.001f, interval);
        }
        private static void AfterLightRecharge(GenericSkill __instance, ref float __result)
        {
            if (!IsRailgunner(__instance.characterBody) || !definitions.TryGetValue(__instance.skillDef, out Profile p) || p.Index != 2 || MagazineKind(C.Get(p.Magazine)) != 0) return;
            __result *= LightRateFactor(p, Magazines(__instance.characterBody));
        }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!IsRailgunner(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p) || !p.Skill.CooldownAfterDuration) return;
            if (skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsRailgunner(fireProjectileInfo.owner.GetComponent<CharacterBody>())) return;
            GameObject prefab = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(p => p.Projectile == prefab); if (p == null) return;
            if (p.Skill.ProjectileSpeed != null) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
            if (p.Index == 3) { fireProjectileInfo.damage = fireProjectileInfo.owner.GetComponent<CharacterBody>().damage * C.Get(p.Skill.Damage) / 100f; fireProjectileInfo.force = p.NativeInputForce > 0f || p.NativeForce <= 0f ? C.Get(p.Push) : 0f; }
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => ConfigureProjectile(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance) => ConfigureProjectile(__instance);
        private static void ConfigureProjectile(ProjectileController pc)
        {
            if (!pc || !pc.owner || !IsRailgunner(pc.owner.GetComponent<CharacterBody>())) return;
            Profile p = profiles.Values.FirstOrDefault(p => p.ProjectileFamily.Any(g => g.GetComponent<ProjectileController>() && g.GetComponent<ProjectileController>().catalogIndex == pc.catalogIndex));
            if (p == null || !configuredProjectiles.GetOrCreateValue(pc).Add(p.Index)) return;
            if (p.Skill.Proc != null) pc.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Skill.Falloff != null)
            {
                var damageMarker = pc.GetComponent<DamageMarker>() ?? pc.gameObject.AddComponent<DamageMarker>();
                damageMarker.Profile = p; damageMarker.Origin = pc.transform.position;
            }
            if (p.Index == 0)
            {
                var steer = pc.GetComponent<ProjectileSteerTowardTarget>();
                if (steer)
                {
                    float factor = p.NativeTracking > 0f ? C.Get(p.Tracking) / p.NativeTracking : 1f;
                    foreach (string field in new[] { "maxRotationSpeed", "minRotationSpeed", "rotationChangePerSecond" }) Write(steer, field, Number(steer, field) * factor);
                    steer.rotationSpeed = C.Get(p.Tracking);
                }
            }
            foreach (ProjectileExplosion explosion in pc.GetComponentsInChildren<ProjectileExplosion>(true))
                if (p.Skill.Radius != null)
                {
                    explosion.blastRadius = C.Get(p.Skill.Radius); explosion.falloffModel = (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), C.Get(p.Skill.Area).ToString());
                    if (p.Index == 3)
                    {
                        explosion.blastDamageCoefficient = 1f; explosion.blastProcCoefficient = 1f;
                        if (p.NativeForce > 0f) explosion.bonusBlastForce *= C.Get(p.Push) / p.NativeForce;
                    }
                }
            if (p.Index == 4)
                foreach (BuffWard ward in pc.GetComponentsInChildren<BuffWard>(true))
                {
                    ward.Networkradius = C.Get(p.Skill.Radius); ward.buffDef = FindBuff("Slow" + C.Get(slow));
                    ward.expires = C.Get(p.Skill.Duration) >= 0f; ward.expireDuration = Math.Max(0f, C.Get(p.Skill.Duration));
                    AreaMarker marker = ward.GetComponent<AreaMarker>() ?? ward.gameObject.AddComponent<AreaMarker>();
                    marker.Profile = p; marker.Owner = pc.owner;
                }
        }
        internal static float ProjectileFalloff(BulletAttack.FalloffModel model, float distance)
        {
            if (model == BulletAttack.FalloffModel.DefaultBullet) return 0.5f + Mathf.Clamp01((60f - distance) / 35f) * 0.5f;
            if (model == BulletAttack.FalloffModel.Buckshot) return 0.3f + Mathf.Clamp01((40f - distance) / 28f) * 0.7f;
            return 1f;
        }
        private static bool BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out DamageScope __state)
        {
            __state = new DamageScope { Previous = cryoExecution }; cryoExecution = null;
            if (!NetworkServer.active || !damageInfo.attacker || !IsRailgunner(damageInfo.attacker.GetComponent<CharacterBody>())) return true;
            var marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<DamageMarker>() : null;
            if (marker?.Profile.Index == 3 && marker.Profile.SelfDamage != null && !C.Get(marker.Profile.SelfDamage) && damageInfo.attacker == __instance.gameObject)
            {
                // BlastAttack normally routes launch force through TakeDamage. Retain that
                // force explicitly, then skip damage processing and reject self-hit procs.
                __instance.TakeDamageForce(damageInfo);
                damageInfo.damage = 0f; damageInfo.procCoefficient = 0f; damageInfo.rejected = true;
                return false;
            }
            if (marker?.Profile.Skill.Falloff != null)
            {
                var model = C.Get(marker.Profile.Skill.Falloff);
                var native = model == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : model == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
                damageInfo.damage *= ProjectileFalloff(native, Vector3.Distance(marker.Origin, damageInfo.position));
            }
            if (!profiles.TryGetValue(6, out Profile p) || !C.Get(p.Execute) || (damageInfo.damageType & DamageType.Freeze2s) == 0 || damageInfo.rejected) return true;
            var hurt = __instance.GetComponent<SetStateOnHurt>();
            if (hurt && hurt.canBeFrozen && __instance.body && (__instance.body.bodyFlags & CharacterBody.BodyFlags.ImmuneToExecutes) == 0)
            {
                BuffDef immunity = FindBuff("FreezeImmune");
                if (!immunity || !__instance.body.HasBuff(immunity)) cryoExecution = __instance;
            }
            return true;
        }
        private static Exception EndDamage(DamageScope __state, Exception __exception) { cryoExecution = __state.Previous; return __exception; }
        private static bool IsFrozenForExecution(HealthComponent target) => target.isInFrozenState || target == cryoExecution;
        private static IEnumerable<CodeInstruction> ExecuteFrozenShot(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.Property(typeof(HealthComponent), "isInFrozenState").GetGetMethod();
            MethodInfo replacement = AccessTools.Method(typeof(Railgunner), nameof(IsFrozenForExecution));
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && Equals(instruction.operand, getter))
                { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
                yield return instruction;
            }
        }
        private static string PassiveDescription() => "Weakpoints can be highlighted while aiming. Gain <style=cIsDamage>" + S.Number(C.Get(critPerChance)) + "% critical strike damage</style> per <style=cIsDamage>1% critical strike chance</style>." + (C.Get(convert) ? " Critical strike chance is converted into critical damage." : " You can still critically strike normally.");
        private static string Description(Profile p)
        {
            string damage = p.Skill.Damage == null ? "" : S.Damage(S.Number(C.Get(p.Skill.Damage)) + "% damage");
            switch (p.Index)
            {
                case 0: return p.Skill.AgileText + "Fire aggressive tracking rounds for " + damage + ".";
                case 1: return p.Skill.AgileText + "Activate your long-range scope, highlighting <style=cIsDamage>Weak Points</style> and transforming your weapon into a piercing railgun for " + damage + ". Perfectly reloading adds " + S.Damage(S.Number(C.Get(p.PerfectBonus)) + "% base damage") + (C.Get(p.AllPerfect) ? " to every shot before reloading." : " to your next shot.");
                case 2: return p.Skill.AgileText + "Activate your short-range scope, highlighting <style=cIsDamage>Weak Points</style> and transforming your weapon into a rapid-fire railgun for " + damage + "." + (MagazineKind(C.Get(p.Magazine)) == 3 ? " Each <style=cIsUtility>Backup Magazine</style> adds <style=cIsDamage>" + S.Number(MagazineDamage(C.Get(p.Magazine))) + "% shot damage</style>." : "");
                case 3: return p.Skill.AgileText + "Throw out a device that pushes away all nearby enemies and you, dealing " + damage + "." + (!C.Get(p.SelfDamage) ? " Does not damage you." : " Can damage you.");
                case 4: return p.Skill.AgileText + "Throw out a device that creates a field, <style=cIsUtility>slowing</style> enemies by <style=cIsUtility>" + C.Get(slow) + "%</style>" + (C.Get(weaken) ? " and <style=cIsUtility>weakening</style> them" : "") + "." + (C.Get(frost) ? " Applies <style=cIsUtility>Frost</style> every <style=cIsUtility>" + S.Number(C.Get(frostInterval)) + " seconds</style>." : "");
                default: return p.Skill.AgileText + "Temporarily supercharge your railgun, firing " + (p.Index == 6 ? "a <style=cIsUtility>freezing</style> shot" : "a piercing shot") + " for " + damage + " with <style=cIsDamage>" + S.Number(C.Get(p.WeakBonus)) + "% bonus weakpoint damage</style>." + (C.Get(p.Disable) > 0f ? " Afterwards, weapons are <style=cIsUtility>disabled</style> for <style=cIsUtility>" + S.Number(C.Get(p.Disable)) + " seconds</style>." : "");
            }
        }
        private static string AttackDescription(Profile p) => p.Skill.AgileText + (p.Index == 6 ? "<style=cIsUtility>Freezing.</style> " : "") + "Fire a piercing railgun shot for " + S.Damage(S.Number(C.Get(p.Skill.Damage)) + "% damage") + (p.WeakBonus != null ? " with <style=cIsDamage>" + S.Number(C.Get(p.WeakBonus)) + "% bonus weakpoint damage</style>" : "") + ".";
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) text += "\nRadius: " + S.Utility(S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) text += "\nDuration: " + S.Utility(C.Get(p.Skill.Duration) < 0f ? "Unlimited" : S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.Charge != null) text += "\nCharge Time: " + S.Utility(S.Number(C.Get(p.Charge)) + "s");
            return text;
        }
    }
}
