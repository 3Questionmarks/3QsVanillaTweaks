using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Mage.Weapon;
using EntityStates.GlobalSkills.LunarNeedle;
using EntityStates.GlobalSkills.LunarDetonator;
using HarmonyLib;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;
using LG = Tweaks.ModCompatibility.LookingGlassSkillStats;

namespace Tweaks.Items.Lunar
{
    internal static class HeresyItems
    {
        private sealed class Profile
        {
            internal int Index;
            internal string Section, SkillSection;
            internal ItemDef Item;
            internal SkillDef Definition;
            internal S.Skill Skill;
            internal ConfigEntry<bool> Enabled;
            internal readonly Dictionary<string, ConfigEntry<float>> Numbers = new Dictionary<string, ConfigEntry<float>>();
            internal readonly Dictionary<string, ConfigEntry<bool>> Flags = new Dictionary<string, ConfigEntry<bool>>();
            internal GameObject Projectile;
            internal bool Loaded;
            internal bool Active => Loaded && C.Get(Enabled);
            internal float F(string key) => C.Get(Numbers[key]);
            internal bool B(string key) => C.Get(Flags[key]);
            internal void Number(string key, float value, float vanilla, string description, bool skill = true, float min = 0f)
            { Numbers[key] = S.Float(skill ? SkillSection : Section, key, value, vanilla, description, min); }
            internal void Flag(string key, bool value, bool vanilla, string description, bool skill = true)
            { Flags[key] = S.Bool(skill ? SkillSection : Section, key, value, vanilla, description); }
        }
        private sealed class ExecuteScope { internal SkillDef Definition; internal int Consumed; }
        private sealed class CancelData { internal bool Released; }
        private sealed class ImpactScope { internal ProjectileController Controller; internal ProjectileDamage Damage; internal float CarrierDamage, Proc; }
        private sealed class HealScope { internal float Fraction, Frequency; }
        private static readonly List<Profile> profiles = new List<Profile>();
        private static readonly Dictionary<string, Func<string>> descriptions = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        private static readonly ConditionalWeakTable<GhostUtilitySkillState, CancelData> cancel = new ConditionalWeakTable<GhostUtilitySkillState, CancelData>();
        private static Harmony harmony;
        [ThreadStatic] private static CharacterBody rootAttacker;
        private static readonly string[] itemNames = { "Visions of Heresy", "Hooks of Heresy", "Strides of Heresy", "Essence of Heresy" };
        private static readonly string[] skillNames = { "Hungering Gaze", "Slicing Maelstrom", "Shadowfade", "Ruin" };
        private static readonly string[] catalogNames = { "LunarPrimaryReplacement", "LunarSecondaryReplacement", "LunarUtilityReplacement", "LunarDetonatorSpecialReplacement" };
        private static readonly Type[] stateTypes = { typeof(FireLunarNeedle), typeof(ChargeLunarSecondary), typeof(GhostUtilitySkillState), typeof(Detonate) };

