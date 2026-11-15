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
    internal static class Bandit
    {
        private const string Root = "Survivors - 03 Bandit";
        private const short MissMessageId = 183;
        private static readonly string[] Names = { "Burst", "Blast", "Serrated Dagger", "Serrated Shiv", "Smoke Bomb", "Lights Out", "Desperado" };
        private static readonly string[] States = { "FireShotgun2", "Bandit2FireRifle", "SlashBlade", "Bandit2FireShiv", "StealthMode", "FireSidearmResetRevolver", "FireSidearmSkullRevolver" };
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<string, Profile> skills = new Dictionary<string, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly HashSet<RuntimeMethodHandle> entered = new HashSet<RuntimeMethodHandle>();
        private static readonly ConditionalWeakTable<DamageInfo, Profile> damageProfiles = new ConditionalWeakTable<DamageInfo, Profile>();
        private static ConfigEntry<bool> enabled, statsEnabled, ignoreArmour, lunge, resetDeath, resetHit, resetStage, resetFailed, resetMiss;
        private static ConfigEntry<int> pellets, pierces, hitBoxSize, shivHitBoxSize, smokeSpeed, skullDamage;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static GameObject shivPrefab;
        private static ConfigEntry<float> lungeDistance, lungeDuration;
        private static readonly ConditionalWeakTable<object, Dash> dashes = new ConditionalWeakTable<object, Dash>();
        private static readonly ConditionalWeakTable<GenericSkill, PrimaryTiming> primaryTimings = new ConditionalWeakTable<GenericSkill, PrimaryTiming>();
        private static bool serverRegistered;
        private static PropertyInfo stateAuthority;
        private static MethodInfo addDisplacement;
        private static readonly List<SkullMemory> memories = new List<SkullMemory>();
        [ThreadStatic] private static Profile active;
        [ThreadStatic] private static CharacterBody activeBody;
        private sealed class Profile
        {
            internal S.Skill Skill;
            internal int Index;
            internal ConfigEntry<int> Hemorrhage;
            internal ConfigEntry<float> Grace, AutoFireDelay;
            internal ConfigEntry<float> MinSpread, MaxSpread, BloomSpread, MinFan, MaxFan;
            internal float NativeBloomSpread;
            internal ConfigEntry<bool> AutoFire, ScaleAutoFireDelay, Release;
            internal GameObject KillEffect;
        }
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Bandit2.Weapon." + name) ?? typeof(EntityState).Assembly.GetType("EntityStates.Bandit2." + name);
        private static bool IsBandit(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static Profile ForState(object state)
        {
            if (state == null) return null;
            string name = state.GetType().Name;
            if (name == "PrepSidearmResetRevolver") name = States[5];
            if (name == "PrepSidearmSkullRevolver") name = States[6];
            return skills.TryGetValue(name, out Profile p) ? p : null;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (method == "OnEnter" && !entered.Add(target.MethodHandle)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Bandit), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Bandit), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Bandit), finalizer));
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Bandit changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool("Survivor-Stats - 03 Bandit", "Changes Enabled", true, "Enable Bandit stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true,
                "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            ignoreArmour = S.Bool(Root + " - General", "Hemorrhage Ignores Armour", true, false, "Hemorrhage inflicted by Bandit ignores armour.");
            RoR2Application.onLoad += Load;
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Bandit2/Bandit2Body.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Bandit");
                stateAuthority = AccessTools.Property(typeof(EntityState), "isAuthority");
                if (statsEnabled.Value)
                {
                    stats = new S.Stats("Survivor-Stats - 03 Bandit", prefabBody,
                        new Dictionary<string, float> { { "baseCrit", 5f } }); stats.Apply(prefabBody);
                    Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(slot => slot.skillFamily)
                    .SelectMany(slot => slot.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < States.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(States[i]);
                    string activation = i == 4 ? "ThrowSmokebomb" : i == 5 ? "PrepSidearmResetRevolver" : i == 6 ? "PrepSidearmSkullRevolver" : States[i];
                    SkillDef def = defs.FirstOrDefault(d => d.activationState.stateType?.Name == activation);
                    if (type == null || !def) { ThreeQsVanillaTweaks.Log.Warning("Bandit skill unavailable: " + Names[i]); continue; }
                    var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                    Bind(p); p.Skill.Loaded = true; skills[States[i]] = p; definitions[def] = p;
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                    Patch(type, "OnEnter", nameof(BeforeEnter), i < 2 ? nameof(AfterPrimaryEnter) : i == 2 ? nameof(AfterDaggerEnter) : null, nameof(AfterEnter));
                }
                Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), nameof(AfterDamage));
                if (skills.Count > 0)
                {
                    Patch(typeof(GlobalEventManager), "OnHitEnemy", nameof(BeforeHit));
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), finalizer: nameof(AfterBullet), args: Type.EmptyTypes);
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                }
                if (skills.ContainsKey(States[2]))
                {
                    addDisplacement = AccessTools.Method(typeof(CharacterMotor), "AddDisplacement", new[] { typeof(Vector3) });
                    if (addDisplacement == null) throw new MissingMethodException("CharacterMotor.AddDisplacement");
                    Patch(typeof(BasicMeleeAttack), "AuthorityFixedUpdate", nameof(BeforeDaggerTick));
                    Patch(State(States[2]), "OnExit", postfix: nameof(AfterDaggerExit));
                    Patch(typeof(OverlapAttack), "Fire", nameof(BeforeOverlap), finalizer: nameof(AfterOverlap));
                }
                if (skills.ContainsKey(States[3]))
                {
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                }
                if (skills.ContainsKey(States[4]))
                    harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(Bandit), nameof(SmokeSpeedIL)));
                if (skills.ContainsKey(States[5]) || skills.ContainsKey(States[6]))
                {
                    Patch(State("BasePrepSidearmRevolverState"), "FixedUpdate", nameof(BeforePrep), finalizer: nameof(AfterPrep));
                    harmony.Patch(AccessTools.Method(State("BaseSidearmState"), "FixedUpdate"), transpiler: new HarmonyMethod(typeof(Bandit), nameof(SidearmSprintIL)));
                    EventInfo death = typeof(GlobalEventManager).GetEvent("onCharacterDeathServer") ?? typeof(GlobalEventManager).GetEvent("onCharacterDeathGlobal");
                    if (death == null) throw new MissingMemberException("Character death event unavailable.");
                    death.AddEventHandler(null, new Action<DamageReport>(OnDeath));
                }
                if (skills.ContainsKey(States[6]))
                {
                    CharacterBody.onBodyStartGlobal += OnBodyStart;
                    CharacterBody.onBodyDestroyGlobal += OnBodyDestroyed;
                    Stage.onStageStartGlobal += OnStage;
                    new GameObject("3QsBanditRuntime").AddComponent<NetworkRunner>();
                }
                C.EffectiveSettingsChanged += Apply;
                Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Bandit changes could not be loaded: " + e); }
        }
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index;
            s.Bind(i >= 5 ? 6f : (float?)null, changedAgile: i >= 2 && i <= 4 ? true : (bool?)null,
                damage: false, changedCombat: i >= 2 && i <= 4 ? false : (bool?)null);
            float nativeDamage = S.Value(s.Sample, i == 4 ? "blastAttackDamageCoefficient" : "damageCoefficient", 1f) * 100f;
            float nativeProc = S.Value(s.Sample, i == 4 ? "blastAttackProcCoefficient" : "procCoefficient", 1f);
            if (i == 3)
            {
                shivPrefab = S.Read(s.Sample, "projectilePrefab") as GameObject;
                if (shivPrefab) nativeProc = shivPrefab.GetComponent<ProjectileController>().procCoefficient;
            }
            s.BindDamage(nativeDamage, i == 1 || i == 2 ? 400f : i == 3 ? 300f : i == 5 ? 700f : i == 6 ? 600f : (float?)null, nativeProc);
            int nativeHemorrhage = i == 2 || i == 3 ? 100 : 0;
            p.Hemorrhage = C.Bind(s.Section, "Hemorrhage On Crit (%)", i == 0 ? 5 : i == 1 ? 25 : i == 4 ? 0 : 100,
                new ConfigDescription("Chance for a critical hit to inflict Hemorrhage. Vanilla: " + S.Number(nativeHemorrhage) + "%.", new AcceptableValueRange<int>(0, 100)));
            if (i < 2)
            {
                p.AutoFire = C.BindBool(s.Section, "Auto Fire (Client)", true, "Fire repeatedly while holding the button. Vanilla: False.", true);
                float nativeDelay = S.Value(s.Sample, "minimumBaseDuration", S.Value(s.Sample, "baseDuration"));
                p.AutoFireDelay = S.Float(s.Section, "Auto Fire Delay", nativeDelay, nativeDelay, "Seconds between automatic shots before attack speed is applied.");
                p.ScaleAutoFireDelay = S.Bool(s.Section, "Scale Auto Fire Delay With Attack Speed", true, true, "Reduce the automatic firing delay as attack speed increases.");
                float scale = i == 1 ? 0.5f : 1f;
                float minimum = S.Value(s.Sample, "minSpread"), maximum = S.Value(s.Sample, "maxSpread");
                p.NativeBloomSpread = prefabBody.spreadBloomCurve == null ? 0f : Math.Max(0f, prefabBody.spreadBloomCurve.Evaluate(1f));
                p.MinSpread = S.Float(s.Section, "Minimum Random Spread (Degrees)", minimum * scale, minimum, "Minimum random shot spread in degrees.");
                p.MaxSpread = S.Float(s.Section, "Maximum Random Spread (Degrees)", maximum * scale, maximum, "Maximum random shot spread in degrees, before spread bloom. Cannot be lower than the minimum.");
                p.BloomSpread = S.Float(s.Section, "Maximum Spread Bloom (Degrees)", p.NativeBloomSpread * scale, p.NativeBloomSpread, "Maximum extra spread from repeated firing, in degrees.");
                if (i == 0)
                {
                    float minFan = S.Value(s.Sample, "minFixedSpreadYaw"), maxFan = S.Value(s.Sample, "maxFixedSpreadYaw");
                    p.MinFan = S.Float(s.Section, "Minimum Pellet Spread (Degrees)", minFan, minFan, "Minimum sideways angle of the outer shotgun pellets from the crosshair, in degrees.");
                    p.MaxFan = S.Float(s.Section, "Maximum Pellet Spread (Degrees)", maxFan, maxFan, "Maximum sideways angle of the outer shotgun pellets from the crosshair, in degrees. Cannot be lower than the minimum.");
                }
            }
            if (i == 0) pellets = S.Int(s.Section, "Pellets", 5, (int)S.Value(s.Sample, "bulletCount", 5f), "Pellets fired per shot.", 1);
            if (i == 1) pierces = S.Int(s.Section, "Pierces", 2, 0, "Enemies the shot can pass through before stopping. -1 allows unlimited piercing.", -1);
            if (i == 2)
            {
                hitBoxSize = S.Int(s.Section, "Hit Box Size (%)", 150, 100, "Size of the attack's hitbox. 100 uses its normal size.", 1);
                lunge = S.Bool(s.Section, "Lunge", true, false, "Dash forward when using Serrated Dagger.");
                lungeDistance = S.Float(s.Section, "Lunge Distance", 3f, 0f, "Forward dash distance in metres.");
                lungeDuration = S.Float(s.Section, "Lunge Duration", 0.15f, 0f, "Dash duration in seconds, limited by the attack's duration.", 0.01f);
            }
            if (i == 3)
            {
                shivHitBoxSize = S.Int(s.Section, "Hit Box Size (%)", 200, 100, "Size of the projectile's hitbox. 100 uses its normal size. Does not change its appearance.", 1);
                if (shivPrefab) s.BindProjectileSpeed(shivPrefab);
            }
            if (i == 4)
            {
                smokeSpeed = S.Int(s.Section, "Speed Boost (%)", 40, 40, "Movement speed gained while invisible.");
                float duration = S.Value(s.Sample, "duration");
                s.Duration = S.Float(s.Section, "Duration", duration, duration, "Duration of invisibility in seconds.", 0.01f);
                float radius = S.Value(s.Sample, "blastAttackRadius");
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Radius of the smoke explosions in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
                Patch(State("StealthMode"), "FireSmokebomb", nameof(BeforeSmoke), finalizer: nameof(AfterSmoke));
                Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                Patch(State("StealthMode"), "OnExit", postfix: nameof(AfterStealthExit));
            }
            if (i >= 5)
            {
                p.Grace = S.Float(s.Section, "Grace Period", 0.5f, 0f, "Trigger the skill's kill reward if the target dies within this many seconds of being hit.");
                p.Release = S.Bool(s.Section, "Fire When Releasing Button", true, false, "Draw on press and fire on release. Releasing early still waits for the draw animation.");
                try { p.KillEffect = Addressables.LoadAssetAsync<GameObject>(i == 5 ? "RoR2/Base/Bandit2/Bandit2ResetEffect.prefab" : "RoR2/Base/Bandit2/Bandit2KillEffect.prefab").WaitForCompletion(); }
                catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Bandit kill effect unavailable: " + e.Message); }
            }
            if (i == 6)
            {
                skullDamage = S.Int(s.Section, "Damage Per Bandit Skull (%)", 1, 10, "Extra Desperado damage per Bandit Skull stack.");
                resetDeath = S.Bool(s.Section, "Reset On Death", false, true, "Lose Desperado stacks when you die.");
                resetHit = S.Bool(s.Section, "Reset On Hit", false, false, "Lose Desperado stacks when you take damage.");
                resetStage = S.Bool(s.Section, "Reset On New Stage", false, true, "Lose Desperado stacks on entering a new stage.");
                resetFailed = S.Bool(s.Section, "Reset On Failed Kill", false, false, "Lose Desperado stacks if a hit target survives the grace period.");
                resetMiss = S.Bool(s.Section, "Reset On Miss", false, false, "Lose Desperado stacks when a shot hits no enemies.");
            }
        }
        private static void Apply()
        {
            if (stats != null)
            {
                stats.Apply(prefabBody);
                foreach (CharacterBody body in UnityEngine.Object.FindObjectsOfType<CharacterBody>())
                    if (IsBandit(body)) { stats.Apply(body); body.RecalculateStats(); }
            }
            foreach (Profile p in skills.Values)
            {
                S.Skill s = p.Skill; s.ApplyDefinition();
                s.SetKeyword("KEYWORD_SUPERBLEED", C.Get(p.Hemorrhage) > 0);
                if (p.Index >= 5) s.SetKeyword("KEYWORD_SLAYER", true);
                if (p.AutoFire != null) s.Definition.mustKeyPress = !C.Get(p.AutoFire);
                string token = "3QS_BANDIT_" + States[p.Index].ToUpperInvariant() + "_DESC";
                S.SetDescription(s, token, () => Description(p)); s.Definition.skillDescriptionToken = token;
                if (p.Index == 6) S.SetText("LG_TOKEN_DESCRIPTION_" + RoR2Content.Buffs.BanditSkull.name, SkullDescription);
            }
        }
        private static string SkullDescription() => "Desperado gains " + S.Damage(S.Number(C.Get(skullDamage)) + "% damage") + " per stack.";
        private static bool LungeEnabled => C.Get(lunge) && C.Get(lungeDistance) > 0f;
        private static string Description(Profile p)
        {
            string damage = S.DamageText(C.Get(p.Skill.Damage), 0f, false), text;
            switch (p.Index)
            {
                case 0: text = "Fire a shotgun blast of " + S.Damage(C.Get(pellets).ToString()) + " pellets for " + damage + " each."; break;
                case 1: text = "Fire a rifle shot for " + damage + "."; break;
                case 2: text = (LungeEnabled ? S.Utility("Lunge forward") + " and slash" : "Slash") + " with a dagger for " + damage + "."; break;
                case 3: text = "Throw a shiv for " + damage + "."; break;
                case 4: text = S.Utility("Stunning") + ". Become " + S.Utility("invisible") + " and gain " + S.Utility(S.Number(C.Get(smokeSpeed)) + "% movement speed") + ". Deal " + damage + " on entering and leaving invisibility."; break;
                case 5: text = "Fire a revolver shot for " + damage + ". Kills " + S.Utility("reset all skill cooldowns") + "."; break;
                default: text = "Fire a revolver shot for " + damage + ". Kills grant a stack that increases Desperado's damage by " + S.Damage(S.Number(C.Get(skullDamage)) + "%") + "."; break;
            }
            float chance = C.Get(p.Hemorrhage);
            if (chance > 0f) text += chance >= 100f ? " Critical hits inflict " + S.Damage("Hemorrhage") + "." : " Critical hits have a " + S.Damage(S.Number(chance) + "%") + " chance to inflict " + S.Damage("Hemorrhage") + ".";
            return (p.Index >= 5 ? S.Damage("Slayer.") + " " : "") + p.Skill.AgileText + text;
        }
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (n, v) => b.Append("\n").Append(n).Append(": ").Append(S.Utility(v));
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Skill.Radius != null) add("AOE Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.Duration != null) add("Duration", S.Number(C.Get(p.Skill.Duration)) + "s");
            if (p.MinSpread != null)
            {
                add("Random Spread", S.Number(C.Get(p.MinSpread)) + "–" + S.Number(Math.Max(C.Get(p.MinSpread), C.Get(p.MaxSpread))) + "°");
                add("Maximum Spread Bloom", S.Number(C.Get(p.BloomSpread)) + "°");
            }
            if (p.Index == 1) add("Pierces", C.Get(pierces) < 0 ? "Unlimited" : C.Get(pierces).ToString());
            if (p.Index == 2)
            {
                add("Hit Box Size", S.Number(C.Get(hitBoxSize)) + "%");
                if (LungeEnabled) { add("Lunge Distance", S.Number(C.Get(lungeDistance)) + "m"); add("Lunge Duration", S.Number(C.Get(lungeDuration)) + "s"); }
            }
            if (p.Index == 3) add("Hit Box Size", S.Number(C.Get(shivHitBoxSize)) + "%");
            if (p.Grace != null) add("Grace Period", S.Number(C.Get(p.Grace)) + "s");
            return b.ToString();
        }
        private sealed class Scope { internal Profile Previous; internal CharacterBody Body; }
        private static void BeforeEnter(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = active, Body = activeBody };
            Profile p = ForState(__instance); CharacterBody body = __instance.outer ? __instance.outer.GetComponent<CharacterBody>() : null;
            if (p == null || !IsBandit(body)) return;
            active = p; activeBody = body;
            S.Write(__instance, p.Index == 4 ? "blastAttackDamageCoefficient" : "damageCoefficient", C.Get(p.Skill.Damage) / 100f);
            S.Write(__instance, p.Index == 4 ? "blastAttackProcCoefficient" : "procCoefficient", C.Get(p.Skill.Proc));
            if (p.Index < 2)
            {
                S.Write(__instance, "minSpread", C.Get(p.MinSpread));
                S.Write(__instance, "maxSpread", Math.Max(C.Get(p.MinSpread), C.Get(p.MaxSpread)));
                if (p.Index == 0)
                {
                    S.Write(__instance, "bulletCount", C.Get(pellets));
                    // The shotgun adds the body's bloom to its fan separately from each pellet's random cone.
                    float adjustment = SpreadBloom(p, body) - body.spreadBloomAngle;
                    S.Write(__instance, "minFixedSpreadYaw", C.Get(p.MinFan) + adjustment);
                    S.Write(__instance, "maxFixedSpreadYaw", Math.Max(C.Get(p.MinFan), C.Get(p.MaxFan)) + adjustment);
                }
            }
            if (p.Index == 4)
            {
                S.Write(__instance, "duration", C.Get(p.Skill.Duration)); S.Write(__instance, "blastAttackRadius", C.Get(p.Skill.Radius));
                if (p.Skill.CooldownAfterDuration && body.skillLocator && body.skillLocator.utility)
                    body.skillLocator.utility.SetBlockedCooldownSkillState(true);
            }
        }
        private static Exception AfterEnter(Scope __state, Exception __exception)
        { if (__state != null) { active = __state.Previous; activeBody = __state.Body; } return __exception; }
        private static void BeforeSmoke(EntityState __instance, out Scope __state) => BeforeEnter(__instance, out __state);
        private static Exception AfterSmoke(Scope __state, Exception __exception) => AfterEnter(__state, __exception);
        private static void AfterStealthExit(EntityState __instance)
        {
            CharacterBody body = __instance.outer ? __instance.outer.GetComponent<CharacterBody>() : null;
            if (IsBandit(body) && body.skillLocator && body.skillLocator.utility)
                body.skillLocator.utility.SetBlockedCooldownSkillState(false);
        }
        private sealed class PrimaryTiming { internal float ReadyAt; }
        private static void AfterPrimaryEnter(EntityState __instance)
        {
            Profile p = ForState(__instance);
            CharacterBody body = __instance.outer ? __instance.outer.GetComponent<CharacterBody>() : null;
            if (p?.AutoFire == null || !IsBandit(body) || !body.skillLocator || !body.skillLocator.primary) return;
            float duration = C.Get(p.AutoFireDelay);
            if (C.Get(p.ScaleAutoFireDelay)) duration /= Math.Max(0.01f, body.attackSpeed);
            primaryTimings.GetValue(body.skillLocator.primary, _ => new PrimaryTiming()).ReadyAt = Time.fixedTime + duration;
        }
        private sealed class Dash
        {
            internal CharacterBody Body;
            internal Component Motor;
            internal Vector3 Direction;
            internal float Distance, Duration, Elapsed;
        }
        private static void AfterDaggerEnter(EntityState __instance)
        {
            dashes.Remove(__instance);
            CharacterBody body = __instance.outer ? __instance.outer.GetComponent<CharacterBody>() : null;
            if (!IsBandit(body) || !LungeEnabled ||
                (stateAuthority?.GetValue(__instance, null) is bool authority && !authority)) return;
            Component motor = (object)body.characterMotor as Component;
            if (!motor) return;
            Vector3 forward = body.inputBank ? body.inputBank.aimDirection : body.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) { forward = body.transform.forward; forward.y = 0f; }
            float duration = Math.Max(0.001f, Math.Min(C.Get(lungeDuration), S.Value(__instance, "duration", C.Get(lungeDuration))));
            dashes.Add(__instance, new Dash { Body = body, Motor = motor, Direction = forward.normalized, Distance = C.Get(lungeDistance), Duration = duration });
        }
        private static void BeforeDaggerTick(EntityState __instance)
        {
            if (!dashes.TryGetValue(__instance, out Dash dash) || !LungeEnabled || !dash.Body || !dash.Motor) return;
            if (S.Value(__instance, "hitPauseTimer") > 0f) return;
            float step = Math.Min(Time.fixedDeltaTime, dash.Duration - dash.Elapsed);
            if (step <= 0f) return;
            // CharacterMotor resolves this displacement against terrain rather than teleporting the body.
            addDisplacement.Invoke(dash.Motor, new object[] { dash.Direction * (dash.Distance * step / dash.Duration) });
            dash.Elapsed += step;
        }
        private static void AfterDaggerExit(EntityState __instance) => dashes.Remove(__instance);
        private sealed class ScaledBoxes { internal Transform[] Boxes; internal Vector3[] Scales; }
        private static void BeforeOverlap(OverlapAttack __instance, out ScaledBoxes __state)
        {
            __state = null; CharacterBody body = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null;
            if (!IsBandit(body) || !skills.TryGetValue(States[2], out Profile p) || !__instance.hitBoxGroup) return;
            if (__instance.inflictor && __instance.inflictor.GetComponent<ShivMarker>()) return;
            if (__instance.damageType.damageSource != DamageSource.Secondary ||
                !string.Equals(S.Read(__instance.hitBoxGroup, "groupName") as string, S.Read(p.Skill.Sample, "hitBoxGroupName") as string, StringComparison.Ordinal)) return;
            Transform[] boxes = __instance.hitBoxGroup.hitBoxes.Where(h => h).Select(h => h.transform).Distinct().ToArray();
            __state = new ScaledBoxes { Boxes = boxes, Scales = boxes.Select(t => t.localScale).ToArray() };
            for (int i = 0; i < boxes.Length; i++) boxes[i].localScale = __state.Scales[i] * (C.Get(hitBoxSize) / 100f);
            __instance.procCoefficient = C.Get(p.Skill.Proc);
        }
        private static Exception AfterOverlap(ScaledBoxes __state, Exception __exception)
        { if (__state != null) for (int i = 0; i < __state.Boxes.Length; i++) if (__state.Boxes[i]) __state.Boxes[i].localScale = __state.Scales[i]; return __exception; }
        private static void BeforeBlast(BlastAttack __instance)
        {
            if (active?.Index != 4 || __instance.damageType.damageSource != DamageSource.Utility || !IsBandit(__instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null)) return;
            __instance.falloffModel = Area(C.Get(active.Skill.Area));
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff f)
        { switch (f) { case S.AreaFalloff.Linear: return BlastAttack.FalloffModel.Linear; case S.AreaFalloff.HalfLinear: return BlastAttack.FalloffModel.HalfLinear; case S.AreaFalloff.QuarterLinear: return BlastAttack.FalloffModel.QuarterLinear; case S.AreaFalloff.SweetSpot: return BlastAttack.FalloffModel.SweetSpot; default: return BlastAttack.FalloffModel.None; } }
        private static IEnumerable<CodeInstruction> SmokeSpeedIL(IEnumerable<CodeInstruction> original)
        {
            var code = original.ToList();
            int buff = code.FindIndex(c => c.opcode == OpCodes.Ldsfld && c.operand is FieldInfo f && f.Name == "CloakSpeed");
            int value = buff < 0 ? -1 : code.FindIndex(buff, Math.Min(16, code.Count - buff), c => c.opcode == OpCodes.Ldc_R4 && c.operand is float f && Math.Abs(f - 0.4f) < 0.0001f);
            if (value < 0)
            {
                ThreeQsVanillaTweaks.Log.Warning("Smoke Bomb speed could not be changed: movement speed calculation not found.");
                return code;
            }
            CodeInstruction load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(code[value].labels); load.blocks.AddRange(code[value].blocks);
            code[value] = load; code.Insert(value + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Bandit), nameof(SmokeSpeedBonus))));
            return code;
        }
        private static float SmokeSpeedBonus(CharacterBody body) => IsBandit(body) ? C.Get(smokeSpeed) / 100f : 0.4f;
        private static IEnumerable<CodeInstruction> SidearmSprintIL(IEnumerable<CodeInstruction> original)
        {
            var code = original.ToList();
            MethodInfo getter = AccessTools.Property(typeof(CharacterBody), "isSprinting").GetGetMethod();
            int index = code.FindIndex(c => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && Equals(c.operand, getter));
            if (index < 0) return code;
            var state = new CodeInstruction(OpCodes.Ldarg_0); state.labels.AddRange(code[index].labels); state.blocks.AddRange(code[index].blocks);
            code[index] = state;
            code.Insert(index + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Bandit), nameof(SidearmCancelsFromSprint))));
            return code;
        }
        private static bool SidearmCancelsFromSprint(CharacterBody body, object state)
        {
            Profile p = ForState(state);
            return body.isSprinting && (!IsBandit(body) || p == null || string.IsNullOrEmpty(p.Skill.AgileText));
        }
        private static void BeforePrep(EntityState __instance, out float? __state)
        {
            __state = null; Profile p = ForState(__instance);
            CharacterBody body = __instance.outer ? __instance.outer.GetComponent<CharacterBody>() : null;
            if (p?.Release == null || !IsBandit(body) || !C.Get(p.Release)) return;
            // Observers wait for the owning player's replicated transition rather than firing on their own timer.
            bool observer = stateAuthority?.GetValue(__instance, null) is bool authority && !authority;
            if (!observer && (!body.inputBank || !body.inputBank.skill4.down)) return;
            __state = S.Value(__instance, "duration"); S.Write(__instance, "duration", float.PositiveInfinity);
        }
        private static Exception AfterPrep(EntityState __instance, float? __state, Exception __exception)
        { if (__state.HasValue) S.Write(__instance, "duration", __state.Value); return __exception; }
        private static float SpreadBloom(Profile p, CharacterBody body)
        {
            if (p.NativeBloomSpread > 0f) return body.spreadBloomAngle * C.Get(p.BloomSpread) / p.NativeBloomSpread;
            return S.Value(body, "spreadBloomInternal") * C.Get(p.BloomSpread);
        }
        private sealed class BulletContext
        {
            internal Profile Profile; internal BulletAttack.HitCallback Callback; internal bool Hit;
            internal readonly HashSet<HealthComponent> Targets = new HashSet<HealthComponent>();
        }
        private static void BeforeBullet(BulletAttack __instance, out BulletContext __state)
        {
            __state = null; CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            if (!IsBandit(body)) return;
            Profile p = activeBody == body && active != null && __instance.damageType.damageSource == Source(active.Index) ? active : null;
            if (p == null)
            {
                if ((__instance.damageType.damageType & DamageType.GiveSkullOnKill) != 0) skills.TryGetValue(States[6], out p);
                else if ((__instance.damageType.damageType & DamageType.ResetCooldownsOnKill) != 0) skills.TryGetValue(States[5], out p);
            }
            if (p == null) return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            __instance.falloffModel = (BulletAttack.FalloffModel)(int)C.Get(p.Skill.Falloff);
            if (p.MinSpread != null)
            {
                __instance.minSpread = C.Get(p.MinSpread);
                __instance.maxSpread = Math.Max(__instance.minSpread, Math.Max(C.Get(p.MinSpread), C.Get(p.MaxSpread)) + SpreadBloom(p, body));
            }
            if (p.Index == 6)
            {
                int skulls = body.GetBuffCount(RoR2Content.Buffs.BanditSkull);
                __instance.damage *= (1f + C.Get(skullDamage) / 100f * skulls) / (1f + 0.1f * skulls);
            }
            var context = new BulletContext { Profile = p, Callback = __instance.hitCallback }; __state = context;
            if (p.Index == 1) __instance.stopperMask = LayerIndex.world.mask;
            __instance.hitCallback = (BulletAttack attack, ref BulletAttack.BulletHit hit) =>
            {
                HurtBox hurt = hit.hurtBox ? hit.hurtBox : hit.hitHurtBox;
                HealthComponent health = hurt ? hurt.healthComponent : hit.entityObject ? hit.entityObject.GetComponent<HealthComponent>() : null;
                bool enemy = health && TeamComponent.GetObjectTeam(health.gameObject) != TeamComponent.GetObjectTeam(attack.owner);
                bool fresh = enemy && context.Targets.Add(health);
                if (enemy) context.Hit = true;
                bool proceed = context.Callback(attack, ref hit);
                if (p.Index != 1 || !fresh) return proceed;
                return proceed && (C.Get(pierces) < 0 || context.Targets.Count <= C.Get(pierces));
            };
        }
        private static Exception AfterBullet(BulletAttack __instance, BulletContext __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.hitCallback = __state.Callback;
                if (__exception == null && __state.Profile.Index == 6 && !__state.Hit && C.Get(resetMiss)) NotifyMiss(__instance.owner);
            }
            return __exception;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (fireProjectileInfo.projectilePrefab == shivPrefab && IsBandit(fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null) && skills.TryGetValue(States[3], out Profile p))
                fireProjectileInfo.speedOverride = C.Get(p.Skill.ProjectileSpeed);
        }
        private sealed class ShivMarker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            if (!shivPrefab || !IsBandit(__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null) || !skills.TryGetValue(States[3], out Profile p)) return;
            if (__instance.catalogIndex != shivPrefab.GetComponent<ProjectileController>().catalogIndex) return;
            var marker = __instance.gameObject.GetComponent<ShivMarker>();
            if (!marker)
            {
                marker = __instance.gameObject.AddComponent<ShivMarker>();
                float scale = C.Get(shivHitBoxSize) / 100f;
                // Resize collision geometry without changing the projectile or ghost transforms.
                foreach (Collider collider in __instance.GetComponentsInChildren<Collider>(true))
                {
                    if (collider is SphereCollider sphere) sphere.radius *= scale;
                    else if (collider is CapsuleCollider capsule) { capsule.radius *= scale; capsule.height *= scale; }
                    else if (collider is BoxCollider box) box.size *= scale;
                }
            }
            marker.Profile = p; marker.Origin = __instance.transform.position;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
        }
        private static DamageSource Source(int index) => index < 2 ? DamageSource.Primary : index < 4 ? DamageSource.Secondary : index == 4 ? DamageSource.Utility : DamageSource.Special;
        private static Profile ForDamage(DamageInfo info)
        {
            if (damageProfiles.TryGetValue(info, out Profile tagged)) return tagged;
            CharacterBody body = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null;
            if (!IsBandit(body) || info.dotIndex != DotController.DotIndex.None) return null;
            if ((info.damageType.damageType & DamageType.GiveSkullOnKill) != 0) return skills.TryGetValue(States[6], out Profile skull) ? skull : null;
            if ((info.damageType.damageType & DamageType.ResetCooldownsOnKill) != 0) return skills.TryGetValue(States[5], out Profile reset) ? reset : null;
            ShivMarker marker = info.inflictor ? info.inflictor.GetComponent<ShivMarker>() : null;
            if (marker) return marker.Profile;
            if (active != null && activeBody == body && info.damageType.damageSource == Source(active.Index)) return active;
            SkillLocator locator = body.skillLocator;
            if (!locator) return null;
            GenericSkill slot = info.damageType.damageSource == DamageSource.Primary ? locator.primary : info.damageType.damageSource == DamageSource.Secondary ? locator.secondary : info.damageType.damageSource == DamageSource.Utility ? locator.utility : null;
            return slot && definitions.TryGetValue(slot.skillDef, out Profile equipped) ? equipped : null;
        }
        private static void BeforeHit(DamageInfo damageInfo)
        {
            if (!NetworkServer.active || damageInfo.rejected) return;
            Profile p = ForDamage(damageInfo); if (p == null) return;
            damageInfo.damageType.damageType &= ~DamageType.SuperBleedOnCrit;
            if (damageInfo.crit && Util.CheckRoll(C.Get(p.Hemorrhage), damageInfo.attacker.GetComponent<CharacterBody>().master))
                damageInfo.damageType.damageType |= DamageType.SuperBleedOnCrit;
        }
        private sealed class DamageSnapshot { internal float Before; internal Profile Profile; internal CharacterBody Attacker; }
        private static float Combined(HealthComponent h) => h.health + h.shield + h.barrier;
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out DamageSnapshot __state)
        {
            __state = null;
            if (!NetworkServer.active) return;
            CharacterBody attacker = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            if (IsBandit(attacker) && C.Get(ignoreArmour) && damageInfo.dotIndex == DotController.DotIndex.SuperBleed)
                damageInfo.damageType.damageType |= DamageType.BypassArmor;
            Profile p = ForDamage(damageInfo);
            if (p != null && !damageProfiles.TryGetValue(damageInfo, out _)) damageProfiles.Add(damageInfo, p);
            if (p?.Index == 3 && damageInfo.inflictor)
            {
                var marker = damageInfo.inflictor.GetComponent<ShivMarker>();
                if (marker)
                {
                    S.BulletFalloff f = C.Get(p.Skill.Falloff); float distance = Vector3.Distance(marker.Origin, damageInfo.position);
                    if (f == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01((60f - distance) / 35f) * 0.5f;
                    if (f == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.3f + Mathf.Clamp01((40f - distance) / 28f) * 0.7f;
                }
            }
            if (p?.Grace != null || (IsBandit(__instance.body) && skills.ContainsKey(States[6]) && C.Get(resetHit)))
                __state = new DamageSnapshot { Before = Combined(__instance), Profile = p, Attacker = attacker };
        }
        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo, DamageSnapshot __state)
        {
            if (__state == null || damageInfo.rejected || (__instance.alive && Combined(__instance) >= __state.Before)) return;
            if (IsBandit(__instance.body) && skills.ContainsKey(States[6]) && C.Get(resetHit)) ResetSkulls(__instance.body.master);
            Profile p = __state.Profile;
            if (p?.Grace == null || !__state.Attacker || !__state.Attacker.master) return;
            var marks = __instance.GetComponent<KillMarks>() ?? __instance.gameObject.AddComponent<KillMarks>(); marks.Victim = __instance;
            bool nativeReward = !__instance.alive && ((p.Index == 5 && (damageInfo.damageType.damageType & DamageType.ResetCooldownsOnKill) != 0) ||
                (p.Index == 6 && (damageInfo.damageType.damageType & DamageType.GiveSkullOnKill) != 0));
            if (nativeReward)
            {
                marks.Acknowledge(__state.Attacker.master);
                if (p.Index == 6) Memory(__state.Attacker.master).Skulls = __state.Attacker.GetBuffCount(RoR2Content.Buffs.BanditSkull);
            }
            else marks.Mark(__state.Attacker.master, p.Index, C.Get(p.Grace));
        }
        private sealed class Claim { internal CharacterMaster Master; internal int Skill; internal float Deadline; }
        private sealed class KillMarks : MonoBehaviour
        {
            internal HealthComponent Victim;
            private readonly Dictionary<CharacterMaster, Claim> claims = new Dictionary<CharacterMaster, Claim>();
            private readonly HashSet<CharacterMaster> rewarded = new HashSet<CharacterMaster>();
            internal void Mark(CharacterMaster master, int skill, float grace)
            {
                if (rewarded.Contains(master)) return;
                if (!Victim.alive) { Resolve(new Claim { Master = master, Skill = skill }); return; }
                if (grace <= 0f) { if (skill == 6 && C.Get(resetFailed)) ResetSkulls(master); return; }
                claims[master] = new Claim { Master = master, Skill = skill, Deadline = Time.fixedTime + grace };
            }
            internal void Acknowledge(CharacterMaster master) { rewarded.Add(master); claims.Remove(master); }
            internal void Died(CharacterMaster nativeKiller)
            {
                // Native revolver kills have already granted their reward before this death event.
                if (nativeKiller) Acknowledge(nativeKiller);
                foreach (Claim claim in claims.Values.ToArray()) if (Time.fixedTime <= claim.Deadline) Resolve(claim);
                claims.Clear();
            }
            private void Resolve(Claim claim)
            { if (claim.Master && rewarded.Add(claim.Master)) Reward(claim.Master, claim.Skill, Victim); claims.Remove(claim.Master); }
            private void FixedUpdate()
            {
                foreach (Claim claim in claims.Values.ToArray())
                    if (Time.fixedTime > claim.Deadline)
                    { claims.Remove(claim.Master); if (claim.Skill == 6 && C.Get(resetFailed)) ResetSkulls(claim.Master); }
            }
        }
        private static void Reward(CharacterMaster master, int index, HealthComponent victim)
        {
            CharacterBody body = master ? master.GetBody() : null;
            if (!master || !victim) return;
            if (index == 5) { if (body && body.skillLocator) body.skillLocator.ResetSkills(); }
            else
            {
                if (!victim.body || !victim.body.master) return;
                SkullMemory memory = Memory(master);
                if (body)
                {
                    SetSkulls(body, Math.Min(int.MaxValue - 1, body.GetBuffCount(RoR2Content.Buffs.BanditSkull)) + 1);
                    memory.Skulls = body.GetBuffCount(RoR2Content.Buffs.BanditSkull);
                }
                else memory.Skulls = Math.Min(int.MaxValue - 1, memory.Skulls) + 1;
            }
            GameObject effect = skills.TryGetValue(States[index], out Profile rewardedSkill) ? rewardedSkill.KillEffect : null;
            if (effect) EffectManager.SpawnEffect(effect, new EffectData { origin = victim.transform.position }, true);
        }
        private static void OnDeath(DamageReport report)
        {
            if (!NetworkServer.active) return;
            CharacterMaster nativeKiller = null;
            Profile killingSkill = report.damageInfo == null ? null : ForDamage(report.damageInfo);
            if ((killingSkill?.Index == 5 && (report.damageInfo.damageType.damageType & DamageType.ResetCooldownsOnKill) != 0) ||
                (killingSkill?.Index == 6 && (report.damageInfo.damageType.damageType & DamageType.GiveSkullOnKill) != 0)) nativeKiller = report.attackerMaster;
            if (report.victim) report.victim.GetComponent<KillMarks>()?.Died(nativeKiller);
            if (skills.ContainsKey(States[6]) && IsBandit(report.victimBody) && report.victimBody.master)
            {
                SkullMemory memory = Memory(report.victimBody.master);
                memory.Skulls = C.Get(resetDeath) ? 0 : report.victimBody.GetBuffCount(RoR2Content.Buffs.BanditSkull);
            }
        }
        private sealed class SkullMemory : MonoBehaviour
        {
            internal CharacterMaster Master; internal int Skulls;
            private void Awake() { Master = GetComponent<CharacterMaster>(); memories.Add(this); }
            private void OnDestroy() { memories.Remove(this); }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !Master) return;
                CharacterBody body = Master.GetBody();
                if (IsBandit(body) && body.healthComponent && body.healthComponent.alive) Skulls = body.GetBuffCount(RoR2Content.Buffs.BanditSkull);
            }
        }
        private static SkullMemory Memory(CharacterMaster master) => master.GetComponent<SkullMemory>() ?? master.gameObject.AddComponent<SkullMemory>();
        private static void SetSkulls(CharacterBody body, int count)
        {
            int current = body.GetBuffCount(RoR2Content.Buffs.BanditSkull);
            if (count == current) return;
            MethodInfo method = AccessTools.Method(typeof(CharacterBody), count > current ? "AddUnmanagedBuff" : "RemoveUnmanagedBuff", new[] { typeof(BuffDef), typeof(int) });
            if (method != null) method.Invoke(body, new object[] { RoR2Content.Buffs.BanditSkull, Math.Abs(count - current) });
            else body.SetBuffCount(RoR2Content.Buffs.BanditSkull.buffIndex, count);
        }
        private static void ResetSkulls(CharacterMaster master)
        { if (master) { Memory(master).Skulls = 0; CharacterBody body = master.GetBody(); if (body) SetSkulls(body, 0); } }
        private static void AfterBodyAwake(CharacterBody __instance) { if (IsBandit(__instance)) stats?.Apply(__instance); }
        private static void OnBodyStart(CharacterBody body)
        { if (NetworkServer.active && IsBandit(body) && body.master) SetSkulls(body, Memory(body.master).Skulls); }
        private static void OnBodyDestroyed(CharacterBody body)
        {
            if (!NetworkServer.active || !IsBandit(body) || !body.master) return;
            Memory(body.master).Skulls = body.healthComponent && !body.healthComponent.alive && C.Get(resetDeath) ? 0 : body.GetBuffCount(RoR2Content.Buffs.BanditSkull);
        }
        private static void OnStage(Stage stage)
        { if (NetworkServer.active && C.Get(resetStage)) foreach (SkullMemory memory in memories.ToArray()) if (memory && memory.Master) ResetSkulls(memory.Master); }
        private sealed class NetworkRunner : MonoBehaviour
        {
            private void Awake() { DontDestroyOnLoad(gameObject); }
            private void Update()
            {
                if (!NetworkServer.active) { serverRegistered = false; return; }
                if (serverRegistered) return;
                NetworkServer.RegisterHandler(MissMessageId, OnMiss); serverRegistered = true;
            }
        }
        private sealed class MissMessage : MessageBase
        {
            internal NetworkInstanceId Body;
            public override void Serialize(NetworkWriter writer) { writer.Write(Body); }
            public override void Deserialize(NetworkReader reader) { Body = reader.ReadNetworkId(); }
        }
        private static void NotifyMiss(GameObject owner)
        {
            CharacterBody body = owner ? owner.GetComponent<CharacterBody>() : null;
            if (!IsBandit(body)) return;
            if (NetworkServer.active) { ResetSkulls(body.master); return; }
            NetworkIdentity identity = owner.GetComponent<NetworkIdentity>();
            if (identity && NetworkManager.singleton && NetworkManager.singleton.client != null)
                NetworkManager.singleton.client.Send(MissMessageId, new MissMessage { Body = identity.netId });
        }
        private static void OnMiss(NetworkMessage message)
        {
            if (!NetworkServer.active || !C.Get(resetMiss)) return;
            GameObject obj = NetworkServer.FindLocalObject(message.ReadMessage<MissMessage>().Body);
            if (!obj) return;
            var identity = obj.GetComponent<NetworkIdentity>();
            if (!identity || identity.clientAuthorityOwner != message.conn) return;
            CharacterBody body = obj.GetComponent<CharacterBody>(); if (IsBandit(body)) ResetSkulls(body.master);
        }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (!__result || !definitions.TryGetValue(__instance, out Profile p)) return;
            if (p.Skill.RequireAll) __result = skillSlot.stock >= skillSlot.maxStock;
            // Keep the configured repeat delay even if another state cancels the firing animation.
            if (__result && p.AutoFire != null && C.Get(p.AutoFire) && IsBandit(skillSlot.characterBody))
            {
                if (primaryTimings.TryGetValue(skillSlot, out PrimaryTiming timing)) __result = Time.fixedTime >= timing.ReadyAt;
                else if (skillSlot.stateMachine && p.Skill.StateType.IsInstanceOfType(skillSlot.stateMachine.state)) __result = false;
            }
        }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out int? __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = __instance.stockToConsume; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, int? __state, Exception __exception)
        { if (__state.HasValue) __instance.stockToConsume = __state.Value; return __exception; }
    }
}
