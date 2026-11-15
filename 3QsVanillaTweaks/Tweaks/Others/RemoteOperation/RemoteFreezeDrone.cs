using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Audio;
using RoR2.ContentManagement;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using C = Config.ConfigManager;
using D = EntityStates.Drone.DroneCopycat;
using M = EntityStates.Mage.Weapon;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteFreezeDrone
    {
        private const string Root = RemoteDroneSkills.FreezeSection;
        private const string P = "3QSFrostDiscs", S = "Weapon", U = "3QSFrostField", X = "3QSFrostSpecial", H = "3QSFrostHold";
        private const string DiscName = "Frost Disc", NativeName = "Freeze Disc", FieldName = "Frost Field", SpearName = "Absolute Zero";
        internal static readonly Type[] States = { typeof(ThrowDisc), typeof(LaunchDisc), typeof(AimField), typeof(PlaceField),
            typeof(ChargeSpear), typeof(LaunchSpear), typeof(Overloaded), typeof(HoldingDiscs) };
        private static ConfigEntry<bool> enabled, discOn, nativeOn, fieldOn, spearOn, nativeLoS, fieldHoldFreeze;
        private static ConfigEntry<float> discDamage, discProc, discInterval, discRange, discSpeed, returnSpeed, discHitRadius,
            spinDamage, spinProc, spinInterval, spinRadius, spinFrostInterval, frostDuration,
            nativeDamage, nativeProc, nativeCooldown, nativeDuration, nativeRange, nativeSpeed, nativeBounceDamage,
            nativeTierSpeed, nativeTierProc, nativeTargetRange, nativeTargetAngle, nativeTargetFrequency,
            fieldRadius, fieldDuration, fieldSlow, fieldInterval, fieldArmour, fieldCooldown, fieldRange,
            fieldDamage, fieldDamageInterval, fieldProc, fieldExtension, fieldFrostArmour,
            chargeTime, holdTime, spearMinDamage, spearMaxDamage, spearMinRadius, spearMaxRadius, spearProc,
            spearSpeed, spearHitRadius, spearLifetime, freezeTime, frostBonus, frozenBonus,
            specialCooldown, overloadRate, overloadMax, shatterDamage, shatterRadius, shatterProc,
            iceRadius, iceDuration, iceInterval, iceExtension, iceExecuteThreshold;
        private static ConfigEntry<int> discFrost, spinFrost, nativeBounces, nativeTierBounces, fieldFrost, armourCap, shatterFrost, iceFrost;
        private static readonly Dictionary<SkillDef, StockOptions> stocks = new Dictionary<SkillDef, StockOptions>();
        private static SkillDef primary, secondary, utility, special;
        private static BuffDef armourBuff, slowBuff;
        private static GameObject visualPrefab, discArt, nativeCharge, discImpact, spearArt, spearCharge, spearImpact, spearMuzzle, nativeSpear;
        private static string discSound, chargeSound;
        private const string IceBurstSound = "Play_item_proc_iceRingSpear";
        private static Material frozenMaterial;
        private static Material fieldMistMaterial;
        private static float nativeChargeTime;
        private static Sprite offlineIcon;
        private static AsyncOperationHandle<GameObject> spearGhostHandle;
        private static bool assetsReady;
        [ThreadStatic] private static bool launchingNative;
        private static readonly Color Ice = new Color(.38f, .85f, 1f, .9f);
        private const float NativeFreezeThreshold = .3f;
        private static readonly List<FrostArea> fields = new List<FrostArea>();
        private static readonly List<Visual> clientFields = new List<Visual>();
        private static readonly System.Reflection.MethodInfo Cascade = AccessTools.Method(typeof(CharacterBody), "HandleCascadingBuffs");
        private static readonly System.Reflection.FieldInfo TrackerIndicator = AccessTools.Field(typeof(TargetTracker), "indicator");
        private static readonly System.Reflection.FieldInfo NativeInitialTarget = AccessTools.Field(typeof(D.FireLaserDisc), "initialOrbTarget");
        private static readonly System.Reflection.FieldInfo NativeHasFired = AccessTools.Field(typeof(D.FireLaserDisc), "hasFired");
        private static readonly System.Reflection.FieldInfo PendingState = AccessTools.Field(typeof(EntityStateMachine), "nextState");
        private static ConfigEntry<float> N(string group, string key, float value, string help) => RemoteDroneSkills.Number(Root + " - " + group, key, value, help);
        private static ConfigEntry<int> I(string group, string key, int value, string help) => RemoteDroneSkills.Number(Root + " - " + group, key, value, help);
        private static float F(ConfigEntry<float> value) => value == null ? 0f : Mathf.Max(0f, C.Get(value));
        private static float T(ConfigEntry<float> value) => Mathf.Max(.02f, F(value));
        private static int Count(ConfigEntry<int> value) => Mathf.Max(0, C.Get(value));
        private static float Now => Run.FixedTimeStamp.now.t;
        private static bool Alive(CharacterBody body) => body && body.healthComponent && body.healthComponent.alive;
        private static ConfigEntry<bool> Toggle(string group) => C.BindStartupBool(Root + " - " + group, "Changes Enabled", true,
            "Enable " + group + " for player-controlled Frost Drones. Restart to apply.");

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable player-controlled Frost Drone skills. AI drones retain their native skills. Restart to apply.");
            discOn = Toggle(DiscName); nativeOn = Toggle(NativeName); fieldOn = Toggle(FieldName); spearOn = Toggle(SpearName);
            frostDuration = N("Frost", "Stack Duration (s)", 8f, "Lifetime of each Frost stack applied by these skills. Uses the native Frost buff and its six-stack freeze interaction.");
            discDamage = N(DiscName, "Travel Damage (%)", 150f, "Damage once per enemy on the outward flight and once on the return.");
            discProc = N(DiscName, "Travel Proc Coefficient", .5f, "Proc coefficient of outward and return hits.");
            discInterval = N(DiscName, "Throw Interval (s)", .5f, "Minimum interval between throws at base attack speed. Attack speed reduces this interval.");
            discRange = N(DiscName, "Placement Range (m)", 30f, "Distance to each disc's hovering position. Terrain can shorten this distance.");
            discSpeed = N(DiscName, "Outward Speed (m/s)", 60f, "Outward flight speed.");
            returnSpeed = N(DiscName, "Return Speed (m/s)", 80f, "Homing speed when returning to the owner.");
            discHitRadius = N(DiscName, "Travel Hit Radius (m)", .75f, "Collision forgiveness for damaging enemies during travel.");
            discFrost = I(DiscName, "Travel Frost Stacks", 1, "Frost applied by each outward or return hit.");
            spinDamage = N(DiscName, "Spinning Damage Per Tick (%)", 50f, "Damage per spinning tick to every enemy in range.");
            spinProc = N(DiscName, "Spinning Proc Coefficient", .2f, "Proc coefficient of each spinning damage tick.");
            spinInterval = N(DiscName, "Spinning Tick Interval (s)", .25f, "Seconds between spinning damage ticks. Frost has its own interval.");
            spinRadius = N(DiscName, "Spinning Radius (m)", 3f, "Damage and Frost radius around each hovering disc.");
            spinFrost = I(DiscName, "Spinning Frost Stacks", 1, "Frost applied per spinning Frost tick, separately for each disc.");
            spinFrostInterval = N(DiscName, "Spinning Frost Interval (s)", 1f, "Seconds between Frost ticks. The first tick occurs when the disc starts hovering.");
            // Defaults verified against the supplied LaserDisc SkillDef and state configuration.
            nativeDamage = N(NativeName, "Damage (%)", 200f, "Native disc damage. Vanilla: 200%.");
            nativeCooldown = N(NativeName, "Cooldown (s)", 15f, "Recharge time after the disc is released.");
            nativeDuration = N(NativeName, "Cast Duration (s)", 1f, "Cast duration at base attack speed. Vanilla: 1s.");
            nativeBounces = I(NativeName, "Additional Bounces", 6, "Native additional bounce count before drone upgrades. Vanilla: 6.");
            nativeRange = N(NativeName, "Bounce Range (m)", 36f, "Native distance between targets. Vanilla: 36m.");
            nativeSpeed = N(NativeName, "Travel Speed (m/s)", 75f, "Native orb travel speed. Vanilla: 75m/s.");
            nativeProc = N(NativeName, "Proc Coefficient", 1f, "Native base proc coefficient. Vanilla: 1.");
            nativeBounceDamage = N(NativeName, "Damage Retained Per Bounce (%)", 100f, "Native damage multiplier on each bounce. Vanilla: 100%.");
            nativeTierBounces = I(NativeName, "Extra Bounces Per Drone Tier", 6, "Native upgrade scaling. Vanilla: 6.");
            nativeTierSpeed = N(NativeName, "Extra Speed Per Drone Tier (m/s)", 37.5f, "Native upgrade scaling. Vanilla: 37.5m/s.");
            nativeTierProc = N(NativeName, "Extra Proc Per Drone Tier", .5f, "Native upgrade scaling. Vanilla: 0.5.");
            nativeTargetRange = N(NativeName, "Targeting Range (m)", 60f, "Maximum native target acquisition distance. Vanilla: 60m.");
            nativeTargetAngle = N(NativeName, "Targeting Angle (degrees)", 30f, "Native target acquisition angle. Vanilla: 30 degrees.");
            nativeTargetFrequency = N(NativeName, "Targeting Updates Per Second", 3f, "Native target acquisition frequency. Vanilla: 3.");
            nativeLoS = C.BindBool(Root + " - " + NativeName, "Require Line Of Sight", true, "Require line of sight to acquire the initial target. Vanilla: True.");
            fieldRadius = N(FieldName, "Radius (m)", 18f, "Radius of the stationary field.");
            fieldDuration = N(FieldName, "Duration (s)", 8f, "Field duration.");
            fieldSlow = N(FieldName, "Slow (%)", 50f, "Additional movement slow while an enemy is in the field.");
            fieldFrost = I(FieldName, "Frost Stacks Per Tick", 1, "Frost applied immediately and at each interval.");
            fieldInterval = N(FieldName, "Frost Interval (s)", 1f, "Seconds between Frost applications.");
            fieldDamage = N(FieldName, "Damage Per Tick (%)", 25f, "Damage dealt to each enemy inside the field per damage tick.");
            fieldDamageInterval = N(FieldName, "Damage Tick Interval (s)", .5f, "Seconds between damage ticks, independently of Frost applications.");
            fieldProc = N(FieldName, "Proc Coefficient", .2f, "Proc coefficient of each damage tick.");
            fieldExtension = N(FieldName, "Frozen Execution Extension (s)", 1.5f, "Time added when an enemy is executed by Freeze inside the field.");
            fieldArmour = N(FieldName, "Armour Per Frozen Enemy", 15f, "Armour granted to the owner per distinct frozen enemy inside their utility fields.");
            fieldFrostArmour = N(FieldName, "Armour Per Frost Stack", 2f, "Additional armour granted for each Frost stack on enemies inside your utility fields.");
            fieldHoldFreeze = C.BindBool(Root + " - " + FieldName, "Keep Enemies Frozen", true, "Enemies already Frozen cannot thaw while inside the blizzard.");
            armourCap = I(FieldName, "Maximum Counted Enemies", 0, "Maximum frozen enemies contributing armour. 0 means no limit.");
            fieldCooldown = N(FieldName, "Cooldown (s)", 15f, "Cooldown starts when the field is deployed.");
            fieldRange = N(FieldName, "Placement Range (m)", 100f, "Maximum terrain targeting range.");
            chargeTime = N(SpearName, "Charge Time (s)", 5f, "Real time to fully charge. Attack speed does not shorten this.");
            holdTime = N(SpearName, "Full Charge Hold (s)", 5f, "Additional hold time before automatically firing.");
            spearMinDamage = N(SpearName, "Minimum Damage (%)", 1000f, "Explosion damage at minimum charge.");
            spearMaxDamage = N(SpearName, "Maximum Damage (%)", 5000f, "Explosion damage at full charge.");
            spearMinRadius = N(SpearName, "Minimum Radius (m)", 8f, "Explosion radius at minimum charge.");
            spearMaxRadius = N(SpearName, "Maximum Radius (m)", 25f, "Explosion radius at full charge.");
            spearProc = N(SpearName, "Proc Coefficient", 1f, "Proc coefficient of the spear explosion.");
            spearSpeed = N(SpearName, "Travel Speed (m/s)", 120f, "Projectile travel speed. Native Nano-Spear: 120m/s.");
            spearHitRadius = N(SpearName, "Hit Radius (m)", .75f, "Collision forgiveness. The spear explodes on the first enemy or terrain impact.");
            spearLifetime = N(SpearName, "Lifetime (s)", 10f, "Maximum travel time. Native Nano-Spear: 10s.");
            freezeTime = N(SpearName, "Freeze Duration (s)", 2f, "Direct Freeze duration, subject to native immunities.");
            frostBonus = N(SpearName, "Damage Bonus Per Frost Stack (%)", 10f, "Bonus evaluated before damage and Freeze are applied.");
            frozenBonus = N(SpearName, "Damage Bonus Against Frozen (%)", 100f, "Replaces the per-Frost-stack bonus for already frozen targets.");
            specialCooldown = N(SpearName, "Cooldown After Overload (s)", 25f, "Cooldown starts when the post-shot overload ends.");
            overloadRate = N(SpearName, "Overload Per Charge Second (s)", 1f, "Weapon lockout per second actually spent charging. Held full-charge time is excluded.");
            overloadMax = N(SpearName, "Maximum Overload (s)", 5f, "Maximum weapon lockout. Utility remains usable.");
            shatterDamage = N(SpearName, "Shatter Damage (%)", 300f, "Damage of execution shatter explosions.");
            shatterRadius = N(SpearName, "Shatter Radius (m)", 8f, "Radius of each shatter.");
            shatterProc = N(SpearName, "Shatter Proc Coefficient", .5f, "Proc coefficient of shatter explosions.");
            shatterFrost = I(SpearName, "Shatter Frost Stacks", 3, "Frost applied by each shatter. Each enemy can shatter once per cast.");
            iceRadius = N(SpearName, "Full Charge Field Radius (m)", 20f, "Radius of the icy field left by a fully charged impact.");
            iceDuration = N(SpearName, "Full Charge Field Duration (s)", 12f, "Duration of the full-charge icy field.");
            iceExtension = N(SpearName, "Full Charge Field Execution Extension (s)", 1.5f, "Time added when an enemy is executed by Freeze inside the full-charge field.");
            iceExecuteThreshold = N(SpearName, "Full Charge Field Freeze Execute Threshold (%)", 50f, "Frozen non-boss enemies inside the full-charge field can be executed at this combined health percentage. The native minimum is 30%.");
            iceFrost = I(SpearName, "Full Charge Field Frost Stacks", 1, "Frost applied immediately and at each field interval.");
            iceInterval = N(SpearName, "Full Charge Field Frost Interval (s)", 1f, "Seconds between full-charge field Frost applications.");
        }

        private sealed class StockOptions
        {
            internal ConfigEntry<int> Maximum, Required, Consumed, Recharged;
            internal StockOptions(string group, int maximum)
            {
                Maximum = I(group, "Base Stocks", maximum, "Maximum skill stocks.");
                Required = I(group, "Stocks Required", 1, "Stocks required to use the skill.");
                Consumed = I(group, "Stocks Consumed", 1, "Stocks consumed per use. A returning primary disc refunds the stocks spent on that disc.");
                Recharged = I(group, "Stocks Recharged", 1, "Stocks restored per cooldown. The primary restores its spent stocks when caught instead.");
            }
            internal void Apply(SkillDef def)
            {
                def.baseMaxStock = Mathf.Max(1, Count(Maximum)); def.requiredStock = Mathf.Max(1, Count(Required));
                def.stockToConsume = Count(Consumed); def.rechargeStock = Mathf.Max(1, Count(Recharged));
            }
        }
        private static SkillDef Skill(string name, string title, Type state, string machine, float cooldown, SkillDef def, int maximum = 1, bool onEnd = false)
        {
            SkillDef result = RemoteDroneSkills.MakeSkill(name, title, "", state, machine, cooldown, null,
                beginsOnEnd: onEnd, customDefinition: def, useStockConfig: false);
            stocks[result] = new StockOptions(title, maximum); stocks[result].Apply(result);
            return result;
        }
        internal static BuffDef[] Buffs => armourBuff ? new[] { armourBuff, slowBuff } : Array.Empty<BuffDef>();
        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = Skill("FreezeDisc", DiscName, typeof(ThrowDisc), P, 0f, ScriptableObject.CreateInstance<DiscSkill>(), 3);
            secondary = Skill("FreezeNative", NativeName, typeof(D.FireLaserDisc), S, F(nativeCooldown), ScriptableObject.CreateInstance<NativeSkill>(), onEnd: true);
            utility = Skill("FreezeField", FieldName, typeof(AimField), U, F(fieldCooldown), ScriptableObject.CreateInstance<FieldSkill>(), onEnd: true);
            special = Skill("FreezeSpear", SpearName, typeof(ChargeSpear), X, F(specialCooldown), ScriptableObject.CreateInstance<SpearSkill>(), onEnd: true);
            primary.hideCooldown = true;
            primary.isCooldownBlockedUntilManuallyReset = true;
            secondary.mustKeyPress = utility.mustKeyPress = special.mustKeyPress = true;
            armourBuff = ScriptableObject.CreateInstance<BuffDef>();
            armourBuff.name = "3QSFrostFieldArmour"; armourBuff.buffColor = Ice; armourBuff.canStack = true;
            slowBuff = ScriptableObject.CreateInstance<BuffDef>();
            slowBuff.name = "3QSFrostFieldSlow"; slowBuff.buffColor = Ice; slowBuff.isDebuff = true;
            R2API.LanguageAPI.Add("3QS_REMOTE_FROST_KEYWORD", "<style=cKeywordName>Frost</style><style=cSub>Stacks slow enemies. Six stacks trigger the native freeze interaction.</style>");
            primary.keywordTokens = utility.keywordTokens = new[] { "3QS_REMOTE_FROST_KEYWORD" };
            special.keywordTokens = new[] { "KEYWORD_FREEZING", "3QS_REMOTE_FROST_KEYWORD" };
            Refresh();
        }
        internal static void Refresh()
        {
            if (!primary) return;
            foreach (var pair in stocks) pair.Value.Apply(pair.Key);
            if (RemoteDroneSkills.FreezeBody) ConfigureTracker(RemoteDroneSkills.FreezeBody.GetComponent<TargetTracker>());
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
                if (body && body.GetComponent<Control>()) ConfigureTracker(body.GetComponent<TargetTracker>());
            secondary.baseRechargeInterval = F(nativeCooldown); utility.baseRechargeInterval = F(fieldCooldown); special.baseRechargeInterval = F(specialCooldown);
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"Throw a disc for <style=cIsDamage>{F(discDamage):0.##}% damage</style> and <style=cIsUtility>{Count(discFrost)} Frost</style> on the way out and back. Hold to keep discs spinning, dealing <style=cIsDamage>{F(spinDamage):0.##}% damage every {F(spinInterval):0.##}s</style> and applying <style=cIsUtility>{Count(spinFrost)} Frost every {F(spinFrostInterval):0.##}s</style>. Release to recall them and <style=cIsUtility>restore their stocks</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Fire a <style=cIsUtility>Freezing</style> disc for <style=cIsDamage>{F(nativeDamage):0.##}% damage</style>, bouncing up to <style=cIsUtility>{Count(nativeBounces)} additional times</style>. Drone upgrades improve its bounces, speed, and proc coefficient.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Deploy a blizzard for <style=cIsUtility>{F(fieldDuration):0.##}s</style>. Enemies inside take <style=cIsDamage>{F(fieldDamage):0.##}% damage every {F(fieldDamageInterval):0.##}s</style>, are <style=cIsUtility>slowed by {Mathf.Clamp(F(fieldSlow), 0f, 99f):0.##}%</style>, and gain <style=cIsUtility>{Count(fieldFrost)} Frost every {F(fieldInterval):0.##}s</style>.{(C.Get(fieldHoldFreeze) ? " <style=cIsUtility>Frozen</style> enemies cannot thaw inside." : "")} Gain <style=cIsUtility>{F(fieldFrostArmour):0.##} armour per Frost stack</style> and <style=cIsUtility>{F(fieldArmour):0.##} armour per frozen enemy</style> inside your blizzards. Frozen executions inside extend the field by <style=cIsUtility>{F(fieldExtension):0.##}s</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Charge an ice spear that explodes for <style=cIsDamage>{F(spearMinDamage):0.##}–{F(spearMaxDamage):0.##}% damage</style> and <style=cIsUtility>Freezes</style> enemies for <style=cIsUtility>{F(freezeTime):0.##}s</style>. Deal <style=cIsDamage>{F(frostBonus):0.##}% more damage per Frost stack</style>, or <style=cIsDamage>{F(frozenBonus):0.##}% more</style> against frozen enemies. Executions shatter for <style=cIsDamage>{F(shatterDamage):0.##}% damage</style> and <style=cIsUtility>{Count(shatterFrost)} Frost</style>, causing further shatters. Full charge leaves an icy field for <style=cIsUtility>{F(iceDuration):0.##}s</style>, applying <style=cIsUtility>{Count(iceFrost)} Frost every {F(iceInterval):0.##}s</style>.{(IceThreshold > NativeFreezeThreshold ? $" Frozen non-boss enemies inside can be <style=cIsDamage>executed at {IceThreshold * 100f:0.##}% health</style>." : "")} Frozen executions inside extend the field by <style=cIsUtility>{F(iceExtension):0.##}s</style>. <style=cDeath>Overloads your weapons for up to {F(overloadMax):0.##}s</style> after firing.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () => $"\nThrow Interval: {F(discInterval):0.##}s / attack speed\nPlacement Range: {F(discRange):0.##}m\nSpinning Radius: {F(spinRadius):0.##}m\nSpinning Proc Coefficient: {F(spinProc):0.##}\nTravel Frost: {Count(discFrost)}", () => F(discProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () => $"\nBounce Range: {F(nativeRange):0.##}m\nAdditional Bounces Per Drone Tier: {Count(nativeTierBounces)}\nAdditional Proc Per Drone Tier: {F(nativeTierProc):0.##}", () => F(nativeProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility, () => $"\nRadius: {F(fieldRadius):0.##}m\nFrozen Enemy Armour Limit: {(Count(armourCap) == 0 ? "Unlimited" : Count(armourCap).ToString())}", () => F(fieldProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () => $"\nCharge Time: {F(chargeTime):0.##}s\nFull Charge Hold: {F(holdTime):0.##}s\nRadius: {F(spearMinRadius):0.##}–{F(spearMaxRadius):0.##}m\nShatter Radius: {F(shatterRadius):0.##}m\nShatter Proc Coefficient: {F(shatterProc):0.##}\nIcy Field Radius: {F(iceRadius):0.##}m\nIcy Field Frost: {Count(iceFrost)} every {F(iceInterval):0.##}s", () => F(spearProc));
        }

        internal static void Install(GameObject body)
        {
            if (!primary) return;
            SkillDef native = body.GetComponent<SkillLocator>().primary.skillFamily.variants[0].skillDef;
            primary.icon = IceIcon(RemoteDroneSkills.VanillaIcon("HuntressBody", "Secondary", native.icon));
            secondary.icon = native.icon;
            utility.icon = IceIcon(RemoteDroneSkills.VanillaIcon("MageBody", "Utility", native.icon));
            special.icon = IceIcon(RemoteDroneSkills.VanillaIcon("MageBody", "Secondary", native.icon, 1));
            secondary.cancelSprintingOnActivation = native.cancelSprintingOnActivation;
            secondary.mustKeyPress = native.mustKeyPress;
            secondary.interruptPriority = native.interruptPriority;
            secondary.attackSpeedBuffsRestockSpeed = native.attackSpeedBuffsRestockSpeed;
            foreach (string machine in new[] { P, U, X, H }) RemoteDroneSkills.AddMachine(body, machine);
            if (discOn.Value) RemoteDroneSkills.SetSkill(body, "Primary", primary);
            if (nativeOn.Value) RemoteDroneSkills.SetSkill(body, "Secondary", secondary);
            if (nativeOn.Value) ConfigureTracker(body.GetComponent<TargetTracker>());
            if (fieldOn.Value) RemoteDroneSkills.SetSkill(body, "Utility", utility);
            if (spearOn.Value) RemoteDroneSkills.SetSkill(body, "Special", special);
            if (!body.GetComponent<Control>()) body.AddComponent<Control>();
            RoR2Application.onLoad += LoadAssets;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteFreezeDrone");
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)), prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(CheckSkill)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)), prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(CheckSkill)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(GenericSkill), nameof(GenericSkill.icon)), postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(LockedIcon)));
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)), postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(Stats)));
            harmony.Patch(AccessTools.Method(typeof(D.FireLaserDisc), nameof(D.FireLaserDisc.OnEnter)), postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(NativeCastDuration)));
            harmony.Patch(AccessTools.Method(typeof(D.FireLaserDisc), "Fire"), prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(NativeScope)), finalizer: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(EndNativeScope)));
            harmony.Patch(AccessTools.Method(typeof(D.LaserDiscOrb), nameof(D.LaserDiscOrb.Begin)), prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(NativeOrb)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.TakeDamage)), prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(CaptureFrozen)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), "TakeDamageProcess"), transpiler: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(FreezeExecuteThreshold)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.GetHealthBarValues)), postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(FreezeCullBar)));
            harmony.Patch(AccessTools.Method(typeof(FrozenState), nameof(FrozenState.FixedUpdate)),
                prefix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(HoldFreeze)),
                postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(RestoreFreezeDuration)));
            harmony.Patch(AccessTools.Method(typeof(GlobalEventManager), "OnCharacterDeath"), postfix: new HarmonyMethod(typeof(RemoteFreezeDrone), nameof(AfterDeath)));
            GlobalEventManager.onServerCharacterExecuted += ExtendFields;
        }
        private static void ConfigureTracker(TargetTracker tracker)
        {
            if (!tracker || !nativeOn.Value) return;
            tracker.maxTrackingDistance = F(nativeTargetRange); tracker.maxTrackingAngle = F(nativeTargetAngle);
            tracker.trackerUpdateFrequency = T(nativeTargetFrequency); tracker.useLoS = C.Get(nativeLoS);
        }
        private static void LoadAssets()
        {
            if (assetsReady) return;
            var preload = AccessTools.Method(typeof(EntityStatePreloadCatalog), "LoadEntityStateContent");
            preload.Invoke(null, new[] { (object)EntityStateCatalog.GetStateIndex(typeof(D.FireLaserDisc)), Enum.ToObject(preload.GetParameters()[1].ParameterType, 0) });
            discArt = RemoteDroneSkills.NativeGameObject(new D.FireLaserDisc(), "orbEffectPrefab");
            nativeCharge = RemoteDroneSkills.NativeGameObject(new D.FireLaserDisc(), "chargePrefab");
            discSound = D.FireLaserDisc.attackSoundString;
            if (discArt && discArt.GetComponent<OrbEffect>()) discImpact = discArt.GetComponent<OrbEffect>().endEffect;
            var charge = new M.ChargeIcebomb();
            spearCharge = charge.chargeEffectPrefab; chargeSound = charge.chargeSoundString;
            nativeChargeTime = Mathf.Max(.02f, charge.baseDuration);
            frozenMaterial = LegacyResourcesAPI.Load<Material>("Materials/matIsFrozen");
            var launch = new M.ThrowIcebomb(); nativeSpear = launch.projectilePrefab; spearMuzzle = launch.muzzleflashEffectPrefab;
            if (nativeSpear)
            {
                var pc = nativeSpear.GetComponent<ProjectileController>();
                var address = AccessTools.Field(typeof(ProjectileController), "ghostPrefabAddress").GetValue(pc) as AssetReference;
                if (address != null && address.RuntimeKeyIsValid())
                {
                    spearGhostHandle = Addressables.LoadAssetAsync<GameObject>(address.RuntimeKey);
                    spearArt = spearGhostHandle.WaitForCompletion();
                }
                else spearArt = AccessTools.Field(typeof(ProjectileController), "ghostPrefab").GetValue(pc) as GameObject;
                var overlap = nativeSpear.GetComponent<ProjectileOverlapAttack>();
                if (overlap) spearImpact = overlap.impactEffect;
            }
            if (armourBuff && RoR2Content.Buffs.ArmorBoost) armourBuff.iconSprite = RoR2Content.Buffs.ArmorBoost.iconSprite;
            if (slowBuff && RoR2Content.Buffs.Slow50) slowBuff.iconSprite = RoR2Content.Buffs.Slow50.iconSprite;
            assetsReady = discArt && spearArt && spearCharge && spearImpact;
            if (!assetsReady) ThreeQsVanillaTweaks.Log.Warning("Remote Frost Drone: a native disc or Nano-Spear visual is unavailable.");
        }
        private static bool NativeState(D.FireLaserDisc state)
        {
            var body = state.outer ? state.outer.GetComponent<CharacterBody>() : null;
            return body && body.GetComponent<Control>() && body.skillLocator.secondary && body.skillLocator.secondary.skillDef == secondary && body.skillLocator.secondary.stateMachine == state.outer;
        }
        private static void NativeCastDuration(D.FireLaserDisc __instance)
        {
            if (!NativeState(__instance)) return;
            var body = __instance.outer.GetComponent<CharacterBody>();
            float duration = T(nativeDuration) / Mathf.Max(.01f, body.attackSpeed);
            AccessTools.Field(typeof(D.FireLaserDisc), "duration").SetValue(__instance, duration);
            var fx = AccessTools.Field(typeof(D.FireLaserDisc), "chargeEffect").GetValue(__instance) as GameObject;
            if (fx && fx.GetComponent<ObjectScaleCurve>()) fx.GetComponent<ObjectScaleCurve>().timeMax = duration;
        }
        private static void NativeScope(D.FireLaserDisc __instance, out bool __state)
        { __state = launchingNative; launchingNative = NativeState(__instance); }
        private static Exception EndNativeScope(bool __state, Exception __exception) { launchingNative = __state; return __exception; }
        private static void NativeOrb(D.LaserDiscOrb __instance)
        {
            if (!launchingNative || !__instance.attacker) return;
            var body = __instance.attacker.GetComponent<CharacterBody>();
            if (!body) return;
            int tier = body.inventory ? body.inventory.GetItemCountEffective(DLC3Content.Items.DroneUpgradeHidden) : 0;
            __instance.damageValue = body.damage * F(nativeDamage) / 100f;
            __instance.bouncesRemaining = Count(nativeBounces) + tier * Count(nativeTierBounces);
            __instance.speed = Mathf.Max(.01f, F(nativeSpeed) + tier * F(nativeTierSpeed));
            __instance.range = F(nativeRange); __instance.procCoefficient = F(nativeProc) + tier * F(nativeTierProc);
            __instance.damageCoefficientPerBounce = F(nativeBounceDamage) / 100f;
            __instance.damageType.damageSource = DamageSource.Secondary;
        }
        private static bool CheckSkill(GenericSkill __instance, ref bool __result)
        {
            var c = __instance.GetComponent<Control>();
            if (!c || !c.Locked(__instance)) return true;
            __result = false; return false;
        }
        private static void LockedIcon(GenericSkill __instance, ref Sprite __result)
        {
            var c = __instance.GetComponent<Control>();
            if (!c || !c.Locked(__instance, includeCharge: false)) return;
            if (!offlineIcon) offlineIcon = SkillCatalog.allSkillDefs.OfType<RailgunSkillDef>().Select(d => d.offlineIcon).FirstOrDefault(s => s);
            if (offlineIcon) __result = offlineIcon;
        }
        private static void Stats(CharacterBody __instance)
        {
            var c = __instance.GetComponent<Control>();
            if (c && c.BonusArmour > 0f)
                AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.armor)).SetValue(__instance, __instance.armor + c.BonusArmour);
            float slow = slowBuff && __instance.HasBuff(slowBuff) ? Mathf.Clamp(F(fieldSlow), 0f, 99f) : 0f;
            if (slow > 0f) AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.moveSpeed)).SetValue(__instance, __instance.moveSpeed * (1f - slow / 100f));
        }
        private static HurtBox[] Around(CharacterBody owner, Vector3 point, float radius)
        {
            if (!Alive(owner) || radius <= 0f) return Array.Empty<HurtBox>();
            return new SphereSearch { origin = point, radius = radius, mask = LayerIndex.entityPrecise.mask }.RefreshCandidates()
                .FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex))
                .OrderCandidatesByDistance().FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
        }
        private static void Frost(CharacterBody target, int count)
        {
            if (!NetworkServer.active || !Alive(target) || count <= 0 || F(frostDuration) <= 0f) return;
            for (int i = 0; i < count; i++) target.AddTimedBuff(DLC2Content.Buffs.Frost, F(frostDuration));
            // Let the native implementation consume stacks and enforce its own
            // immunities, Oiled interaction, and non-freezable-target behaviour.
            Cascade.Invoke(target, null);
            CommitFreeze(target);
        }
        private static void CommitFreeze(CharacterBody body)
        {
            var hurt = body ? body.GetComponent<SetStateOnHurt>() : null;
            var machine = hurt ? hurt.targetStateMachine : null;
            // SetFrozen queues the native state. Enter that accepted state before
            // damage so the ordinary frozen execution check sees this first hit.
            if (machine && Util.HasEffectiveAuthority(machine.gameObject) && PendingState.GetValue(machine) is FrozenState frozen)
                machine.SetState(frozen);
        }
        private static bool CanFreeze(CharacterBody body)
        {
            var hurt = body ? body.GetComponent<SetStateOnHurt>() : null;
            return hurt && hurt.canBeFrozen && !body.HasBuff(DLC2Content.Buffs.FreezeImmune);
        }
        private static bool ContainsEnemy(CharacterBody owner, CharacterBody enemy, Vector3 center, float radius)
        {
            return Alive(owner) && owner.teamComponent && enemy && enemy.teamComponent &&
                (enemy.corePosition - center).sqrMagnitude <= radius * radius &&
                TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex).HasTeam(enemy.teamComponent.teamIndex);
        }
        private static bool InField(CharacterBody enemy, bool utilityField)
        {
            if (NetworkServer.active)
            {
                foreach (FrostArea field in fields)
                    if (field && field.Utility == utilityField && Now < field.End &&
                        ContainsEnemy(field.Owner, enemy, field.transform.position, field.Radius)) return true;
            }
            else
            {
                Kind kind = utilityField ? Kind.UtilityField : Kind.IcyField;
                foreach (Visual field in clientFields)
                    if (field && field.Kind == kind && Now < field.Born + field.Lifetime &&
                        ContainsEnemy(field.Owner, enemy, field.transform.position, field.Radius)) return true;
            }
            return false;
        }
        private static void HoldFreeze(FrozenState __instance, ref float ___duration, TemporaryOverlayInstance ___temporaryOverlay, out float __state)
        {
            __state = ___duration;
            if (!C.Get(fieldHoldFreeze) || !InField(__instance.characterBody, utilityField: true)) return;
            // Postpone only the native timeout. Exiting the blizzard restores the original thaw deadline.
            ___duration = Mathf.Max(___duration, __instance.fixedAge + .05f);
            if (___temporaryOverlay != null)
                ___temporaryOverlay.duration = Mathf.Max(___temporaryOverlay.duration, __instance.age + .25f);
        }
        private static void RestoreFreezeDuration(ref float ___duration, float __state) => ___duration = __state;
        private static float IceThreshold => Mathf.Clamp(F(iceExecuteThreshold) / 100f, NativeFreezeThreshold, 1f);
        private static float FrozenThreshold(HealthComponent health)
        {
            var body = health.body;
            return body && !body.isBoss && IceThreshold > NativeFreezeThreshold && InField(body, utilityField: false)
                ? IceThreshold : NativeFreezeThreshold;
        }
        private static IEnumerable<CodeInstruction> FreezeExecuteThreshold(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var frozenEffect = AccessTools.Field(typeof(FrozenState), nameof(FrozenState.executeEffectPrefab));
            int effect = code.FindIndex(i => i.opcode == OpCodes.Ldsfld && Equals(i.operand, frozenEffect));
            var constants = new List<int>();
            // The Railgunner tweaks can replace the frozen getter. Anchor to the native freeze effect.
            for (int i = Math.Max(0, effect - 7); i < effect; i++)
                if (code[i].opcode == OpCodes.Ldc_R4 && Equals(code[i].operand, NativeFreezeThreshold)) constants.Add(i);
            if (constants.Count != 2)
            {
                ThreeQsVanillaTweaks.Log.Warning("Remote Frost Drone: the native Freeze execution threshold could not be patched.");
                return code;
            }
            var threshold = AccessTools.Method(typeof(RemoteFreezeDrone), nameof(FrozenThreshold));
            for (int i = constants.Count - 1; i >= 0; i--)
            {
                int index = constants[i];
                // Keep the original instruction and its labels for branches into the freeze block.
                code[index].opcode = OpCodes.Ldarg_0;
                code[index].operand = null;
                code.Insert(index + 1, new CodeInstruction(OpCodes.Call, threshold));
            }
            return code;
        }
        private static void FreezeCullBar(HealthComponent __instance, ref HealthComponent.HealthBarValues __result)
        {
            if (__result.cullFraction > 0f)
                __result.cullFraction = Mathf.Clamp01(__result.cullFraction * FrozenThreshold(__instance) / NativeFreezeThreshold);
        }
        private sealed class Control : NetworkBehaviour
        {
            internal readonly List<DiscFlight> Discs = new List<DiscFlight>();
            internal readonly List<CastContext> Casts = new List<CastContext>();
            internal float NextThrow, OverloadUntil;
            internal bool Charging;
            internal bool Holding;
            private int frozenCount, frostStacks;
            internal float BonusArmour => frozenCount * F(fieldArmour) + frostStacks * F(fieldFrostArmour);
            private int returned, observedReturns;
            private float nextArmourUpdate;
            private void LateUpdate()
            {
                var body = GetComponent<CharacterBody>();
                if (!body || !body.hasEffectiveAuthority || !body.skillLocator) return;
                var skill = body.skillLocator.secondary;
                if (!skill || skill.skillDef != secondary) return;
                var tracker = GetComponent<TargetTracker>();
                var indicator = tracker ? TrackerIndicator.GetValue(tracker) as Indicator : null;
                if (indicator == null) return;

                // The native cast locks its target on entry, even if the player aims away.
                var cast = skill.stateMachine ? skill.stateMachine.state as D.FireLaserDisc : null;
                var target = cast != null ? NativeInitialTarget.GetValue(cast) as HurtBox : tracker.GetTrackingTarget();
                indicator.targetTransform = target ? target.transform : null;
                indicator.active = Alive(body) && target && (cast != null ? !(bool)NativeHasFired.GetValue(cast) : skill.IsReady());
            }
            internal float Remaining => Mathf.Max(0f, OverloadUntil - Now);
            internal bool Locked(GenericSkill skill, bool includeCharge = true)
            {
                var slots = GetComponent<SkillLocator>();
                return slots && skill != slots.utility && (Remaining > 0f || (includeCharge && Charging));
            }
            internal void StartOverload(float duration)
            { OverloadUntil = Now + duration; if (NetworkServer.active) SetDirtyBit(1u); }
            internal void Return(DiscFlight disc)
            {
                Discs.Remove(disc);
                returned += disc.Spent;
                SetDirtyBit(1u);
            }
            private void FixedUpdate()
            {
                var body = GetComponent<CharacterBody>();
                if (!Alive(body)) return;
                if (body.hasEffectiveAuthority && body.skillLocator.primary && body.skillLocator.primary.skillDef == primary)
                {
                    EntityStateMachine inputMachine = GetComponents<EntityStateMachine>().FirstOrDefault(m => m.customName == H);
                    var input = GetComponent<InputBankTest>();
                    if (inputMachine && input && input.skill1.down && inputMachine.state is Idle)
                        inputMachine.SetNextState(new HoldingDiscs());
                }
                // GenericSkill stocks are locally simulated. Refund the same
                // cumulative server credits on the owning client and on the host.
                if (body.hasEffectiveAuthority && returned != observedReturns)
                {
                    int delta = returned - observedReturns;
                    observedReturns = returned;
                    var skill = body.skillLocator.primary;
                    if (delta > 0 && skill && skill.skillDef == primary) skill.AddStocks(delta);
                }
                if (!NetworkServer.active) return;
                foreach (DiscFlight disc in Discs.ToArray()) if (!disc) Discs.Remove(disc);
                foreach (CastContext cast in Casts.ToArray())
                {
                    cast.Drain();
                    if (Now >= cast.Expires && cast.Pending.Count == 0) Casts.Remove(cast);
                }
                if (Now < nextArmourUpdate) return;
                nextArmourUpdate = Now + .1f;
                var enemies = new HashSet<HealthComponent>();
                int frozen = 0, stacksInFields = 0;
                foreach (FrostArea field in fields)
                    if (field && field.Utility && field.Owner == body && Now < field.End)
                        foreach (HurtBox box in Around(body, field.transform.position, field.Radius))
                        {
                            HealthComponent enemy = box.healthComponent;
                            if (!enemy.alive || !ContainsEnemy(body, enemy.body, field.transform.position, field.Radius) || !enemies.Add(enemy)) continue;
                            if (enemy.isInFrozenState) frozen++;
                            stacksInFields += enemy.body.GetBuffCount(DLC2Content.Buffs.Frost);
                        }
                int count = Count(armourCap) > 0 ? Math.Min(frozen, Count(armourCap)) : frozen;
                if (count == frozenCount && stacksInFields == frostStacks) return;
                frozenCount = count; frostStacks = stacksInFields; SetDirtyBit(1u);
                body.SetBuffCount(armourBuff.buffIndex, Mathf.CeilToInt(BonusArmour)); body.MarkAllStatsDirty();
            }
            private void OnDisable()
            {
                if (!NetworkServer.active) return;
                foreach (DiscFlight disc in Discs.ToArray()) if (disc) NetworkServer.Destroy(disc.gameObject);
                foreach (FrostArea field in fields.ToArray())
                    if (field && field.Owner && field.Owner.gameObject == gameObject) NetworkServer.Destroy(field.gameObject);
            }
            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState) writer.WritePackedUInt32(syncVarDirtyBits);
                if (!initialState && syncVarDirtyBits == 0u) return false;
                writer.Write(returned); writer.Write(frozenCount); writer.Write(frostStacks); writer.Write(OverloadUntil); return true;
            }
            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (!initialState && reader.ReadPackedUInt32() == 0u) return;
                returned = reader.ReadInt32();
                int previousFrozen = frozenCount, previousStacks = frostStacks;
                frozenCount = reader.ReadInt32(); frostStacks = reader.ReadInt32(); OverloadUntil = reader.ReadSingle();
                if ((previousFrozen != frozenCount || previousStacks != frostStacks) && GetComponent<CharacterBody>())
                    GetComponent<CharacterBody>().MarkAllStatsDirty();
            }
        }
        private sealed class DiscSkill : SkillDef
        {
            public override void OnFixedUpdate(GenericSkill skill, float dt) { }
            public override bool IsReady(GenericSkill skill) => base.IsReady(skill) && ThrowReady(skill);
            public override bool CanExecute(GenericSkill skill) => base.CanExecute(skill) && ThrowReady(skill);
            private static bool ThrowReady(GenericSkill skill)
            {
                var c = skill.GetComponent<Control>();
                return c && Now >= c.NextThrow && !c.Locked(skill) && c.Discs.Count < skill.maxStock;
            }
        }
        public sealed class HoldingDiscs : BaseState
        {
            public override void OnEnter() { base.OnEnter(); var c = GetComponent<Control>(); if (c) c.Holding = true; }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && (!inputBank || !inputBank.skill1.down)) outer.SetNextStateToMain();
            }
            public override void OnExit() { var c = GetComponent<Control>(); if (c) c.Holding = false; base.OnExit(); }
        }
        private sealed class NativeSkill : SkillDef
        {
            public override bool IsReady(GenericSkill skill) => base.IsReady(skill) && HasTarget(skill);
            public override bool CanExecute(GenericSkill skill) => base.CanExecute(skill) && HasTarget(skill);
            private static bool HasTarget(GenericSkill skill)
            {
                var tracker = skill.GetComponent<TargetTracker>();
                return tracker && tracker.GetTrackingTarget();
            }
        }
        private sealed class FieldSkill : SkillDef
        {
            public override bool IsReady(GenericSkill skill) => base.IsReady(skill) && HasTerrain(skill);
            public override bool CanExecute(GenericSkill skill) => base.CanExecute(skill) && HasTerrain(skill);
            private static bool HasTerrain(GenericSkill skill)
            {
                var input = skill.GetComponent<InputBankTest>();
                return input && Ground(input.GetAimRay(), F(fieldRange), out _);
            }
        }
        private sealed class SpearSkill : SkillDef
        {
            public override void OnFixedUpdate(GenericSkill skill, float dt)
            {
                var c = skill.GetComponent<Control>();
                if (!c || (!c.Charging && c.Remaining <= 0f)) base.OnFixedUpdate(skill, dt);
            }
        }
        public sealed class ThrowDisc : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                var control = GetComponent<Control>();
                if (control) control.NextThrow = Now + T(discInterval) / Mathf.Max(.01f, attackSpeedStat);
                if (isAuthority) outer.SetNextState(new LaunchDisc { Aim = GetAimRay() });
            }
        }
        public sealed class LaunchDisc : BaseState
        {
            internal Ray Aim;
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Aim.origin); writer.Write(Aim.direction); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Aim = new Ray(reader.ReadVector3(), reader.ReadVector3()); }
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                {
                    var control = GetComponent<Control>();
                    if (control && control.Discs.Count < characterBody.skillLocator.primary.maxStock)
                    {
                        float distance = F(discRange);
                        if (Ground(Aim, distance, out RaycastHit hit)) distance = Mathf.Max(0f, hit.distance - .3f);
                        Vector3 origin = FindModelChild("Muzzle") ? FindModelChild("Muzzle").position : characterBody.corePosition;
                        Visual visual = Spawn(Kind.Disc, characterBody, origin, 0f, 0f);
                        var flight = visual.gameObject.AddComponent<DiscFlight>();
                        flight.Owner = characterBody; flight.Control = control; flight.Destination = Aim.GetPoint(distance);
                        flight.Spent = Mathf.Max(0, primary.stockToConsume); flight.Crit = RollCrit(); control.Discs.Add(flight);
                    }
                }
                if (isAuthority) outer.SetNextStateToMain();
            }
        }
        public sealed class AimField : BaseState
        {
            private GameObject indicator;
            private LineRenderer edge;
            private Vector3 point;
            private bool valid;
            public override void OnEnter()
            {
                base.OnEnter();
                if (!isAuthority) return;
                indicator = new GameObject("Frost Field Target");
                edge = Line(indicator.transform, "Placement", .09f, 64, true);
                UpdateAim();
            }
            private void UpdateAim()
            {
                if (!isAuthority) return;
                if (Ground(GetAimRay(), F(fieldRange), out RaycastHit hit))
                { point = hit.point; valid = true; if (edge) Ring(edge, point, F(fieldRadius)); }
                if (edge) edge.enabled = valid;
            }
            public override void Update() { base.Update(); UpdateAim(); }
            public override void FixedUpdate()
            {
                base.FixedUpdate(); UpdateAim();
                if (!isAuthority || !inputBank || inputBank.skill3.down) return;
                if (valid) outer.SetNextState(new PlaceField { Point = point }); else outer.SetNextStateToMain();
            }
            public override void OnExit() { if (indicator) Destroy(indicator); base.OnExit(); }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
        public sealed class PlaceField : BaseState
        {
            internal Vector3 Point;
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Point); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Point = reader.ReadVector3(); }
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active) MakeField(characterBody, Point, true, null, RollCrit());
                if (isAuthority) outer.SetNextStateToMain();
            }
        }
        public sealed class ChargeSpear : BaseState
        {
            private Control control;
            private GameObject effect;
            private TemporaryOverlayInstance frostOverlay;
            private bool fullCue;
            private uint sound;
            public override void OnEnter()
            {
                base.OnEnter(); LoadAssets(); control = GetComponent<Control>();
                if (control) control.Charging = true;
                if (NetworkClient.active)
                {
                    Transform muzzle = FindModelChild("Muzzle") ?? characterBody.coreTransform;
                    if (spearCharge)
                    {
                        effect = Instantiate(spearCharge, muzzle.position, muzzle.rotation);
                        effect.transform.SetParent(muzzle, true);
                        var particles = effect.GetComponent<ScaleParticleSystemDuration>();
                        if (particles) particles.newDuration = T(chargeTime) + F(holdTime);
                        var scale = effect.GetComponent<ObjectScaleCurve>();
                        if (scale) scale.timeMax = T(chargeTime);
                    }
                    Transform model = GetModelTransform();
                    var characterModel = model ? model.GetComponent<CharacterModel>() : null;
                    if (characterModel && frozenMaterial)
                    {
                        frostOverlay = TemporaryOverlayManager.AddOverlay(gameObject);
                        frostOverlay.originalMaterial = frozenMaterial;
                        frostOverlay.duration = T(chargeTime) + F(holdTime) + .2f;
                        frostOverlay.AddToCharacterModel(characterModel);
                    }
                    if (!string.IsNullOrEmpty(chargeSound)) sound = Util.PlayAttackSpeedSound(chargeSound, gameObject, nativeChargeTime / T(chargeTime));
                }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float fraction = Mathf.Clamp01(fixedAge / T(chargeTime));
                if (fraction >= 1f && !fullCue && NetworkClient.active)
                {
                    fullCue = true;
                    PlayIceBurst(characterBody.corePosition, 2.5f, true, false);
                }
                float progress = fraction < 1f ? fraction : Mathf.Clamp01(1f - (fixedAge - F(chargeTime)) / T(holdTime));
                RemoteDroneSkills.ChargeDisplay(this, progress, fraction >= 1f, Color.Lerp(Ice, Color.white, fraction));
                if (isAuthority && inputBank && (!inputBank.skill4.down || fixedAge >= F(chargeTime) + F(holdTime)))
                    outer.SetNextState(new LaunchSpear { Aim = GetAimRay(), Fraction = fraction, Charged = Mathf.Min(fixedAge, F(chargeTime)) });
            }
            public override void OnExit()
            {
                if (control) control.Charging = false;
                if (effect) Destroy(effect);
                frostOverlay?.Destroy();
                if (sound != 0u) AkSoundEngine.StopPlayingID(sound);
                RemoteDroneSkills.HideCharge(this); base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
        public sealed class LaunchSpear : BaseState
        {
            internal Ray Aim;
            internal float Fraction, Charged;
            public override void OnSerialize(NetworkWriter writer)
            { base.OnSerialize(writer); writer.Write(Aim.origin); writer.Write(Aim.direction); writer.Write(Fraction); writer.Write(Charged); }
            public override void OnDeserialize(NetworkReader reader)
            { base.OnDeserialize(reader); Aim = new Ray(reader.ReadVector3(), reader.ReadVector3()); Fraction = reader.ReadSingle(); Charged = reader.ReadSingle(); }
            public override void OnEnter()
            {
                base.OnEnter();
                float overload = Mathf.Min(F(overloadMax), Charged * F(overloadRate));
                var control = GetComponent<Control>();
                if (control && (isAuthority || NetworkServer.active)) control.StartOverload(overload);
                if (NetworkServer.active)
                {
                    var ctx = new CastContext(characterBody, RollCrit()); control.Casts.Add(ctx);
                    float visualScale = Mathf.Lerp(.9f, 1.65f, Mathf.Clamp01(Fraction));
                    Visual visual = Spawn(Kind.Spear, characterBody, Aim.origin, visualScale, T(spearLifetime), Util.QuaternionSafeLookRotation(Aim.direction));
                    var flight = visual.gameObject.AddComponent<SpearFlight>();
                    flight.Owner = characterBody; flight.Context = ctx; flight.Direction = Aim.direction.normalized;
                    flight.Fraction = Mathf.Clamp01(Fraction);
                    visual.transform.rotation = Util.QuaternionSafeLookRotation(flight.Direction);
                }
                if (isAuthority)
                {
                    if (overload > 0f) outer.SetNextState(new Overloaded { Duration = overload });
                    else outer.SetNextStateToMain();
                }
            }
        }
        public sealed class Overloaded : BaseState
        {
            internal float Duration;
            private LoopSoundManager.SoundLoopPtr coolingLoop;
            private string rebootSound;
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Duration); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Duration = reader.ReadSingle(); }
            public override void OnEnter()
            {
                base.OnEnter();
                RemoteDroneSkills.ChargeDisplay(this, 1f, true);
                if (!NetworkClient.active) return;
                if (EntityStateCatalog.InstantiateState(typeof(EntityStates.Railgunner.Backpack.Offline)) is EntityStates.Railgunner.Backpack.Offline offline)
                {
                    if (!string.IsNullOrEmpty(offline.enterSoundString)) Util.PlaySound(offline.enterSoundString, gameObject);
                    if (offline.loopSoundDef) coolingLoop = LoopSoundManager.PlaySoundLoopLocal(gameObject, offline.loopSoundDef);
                }
                if (EntityStateCatalog.InstantiateState(typeof(EntityStates.Railgunner.Backpack.Reboot)) is EntityStates.Railgunner.Backpack.Reboot reboot)
                    rebootSound = reboot.enterSoundString;
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate(); var control = GetComponent<Control>();
                float remaining = control ? control.Remaining : Mathf.Max(0f, Duration - fixedAge);
                RemoteDroneSkills.ChargeDisplay(this, remaining / Mathf.Max(.02f, Duration), true);
                if (isAuthority && remaining <= 0f) outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (coolingLoop.isValid) LoopSoundManager.StopSoundLoopLocal(coolingLoop);
                if (NetworkClient.active && Alive(characterBody) && !string.IsNullOrEmpty(rebootSound))
                    Util.PlaySound(rebootSound, gameObject);
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
        private sealed class DiscFlight : MonoBehaviour
        {
            internal CharacterBody Owner;
            internal Control Control;
            internal Vector3 Destination;
            internal int Spent;
            internal bool Crit;
            private int phase;
            private float nextDamage, nextFrost;
            private bool refunded;
            private readonly HashSet<HealthComponent> outward = new HashSet<HealthComponent>(), inward = new HashSet<HealthComponent>();
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (!Alive(Owner) || !Control) { NetworkServer.Destroy(gameObject); return; }
                Vector3 from = transform.position;
                if (phase == 0)
                {
                    Vector3 to = Vector3.MoveTowards(from, Destination, T(discSpeed) * Time.fixedDeltaTime);
                    TravelHits(from, to, outward);
                    transform.position = to;
                    if ((to - Destination).sqrMagnitude <= .001f)
                    {
                        phase = Control.Holding ? 1 : 2;
                    }
                }
                else if (phase == 1)
                {
                    if (!Control.Holding) { phase = 2; return; }
                    bool frostTick = Now >= nextFrost;
                    bool damageTick = Now >= nextDamage;
                    if (!frostTick && !damageTick) return;
                    if (frostTick) nextFrost = Now + T(spinFrostInterval);
                    if (damageTick) nextDamage = Now + T(spinInterval);
                    foreach (HurtBox box in Around(Owner, from, F(spinRadius)))
                        if (damageTick) Hit(Owner, box, Owner.damage * F(spinDamage) / 100f, F(spinProc),
                            frostTick ? Count(spinFrost) : 0, 0f, null, Crit);
                        else Frost(box.healthComponent.body, Count(spinFrost));
                }
                else
                {
                    Vector3 to = Vector3.MoveTowards(from, Owner.corePosition, T(returnSpeed) * Time.fixedDeltaTime);
                    TravelHits(from, to, inward); transform.position = to;
                    if ((to - Owner.corePosition).sqrMagnitude <= 1f) NetworkServer.Destroy(gameObject);
                }
            }
            private void TravelHits(Vector3 from, Vector3 to, HashSet<HealthComponent> visited)
            {
                Vector3 step = to - from;
                var boxes = new List<HurtBox>(Around(Owner, from, F(discHitRadius)));
                if (step.sqrMagnitude > .00001f)
                    foreach (RaycastHit hit in Physics.SphereCastAll(from, Mathf.Max(.01f, F(discHitRadius)), step.normalized,
                        step.magnitude, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                    {
                        var box = hit.collider.GetComponent<HurtBox>(); if (box) boxes.Add(box);
                    }
                TeamMask enemies = TeamMask.GetEnemyTeams(Owner.teamComponent.teamIndex);
                foreach (HurtBox box in boxes)
                {
                    if (!box || !box.healthComponent || !Alive(box.healthComponent.body) ||
                        !enemies.HasTeam(box.healthComponent.body.teamComponent.teamIndex) || !visited.Add(box.healthComponent)) continue;
                    Hit(Owner, box, Owner.damage * F(discDamage) / 100f, F(discProc), Count(discFrost), 0f, null, Crit);
                    if (discImpact) EffectManager.SpawnEffect(discImpact, new EffectData { origin = box.transform.position }, true);
                }
            }
            private void OnDestroy()
            {
                if (NetworkServer.active && Control && !refunded)
                { refunded = true; Control.Return(this); }
            }
        }
        private sealed class SpearFlight : MonoBehaviour
        {
            internal CharacterBody Owner;
            internal CastContext Context;
            internal Vector3 Direction;
            internal float Fraction;
            private float age;
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                age += Time.fixedDeltaTime;
                if (!Alive(Owner) || age >= T(spearLifetime)) { NetworkServer.Destroy(gameObject); return; }
                Vector3 from = transform.position;
                float distance = T(spearSpeed) * Time.fixedDeltaTime;
                bool contact = Physics.SphereCast(from, Mathf.Max(.01f, F(spearHitRadius)), Direction, out RaycastHit wall,
                    distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                float nearest = contact ? wall.distance : distance;
                Vector3 point = contact ? wall.point : from + Direction * distance;
                TeamMask enemies = TeamMask.GetEnemyTeams(Owner.teamComponent.teamIndex);
                HurtBox overlap = Around(Owner, from, F(spearHitRadius)).FirstOrDefault(box => box && box.healthComponent && box.healthComponent.alive);
                if (overlap) { nearest = 0f; point = from; contact = true; }
                foreach (RaycastHit hit in Physics.SphereCastAll(from, Mathf.Max(.01f, F(spearHitRadius)), Direction,
                    nearest, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    var box = hit.collider.GetComponent<HurtBox>();
                    if (!box || !box.healthComponent || !Alive(box.healthComponent.body) ||
                        !enemies.HasTeam(box.healthComponent.body.teamComponent.teamIndex) || hit.distance > nearest) continue;
                    nearest = hit.distance; point = hit.point; contact = true;
                }
                if (!contact) { transform.position += Direction * distance; return; }
                float radius = Mathf.Lerp(F(spearMinRadius), F(spearMaxRadius), Fraction);
                Burst(point, radius);
                if (Fraction >= .999f) MakeField(Owner, Terrain(point), false, Context);
                foreach (HurtBox box in Around(Owner, point, radius))
                    Hit(Owner, box, Owner.damage * Mathf.Lerp(F(spearMinDamage), F(spearMaxDamage), Fraction) / 100f,
                        F(spearProc), 0, F(freezeTime), Context, Context.Crit, true);
                Context.Drain();
                NetworkServer.Destroy(gameObject);
            }
        }
        private static FrostArea MakeField(CharacterBody owner, Vector3 point, bool utilityField, CastContext context, bool crit = false)
        {
            float radius = utilityField ? F(fieldRadius) : F(iceRadius);
            float duration = utilityField ? F(fieldDuration) : F(iceDuration);
            Visual visual = Spawn(utilityField ? Kind.UtilityField : Kind.IcyField, owner, point, radius, duration);
            var field = visual.gameObject.AddComponent<FrostArea>();
            field.Owner = owner; field.Utility = utilityField; field.Context = context;
            field.Radius = radius; field.End = Now + duration; field.Crit = crit;
            fields.Add(field);
            if (utilityField) Burst(point, Mathf.Min(6f, radius));
            return field;
        }
        private sealed class FrostArea : MonoBehaviour
        {
            internal CharacterBody Owner;
            internal CastContext Context;
            internal bool Utility, Crit;
            internal float Radius, End;
            private float nextScan, nextFrost, nextDamage;
            private readonly HashSet<HealthComponent> extended = new HashSet<HealthComponent>();
            internal bool Extend(HealthComponent victim)
            {
                float amount = F(Utility ? fieldExtension : iceExtension);
                if (amount <= 0f || Now >= End || !ContainsEnemy(Owner, victim.body, transform.position, Radius) ||
                    !extended.Add(victim)) return false;
                End += amount;
                GetComponent<Visual>().ExtendTo(End);
                if (Context != null) Context.Expires = Mathf.Max(Context.Expires, End + F(frostDuration) + 1f);
                return true;
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (!Alive(Owner) || Now >= End) { NetworkServer.Destroy(gameObject); return; }
                float now = Now;
                bool tick = now >= nextFrost;
                bool damageTick = Utility && F(fieldDamage) > 0f && now >= nextDamage;
                if (now < nextScan && !tick && !damageTick) return;
                nextScan = now + .1f;
                if (tick) nextFrost = AdvanceTick(nextFrost, T(Utility ? fieldInterval : iceInterval), now);
                if (damageTick) nextDamage = AdvanceTick(nextDamage, T(fieldDamageInterval), now);
                foreach (HurtBox box in Around(Owner, transform.position, Radius))
                {
                    if (!box || !box.healthComponent || !Alive(box.healthComponent.body)) continue;
                    var body = box.healthComponent.body;
                    if (Utility) body.AddTimedBuff(slowBuff, .25f);
                    if (Context != null) Mark(box.healthComponent, Context, End);
                    int frost = tick ? Count(Utility ? fieldFrost : iceFrost) : 0;
                    if (damageTick) Hit(Owner, box, Owner.damage * F(fieldDamage) / 100f, F(fieldProc), frost, 0f,
                        null, Crit, source: DamageSource.Utility);
                    else Frost(body, frost);
                }
            }
            private static float AdvanceTick(float deadline, float interval, float now)
            {
                float next = deadline + interval;
                return deadline <= 0f || next <= now ? now + interval : next;
            }
            private void OnDestroy() { fields.Remove(this); }
        }
        private static void ExtendFields(DamageReport report, float healthRemoved)
        {
            if (!NetworkServer.active || fields.Count == 0 || report == null || !report.victim || !report.victimBody ||
                !report.victim.isInFrozenState || healthRemoved <= 0f ||
                healthRemoved > report.victim.fullCombinedHealth * FrozenThreshold(report.victim) ||
                (report.victim.killingDamageType & DamageType.VoidDeath) != 0) return;
            Vector3 point = report.victimBody.corePosition;
            bool extended = false;
            foreach (FrostArea field in fields.ToArray())
                if (field && field.Extend(report.victim)) extended = true;
            // A pending shatter already provides the execution's burst.
            var mark = report.victim.GetComponent<ShatterMark>();
            bool willShatter = mark && mark.WasFrozen && mark.Casts.Any(pair => Now <= pair.Value && Alive(pair.Key.Owner));
            if (extended && !willShatter) Burst(point, 5f);
        }
        private sealed class ShatterMark : MonoBehaviour
        {
            internal readonly Dictionary<CastContext, float> Casts = new Dictionary<CastContext, float>();
            internal bool WasFrozen;
            private void FixedUpdate()
            {
                foreach (var pair in Casts.ToArray()) if (Now > pair.Value || !Alive(pair.Key.Owner)) Casts.Remove(pair.Key);
                if (Casts.Count == 0) Destroy(this);
            }
        }
        private static void Mark(HealthComponent target, CastContext context, float until)
        {
            var mark = target.GetComponent<ShatterMark>() ?? target.gameObject.AddComponent<ShatterMark>();
            mark.Casts[context] = mark.Casts.TryGetValue(context, out float previous) ? Mathf.Max(previous, until) : until;
        }
        private static void CaptureFrozen(HealthComponent __instance)
        {
            if (!NetworkServer.active) return;
            var mark = __instance.GetComponent<ShatterMark>();
            if (mark) mark.WasFrozen = __instance.isInFrozenState;
        }
        private static void AfterDeath(DamageReport __0)
        {
            if (!NetworkServer.active || __0 == null || !__0.victim) return;
            var mark = __0.victim.GetComponent<ShatterMark>();
            if (!mark) return;
            Vector3 point = __0.victimBody ? __0.victimBody.corePosition : __0.victim.transform.position;
            foreach (var pair in mark.Casts.ToArray())
                if (Now <= pair.Value && mark.WasFrozen) pair.Key.Enqueue(__0.victim, point);
        }
        private sealed class CastContext
        {
            internal readonly CharacterBody Owner;
            internal readonly bool Crit;
            internal readonly Queue<Vector3> Pending = new Queue<Vector3>();
            internal readonly HashSet<HealthComponent> Shattered = new HashSet<HealthComponent>();
            internal float Expires;
            private bool processing;
            internal CastContext(CharacterBody owner, bool crit)
            { Owner = owner; Crit = crit; Expires = Now + T(spearLifetime) + F(iceDuration) + F(frostDuration) + 1f; }
            internal void Enqueue(HealthComponent victim, Vector3 point)
            { if (Shattered.Add(victim)) Pending.Enqueue(point); }
            internal void Drain()
            {
                if (processing || !Alive(Owner)) return;
                processing = true;
                try
                {
                    // Spread large chains across physics frames, without a
                    // gameplay cap or recursive damage callbacks.
                    for (int i = 0; i < 16 && Pending.Count > 0; i++)
                    {
                        Vector3 point = Pending.Dequeue(); Burst(point, F(shatterRadius));
                        foreach (HurtBox box in Around(Owner, point, F(shatterRadius)))
                            Hit(Owner, box, Owner.damage * F(shatterDamage) / 100f, F(shatterProc), Count(shatterFrost), 0f, this, Crit);
                    }
                }
                finally { processing = false; }
            }
        }
        private static void Hit(CharacterBody owner, HurtBox box, float damage, float proc, int frost, float freeze,
            CastContext context, bool crit, bool bonus = false, DamageSource? source = null)
        {
            if (!NetworkServer.active || !Alive(owner) || !box || !box.healthComponent || !box.healthComponent.alive) return;
            HealthComponent health = box.healthComponent;
            CharacterBody body = health.body;
            bool freezable = CanFreeze(body);
            if (bonus) damage *= 1f + (health.isInFrozenState ? F(frozenBonus) : body.GetBuffCount(DLC2Content.Buffs.Frost) * F(frostBonus)) / 100f;
            if (context != null) Mark(health, context, Now + Mathf.Max(F(frostDuration), freeze) + .1f);
            Frost(body, frost);
            if (!health || !health.alive) return;
            // Snapshot the bonus above, then freeze before damage so the native
            // frozen execution check also applies to the spear's first hit.
            if (freezable && freeze > 0f)
            { body.GetComponent<SetStateOnHurt>().SetFrozen(freeze); CommitFreeze(body); }
            var info = new DamageInfo { attacker = owner.gameObject, damage = damage, crit = crit,
                procCoefficient = proc, position = box.transform.position, inflictedHurtbox = box,
                damageType = new DamageTypeCombo { damageSource = source ?? (context == null ? DamageSource.Primary : DamageSource.Special) } };
            health.TakeDamage(info);
            if (GlobalEventManager.instance)
            { GlobalEventManager.instance.OnHitEnemy(info, health.gameObject); GlobalEventManager.instance.OnHitAll(info, health.gameObject); }

        }
        private static bool Ground(Ray ray, float distance, out RaycastHit hit) => Physics.Raycast(ray, out hit, distance, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
        private static Vector3 Terrain(Vector3 point) => Ground(new Ray(point + Vector3.up * 2f, Vector3.down), 100f, out RaycastHit hit) ? hit.point : point;
        private enum Kind : byte { Disc, Spear, UtilityField, IcyField, IceImpact }
        internal static void RegisterPrefabs(ContentPack content)
        {
            if (!primary) return;
            var template = new GameObject("3QS Frost Drone Visual Template"); template.SetActive(false);
            template.AddComponent<NetworkIdentity>();
            var movement = template.AddComponent<NetworkTransform>();
            movement.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform; movement.sendInterval = .05f;
            template.AddComponent<Visual>();
            visualPrefab = R2API.PrefabAPI.InstantiateClone(template, "3QSRemoteFrostVisual", true);
            visualPrefab.SetActive(true); UnityEngine.Object.Destroy(template);
            content.networkedObjectPrefabs.Add(new[] { visualPrefab });
        }
        private static Visual Spawn(Kind kind, CharacterBody owner, Vector3 point, float radius, float lifetime, Quaternion? rotation = null)
        {
            GameObject obj = UnityEngine.Object.Instantiate(visualPrefab, point, rotation ?? Quaternion.identity);
            var visual = obj.GetComponent<Visual>();
            visual.Kind = kind; visual.Owner = owner; visual.Radius = radius; visual.Lifetime = lifetime; visual.Born = Now;
            NetworkServer.Spawn(obj); return visual;
        }
        private static void Burst(Vector3 point, float radius)
        {
            // The native impact ignores EffectData.scale. Use our existing
            // networked visual to scale a private copy on each client.
            Visual visual = Spawn(Kind.IceImpact, null, point, radius, 2.8f);
            UnityEngine.Object.Destroy(visual.gameObject, 2.8f);
        }
        private static bool StartNativeSounds(GameObject prefab, GameObject emitter)
        {
            if (!prefab) return false;
            bool played = false;
            foreach (AkEvent sound in prefab.GetComponentsInChildren<AkEvent>(true))
                if (sound.data != null && (sound.triggerList.Contains(AkTriggerHandler.START_TRIGGER_ID) ||
                    sound.triggerList.Contains(AkTriggerHandler.ON_ENABLE_TRIGGER_ID)))
                {
                    string name = sound.data.WwiseObjectReference ? sound.data.WwiseObjectReference.ObjectName : null;
                    uint id = string.IsNullOrEmpty(name) ? sound.data.Post(emitter) : Util.PlaySound(name, emitter);
                    played |= id != 0u;
                }
            return played;
        }
        private static void PlayIceBurst(Vector3 point, float radius, bool audible, bool cameraShake)
        {
            if (!spearImpact) return;
            var holder = new GameObject("Frost Drone Ice Burst"); holder.SetActive(false);
            holder.transform.position = point;
            holder.transform.localScale = Vector3.one * Mathf.Clamp(radius / 4f, .65f, 6f);
            var clone = UnityEngine.Object.Instantiate(spearImpact, holder.transform, false);
            var effect = clone.GetComponent<EffectComponent>();
            string sound = effect && !string.IsNullOrEmpty(effect.soundName) ? effect.soundName : IceBurstSound;
            if (effect)
            {
                effect.effectData = new EffectData { origin = point, scale = 1f };
                effect.soundName = null;
            }
            foreach (AkEvent evt in clone.GetComponentsInChildren<AkEvent>(true)) evt.enabled = false;
            if (!cameraShake)
                foreach (ShakeEmitter shake in clone.GetComponentsInChildren<ShakeEmitter>(true)) shake.enabled = false;
            foreach (ParticleSystem particles in clone.GetComponentsInChildren<ParticleSystem>(true))
            { var main = particles.main; main.scalingMode = ParticleSystemScalingMode.Hierarchy; }
            if (audible) holder.AddComponent<AkGameObj>();
            holder.SetActive(true);
            if (audible) Util.PlaySound(sound, holder);
            UnityEngine.Object.Destroy(holder, 2.8f);
        }
        private static GameObject Art(GameObject prefab, Transform parent, bool continuous = false, bool keepVisualMotion = false)
        {
            if (!prefab) return null;
            var holder = new GameObject("Native Frost Art"); holder.SetActive(false); holder.transform.SetParent(parent, false);
            // The native Nano-Spear ghost has a nonzero root position in its prefab.
            GameObject obj = UnityEngine.Object.Instantiate(prefab, parent.position, parent.rotation, holder.transform);
            foreach (MonoBehaviour script in obj.GetComponentsInChildren<MonoBehaviour>(true))
                if (!keepVisualMotion || script is ProjectileGhostController || script is DestroyOnTimer || script is AkEvent)
                    script.enabled = false;
            if (keepVisualMotion && obj.TryGetComponent(out EffectComponent effect))
                effect.effectData = new EffectData { origin = parent.position, scale = 1f };
            foreach (Collider collider in obj.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (Rigidbody body in obj.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
            if (continuous)
                foreach (ParticleSystem particle in obj.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particle.main; main.loop = true;
                }
            holder.SetActive(true);
            foreach (ParticleSystem particle in obj.GetComponentsInChildren<ParticleSystem>(true)) particle.Play();
            return holder;
        }
        private static LineRenderer Line(Transform parent, string name, float width, int count, bool loop)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>(); line.transform.SetParent(parent, false);
            line.positionCount = count; line.useWorldSpace = true; line.loop = loop; line.widthMultiplier = width;
            line.sharedMaterial = Graphic.defaultGraphicMaterial; line.startColor = line.endColor = Ice;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        private static void Ring(LineRenderer line, Vector3 point, float radius, bool terrain = true)
        {
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / line.positionCount;
                Vector3 p = point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (terrain && Ground(new Ray(p + Vector3.up * 12f, Vector3.down), 30f, out RaycastHit hit)) p = hit.point;
                line.SetPosition(i, p + Vector3.up * .08f);
            }
        }
        private static void FieldOutline(Transform parent, float radius, List<LineRenderer> edges)
        {
            const int count = 64;
            var points = new Vector3[count]; var supported = new bool[count];
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Vector3 p = parent.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                supported[i] = Ground(new Ray(p + Vector3.up * 2.5f, Vector3.down), 5f, out RaycastHit hit);
                points[i] = (supported[i] ? hit.point : p) + Vector3.up * .06f;
            }
            bool Connected(int a, int b) => supported[a] && supported[b] && Mathf.Abs(points[a].y - points[b].y) < 1.25f;
            int split = Array.FindIndex(supported, value => !value);
            if (split < 0)
                for (int i = 0; i < count; i++) if (!Connected(i, (i + 1) % count)) { split = i; break; }
            if (split < 0)
            {
                var rim = Line(parent, "Frost Field Boundary", .065f, count, true); rim.SetPositions(points); edges.Add(rim); return;
            }
            var segment = new List<Vector3>();
            for (int step = 1; step <= count; step++)
            {
                int i = (split + step) % count;
                if (supported[i]) segment.Add(points[i]);
                if (step < count && Connected(i, (i + 1) % count)) continue;
                if (segment.Count >= 2)
                {
                    var edge = Line(parent, "Frost Field Boundary", .065f, segment.Count, false);
                    edge.SetPositions(segment.ToArray()); edges.Add(edge);
                }
                segment.Clear();
            }
        }
        private static Material GetFieldMistMaterial()
        {
            if (fieldMistMaterial) return fieldMistMaterial;
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                { name = "Frost Drone Mist", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (float)x / (size - 1), v = (float)y / (size - 1);
                    float edge = Mathf.Clamp01(1f - new Vector2(u * 2f - 1f, v * 2f - 1f).sqrMagnitude);
                    float billow = Mathf.PerlinNoise(u * 2.5f + 3f, v * 4.5f + 7f);
                    float wisps = Mathf.PerlinNoise(u * 5f + 10f, v * 13f + 2f);
                    float alpha = Mathf.SmoothStep(0f, 1f, edge) * Mathf.Clamp01(billow * .85f + wisps * .5f - .18f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            // Use the same world-space rendering material as the working range
            // lines. These meshes supply their own vertex colours and texture UVs.
            fieldMistMaterial = new Material(Graphic.defaultGraphicMaterial) { mainTexture = texture };
            return fieldMistMaterial;
        }

        private sealed class FieldArt : IDisposable
        {
            private const int Cells = 20;
            private const int Stride = Cells + 1;
            private readonly float radius, cellSize;
            private readonly bool blizzard;
            private readonly float[] groundHeight = new float[Stride * Stride];
            private readonly bool[] continuousGround = new bool[Cells * Cells * 2];
            private readonly Mesh floorMesh, fogMesh, iceMesh;
            private readonly MeshRenderer floorRenderer;
            private readonly MaterialPropertyBlock floorProperties = new MaterialPropertyBlock();
            private readonly Vector3[] fogVertices, iceVertices;
            private readonly Color32[] fogColours, iceColours;
            private readonly Mote[] fog = Array.Empty<Mote>();
            private readonly Mote[] ice;

            private struct Mote
            {
                internal float Distance, Angle, Phase, Drift, Size;
            }

            internal FieldArt(Transform parent, float radius, bool blizzard)
            {
                this.radius = radius;
                this.blizzard = blizzard;
                cellSize = radius * 2f / Cells;
                Material mist = GetFieldMistMaterial();
                floorRenderer = CreateLayer(parent, "Ground Frost", mist, -2, out floorMesh);
                CreateLayer(parent, blizzard ? "Windblown Ice" : "Rising Ice Crystals", Graphic.defaultGraphicMaterial, 0, out iceMesh);
                BuildFloor(parent.position);
                var bounds = new Bounds(Vector3.zero, new Vector3(radius * 2f + 2f, 24f, radius * 2f + 2f));
                if (blizzard)
                {
                    CreateLayer(parent, "Drifting Frost Mist", mist, -1, out fogMesh);
                    fog = MakeMotes(Mathf.Clamp(Mathf.RoundToInt(radius * 2.5f), 10, 64), 41);
                    fogVertices = new Vector3[fog.Length * 8];
                    fogColours = new Color32[fogVertices.Length];
                    var fogUV = new Vector2[fogVertices.Length];
                    var fogIndices = new int[fog.Length * 12];
                    for (int card = 0; card < fog.Length * 2; card++)
                    {
                        int v = card * 4, t = card * 6;
                        fogUV[v] = Vector2.zero;
                        fogUV[v + 1] = Vector2.right;
                        fogUV[v + 2] = Vector2.one;
                        fogUV[v + 3] = Vector2.up;
                        fogIndices[t] = v; fogIndices[t + 1] = v + 1; fogIndices[t + 2] = v + 2;
                        fogIndices[t + 3] = v; fogIndices[t + 4] = v + 2; fogIndices[t + 5] = v + 3;
                    }
                    fogMesh.vertices = fogVertices;
                    fogMesh.uv = fogUV;
                    fogMesh.SetIndices(fogIndices, MeshTopology.Triangles, 0, false);
                    fogMesh.bounds = bounds;
                }

                ice = MakeMotes(Mathf.Clamp(Mathf.RoundToInt(radius * radius * .52f), 30, 245), 127);
                iceVertices = new Vector3[ice.Length * 6];
                iceColours = new Color32[iceVertices.Length];
                var iceUV = new Vector2[iceVertices.Length];
                var iceIndices = new int[ice.Length * 24];
                for (int i = 0; i < ice.Length; i++)
                    for (int side = 0; side < 4; side++)
                    {
                        int v = i * 6, t = i * 24 + side * 6;
                        int a = v + side, b = v + (side + 1) % 4;
                        iceIndices[t] = a; iceIndices[t + 1] = b; iceIndices[t + 2] = v + 4;
                        iceIndices[t + 3] = b; iceIndices[t + 4] = a; iceIndices[t + 5] = v + 5;
                    }
                iceMesh.vertices = iceVertices;
                iceMesh.uv = iceUV;
                iceMesh.SetIndices(iceIndices, MeshTopology.Triangles, 0, false);
                iceMesh.bounds = bounds;
            }

            private static MeshRenderer CreateLayer(Transform parent, string name, Material material, int sort, out Mesh mesh)
            {
                var layer = new GameObject(name);
                layer.transform.SetParent(parent, false);
                mesh = new Mesh { name = name };
                mesh.MarkDynamic();
                layer.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = layer.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.sortingOrder = sort;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                return renderer;
            }

            private void BuildFloor(Vector3 centre)
            {
                var vertices = new Vector3[groundHeight.Length];
                var colours = new Color32[vertices.Length];
                var uv = new Vector2[vertices.Length];
                var triangles = new List<int>(Cells * Cells * 6);
                for (int y = 0; y <= Cells; y++)
                    for (int x = 0; x <= Cells; x++)
                    {
                        int i = y * Stride + x;
                        var offset = new Vector2(x * cellSize - radius, y * cellSize - radius);
                        var ray = new Ray(centre + new Vector3(offset.x, 7f, offset.y), Vector3.down);
                        RaycastHit hit = default;
                        bool supported = offset.sqrMagnitude <= (radius + cellSize) * (radius + cellSize) &&
                            Ground(ray, 14f, out hit);
                        groundHeight[i] = supported ? hit.point.y - centre.y : float.NaN;
                        vertices[i] = new Vector3(offset.x, supported ? groundHeight[i] + .19f : 0f, offset.y);
                        Color colour = blizzard ? new Color(.43f, .78f, 1f) : new Color(.54f, .69f, 1f);
                        colour.a = supported ? (blizzard ? .77f : .62f) * EdgeFade(offset.magnitude) : 0f;
                        colours[i] = colour;
                        uv[i] = new Vector2((float)x / Cells, (float)y / Cells);
                    }
                for (int y = 0; y < Cells; y++)
                    for (int x = 0; x < Cells; x++)
                    {
                        int i = y * Stride + x;
                        int cell = (y * Cells + x) * 2;
                        continuousGround[cell] = AddFloorTriangle(triangles, i, i + Stride, i + 1);
                        continuousGround[cell + 1] = AddFloorTriangle(triangles, i + 1, i + Stride, i + Stride + 1);
                    }
                floorMesh.vertices = vertices;
                floorMesh.colors32 = colours;
                floorMesh.uv = uv;
                floorMesh.SetTriangles(triangles, 0);
            }

            private bool AddFloorTriangle(List<int> triangles, int a, int b, int c)
            {
                float first = groundHeight[a], second = groundHeight[b], third = groundHeight[c];
                if (float.IsNaN(first) || float.IsNaN(second) || float.IsNaN(third)) return false;
                float low = Mathf.Min(first, Mathf.Min(second, third));
                float high = Mathf.Max(first, Mathf.Max(second, third));
                if (high - low > Mathf.Max(1.2f, cellSize * 1.5f)) return false;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                return true;
            }

            private float GroundAt(Vector2 position)
            {
                float x = Mathf.Clamp((position.x + radius) / cellSize, 0f, Cells - .001f);
                float y = Mathf.Clamp((position.y + radius) / cellSize, 0f, Cells - .001f);
                int column = (int)x, row = (int)y;
                int i = row * Stride + column;
                int cell = (row * Cells + column) * 2;
                float u = x - column, v = y - row;
                if (u + v <= 1f)
                    return continuousGround[cell] ? groundHeight[i] * (1f - u - v) +
                        groundHeight[i + 1] * u + groundHeight[i + Stride] * v : float.NaN;
                return continuousGround[cell + 1] ? groundHeight[i + Stride + 1] * (u + v - 1f) +
                    groundHeight[i + 1] * (1f - v) + groundHeight[i + Stride] * (1f - u) : float.NaN;
            }

            private Mote[] MakeMotes(int count, int seed)
            {
                var random = new System.Random(seed);
                var motes = new Mote[count];
                for (int i = 0; i < count; i++)
                    motes[i] = new Mote
                    {
                        Distance = radius * .96f * Mathf.Sqrt((i + .5f) / count),
                        Angle = i * 2.399963f,
                        Phase = (float)random.NextDouble(),
                        Drift = Mathf.Lerp(.72f, 1.26f, (float)random.NextDouble()),
                        Size = Mathf.Lerp(.68f, 1.35f, (float)random.NextDouble())
                    };
                return motes;
            }

            private float EdgeFade(float distance) => Mathf.Clamp01((radius - distance) / (radius * .14f));

            internal void Draw(float age, float fade)
            {
                floorProperties.SetVector("_MainTex_ST", blizzard
                    ? new Vector4(1.5f, 2.2f, -age * .19f, -age * .08f)
                    : new Vector4(2.35f, 2.35f, -age * .042f, age * .025f));
                floorProperties.SetColor("_Color", new Color(1f, 1f, 1f, fade));
                floorRenderer.SetPropertyBlock(floorProperties);

                for (int i = 0; i < fog.Length; i++)
                {
                    Mote mote = fog[i];
                    float angle = mote.Angle + age * .35f * mote.Drift;
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * mote.Distance;
                    float ground = GroundAt(offset);
                    float wave = .72f + .28f * Mathf.Sin(age * mote.Drift + mote.Angle);
                    float width = Mathf.Min(radius * .24f * mote.Size, (radius - mote.Distance) * 1.3f);
                    float height = Mathf.Min(radius * .15f, 1.45f * mote.Size);
                    float opacity = float.IsNaN(ground) ? 0f : fade * wave * EdgeFade(mote.Distance) * .48f;
                    var colour = new Color(.67f, .89f, 1f, opacity);
                    Vector3 centre = new Vector3(offset.x, (float.IsNaN(ground) ? 0f : ground) + height * .72f, offset.y);
                    Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    SetFogCard(i * 8, centre, tangent * width, height, colour);
                    SetFogCard(i * 8 + 4, centre, Vector3.Cross(Vector3.up, tangent) * width * .65f, height, colour);
                }

                for (int i = 0; i < ice.Length; i++)
                {
                    Mote mote = ice[i];
                    float phase = Mathf.Repeat(mote.Phase + age * (blizzard ? .65f : .31f) * mote.Drift, 1f);
                    float angle = mote.Angle + age * (blizzard ? .72f : -.23f) * mote.Drift;
                    float distance = mote.Distance * (blizzard ? 1f : .83f + phase * .17f);
                    Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
                    float ground = GroundAt(offset);
                    float height = blizzard ? .5f + (.7f + mote.Size) * phase : .45f + 2.1f * phase;
                    Vector3 centre = new Vector3(offset.x, (float.IsNaN(ground) ? 0f : ground) + height, offset.y);
                    float opacity = float.IsNaN(ground) ? 0f : Mathf.Sin(Mathf.PI * phase) * fade * EdgeFade(distance);
                    Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    Vector3 side = Vector3.Cross(Vector3.up, tangent);
                    Vector3 axis = blizzard ? (tangent + Vector3.up * .22f) * (.25f * mote.Size) : Vector3.up * (.28f * mote.Size);
                    SetIceCrystal(i * 6, centre, tangent, side, axis, mote.Size, opacity);
                }

                if (fogMesh)
                {
                    fogMesh.SetVertices(fogVertices, 0, fogVertices.Length, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                    fogMesh.colors32 = fogColours;
                }
                iceMesh.SetVertices(iceVertices, 0, iceVertices.Length, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
                iceMesh.colors32 = iceColours;
            }

            private void SetFogCard(int i, Vector3 centre, Vector3 width, float height, Color32 colour)
            {
                Vector3 up = Vector3.up * height * .5f;
                fogVertices[i] = centre - width - up;
                fogVertices[i + 1] = centre + width - up;
                fogVertices[i + 2] = centre + width + up;
                fogVertices[i + 3] = centre - width + up;
                for (int v = 0; v < 4; v++) fogColours[i + v] = colour;
            }

            private void SetIceCrystal(int i, Vector3 centre, Vector3 tangent, Vector3 side, Vector3 axis, float size, float opacity)
            {
                Vector3 cross = (blizzard ? Vector3.up : tangent) * (.064f * size);
                side *= .084f * size;
                iceVertices[i] = centre + side;
                iceVertices[i + 1] = centre + cross;
                iceVertices[i + 2] = centre - side;
                iceVertices[i + 3] = centre - cross;
                iceVertices[i + 4] = centre + axis;
                iceVertices[i + 5] = centre - axis;
                iceColours[i] = new Color(.48f, .8f, 1f, opacity);
                iceColours[i + 1] = new Color(.91f, .97f, 1f, opacity);
                iceColours[i + 2] = new Color(.64f, .88f, 1f, opacity);
                iceColours[i + 3] = new Color(.33f, .64f, .96f, opacity);
                iceColours[i + 4] = new Color(.96f, 1f, 1f, opacity * .85f);
                iceColours[i + 5] = new Color(.52f, .82f, 1f, opacity * .42f);
            }

            public void Dispose()
            {
                UnityEngine.Object.Destroy(floorMesh);
                UnityEngine.Object.Destroy(fogMesh);
                UnityEngine.Object.Destroy(iceMesh);
            }
        }
        private sealed class Visual : NetworkBehaviour
        {
            internal Kind Kind;
            internal CharacterBody Owner;
            internal float Radius, Lifetime, Born;
            private float nextPulse;
            private int pulseIndex;
            private GameObject art;
            private FieldArt fieldArt;
            private readonly List<LineRenderer> edges = new List<LineRenderer>();
            internal void ExtendTo(float end)
            { Lifetime = Mathf.Max(0f, end - Born); SetDirtyBit(1u); }
            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState)
                {
                    writer.WritePackedUInt32(syncVarDirtyBits);
                    if (syncVarDirtyBits == 0u) return false;
                    writer.Write(Lifetime); return true;
                }
                writer.Write((byte)Kind); writer.Write(Owner ? Owner.gameObject : null);
                writer.Write(Radius); writer.Write(Lifetime); writer.Write(Born); return true;
            }
            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (!initialState) { if (reader.ReadPackedUInt32() != 0u) Lifetime = reader.ReadSingle(); return; }
                Kind = (Kind)reader.ReadByte(); var owner = reader.ReadGameObject(); Owner = owner ? owner.GetComponent<CharacterBody>() : null;
                Radius = reader.ReadSingle(); Lifetime = reader.ReadSingle(); Born = reader.ReadSingle();
            }
            public override void OnStartClient()
            {
                base.OnStartClient(); LoadAssets();
                if (Kind == Kind.Disc)
                {
                    if (!GetComponent<AkGameObj>()) gameObject.AddComponent<AkGameObj>();
                    art = Art(discArt, transform, true);
                    if (art) art.transform.localScale = Vector3.one * .7f;
                    if (!string.IsNullOrEmpty(discSound)) Util.PlaySound(discSound, gameObject);
                    StartNativeSounds(discArt, gameObject);
                }
                else if (Kind == Kind.Spear)
                {
                    art = Art(spearArt, transform, keepVisualMotion: true);
                    if (art) art.transform.localScale = Vector3.one * Radius;
                    // Let the shot sound finish on the drone even if the spear
                    // hits immediately and its projectile is destroyed.
                    GameObject emitter = Owner ? Owner.gameObject : gameObject;
                    if (!emitter.GetComponent<AkGameObj>()) emitter.AddComponent<AkGameObj>();
                    if (!StartNativeSounds(nativeSpear, emitter)) Util.PlaySound(IceBurstSound, emitter);
                    if (spearMuzzle) EffectManager.SpawnEffect(spearMuzzle, new EffectData { origin = transform.position, rotation = transform.rotation }, false);
                }
                else if (Kind == Kind.IceImpact)
                {
                    if (Now - Born < .6f) PlayIceBurst(transform.position, Radius, true, false);
                }
                else
                {
                    clientFields.Add(this);
                    FieldOutline(transform, Radius, edges);
                    if (Radius > 0f)
                    {
                        fieldArt = new FieldArt(transform, Radius, Kind == Kind.UtilityField);
                        fieldArt.Draw(Mathf.Max(0f, Now - Born), 0f);
                    }
                    nextPulse = Now + 1.5f;
                }
            }
            private void FieldPulse()
            {
                float angle = Born + ++pulseIndex * 2.399963f;
                float distance = Radius * (.22f + .52f * Mathf.Repeat(pulseIndex * .618034f, 1f));
                Vector3 point = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
                if (Ground(new Ray(point + Vector3.up * 2.5f, Vector3.down), 5f, out RaycastHit hit)) point = hit.point;
                else point = transform.position;
                PlayIceBurst(point, 5f, true, false);
            }
            private void LateUpdate()
            {
                if (Kind == Kind.Disc)
                {
                    if (art) art.transform.Rotate(Vector3.up, Time.deltaTime * 600f, Space.Self);
                }
                else if (Kind == Kind.UtilityField || Kind == Kind.IcyField)
                {
                    float age = Mathf.Max(0f, Now - Born);
                    float fade = Mathf.Clamp01(age / .25f) * Mathf.Clamp01((Lifetime - age) / .5f);
                    fieldArt?.Draw(age, fade);
                    Color color = Kind == Kind.UtilityField ? new Color(.24f, .76f, 1f, .9f) : new Color(.72f, .93f, 1f, .9f);
                    color.a *= fade * (.31f + .09f * Mathf.Sin(age * 2f));
                    foreach (LineRenderer edge in edges) edge.startColor = edge.endColor = color;
                    if (Kind == Kind.IcyField && NetworkClient.active && Now < Born + Lifetime && Now >= nextPulse)
                    {
                        nextPulse = Now + 2f;
                        FieldPulse();
                    }
                }
            }
            private void OnDestroy()
            {
                if (Kind == Kind.UtilityField || Kind == Kind.IcyField) clientFields.Remove(this);
                fieldArt?.Dispose();
                if (NetworkClient.active && GetComponent<AkGameObj>()) AkSoundEngine.StopAll(gameObject);
            }
        }
        private static Sprite IceIcon(Sprite source)
        {
            if (!source) return null;
            RenderTexture saved = RenderTexture.active;
            RenderTexture render = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source.texture, render); RenderTexture.active = render;
                Rect rect = source.textureRect;
                var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0); Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color colour = Color.Lerp(new Color(.015f, .04f, .12f), Ice, Mathf.Clamp01(pixels[i].grayscale * 1.4f));
                    colour = Color.Lerp(colour, Color.white, Mathf.Clamp01((pixels[i].grayscale - .7f) / .3f));
                    colour.a = pixels[i].a; pixels[i] = colour;
                }
                texture.SetPixels(pixels); texture.Apply(); texture.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height), source.pixelsPerUnit);
            }
            finally { RenderTexture.active = saved; RenderTexture.ReleaseTemporary(render); }
        }
    }
}
