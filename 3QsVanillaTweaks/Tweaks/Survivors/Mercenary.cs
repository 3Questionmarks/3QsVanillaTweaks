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
    internal static class Mercenary
    {
        private const string Root = "Survivors - 07 Mercenary", StatsRoot = "Survivor-Stats - 07 Mercenary";
        private const short KillMessageId = 186;
        private static readonly string[] Names = { "Laser Sword", "Whirlwind", "Rising Thunder", "Blinding Assault", "Focused Assault", "Eviscerate", "Slicing Winds" };
        private static readonly string[] Activations = { "Weapon.GroundLight2", "WhirlwindEntry", "Uppercut", "PrepAssaulter2", "FocusedAssaultPrep", "EvisDash", "Weapon.ThrowEvisProjectile" };
        private static readonly string[] Attacks = { "Weapon.GroundLight2", "WhirlwindGround", "Uppercut", "Assaulter2", "FocusedAssaultDash", "Evis", "Weapon.ThrowEvisProjectile" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal ConfigEntry<bool> ScaleDistance;
            internal ConfigEntry<float> DistanceScale;
            internal ConfigEntry<bool> ReduceMovementScaling;
            internal ConfigEntry<float> MovementScaling;
            internal float NativeFinisherRatio = 1f;
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly List<Profile> allProfiles = new List<Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<string> patched = new HashSet<string>();
        private static ConfigEntry<bool> enabled, statsEnabled, passiveEnabled, exposeEnabled, killsResetJump, recoveryScales, slayer, resetOnKill;
        private static ConfigEntry<int> extraJumps, extraDashes, maxKillResets, maxWinds;
        private static ConfigEntry<float> exposeReduction, exposeDamage, delayedDamage, delayedProc, windDuration;
        private static S.Stats stats;
        private static CharacterBody prefabBody;
        private static int nativeJumpCount;
        private static Harmony harmony;
        private static Sprite[] dashIcons, focusIcons;
        private static NetworkClient registeredClient;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static FocusCast activeFocusHit;
        [ThreadStatic] private static DelayedHitOrb activeOrb;
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class JumpBudget { internal int Spent; }
        private sealed class DashBudget { internal int Paid = 1, Refund = -1; }
        private sealed class OrbScope { internal FocusCast Previous; internal DelayedHitOrb Orb; }
        private sealed class FocusTracker { internal int Resets; internal bool RefundPending; internal FocusCast Current; }
        private sealed class FocusCast { internal FocusTracker Tracker; internal GenericSkill Slot; internal bool Granted; }
        private sealed class ProjectileMarker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; }
        private sealed class RemoveWind : MonoBehaviour
        { private void Start() { if (NetworkServer.active) NetworkServer.Destroy(gameObject); } }
        private static readonly ConditionalWeakTable<CharacterBody, JumpBudget> jumps = new ConditionalWeakTable<CharacterBody, JumpBudget>();
        private static readonly ConditionalWeakTable<GenericSkill, DashBudget> dashBudgets = new ConditionalWeakTable<GenericSkill, DashBudget>();
        private static readonly ConditionalWeakTable<GenericSkill, FocusTracker> focusTrackers = new ConditionalWeakTable<GenericSkill, FocusTracker>();
        private static readonly ConditionalWeakTable<DamageInfo, FocusCast> focusDamage = new ConditionalWeakTable<DamageInfo, FocusCast>();
        private static readonly ConditionalWeakTable<Orb, FocusCast> focusOrbs = new ConditionalWeakTable<Orb, FocusCast>();
        private static readonly ConditionalWeakTable<GameObject, List<GameObject>> windInstances = new ConditionalWeakTable<GameObject, List<GameObject>>();
        private static Type State(string suffix) => typeof(EntityState).Assembly.GetType("EntityStates.Merc." + suffix);
        private static object Sample(string suffix) => EntityStateCatalog.InstantiateState(State(suffix));
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static object Motor(CharacterBody body) => body ? (object)body.characterMotor : null;
        private static bool IsMercenary(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static bool Authority(EntityState state) => S.Read(state, "isAuthority") is bool value && value;
        private static Profile ForState(EntityState state)
        {
            Type type = state?.GetType();
            if (type == null || type.Namespace != "EntityStates.Merc" && type.Namespace != "EntityStates.Merc.Weapon") return null;
            int index = type.Name == "WhirlwindAir" || type.Name == "WhirlwindEntry" ? 1 : Array.FindIndex(Attacks, n => n.Substring(n.LastIndexOf('.') + 1) == type.Name);
            if (type.Name == "PrepAssaulter2") index = 3;
            if (type.Name == "FocusedAssaultPrep") index = 4;
            if (type.Name == "EvisDash") index = 5;
            return profiles.TryGetValue(index, out Profile p) ? p : null;
        }
        private static Profile Selected(CharacterBody body, SkillSlot slot)
        {
            if (!IsMercenary(body) || !body.skillLocator) return null;
            GenericSkill skill = slot == SkillSlot.Primary ? body.skillLocator.primary : slot == SkillSlot.Secondary ? body.skillLocator.secondary : slot == SkillSlot.Utility ? body.skillLocator.utility : body.skillLocator.special;
            return skill && skill.skillDef && definitions.TryGetValue(skill.skillDef, out Profile p) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Mercenary changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Mercenary stat changes. Restart to apply. Vanilla: False.");
            passiveEnabled = C.BindStartupBool(Root + " - Cybernetic Enhancements", "Changes Enabled", true, "Enable Cybernetic Enhancements changes. Restart to apply. Vanilla: False.");
            exposeEnabled = C.BindStartupBool(Root + " - Expose", "Changes Enabled", true, "Enable Expose changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (enabled.Value) RoR2Application.onLoad += Load;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (!patched.Add(target.MethodHandle.Value + ":" + prefix + ":" + postfix + ":" + finalizer)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Mercenary), prefix), postfix == null ? null : new HarmonyMethod(typeof(Mercenary), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Mercenary), finalizer));
        }
        private static void HookState(Type type)
        {
            foreach (string method in new[] { "OnEnter", "FixedUpdate", "OnExit" })
            {
                MethodInfo target = AccessTools.Method(type, method);
                if (target == null || target.DeclaringType == typeof(EntityState) || target.DeclaringType.FullName == "EntityStates.BaseState") continue;
                Patch(type, method, nameof(BeforeState), method == "OnEnter" ? nameof(AfterEnter) : method == "FixedUpdate" ? nameof(AfterTick) : null, nameof(EndScope));
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Merc/MercBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                nativeJumpCount = prefabBody.baseJumpCount;
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Mercenary");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefabBody);
                if (passiveEnabled.Value)
                {
                    string section = Root + " - Cybernetic Enhancements";
                    extraJumps = S.Int(section, "Number Of Extra Jumps", 1, Math.Max(0, nativeJumpCount - 1), "Number of extra jumps granted by Cybernetic Enhancements.");
                    killsResetJump = S.Bool(section, "Kills Reset Extra Jump", true, false, "Kills restore the extra jumps granted by Cybernetic Enhancements.");
                    Patch(typeof(GenericCharacterMain), "TryPerformJump", nameof(BeforeJump), nameof(AfterJump));
                    Patch(typeof(CharacterMotor), "OnLanded", postfix: nameof(AfterLanded));
                }
                if (stats != null || passiveEnabled.Value) Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                if (exposeEnabled.Value)
                {
                    string section = Root + " - Expose";
                    exposeReduction = S.Float(section, "Skill Cooldown Reduction On Hit", 1f, 1f, "Seconds removed from all skill cooldowns when consuming Expose.");
                    exposeDamage = S.Float(section, "Damage Bonus (%)", 350f, 350f, "Extra damage when consuming Expose, as a percentage of base damage.");
                    harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(Mercenary), nameof(ExposeIL)));
                }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(Attacks[i]);
                    SkillDef[] matches = defs.Where(d => d.activationState.stateType == State(Activations[i])).ToArray();
                    if (type == null || matches.Length == 0) { ThreeQsVanillaTweaks.Log.Warning("Mercenary skill unavailable: " + Names[i]); continue; }
                    foreach (SkillDef def in matches)
                    {
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; if (!profiles.ContainsKey(i)) profiles[i] = p;
                        definitions[def] = p; allProfiles.Add(p);
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                    }
                    HookState(type);
                    if (Activations[i] != Attacks[i]) HookState(State(Activations[i]));
                    if (i == 1) HookState(State("WhirlwindAir"));
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "GetCurrentIcon", postfix: nameof(AfterIcon));
                    Patch(typeof(SkillDef), "OnAssigned", postfix: nameof(AfterAssigned));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillUpdate));
                    Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage));
                }
                if (profiles.ContainsKey(3))
                {
                    Type dash = typeof(SkillDef).Assembly.GetType("RoR2.Skills.MercDashSkillDef");
                    dashIcons = S.Read(profiles[3].Skill.Definition, "icons") as Sprite[];
                    Patch(dash, "AddHit", nameof(BeforeDashHit));
                    Patch(dash, "OnFixedUpdate", postfix: nameof(AfterDashUpdate));
                    Patch(dash, "OnAssigned", postfix: nameof(AfterAssigned));
                    Patch(dash, "GetCurrentIcon", postfix: nameof(AfterIcon));
                }
                if (profiles.ContainsKey(4))
                {
                    Sprite icon = profiles[4].Skill.Definition.icon;
                    focusIcons = new[] { icon, TintIcon(icon, 0.14f), TintIcon(icon, 0f) };
                    Patch(State("FocusedAssaultDash"), "HandleHit", nameof(BeforeFocusedHit), finalizer: nameof(AfterFocusedHit));
                    Patch(typeof(OrbManager), "AddOrb", nameof(BeforeOrb));
                    Patch(typeof(DelayedHitOrb), "OnArrival", nameof(BeforeOrbArrival), finalizer: nameof(AfterOrbArrival));
                }
                if (profiles.ContainsKey(6))
                {
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                }
                string[] movementStates = { "WhirlwindBase", "Uppercut", "Assaulter2", "FocusedAssaultDash", "EvisDash" };
                for (int i = 0; i < movementStates.Length; i++)
                    if (profiles.ContainsKey(i + 1))
                    {
                        string method = i == 2 || i == 3 ? "get_dashVelocity" : "FixedUpdate";
                        harmony.Patch(AccessTools.Method(State(movementStates[i]), method), transpiler: new HarmonyMethod(typeof(Mercenary), nameof(MovementSpeedIL)));
                    }
                if (passiveEnabled.Value || profiles.ContainsKey(4))
                {
                    Patch(typeof(GlobalEventManager), "OnCharacterDeath", postfix: nameof(OnKill));
                    RoR2Application.onUpdate += RegisterClient;
                }
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Mercenary changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(changedAgile: i > 0 ? true : (bool?)null, damage: false);
            float damage = S.Value(sample, i == 1 || i == 2 ? "baseDamageCoefficient" : "damageCoefficient", 1f) * 100f;
            float proc = S.Value(sample, "procCoefficient", 1f);
            if (i == 6)
            {
                p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
                if (!p.Projectile) throw new MissingMemberException("Slicing Winds projectile unavailable.");
                ProjectileOverlapAttack overlap = p.Projectile.GetComponent<ProjectileOverlapAttack>();
                proc = p.Projectile.GetComponent<ProjectileController>().procCoefficient * (overlap ? overlap.overlapProcCoefficient : 1f);
                damage *= overlap ? overlap.damageCoefficient : 1f;
                s.BindProjectileSpeed(p.Projectile);
                if (overlap && overlap.resetInterval > 0f) s.Interval = S.Float(s.Section, "Damage Interval", overlap.resetInterval, overlap.resetInterval, "Seconds between damage ticks.", 0.001f);
                ProjectileSimple mover = p.Projectile.GetComponent<ProjectileSimple>();
                if (mover)
                {
                    windDuration = S.Float(s.Section, "AOE Duration", mover.lifetime, mover.lifetime, "How long each wind lasts, in seconds. -1 lasts until its normal end condition.", -1f);
                    maxWinds = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum active winds per player. Recasting at this limit removes the oldest wind. -1 is unlimited.", -1);
                }
            }
            s.BindDamage(damage, null, proc, i == 5 || i == 6 && s.Interval != null);
            if (i >= 1 && i <= 5)
            {
                float nativeDuration = S.Value(sample, i == 5 ? "duration" : "baseDuration", 1f);
                s.Duration = S.Float(s.Section, "Duration", i == 5 ? 2f : nativeDuration, nativeDuration, "Duration in seconds.", 0.001f);
                p.ReduceMovementScaling = S.Bool(s.Section, "Reduce Movement Speed Scaling", true, false, "Reduce how strongly movement speed affects travel distance.");
                p.MovementScaling = C.Bind(s.Section, "Movement Speed Scaling (%)", 0f, new ConfigDescription("How strongly movement speed affects travel distance while reduced scaling is enabled. 0 ignores movement speed changes; 100 uses normal scaling. Vanilla: 100.", new AcceptableValueRange<float>(0f, 100f)));
            }
            if (i == 0)
            {
                p.NativeFinisherRatio = S.Value(sample, "comboFinisherDamageCoefficient", damage / 100f) / Math.Max(0.001f, damage / 100f);
                recoveryScales = C.BindBool(s.Section, "Recovery Window Affected By Attack Speed", false, "Allow attack speed to shorten Laser Sword's recovery window. Disabling this keeps its timing consistent. Vanilla: True.", true);
            }
            if (i == 1 || i == 2)
            {
                p.ScaleDistance = S.Bool(s.Section, "Increase Distance Travelled With Attack Speed", true, false, "Scale movement speed with attack speed to compensate for faster animations.");
                p.DistanceScale = S.Float(s.Section, "Distance Attack Speed Modifier (%)", 100f, 0f, "How strongly attack speed increases movement during the skill. 100 fully compensates for the shorter animation.");
            }
            if (i == 3)
                extraDashes = S.Int(s.Section, "Number Of Times You Can Dash Again", -1, Math.Max(0, (int)S.Value(s.Definition, "maxDashes", 3f) - 1), "Additional dashes granted by hitting an enemy. -1 is unlimited.", -1);
            if (i == 4)
            {
                slayer = S.Bool(s.Section, "Slayer", true, false, "Deal more damage to enemies at low health.");
                resetOnKill = S.Bool(s.Section, "Reset On Kill", true, false, "Kills with Focused Assault reset its cooldown. Each use can grant one reset.");
                maxKillResets = S.Int(s.Section, "Max Resets On Kill", -1, 0, "Maximum consecutive kill resets before Focused Assault must recharge. -1 is unlimited.", -1);
                delayedDamage = S.Float(s.Section, "Delayed Damage (%)", S.Value(sample, "delayedDamageCoefficient") * 100f, S.Value(sample, "delayedDamageCoefficient") * 100f, "Delayed strike damage as a percentage of base damage.");
                delayedProc = S.Float(s.Section, "Delayed Proc Coefficient", S.Value(sample, "delayedProcCoefficient", 1f), S.Value(sample, "delayedProcCoefficient", 1f), "Proc coefficient of the delayed strike.");
            }
            if (i == 5)
            {
                float frequency = Math.Max(0.001f, S.Value(sample, "damageFrequency", 7f));
                s.Interval = S.Float(s.Section, "Damage Interval", 1f / frequency, 1f / frequency, "Seconds between strikes before attack speed is applied.", 0.001f);
                s.Radius = S.Float(s.Section, "AOE Radius", S.Value(sample, "maxRadius"), S.Value(sample, "maxRadius"), "Maximum distance at which Eviscerate finds an enemy, in metres.");
            }
        }
        private static void ApplyBody(CharacterBody body)
        {
            stats?.Apply(body);
            if (passiveEnabled.Value)
            {
                int baseline = stats != null ? body.baseJumpCount : nativeJumpCount;
                body.baseJumpCount = Math.Max(1, baseline + C.Get(extraJumps) - Math.Max(0, nativeJumpCount - 1));
                AccessTools.Method(typeof(CharacterBody), "MarkAllStatsDirty")?.Invoke(body, null);
            }
        }
        private static void Apply()
        {
            if (prefabBody) ApplyBody(prefabBody);
            foreach (CharacterBody body in UnityEngine.Object.FindObjectsOfType<CharacterBody>()) if (IsMercenary(body) && body != prefabBody) ApplyBody(body);
            foreach (Profile p in allProfiles)
            {
                p.Skill.ApplyDefinition();
                if (p.Index == 0 || p.Index == 4 || p.Index == 6) p.Skill.SetKeyword("KEYWORD_EXPOSE", true);
                if (p.Index == 3) p.Skill.SetKeyword("KEYWORD_STUNNING", true);
                if (p.Index == 4) p.Skill.SetKeyword("KEYWORD_SLAYER", C.Get(slayer));
                S.SetDescription(p.Skill, "3QS_MERCENARY_" + p.Index + "_DESCRIPTION", () => Description(p));
            }
            if (passiveEnabled.Value)
            {
                object passive = S.Read(prefabBody.GetComponent<SkillLocator>(), "passiveSkill");
                string token = S.Read(passive, "skillDescriptionToken") as string;
                Func<string> description = () => "The Mercenary can " + S.Utility("jump " + C.Get(extraJumps) + " additional " + (C.Get(extraJumps) == 1 ? "time" : "times")) + "." + (C.Get(killsResetJump) ? " Kills " + S.Utility("restore these extra jumps") + "." : "");
                S.SetText(token ?? "MERC_PASSIVE_DESCRIPTION", description);
            }
            if (exposeEnabled.Value)
            {
                Func<string> text = () => "<style=cKeywordName>Expose</style><style=cSub>Hitting an Exposed enemy reduces all skill cooldowns by " + S.Utility(S.Number(C.Get(exposeReduction)) + "s") + " and deals an additional " + S.Damage(S.Number(C.Get(exposeDamage)) + "% damage") + ".</style>";
                S.SetText("KEYWORD_EXPOSE", text);
            }
        }
        private static void AfterBodyAwake(CharacterBody __instance) { if (IsMercenary(__instance)) ApplyBody(__instance); }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string damage = S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), s.Periodic); string text;
            switch (p.Index)
            {
                case 0: text = "Slice in front for " + damage + ". Every 3rd hit strikes in a greater area and " + S.Utility("Exposes") + " enemies."; break;
                case 1: text = "Quickly slice horizontally twice, dealing " + damage + " each."; break;
                case 2: text = "Unleash a rising slash for " + damage + " that sends you airborne."; break;
                case 3: text = S.Utility("Stunning. ") + "Dash forward, dealing " + damage + "." + (C.Get(extraDashes) == 0 ? "" : " Hitting an enemy lets you dash again" + (C.Get(extraDashes) < 0 ? " without limit." : " up to " + S.Utility(C.Get(extraDashes) + " additional " + (C.Get(extraDashes) == 1 ? "time" : "times")) + ".")); break;
                case 4: text = (C.Get(slayer) ? S.Damage("Slayer. ") : "") + "Dash forward, striking enemies for " + damage + ". After a short delay, strike again for " + S.Damage(S.Number(C.Get(delayedDamage)) + "% damage") + " and " + S.Utility("Expose") + " enemies." + (C.Get(resetOnKill) && C.Get(maxKillResets) != 0 ? " Kills " + S.Utility("reset the cooldown") + (C.Get(maxKillResets) < 0 ? "." : " up to " + S.Utility(C.Get(maxKillResets) + " consecutive " + (C.Get(maxKillResets) == 1 ? "time" : "times")) + ".") : ""); break;
                case 5: text = "Target the nearest enemy, attacking for " + damage + " for " + S.Utility(S.Number(C.Get(s.Duration)) + " seconds") + ". " + S.Utility("You cannot be hit for the duration."); break;
                default: text = "Fire a wind of blades that attacks enemies for " + damage + ". The final hit " + S.Utility("Exposes") + " enemies."; break;
            }
            return s.AgileText + text;
        }
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (key, value) => b.Append("\n").Append(key).Append(": ").Append(S.Utility(value));
            S.Skill s = p.Skill;
            if (s.Interval != null) add("Damage Interval", S.Number(C.Get(s.Interval)) + "s");
            if (s.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (s.Radius != null) add("AOE Radius", S.Number(C.Get(s.Radius)) + "m");
            if (p.Index == 4) add("Delayed Proc Coefficient", S.Number(C.Get(delayedProc)));
            if (p.Index == 6 && windDuration != null) add("AOE Duration", C.Get(windDuration) < 0f ? "Until its end condition" : S.Number(C.Get(windDuration)) + "s");
            return b.ToString();
        }
        private static void Set(Scope scope, object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name); if (field == null) return;
            if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
            S.Write(instance, name, value);
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState }; activeState = null;
            Profile p = ForState(__instance); if (p == null || !IsMercenary(Body(__instance))) return;
            activeState = __instance; S.Skill s = p.Skill; string name = __instance.GetType().Name;
            bool attack = name != "PrepAssaulter2" && name != "FocusedAssaultPrep" && name != "WhirlwindEntry" && name != "EvisDash";
            if (attack)
            {
                Set(__state, __instance, p.Index == 1 || p.Index == 2 ? "baseDamageCoefficient" : "damageCoefficient", C.Get(s.Damage) / 100f);
                Set(__state, __instance, "procCoefficient", C.Get(s.Proc));
                if (s.Duration != null) Set(__state, __instance, p.Index == 5 ? "duration" : "baseDuration", C.Get(s.Duration));
            }
            if (p.Index == 0)
            {
                Set(__state, __instance, "comboFinisherDamageCoefficient", C.Get(s.Damage) / 100f * p.NativeFinisherRatio);
                if (Authority(__instance) && !C.Get(recoveryScales)) S.Write(__instance, "ignoreAttackSpeed", true);
            }
            if (p.Index == 4 && name == "FocusedAssaultDash")
            { Set(__state, __instance, "delayedDamageCoefficient", C.Get(delayedDamage) / 100f); Set(__state, __instance, "delayedProcCoefficient", C.Get(delayedProc)); }
            if (p.Index == 5 && name == "Evis")
            { Set(__state, __instance, "damageFrequency", 1f / C.Get(s.Interval)); Set(__state, __instance, "maxRadius", C.Get(s.Radius)); }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value);
            activeState = __state.Previous; return __exception;
        }
        private static void AfterEnter(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance); if (p == null || !IsMercenary(body)) return;
            if (p.Index == 0 && Authority(__instance) && !C.Get(recoveryScales))
                S.Write(__instance, "durationBeforeInterruptable", S.Value(__instance, (int)S.Value(__instance, "step") == 2 ? "comboFinisherBaseDurationBeforeInterruptable" : "baseDurationBeforeInterruptable"));
            if (p.Index == 4 && __instance.GetType().Name == "FocusedAssaultPrep") BeginFocusCast(body);
        }
        private static float TravelMultiplier(float attackSpeed, float percentage) => Math.Max(0f, 1f + (attackSpeed - 1f) * percentage / 100f);
        private static float MovementSpeed(float original, EntityState state)
        {
            Profile p = ForState(state); CharacterBody body = Body(state);
            if (p?.ReduceMovementScaling == null || !C.Get(p.ReduceMovementScaling) || !IsMercenary(body)) return original;
            float baseline = body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
            if (body.isSprinting) baseline *= body.sprintingSpeedMultiplier;
            return Math.Max(0f, baseline + (original - baseline) * C.Get(p.MovementScaling) / 100f);
        }
        private static IEnumerable<CodeInstruction> MovementSpeedIL(IEnumerable<CodeInstruction> instructions)
        {
            bool found = false;
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field.Name == "moveSpeedStat" ||
                    (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo method && method.Name == "get_moveSpeedStat")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Mercenary), nameof(MovementSpeed)));
                    found = true;
                }
            }
            if (!found) ThreeQsVanillaTweaks.Log.Warning("Mercenary movement scaling: native movement speed could not be found.");
        }
        private static void AfterTick(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p?.ScaleDistance == null || !C.Get(p.ScaleDistance) || !IsMercenary(body) || !Authority(__instance) || S.Read(__instance, "isInHitPause") is bool paused && paused) return;
            if (__instance.GetType().Name == "WhirlwindEntry") return;
            object motor = Motor(body);
            if (S.Read(motor, "velocity") is Vector3 velocity)
            {
                float multiplier = TravelMultiplier(body.attackSpeed, C.Get(p.DistanceScale));
                velocity.x *= multiplier; velocity.z *= multiplier; if (p.Index == 2) velocity.y *= multiplier;
                S.Write(motor, "velocity", velocity);
            }
        }
        private static void BeforeOverlap(OverlapAttack __instance)
        {
            CharacterBody body = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null; if (!IsMercenary(body)) return;
            Profile p = ForState(activeState);
            if (Body(activeState) != body) p = null;
            if (p == null)
            {
                switch (__instance.damageType.damageSource)
                {
                    case DamageSource.Primary: p = Selected(body, SkillSlot.Primary); break;
                    case DamageSource.Secondary: p = Selected(body, SkillSlot.Secondary); break;
                    case DamageSource.Utility: p = Selected(body, SkillSlot.Utility); break;
                }
            }
            if (p == null) return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Index == 4 && C.Get(slayer)) __instance.damageType.damageType |= DamageType.BonusToLowHealth;
        }
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            CharacterBody body = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (!IsMercenary(body) || damageInfo.dotIndex != DotController.DotIndex.None) return;
            FocusCast cast = activeOrb != null && activeOrb.attacker == damageInfo.attacker && activeOrb.target && activeOrb.target.healthComponent == __instance && damageInfo.inflictor == null && damageInfo.damageType.damageSource == DamageSource.Utility && (damageInfo.damageType.damageType & DamageType.ApplyMercExpose) != 0 ? activeFocusHit : null;
            Profile p = ForState(activeState);
            if (Body(activeState) != body) p = null;
            if (damageInfo.damageType.damageSource == DamageSource.Utility && damageInfo.inflictor == damageInfo.attacker && Selected(body, SkillSlot.Utility)?.Index == 4)
                cast = CurrentFocusCast(body);
            if (p == null && damageInfo.inflictor == damageInfo.attacker)
            {
                switch (damageInfo.damageType.damageSource)
                {
                    case DamageSource.Primary: p = Selected(body, SkillSlot.Primary); break;
                    case DamageSource.Secondary: p = Selected(body, SkillSlot.Secondary); break;
                    case DamageSource.Utility: p = Selected(body, SkillSlot.Utility); break;
                }
            }
            if (cast != null && cast.Slot && cast.Slot.characterBody == body)
            {
                focusDamage.Remove(damageInfo); focusDamage.Add(damageInfo, cast);
                if (C.Get(slayer)) damageInfo.damageType.damageType |= DamageType.BonusToLowHealth;
                p = profiles[4];
            }
            ProjectileMarker marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponent<ProjectileMarker>() : null;
            if (marker) p = marker.Profile;
            if (p != null && p.Skill.Falloff != null)
            {
                float distance = Vector3.Distance(marker ? marker.Origin : body.corePosition, damageInfo.position);
                if (C.Get(p.Skill.Falloff) == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f;
                else if (C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.25f + Mathf.Clamp01(Mathf.InverseLerp(25f, 7f, distance)) * 0.75f;
            }
        }
        private static float ExposeDamage(float original) => C.Get(exposeDamage) / 100f;
        private static float ExposeCooldown(float original) => C.Get(exposeReduction);
        private static IEnumerable<CodeInstruction> ExposeIL(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList(); int exposeAt = -1000; bool damagePatched = false, cooldownPatched = false;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo field && field.Name == "MercExpose") exposeAt = i;
                yield return code;
                if (!damagePatched && i - exposeAt < 60 && code.opcode == OpCodes.Ldc_R4 && code.operand is float value && value == 3.5f)
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Mercenary), nameof(ExposeDamage))); damagePatched = true; }
                if (i + 1 < codes.Count && code.opcode == OpCodes.Ldc_R4 && codes[i + 1].operand is MethodInfo method && method.Name == "DeductCooldownFromAllSkillsServer")
                { yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Mercenary), nameof(ExposeCooldown))); cooldownPatched = true; }
            }
            if (!damagePatched || !cooldownPatched) ThreeQsVanillaTweaks.Log.Warning("Mercenary Expose: could not find both native damage and cooldown values.");
        }
        private static void BeforeJump(EntityState __instance, out int __state) { __state = (int)S.Value(Motor(Body(__instance)), "jumpCount"); }
        private static void AfterJump(EntityState __instance, bool __result, int __state)
        {
            CharacterBody body = Body(__instance); if (!__result || !IsMercenary(body)) return;
            JumpBudget budget = jumps.GetValue(body, _ => new JumpBudget()); if (__state == 0) budget.Spent = 0;
            else if (budget.Spent < C.Get(extraJumps)) budget.Spent++;
        }
        private static void AfterLanded(object __instance)
        { if (S.Read(__instance, "body") is CharacterBody body && IsMercenary(body)) jumps.GetValue(body, _ => new JumpBudget()).Spent = 0; }
        private static void RefreshJumps(CharacterBody body)
        {
            if (!passiveEnabled.Value || !C.Get(killsResetJump)) return;
            JumpBudget budget = jumps.GetValue(body, _ => new JumpBudget()); object motor = Motor(body);
            int used = (int)S.Value(motor, "jumpCount");
            if (used > 0) S.Write(motor, "jumpCount", Math.Max(1, used - Math.Min(budget.Spent, C.Get(extraJumps))));
            budget.Spent = 0;
        }
        private static bool BeforeDashHit(SkillDef __instance, GenericSkill skillSlot)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || p.Index != 3) return true;
            object data = skillSlot.skillInstanceData; int index = (int)S.Value(data, "currentDashIndex"); int maximum = C.Get(extraDashes);
            if (maximum < 0 || index < maximum)
            {
                S.Write(data, "currentDashIndex", maximum < 0 ? 1 : index + 1);
                DashBudget budget = dashBudgets.GetValue(skillSlot, _ => new DashBudget()); budget.Refund = budget.Paid;
                skillSlot.stock += budget.Refund; S.Write(data, "hasExtraStock", true);
            }
            else S.Write(data, "currentDashIndex", 0);
            return false;
        }
        private static void AfterDashUpdate(SkillDef __instance, GenericSkill skillSlot)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || p.Index != 3) return;
            object data = skillSlot.skillInstanceData;
            if (S.Value(data, "timeoutTimer") <= 0f && S.Value(data, "currentDashIndex") == 0f && S.Read(data, "hasExtraStock") is bool extra && !extra)
            {
                DashBudget budget = dashBudgets.GetValue(skillSlot, _ => new DashBudget());
                if (budget.Refund >= 0) skillSlot.stock = Math.Max(0, skillSlot.stock - (budget.Refund - 1));
                budget.Refund = -1;
            }
        }
        private static int IconIndex(int remaining) => remaining == 1 ? 2 : remaining == 2 ? 1 : 0;
        private static void AfterIcon(SkillDef __instance, GenericSkill skillSlot, ref Sprite __result)
        {
            if (!definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Index == 3 && dashIcons != null && dashIcons.Length >= 3)
            {
                int used = (int)S.Value(skillSlot.skillInstanceData, "currentDashIndex");
                int remaining = C.Get(extraDashes) < 0 ? -1 : Math.Max(1, C.Get(extraDashes) + 1 - used);
                __result = dashIcons[IconIndex(remaining)];
            }
            if (p.Index == 4 && focusIcons != null)
            {
                int maximum = C.Get(maxKillResets); FocusTracker tracker = focusTrackers.GetValue(skillSlot, _ => new FocusTracker());
                int used = skillSlot.stock >= skillSlot.maxStock && !tracker.RefundPending ? 0 : tracker.Resets;
                __result = focusIcons[IconIndex(!C.Get(resetOnKill) || maximum < 0 ? -1 : Math.Max(1, maximum + 1 - used))];
            }
        }
        private static Sprite TintIcon(Sprite original, float hue)
        {
            if (!original || Application.isBatchMode) return original;
            RenderTexture previous = RenderTexture.active; RenderTexture target = null;
            try
            {
                target = RenderTexture.GetTemporary(original.texture.width, original.texture.height, 0);
                Graphics.Blit(original.texture, target); RenderTexture.active = target;
                int width = (int)original.rect.width, height = (int)original.rect.height;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false); texture.ReadPixels(original.rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++) { Color.RGBToHSV(pixels[i], out float h, out float s, out float v); Color tint = Color.HSVToRGB(hue, Math.Max(0.35f, s), v); tint.a = pixels[i].a; pixels[i] = tint; }
                texture.SetPixels(pixels); texture.Apply();
                return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Mercenary icon colour could not be created: " + e.Message); return original; }
            finally { RenderTexture.active = previous; if (target) RenderTexture.ReleaseTemporary(target); }
        }
        private static FocusCast BeginFocusCast(CharacterBody body)
        {
            GenericSkill slot = body.skillLocator.utility;
            FocusTracker tracker = focusTrackers.GetValue(slot, _ => new FocusTracker());
            if (!tracker.RefundPending) tracker.Resets = 0;
            tracker.RefundPending = false; tracker.Current = new FocusCast { Tracker = tracker, Slot = slot }; return tracker.Current;
        }
        private static FocusCast CurrentFocusCast(CharacterBody body)
        {
            if (Selected(body, SkillSlot.Utility)?.Index != 4) return null;
            FocusTracker tracker = focusTrackers.GetValue(body.skillLocator.utility, _ => new FocusTracker());
            return tracker.Current ?? BeginFocusCast(body);
        }
        private static void BeforeFocusedHit(GameObject attackerObject, out FocusCast __state)
        { __state = activeFocusHit; activeFocusHit = attackerObject ? CurrentFocusCast(attackerObject.GetComponent<CharacterBody>()) : null; }
        private static Exception AfterFocusedHit(FocusCast __state, Exception __exception) { activeFocusHit = __state; return __exception; }
        private static void BeforeOrb(Orb orb)
        {
            if (activeFocusHit == null || !(orb is DelayedHitOrb hit)) return;
            if (C.Get(slayer)) hit.damageType.damageType |= DamageType.BonusToLowHealth;
            focusOrbs.Remove(orb); focusOrbs.Add(orb, activeFocusHit);
        }
        private static void BeforeOrbArrival(DelayedHitOrb __instance, out OrbScope __state)
        { __state = new OrbScope { Previous = activeFocusHit, Orb = activeOrb }; activeOrb = __instance; activeFocusHit = focusOrbs.TryGetValue(__instance, out FocusCast cast) ? cast : null; }
        private static Exception AfterOrbArrival(OrbScope __state, Exception __exception)
        { activeFocusHit = __state.Previous; activeOrb = __state.Orb; return __exception; }
        private static bool TryResetFocus(CharacterBody body, DamageInfo info, out int resets)
        {
            resets = 0;
            if (!profiles.ContainsKey(4) || !C.Get(resetOnKill) || !focusDamage.TryGetValue(info, out FocusCast cast) || cast.Granted || cast.Tracker.Current != cast || cast.Slot != body.skillLocator.utility || Selected(body, SkillSlot.Utility)?.Index != 4) return false;
            int maximum = C.Get(maxKillResets); FocusTracker tracker = cast.Tracker;
            if (maximum >= 0 && tracker.Resets >= maximum) return false;
            cast.Granted = true; tracker.Resets++; tracker.RefundPending = true; resets = tracker.Resets;
            cast.Slot.Reset(); return true;
        }
        private sealed class KillNotice : MessageBase
        {
            internal NetworkInstanceId BodyId; internal bool Jump, Focus; internal int Resets;
            public override void Serialize(NetworkWriter writer) { writer.Write(BodyId); writer.Write(Jump); writer.Write(Focus); writer.Write(Resets); }
            public override void Deserialize(NetworkReader reader) { BodyId = reader.ReadNetworkId(); Jump = reader.ReadBoolean(); Focus = reader.ReadBoolean(); Resets = reader.ReadInt32(); }
        }
        private static void OnKill([HarmonyArgument(0)] DamageReport report)
        {
            if (!NetworkServer.active || !IsMercenary(report.attackerBody)) return;
            CharacterBody body = report.attackerBody;
            bool refresh = passiveEnabled.Value && C.Get(killsResetJump), focus = TryResetFocus(body, report.damageInfo, out int resets);
            if (refresh) RefreshJumps(body);
            if (!refresh && !focus) return;
            NetworkIdentity identity = body.GetComponent<NetworkIdentity>();
            if (identity) NetworkServer.SendToAll(KillMessageId, new KillNotice { BodyId = identity.netId, Jump = refresh, Focus = focus, Resets = resets });
        }
        private static void RegisterClient()
        {
            if (NetworkServer.active || !NetworkClient.active) { registeredClient = null; return; }
            NetworkClient client = NetworkManager.singleton ? NetworkManager.singleton.client : null;
            if (client != null && client != registeredClient) { client.RegisterHandler(KillMessageId, ReceiveKill); registeredClient = client; }
        }
        private static void ReceiveKill(NetworkMessage message)
        {
            if (NetworkServer.active) return;
            KillNotice notice = message.ReadMessage<KillNotice>(); GameObject obj = ClientScene.FindLocalObject(notice.BodyId);
            CharacterBody body = obj ? obj.GetComponent<CharacterBody>() : null; if (!IsMercenary(body)) return;
            if (notice.Jump) RefreshJumps(body);
            if (notice.Focus && Selected(body, SkillSlot.Utility)?.Index == 4)
            {
                GenericSkill slot = body.skillLocator.utility; FocusTracker tracker = focusTrackers.GetValue(slot, _ => new FocusTracker());
                tracker.Resets = notice.Resets; tracker.RefundPending = true; if (tracker.Current != null) tracker.Current.Granted = true;
                slot.Reset();
            }
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            CharacterBody body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            if (IsMercenary(body) && profiles.TryGetValue(6, out Profile p) && fireProjectileInfo.projectilePrefab == p.Projectile)
                fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => BeforeProjectileStart(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            if (__instance.GetComponent<ProjectileMarker>() || !profiles.TryGetValue(6, out Profile p) || p.Projectile.GetComponent<ProjectileController>().catalogIndex != __instance.catalogIndex) return;
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null; if (!IsMercenary(body)) return;
            var marker = __instance.gameObject.AddComponent<ProjectileMarker>(); marker.Profile = p; marker.Origin = __instance.transform.position;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            var overlap = __instance.GetComponent<ProjectileOverlapAttack>();
            if (overlap) { overlap.damageCoefficient = 1f; overlap.overlapProcCoefficient = 1f; if (p.Skill.Interval != null) overlap.resetInterval = C.Get(p.Skill.Interval); }
            var simple = __instance.GetComponent<ProjectileSimple>();
            if (simple && windDuration != null && C.Get(windDuration) >= 0f) simple.lifetime = C.Get(windDuration);
            if (simple && windDuration != null && C.Get(windDuration) < 0f) simple.lifetime = float.PositiveInfinity;
            if (!NetworkServer.active || maxWinds == null) return;
            List<GameObject> instances = windInstances.GetValue(__instance.owner, _ => new List<GameObject>()); instances.RemoveAll(o => !o); instances.Add(__instance.gameObject);
            int maximum = C.Get(maxWinds); while (maximum >= 0 && instances.Count > maximum) { GameObject oldest = instances[0]; instances.RemoveAt(0); if (oldest && !oldest.GetComponent<RemoveWind>()) oldest.AddComponent<RemoveWind>(); }
        }
        private static void BeforeSkillUpdate(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || !p.Skill.CooldownAfterDuration) return;
            CharacterBody body = skillSlot.characterBody;
            if (IsMercenary(body) && body.GetComponents<EntityStateMachine>().Any(machine => ForState(machine.state)?.Index == p.Index)) deltaTime = 0f;
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (__result && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll) __result = skillSlot.stock >= skillSlot.maxStock; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out int? __state)
        {
            __state = null; if (!definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Index == 3)
            {
                DashBudget budget = dashBudgets.GetValue(skillSlot, _ => new DashBudget());
                budget.Paid = p.Skill.ConsumeAll ? skillSlot.stock : __instance.stockToConsume; budget.Refund = -1;
            }
            if (p.Skill.ConsumeAll) { __state = __instance.stockToConsume; __instance.stockToConsume = skillSlot.stock; }
        }
        private static Exception AfterPayment(SkillDef __instance, int? __state, Exception __exception) { if (__state.HasValue) __instance.stockToConsume = __state.Value; return __exception; }
        private static void AfterAssigned(SkillDef __instance, GenericSkill skillSlot)
        {
            if (!definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Index == 3) dashBudgets.Remove(skillSlot);
            if (p.Index == 4) focusTrackers.Remove(skillSlot);
        }
    }
}
