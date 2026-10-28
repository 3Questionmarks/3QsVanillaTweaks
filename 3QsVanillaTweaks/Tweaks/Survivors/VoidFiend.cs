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
    internal static class VoidFiend
    {
        private const string Root = "Survivors - 13 Void Fiend", StatsRoot = "Survivor-Stats - 13 Void Fiend", FloodMachine = "3QSVoidFiendFlood";
        private static readonly string[] Names = { "Drown", "Corrupted Drown", "Flood", "Corrupted Flood", "Trespass", "Corrupted Trespass", "Suppress", "Corrupted Suppress" };
        private static readonly string[] Attacks = { "Weapon.FireHandBeam", "Weapon.FireCorruptHandBeam", "Weapon.FireMegaBlasterSmall", "Weapon.FireMegaBlasterBig", "VoidBlinkBase+VoidBlinkUp", "VoidBlinkBase+VoidBlinkDown", "Weapon.CrushCorruption", "Weapon.CrushHealth" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal string NativeMachine;
            internal GameObject Projectile, FullProjectile;
            internal ConfigEntry<float> Range, SelfSlow, MaxDamage, Charge, Movement, FallTime, Corruption, Healing, Cooldown;
            internal ConfigEntry<int> Slow;
            internal ConfigEntry<bool> Concurrent, ProtectFall, RequireCorruption, RequireHealth;
            internal float NativeRadius = 1f, FullRadius = 1f, NativeMinRange, NativeRange;
            internal bool NativeFullRestock;
        }
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class MovementData { internal float Baseline; }
        private sealed class LandingProtection { internal float Expires; }
        private sealed class Payment { internal int Cost; }
        private sealed class RechargeBlock { internal bool Previous; }
        private sealed class ProjectileMarker : MonoBehaviour
        {
            internal Profile Profile;
            internal Vector3 Origin;
        }
        private sealed class HitProfile { internal Profile Profile; }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<string, ConfigEntry<float>> corruptStats = new Dictionary<string, ConfigEntry<float>>();
        private static readonly HashSet<MethodBase> stateHooks = new HashSet<MethodBase>();
        private static readonly ConditionalWeakTable<EntityState, MovementData> movement = new ConditionalWeakTable<EntityState, MovementData>();
        private static readonly ConditionalWeakTable<CharacterBody, LandingProtection> landings = new ConditionalWeakTable<CharacterBody, LandingProtection>();
        private static readonly ConditionalWeakTable<DamageInfo, HitProfile> hits = new ConditionalWeakTable<DamageInfo, HitProfile>();
        private static readonly ConditionalWeakTable<ProjectileController, HitProfile> projectiles = new ConditionalWeakTable<ProjectileController, HitProfile>();
        private static readonly ConditionalWeakTable<GenericSkill, RechargeBlock> floodRechargeBlocks = new ConditionalWeakTable<GenericSkill, RechargeBlock>();
        private static ConfigEntry<bool> enabled, passiveEnabled, statsEnabled;
        private static ConfigEntry<float> onDamage, onCrit, minimum, onHeal, perSecond, onKill, depletion;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static EntityStateMachine floodMachine;
        private static Action<SkillDef, GenericSkill, float> baseSkillTick;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static CharacterBody protectedLanding;
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.VoidSurvivor." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsFiend(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static object Read(object obj, string name)
        {
            if (obj == null) return null;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = t.GetField(name, flags); if (field != null) return field.GetValue(obj);
                PropertyInfo property = t.GetProperty(name, flags); if (property != null) return property.GetValue(obj, null);
            }
            return null;
        }
        private static float Number(object obj, string name, float fallback = 0f) => Read(obj, name) is IConvertible value ? Convert.ToSingle(value) : fallback;
        private static void Write(object obj, string name, object value)
        {
            if (obj == null) return;
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                FieldInfo field = t.GetField(name, flags); if (field != null) { field.SetValue(obj, value); return; }
                PropertyInfo property = t.GetProperty(name, flags); if (property?.GetSetMethod(true) != null) { property.SetValue(obj, value, null); return; }
            }
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Void Fiend changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Void Corruption", "Changes Enabled", true, "Enable Void Corruption changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Void Fiend stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo method = type == null ? null : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type?.FullName, name);
            method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray());
            harmony.Patch(method, prefix == null ? null : new HarmonyMethod(typeof(VoidFiend), prefix), postfix == null ? null : new HarmonyMethod(typeof(VoidFiend), postfix), finalizer: finalizer == null ? null : new HarmonyMethod(typeof(VoidFiend), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "OnExit" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType == typeof(EntityState) || method.DeclaringType == typeof(BaseState)) continue;
                method = AccessTools.DeclaredMethod(method.DeclaringType, method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray());
                if (!stateHooks.Add(method)) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(VoidFiend), nameof(BeforeState)), postfix: new HarmonyMethod(typeof(VoidFiend), nameof(AfterState)), finalizer: new HarmonyMethod(typeof(VoidFiend), nameof(EndScope)));
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/VoidSurvivor/VoidSurvivorBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.VoidFiend");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefabBody);
                if (passiveEnabled.Value) BindPassive();
                var defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToList();
                object corruptMode = EntityStateCatalog.InstantiateState(State("CorruptMode.CorruptMode"));
                string[] slots = { "primaryOverrideSkillDef", "secondaryOverrideSkillDef", "utilityOverrideSkillDef", "specialOverrideSkillDef" };
                SkillDef[] corrupt = slots.Select(s => Read(corruptMode, s) as SkillDef).ToArray();
                // Resolve the original skills by their activation states. The mode's configured
                // defaults can refer to an alternative skill rather than the equipped vanilla one.
                string[] corruptStates = { "Weapon.ChargeCorruptHandBeam", "Weapon.FireMegaBlasterBig", "VoidBlinkDown", "Weapon.ChargeCrushHealth" };
                SkillDef[] catalog = SkillCatalog.allSkillDefs.ToArray();
                for (int i = 0; i < corrupt.Length; i++)
                {
                    Type expected = State(corruptStates[i]);
                    if (expected != null && (!corrupt[i] || corrupt[i].activationState.stateType != expected))
                        corrupt[i] = catalog.FirstOrDefault(d => d && d.activationState.stateType == expected) ?? corrupt[i];
                }
                var displayNames = defs.Concat(corrupt).Where(d => d).Distinct().ToDictionary(d => d, d => Language.GetString(d.skillNameToken));
                // Corrupted overrides are separate skills, even when their localisation tokens match.
                defs = defs.Where(d => !corrupt.Contains(d)).ToList();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    try
                    {
                        SkillDef def = i % 2 == 1 ? corrupt[i / 2] : defs.FirstOrDefault(d => d.activationState.stateType == State(i == 0 ? "Weapon.ChargeHandBeam" : i == 2 ? "Weapon.ChargeMegaBlaster" : i == 4 ? Attacks[4] : "Weapon.ChargeCrushCorruption"));
                        if (!def && i == 4) def = defs.FirstOrDefault(d => d.activationState.stateType != null && State("VoidBlinkBase").IsAssignableFrom(d.activationState.stateType));
                        if (!def && i == 0) def = defs.FirstOrDefault(d => d.activationState.stateType == State("Weapon.FireHandBeam"));
                        if (!def) throw new MissingMemberException("Void Fiend skill unavailable: " + Names[i]);
                        if (definitions.ContainsKey(def)) throw new InvalidOperationException("Both forms resolved to the same SkillDef: " + Names[i]);
                        Type attack = i == 4 || i == 5 ? def.activationState.stateType : State(Attacks[i]);
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, attack, switches[Names[i]]), NativeMachine = def.activationStateMachineName };
                        Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                        Hook(attack); Hook(def.activationState.stateType);
                        if (i == 2) { Hook(State("Weapon.ReadyMegaBlaster")); Hook(State("Weapon.FireMegaBlasterBig")); }
                        string name = displayNames.TryGetValue(def, out string originalName) ? originalName : Names[i];
                        if (i % 2 == 1 && defs.Any(d => displayNames[d] == name) && !name.StartsWith("Corrupted ", StringComparison.OrdinalIgnoreCase)) name = "Corrupted " + name;
                        // Shared native name/description aliases cannot identify a form reliably.
                        // Use distinct tokens before registering with the tooltip refresh helpers.
                        def.skillNameToken = "3QS_VOIDFIEND_SKILL_" + i + "_NAME";
                        def.skillDescriptionToken = "3QS_VOIDFIEND_SKILL_" + i + "_DESCRIPTION";
                        S.SetText(def.skillNameToken, () => name);
                        var tooltip = new S.Skill(p.Skill.Section, def, attack, switches[Names[i]]);
                        S.SetDescription(tooltip, def.skillDescriptionToken, () => Description(p));
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? (Func<float>)null : () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Fiend skill could not be loaded: " + Names[i] + ": " + error); }
                }
                if (stats != null || passiveEnabled.Value) Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody));
                if (passiveEnabled.Value || profiles.ContainsKey(1))
                {
                    Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterStats));
                    harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(VoidFiend), nameof(StatsIL)));
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillTick));
                    Patch(typeof(GenericSkill), "ManagedFixedUpdate", nameof(BeforeSlotTick));
                }
                if (profiles.ContainsKey(0) || profiles.ContainsKey(1))
                {
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet));
                    Patch(typeof(HealthComponent), "TakeDamage", postfix: nameof(AfterDamage));
                }
                if (profiles.ContainsKey(2) || profiles.ContainsKey(3))
                {
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeProjectileDamage));
                }
                if (profiles.ContainsKey(2))
                {
                    Patch(typeof(GenericSkill), "RunRecharge", nameof(BeforeFloodRecharge), args: new[] { typeof(float), typeof(bool) });
                    Patch(typeof(GenericSkill), "RechargeBaseSkill", nameof(BeforeFloodRecharge));
                    Patch(typeof(RoR2.UI.SkillIcon), "LateUpdate", nameof(BeforeFloodIconUpdate));
                }
                if (profiles.ContainsKey(4) || profiles.ContainsKey(5))
                {
                    harmony.Patch(AccessTools.Method(State("VoidBlinkBase"), "GetVelocity"), transpiler: new HarmonyMethod(typeof(VoidFiend), nameof(MovementIL)));
                    Patch(typeof(GlobalEventManager), "OnCharacterHitGroundServer", nameof(BeforeLanding), finalizer: nameof(EndLanding));
                    Patch(typeof(GlobalEventManager), "IsImmuneToFallDamage", postfix: nameof(AfterFallImmunity));
                }
                if (profiles.ContainsKey(6) || profiles.ContainsKey(7)) Patch(typeof(VoidSurvivorSkillDef), "HasRequiredCorruption", nameof(BeforeRequiredCorruption));
                if (profiles.ContainsKey(7))
                {
                    Patch(State("CorruptMode.CorruptMode"), "OnEnter", postfix: nameof(AfterCorruptedEnter));
                    Patch(State("CorruptMode.CorruptMode"), "OnExit", postfix: nameof(AfterCorruptedExit));
                    MethodInfo update = AccessTools.Method(profiles[7].Skill.Definition.GetType(), "OnFixedUpdate");
                    if (update != null && update.DeclaringType != typeof(SkillDef))
                    {
                        // Call the base implementation directly; a virtual call would invoke NoRecharge again.
                        var tick = new DynamicMethod("3QSVoidFiendBaseSkillTick", typeof(void), new[] { typeof(SkillDef), typeof(GenericSkill), typeof(float) }, typeof(VoidFiend), true);
                        ILGenerator il = tick.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldarg_2);
                        il.Emit(OpCodes.Call, AccessTools.Method(typeof(SkillDef), "OnFixedUpdate")); il.Emit(OpCodes.Ret);
                        baseSkillTick = (Action<SkillDef, GenericSkill, float>)tick.CreateDelegate(typeof(Action<SkillDef, GenericSkill, float>));
                        harmony.Patch(update, prefix: new HarmonyMethod(typeof(VoidFiend), nameof(BeforeSpecialRechargeTick)));
                    }
                }
                Apply(); C.EffectiveSettingsChanged += Apply;
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Void Fiend changes could not be loaded: " + error); }
        }
        private static void BindPassive()
        {
            string section = Root + " - Void Corruption";
            VoidSurvivorController controller = prefabBody.GetComponent<VoidSurvivorController>();
            float unit = controller.maxCorruption / 100f;
            onDamage = S.Float(section, "Corruption Increase By Taking Damage", 0f, controller.corruptionForFullDamage / (100f * unit), "Corruption gained for each 1% of maximum health lost.");
            onCrit = S.Float(section, "Corruption Increase By Dealing A Crit", 0f, controller.corruptionPerCrit / unit, "Corruption gained on a critical hit, multiplied by its proc coefficient.");
            minimum = S.Float(section, "Corruption Min For Each Void Item", 2f, controller.minimumCorruptionPerVoidItem / unit, "Minimum corruption added by each void item.");
            onHeal = S.Float(section, "Corruption Reduction By Healing", 0f, -controller.corruptionForFullHeal / (100f * unit), "Corruption lost for each 1% of maximum health healed.");
            perSecond = S.Float(section, "Corruption Gain Per Second (%)", 1.5f, controller.corruptionPerSecondInCombat / unit, "Corruption gained each second while uncorrupted.");
            onKill = S.Float(section, "Corruption Gain On Kill (%)", 5f, 0f, "Corruption gained when you kill an enemy.");
            depletion = S.Float(section, "Corruption Depletion Per Second While Corrupted (%)", 7f, -controller.corruptionFractionPerSecondWhileCorrupted * 100f, "Base corruption drained each second while corrupted. Scales down as minimum corruption increases.");
            string[,] fields = { { "Damage", "Damage", "%" }, { "Armour", "Armor", "" }, { "Health", "MaxHealth", "%" }, { "Shield", "MaxShield", "%" }, { "Health Regen", "Regen", "" }, { "Move Speed", "MoveSpeed", "%" }, { "Attack Speed", "AttackSpeed", "%" }, { "Critical Chance", "Crit", "%" }, { "Critical Damage", "CritMultiplier", "%" }, { "Jump Power", "JumpPower", "%" }, { "Acceleration", "Acceleration", "%" }, { "Sprint Speed", "SprintingSpeedMultiplier", "%" }, { "Extra Jumps", "MaxJumpCount", "" } };
            for (int i = 0; i < fields.GetLength(0); i++)
            {
                string name = fields[i, 0], stat = char.ToLowerInvariant(fields[i, 1][0]) + fields[i, 1].Substring(1);
                corruptStats[stat] = S.Float(section, "Corrupt " + name + " Bonus" + (fields[i, 2] == "%" ? " (%)" : ""), 0f, name == "Armour" ? 100f : 0f, "Additional " + name.ToLowerInvariant() + " while corrupted.");
            }
            foreach (string method in new[] { "OnEnable", "FixedUpdate", "OnTakeDamageServer", "OnDamageDealtServer", "OnCharacterHealServer" }) Patch(typeof(VoidSurvivorController), method, nameof(BeforeController));
            Patch(typeof(GlobalEventManager), "OnCharacterDeath", postfix: nameof(AfterKill));
            S.SetText(prefabBody.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken, PassiveDescription);
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index;
            if (i == 7)
            {
                // Bind first so the common skill settings reuse this entry and its -1 range.
                p.Cooldown = C.Bind(s.Section, "Cooldown", 0f, new ConfigDescription("Cooldown in seconds. 0 recharges instantly. -1 only restores charges when you become corrupted again. Vanilla: -1.", new AcceptableValueRange<float>(-1f, 1000000f)));
                p.NativeFullRestock = s.Definition.fullRestockOnAssign;
                C.Bind(s.Section, "Stocks Recharged Per Cooldown", "1", "Stocks restored per cooldown. Use a whole number or All. Minimum 1 when natural recharge is enabled. Vanilla: 0.");
            }
            if (i == 2) C.Bind(s.Section, "Cooldown Starts After Duration", true, "Wait until Flood has been fired and its animation ends before starting its cooldown. Vanilla: True.");
            s.Bind(damage: false, changedAgile: i == 2 || i >= 4 ? (bool?)true : null, changedStocks: i == 7 ? (int?)1 : null, changedCooldown: i == 7 ? (float?)0f : null);
            if (i <= 3)
            {
                float rate = i == 1 ? Math.Max(0.001f, Number(s.Sample, "tickRate", 8f)) : 1f;
                float nativeDamage = i == 1 ? Number(s.Sample, "damageCoefficientPerSecond") / rate * 100f : Number(s.Sample, "damageCoefficient", 1f) * 100f;
                float nativeProc = i == 1 ? Number(s.Sample, "procCoefficientPerSecond", rate) / rate : i == 0 ? 1f / Math.Max(1f, Number(s.Sample, "bulletCount", 1f)) : 1f;
                s.BindDamage(nativeDamage, i == 1 ? (float?)250f : null, nativeProc, i == 1);
                if (i == 0 || i == 1)
                {
                    p.NativeRange = Number(s.Sample, "maxDistance", 40f); p.NativeMinRange = Number(s.Sample, "minDistance");
                    p.Range = S.Float(s.Section, "Range", i == 0 ? 1000f : 50f, p.NativeRange, "Maximum attack range in metres.");
                    if (i == 0) p.Slow = C.Bind(s.Section, "Slow On Hit (%)", 50, new ConfigDescription("Slow applied when the attack hits. Vanilla: 50.", new AcceptableValueList<int>(30, 50, 60, 80)));
                    else
                    {
                        s.Interval = S.Float(s.Section, "Damage Interval", 0.125f, 1f / rate, "Seconds between hits before attack speed is applied.", 0.001f);
                        p.SelfSlow = S.Float(s.Section, "Self Slow (%)", 50f, 50f, "Movement speed penalty while firing.");
                    }
                }
                else
                {
                    p.Projectile = Read(s.Sample, "projectilePrefab") as GameObject;
                    if (!p.Projectile) throw new MissingMemberException("Projectile unavailable: " + Names[i]);
                    s.BindProjectileSpeed(p.Projectile);
                    var explosion = p.Projectile.GetComponentInChildren<ProjectileExplosion>(true);
                    p.NativeRadius = explosion ? explosion.blastRadius : 0f;
                    if (i == 2)
                    {
                        object big = EntityStateCatalog.InstantiateState(State("Weapon.FireMegaBlasterBig"));
                        p.FullProjectile = Read(big, "projectilePrefab") as GameObject;
                        var fullExplosion = p.FullProjectile ? p.FullProjectile.GetComponentInChildren<ProjectileExplosion>(true) : null;
                        p.FullRadius = fullExplosion ? fullExplosion.blastRadius : p.NativeRadius;
                        if (fullExplosion) explosion = fullExplosion;
                        p.MaxDamage = S.Float(s.Section, "Damage At Maximum Charge (%)", 1100f, Number(big, "damageCoefficient", 11f) * 100f, "Damage at full charge as a percentage of base damage.");
                        p.Charge = S.Float(s.Section, "Charge Time", 1f, Number(EntityStateCatalog.InstantiateState(s.Definition.activationState.stateType), "baseDuration", 2f), "Full charge time in seconds, before attack speed is applied.", 0.001f);
                        p.Concurrent = S.Bool(s.Section, "Can Use Other Abilities While Charging", true, false, "Allow other abilities to be used while charging Flood.");
                    }
                    float radius = i == 2 ? p.FullRadius : p.NativeRadius;
                    s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, i == 2 ? "Explosion radius at full charge in metres. 0 uses the normal radius." : "Explosion radius in metres.");
                    s.Area = C.Bind(s.Section, "AOE Damage Falloff", explosion ? (S.AreaFalloff)Enum.Parse(typeof(S.AreaFalloff), explosion.falloffModel.ToString()) : S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: " + (explosion ? explosion.falloffModel.ToString() : "None") + ".");
                }
            }
            else if (i == 4 || i == 5)
            {
                s.Duration = S.Float(s.Section, "Duration", Number(s.Sample, "duration", 0.3f), Number(s.Sample, "duration", 0.3f), "Travel duration in seconds.", 0.001f);
                p.Movement = C.Bind(s.Section, "Movement Speed Scaling (%)", 50f, new ConfigDescription("How strongly movement speed changes affect travel distance. 0 ignores changes; 100 uses normal scaling. Vanilla: 100.", new AcceptableValueRange<float>(0f, 100f)));
                p.ProtectFall = S.Bool(s.Section, "Fall Damage Immunity", true, false, "Ignore fall damage on the first landing after the skill.");
                p.FallTime = S.Float(s.Section, "Fall Damage Immunity Time", 5f, 0f, "Seconds after the skill ends during which the first landing is protected.");
            }
            else
            {
                p.Corruption = S.Float(s.Section, "Corruption (%)", i == 6 ? -25f : 25f, Number(s.Sample, "corruptionChange"), "Corruption gained when used. Negative values consume corruption.", -100f);
                p.Healing = S.Float(s.Section, "Healing (%)", i == 6 ? 25f : -25f, Number(s.Sample, "selfHealFraction") * 100f, "Maximum health restored when used. Negative values consume health.", -100f);
                p.RequireCorruption = S.Bool(s.Section, "Requires Corruption To Cast", true, true, "Require enough corruption to pay the corruption cost.");
                p.RequireHealth = S.Bool(s.Section, "Requires Health To Cast", true, i == 7, "Require enough health to pay the health cost.");
            }
        }
        private static void EnsureFloodMachine()
        {
            if (floodMachine || !profiles.ContainsKey(2)) return;
            floodMachine = prefabBody.GetComponents<EntityStateMachine>().FirstOrDefault(m => m.customName == FloodMachine);
            if (!floodMachine)
            {
                floodMachine = prefabBody.gameObject.AddComponent<EntityStateMachine>(); floodMachine.customName = FloodMachine;
                floodMachine.initialStateType = floodMachine.mainStateType = new SerializableEntityStateType(typeof(Idle));
                var network = prefabBody.GetComponent<NetworkStateMachine>();
                if (network) Write(network, "stateMachines", ((Read(network, "stateMachines") as EntityStateMachine[]) ?? Array.Empty<EntityStateMachine>()).Concat(new[] { floodMachine }).ToArray());
            }
        }
        private static void Apply()
        {
            if (profiles.ContainsKey(2)) EnsureFloodMachine();
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 7)
                {
                    bool vanilla = C.Get(p.Cooldown) == -1f;
                    p.Skill.Definition.baseRechargeInterval = Math.Max(0f, C.Get(p.Cooldown));
                    p.Skill.Definition.isCooldownBlockedUntilManuallyReset = vanilla;
                    if (!vanilla) p.Skill.Definition.rechargeStock = Math.Max(1, p.Skill.Definition.rechargeStock);
                    p.Skill.Definition.fullRestockOnAssign = vanilla || p.NativeFullRestock;
                    p.Skill.Definition.beginSkillCooldownOnSkillEnd = C.Get(p.Cooldown) > 0f && p.Skill.CooldownAfterDuration;
                }
                if (p.Index == 2) p.Skill.Definition.activationStateMachineName = C.Get(p.Concurrent) ? FloodMachine : p.NativeMachine;
                if (p.Index >= 6 && p.Skill.Definition is VoidSurvivorSkillDef special) { special.minimumCorruption = 0f; special.maximumCorruption = float.PositiveInfinity; }
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (IsFiend(body))
                {
                    stats?.Apply(body); if (passiveEnabled.Value) ConfigureController(body.GetComponent<VoidSurvivorController>());
                    foreach (GenericSkill slot in body.GetComponents<GenericSkill>())
                        if (slot.skillDef && definitions.TryGetValue(slot.skillDef, out Profile p) && p.Index == 7)
                        {
                            if (C.Get(p.Cooldown) != -1f) Write(slot, "isCooldownBlocked", false);
                            AccessTools.Method(typeof(GenericSkill), "RecalculateValues")?.Invoke(slot, null);
                        }
                    body.MarkAllStatsDirty();
                }
        }
        private static void BeforeController(VoidSurvivorController __instance) => ConfigureController(__instance);
        private static void ConfigureController(VoidSurvivorController controller)
        {
            if (!controller || !IsFiend(controller.GetComponent<CharacterBody>())) return;
            float unit = controller.maxCorruption / 100f;
            controller.corruptionForFullDamage = C.Get(onDamage) * 100f * unit; controller.corruptionPerCrit = C.Get(onCrit) * unit;
            controller.minimumCorruptionPerVoidItem = C.Get(minimum) * unit; controller.corruptionForFullHeal = -C.Get(onHeal) * 100f * unit;
            controller.corruptionPerSecondInCombat = controller.corruptionPerSecondOutOfCombat = C.Get(perSecond) * unit;
            controller.corruptionFractionPerSecondWhileCorrupted = -C.Get(depletion) / 100f;
        }
        private static void AfterKill(DamageReport damageReport)
        {
            if (!NetworkServer.active || damageReport == null || !IsFiend(damageReport.attackerBody)) return;
            if (!damageReport.victimBody || !TeamMask.GetEnemyTeams(TeamComponent.GetObjectTeam(damageReport.attackerBody.gameObject)).HasTeam(TeamComponent.GetObjectTeam(damageReport.victimBody.gameObject))) return;
            var controller = damageReport.attackerBody.GetComponent<VoidSurvivorController>();
            if (controller) { ConfigureController(controller); controller.AddCorruption(C.Get(onKill) * controller.maxCorruption / 100f); }
        }
        private static void AfterBody(CharacterBody __instance)
        { if (IsFiend(__instance)) { stats?.Apply(__instance); if (passiveEnabled.Value) ConfigureController(__instance.GetComponent<VoidSurvivorController>()); } }
        private static void AfterStats(CharacterBody __instance)
        {
            if (!passiveEnabled.Value || !IsFiend(__instance)) return;
            var controller = __instance.GetComponent<VoidSurvivorController>(); if (!controller || !controller.isCorrupted) return;
            foreach (var pair in corruptStats)
            {
                if (pair.Key == "armor" || C.Get(pair.Value) == 0f) continue;
                float value = Number(__instance, pair.Key), bonus = C.Get(pair.Value);
                bool flat = pair.Key == "regen" || pair.Key == "crit" || pair.Key == "critMultiplier" || pair.Key == "maxJumpCount";
                if (pair.Key == "critMultiplier") bonus /= 100f;
                if (pair.Key == "maxJumpCount") Write(__instance, pair.Key, (int)value + (int)bonus);
                else Write(__instance, pair.Key, flat ? value + bonus : value * (1f + bonus / 100f));
            }
        }
        private static float CorruptArmour(float vanilla, CharacterBody body) => passiveEnabled.Value && IsFiend(body) ? C.Get(corruptStats["armor"]) : vanilla;
        private static bool FiringBeam(CharacterBody body) => body && body.GetComponents<EntityStateMachine>().Any(m => m.state?.GetType() == State("Weapon.FireCorruptHandBeam"));
        private static float BeamSlow(float vanilla, CharacterBody body)
        {
            if (!IsFiend(body) || !profiles.TryGetValue(1, out Profile p) || !FiringBeam(body)) return vanilla;
            float configured = C.Get(p.SelfSlow) / 100f;
            return body.GetBuffCount(RoR2Content.Buffs.Slow50) > 1 ? Math.Max(vanilla, configured) : configured;
        }
        private static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> instructions)
        {
            string pending = null; int remaining = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo buff && (buff.Name == "VoidSurvivorCorruptMode" || buff.Name == "Slow50")) { pending = buff.Name; remaining = 10; }
                yield return instruction;
                if (remaining-- <= 0 || instruction.opcode != OpCodes.Ldc_R4 || !(instruction.operand is float value)) continue;
                if (pending == "VoidSurvivorCorruptMode" && value == 100f || pending == "Slow50" && value == 0.5f)
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(VoidFiend), pending == "Slow50" ? nameof(BeamSlow) : nameof(CorruptArmour)));
                    pending = null; remaining = 0;
                }
            }
        }
        private static Profile ForState(EntityState state)
        {
            if (state == null) return null;
            Type type = state.GetType(); string name = type.Name; int index = -1;
            // Override skills can change activation states between game versions.
            foreach (Profile profile in profiles.Values)
                if (name != "FireMegaBlasterBig" && profile.Index % 2 == 1 && profile.Skill.Definition.activationState.stateType == type) return profile;
            if (name == "FireHandBeam" || name == "ChargeHandBeam") index = 0;
            else if (name == "FireCorruptHandBeam" || name == "ChargeCorruptHandBeam") index = 1;
            else if (name == "ChargeMegaBlaster" || name == "ReadyMegaBlaster" || name == "FireMegaBlasterSmall") index = 2;
            else if (name == "FireMegaBlasterBig") { var controller = Body(state)?.GetComponent<VoidSurvivorController>(); index = state.outer && state.outer.customName == FloodMachine ? 2 : controller && controller.isCorrupted ? 3 : 2; }
            else if (name == "CrushCorruption" || name == "ChargeCrushCorruption") index = 6;
            else if (name == "CrushHealth" || name == "ChargeCrushHealth") index = 7;
            else if (profiles.TryGetValue(4, out Profile normal) && normal.Skill.StateType == type) index = 4;
            else if (profiles.TryGetValue(5, out Profile corrupted) && corrupted.Skill.StateType == type) index = 5;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static void Set(Scope scope, object obj, string name, object value)
        {
            for (Type t = obj.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field == null) continue;
                if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
                field.SetValue(field.IsStatic ? null : obj, value); return;
            }
        }
        private static void BeforeState(EntityState __instance, MethodBase __originalMethod, out Scope __state)
        {
            __state = new Scope { Previous = activeState }; activeState = null;
            Profile p = ForState(__instance); CharacterBody body = Body(__instance); if (p == null || !IsFiend(body)) return;
            activeState = __instance; string name = __instance.GetType().Name;
            if (p.Index == 0) Set(__state, __instance, "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            if (p.Index == 0 || p.Index == 1)
            {
                Set(__state, __instance, "maxDistance", C.Get(p.Range));
                if (p.Index == 1)
                {
                    Set(__state, __instance, "minDistance", p.NativeRange > 0f ? p.NativeMinRange * C.Get(p.Range) / p.NativeRange : 0f);
                    Set(__state, __instance, "tickRate", 1f / C.Get(p.Skill.Interval));
                    Set(__state, __instance, "damageCoefficientPerSecond", C.Get(p.Skill.Damage) / 100f / C.Get(p.Skill.Interval));
                    Set(__state, __instance, "procCoefficientPerSecond", C.Get(p.Skill.Proc) / C.Get(p.Skill.Interval));
                }
            }
            if (p.Index == 2 || p.Index == 3)
            {
                float damage = p.Index == 2 && name == "FireMegaBlasterBig" ? C.Get(p.MaxDamage) : C.Get(p.Skill.Damage);
                Set(__state, __instance, "damageCoefficient", damage / 100f);
                if (p.Index == 2 && name == "ChargeMegaBlaster") Set(__state, __instance, "baseDuration", C.Get(p.Charge));
            }
            if (p.Index == 4 || p.Index == 5)
            {
                Set(__state, __instance, "duration", C.Get(p.Skill.Duration));
                if (__originalMethod.Name == "OnEnter")
                {
                    float baseline = body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
                    if (body.isSprinting) baseline *= body.sprintingSpeedMultiplier;
                    movement.Remove(__instance); movement.Add(__instance, new MovementData { Baseline = baseline });
                }
            }
            if (p.Index >= 6)
            {
                var controller = body.GetComponent<VoidSurvivorController>();
                Set(__state, __instance, "corruptionChange", C.Get(p.Corruption) * (controller ? controller.maxCorruption / 100f : 1f));
                Set(__state, __instance, "selfHealFraction", C.Get(p.Healing) / 100f);
            }
        }
        private static void AfterState(EntityState __instance, MethodBase __originalMethod)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance); if (p == null || !IsFiend(body)) return;
            if ((p.Index == 4 || p.Index == 5) && __originalMethod.Name == "OnExit" && C.Get(p.ProtectFall) && C.Get(p.FallTime) > 0f)
            { landings.Remove(body); landings.Add(body, new LandingProtection { Expires = Time.fixedTime + C.Get(p.FallTime) }); }
            if (p.Index == 1 && (__originalMethod.Name == "OnEnter" || __originalMethod.Name == "OnExit")) body.MarkAllStatsDirty();
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        { if (__state != null) { for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value); activeState = __state.Previous; } return __exception; }
        internal static float ScaledSpeed(float baseline, float actual, float percentage) => Math.Max(0f, baseline + (actual - baseline) * percentage / 100f);
        private static float MovementSpeed(EntityState state)
        {
            float original = Number(state, "moveSpeedStat"); Profile p = ForState(state);
            if (p?.Movement == null || !IsFiend(Body(state))) return original;
            return movement.TryGetValue(state, out MovementData data) ? ScaledSpeed(data.Baseline, original, C.Get(p.Movement)) : original;
        }
        private static IEnumerable<CodeInstruction> MovementIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo getter && getter.Name == "get_moveSpeedStat")
                { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(VoidFiend), nameof(MovementSpeed)); }
                yield return instruction;
            }
        }
        private static void BeforeLanding(CharacterBody __0, out CharacterBody __state)
        {
            __state = protectedLanding; protectedLanding = null;
            if (!__0 || !landings.TryGetValue(__0, out LandingProtection protection)) return;
            landings.Remove(__0); if (Time.fixedTime <= protection.Expires) protectedLanding = __0;
        }
        private static void AfterFallImmunity(CharacterBody __0, ref bool __result) { if (__0 && __0 == protectedLanding) __result = true; }
        private static Exception EndLanding(CharacterBody __state, Exception __exception) { protectedLanding = __state; return __exception; }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(activeState); if (p == null || p.Index > 1 || !__instance.owner || !IsFiend(__instance.owner.GetComponent<CharacterBody>())) return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            __instance.falloffModel = BulletFalloff(C.Get(p.Skill.Falloff));
            if (p.Index == 0 && C.Get(p.Slow) != 50)
            {
                __instance.damageType &= ~DamageType.SlowOnHit;
                var previous = __instance.modifyOutgoingDamageCallback;
                __instance.modifyOutgoingDamageCallback = (BulletAttack bullet, ref BulletAttack.BulletHit hit, DamageInfo damage) => { previous?.Invoke(bullet, ref hit, damage); hits.Remove(damage); hits.Add(damage, new HitProfile { Profile = p }); };
            }
        }
        private static BuffDef SlowBuff(int percent)
        {
            if (percent == 30) return JunkContent.Buffs.Slow30;
            if (percent == 60) return RoR2Content.Buffs.Slow60;
            if (percent == 80) return RoR2Content.Buffs.Slow80;
            return RoR2Content.Buffs.Slow50;
        }
        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (NetworkServer.active && !damageInfo.rejected && __instance.alive && __instance.body && hits.TryGetValue(damageInfo, out HitProfile hit))
                __instance.body.AddTimedBuff(SlowBuff(C.Get(hit.Profile.Slow)), 2f);
        }
        private static bool MatchesProjectile(Profile p, GameObject source, ProjectileController instance = null)
        {
            if (p == null) return false;
            foreach (GameObject prefab in new[] { p.Projectile, p.FullProjectile })
            {
                if (!prefab) continue;
                if (source) { if (prefab == source) return true; continue; }
                if (!instance) continue;
                ProjectileController native = prefab.GetComponent<ProjectileController>();
                if (native && native.catalogIndex >= 0 && native.catalogIndex == instance.catalogIndex) return true;
                // Some predicted instances are initialised before their catalogue index is assigned.
                if (instance.gameObject.name == prefab.name + "(Clone)") return true;
            }
            return false;
        }
        private static Profile ProjectileProfile(GameObject owner, GameObject source, ProjectileController instance = null)
        {
            CharacterBody body = owner ? owner.GetComponent<CharacterBody>() : null;
            if (!IsFiend(body)) return null;
            VoidSurvivorController controller = body.GetComponent<VoidSurvivorController>();
            // The charged normal shot and Corrupted Flood share a projectile prefab.
            // Its owner determines the form; the cached firing state may already have ended.
            if (controller && controller.isCorrupted)
                return profiles.TryGetValue(3, out Profile corrupted) && MatchesProjectile(corrupted, source, instance) ? corrupted : null;
            if (profiles.TryGetValue(2, out Profile normal) && MatchesProjectile(normal, source, instance)) return normal;
            return null;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            Profile p = ProjectileProfile(fireProjectileInfo.owner, fireProjectileInfo.projectilePrefab); if (p == null) return;
            CharacterBody body = fireProjectileInfo.owner.GetComponent<CharacterBody>();
            float damage = p.Index == 2 && fireProjectileInfo.projectilePrefab == p.FullProjectile ? C.Get(p.MaxDamage) : C.Get(p.Skill.Damage);
            fireProjectileInfo.damage = body.damage * damage / 100f;
            fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController, FireProjectileInfo fireProjectileInfo) => ConfigureProjectile(projectileController, fireProjectileInfo.projectilePrefab);
        private static void BeforeProjectileStart(ProjectileController __instance) => ConfigureProjectile(__instance);
        private static void ConfigureProjectile(ProjectileController pc, GameObject sourcePrefab = null)
        {
            if (!pc || !pc.owner || !IsFiend(pc.owner.GetComponent<CharacterBody>()) || projectiles.TryGetValue(pc, out _)) return;
            Profile p = ProjectileProfile(pc.owner, sourcePrefab, pc); if (p == null) return;
            projectiles.Add(pc, new HitProfile { Profile = p }); pc.procCoefficient = C.Get(p.Skill.Proc);
            var marker = pc.GetComponent<ProjectileMarker>() ?? pc.gameObject.AddComponent<ProjectileMarker>(); marker.Profile = p; marker.Origin = pc.transform.position;
            var explosion = pc.GetComponentInChildren<ProjectileExplosion>(true);
            if (explosion)
            {
                bool full = p.FullProjectile && (sourcePrefab ? sourcePrefab == p.FullProjectile : p.FullProjectile.GetComponent<ProjectileController>() && pc.catalogIndex >= 0 && p.FullProjectile.GetComponent<ProjectileController>().catalogIndex == pc.catalogIndex);
                // The small missile has a direct-hit attack, not the charged ball's explosion.
                if (p.Index != 2 || full) explosion.blastRadius = EffectiveRadius(p);
                explosion.falloffModel = (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), C.Get(p.Skill.Area).ToString());
            }
        }
        private static void BeforeProjectileDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            var marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponentInParent<ProjectileMarker>() : null;
            if (marker?.Profile.Skill.Falloff == null) return;
            damageInfo.damage *= FalloffFactor(C.Get(marker.Profile.Skill.Falloff), Vector3.Distance(marker.Origin, damageInfo.position));
        }
        internal static float FalloffFactor(S.BulletFalloff model, float distance) => model == S.BulletFalloff.Default ? 0.5f + Mathf.Clamp01((60f - distance) / 35f) * 0.5f : model == S.BulletFalloff.Buckshot ? 0.3f + Mathf.Clamp01((40f - distance) / 28f) * 0.7f : 1f;
        private static float EffectiveRadius(Profile p) => p.Index == 2 && C.Get(p.Skill.Radius) <= 0f ? p.FullRadius : C.Get(p.Skill.Radius);
        private static BulletAttack.FalloffModel BulletFalloff(S.BulletFalloff value) => value == S.BulletFalloff.None ? BulletAttack.FalloffModel.None : value == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        internal static bool CanPay(float corruption, float health, float maximumHealth, float corruptionChange, float healingChange, bool requireCorruption, bool requireHealth)
            => (!requireCorruption || corruptionChange >= 0f || corruption >= -corruptionChange) && (!requireHealth || healingChange >= 0f || health > maximumHealth * -healingChange / 100f);
        private static bool SpecialReady(Profile p, CharacterBody body)
        {
            var controller = body.GetComponent<VoidSurvivorController>(); var health = body.healthComponent;
            if (!controller || !health) return false;
            float maximum = health.fullCombinedHealth;
            return CanPay(controller.corruptionPercentage, health.combinedHealth, maximum, C.Get(p.Corruption), C.Get(p.Healing), C.Get(p.RequireCorruption), C.Get(p.RequireHealth));
        }
        private static bool BeforeRequiredCorruption(VoidSurvivorSkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (!IsFiend(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p) || p.Index < 6) return true;
            __result = SpecialReady(p, skillSlot.characterBody); return false;
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (!IsFiend(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false;
            if (p.Index >= 6 && !SpecialReady(p, skillSlot.characterBody)) __result = false;
        }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        { __state = null; if (IsFiend(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, GenericSkill skillSlot, Payment __state, Exception __exception)
        {
            if (__state != null) __instance.stockToConsume = __state.Cost;
            if (__exception == null && IsFiend(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p)) RestockInstantSpecial(p, skillSlot);
            return __exception;
        }
        private static void RestockInstantSpecial(Profile p, GenericSkill slot)
        {
            if (p.Index != 7 || C.Get(p.Cooldown) != 0f) return;
            Write(slot, "isCooldownBlocked", false);
            slot.stock = slot.maxStock;
            slot.rechargeStopwatch = 0f;
        }
        private static void BeforeSlotTick(GenericSkill __instance, ref float deltaTime)
        {
            UpdateFloodRechargeBlock(__instance);
            if (!__instance.skillDef || !IsFiend(__instance.characterBody) || !definitions.TryGetValue(__instance.skillDef, out Profile p)) return;
            RestockInstantSpecial(p, __instance);
            BeforeSkillTick(__instance.skillDef, __instance, ref deltaTime);
        }
        private static bool HoldFloodRecharge(GenericSkill slot)
        {
            CharacterBody body = slot.characterBody;
            if (!IsFiend(body) || !body.skillLocator || slot != body.skillLocator.secondary || !profiles.TryGetValue(2, out Profile p) || !p.Skill.CooldownAfterDuration) return false;
            var controller = body.GetComponent<VoidSurvivorController>();
            foreach (EntityStateMachine machine in body.GetComponents<EntityStateMachine>())
            {
                string name = machine.state?.GetType().Name;
                if (name == "ChargeMegaBlaster" || name == "ReadyMegaBlaster" || name == "FireMegaBlasterSmall") return true;
                if (name == "FireMegaBlasterBig" && (machine.customName == FloodMachine || !controller || !controller.isCorrupted)) return true;
            }
            return false;
        }
        private static bool UpdateFloodRechargeBlock(GenericSkill slot)
        {
            bool hold = HoldFloodRecharge(slot);
            if (hold)
            {
                if (!floodRechargeBlocks.TryGetValue(slot, out _))
                    floodRechargeBlocks.Add(slot, new RechargeBlock { Previous = Read(slot, "isCooldownBlocked") is bool previous && previous });
                Write(slot, "isCooldownBlocked", true);
            }
            else if (floodRechargeBlocks.TryGetValue(slot, out RechargeBlock block))
            {
                Write(slot, "isCooldownBlocked", block.Previous);
                floodRechargeBlocks.Remove(slot);
            }
            return hold;
        }
        private static bool BeforeFloodRecharge(GenericSkill __instance) => !UpdateFloodRechargeBlock(__instance);
        private static void BeforeFloodIconUpdate(RoR2.UI.SkillIcon __instance)
        {
            GenericSkill slot = Read(__instance, "targetSkill") as GenericSkill;
            if (!slot || !slot.skillDef || !IsFiend(slot.characterBody) || slot != slot.characterBody.skillLocator.secondary ||
                !definitions.TryGetValue(slot.skillDef, out Profile p) || p.Index != 2 || slot.stock > 0 || slot.skillDef.hideCooldown ||
                (Read(slot, "isCooldownBlocked") is bool blocked && blocked) || Number(slot, "cooldownRemaining") <= 0f) return;
            Component text = Read(__instance, "cooldownText") as Component;
            // The native UI only reactivates this text when the rounded number changes.
            // Invalidate its cached number after a blocked cooldown hid the text.
            if (text && !text.gameObject.activeSelf) Write(__instance, "previousCoolDownInt", int.MinValue);
        }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (IsFiend(skillSlot.characterBody) && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforeSkillTick(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!IsFiend(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Index == 7)
            {
                if (C.Get(p.Cooldown) == -1f) { deltaTime = 0f; return; }
                Write(skillSlot, "isCooldownBlocked", false);
                if (C.Get(p.Cooldown) <= 0f) return;
            }
            if (p.Skill.CooldownAfterDuration && skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p)) deltaTime = 0f;
        }
        private static bool BeforeSpecialRechargeTick(SkillDef __instance, GenericSkill skillSlot, float deltaTime)
        {
            if (!IsFiend(skillSlot.characterBody) || !definitions.TryGetValue(__instance, out Profile p) || p.Index != 7 || C.Get(p.Cooldown) == -1f) return true;
            BeforeSkillTick(__instance, skillSlot, ref deltaTime);
            baseSkillTick(__instance, skillSlot, deltaTime);
            return false;
        }
        private static void AfterCorruptedEnter(EntityState __instance)
        {
            CharacterBody body = Body(__instance);
            if (!IsFiend(body) || !profiles.TryGetValue(7, out Profile p) || !(Read(__instance, "isAuthority") is bool authority && authority)) return;
            GenericSkill slot = body.skillLocator.special;
            if (!slot || slot.skillDef != p.Skill.Definition) return;
            Write(slot, "isCooldownBlocked", false);
            if (C.Get(p.Cooldown) == -1f) { slot.stock = slot.maxStock; slot.rechargeStopwatch = 0f; }
        }
        private static void AfterCorruptedExit(EntityState __instance)
        {
            CharacterBody body = Body(__instance);
            if (IsFiend(body) && body.skillLocator.special) Write(body.skillLocator.special, "isCooldownBlocked", false);
        }

        private static string PassiveDescription()
        {
            string text = C.Get(perSecond) > 0f ? "Build <style=cIsVoid>" + S.Number(C.Get(perSecond)) + "% Corruption</style> each second." : "Build <style=cIsVoid>Corruption</style> to transform.";
            text += " At full corruption, transform into an aggressive form with <style=cIsUtility>empowered abilities</style>. Corruption drains while transformed.";
            if (C.Get(onKill) > 0f) text += " Kills grant <style=cIsVoid>" + S.Number(C.Get(onKill)) + "% corruption</style>.";
            text += " Each void item raises minimum corruption by <style=cIsVoid>" + S.Number(C.Get(minimum)) + "%</style>.";
            if (C.Get(corruptStats["damage"]) != 0f) text += " While corrupted, gain " + S.Damage(S.Number(C.Get(corruptStats["damage"])) + "% bonus damage") + ".";
            if (C.Get(corruptStats["armor"]) != 0f) text += " While corrupted, gain <style=cIsHealing>" + S.Number(C.Get(corruptStats["armor"])) + " armour</style>.";
            return text;
        }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string agile = s.AgileText;
            if (p.Index == 0) return agile + "Fire a long-range beam for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + ", <style=cIsUtility>slowing</style> enemies by <style=cIsUtility>" + C.Get(p.Slow) + "%</style>.";
            if (p.Index == 1) return agile + "Fire a short-range beam, dealing " + S.DamageText(C.Get(s.Damage), C.Get(s.Interval), true) + ".";
            if (p.Index == 2) return agile + "Fire a plasma missile for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + ". Fully charge it to fire an explosive plasma ball for " + S.Damage(S.Number(C.Get(p.MaxDamage)) + "% damage") + ".";
            if (p.Index == 3) return agile + "Fire an explosive plasma ball for " + S.Damage(S.Number(C.Get(s.Damage)) + "% damage") + ".";
            if (p.Index == 4 || p.Index == 5) return agile + "<style=cIsUtility>Disappear</style> and cleanse all debuffs, " + (p.Index == 4 ? "arcing upwards" : "dashing forwards") + " in a void-powered burst.";
            string corruption = C.Get(p.Corruption) < 0f ? "Consume <style=cIsVoid>" + S.Number(-C.Get(p.Corruption)) + "% corruption</style>" : "Gain <style=cIsVoid>" + S.Number(C.Get(p.Corruption)) + "% corruption</style>";
            string healing = C.Get(p.Healing) < 0f ? "sacrifice <style=cIsHealth>" + S.Number(-C.Get(p.Healing)) + "% HP</style>" : "heal for <style=cIsHealing>" + S.Number(C.Get(p.Healing)) + "% of your maximum health</style>";
            return agile + corruption + " and " + healing + "." + (p.Index == 7 && C.Get(p.Cooldown) == -1f ? " Charges are restored when you become corrupted again." : "");
        }
        private static string Extra(Profile p)
        {
            string text = "";
            if (p.Range != null) text += "\nRange: " + S.Utility(S.Number(C.Get(p.Range)) + "m");
            if (p.Skill.ProjectileSpeed != null) text += "\nProjectile Speed: " + S.Utility(S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) text += (p.Index == 2 ? "\nFull Charge Radius: " : "\nRadius: ") + S.Utility(S.Number(EffectiveRadius(p)) + "m");
            if (p.Charge != null) text += "\nCharge Time: " + S.Utility(S.Number(C.Get(p.Charge)) + "s");
            if (p.Movement != null) text += "\nMovement Speed Scaling: " + S.Utility(S.Number(C.Get(p.Movement)) + "%");
            if (p.ProtectFall != null && C.Get(p.ProtectFall)) text += "\nFall Damage Immunity: " + S.Utility("First landing within " + S.Number(C.Get(p.FallTime)) + "s");
            return text;
        }
    }
}