        internal static void Init()
        {
            for (int i = 0; i < 4; i++)
            {
                var p = new Profile { Index = i, Section = "Items - Lunar - " + itemNames[i] };
                p.SkillSection = p.Section + " - " + skillNames[i];
                p.Enabled = C.BindStartupBool(p.Section, "Enabled", true, "Enable item stacking and replacement skill changes. Restart to apply. Vanilla: False.");
                profiles.Add(p);
            }
            RoR2Application.onLoad += Load;
        }
        internal static bool OwnsSkill(int index) => profiles.Count > index && profiles[index].Active;
        private static Profile P(int index) => profiles[index];
        private static int Copies(CharacterBody body, Profile p) => body && body.inventory ? Math.Max(1, body.inventory.GetItemCountEffective(p.Item)) : 1;
        private static int ExtraCopies(CharacterBody body, Profile p) => Copies(body, p) - 1;
        private static bool HereticIgnoresPenalties(CharacterBody body)
        {
            // The survivor module owns this option; itemless Moffein Heretic is still a first-copy skill.
            return body && Tweaks.Survivors.Heretic.IgnoreHeresyStackPenalties(body);
        }
        private static float Scale(Profile p, CharacterBody body, string extra) => ExtraCopies(body, p) * p.F(extra);
        private static Profile For(GenericSkill slot) => !slot ? null : profiles.FirstOrDefault(p => p.Active && p.Definition == slot.skillDef);
        private static void Load()
        {
            harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Items.Heresy");
            ItemDef[] items = { RoR2Content.Items.LunarPrimaryReplacement, RoR2Content.Items.LunarSecondaryReplacement, RoR2Content.Items.LunarUtilityReplacement, RoR2Content.Items.LunarSpecialReplacement };
            foreach (Profile p in profiles)
            {
                if (!p.Enabled.Value) continue;
                try
                {
                    p.Item = items[p.Index];
                    p.Definition = SkillCatalog.GetSkillDef(SkillCatalog.FindSkillIndexByName(catalogNames[p.Index]));
                    if (!p.Definition) throw new InvalidOperationException("Missing skill " + catalogNames[p.Index]);
                    p.Skill = new S.Skill(p.SkillSection, p.Definition, stateTypes[p.Index], p.Enabled);
                    if (p.Index == 0) C.Bind(p.SkillSection, "Stocks Recharged Per Cooldown", "All", "Stocks restored per cooldown. Use a whole number or All. Vanilla: All.");
                    p.Skill.Bind(damage: false, changedAgile: p.Index == 3 ? (bool?)true : null);
                    if (p.Index != 0) p.Flag("Cooldown Scales With Attack Speed", p.Definition.attackSpeedBuffsRestockSpeed, p.Definition.attackSpeedBuffsRestockSpeed,
                        "Use the skill's native attack-speed recharge rate scaling.");
                    if (p.Index == 3) p.Number("Activation Duration", S.Value(p.Skill.Sample, "baseDuration", .2f), S.Value(p.Skill.Sample, "baseDuration", .2f), "Activation animation time before attack speed is applied.", min: .001f);
                    BindSpecific(p);
                    p.Skill.Loaded = p.Loaded = true;
                    RegisterDescriptions(p);
                    LG.Register(p.Definition, () => ExtraStats(p), p.Index == 2 ? null : (Func<float>)(() => p.F("Proc Coefficient")));
                }
                catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning(itemNames[p.Index] + " changes could not load: " + e); }
            }
            if (!profiles.Any(p => p.Active)) return;
            InstallHooks();
            C.EffectiveSettingsChanged += Apply;
            Apply();
            // Resolve descriptions after the survivor module's optional Moffein description registration.
            On.RoR2.Language.GetLocalizedStringByToken += LanguageText;
        }
        private static void BindSpecific(Profile p)
        {
            int i = p.Index;
            float cooldown = i == 0 ? 2f : i == 1 ? 5f : i == 2 ? 0f : 8f;
            p.Number("Cooldown Per Stack", cooldown, cooldown, "Additional cooldown seconds for each item after the first. First-copy cooldown is configured under the skill.", false);
            if (i == 0)
            {
                p.Flag("Rapid", false, false, "Cooldown is reduced by attack speed. Overrides Moffein Heretic's Visions attack-speed setting.", false);
                p.Number("Initial Hit Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on impact per extra item.", false);
                p.Number("Detonation Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on detonation per extra item.", false);
                p.Number("Charges Per Stack", 12f, 12f, "Additional charges per extra item, rounded down.", false);
                p.Projectile = FireLunarNeedle.projectilePrefab;
                RequireProjectile(p);
                p.Number("Initial Hit Damage (%)", 5f, 5f, "Impact damage as a percentage of base damage.");
                p.Number("Initial Hit Proc Coefficient", .1f, .1f, "Proc coefficient of the initial impact.");
                p.Number("Detonation Damage (%)", 120f, 120f, "Detonation damage as a percentage of base damage.");
                p.Number("Proc Coefficient", NativeExplosionProc(p), NativeExplosionProc(p), "Detonation proc coefficient.");
                p.Number("Damage Interval", FireLunarNeedle.baseDuration, FireLunarNeedle.baseDuration, "Minimum time between shots before attack speed.", min: .001f);
                var steering = p.Projectile.GetComponent<ProjectileSteerTowardTarget>();
                if (!steering) throw new InvalidOperationException("Hungering Gaze steering component was not found.");
                p.Number("Tracking Aggression", steering.rotationSpeed * 2f, steering.rotationSpeed, "Homing turn rate in degrees per second; default is twice vanilla.");
                BindProjectileBasics(p);
            }
            if (i == 1)
            {
                p.Number("Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points per periodic hit per extra item.", false);
                p.Number("Explosion Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on explosion per extra item.", false);
                p.Number("Root Duration Per Stack", 3f, 3f, "Additional root seconds per extra item.", false);
                object sample = EntityStateCatalog.InstantiateState(typeof(ThrowLunarSecondary));
                p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
                RequireProjectile(p);
                p.Number("Damage (%)", 175f, 175f, "Damage per periodic hit as a percentage of base damage.");
                p.Number("Damage Interval", .2f, .2f, "Time between repeated hits in seconds.", min: .001f);
                p.Number("Explosion Damage (%)", 700f, 700f, "Explosion damage as a percentage of base damage.");
                p.Number("Proc Coefficient", NativeProc(p), NativeProc(p), "Proc coefficient per periodic hit.");
                p.Number("Explosion Proc Coefficient", NativeExplosionProc(p), NativeExplosionProc(p), "Proc coefficient of the final explosion.");
                p.Number("Root Duration", 3f, 3f, "First-copy root duration in seconds.");
                p.Number("Minimum Velocity", S.Value(sample, "minSpeed"), S.Value(sample, "minSpeed"), "Projectile launch speed at minimum charge in metres per second.");
                p.Number("Maximum Velocity", S.Value(sample, "maxSpeed"), S.Value(sample, "maxSpeed"), "Projectile launch speed at full charge in metres per second.");
                p.Number("Charge Duration", S.Value(p.Skill.Sample, "baseDuration", 1f), S.Value(p.Skill.Sample, "baseDuration", 1f), "Time required to fully charge before attack speed.", min: .001f);
                p.Number("Minimum Charge Duration", S.Value(p.Skill.Sample, "minChargeDuration", .5f), S.Value(p.Skill.Sample, "minChargeDuration", .5f), "Minimum time before releasing a charged shot.");
                p.Number("Throw Duration", S.Value(sample, "baseDuration", .2f), S.Value(sample, "baseDuration", .2f), "Throw animation time before attack speed.", min: .001f);
                p.Flag("Show Damage Per Tick", true, false, "Show damage per hit rather than damage per second.");
                BindProjectileBasics(p);
            }
            if (i == 2)
            {
                p.Number("Movement Speed Per Stack (%)", 0f, 0f, "Additional movement-speed percentage points per extra item.", false);
                p.Number("Heal Per Stack (%)", 1.5f, 0f, "Additional percentage of maximum health restored per interval per extra item.", false);
                p.Number("Duration Per Stack", 3f, 3f, "Additional duration seconds per extra item.", false);
                p.Number("Movement Speed Boost (%)", 30f, (GhostUtilitySkillState.moveSpeedCoefficient - 1f) * 100f, "Movement-speed boost while intangible.");
                p.Number("Heal Per Interval (%)", 1.5f, GhostUtilitySkillState.healFractionPerTick * 100f, "Percentage of maximum health healed each tick.");
                p.Number("Interval", .2f, 1f / Math.Max(.001f, GhostUtilitySkillState.healFrequency), "Seconds between healing ticks.", min: .001f);
                p.Number("Duration", 3f, GhostUtilitySkillState.baseDuration, "First-copy duration in seconds.", min: .001f);
                p.Flag("Can Cancel", false, false, "Press utility again after releasing it to cancel early. Heretic's passive can independently enable this.");
                p.Flag("Cleanses Debuffs", true, false, "Cleanse debuffs, damage over time and stun on activation. Keeps positive buffs and cooldown debuffs, including bands.");
            }
            if (i == 3)
            {
                p.Number("Ruin Duration Per Stack", 20f, 0f, "Additional Ruin debuff seconds per extra item.", false);
                p.Number("Ruin Damage Per Stack (%)", 0f, 0f, "Additional base explosion damage percentage points per extra item.", false);
                p.Number("Ruin Damage Per Debuff Stack Per Stack (%)", 0f, 0f, "Additional damage percentage points per Ruin debuff stack per extra item.", false);
                p.Number("Damage (%)", 300f, Detonate.baseDamageCoefficient * 100f, "Base detonation damage percentage, before Ruin debuff stacks.");
                p.Number("Ruin Debuff Per Stack Extra Damage (%)", 120f, Detonate.damageCoefficientPerStack * 100f, "Additional damage percentage per Ruin debuff stack.");
                p.Number("Ruin Debuff Duration", 20f, 10f, "First-copy Ruin debuff duration in seconds.");
                p.Number("Proc Coefficient", 1f, 1f, "Proc coefficient of the detonation.");
                p.Number("Orb Speed", 120f, 120f, "Detonation orb travel speed in metres per second.");
                p.Flag("Can Apply Ruin On Cooldown", false, false, "Apply Ruin when the skill has no stocks ready.");
                p.Flag("Reapplying Resets Duration", true, false, "A new Ruin stack refreshes the timers of existing Ruin stacks.");
            }
        }
        private static void RequireProjectile(Profile p)
        { if (!p.Projectile) throw new InvalidOperationException("Missing projectile for " + skillNames[p.Index]); }
        private static float NativeProc(Profile p)
        {
            var controller = p.Projectile.GetComponent<ProjectileController>();
            var overlap = p.Projectile.GetComponent<ProjectileOverlapAttack>();
            var dot = p.Projectile.GetComponent<ProjectileDotZone>();
            return (controller ? controller.procCoefficient : 1f) * (overlap ? overlap.overlapProcCoefficient : dot ? dot.overlapProcCoefficient : 1f);
        }
        private static float NativeExplosionProc(Profile p)
        {
            var controller = p.Projectile.GetComponent<ProjectileController>();
            var explosion = p.Projectile.GetComponent<ProjectileExplosion>();
            return (controller ? controller.procCoefficient : 1f) * (explosion ? explosion.blastProcCoefficient : 1f);
        }
        private static void BindProjectileBasics(Profile p)
        {
            var explosion = p.Projectile.GetComponent<ProjectileExplosion>();
            if (!explosion) throw new InvalidOperationException("Missing projectile explosion for " + skillNames[p.Index]);
            p.Number("Explosion Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
            var impact = p.Projectile.GetComponent<ProjectileImpactExplosion>();
            var fuse = p.Projectile.GetComponent<ProjectileFuse>();
            var zone = p.Projectile.GetComponent<ProjectileDotZone>();
            float lifetime = impact ? impact.lifetime : fuse ? fuse.fuse : zone ? zone.lifetime : 0f;
            if (lifetime > 0f) p.Number("Projectile Lifetime", lifetime, lifetime, "Projectile lifetime before explosion in seconds.", min: .001f);
            // Area enum is kept separately so the complete native falloff set remains available.
            falloffs[p.Index] = C.Bind(p.SkillSection, "AOE Damage Falloff", explosion.falloffModel, "Explosion damage falloff. Vanilla: " + explosion.falloffModel + ".");
            if (p.Index == 0)
            {
                var simple = p.Projectile.GetComponent<ProjectileSimple>();
                if (simple) p.Number("Projectile Speed", simple.desiredForwardSpeed, simple.desiredForwardSpeed, "Projectile speed in metres per second.");
            }
        }
        private static readonly Dictionary<int, ConfigEntry<BlastAttack.FalloffModel>> falloffs = new Dictionary<int, ConfigEntry<BlastAttack.FalloffModel>>();
        private static void Patch(Type type, string method, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = AccessTools.Method(type, method, args);
            if (target == null) throw new MissingMethodException(type.FullName, method);
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(HeresyItems), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(HeresyItems), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(HeresyItems), finalizer));
        }
        private static void InstallHooks()
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(GenericSkill), "baseRechargeInterval"), postfix: new HarmonyMethod(typeof(HeresyItems), nameof(RechargeInterval)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(GenericSkill), "rechargeStock"), postfix: new HarmonyMethod(typeof(HeresyItems), nameof(RechargeStocks)));
            Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(RequiredStocks));
            Patch(typeof(SkillDef), "OnExecute", prefix: nameof(ExecuteStocks), finalizer: nameof(RestoreExecuteStocks));
            Patch(typeof(GenericSkill), "RecalculateMaxStock", postfix: nameof(MaxStocks));
            Patch(typeof(ProjectileSingleTargetImpact), "OnProjectileImpact", prefix: nameof(NeedleImpact), finalizer: nameof(NeedleImpactRestore));
            Patch(typeof(ProjectileController), "DispatchOnInitialized", prefix: nameof(ProjectileInitialized));
            Patch(typeof(ProjectileController), "Start", prefix: nameof(ProjectileInitialized));
            Patch(typeof(ThrowLunarSecondary), "ModifyProjectile", prefix: nameof(ThrowVelocity));
            Patch(typeof(GhostUtilitySkillState), "OnEnter", postfix: nameof(ShadowEnter));
            Patch(typeof(GhostUtilitySkillState), "FixedUpdate", nameof(ShadowBefore), nameof(ShadowAfter), nameof(ShadowRestore));
            Patch(typeof(GlobalEventManager), "ProcessHitEnemy", nameof(RootBefore), finalizer: nameof(RootRestore));
            Patch(typeof(CharacterBody), "AddTimedBuff", prefix: nameof(RootBuff), args: new[] { typeof(BuffDef), typeof(float) });
            Type listener = typeof(LunarDetonatorPassiveAttachment).GetNestedType("DamageListener", BindingFlags.Public | BindingFlags.NonPublic);
            if (listener == null) throw new MissingMemberException("Ruin damage listener was not found.");
            Patch(listener, "OnDamageDealtServer", prefix: nameof(RuinDamage));
            Patch(typeof(LunarDetonatorOrb), "Begin", prefix: nameof(RuinOrb));
            Patch(typeof(BaseChargeBombState), "OnEnter", prefix: nameof(ChargeEnter));
            Patch(typeof(BaseThrowBombState), "OnEnter", prefix: nameof(ThrowEnter));
        }
        private static void Apply()
        {
            foreach (Profile p in profiles.Where(p => p.Active))
            {
                p.Skill.ApplyDefinition();
                p.Definition.attackSpeedBuffsRestockSpeed = p.Index == 0 ? p.B("Rapid") : p.B("Cooldown Scales With Attack Speed");
                p.Skill.SetKeyword("KEYWORD_RAPID", p.Definition.attackSpeedBuffsRestockSpeed);
                if (p.Index == 0)
                {
                    S.SetText("KEYWORD_RAPID", () => "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
                    FireLunarNeedle.baseDuration = p.F("Damage Interval");
                }
                if (p.Index == 3)
                {
                    Detonate.baseDamageCoefficient = p.F("Damage (%)") / 100f;
                    Detonate.damageCoefficientPerStack = p.F("Ruin Debuff Per Stack Extra Damage (%)") / 100f;
                    Detonate.baseDuration = p.F("Activation Duration");
                }
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (body.skillLocator)
                    foreach (GenericSkill slot in body.skillLocator.allSkills ?? Array.Empty<GenericSkill>())
                        if (For(slot) != null) slot.RecalculateValues();
        }
        private static void RechargeInterval(GenericSkill __instance, ref float __result)
        {
            Profile p = For(__instance);
            if (p == null || __instance.overriddenRechargeInterval.HasValue) return;
            float extra = HereticIgnoresPenalties(__instance.characterBody) ? 0f : Scale(p, __instance.characterBody, "Cooldown Per Stack");
            __result = Math.Max(0f, p.Definition.baseRechargeInterval + extra);
        }
        private static void MaxStocks(GenericSkill __instance)
        {
            Profile p = For(__instance);
            if (p == null || p.Index != 0) return;
            int value = p.Definition.baseMaxStock + (int)Scale(p, __instance.characterBody, "Charges Per Stack");
            if (!p.Definition.dontAllowPastMaxStocks) value += __instance.bonusStockFromBody;
            __instance.OverrideMaxStock(Math.Max(0, value));
        }
        private static void RechargeStocks(GenericSkill __instance, ref int __result)
        {
            Profile p = For(__instance);
            if (p == null) return;
            var configured = S.Read(p.Skill, "recharge") as ConfigEntry<string>;
            if (configured != null) __result = S.Stock(C.Get(configured), __instance.maxStock, p.Definition.rechargeStock);
        }
        private static int StockSetting(Profile p, string field, int maximum, int fallback)
        {
            var entry = S.Read(p.Skill, field) as ConfigEntry<string>;
            return entry == null ? fallback : S.Stock(C.Get(entry), maximum, fallback);
        }
        private static void RequiredStocks(GenericSkill skillSlot, ref bool __result)
        {
            Profile p = For(skillSlot); if (p == null) return;
            int consumed = StockSetting(p, "consumed", skillSlot.maxStock, p.Definition.stockToConsume);
            int required = StockSetting(p, "required", skillSlot.maxStock, p.Definition.requiredStock);
            __result = skillSlot.stock >= Math.Max(consumed, required);
        }
        private static void ExecuteStocks(SkillDef __instance, GenericSkill skillSlot, out ExecuteScope __state)
        {
            __state = null; Profile p = For(skillSlot); if (p == null) return;
            __state = new ExecuteScope { Definition = __instance, Consumed = __instance.stockToConsume };
            __instance.stockToConsume = StockSetting(p, "consumed", skillSlot.maxStock, __instance.stockToConsume);
        }
        private static Exception RestoreExecuteStocks(ExecuteScope __state, Exception __exception)
        {
            if (__state != null && __state.Definition) __state.Definition.stockToConsume = __state.Consumed;
            return __exception;
        }
        private static void ChargeEnter(BaseChargeBombState __instance)
        {
            if (!P(1).Active || !(__instance is ChargeLunarSecondary)) return;
            __instance.baseDuration = P(1).F("Charge Duration");
            __instance.minChargeDuration = Math.Min(P(1).F("Minimum Charge Duration"), __instance.baseDuration);
        }
        private static void ThrowEnter(BaseThrowBombState __instance)
        { if (P(1).Active && __instance is ThrowLunarSecondary) __instance.baseDuration = P(1).F("Throw Duration"); }
        private static void ThrowVelocity(ThrowLunarSecondary __instance)
        {
            if (!P(1).Active) return;
            __instance.minSpeed = P(1).F("Minimum Velocity");
            __instance.maxSpeed = P(1).F("Maximum Velocity");
        }
        private static Profile ProjectileProfile(ProjectileController controller) => profiles.FirstOrDefault(p => p.Active && p.Projectile &&
            ((controller.catalogIndex >= 0 && controller.catalogIndex == p.Projectile.GetComponent<ProjectileController>().catalogIndex) || controller.gameObject.name == p.Projectile.name + "(Clone)"));
        private static void ProjectileInitialized(ProjectileController __instance)
        {
            Profile p = ProjectileProfile(__instance);
            if (p == null || !__instance.owner) return;
            CharacterBody body = __instance.owner.GetComponent<CharacterBody>();
            if (!body) return;
            var damage = __instance.GetComponent<ProjectileDamage>();
            if (!damage) return;
            // Normalize the projectile's carrier damage; individual hit/explosion coefficients are independent.
            damage.damage = body.damage;
            __instance.procCoefficient = 1f;
            float periodic = p.Index == 0 ? p.F("Initial Hit Damage (%)") + Scale(p, body, "Initial Hit Damage Per Stack (%)") : p.F("Damage (%)") + Scale(p, body, "Damage Per Stack (%)");
            float periodicProc = p.Index == 0 ? p.F("Initial Hit Proc Coefficient") : p.F("Proc Coefficient");
            var overlap = __instance.GetComponent<ProjectileOverlapAttack>();
            if (overlap)
            {
                overlap.damageCoefficient = periodic / 100f;
                overlap.overlapProcCoefficient = periodicProc;
                if (p.Index == 1) overlap.resetInterval = p.F("Damage Interval");
                if (S.Read(overlap, "attack") != null) AccessTools.Method(typeof(ProjectileOverlapAttack), "UpdateAttackValues")?.Invoke(overlap, null);
            }
            var zone = __instance.GetComponent<ProjectileDotZone>();
            if (zone)
            {
                zone.damageCoefficient = periodic / 100f;
                zone.overlapProcCoefficient = periodicProc;
                if (p.Index == 1) { zone.resetFrequency = 1f / p.F("Damage Interval"); zone.fireFrequency = Math.Max(zone.fireFrequency, zone.resetFrequency); }
                if (S.Read(zone, "attack") != null) AccessTools.Method(typeof(ProjectileDotZone), "ResetOverlap")?.Invoke(zone, null);
            }
            var explosion = __instance.GetComponent<ProjectileExplosion>();
            if (explosion)
            {
                explosion.blastDamageCoefficient = (p.Index == 0 ? p.F("Detonation Damage (%)") + Scale(p, body, "Detonation Damage Per Stack (%)") : p.F("Explosion Damage (%)") + Scale(p, body, "Explosion Damage Per Stack (%)")) / 100f;
                explosion.blastProcCoefficient = p.Index == 0 ? p.F("Proc Coefficient") : p.F("Explosion Proc Coefficient");
                explosion.blastRadius = p.F("Explosion Radius");
                explosion.falloffModel = C.Get(falloffs[p.Index]);
                if (p.Numbers.ContainsKey("Projectile Lifetime"))
                {
                    var impact = __instance.GetComponent<ProjectileImpactExplosion>();
                    var fuse = __instance.GetComponent<ProjectileFuse>();
                    if (impact) impact.lifetime = p.F("Projectile Lifetime");
                    if (fuse) fuse.fuse = p.F("Projectile Lifetime");
                    if (zone && zone.lifetime > 0f) zone.lifetime = p.F("Projectile Lifetime");
                }
            }
            if (p.Index == 0)
            {
                var steer = __instance.GetComponent<ProjectileSteerTowardTarget>();
                if (steer) { steer.rotationSpeed = p.F("Tracking Aggression"); S.Write(steer, "_rotationSpeed", steer.rotationSpeed); }
                var simple = __instance.GetComponent<ProjectileSimple>();
                if (simple && p.Numbers.ContainsKey("Projectile Speed")) simple.desiredForwardSpeed = p.F("Projectile Speed");
            }
        }
        private static void NeedleImpact(ProjectileSingleTargetImpact __instance, out ImpactScope __state)
        {
            __state = null;
            var controller = __instance.GetComponent<ProjectileController>();
            Profile p = controller ? ProjectileProfile(controller) : null;
            if (p == null || p.Index != 0 || !controller.owner) return;
            CharacterBody body = controller.owner.GetComponent<CharacterBody>();
            var damage = __instance.GetComponent<ProjectileDamage>();
            if (!body || !damage) return;
            __state = new ImpactScope { Controller = controller, Damage = damage, CarrierDamage = damage.damage, Proc = controller.procCoefficient };
            damage.damage = body.damage * (p.F("Initial Hit Damage (%)") + Scale(p, body, "Initial Hit Damage Per Stack (%)")) / 100f;
            controller.procCoefficient = p.F("Initial Hit Proc Coefficient");
        }
        private static Exception NeedleImpactRestore(ImpactScope __state, Exception __exception)
        {
            if (__state != null)
            {
                if (__state.Damage) __state.Damage.damage = __state.CarrierDamage;
                if (__state.Controller) __state.Controller.procCoefficient = __state.Proc;
            }
            return __exception;
        }
        private static void ShadowEnter(GhostUtilitySkillState __instance)
        {
            Profile p = P(2); if (!p.Active) return;
            CharacterBody body = __instance.characterBody;
            S.Write(__instance, "duration", p.F("Duration") + Scale(p, body, "Duration Per Stack"));
            if (__instance.characterMotor) __instance.characterMotor.walkSpeedPenaltyCoefficient = 1f + (p.F("Movement Speed Boost (%)") + Scale(p, body, "Movement Speed Per Stack (%)")) / 100f;
            cancel.GetOrCreateValue(__instance).Released = false;
            if (NetworkServer.active && p.B("Cleanses Debuffs") && body)
                CleanseSystem.CleanseBodyServer(body, true, false, false, true, true, false);
        }
        private static void ShadowBefore(GhostUtilitySkillState __instance, out HealScope __state)
        {
            __state = null; Profile p = P(2); if (!p.Active) return;
            __state = new HealScope { Fraction = GhostUtilitySkillState.healFractionPerTick, Frequency = GhostUtilitySkillState.healFrequency };
            GhostUtilitySkillState.healFractionPerTick = (p.F("Heal Per Interval (%)") + Scale(p, __instance.characterBody, "Heal Per Stack (%)")) / 100f;
            GhostUtilitySkillState.healFrequency = 1f / p.F("Interval");
        }
        private static void ShadowAfter(GhostUtilitySkillState __instance)
        {
            if (!P(2).Active || !P(2).B("Can Cancel") || !__instance.isAuthority || !__instance.inputBank) return;
            CancelData data = cancel.GetOrCreateValue(__instance);
            if (!__instance.inputBank.skill3.down) data.Released = true;
            else if (data.Released && __instance.inputBank.skill3.justPressed) __instance.outer.SetNextStateToMain();
        }
        private static Exception ShadowRestore(HealScope __state, Exception __exception)
        {
            if (__state != null) { GhostUtilitySkillState.healFractionPerTick = __state.Fraction; GhostUtilitySkillState.healFrequency = __state.Frequency; }
            return __exception;
        }
        private static void RootBefore(DamageInfo damageInfo, out CharacterBody __state)
        { __state = rootAttacker; rootAttacker = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null; }
        private static Exception RootRestore(CharacterBody __state, Exception __exception) { rootAttacker = __state; return __exception; }
        private static void RootBuff(BuffDef buffDef, ref float duration)
        {
            Profile p = P(1);
            if (p.Active && rootAttacker && buffDef == RoR2Content.Buffs.LunarSecondaryRoot)
                duration = p.F("Root Duration") + Scale(p, rootAttacker, "Root Duration Per Stack");
        }
        private static bool RuinDamage(object __instance, DamageReport damageReport)
        {
            Profile p = P(3); if (!p.Active) return true;
            if (!NetworkServer.active || damageReport == null || !damageReport.victim || !damageReport.victim.alive || !damageReport.victimBody) return false;
            var controller = S.Read(__instance, "passiveController") as LunarDetonatorPassiveAttachment;
            GenericSkill slot = controller ? controller.monitoredSkill : null;
            if (!slot || (!p.B("Can Apply Ruin On Cooldown") && slot.stock <= 0)) return false;
            if (!Util.CheckRoll(damageReport.damageInfo.procCoefficient * 100f, damageReport.attackerMaster)) return false;
            CharacterBody victim = damageReport.victimBody;
            float duration = p.F("Ruin Debuff Duration") + Scale(p, slot.characterBody, "Ruin Duration Per Stack");
            if (p.B("Reapplying Resets Duration") && S.Read(victim, "timedBuffs") is System.Collections.IEnumerable timed)
                foreach (object buff in timed)
                    if (S.Read(buff, "buffIndex") is BuffIndex index && index == RoR2Content.Buffs.LunarDetonationCharge.buffIndex)
                    { S.Write(buff, "timer", duration); S.Write(buff, "totalDuration", duration); }
            victim.AddTimedBuff(RoR2Content.Buffs.LunarDetonationCharge, duration);
            return false;
        }
        private static void RuinOrb(LunarDetonatorOrb __instance)
        {
            Profile p = P(3); if (!p.Active) return;
            CharacterBody body = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null;
            if (body)
            {
                __instance.baseDamage += body.damage * Scale(p, body, "Ruin Damage Per Stack (%)") / 100f;
                __instance.damagePerStack += body.damage * Scale(p, body, "Ruin Damage Per Debuff Stack Per Stack (%)") / 100f;
            }
            __instance.procCoefficient = p.F("Proc Coefficient");
            __instance.travelSpeed = p.F("Orb Speed");
        }
        private static void RegisterDescriptions(Profile p)
        {
            string original = p.Definition.skillDescriptionToken;
            string token = "3QS_HERESY_SKILL_" + p.Index;
            descriptions[token] = () => SkillDescription(p);
            descriptions[original] = () => SkillDescription(p);
            if (p.Index == 1) descriptions["MOFFEINHERETIC_SKILL_LUNAR_SECONDARY_REPLACEMENT_DESCRIPTION"] = () => SkillDescription(p);
            if (p.Index == 3)
            {
                descriptions["MOFFEINHERETIC_SKILL_LUNAR_SPECIAL_REPLACEMENT_DESCRIPTION"] = () => SkillDescription(p);
                descriptions["3QS_MOFFEIN_RUIN_DESCRIPTION"] = () => SkillDescription(p);
            }
            p.Definition.skillDescriptionToken = token;
            S.SetDescription(p.Skill, token, () => SkillDescription(p), original);
            descriptions[p.Item.descriptionToken] = () => ItemDescription(p);
            S.SetText(p.Item.descriptionToken, () => ItemDescription(p));
        }
        private static string LanguageText(On.RoR2.Language.orig_GetLocalizedStringByToken orig, Language self, string token)
        { return token != null && descriptions.TryGetValue(token, out Func<string> text) ? text() : orig(self, token); }
        private static string N(float value) => S.Number(value);
        private static string D(float value) => S.Damage(N(value) + "% damage");
        private static string Stack(float value, string unit) => " <style=cStack>(+" + N(value) + unit + " per stack)</style>";
        private static CharacterBody DescriptionBody => LocalUserManager.GetFirstLocalUser()?.cachedBody;
        private static float V(Profile p, string key, string extra, bool totals) => p.F(key) + (totals ? Scale(p, DescriptionBody, extra) : 0f);
        private static string SkillDescription(Profile p, bool totals = true)
        {
            string text = p.Skill.AgileText;
            if (p.Index == 0) return (p.B("Rapid") ? S.Utility("Rapid. ") : "") + text + "Fire tracking shards that hit for " + D(V(p, "Initial Hit Damage (%)", "Initial Hit Damage Per Stack (%)", totals)) + " and detonate for " + D(V(p, "Detonation Damage (%)", "Detonation Damage Per Stack (%)", totals)) + ".";
            if (p.Index == 1)
            {
                string damage = p.B("Show Damage Per Tick") ? D(V(p, "Damage (%)", p.Index == 3 ? "Ruin Damage Per Stack (%)" : "Damage Per Stack (%)", totals)) + " every " + S.Utility(N(p.F("Damage Interval")) + " seconds") : S.Damage(N(V(p, "Damage (%)", p.Index == 3 ? "Ruin Damage Per Stack (%)" : "Damage Per Stack (%)", totals) / p.F("Damage Interval")) + "% damage per second") + " (" + D(V(p, "Damage (%)", "Damage Per Stack (%)", totals)) + " every " + S.Utility(N(p.F("Damage Interval")) + " seconds") + ")";
                return text + "Charge up a ball of blades that repeatedly deals " + damage + ". After a delay, explode for " + D(V(p, "Explosion Damage (%)", "Explosion Damage Per Stack (%)", totals)) + " and " + S.Damage("root") + " enemies for " + S.Utility(N(V(p, "Root Duration", "Root Duration Per Stack", totals)) + " seconds") + ".";
            }
            if (p.Index == 2) return text + "Fade away, becoming " + S.Utility("intangible") + " and gaining " + S.Utility(N(V(p, "Movement Speed Boost (%)", "Movement Speed Per Stack (%)", totals)) + "% movement speed") + ". <style=cIsHealing>Heal for " + N(V(p, "Heal Per Interval (%)", "Heal Per Stack (%)", totals)) + "% of your maximum health</style> every " + S.Utility(N(p.F("Interval")) + " seconds") + " for " + S.Utility(N(V(p, "Duration", "Duration Per Stack", totals)) + " seconds") + "." + (p.B("Cleanses Debuffs") ? " <style=cIsUtility>Cleanse debuffs</style> on activation." : "") + (p.B("Can Cancel") ? " Reactivate to cancel." : "");
            return text + "Dealing damage adds a stack of " + S.Damage("Ruin") + " for " + S.Utility(N(V(p, "Ruin Debuff Duration", "Ruin Duration Per Stack", totals)) + " seconds") + ". Activating the skill " + S.Damage("detonates all Ruin stacks") + " at unlimited range, dealing " + D(V(p, "Damage (%)", p.Index == 3 ? "Ruin Damage Per Stack (%)" : "Damage Per Stack (%)", totals)) + " plus " + D(V(p, "Ruin Debuff Per Stack Extra Damage (%)", "Ruin Damage Per Debuff Stack Per Stack (%)", totals)) + " per stack of " + S.Damage("Ruin") + ".";
        }
        private static string Bonus(Profile p, string key, string suffix) => p.F(key) == 0f ? "" : Stack(p.F(key), suffix);
        private static string ItemDescription(Profile p)
        {
            string text = "Replace your " + new[] { "primary", "secondary", "utility", "special" }[p.Index] + " skill with " + S.Utility(skillNames[p.Index]) + ".\n";
            if (p.Index == 0)
                text += "Fire tracking shards for " + D(p.F("Initial Hit Damage (%)")) + Bonus(p, "Initial Hit Damage Per Stack (%)", "% damage") + " that detonate for " + D(p.F("Detonation Damage (%)")) + Bonus(p, "Detonation Damage Per Stack (%)", "% damage") + ". Hold up to " + S.Utility(p.Definition.baseMaxStock + " charges") + Bonus(p, "Charges Per Stack", "") + ".";
            if (p.Index == 1)
            {
                string damage = p.B("Show Damage Per Tick") ? D(p.F("Damage (%)")) + " every " + S.Utility(N(p.F("Damage Interval")) + " seconds") : S.Damage(N(p.F("Damage (%)") / p.F("Damage Interval")) + "% damage per second") + " (" + D(p.F("Damage (%)")) + " every " + S.Utility(N(p.F("Damage Interval")) + " seconds") + ")";
                text += "Charge a ball of blades that deals " + damage + Bonus(p, "Damage Per Stack (%)", "% damage per hit") + ". Explode for " + D(p.F("Explosion Damage (%)")) + Bonus(p, "Explosion Damage Per Stack (%)", "% damage") + " and " + S.Damage("root") + " enemies for " + S.Utility(N(p.F("Root Duration")) + " seconds") + Bonus(p, "Root Duration Per Stack", "s") + ".";
            }
            if (p.Index == 2)
                text += "Become " + S.Utility("intangible") + " and gain " + S.Utility(N(p.F("Movement Speed Boost (%)")) + "% movement speed") + Bonus(p, "Movement Speed Per Stack (%)", "%") + ". <style=cIsHealing>Heal for " + N(p.F("Heal Per Interval (%)")) + "% of maximum health</style>" + Bonus(p, "Heal Per Stack (%)", "% max HP") + " every " + S.Utility(N(p.F("Interval")) + " seconds") + " for " + S.Utility(N(p.F("Duration")) + " seconds") + Bonus(p, "Duration Per Stack", "s") + "." + (p.B("Cleanses Debuffs") ? " <style=cIsUtility>Cleanse debuffs</style> on activation." : "");
            if (p.Index == 3)
                text += "Dealing damage adds " + S.Damage("Ruin") + " for " + S.Utility(N(p.F("Ruin Debuff Duration")) + " seconds") + Bonus(p, "Ruin Duration Per Stack", "s") + ". Detonate all " + S.Damage("Ruin stacks") + " for " + D(p.F("Damage (%)")) + Bonus(p, "Ruin Damage Per Stack (%)", "% damage") + " plus " + D(p.F("Ruin Debuff Per Stack Extra Damage (%)")) + Bonus(p, "Ruin Damage Per Debuff Stack Per Stack (%)", "% damage") + " per debuff stack.";
            text += " Recharge after " + S.Utility(N(p.Definition.baseRechargeInterval) + " seconds") + Bonus(p, "Cooldown Per Stack", "s") + ".";
            return text;
        }
        private static string ExtraStats(Profile p)
        {
            string text = "\nCooldown Per Extra Item: " + S.Utility(N(p.F("Cooldown Per Stack")) + "s");
            if (p.Index < 2) text += "\nExplosion Radius: " + S.Utility(N(p.F("Explosion Radius")) + "m");
            if (p.Index == 0) text += "\nImpact Proc: " + S.Utility(N(p.F("Initial Hit Proc Coefficient"))) + "\nTracking Turn Rate: " + S.Utility(N(p.F("Tracking Aggression")) + " degrees/s");
            if (p.Index == 1) text += "\nExplosion Proc: " + S.Utility(N(p.F("Explosion Proc Coefficient"))) + "\nDamage Interval: " + S.Utility(N(p.F("Damage Interval")) + "s");
            if (p.Index == 2) text += "\nHealing Interval: " + S.Utility(N(p.F("Interval")) + "s");
            if (p.Index == 3) text += "\nApply Ruin On Cooldown: " + S.Utility(p.B("Can Apply Ruin On Cooldown") ? "Yes" : "No") + "\nRefresh Ruin Duration: " + S.Utility(p.B("Reapplying Resets Duration") ? "Yes" : "No");
            return text;
        }
    }
}
