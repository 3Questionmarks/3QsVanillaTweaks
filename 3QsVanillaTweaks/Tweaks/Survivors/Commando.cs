using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    internal static class Commando
    {
        private const string Root = "Survivors - 01 Commando";
        private static readonly string[] Names = { "Double Tap", "Phase Round", "Phase Blast", "Tactical Dive", "Tactical Slide", "Suppressive Fire", "Frag Grenade", "Sabotage" };
        private static readonly string[] States = { "FirePistol2", "FireFMJ", "FireShotgunBlast", "DodgeState", "SlideState", "FireBarrage", "ThrowGrenade", "Sabotage" };
        private static readonly Dictionary<GameObject, S.Skill> projectileSkills = new Dictionary<GameObject, S.Skill>();
        private static readonly Dictionary<string, S.Skill> skills = new Dictionary<string, S.Skill>();
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static ConfigEntry<bool> enabled, statsEnabled, barrageScaling, sticky, canCook, localCook, selfDamage;
        private static ConfigEntry<float> pierceBonus, fuse, selfPercent, chainDistance, sabotageDuration;
        private static ConfigEntry<int> pellets, shots, explosiveCount, maxExplosives;
        private static ConfigEntry<string> primaryReload, secondaryReload;
        private static S.Stats stats;
        private static CharacterBody prefabBody;
        private static GameObject roundPrefab, grenadePrefab, sabotagePrefab;
        private static Harmony harmony;
        private static readonly HashSet<RuntimeMethodHandle> enterHooks = new HashSet<RuntimeMethodHandle>();
        [ThreadStatic] private static S.Skill activeBullet;
        [ThreadStatic] private static S.Skill activeBlast;
        private static readonly ConditionalWeakTable<object, Cooking> cooks = new ConditionalWeakTable<object, Cooking>();
        private static readonly List<ExplosiveLifetime> explosives = new List<ExplosiveLifetime>();

        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable Commando changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool("Survivor-Stats - 01 Commando", "Changes Enabled", true, "Enable Commando stat changes. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true,
                "Enable changes to " + name + ". Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            RoR2Application.onLoad += Load;
        }

        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Commando.CommandoWeapon." + name) ??
            typeof(EntityState).Assembly.GetType("EntityStates.Commando." + name);
        private static bool IsCommando(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static S.Skill Skill(object state)
        {
            if (state == null) return null;
            for (Type type = state.GetType(); type != null; type = type.BaseType)
                if (skills.TryGetValue(type.Name, out S.Skill result) && result.Loaded) return result;
            return null;
        }
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null)
        {
            MethodInfo target = AccessTools.Method(type, method);
            if (target == null) throw new MissingMethodException(type?.FullName, method);
            if (method == "OnEnter" && !enterHooks.Add(target.MethodHandle)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(Commando), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(Commando), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Commando), finalizer));
        }
        private static void Load()
        {
            try
            {
                prefabBody = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.Commando");
                if (statsEnabled.Value)
                {
                    stats = new S.Stats("Survivor-Stats - 01 Commando", prefabBody);
                    stats.Apply(prefabBody);
                    Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBodyAwake));
                }
                SkillDef[] definitions = prefabBody.GetComponents<GenericSkill>().Where(slot => slot.skillFamily)
                    .SelectMany(slot => slot.skillFamily.variants.Select(variant => variant.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    Type type = State(States[i]);
                    if (type == null) { ThreeQsVanillaTweaks.Log.Warning("Commando skill unavailable: " + Names[i]); continue; }
                    SkillDef definition = definitions.FirstOrDefault(d => d.activationState.stateType == type ||
                        type.IsAssignableFrom(d.activationState.stateType) || (i == 5 && d.activationState.stateType?.Name == "PrepBarrage"));
                    if (!definition) { ThreeQsVanillaTweaks.Log.Warning("Commando skill definition unavailable: " + Names[i]); continue; }
                    var skill = new S.Skill(Root + " - " + Names[i], definition, type, switches[Names[i]]);
                    // Sabotage's spread variant stores its own entity-state configuration.
                    if (i == 7 && definition.activationState.stateType != type)
                        skill = new S.Skill(skill.Section, definition, definition.activationState.stateType, switches[Names[i]]);
                    skills[States[i]] = skill;
                    BindSkill(skill, i);
                    skill.Loaded = true;
                    skill.ApplyDefinition();
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(skill.Definition, () => ExtraStats(skill),
                        skill.Proc == null ? (Func<float>)null : () => C.Get(skill.Proc));
                    Patch(type, "OnEnter", prefix: nameof(BeforeEnter), postfix: nameof(AfterEnter));
                }
                InstallHooks();
                C.EffectiveSettingsChanged += ApplySettings;
                ApplySettings();
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Commando changes could not be loaded: " + error); }
        }
        private static void BindSkill(S.Skill s, int index)
        {
            bool damaging = index != 3 && index != 4;
            var projectile = S.Read(s.Sample, "projectilePrefab") as GameObject;
            var pc = projectile ? projectile.GetComponent<ProjectileController>() : null;
            float nativeProc = pc ? pc.procCoefficient : 1f;
            float damageFactor = 1f;
            if (index == 1 && projectile && projectile.GetComponent<ProjectileOverlapAttack>() is ProjectileOverlapAttack overlap)
            { damageFactor = overlap.damageCoefficient; nativeProc *= overlap.overlapProcCoefficient; }
            if (index == 6 && projectile.GetComponent<ProjectileImpactExplosion>() is ProjectileImpactExplosion nativeExplosion)
            { damageFactor = nativeExplosion.blastDamageCoefficient; nativeProc *= nativeExplosion.blastProcCoefficient; }
            if (index == 7)
            {
                Type explosionType = typeof(EntityState).Assembly.GetType("EntityStates.Commando.StickyGrenade.StickyGrenadeExplodeState");
                object explosionState = EntityStateCatalog.InstantiateState(explosionType);
                damageFactor = S.Value(explosionState, "damageCoefficient", 1f);
                nativeProc *= S.Value(explosionState, "procCoefficient", 1f);
            }
            s.DamageFactor = damageFactor;
            s.Bind(index == 4 || index == 7 ? 3f : (float?)null, index == 7 ? 3 : (int?)null,
                index == 3 || index == 4 ? true : (bool?)null, index == 7, damaging,
                index == 2 || index == 5, index == 3 ? "duration" : index == 4 ? "slideDuration" : index == 5 ? "totalDuration" : null, false, nativeProc);
            if (index == 1)
            {
                roundPrefab = S.Read(s.Sample, "projectilePrefab") as GameObject;
                pierceBonus = S.Float(s.Section, "Damage Added Per Enemy Pierced (%)", 50f, 40f,
                    "Increase the shot's current damage for each enemy pierced.");
                BindProjectile(s, roundPrefab, false);
            }
            if (index == 2) pellets = S.Int(s.Section, "Pellets", 4, (int)S.Value(s.Sample, "bulletCount", 8), "Pellets fired in each blast.", 1);
            if (index == 3)
            {
                primaryReload = C.Bind(s.Section, "Reload Primary Stocks On Use", "100%", "Primary stocks restored on use. Use Vanilla, a percentage such as 100%, or a whole number. Vanilla: " + S.Number(S.Value(s.Sample, "primaryReloadStockCount")) + " stocks.");
                secondaryReload = C.Bind(s.Section, "Reload Secondary Stocks On Use", "Vanilla", "Secondary stocks restored on use. Use Vanilla, a percentage such as 100%, or a whole number. Vanilla: Unchanged.");
            }
            if (index == 5)
            {
                shots = S.Int(s.Section, "Base Shots", 8, (int)S.Value(s.Sample, "baseBulletCount", 6), "Shots fired before attack speed is applied.", 1);
                barrageScaling = S.Bool(s.Section, "Shots Multiplied By Attack Speed", true, true, "Multiply the number of shots by attack speed.");
            }
            if (index == 6)
            {
                grenadePrefab = S.Read(s.Sample, "projectilePrefab") as GameObject;
                BindProjectile(s, grenadePrefab, true);
                var explosion = grenadePrefab.GetComponent<ProjectileImpactExplosion>();
                sticky = S.Bool(s.Section, "Sticky", false, false, "Stick to the first surface or enemy hit instead of bouncing.");
                fuse = S.Float(s.Section, "Explode Timer", 2f, explosion.lifetimeAfterImpact, "Seconds until detonation after the first impact, or after release when cooked.", 0.001f);
                canCook = S.Bool(s.Section, "Can Cook", true, false, "Allow grenades to be cooked by holding the skill button.");
                localCook = C.BindBool(s.Section, "Cook Grenades (Client)", true, "Cook grenades by holding the skill button. Always disabled when Can Cook is false. Vanilla: False.", true);
                selfDamage = S.Bool(s.Section, "Overcook Self Damage", false, false, "Take damage when a grenade detonates in your hand.");
                selfPercent = C.Bind(s.Section, "Overcook Self Damage (%)", 20f, new ConfigDescription("Percentage of the grenade's damage dealt to yourself when overcooked. Vanilla: No self damage.", new AcceptableValueRange<float>(0f, 100f)));
            }
            if (index == 7)
            {
                sabotagePrefab = S.Read(s.Sample, "projectilePrefab") as GameObject;
                BindProjectile(s, sabotagePrefab, true);
                float nativeInterval = S.Value(s.Sample, "baseDuration", 1f) * S.Value(s.Sample, "fireTime", 1f) / Math.Max(1f, S.Value(s.Sample, "projectileCount", 3));
                s.Interval = S.Float(s.Section, "Damage Interval", nativeInterval, nativeInterval, "Seconds between explosives before attack speed is applied.", 0.001f);
                explosiveCount = S.Int(s.Section, "Explosives Thrown", 1, (int)S.Value(s.Sample, "projectileCount", 3), "Explosives thrown per use.", 1);
                Type explode = typeof(EntityState).Assembly.GetType("EntityStates.Commando.StickyGrenade.StickyGrenadeExplodeState");
                object sample = EntityStateCatalog.InstantiateState(explode);
                float radius = S.Value(sample, "blastRadius", 8.25f);
                s.Radius = S.Float(s.Section, "AOE Radius", radius, radius, "Explosion radius in metres.");
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre of the explosion. Vanilla: None.");
                chainDistance = S.Float(s.Section, "Chain Distance", 10.25f, radius + 2f, "Distance within which an explosion triggers other players' Sabotage explosives.");
                maxExplosives = S.Int(s.Section, "Maximum Explosives", -1, -1, "Maximum active explosives per player. The oldest explodes when exceeded. -1 is unlimited; 0 detonates after arming.", -1);
                Type stuck = typeof(EntityState).Assembly.GetType("EntityStates.Commando.StickyGrenade.StickyGrenadeStuckState");
                float native = S.Value(EntityStateCatalog.InstantiateState(stuck), "timeBeforeDespawn", 8f);
                sabotageDuration = S.Float(s.Section, "Duration", 10f, native, "Seconds before an undetonated explosive explodes.", 0f);
            }
        }
        private static void BindProjectile(S.Skill s, GameObject prefab, bool area)
        {
            if (!prefab) throw new InvalidOperationException("Missing projectile for " + s.Section);
            s.BindProjectileSpeed(prefab, s.StateType.Name == "FireFMJ" ? 1.5f : 1f);
            projectileSkills[prefab] = s;
            if (area && prefab.GetComponent<ProjectileImpactExplosion>() is ProjectileImpactExplosion explosion)
            {
                s.Radius = S.Float(s.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
                S.AreaFalloff native = (S.AreaFalloff)Enum.Parse(typeof(S.AreaFalloff), explosion.falloffModel.ToString());
                s.Area = C.Bind(s.Section, "AOE Damage Falloff", native, "Damage falloff from the centre of the explosion. Vanilla: " + native + ".");
            }
        }
        private static void InstallHooks()
        {
            if (skills.Count > 0)
            {
                Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequiredStock));
                Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRechargeStock));
                Patch(typeof(SkillDef), "OnExecute", prefix: nameof(BeforeExecuteStocks), finalizer: nameof(AfterExecuteStocks));
            }
            bool bullets = skills.ContainsKey("FirePistol2") || skills.ContainsKey("FireShotgunBlast") || skills.ContainsKey("FireBarrage");
            if (bullets)
            {
                Patch(typeof(BulletAttack), "Fire", prefix: nameof(BeforeBullet));
                if (skills.ContainsKey("FirePistol2")) Patch(State("FirePistol2"), "FireBullet", nameof(BeforeBulletScope), finalizer: nameof(EndBulletScope));
                if (skills.ContainsKey("FireBarrage")) Patch(State("FireBarrage"), "FireBullet", nameof(BeforeBulletScope), finalizer: nameof(EndBulletScope));
                if (skills.ContainsKey("FireShotgunBlast")) Patch(typeof(GenericBulletBaseState), "FireBullet", nameof(BeforeBulletScope), finalizer: nameof(EndBulletScope));
            }
            if (skills.ContainsKey("FireFMJ") || skills.ContainsKey("ThrowGrenade") || skills.ContainsKey("Sabotage"))
            {
                MethodInfo launch = AccessTools.Method(typeof(ProjectileManager), "FireProjectile", new[] { typeof(FireProjectileInfo) });
                if (launch == null) throw new MissingMethodException("ProjectileManager.FireProjectile(FireProjectileInfo)");
                harmony.Patch(launch, prefix: new HarmonyMethod(typeof(Commando), nameof(BeforeProjectileLaunch)));
                Patch(typeof(ProjectileController), "Start", prefix: nameof(BeforeProjectileStart));
                Patch(typeof(HealthComponent), "TakeDamage", prefix: nameof(BeforeProjectileDamage));
            }
            if (skills.ContainsKey("FireFMJ")) Patch(typeof(ProjectileOverlapAttack), "AddToDamageCoefficient", nameof(BeforePierceIncrease));
            if (skills.ContainsKey("ThrowGrenade"))
            {
                Patch(typeof(GenericProjectileBaseState), "FixedUpdate", nameof(BeforeProjectileUpdate));
                MethodInfo animation = AccessTools.Method(State("ThrowGrenade"), "PlayAnimation", new[] { typeof(float) });
                if (animation == null) throw new MissingMethodException("ThrowGrenade.PlayAnimation(float)");
                harmony.Patch(animation, prefix: new HarmonyMethod(typeof(Commando), nameof(BeforeGrenadeAnimation)));
                Patch(State("ThrowGrenade"), "ModifyProjectileInfo", postfix: nameof(AfterGrenadeInfo));
                Patch(typeof(ProjectileImpactExplosion), "OnProjectileImpact", postfix: nameof(AfterGrenadeImpact));
            }
            if (skills.ContainsKey("Sabotage"))
            {
                Type explode = typeof(EntityState).Assembly.GetType("EntityStates.Commando.StickyGrenade.StickyGrenadeExplodeState");
                Patch(explode, "Fire", nameof(BeforeBlastScope), finalizer: nameof(EndBlastScope));
                Type controller = typeof(ProjectileController).Assembly.GetType("HG.SabotageProjectileController");
                Patch(controller, "KillAllProjectilesOfComboAndAttacker", nameof(BeforeChain));
                Patch(typeof(BlastAttack), "Fire", prefix: nameof(BeforeBlast));
                Type stuck = typeof(EntityState).Assembly.GetType("EntityStates.Commando.StickyGrenade.StickyGrenadeStuckState");
                Patch(stuck, "FixedUpdate", nameof(BeforeStuckUpdate));
            }
        }
        private static S.Skill Configured(SkillDef definition) => skills.Values.FirstOrDefault(s => s.Loaded && s.Definition == definition);
        private static void AfterRequiredStock(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { S.Skill s = Configured(__instance); if (s != null && s.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void AfterRechargeStock(SkillDef __instance, GenericSkill skillSlot, ref int __result)
        { S.Skill s = Configured(__instance); if (s != null && s.RechargeAll) __result = skillSlot.maxStock; }
        private sealed class StockPayment { internal int Original; }
        private static void BeforeExecuteStocks(SkillDef __instance, GenericSkill skillSlot, out StockPayment __state)
        {
            __state = null;
            S.Skill s = Configured(__instance);
            if (s == null || !s.ConsumeAll) return;
            __state = new StockPayment { Original = __instance.stockToConsume };
            __instance.stockToConsume = skillSlot.stock;
        }
        private static Exception AfterExecuteStocks(SkillDef __instance, StockPayment __state, Exception __exception)
        { if (__state != null) __instance.stockToConsume = __state.Original; return __exception; }
        private static void ApplySettings()
        {
            if (stats != null && prefabBody)
            {
                stats.Apply(prefabBody);
                foreach (CharacterBody body in UnityEngine.Object.FindObjectsOfType<CharacterBody>())
                    if (IsCommando(body)) { stats.Apply(body); body.RecalculateStats(); }
            }
            foreach (S.Skill s in skills.Values)
            {
                s.ApplyDefinition();
                string token = "3QS_COMMANDO_" + s.StateType.Name.ToUpperInvariant() + "_DESCRIPTION";
                S.SetDescription(s, token, () => Description(s));
                s.Definition.skillDescriptionToken = token;
            }
        }
        private static string Description(S.Skill s)
        {
            string damage = s.Damage == null ? "" : S.DamageText(C.Get(s.Damage), s.Interval == null ? 0f : C.Get(s.Interval), false);
            string text;
            switch (s.StateType.Name)
            {
                case "FirePistol2": text = "Shoot for " + damage + "."; break;
                case "FireFMJ": text = "Fire a " + S.Damage("piercing") + " bullet for " + damage + ". Each enemy pierced increases its current damage by " + S.Damage(S.Number(C.Get(pierceBonus)) + "%") + "."; break;
                case "FireShotgunBlast": text = "Fire two close-range blasts of " + S.Damage(C.Get(pellets).ToString()) + " pellets for " + damage + " per pellet."; break;
                case "DodgeState": text = S.Utility("Roll") + " a short distance." + ReloadDescription("primary", C.Get(primaryReload)) + ReloadDescription("secondary", C.Get(secondaryReload)); break;
                case "SlideState": text = S.Utility("Slide") + " a short distance. You can " + S.Utility("fire while sliding") + "."; break;
                case "FireBarrage": text = "Fire " + S.Damage(C.Get(shots).ToString()) + " shots for " + damage + " each, " + S.Damage("stunning") + " enemies." + (C.Get(barrageScaling) ? " Fire more shots with increased " + S.Utility("attack speed") + "." : ""); break;
                case "ThrowGrenade": text = "Throw a " + S.Utility(C.Get(sticky) ? "sticky" : "bouncing") + " grenade for " + damage + ". Detonates after impact." + (C.Get(canCook) && localCook.Value ? " Hold to " + S.Utility("cook") + ". Cooking reduces the fuse; holding too long detonates it in your hand." : ""); break;
                default: text = "Throw " + S.Damage(C.Get(explosiveCount).ToString()) + " explosives for " + damage + ". Shoot them to detonate, triggering nearby Sabotage explosives. Undetonated explosives detonate automatically."; break;
            }
            return s.AgileText + text;
        }
        private static string ExtraStats(S.Skill s)
        {
            var text = new System.Text.StringBuilder();
            Action<string, string> add = (label, value) => text.Append("\n").Append(label).Append(": ").Append(S.Utility(value));
            if (s.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(s.ProjectileSpeed)) + "m/s");
            if (s.Radius != null) add("Explosion Radius", S.Number(C.Get(s.Radius)) + "m");
            if (s.Duration != null) add("Duration", S.Number(C.Get(s.Duration)) + "s");
            if (s.Periodic && s.Interval != null) add("Damage Interval", S.Number(C.Get(s.Interval)) + "s / Attack Speed");
            if (s.StateType.Name == "ThrowGrenade")
            {
                add("Grenade Fuse", S.Number(C.Get(fuse)) + "s");
                if (C.Get(canCook) && localCook.Value) add("Cooking Delay", "0.5s");
                if (C.Get(canCook) && C.Get(selfDamage)) add("Overcook Self Damage", S.Number(C.Get(selfPercent)) + "%");
            }
            if (s.StateType.Name == "Sabotage" || s.StateType.Name == "SabotageSpread")
            {
                add("Chain Distance", S.Number(C.Get(chainDistance)) + "m");
                add("Explosive Duration", S.Number(C.Get(sabotageDuration)) + "s");
                add("Maximum Explosives", C.Get(maxExplosives) < 0 ? "Unlimited" : C.Get(maxExplosives).ToString());
            }
            return text.ToString();
        }
        private static string ReloadDescription(string slot, string value) => string.Equals(value?.Trim(), "Vanilla", StringComparison.OrdinalIgnoreCase) ? "" : " Restore " + S.Utility(value + " " + slot + " stocks") + ".";
        private static void AfterBodyAwake(CharacterBody __instance) { if (stats != null && IsCommando(__instance)) stats.Apply(__instance); }
        private static void BeforeEnter(object __instance)
        {
            S.Skill s = Skill(__instance); if (s == null) return;
            if (s.StateType.Name == "DodgeState" && __instance is EntityState dive && dive.outer)
            {
                CharacterBody body = dive.outer.GetComponent<CharacterBody>();
                if (body && body.skillLocator.primary) diveStocks.GetValue(__instance, key => new DiveStock()).Primary = body.skillLocator.primary.stock;
            }
            if (s.StateType.Name == "ThrowGrenade") cooks.Remove(__instance);
            if (s.Damage != null) S.Write(__instance, "damageCoefficient", C.Get(s.Damage) / 100f / Math.Max(0.00001f, s.DamageFactor));
            if (s.Proc != null) S.Write(__instance, "procCoefficient", C.Get(s.Proc));
            switch (s.StateType.Name)
            {
                case "FireShotgunBlast":
                    S.Write(__instance, "bulletCount", C.Get(pellets)); S.Write(__instance, "delayBetweenShotgunBlasts", C.Get(s.Interval));
                    S.Write(__instance, "baseDuration", Math.Max(S.Value(s.Sample, "baseDuration"), C.Get(s.Interval) + 0.05f)); break;
                case "DodgeState": S.Write(__instance, "duration", C.Get(s.Duration)); break;
                case "SlideState":
                    S.Write(__instance, "slideDuration", C.Get(s.Duration));
                    S.Write(__instance, "jumpDuration", C.Get(s.Duration) * s.AirDurationRatio); break;
                case "FireBarrage": S.Write(__instance, "baseBulletCount", C.Get(shots)); S.Write(__instance, "baseDurationBetweenShots", C.Get(s.Interval)); S.Write(__instance, "totalDuration", C.Get(s.Duration)); break;
                case "Sabotage": case "SabotageSpread": S.Write(__instance, "projectileCount", C.Get(explosiveCount)); break;
            }
        }
        private static void AfterEnter(EntityState __instance)
        {
            S.Skill s = Skill(__instance); if (s == null || !__instance.outer) return;
            CharacterBody body = __instance.outer.GetComponent<CharacterBody>();
            if (s.StateType.Name == "FireBarrage")
            {
                int count = C.Get(barrageScaling) && body ? Math.Max(1, (int)(C.Get(shots) * body.attackSpeed)) : C.Get(shots);
                S.Write(__instance, "bulletCount", count);
            }
            if (s.StateType.Name == "Sabotage" || s.StateType.Name == "SabotageSpread")
                S.Write(__instance, "fireInterval", C.Get(s.Interval) / (body ? Math.Max(0.01f, body.attackSpeed) : 1f));
            if (s.StateType.Name == "DodgeState" && body && Util.HasEffectiveAuthority(body.gameObject))
            {
                GenericSkill primary = body.skillLocator.primary, secondary = body.skillLocator.secondary;
                // Native Dive resets primary stock in OnEnter. Restore the pre-roll stock before adding our reload.
                if (primary && !string.Equals(C.Get(primaryReload)?.Trim(), "Vanilla", StringComparison.OrdinalIgnoreCase))
                {
                    var saved = diveStocks.GetValue(__instance, key => new DiveStock());
                    primary.stock = Math.Min(primary.maxStock, saved.Primary + S.Reload(C.Get(primaryReload), primary.maxStock, 0));
                }
                if (secondary && !string.Equals(C.Get(secondaryReload)?.Trim(), "Vanilla", StringComparison.OrdinalIgnoreCase))
                    secondary.stock = Math.Min(secondary.maxStock, secondary.stock + S.Reload(C.Get(secondaryReload), secondary.maxStock, 0));
            }
            if (s.StateType.Name == "ThrowGrenade")
            {
                cooks.Remove(__instance);
                cooks.Add(__instance, new Cooking { Started = Time.fixedTime, NativeDuration = S.Value(__instance, "duration"), NativeDelay = S.Value(__instance, "delayBeforeFiringProjectile") });
            }
        }
        private sealed class DiveStock { internal int Primary; }
        private static readonly ConditionalWeakTable<object, DiveStock> diveStocks = new ConditionalWeakTable<object, DiveStock>();
        private static void BeforeBulletScope(object __instance, out S.Skill __state) { __state = activeBullet; activeBullet = Skill(__instance); }
        private static Exception EndBulletScope(S.Skill __state, Exception __exception) { activeBullet = __state; return __exception; }
        private static void BeforeBullet(BulletAttack __instance)
        {
            S.Skill s = activeBullet; if (s == null) return;
            __instance.procCoefficient = C.Get(s.Proc);
            __instance.falloffModel = C.Get(s.Falloff) == S.BulletFalloff.None ? BulletAttack.FalloffModel.None :
                C.Get(s.Falloff) == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : BulletAttack.FalloffModel.DefaultBullet;
        }
        private static void BeforeProjectileLaunch(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !fireProjectileInfo.projectilePrefab ||
                !IsCommando(fireProjectileInfo.owner.GetComponent<CharacterBody>()) ||
                !projectileSkills.TryGetValue(fireProjectileInfo.projectilePrefab, out S.Skill skill)) return;
            // An overcooked grenade must still detonate in the player's hand.
            if (skill.StateType.Name == "ThrowGrenade" && fireProjectileInfo.fuseOverride >= 0f &&
                fireProjectileInfo.fuseOverride <= 0.001f && fireProjectileInfo.speedOverride == 0f) return;
            fireProjectileInfo.speedOverride = skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static bool Matches(ProjectileController p, GameObject prefab) => p && prefab && p.owner &&
            IsCommando(p.owner.GetComponent<CharacterBody>()) &&
            (p.catalogIndex >= 0 && prefab.GetComponent<ProjectileController>() &&
             p.catalogIndex == prefab.GetComponent<ProjectileController>().catalogIndex || p.gameObject.name == prefab.name + "(Clone)");
        private static void BeforeProjectileStart(ProjectileController __instance)
        {
            S.Skill skill = null;
            if (Matches(__instance, roundPrefab)) skill = skills["FireFMJ"];
            if (Matches(__instance, grenadePrefab)) skill = skills["ThrowGrenade"];
            if (Matches(__instance, sabotagePrefab)) skill = skills["Sabotage"];
            if (skill == null) return;
            __instance.procCoefficient = C.Get(skill.Proc);
            if (Matches(__instance, roundPrefab))
            {
                var overlap = __instance.GetComponent<ProjectileOverlapAttack>();
                if (overlap) __instance.procCoefficient /= Math.Max(0.00001f, overlap.overlapProcCoefficient);
            }
            if (Matches(__instance, grenadePrefab))
            {
                var nativeExplosion = __instance.GetComponent<ProjectileImpactExplosion>();
                if (nativeExplosion) __instance.procCoefficient /= Math.Max(0.00001f, nativeExplosion.blastProcCoefficient);
            }
            if (Matches(__instance, roundPrefab) || Matches(__instance, grenadePrefab) || Matches(__instance, sabotagePrefab))
            {
                ProjectileOrigin origin = __instance.gameObject.AddComponent<ProjectileOrigin>(); origin.Position = __instance.transform.position; origin.Skill = skill;
            }
            if (Matches(__instance, grenadePrefab))
            {
                var explosion = __instance.GetComponent<ProjectileImpactExplosion>();
                if (explosion)
                {
                    explosion.lifetimeAfterImpact = C.Get(fuse);
                    explosion.lifetimeRandomOffset = 0f;
                    explosion.timerAfterImpact = true; explosion.impactOnWorld = true;
                    explosion.destroyOnEnemy = false; explosion.destroyOnWorld = false; explosion.detonateOnEnemy = false;
                    explosion.blastRadius = C.Get(skill.Radius);
                    explosion.falloffModel = (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), C.Get(skill.Area).ToString());
                    if (NetworkServer.active) __instance.gameObject.AddComponent<Grenade>().Setup(__instance, explosion);
                }
            }
            if (Matches(__instance, sabotagePrefab) && NetworkServer.active)
                __instance.gameObject.AddComponent<ExplosiveLifetime>().Setup(__instance);
        }
        private sealed class ProjectileOrigin : MonoBehaviour { internal Vector3 Position; internal S.Skill Skill; internal readonly HashSet<HealthComponent> Pierced = new HashSet<HealthComponent>(); }
        private static void BeforeProjectileDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (!damageInfo.inflictor) return;
            ProjectileOrigin origin = damageInfo.inflictor.GetComponent<ProjectileOrigin>();
            if (!origin || origin.Skill.Falloff == null) return;
            if (origin.Skill.StateType.Name == "FireFMJ")
            {
                int previous = origin.Pierced.Count;
                damageInfo.damage *= S.PierceMultiplier(C.Get(pierceBonus), previous);
                origin.Pierced.Add(__instance);
            }
            S.BulletFalloff mode = C.Get(origin.Skill.Falloff);
            float distance = (damageInfo.position - origin.Position).magnitude;
            if (mode == S.BulletFalloff.Default) damageInfo.damage *= 0.5f + Mathf.Clamp01(Mathf.InverseLerp(60f, 25f, distance)) * 0.5f;
            if (mode == S.BulletFalloff.Buckshot) damageInfo.damage *= 0.3f + Mathf.Clamp01(Mathf.InverseLerp(40f, 12f, distance)) * 0.7f;
        }
        private static bool BeforePierceIncrease(ProjectileOverlapAttack __instance)
        {
            if (!Matches(__instance.GetComponent<ProjectileController>(), roundPrefab)) return true;
            // Damage grows per unique enemy in BeforeProjectileDamage, including hits in the same physics frame.
            return false;
        }
        private sealed class Cooking { internal float Started, NativeDuration, NativeDelay, Remaining, ReleasedAt; internal bool Released, Cooked; }
        private static bool BeforeGrenadeAnimation(GenericProjectileBaseState __instance)
        {
            if (!C.Get(canCook) || !localCook.Value || Skill(__instance) == null) return true;
            return cooks.TryGetValue(__instance, out Cooking cook) && cook.Released && cook.Remaining > 0f;
        }
        private static void BeforeProjectileUpdate(GenericProjectileBaseState __instance)
        {
            if (__instance.GetType().Name != "ThrowGrenade" || !cooks.TryGetValue(__instance, out Cooking cook) || cook.Released ||
                !C.Get(canCook) || !localCook.Value || !__instance.outer) return;
            var input = __instance.outer.GetComponent<InputBankTest>();
            bool held = input && input.skill4.down;
            float elapsed = Time.fixedTime - cook.Started;
            if (held && S.CookRemaining(elapsed, C.Get(fuse)) > 0f)
            {
                S.Write(__instance, "duration", float.MaxValue); S.Write(__instance, "delayBeforeFiringProjectile", float.MaxValue);
                return;
            }
            cook.Released = true; cook.Cooked = elapsed >= 0.5f; cook.Remaining = S.CookRemaining(elapsed, C.Get(fuse));
            cook.ReleasedAt = Time.fixedTime;
            bool overcooked = cook.Cooked && cook.Remaining <= 0f;
            // Give the throw its normal animation and wind-up after release.
            S.Write(__instance, "stopwatch", 0f);
            S.Write(__instance, "delayBeforeFiringProjectile", overcooked ? 0f : cook.NativeDelay);
            S.Write(__instance, "duration", overcooked ? 0.05f : cook.NativeDuration);
            if (!overcooked)
                AccessTools.Method(State("ThrowGrenade"), "PlayAnimation", new[] { typeof(float) })
                    .Invoke(__instance, new object[] { cook.NativeDuration });
        }
        private static void AfterGrenadeInfo(GenericProjectileBaseState __instance, ref FireProjectileInfo fireProjectileInfo)
        {
            if (!cooks.TryGetValue(__instance, out Cooking cook) || !cook.Cooked) return;
            // The fuse starts at release, including the throw animation's wind-up.
            float remaining = Math.Max(0f, cook.Remaining - Math.Max(0f, Time.fixedTime - cook.ReleasedAt));
            fireProjectileInfo.fuseOverride = remaining;
            if (remaining <= 0f && __instance.outer)
            {
                CharacterBody body = __instance.outer.GetComponent<CharacterBody>();
                if (body) fireProjectileInfo.position = body.corePosition;
                fireProjectileInfo.speedOverride = 0f;
            }
        }
        private static void AfterGrenadeImpact(ProjectileImpactExplosion __instance, ProjectileImpactInfo impactInfo)
        { Grenade grenade = __instance.GetComponent<Grenade>(); if (grenade) grenade.Impact(impactInfo); }
        private sealed class Grenade : MonoBehaviour
        {
            private bool impacted;
            private Transform target;
            private Vector3 localPoint;
            internal void Setup(ProjectileController projectile, ProjectileImpactExplosion explosion)
            {
                if (explosion.lifetime <= 0.001f) explosion.blastAttackerFiltering = AttackerFiltering.NeverHitSelf;
                if (explosion.lifetime <= 0.001f && C.Get(canCook) && C.Get(selfDamage) && projectile.owner)
                {
                    var health = projectile.owner.GetComponent<HealthComponent>();
                    var damage = projectile.GetComponent<ProjectileDamage>();
                    if (health && damage) health.TakeDamage(new DamageInfo { attacker = projectile.owner, inflictor = gameObject,
                        damage = damage.damage * explosion.blastDamageCoefficient * (damage.crit ? 2f : 1f) * C.Get(selfPercent) / 100f,
                        position = transform.position, procCoefficient = 0f, damageType = DamageType.Generic });
                }
            }
            internal void Impact(ProjectileImpactInfo info)
            {
                if (impacted || !C.Get(sticky) || !info.collider) return;
                impacted = true;
                target = info.collider.transform; localPoint = target.InverseTransformPoint(transform.position);
                Rigidbody rigidbody = GetComponent<Rigidbody>();
                if (rigidbody) { rigidbody.velocity = Vector3.zero; rigidbody.useGravity = false; rigidbody.isKinematic = true; }
                ProjectileSimple movement = GetComponent<ProjectileSimple>(); if (movement) movement.enabled = false;
            }
            private void FixedUpdate() { if (impacted && target) transform.position = target.TransformPoint(localPoint); }
        }
        private static void BeforeBlastScope(out S.Skill __state) { __state = activeBlast; activeBlast = skills["Sabotage"]; }
        private static Exception EndBlastScope(S.Skill __state, Exception __exception) { activeBlast = __state; return __exception; }
        private static void BeforeBlast(BlastAttack __instance)
        {
            if (activeBlast == null || !__instance.inflictor || !__instance.inflictor.GetComponent<ExplosiveLifetime>()) return;
            __instance.radius = C.Get(activeBlast.Radius);
            __instance.procCoefficient = C.Get(activeBlast.Proc);
            __instance.falloffModel = (BlastAttack.FalloffModel)Enum.Parse(typeof(BlastAttack.FalloffModel), C.Get(activeBlast.Area).ToString());
        }
        private static bool BeforeChain(Vector3 origin)
        {
            if (activeBlast == null || !NetworkServer.active) return true;
            float squared = C.Get(chainDistance) * C.Get(chainDistance);
            foreach (ExplosiveLifetime explosive in explosives.ToArray())
                if (explosive && explosive.Armed && (explosive.transform.position - origin).sqrMagnitude <= squared) explosive.Detonate();
            return false;
        }
        private static bool BeforeStuckUpdate(EntityState __instance)
        {
            if (!__instance.outer || !__instance.outer.GetComponent<ExplosiveLifetime>()) return true;
            // The per-projectile timer also handles limits and airborne explosives.
            return false;
        }
        private sealed class ExplosiveLifetime : MonoBehaviour
        {
            internal ProjectileController Projectile;
            private float created;
            private bool detonating, exceeded;
            internal bool Armed => Time.fixedTime - created >= 0.25f;
            internal void Setup(ProjectileController projectile)
            {
                Projectile = projectile; created = Time.fixedTime;
                explosives.RemoveAll(e => !e); explosives.Add(this);
                int limit = C.Get(maxExplosives);
                if (limit >= 0)
                {
                    var owned = explosives.Where(e => e && !e.detonating && !e.exceeded && e.Projectile.owner == projectile.owner).ToList();
                    for (int i = 0; i < owned.Count - limit; i++) owned[i].exceeded = true;
                }
            }
            private void FixedUpdate() { if (Armed && (exceeded || Time.fixedTime - created >= C.Get(sabotageDuration))) Detonate(); }
            internal void Detonate()
            {
                if (detonating || !NetworkServer.active) return;
                detonating = true;
                HealthComponent health = GetComponent<HealthComponent>();
                if (health) health.Suicide();
            }
            private void OnDestroy() { explosives.Remove(this); }
        }
    }
}
