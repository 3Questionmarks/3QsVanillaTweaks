using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class Artificer
    {
        private const string Root = "Survivors - 06 Artificer", StatsRoot = "Survivor-Stats - 06 Artificer";
        private static readonly string[] Names = { "Flame Bolt", "Plasma Bolt", "Charged Nano-Bomb", "Cast Nano-Spear", "Snapfreeze", "Tectonic Blast", "Flamethrower", "Ion Surge" };
        private static readonly string[] StateNames = { "Weapon.FireFireBolt", "Weapon.FireLightningBolt", "Weapon.ThrowNovabomb", "Weapon.ThrowIcebomb", "Weapon.PrepWall", "Weapon.FireEarthShards", "Weapon.Flamethrower", "FlyUpState" };
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile;
            internal ConfigEntry<float> Minimum, ChargeTime;
            internal ConfigEntry<bool> Rapid;
            internal ConfigEntry<bool> FloatWhileCharging;
            internal readonly List<GameObject> Children = new List<GameObject>();
        }
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly List<Profile> allProfiles = new List<Profile>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<string> patches = new HashSet<string>();
        private static ConfigEntry<bool> enabled, statsEnabled, voltaicEnabled, gravity, cancelFlames;
        private static ConfigEntry<float> boltBurn, flameBurn, boltDamage, boltProc, shardLifetime, flameRange, launchVelocity;
        private static ConfigEntry<int> shards, flameTicks, launchScale;
        private static ConfigEntry<float> wallDuration;
        private static ConfigEntry<float> fallImmunityTime;
        private static ConfigEntry<int> maxWalls;
        private static CharacterBody prefabBody;
        private static S.Stats stats;
        private static Harmony harmony;
        private static SkillDef voltaic;
        private static bool expanded;
        private static Type passiveType;
        private static MethodInfo passiveCast;
        private static AnimationCurve surgeCurve;
        private static float surgePeak, nativeMoveSpeed;
        private static object surgeSample;
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static bool manualFlameTick;
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class FlameClock { internal int Fired; internal bool Released, VisualsAdjusted; }
        private static readonly ConditionalWeakTable<EntityState, FlameClock> flameClocks = new ConditionalWeakTable<EntityState, FlameClock>();
        private sealed class LandingProtection { internal float ExpiresAt; }
        private static readonly ConditionalWeakTable<CharacterBody, LandingProtection> landingProtection = new ConditionalWeakTable<CharacterBody, LandingProtection>();
        [ThreadStatic] private static CharacterBody protectedLanding;
        private sealed class ProjectileMarker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; internal WallGroup Wall; }
        private sealed class WallGroup { internal readonly List<GameObject> Objects = new List<GameObject>(); internal int Roots; }
        private sealed class WallTracker { internal readonly List<WallGroup> Groups = new List<WallGroup>(); internal WallGroup Incoming; }
        private sealed class PendingWall { internal GameObject Owner, Prefab; internal Vector3 Position; internal WallGroup Group; }
        private static readonly List<PendingWall> pendingWalls = new List<PendingWall>();
        private static readonly ConditionalWeakTable<GameObject, WallTracker> walls = new ConditionalWeakTable<GameObject, WallTracker>();
        private static readonly ConditionalWeakTable<EntityState, WallGroup> wallCasts = new ConditionalWeakTable<EntityState, WallGroup>();
        [ThreadStatic] private static WallGroup parentWall;
        private static Type State(string suffix) => typeof(EntityState).Assembly.GetType("EntityStates.Mage." + suffix);
        private static object Sample(string suffix) => EntityStateCatalog.InstantiateState(State(suffix));
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsArtificer(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static Profile ForState(EntityState state)
        {
            if (state is VoltaicSurgeState) return profiles.TryGetValue(7, out Profile surge) && expanded ? surge : null;
            Type type = state?.GetType();
            if (type == null || type.Namespace != "EntityStates.Mage" && type.Namespace != "EntityStates.Mage.Weapon") return null;
            int i = type.Name == "ChargeNovabomb" ? 2 : type.Name == "ChargeIcebomb" ? 3 : Array.FindIndex(StateNames, s => s.Substring(s.LastIndexOf('.') + 1) == type.Name);
            return profiles.TryGetValue(i, out Profile p) && !(i == 7 && expanded) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Artificer changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable Artificer stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            voltaicEnabled = C.BindStartupBool(Root + " - Voltaic Surge", "Changes Enabled", true, "Add Voltaic Surge when Artificer Extended is installed. Uses the Ion Surge settings and leaves Extended's Ion Surge unchanged. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            // Collecting providers happens after every plugin has finished Awake.
            ContentManager.collectContentPackProviders += CollectContent;
            RoR2Application.onLoad += Load;
        }
        private static void CollectContent(ContentManager.AddContentPackProviderDelegate add)
        {
            if (!voltaicEnabled.Value || !switches[Names[7]].Value || !Chainloader.PluginInfos.TryGetValue("com.Borbo.ArtificerExtended", out var plugin)) return;
            expanded = true;
            passiveType = plugin.Instance.GetType().Assembly.GetType("ArtificerExtended.Passive.AltArtiPassive");
            passiveCast = passiveType == null ? null : AccessTools.Method(passiveType, "SkillCast");
            add(new SurgeContent());
        }
        private sealed class SurgeContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.VoltaicSurge";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                SkillDef native = Addressables.LoadAssetAsync<SkillDef>("RoR2/Base/Mage/MageBodyFlyUp.asset").WaitForCompletion();
                voltaic = UnityEngine.Object.Instantiate(native);
                ((ScriptableObject)voltaic).name = voltaic.skillName = "3QSVoltaicSurge";
                // ElementCounter reads the third word of this token to identify lightning skills.
                voltaic.skillNameToken = "3QS_SURGE_LIGHTNING_NAME";
                voltaic.skillDescriptionToken = "3QS_SURGE_LIGHTNING_DESCRIPTION";
                voltaic.activationState = new SerializableEntityStateType(typeof(VoltaicSurgeState));
                voltaic.cancelSprintingOnActivation = voltaic.canceledFromSprinting = false;
                voltaic.baseRechargeInterval = 8f; voltaic.baseMaxStock = voltaic.rechargeStock = voltaic.requiredStock = voltaic.stockToConsume = 1;
                voltaic.icon = SurgeIcon(native.icon);
                R2API.LanguageAPI.Add(voltaic.skillNameToken, "Voltaic Surge");
                content.skillDefs.Add(new[] { voltaic });
                content.entityStateTypes.Add(new[] { typeof(VoltaicSurgeState) });
                args.ReportProgress(1f); yield break;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args) { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }
        private static Sprite SurgeIcon(Sprite original)
        {
            if (!original || Application.isBatchMode) return original;
            RenderTexture previous = RenderTexture.active;
            RenderTexture target = null;
            try
            {
                int w = (int)original.rect.width, h = (int)original.rect.height;
                target = RenderTexture.GetTemporary(original.texture.width, original.texture.height, 0);
                Graphics.Blit(original.texture, target); RenderTexture.active = target;
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.ReadPixels(original.rect, 0, 0);
                // Keep the original artwork, adding a small lightning mark in its corner.
                for (int y = 0; y < h / 3; y++) for (int x = 0; x < w / 3; x++)
                {
                    float px = x / (w / 3f), py = y / (h / 3f);
                    if (Math.Abs(px - (py > 0.5f ? 0.25f + py * 0.4f : 0.45f + py * 0.4f)) < 0.09f)
                        texture.SetPixel(w - w / 3 + x, y, new Color(0.35f, 0.95f, 1f, 1f));
                }
                texture.Apply(); texture.name = "3QSVoltaicSurgeIcon";
                return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), original.pixelsPerUnit);
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Could not create Voltaic Surge icon: " + e.Message); return original; }
            finally { RenderTexture.active = previous; if (target) RenderTexture.ReleaseTemporary(target); }
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (!patches.Add(target.MethodHandle.Value + ":" + prefix + ":" + postfix + ":" + finalizer)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Artificer), prefix), postfix == null ? null : new HarmonyMethod(typeof(Artificer), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Artificer), finalizer));
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Mage/MageBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                nativeMoveSpeed = prefabBody.baseMoveSpeed;
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Artificer");
                if (statsEnabled.Value) { stats = new S.Stats(StatsRoot, prefabBody); stats.Apply(prefabBody); Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake)); }
                if (expanded && voltaic)
                {
                    GenericSkill special = prefabBody.GetComponent<SkillLocator>()?.special;
                    if (!special || !special.skillFamily) throw new MissingMemberException("Artificer special skill family unavailable.");
                    SkillFamily.Variant native = special.skillFamily.variants.FirstOrDefault(v => v.skillDef && v.skillDef.skillNameToken == "MAGE_SPECIAL_LIGHTNING_NAME");
                    var variant = new SkillFamily.Variant { skillDef = voltaic, unlockableDef = native.unlockableDef, viewableNode = new ViewablesCatalog.Node(voltaic.skillNameToken, false, null) };
                    special.skillFamily.variants = special.skillFamily.variants.Concat(new[] { variant }).ToArray();
                }
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(g => g.skillFamily).SelectMany(g => g.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(StateNames[i]); if (type == null) { ThreeQsVanillaTweaks.Log.Warning("Artificer skill unavailable: " + Names[i]); continue; }
                    SkillDef[] matching = i == 7 && expanded ? (voltaic ? new[] { voltaic } : Array.Empty<SkillDef>()) : defs.Where(d => d.activationState.stateType == State(i == 2 ? "Weapon.ChargeNovabomb" : i == 3 ? "Weapon.ChargeIcebomb" : StateNames[i])).ToArray();
                    if (matching.Length == 0) { ThreeQsVanillaTweaks.Log.Warning("Artificer skill unavailable: " + Names[i]); continue; }
                    foreach (SkillDef def in matching)
                    {
                        var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                        Bind(p); p.Skill.Loaded = true; if (!profiles.ContainsKey(i)) profiles[i] = p;
                        allProfiles.Add(p); definitions[def] = p;
                        Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), () => C.Get(p.Skill.Proc));
                    }
                    if (i == 7 && expanded) continue;
                    HookState(type);
                    if (i == 2 || i == 3) HookState(State(i == 2 ? "Weapon.ChargeNovabomb" : "Weapon.ChargeIcebomb"));
                }
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeProjectileDamage));
                    Patch(typeof(BlastAttack), "Fire", nameof(BeforeBlast));
                }
                if (profiles.ContainsKey(6))
                {
                    Patch(State(StateNames[6]), "FireGauntlet", nameof(BeforeFlameShot));
                    Patch(typeof(BulletAttack), "Fire", nameof(BeforeBullet), args: Type.EmptyTypes);
                    Patch(typeof(SkillDef), "CanExecute", postfix: nameof(AfterCanExecute));
                }
                if (profiles.ContainsKey(2) || profiles.ContainsKey(3))
                    Patch(typeof(CharacterMotor), "UpdateVelocity", postfix: nameof(AfterChargeVelocity));
                if (profiles.ContainsKey(4))
                {
                    Patch(typeof(ProjectileMageFirewallController), "CreateWalkers", nameof(BeforeWallChildren), finalizer: nameof(AfterWallChildren));
                    Patch(typeof(ProjectileMageFirewallWalkerController), "HandleProjectileGeneration", nameof(BeforeWallChildren), finalizer: nameof(AfterWallChildren));
                }
                if (profiles.ContainsKey(7))
                {
                    Patch(typeof(GlobalEventManager), "OnCharacterHitGroundServer", nameof(BeforeLanding), finalizer: nameof(AfterLanding));
                    Patch(typeof(GlobalEventManager), "IsImmuneToFallDamage", postfix: nameof(AfterFallImmunity));
                }
                if (profiles.ContainsKey(7) && !expanded)
                {
                    Patch(State("FlyUpState"), "HandleMovements", nameof(BeforeState), finalizer: nameof(EndScope));
                    harmony.Patch(AccessTools.Method(State("FlyUpState"), "HandleMovements"), transpiler: new HarmonyMethod(typeof(Artificer), nameof(SurgeMovementIL)));
                }
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Artificer changes could not be loaded: " + e); }
        }
        private static void HookState(Type type)
        {
            Patch(type, "OnEnter", nameof(BeforeState), nameof(AfterEnter), nameof(EndScope));
            Patch(type, "FixedUpdate", nameof(BeforeState), nameof(AfterTick), nameof(EndScope));
        }
        private static ConfigEntry<float> Chance(string section, string name, float value, float native, string description) =>
            C.Bind(section, name, value, new ConfigDescription(description + " Vanilla: " + S.Number(native) + "%.", new AcceptableValueRange<float>(0f, 100f)));
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(changedAgile: i == 6 || i == 7 ? true : (bool?)null, damage: false);
            if (i <= 5)
            {
                p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
                if (!p.Projectile) throw new MissingMemberException("Projectile unavailable for " + Names[i]);
            }
            float nativeDamage = S.Value(sample, i == 7 ? "blastAttackDamageCoefficient" : "damageCoefficient", 1f) * 100f;
            float proc = i == 7 ? S.Value(sample, "blastAttackProcCoefficient", 1f) : i == 6 ? S.Value(sample, "procCoefficientPerTick", 1f) : p.Projectile.GetComponent<ProjectileController>().procCoefficient;
            if (i == 2 || i == 3)
            {
                p.Minimum = S.Float(s.Section, "Min Damage (%)", 400f, S.Value(sample, "minDamageCoefficient") * 100f, "Damage at minimum charge as a percentage of base damage.");
                s.Damage = S.Float(s.Section, "Max Damage (%)", i == 2 ? 2000f : 1500f, S.Value(sample, "maxDamageCoefficient") * 100f, "Damage at full charge as a percentage of base damage.");
                s.Proc = S.Float(s.Section, "Proc Coefficient", proc, proc, "Proc coefficient per hit.");
                s.Falloff = C.Bind(s.Section, "Damage Falloff", S.BulletFalloff.None, "Damage falloff over distance. Vanilla: None.");
                object charge = Sample(i == 2 ? "Weapon.ChargeNovabomb" : "Weapon.ChargeIcebomb");
                p.ChargeTime = S.Float(s.Section, "Charge Time", 2f, S.Value(charge, "baseDuration"), "Time to fully charge in seconds, before attack speed is applied.", 0.001f);
                p.FloatWhileCharging = S.Bool(s.Section, "Float While Charging", true, false, "Hold your height while charging.");
            }
            else if (i == 6)
            {
                float duration = S.Value(sample, "baseFlamethrowerDuration"), frequency = S.Value(sample, "tickFrequency");
                int intervals = Math.Max(1, (int)Math.Ceiling(duration * frequency));
                s.BindDamage(S.Value(sample, "totalDamageCoefficient") / intervals * 100f, 100f, proc, true);
                s.Duration = S.Float(s.Section, "Duration", 3f, duration, "Flamethrower duration in seconds.", 0.001f);
                flameTicks = S.Int(s.Section, "Ticks Over Duration", 22, intervals + 1, "Number of damage ticks over the duration at base attack speed.", 1);
                flameRange = S.Float(s.Section, "Range", 30f, S.Value(sample, "maxDistance"), "Maximum distance the flames reach, in metres.");
                s.Radius = S.Float(s.Section, "AOE Radius", S.Value(sample, "radius"), S.Value(sample, "radius"), "Radius of the flames in metres.");
                flameBurn = Chance(s.Section, "Burn Chance (%)", 100f, S.Value(sample, "ignitePercentChance"), "Chance for each hit to ignite the target.");
                cancelFlames = S.Bool(s.Section, "Can Cancel", true, false, "Press the skill again to end Flamethrower early.");
            }
            else if (i == 7)
            {
                s.Damage = S.Float(s.Section, "Damage (%)", nativeDamage, nativeDamage, "Damage per hit as a percentage of base damage.");
                s.Proc = S.Float(s.Section, "Proc Coefficient", proc, proc, "Proc coefficient per hit.");
            }
            else s.BindDamage(nativeDamage, i < 2 ? 300f : (float?)null, proc);
            if (i <= 5)
            {
                if (!p.Projectile.GetComponent<ProjectileSimple>() && p.Projectile.GetComponent<ProjectileCharacterController>())
                {
                    var mover = p.Projectile.GetComponent<ProjectileCharacterController>();
                    s.ProjectileSpeed = S.Float(s.Section, "Projectile Speed (m/s)", mover.velocity, mover.velocity, "Projectile speed in metres per second.");
                }
                else s.BindProjectileSpeed(p.Projectile);
                BindArea(p);
            }
            if (i < 2)
            {
                bool nativeRapid = S.Read(s.Definition, "attackSpeedBuffsRestockSpeed") is bool value && value;
                p.Rapid = S.Bool(s.Section, "Rapid", true, nativeRapid, "Reduce the cooldown with attack speed.");
                if (i == 0) boltBurn = Chance(s.Section, "Burn Chance (%)", 100f, 100f, "Chance for each bolt to ignite its targets.");
            }
            if (i == 2)
            {
                var beam = p.Projectile.GetComponent<ProjectileProximityBeamController>();
                if (!beam) throw new MissingMemberException("Nano-Bomb lightning bolts unavailable.");
                boltDamage = S.Float(s.Section, "Bolt Damage (%)", 10f, beam.damageCoefficient * 100f, "Damage per lightning bolt as a percentage of the Nano-Bomb's damage.");
                boltProc = S.Float(s.Section, "Bolt Proc Coefficient", 0.3f, beam.procCoefficient, "Proc coefficient of the lightning bolts.");
                var rigidbody = p.Projectile.GetComponent<Rigidbody>();
                gravity = S.Bool(s.Section, "Affected By Gravity", true, rigidbody && rigidbody.useGravity, "Allow gravity to curve the Nano-Bomb's flight.");
            }
            if (i == 4)
            {
                var descendants = new Queue<GameObject>(); descendants.Enqueue(p.Projectile);
                while (descendants.Count > 0)
                {
                    GameObject current = descendants.Dequeue();
                    foreach (Component component in current.GetComponents<Component>())
                        foreach (string field in new[] { "walkerPrefab", "firePillarPrefab" })
                            if (S.Read(component, field) is GameObject child && child != p.Projectile && !p.Children.Contains(child))
                            { p.Children.Add(child); descendants.Enqueue(child); }
                }
                DestroyOnTimer timer = p.Children.SelectMany(child => child.GetComponentsInChildren<DestroyOnTimer>(true)).FirstOrDefault();
                if (timer)
                {
                    wallDuration = S.Float(s.Section, "AOE Duration", timer.duration, timer.duration, "How long the ice pillars last, in seconds. -1 lasts until destroyed.", -1f);
                    maxWalls = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum active barriers per player. Recasting at this limit removes the oldest barrier. -1 is unlimited.", -1);
                }
            }
            if (i == 5)
            {
                shards = S.Int(s.Section, "Shards", 10, (int)S.Value(sample, "maxProjectiles"), "Number of shards fired.", 1);
                float nativeLifetime = Lifetime(p.Projectile);
                float meanSpeed = C.Get(s.ProjectileSpeed);
                var simple = p.Projectile.GetComponent<ProjectileSimple>();
                if (simple && simple.enableVelocityOverLifetime && simple.velocityOverLifetime != null)
                    meanSpeed *= Enumerable.Range(0, 128).Average(n => Math.Max(0f, simple.velocityOverLifetime.Evaluate((n + 0.5f) / 128f)));
                shardLifetime = S.Float(s.Section, "Projectile Lifetime", 50f / Math.Max(0.001f, meanSpeed), nativeLifetime, "How long shards travel before disappearing, in seconds.", 0.001f);
            }
            if (i == 7)
            {
                surgeSample = sample; surgeCurve = S.Read(sample, "speedCoefficientCurve") as AnimationCurve;
                if (surgeCurve == null) throw new MissingMemberException("Ion Surge launch curve unavailable.");
                surgePeak = Enumerable.Range(0, 129).Select(n => surgeCurve.Evaluate(n / 128f)).Max();
                launchVelocity = S.Float(s.Section, "Launch Velocity (m/s)", nativeMoveSpeed * surgePeak, nativeMoveSpeed * surgePeak, "Peak upward launch speed at base movement speed, in metres per second.");
                launchScale = S.Int(s.Section, "Launch Velocity Scale (%)", 0, 100, "How much movement speed changes the launch velocity. 0 disables scaling.");
                fallImmunityTime = S.Float(s.Section, "Fall Damage Immunity Time", 5f, 0f, "Ignore fall damage on your first landing within this many seconds of using the skill. 0 disables protection.");
                s.Duration = S.Float(s.Section, "Duration", S.Value(sample, "duration"), S.Value(sample, "duration"), "Launch duration in seconds.", 0.001f);
                s.Radius = S.Float(s.Section, "AOE Radius", S.Value(sample, "blastAttackRadius"), S.Value(sample, "blastAttackRadius"), "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
            }
        }
        private static float Lifetime(GameObject projectile)
        {
            var simple = projectile.GetComponent<ProjectileSimple>(); if (simple) return simple.lifetime;
            var mover = projectile.GetComponent<ProjectileCharacterController>(); if (mover) return mover.lifetime;
            var explosion = projectile.GetComponent<ProjectileImpactExplosion>(); if (explosion) return explosion.lifetime;
            throw new MissingMemberException("Projectile lifetime unavailable for " + projectile.name);
        }
        private static void BindArea(Profile p)
        {
            var explosion = p.Projectile.GetComponent<ProjectileExplosion>(); if (!explosion) return;
            p.Skill.Radius = S.Float(p.Skill.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
            S.AreaFalloff native = Enum.TryParse(explosion.falloffModel.ToString(), out S.AreaFalloff value) ? value : S.AreaFalloff.None;
            p.Skill.Area = C.Bind(p.Skill.Section, "AOE Damage Falloff", native, "Damage falloff from the centre of the explosion. Vanilla: " + native + ".");
        }
        private static void Apply()
        {
            if (stats != null && prefabBody) stats.Apply(prefabBody);
            foreach (Profile p in allProfiles)
            {
                S.Skill s = p.Skill; s.ApplyDefinition();
                if (p.Rapid != null) { S.Write(s.Definition, "attackSpeedBuffsRestockSpeed", C.Get(p.Rapid)); s.SetKeyword("KEYWORD_RAPID", C.Get(p.Rapid)); }
                if (p.Index == 0 || p.Index == 6) s.SetKeyword("KEYWORD_IGNITE", C.Get(p.Index == 0 ? boltBurn : flameBurn) > 0f);
                if (p.Index == 2 || p.Index == 7) s.SetKeyword("KEYWORD_STUNNING", true);
                if (p.Index == 3 || p.Index == 4) s.SetKeyword("KEYWORD_FREEZING", true);
                S.SetDescription(s, p.Index == 7 && expanded ? "3QS_SURGE_LIGHTNING_DESCRIPTION" : "3QS_ARTIFICER_" + p.Index + "_DESCRIPTION", () => Description(p));
            }
            if (profiles.Values.Any(p => p.Rapid != null)) S.SetText("KEYWORD_RAPID", () => "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
        }
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string damage = S.DamageText(C.Get(s.Damage), 0f, false); string text;
            switch (p.Index)
            {
                case 0: case 1:
                    text = (C.Get(p.Rapid) ? S.Utility("Rapid. ") : "") + (p.Index == 0 && C.Get(boltBurn) > 0f ? S.Damage("Ignite. ") : "") + "Fire a bolt for " + damage + ". Hold up to " + S.Utility(s.Definition.baseMaxStock.ToString()) + "."; break;
                case 2: case 3:
                    text = (p.Index == 2 ? S.Utility("Stunning. ") + "Charge up an exploding nano-bomb" : S.Utility("Freezing. ") + "Charge up a piercing nano-spear") + " that deals " + S.Damage(S.Number(C.Get(p.Minimum)) + "%-" + S.Number(C.Get(s.Damage)) + "% damage") + "." + (C.Get(p.FloatWhileCharging) ? " Hover while charging." : ""); break;
                case 4: text = S.Utility("Freezing. ") + "Create a barrier that hurts enemies for " + damage + "."; break;
                case 5: text = "Fire " + S.Utility(C.Get(shards).ToString()) + " shards for " + damage + " each."; break;
                case 6:
                    text = (C.Get(flameBurn) > 0f ? S.Damage("Ignite. ") : "") + "Burn all enemies in front of you, dealing " + S.DamageText(C.Get(s.Damage), FlameInterval(p), true) + " for " + S.Utility(S.Number(C.Get(s.Duration)) + " seconds") + "." + (C.Get(cancelFlames) ? " Press again to cancel." : ""); break;
                default: text = S.Utility("Stunning. ") + "Burst into the sky, dealing " + damage + "." +
                    (C.Get(fallImmunityTime) > 0f ? " Your first landing within " + S.Utility(S.Number(C.Get(fallImmunityTime)) + " seconds") + " ignores fall damage." : ""); break;
            }
            return s.AgileText + text;
        }
        private static float FlameInterval(Profile p) => C.Get(flameTicks) <= 1 ? C.Get(p.Skill.Duration) : C.Get(p.Skill.Duration) / (C.Get(flameTicks) - 1);
        private static string Extra(Profile p)
        {
            var b = new StringBuilder(); Action<string, string> add = (key, value) => b.Append("\n").Append(key).Append(": ").Append(S.Utility(value));
            S.Skill s = p.Skill;
            if (s.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (s.Radius != null) add("AOE Radius", S.Number(C.Get(s.Radius)) + "m");
            if (p.ChargeTime != null) add("Charge Time", S.Number(C.Get(p.ChargeTime)) + "s");
            if (p.Index == 0) add("Burn Chance", S.Number(C.Get(boltBurn)) + "%");
            if (p.Index == 2) { add("Bolt Damage", S.Number(C.Get(boltDamage)) + "% of Nano-Bomb damage"); add("Bolt Proc Coefficient", S.Number(C.Get(boltProc))); }
            if (p.Index == 4 && wallDuration != null) add("AOE Duration", C.Get(wallDuration) < 0f ? "Until destroyed" : S.Number(C.Get(wallDuration)) + "s");
            if (p.Index == 5) add("Projectile Lifetime", S.Number(C.Get(shardLifetime)) + "s");
            if (p.Index == 6) { add("Range", S.Number(C.Get(flameRange)) + "m"); add("Damage Interval", S.Number(FlameInterval(p)) + "s"); add("Burn Chance", S.Number(C.Get(flameBurn)) + "%"); }
            if (p.Index == 7) { add("Launch Velocity", S.Number(C.Get(launchVelocity)) + "m/s"); add("Launch Velocity Scale", C.Get(launchScale) + "%"); }
            return b.ToString();
        }
        private static void AfterBodyAwake(CharacterBody __instance) { if (IsArtificer(__instance) && stats != null) stats.Apply(__instance); }
        private static void Set(Scope scope, object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name); if (field == null) return;
            if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
            S.Write(instance, name, value);
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState }; activeState = null;
            Profile p = ForState(__instance); if (p == null || !IsArtificer(Body(__instance))) return;
            activeState = __instance; S.Skill s = p.Skill;
            if (__instance.GetType().Name.StartsWith("Charge", StringComparison.Ordinal)) Set(__state, __instance, "baseDuration", C.Get(p.ChargeTime));
            else if (p.Minimum != null) { Set(__state, __instance, "minDamageCoefficient", C.Get(p.Minimum) / 100f); Set(__state, __instance, "maxDamageCoefficient", C.Get(s.Damage) / 100f); }
            else if (p.Index <= 5) Set(__state, __instance, "damageCoefficient", C.Get(s.Damage) / 100f);
            if (p.Index == 5) Set(__state, __instance, "maxProjectiles", C.Get(shards));
            if (p.Index == 6)
            {
                float duration = C.Get(s.Duration), frequency = Math.Max(1, C.Get(flameTicks) - 1) / duration;
                Set(__state, __instance, "baseFlamethrowerDuration", duration); Set(__state, __instance, "tickFrequency", frequency);
                Set(__state, __instance, "totalDamageCoefficient", C.Get(s.Damage) / 100f * Math.Max(1, (int)Math.Ceiling(duration * frequency)));
                Set(__state, __instance, "maxDistance", C.Get(flameRange)); Set(__state, __instance, "ignitePercentChance", C.Get(flameBurn));
                Set(__state, __instance, "radius", C.Get(s.Radius));
                Set(__state, __instance, "procCoefficientPerTick", C.Get(s.Proc)); S.Write(__instance, "shouldReturnToMain", false);
            }
            if (p.Index == 7) { Set(__state, __instance, "duration", C.Get(s.Duration)); Set(__state, __instance, "blastAttackDamageCoefficient", C.Get(s.Damage) / 100f); Set(__state, __instance, "blastAttackRadius", C.Get(s.Radius)); Set(__state, __instance, "blastAttackProcCoefficient", C.Get(s.Proc)); }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value);
            activeState = __state.Previous; return __exception;
        }
        private static void AfterEnter(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (!IsArtificer(body)) return;
            if (p?.Index == 7) { ArmLandingProtection(body); return; }
            if (p?.Index != 6) return;
            S.Write(__instance, "shouldReturnToMain", false);
            S.Write(__instance, "tickDamageCoefficient", C.Get(p.Skill.Damage) / 100f);
            flameClocks.Remove(__instance); flameClocks.Add(__instance, new FlameClock());
        }
        private static bool BeforeFlameShot(EntityState __instance) => manualFlameTick || ForState(__instance)?.Index != 6 || !IsArtificer(Body(__instance));
        private static bool IsFloatingWhileCharging(CharacterBody body)
        {
            if (!IsArtificer(body)) return false;
            foreach (EntityStateMachine machine in body.GetComponents<EntityStateMachine>())
            {
                EntityState state = machine.state;
                Profile p = ForState(state);
                if (p?.FloatWhileCharging != null && C.Get(p.FloatWhileCharging) && state.GetType().Name.StartsWith("Charge", StringComparison.Ordinal)) return true;
            }
            return false;
        }
        private static void AfterChargeVelocity(object __instance, ref Vector3 __0)
        {
            CharacterBody body = S.Read(__instance, "body") as CharacterBody;
            if (!IsFloatingWhileCharging(body) || S.Read(__instance, "isGrounded") is bool grounded && grounded) return;
            __0.y = 0f;
            // Clear the motor's stored fall speed as well as the movement for this frame.
            if (S.Read(__instance, "velocity") is Vector3 velocity) { velocity.y = 0f; S.Write(__instance, "velocity", velocity); }
        }
        private static void AfterTick(EntityState __instance)
        {
            Profile p = ForState(__instance); CharacterBody body = Body(__instance);
            if (p?.Index != 6 || !IsArtificer(body) || !(S.Read(__instance, "isAuthority") is bool authority) || !authority) return;
            FlameClock clock = flameClocks.GetValue(__instance, _ => new FlameClock());
            InputBankTest.ButtonState key = body.inputBank.skill4;
            if (!key.down) clock.Released = true;
            if (C.Get(cancelFlames) && clock.Released && key.justPressed) { __instance.outer.SetNextStateToMain(); return; }
            if (!(S.Read(__instance, "hasBegunFlamethrower") is bool begun) || !begun) return;
            if (!clock.VisualsAdjusted)
            {
                foreach (string field in new[] { "leftFlamethrowerEffectInstance", "rightFlamethrowerEffectInstance" })
                    if (S.Read(__instance, field) is GameObject effect)
                    { Vector3 scale = effect.transform.localScale; scale.z *= C.Get(flameRange) / 16f; effect.transform.localScale = scale; }
                clock.VisualsAdjusted = true;
            }
            float elapsed = Math.Max(0f, S.Value(__instance, "stopwatch") - S.Value(__instance, "entryDuration"));
            int ticks = C.Get(flameTicks); float duration = C.Get(p.Skill.Duration);
            float rate = (ticks - 1) / duration * body.attackSpeed;
            int maximum = ticks == 1 ? 1 : 1 + (int)Math.Floor((ticks - 1) * body.attackSpeed + 0.0001f);
            int due = Math.Min(maximum, 1 + (int)Math.Floor(Math.Min(elapsed, duration) * rate + 0.0001f));
            bool previous = manualFlameTick; manualFlameTick = true;
            try { while (clock.Fired < due) { AccessTools.Method(__instance.GetType(), "FireGauntlet").Invoke(__instance, new object[] { "MuzzleCenter" }); clock.Fired++; } }
            finally { manualFlameTick = previous; }
            if (elapsed >= duration) __instance.outer.SetNextStateToMain();
        }
        private static void AfterCanExecute(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (!definitions.TryGetValue(__instance, out Profile p) || p.Index != 6) return;
            if (skillSlot.GetComponents<EntityStateMachine>().Any(m => ForState(m.state)?.Index == 6)) __result = false;
        }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            CharacterBody body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            if (!IsArtificer(body)) return;
            GameObject prefab = fireProjectileInfo.projectilePrefab;
            Profile p = profiles.Values.FirstOrDefault(v => v.Projectile && (v.Projectile == prefab || v.Children.Contains(prefab)));
            if (p == null) return;
            if (p.Index == 4 && NetworkServer.active && maxWalls != null)
            {
                WallGroup group = parentWall;
                if (group == null && ForState(activeState)?.Index == 4) group = wallCasts.GetValue(activeState, _ => new WallGroup());
                if (group != null) pendingWalls.Add(new PendingWall { Owner = fireProjectileInfo.owner, Prefab = prefab, Position = fireProjectileInfo.position, Group = group });
            }
            if (p.Projectile == prefab && p.Skill.ProjectileSpeed != null && prefab.GetComponent<ProjectileSimple>()) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
            if (p.Index == 0)
            {
                DamageTypeCombo type = fireProjectileInfo.damageTypeOverride ?? fireProjectileInfo.projectilePrefab.GetComponent<ProjectileDamage>().damageType;
                type.damageType &= ~DamageType.IgniteOnHit;
                if (Util.CheckRoll(C.Get(boltBurn), body.master)) type.damageType |= DamageType.IgniteOnHit;
                fireProjectileInfo.damageTypeOverride = type;
            }
        }
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            if (__instance.GetComponent<ProjectileMarker>()) return;
            CharacterBody body = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null; if (!IsArtificer(body)) return;
            Profile p = profiles.Values.FirstOrDefault(v => v.Projectile && (v.Projectile.GetComponent<ProjectileController>().catalogIndex == __instance.catalogIndex || v.Children.Any(child => child.GetComponent<ProjectileController>() && child.GetComponent<ProjectileController>().catalogIndex == __instance.catalogIndex))); if (p == null) return;
            var marker = __instance.GetComponent<ProjectileMarker>() ?? __instance.gameObject.AddComponent<ProjectileMarker>(); marker.Profile = p; marker.Origin = __instance.transform.position;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            var characterMover = __instance.GetComponent<ProjectileCharacterController>();
            if (characterMover && p.Skill.ProjectileSpeed != null && p.Projectile.GetComponent<ProjectileController>().catalogIndex == __instance.catalogIndex) characterMover.velocity = C.Get(p.Skill.ProjectileSpeed);
            if (p.Index == 4)
            {
                if (wallDuration != null)
                    foreach (DestroyOnTimer timer in __instance.GetComponentsInChildren<DestroyOnTimer>(true))
                    { timer.enabled = C.Get(wallDuration) >= 0f; if (timer.enabled) timer.duration = C.Get(wallDuration); }
                if (NetworkServer.active && maxWalls != null) TrackWall(__instance, marker);
            }
            ProjectileExplosion explosion = __instance.GetComponent<ProjectileExplosion>();
            if (explosion) { if (p.Skill.Radius != null) explosion.blastRadius = C.Get(p.Skill.Radius); if (p.Skill.Area != null) explosion.falloffModel = Area(C.Get(p.Skill.Area)); }
            if (p.Index == 2)
            {
                ProjectileProximityBeamController beam = __instance.GetComponent<ProjectileProximityBeamController>();
                if (beam) { beam.damageCoefficient = C.Get(boltDamage) / 100f; beam.procCoefficient = C.Get(boltProc); }
                Rigidbody rigidbody = __instance.GetComponent<Rigidbody>(); if (rigidbody) rigidbody.useGravity = C.Get(gravity);
            }
            if (p.Index == 5)
            {
                float lifetime = C.Get(shardLifetime); var simple = __instance.GetComponent<ProjectileSimple>(); if (simple) simple.lifetime = lifetime;
                var mover = __instance.GetComponent<ProjectileCharacterController>(); if (mover) { mover.lifetime = lifetime; mover.velocity = C.Get(p.Skill.ProjectileSpeed); }
                var impact = __instance.GetComponent<ProjectileImpactExplosion>(); if (impact) impact.lifetime = lifetime;
            }
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => BeforeProjectileStart(projectileController);
        private static void BeforeWallChildren(Component __instance, out WallGroup __state)
        { __state = parentWall; parentWall = __instance.GetComponent<ProjectileMarker>()?.Wall; }
        private static Exception AfterWallChildren(WallGroup __state, Exception __exception) { parentWall = __state; return __exception; }
        private static void TrackWall(ProjectileController controller, ProjectileMarker marker)
        {
            WallTracker tracker = walls.GetValue(controller.owner, _ => new WallTracker());
            PendingWall pending = pendingWalls.FirstOrDefault(p => p.Owner == controller.owner && p.Prefab.GetComponent<ProjectileController>().catalogIndex == controller.catalogIndex && Vector3.Distance(p.Position, controller.transform.position) < 0.1f);
            WallGroup group = pending?.Group ?? parentWall; if (pending != null) pendingWalls.Remove(pending);
            bool root = marker.Profile.Projectile.GetComponent<ProjectileController>().catalogIndex == controller.catalogIndex;
            if (group == null)
            {
                // Remote casts arrive as the two native barrier walkers, in order.
                if (root && (tracker.Incoming == null || tracker.Incoming.Roots >= 2)) tracker.Incoming = new WallGroup();
                group = root ? tracker.Incoming : tracker.Groups.LastOrDefault() ?? new WallGroup();
            }
            if (root) group.Roots++;
            marker.Wall = group; group.Objects.Add(controller.gameObject);
            tracker.Groups.RemoveAll(g => g != group && g.Roots >= 2 && g.Objects.All(o => !o));
            if (!tracker.Groups.Contains(group)) tracker.Groups.Add(group);
            int maximum = C.Get(maxWalls);
            while (maximum >= 0 && tracker.Groups.Count > maximum)
            {
                WallGroup oldest = tracker.Groups[0]; tracker.Groups.RemoveAt(0);
                foreach (GameObject instance in oldest.Objects) if (instance) NetworkServer.Destroy(instance);
            }
        }
        private static void BeforeBullet(BulletAttack __instance)
        {
            Profile p = ForState(activeState);
            if (p?.Index != 6 || !__instance.owner || Body(activeState)?.gameObject != __instance.owner) return;
            __instance.procCoefficient = C.Get(p.Skill.Proc);
            __instance.falloffModel = C.Get(p.Skill.Falloff) == S.BulletFalloff.Default ? BulletAttack.FalloffModel.DefaultBullet : C.Get(p.Skill.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.None;
        }
        private static void BeforeBlast(BlastAttack __instance)
        {
            Profile p = ForState(activeState);
            if (p?.Index != 7 || !__instance.attacker || Body(activeState)?.gameObject != __instance.attacker) return;
            __instance.falloffModel = Area(C.Get(p.Skill.Area));
        }
        private static void BeforeProjectileDamage(DamageInfo damageInfo)
        {
            if (!damageInfo.inflictor || damageInfo.dotIndex != DotController.DotIndex.None) return;
            ProjectileMarker marker = damageInfo.inflictor.GetComponent<ProjectileMarker>(); if (!marker) return;
            float distance = Vector3.Distance(marker.Origin, damageInfo.position);
            S.BulletFalloff falloff = C.Get(marker.Profile.Skill.Falloff);
            if (falloff == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f;
            else if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.25f + Mathf.Clamp01(Mathf.InverseLerp(25f, 7f, distance)) * 0.75f;
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static float SurgeMoveSpeed(float original, EntityState state) => ForState(state)?.Index == 7 && IsArtificer(Body(state)) ? SurgeMoveSpeed(Body(state)) : original;
        internal static float SurgeMoveSpeed(CharacterBody body)
        {
            float baseline = body.baseMoveSpeed + body.levelMoveSpeed * Math.Max(0f, body.level - 1f);
            if (body.isSprinting) baseline *= body.sprintingSpeedMultiplier;
            float scaled = baseline + (body.moveSpeed - baseline) * C.Get(launchScale) / 100f;
            return surgePeak <= 0f ? 0f : Math.Max(0f,
                C.Get(launchVelocity) / surgePeak * scaled / Math.Max(0.001f, nativeMoveSpeed));
        }
        private static IEnumerable<CodeInstruction> SurgeMovementIL(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (code.opcode == OpCodes.Ldfld && code.operand is FieldInfo field && field.Name == "moveSpeedStat" ||
                    (code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) && code.operand is MethodInfo method && method.Name == "get_moveSpeedStat")
                { yield return new CodeInstruction(OpCodes.Ldarg_0); yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Artificer), nameof(SurgeMoveSpeed), new[] { typeof(float), typeof(EntityState) })); }
            }
        }
        internal static float SurgeDuration => profiles.TryGetValue(7, out Profile p) ? C.Get(p.Skill.Duration) : 0.3f;
        internal static float SurgeCurve(float fraction) => surgeCurve == null ? 0f : surgeCurve.Evaluate(fraction);
        internal static object SurgeAsset(string field) => S.Read(surgeSample, field);
        internal static void SurgeBlast(CharacterBody body)
        {
            if (!NetworkServer.active || !profiles.TryGetValue(7, out Profile p)) return;
            new BlastAttack { attacker = body.gameObject, position = body.corePosition, teamIndex = body.teamComponent.teamIndex, baseDamage = body.damage * C.Get(p.Skill.Damage) / 100f,
                radius = C.Get(p.Skill.Radius), procCoefficient = C.Get(p.Skill.Proc), falloffModel = Area(C.Get(p.Skill.Area)), baseForce = S.Value(surgeSample, "blastAttackForce"),
                crit = Util.CheckRoll(body.crit, body.master), attackerFiltering = AttackerFiltering.NeverHitSelf, damageType = new DamageTypeCombo(DamageType.Stun1s, DamageTypeExtended.Electrical, DamageSource.Special) }.Fire();
        }
        internal static void NotifyPassive(GameObject body)
        {
            if (passiveType == null || passiveCast == null) return;
            if (AccessTools.Field(passiveType, "instanceLookup")?.GetValue(null) is IDictionary lookup && lookup.Contains(body))
                passiveCast.Invoke(lookup[body], passiveCast.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray());
        }
        internal static void RestoreHover()
        {
            if (Chainloader.PluginInfos.TryGetValue("com.RiskOfBrainrot.JetHack", out var plugin))
                AccessTools.Field(plugin.Instance.GetType(), "hoverStateCache")?.SetValue(null, true);
        }
        internal static void ArmLandingProtection(CharacterBody body)
        {
            if (!IsArtificer(body) || !profiles.ContainsKey(7)) return;
            landingProtection.Remove(body);
            float duration = C.Get(fallImmunityTime);
            if (duration > 0f) landingProtection.Add(body, new LandingProtection { ExpiresAt = Time.fixedTime + duration });
        }
        private static void BeforeLanding(CharacterBody __0, out CharacterBody __state)
        {
            __state = protectedLanding;
            protectedLanding = null;
            if (!__0 || !landingProtection.TryGetValue(__0, out LandingProtection protection)) return;
            landingProtection.Remove(__0);
            if (Time.fixedTime <= protection.ExpiresAt) protectedLanding = __0;
        }
        private static void AfterFallImmunity(CharacterBody __0, ref bool __result)
        { if (__0 && __0 == protectedLanding) __result = true; }
        private static Exception AfterLanding(CharacterBody __state, Exception __exception)
        { protectedLanding = __state; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (__result && definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll) __result = skillSlot.stock >= skillSlot.maxStock; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out int? __state) { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = __instance.stockToConsume; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, int? __state, Exception __exception) { if (__state.HasValue) __instance.stockToConsume = __state.Value; return __exception; }
    }

    // GenericCharacterMain leaves the Jet state machine (and Extended's passive) running.
    public sealed class VoltaicSurgeState : GenericCharacterMain
    {
        public override void OnEnter()
        {
            base.OnEnter();
            Util.PlaySound(Artificer.SurgeAsset("beginSoundString") as string, gameObject);
            PlayCrossfade("Body", "FlyUp", "FlyUp.playbackRate", Artificer.SurgeDuration, 0.1f);
            object motor = S.Read(characterMotor, "Motor");
            if (motor != null) AccessTools.Method(motor.GetType(), "ForceUnground")?.Invoke(motor, new object[] { 0.1f });
            S.Write(characterMotor, "velocity", Vector3.zero);
            Artificer.ArmLandingProtection(characterBody);
            if (Artificer.SurgeAsset("muzzleflashEffect") is GameObject muzzle)
            { EffectManager.SimpleMuzzleFlash(muzzle, gameObject, "MuzzleLeft", false); EffectManager.SimpleMuzzleFlash(muzzle, gameObject, "MuzzleRight", false); }
            if (Artificer.SurgeAsset("blinkPrefab") is GameObject blink)
                EffectManager.SpawnEffect(blink, new EffectData { origin = characterBody.corePosition, rotation = Util.QuaternionSafeLookRotation(Vector3.up) }, false);
            Artificer.SurgeBlast(characterBody); Artificer.NotifyPassive(gameObject);
        }
        public override void HandleMovements()
        {
            base.HandleMovements();
            if ((object)characterMotor == null) return;
            Vector3 root = S.Read(characterMotor, "rootMotion") is Vector3 motion ? motion : Vector3.zero;
            S.Write(characterMotor, "rootMotion", root + Vector3.up * (Artificer.SurgeMoveSpeed(characterBody) * Artificer.SurgeCurve(fixedAge / Artificer.SurgeDuration) * GetDeltaTime()));
            Vector3 velocity = S.Read(characterMotor, "velocity") is Vector3 v ? v : Vector3.zero; velocity.y = 0f; S.Write(characterMotor, "velocity", velocity);
        }
        public override void FixedUpdate() { base.FixedUpdate(); if (isAuthority && fixedAge >= Artificer.SurgeDuration) outer.SetNextStateToMain(); }
        public override void OnExit()
        {
            if (!outer.destroying) Util.PlaySound(Artificer.SurgeAsset("endSoundString") as string, gameObject);
            base.OnExit(); if (isAuthority) Artificer.RestoreHover();
        }
    }
}
