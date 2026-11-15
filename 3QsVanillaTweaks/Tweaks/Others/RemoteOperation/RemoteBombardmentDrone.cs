using System;
using System.Collections.Generic;
using System.Linq;
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
using UnityEngine.Networking;
using UnityEngine.UI;
using B = EntityStates.Drone.DroneBombardment;
using C = Config.ConfigManager;
using Command = EntityStates.Drone.Command.CommandBombardment;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteBombardmentDrone
    {
        private const string Root = RemoteDroneSkills.BombardmentSection;
        private const string Weapon = "3QSBombardmentWeapon", UtilityMachine = "3QSBombardmentUtility";
        private const string ArcName = "Arc Lightning", BarrageName = "Bombardment", EMPName = "EMP", ThunderName = "Orbital Thunderbolt";
        private const byte BarrageCombo = 178;
        internal static readonly Type[] States = { typeof(Arc), typeof(FireArc), typeof(AimBarrage), typeof(DeployBarrage), typeof(EMP), typeof(ChargeThunder), typeof(FireThunder), typeof(Overloaded) };
        private static ConfigEntry<bool> enabled, arcOn, barrageOn, empOn, thunderOn, arcRapid;
        private static ConfigEntry<float> arcDamage, arcInterval, arcRange, arcHitRadius, arcChainRange, arcTerrainRange, arcProc, arcStun, arcStunChance,
            empDamage, empRadius, empProc, empShock, empPush, empSpeed, empAttackSpeed, empTime, empCooldown,
            chargeTime, holdTime, thunderDamageMin, thunderDamageMax, thunderRadiusMin, thunderRadiusMax,
            thunderRange, thunderProc, thunderShock, thunderCooldown, orbDamageMin, orbDamageMax,
            orbRadiusMin, orbRadiusMax, orbProc, orbSpeed, orbUpSpeed, orbGravity, orbLifetime,
            chainDamage, chainRange, chainProc, chainWeak, overloadPerSecond, overloadMaximum;
        private static ConfigEntry<int> arcChains, arcTerrainTargets, orbCountMin, orbCountMax;
        private static ConfigEntry<float> barrageCooldown, barrageDamage, barrageRadius, barrageIndicatorRadius, barrageDuration,
            barrageProc, barrageAimTime, barrageRange, barrageJuggle;
        private static ConfigEntry<int> barragePulses, barrageTierPulses, barrageStocks, barrageRequired, barrageConsumed, barrageRecharge;
        private static ConfigEntry<bool> barrageAttackSpeed;
        private static SkillDef primary, secondary, utility, special;
        private static BuffDef speedBuff;
        private static GameObject visualPrefab, barrageProjectile, targetPrefab, orbArt, chargeArt, lightningImpact;
        private static string arcFireSound;
        private static bool assetsLoaded, assetWarningLogged;
        private static Sprite offlineIcon;
        private static readonly Color Red = new Color(1f, .12f, .16f, .95f);

        private static ConfigEntry<float> N(string group, string name, float value, string help) =>
            RemoteDroneSkills.Number(Root + " - " + group, name, value, help);
        private static ConfigEntry<int> I(string group, string name, int value, string help) =>
            RemoteDroneSkills.Number(Root + " - " + group, name, value, help);
        private static float F(ConfigEntry<float> value) => value == null ? 0f : Mathf.Max(0f, C.Get(value));
        private static float TimeValue(ConfigEntry<float> value) => Mathf.Max(.02f, F(value));
        private static ConfigEntry<bool> Toggle(string group) => C.BindStartupBool(Root + " - " + group,
            "Changes Enabled", true, "Enable " + group + " on the player-controlled Bombardment Drone. Restart to apply.");

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable custom player-controlled Bombardment Drone skills. Restart to apply.");
            arcOn = Toggle(ArcName); barrageOn = Toggle(BarrageName); empOn = Toggle(EMPName); thunderOn = Toggle(ThunderName);
            arcDamage = N(ArcName, "Damage (%)", 250f, "Damage of each enemy hit by the bolt.");
            arcInterval = N(ArcName, "Firing Interval (s)", .8f, "Interval at base attack speed. Attack speed reduces this interval.");
            arcRapid = C.BindBool(Root + " - " + ArcName, "Attack Speed Scales Recharge", true, "Rapid. Higher attack speed shortens the interval between shots.");
            arcRange = N(ArcName, "Range (m)", 50f, "Maximum initial bolt range.");
            arcHitRadius = N(ArcName, "Hit Radius (m)", .5f, "Forgiveness around the aimed bolt.");
            arcChains = I(ArcName, "Additional Targets", 3, "Additional nearby enemies hit once when the initial bolt directly hits an enemy. These hits do not chain further.");
            arcChainRange = N(ArcName, "Additional Target Range (m)", 15f, "Range around the directly hit enemy for additional targets.");
            arcTerrainRange = N(ArcName, "Terrain Bounce Range (m)", 15f, "Range around a terrain impact for redirecting the bolt to visible enemies. Redirected hits do not chain further.");
            arcTerrainTargets = I(ArcName, "Terrain Bounce Targets", 3, "Maximum distinct enemies struck once from a terrain impact. Selects the closest visible enemies within the terrain bounce range.");
            arcProc = N(ArcName, "Proc Coefficient", .5f, "Proc coefficient of each hit.");
            arcStun = N(ArcName, "Stun Duration (s)", .5f, "Stun duration when an Arc Lightning hit successfully rolls its stun chance.");
            arcStunChance = N(ArcName, "Stun Chance (%)", 50f, "Chance to stun, rolled separately for each enemy hit.");
            empDamage = N(EMPName, "Damage (%)", 200f, "Damage to enemies in the pulse.");
            empRadius = N(EMPName, "Radius (m)", 12f, "EMP pulse radius.");
            empProc = N(EMPName, "Proc Coefficient", 1f, "Proc coefficient of the pulse.");
            empShock = N(EMPName, "Shock Duration (s)", 1f, "Shock applied by the pulse.");
            empPush = N(EMPName, "Knockback Speed (m/s)", 20f, "Outward velocity added to non-boss enemies, independent of their mass.");
            empSpeed = N(EMPName, "Movement Speed Bonus (%)", 100f, "Movement speed granted after using EMP.");
            empAttackSpeed = N(EMPName, "Attack Speed Bonus (%)", 50f, "Attack speed granted after using EMP, lasting for the movement buff duration.");
            empTime = N(EMPName, "Movement Buff Duration (s)", 4f, "Duration of both the movement speed and attack speed bonuses.");
            empCooldown = N(EMPName, "Cooldown (s)", 10f, "Cooldown starts on use.");
            chargeTime = N(ThunderName, "Charge Time (s)", 5f, "Real time needed to reach full strength. Attack speed does not shorten charging.");
            holdTime = N(ThunderName, "Full Charge Hold (s)", 5f, "Additional time a full charge can be held before automatically firing.");
            thunderDamageMin = N(ThunderName, "Minimum Damage (%)", 1000f, "Main strike damage at minimum charge.");
            thunderDamageMax = N(ThunderName, "Maximum Damage (%)", 6000f, "Main strike damage at maximum charge.");
            thunderRadiusMin = N(ThunderName, "Minimum Radius (m)", 8f, "Main strike radius at minimum charge.");
            thunderRadiusMax = N(ThunderName, "Maximum Radius (m)", 30f, "Main strike radius at maximum charge.");
            thunderRange = N(ThunderName, "Target Range (m)", 100f, "Maximum distance to select a terrain target.");
            thunderProc = N(ThunderName, "Main Proc Coefficient", 1f, "Proc coefficient of the main strike.");
            thunderShock = N(ThunderName, "Shock Duration (s)", 2f, "Shock from the main strike, orb strikes, and full-charge chains.");
            thunderCooldown = N(ThunderName, "Cooldown (s)", 30f, "Cooldown begins after the post-shot overload.");
            orbCountMin = I(ThunderName, "Minimum Orbs", 3, "Orbs released at minimum charge.");
            orbCountMax = I(ThunderName, "Maximum Orbs", 12, "Orbs released at maximum charge.");
            orbDamageMin = N(ThunderName, "Minimum Orb Damage (%)", 300f, "Damage of an orb's landing strike at minimum charge.");
            orbDamageMax = N(ThunderName, "Maximum Orb Damage (%)", 600f, "Damage of an orb's landing strike at maximum charge.");
            orbRadiusMin = N(ThunderName, "Minimum Orb Strike Radius (m)", 4f, "Orb strike radius at minimum charge.");
            orbRadiusMax = N(ThunderName, "Maximum Orb Strike Radius (m)", 6f, "Orb strike radius at full charge.");
            orbProc = N(ThunderName, "Orb Proc Coefficient", .5f, "Proc coefficient of each landing strike.");
            orbSpeed = N(ThunderName, "Orb Outward Speed (m/s)", 14f, "Initial horizontal speed of the radial orbs.");
            orbUpSpeed = N(ThunderName, "Orb Upward Speed (m/s)", 26f, "Initial upward speed. Orbs travel in a high arc and strike on their first terrain impact without bouncing.");
            orbGravity = N(ThunderName, "Orb Gravity (m/s2)", 20f, "Downward acceleration of the orbs.");
            orbLifetime = N(ThunderName, "Orb Lifetime (s)", 8f, "Maximum travel time. Orbs that never land expire without a strike.");
            chainDamage = N(ThunderName, "Full Charge Chain Damage (%)", 10f, "Chain damage as a percentage of its originating main or orb strike.");
            chainRange = N(ThunderName, "Full Charge Chain Range (m)", 12f, "Each directly hit enemy chains to every other enemy in this range.");
            chainProc = N(ThunderName, "Full Charge Chain Proc Coefficient", .2f, "Proc coefficient of non-recursive chains.");
            chainWeak = N(ThunderName, "Full Charge Chain Weak Duration (s)", 5f, "Duration of vanilla Weaken applied by chains.");
            overloadPerSecond = N(ThunderName, "Overload Per Charge Second (s)", 1f, "Weapon lockout per second spent charging. Full-charge hold time is excluded.");
            overloadMaximum = N(ThunderName, "Maximum Overload (s)", 5f, "Maximum post-shot weapon lockout. Utility remains usable.");
        }

        internal static BuffDef[] Buffs => speedBuff ? new[] { speedBuff } : Array.Empty<BuffDef>();

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("BombardmentArc", ArcName, "", typeof(Arc), Weapon, F(arcInterval), null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>());
            secondary = RemoteDroneSkills.MakeSkill("BombardmentBarrage", BarrageName, "", typeof(AimBarrage), Weapon, 0f, null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>(), useStockConfig: false);
            utility = RemoteDroneSkills.MakeSkill("BombardmentEMP", EMPName, "", typeof(EMP), UtilityMachine, F(empCooldown), null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>());
            special = RemoteDroneSkills.MakeSkill("BombardmentThunderbolt", ThunderName, "", typeof(ChargeThunder), Weapon, F(thunderCooldown), null,
                beginsOnEnd: true, customDefinition: ScriptableObject.CreateInstance<ThunderSkill>());
            secondary.mustKeyPress = utility.mustKeyPress = special.mustKeyPress = true;
            R2API.LanguageAPI.Add("KEYWORD_RAPID",
                "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
            speedBuff = ScriptableObject.CreateInstance<BuffDef>();
            speedBuff.name = "3QSBombardmentEMP";
            speedBuff.buffColor = Red;
            speedBuff.canStack = false;
            Refresh();
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = TimeValue(arcInterval);
            primary.attackSpeedBuffsRestockSpeed = C.Get(arcRapid);
            primary.keywordTokens = C.Get(arcRapid) ? new[] { "KEYWORD_RAPID" } : Array.Empty<string>();
            utility.baseRechargeInterval = F(empCooldown);
            special.baseRechargeInterval = F(thunderCooldown);
            if (barrageCooldown != null)
            {
                secondary.baseRechargeInterval = F(barrageCooldown);
                secondary.baseMaxStock = Mathf.Max(1, C.Get(barrageStocks));
                secondary.requiredStock = Mathf.Max(1, C.Get(barrageRequired));
                secondary.stockToConsume = Mathf.Max(0, C.Get(barrageConsumed));
                secondary.rechargeStock = Mathf.Max(1, C.Get(barrageRecharge));
                secondary.attackSpeedBuffsRestockSpeed = C.Get(barrageAttackSpeed);
            }
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"{(C.Get(arcRapid) ? "<style=cIsUtility>Rapid.</style> " : "")}Fire lightning for <style=cIsDamage>{F(arcDamage):0.##}% damage</style>. Direct hits also strike up to <style=cIsUtility>{Mathf.Max(0, C.Get(arcChains))} nearby enemies</style> once. Missed shots can redirect from terrain to up to <style=cIsUtility>{Mathf.Max(0, C.Get(arcTerrainTargets))} nearby enemies</style>. Each hit has a <style=cIsUtility>{Mathf.Clamp(F(arcStunChance), 0f, 100f):0.##}% chance to Stun</style> for <style=cIsUtility>{F(arcStun):0.##}s</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Deploy a drone to bombard a targeted area, dealing <style=cIsDamage>{F(barrageDamage):0.##}% damage</style> per strike and <style=cIsUtility>Stunning</style> enemies hit.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Release an EMP for <style=cIsDamage>{F(empDamage):0.##}% damage</style>, <style=cIsUtility>Shocking</style> enemies for <style=cIsUtility>{F(empShock):0.##}s</style> and knocking them back. Gain <style=cIsUtility>{F(empSpeed):0.##}% movement speed</style> and <style=cIsDamage>{F(empAttackSpeed):0.##}% attack speed</style> for <style=cIsUtility>{F(empTime):0.##}s</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Charge a thunderbolt for <style=cIsDamage>{F(thunderDamageMin):0.##}–{F(thunderDamageMax):0.##}% damage</style> and launch <style=cIsUtility>{C.Get(orbCountMin)}–{C.Get(orbCountMax)} arcing orbs</style> that strike on impact for <style=cIsDamage>{F(orbDamageMin):0.##}–{F(orbDamageMax):0.##}% damage</style> each. <style=cIsUtility>Shock</style> enemies for <style=cIsUtility>{F(thunderShock):0.##}s</style>. At full charge, hits chain for <style=cIsDamage>{F(chainDamage):0.##}% of their damage</style> and <style=cIsUtility>Weaken</style> enemies for <style=cIsUtility>{F(chainWeak):0.##}s</style>. <style=cDeath>Overloads your weapons for up to {F(overloadMaximum):0.##}s</style> after firing.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary,
                () => $"\nRange: {F(arcRange):0.##}m\nAdditional Target Range: {F(arcChainRange):0.##}m\nTerrain Bounce Range: {F(arcTerrainRange):0.##}m", () => F(arcProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary,
                () => $"\nRadius: {F(barrageRadius):0.##}m\nDuration: {F(barrageDuration):0.##}s\nBase Strikes: {(barragePulses == null ? 0 : C.Get(barragePulses))}\nAdditional Strikes Per Drone Tier: {(barrageTierPulses == null ? 0 : C.Get(barrageTierPulses))}", () => F(barrageProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                () => $"\nRadius: {F(empRadius):0.##}m", () => F(empProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special,
                () => $"\nCharge Time: {F(chargeTime):0.##}s\nFull Charge Hold: {F(holdTime):0.##}s\nRadius: {F(thunderRadiusMin):0.##}–{F(thunderRadiusMax):0.##}m\nOrb Strike Radius: {F(orbRadiusMin):0.##}–{F(orbRadiusMax):0.##}m\nOrb Proc Coefficient: {F(orbProc):0.##}\nChain Proc Coefficient: {F(chainProc):0.##}\nChain Range: {F(chainRange):0.##}m", () => F(thunderProc));
        }

        internal static void Install(GameObject body)
        {
            if (!primary) return;
            SkillDef native = body.GetComponent<SkillLocator>().primary.skillFamily.variants[0].skillDef;
            secondary.icon = native.icon;
            secondary.baseRechargeInterval = native.baseRechargeInterval;
            secondary.beginSkillCooldownOnSkillEnd = native.beginSkillCooldownOnSkillEnd;
            primary.icon = RedIcon(RemoteDroneSkills.VanillaIcon("MageBody", "Primary", native.icon, 1));
            utility.icon = RedIcon(RemoteDroneSkills.VanillaIcon("RailgunnerBody", "Utility", native.icon));
            special.icon = RedIcon(RemoteDroneSkills.VanillaIcon("MageBody", "Secondary", native.icon));
            SetBuffIcon();
            RemoteDroneSkills.AddMachine(body, Weapon);
            RemoteDroneSkills.AddMachine(body, UtilityMachine);
            if (arcOn.Value) RemoteDroneSkills.SetSkill(body, "Primary", primary);
            if (barrageOn.Value) RemoteDroneSkills.SetSkill(body, "Secondary", secondary);
            if (empOn.Value) RemoteDroneSkills.SetSkill(body, "Utility", utility);
            if (thunderOn.Value) RemoteDroneSkills.SetSkill(body, "Special", special);
            if (!body.GetComponent<Control>()) body.AddComponent<Control>();
            RoR2Application.onLoad += () => BindNativeBarrage(native);
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteBombardmentDrone");
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)), prefix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(CheckSkill)));
            harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)), prefix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(CheckSkill)));
            harmony.Patch(AccessTools.PropertyGetter(typeof(GenericSkill), nameof(GenericSkill.icon)), postfix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(LockIcon)));
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)), postfix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(SpeedStats)));
            harmony.Patch(AccessTools.Method(typeof(ProjectileManager), "InitializeProjectile"), postfix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(ConfigureBarrageProjectile)));
            harmony.Patch(AccessTools.Method(typeof(B.BombardmentDroneProjectileEffect), "OnEnter"), prefix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(ConfigureBarrageState)));
            harmony.Patch(AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.TakeDamage)), prefix: new HarmonyMethod(typeof(RemoteBombardmentDrone), nameof(BarrageDamageSource)));
        }

        private static void BindNativeBarrage(SkillDef native)
        {
            SetBuffIcon();
            // Our custom states do not enter these native state machines. Hold
            // their asset preloads explicitly instead of relying on an AI drone
            // having used the corresponding attack earlier in the run.
            var preload = AccessTools.Method(typeof(EntityStatePreloadCatalog), "LoadEntityStateContent");
            object unloadMode = Enum.ToObject(preload.GetParameters()[1].ParameterType, 0); // Native default.
            foreach (Type state in new[] { typeof(B.BombardmentDroneRemoteOpSkill), typeof(B.BombardmentDroneProjectileSpawnRemoteOp),
                typeof(B.BombardmentDroneProjectileEffectRemoteOp), typeof(Command), typeof(EntityStates.Drone.Command.CommandBombardmentPosition) })
                preload.Invoke(null, new[] { (object)EntityStateCatalog.GetStateIndex(state), unloadMode });
            LoadAssets();
            var sample = new B.BombardmentDroneProjectileEffectRemoteOp();
            barrageCooldown = N(BarrageName, "Cooldown (s)", native.baseRechargeInterval, "Recharge time. Vanilla: " + native.baseRechargeInterval + "s.");
            barrageStocks = I(BarrageName, "Base Stocks", native.baseMaxStock, "Maximum stocks. Vanilla: " + native.baseMaxStock + ".");
            barrageRequired = I(BarrageName, "Stocks Required", native.requiredStock, "Stocks needed to activate. Vanilla: " + native.requiredStock + ".");
            barrageConsumed = I(BarrageName, "Stocks Consumed", native.stockToConsume, "Stocks spent per activation. Vanilla: " + native.stockToConsume + ".");
            barrageRecharge = I(BarrageName, "Stocks Recharged", native.rechargeStock, "Stocks restored per recharge. Vanilla: " + native.rechargeStock + ".");
            barrageAttackSpeed = C.BindBool(Root + " - " + BarrageName, "Attack Speed Scales Recharge", native.attackSpeedBuffsRestockSpeed, "Use the native skill's attack-speed recharge setting by default.");
            barrageDamage = N(BarrageName, "Damage Per Strike (%)", B.BombardmentDroneRemoteOpSkill.damageCoefficient * 100f, "Damage per strike. Defaults to the native Remote Operation primary.");
            barrageRadius = N(BarrageName, "Radius (m)", sample.radius, "Strike radius. Defaults to the native attack.");
            barrageIndicatorRadius = N(BarrageName, "Targeting Indicator Radius (m)", B.BombardmentDroneRemoteOpSkill.radius, "Radius of the native aiming indicator.");
            barrageDuration = N(BarrageName, "Duration (s)", sample.duration, "Bombardment duration before native attack-speed scaling.");
            barragePulses = I(BarrageName, "Base Strike Count", sample.rootPulseCount, "Base pulse count before native drone upgrade scaling.");
            barrageTierPulses = I(BarrageName, "Extra Strikes Per Drone Tier", sample.extraPulsesPerTier, "Additional pulses per native drone upgrade tier.");
            var controller = barrageProjectile ? barrageProjectile.GetComponent<ProjectileController>() : null;
            barrageProc = N(BarrageName, "Proc Coefficient", controller ? controller.procCoefficient : 1f, "Proc coefficient. Defaults to the native projectile.");
            barrageJuggle = N(BarrageName, "Upward Force", sample.juggleForce, "Native upward force per strike.");
            barrageAimTime = N(BarrageName, "Maximum Targeting Time (s)", B.BombardmentDroneRemoteOpSkill.maxDuration, "Maximum time to position the attack before it fires automatically.");
            barrageRange = N(BarrageName, "Targeting Range (m)", 1000f, "Terrain targeting distance. Vanilla: 1000m.");
            Refresh();
        }

        private static void SetBuffIcon()
        {
            if (speedBuff && RoR2Content.Buffs.CloakSpeed)
                speedBuff.iconSprite = RoR2Content.Buffs.CloakSpeed.iconSprite;
        }

        private static bool IsBarrage(ProjectileController projectile) => projectile && projectile.combo == BarrageCombo &&
            projectile.owner && projectile.owner.GetComponent<Control>();
        private static void ConfigureBarrageProjectile(ProjectileController projectileController)
        {
            if (IsBarrage(projectileController)) projectileController.procCoefficient = F(barrageProc);
        }
        private static void ConfigureBarrageState(B.BombardmentDroneProjectileEffect __instance)
        {
            if (!__instance.outer || !IsBarrage(__instance.outer.GetComponent<ProjectileController>())) return;
            __instance.radius = F(barrageRadius); __instance.duration = TimeValue(barrageDuration);
            __instance.rootPulseCount = Mathf.Max(1, C.Get(barragePulses));
            __instance.extraPulsesPerTier = Mathf.Max(0, C.Get(barrageTierPulses));
            __instance.juggleForce = F(barrageJuggle);
        }
        private static void BarrageDamageSource(DamageInfo __0)
        {
            if (__0 != null && __0.inflictor && IsBarrage(__0.inflictor.GetComponent<ProjectileController>()))
                __0.damageType.damageSource = DamageSource.Secondary;
        }

        private static bool CheckSkill(GenericSkill __instance, ref bool __result)
        {
            var control = __instance.GetComponent<Control>();
            if (!control || !control.Locked(__instance)) return true;
            __result = false;
            return false;
        }
        private static void LockIcon(GenericSkill __instance, ref Sprite __result)
        {
            var control = __instance.GetComponent<Control>();
            if (!control || !control.Locked(__instance)) return;
            if (!offlineIcon) offlineIcon = SkillCatalog.allSkillDefs.OfType<RailgunSkillDef>().Select(s => s.offlineIcon).FirstOrDefault(s => s);
            if (offlineIcon) __result = offlineIcon;
            else if (CharacterBody.CommonAssets.disabledSkill) __result = CharacterBody.CommonAssets.disabledSkill.icon;
        }
        private static void SpeedStats(CharacterBody __instance)
        {
            if (!speedBuff || !__instance.HasBuff(speedBuff)) return;
            var property = AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.moveSpeed));
            property.SetValue(__instance, __instance.moveSpeed * (1f + F(empSpeed) / 100f));
            var attackSpeed = AccessTools.Property(typeof(CharacterBody), nameof(CharacterBody.attackSpeed));
            attackSpeed.SetValue(__instance, __instance.attackSpeed * (1f + F(empAttackSpeed) / 100f));
        }

        private static void LoadAssets()
        {
            if (assetsLoaded) return;
            barrageProjectile = RemoteDroneSkills.NativeGameObject(new B.BombardmentDroneRemoteOpSkill(), "projectilePrefab");
            targetPrefab = RemoteDroneSkills.NativeGameObject(new B.BombardmentDroneRemoteOpSkill(), "areaIndicatorPrefab");
            orbArt = RemoteDroneSkills.NativeGameObject(new Command(), "orbEffectPrefab");
            chargeArt = RemoteDroneSkills.NativeGameObject(new EntityStates.Drone.Command.CommandBombardmentPosition(), "effectPrefab");
            arcFireSound = new EntityStates.Mage.Weapon.FireLightningBolt().attackSoundString;
            assetsLoaded = barrageProjectile && targetPrefab && orbArt && chargeArt;
            if ((!barrageProjectile || !targetPrefab) && !assetWarningLogged)
            {
                assetWarningLogged = true;
                ThreeQsVanillaTweaks.Log.Warning("Remote Bombardment Drone: native bombardment projectile or targeting indicator was not loaded.");
            }
        }

        private static Sprite RedIcon(Sprite source)
        {
            if (!source) return null;
            RenderTexture previous = RenderTexture.active;
            RenderTexture render = null;
            try
            {
                render = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(source.texture, render);
                RenderTexture.active = render;
                Rect rect = source.textureRect;
                var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float brightness = pixels[i].grayscale;
                    float alpha = pixels[i].a;
                    Color colour = Color.Lerp(new Color(.12f, .015f, .025f), new Color(1f, .18f, .22f), Mathf.Clamp01(brightness * 1.35f));
                    colour = Color.Lerp(colour, new Color(1f, .92f, .9f), Mathf.Clamp01((brightness - .65f) / .35f));
                    colour.a = alpha;
                    pixels[i] = colour;
                }
                texture.SetPixels(pixels); texture.Apply();
                texture.wrapMode = TextureWrapMode.Clamp;
                Sprite icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height), source.pixelsPerUnit);
                icon.name = source.name + "_3QSBombardmentRed";
                return icon;
            }
            finally
            {
                RenderTexture.active = previous;
                if (render) RenderTexture.ReleaseTemporary(render);
            }
        }

        private static TeamIndex Team(CharacterBody body) => body && body.teamComponent ? body.teamComponent.teamIndex : TeamIndex.None;
        private static bool Alive(CharacterBody body) => body && body.healthComponent && body.healthComponent.alive;
        private static HurtBox[] Around(CharacterBody owner, Vector3 point, float radius)
        {
            if (!Alive(owner) || radius <= 0f) return Array.Empty<HurtBox>();
            return new SphereSearch { origin = point, radius = radius, mask = LayerIndex.entityPrecise.mask }
                .RefreshCandidates().FilterCandidatesByHurtBoxTeam(TeamMask.GetEnemyTeams(Team(owner)))
                .OrderCandidatesByDistance().FilterCandidatesByDistinctHurtBoxEntities().GetHurtBoxes();
        }
        private static bool Ground(Ray aim, float range, out RaycastHit hit) =>
            Physics.Raycast(aim, out hit, range, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);

        private static void Damage(CharacterBody owner, HurtBox box, float damage, float proc, float shock,
            float weak, bool crit, DamageSource source)
        {
            if (!Alive(owner) || !box || !box.healthComponent || !box.healthComponent.alive) return;
            var type = new DamageTypeCombo { damageSource = source, damageTypeExtended = DamageTypeExtended.Electrical };
            var info = new DamageInfo { attacker = owner.gameObject, damage = damage, crit = crit,
                procCoefficient = proc, position = box.transform.position, inflictedHurtbox = box, damageType = type };
            box.healthComponent.TakeDamage(info);
            if (GlobalEventManager.instance)
            {
                GlobalEventManager.instance.OnHitEnemy(info, box.healthComponent.gameObject);
                GlobalEventManager.instance.OnHitAll(info, box.healthComponent.gameObject);
            }
            CharacterBody body = box.healthComponent.body;
            if (body && weak > 0f) body.AddTimedBuff(RoR2Content.Buffs.Weak, weak);
            var reaction = box.healthComponent.GetComponent<SetStateOnHurt>();
            if (reaction && shock > 0f) reaction.SetShock(shock);
        }

        private sealed class Control : NetworkBehaviour
        {
            private float overloadUntil;
            internal float OverloadRemaining => Mathf.Max(0f, overloadUntil - Run.FixedTimeStamp.now.t);
            internal bool Overloaded => OverloadRemaining > 0f;
            internal bool Locked(GenericSkill slot) => Overloaded && slot.characterBody && slot.characterBody.skillLocator &&
                slot != slot.characterBody.skillLocator.utility;
            internal void Overload(float seconds)
            {
                overloadUntil = Run.FixedTimeStamp.now.t + Mathf.Max(0f, seconds);
                if (NetworkServer.active) SetDirtyBit(1u);
            }
            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState) writer.WritePackedUInt32(syncVarDirtyBits);
                if (!initialState && syncVarDirtyBits == 0u) return false;
                writer.Write(overloadUntil);
                return true;
            }
            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (initialState || reader.ReadPackedUInt32() != 0u) overloadUntil = reader.ReadSingle();
            }
        }

        private sealed class ThunderSkill : SkillDef
        {
            public override bool IsReady(GenericSkill slot) => base.IsReady(slot) && HasGround(slot);
            public override bool CanExecute(GenericSkill slot) => base.CanExecute(slot) && HasGround(slot);
            public override void OnFixedUpdate(GenericSkill slot, float dt)
            {
                var control = slot.GetComponent<Control>();
                if (!control || !control.Overloaded) base.OnFixedUpdate(slot, dt);
            }
            private static bool HasGround(GenericSkill slot)
            {
                var input = slot.GetComponent<InputBankTest>();
                return input && Ground(input.GetAimRay(), F(thunderRange), out _);
            }
        }

        public sealed class Arc : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (!isAuthority) return;
                Ray aim = GetAimRay();
                outer.SetNextState(new FireArc { Aim = aim });
            }
        }

        public sealed class FireArc : BaseState
        {
            internal Ray Aim;
            public override void OnSerialize(NetworkWriter writer)
            { base.OnSerialize(writer); writer.Write(Aim.origin); writer.Write(Aim.direction); }
            public override void OnDeserialize(NetworkReader reader)
            { base.OnDeserialize(reader); Aim = new Ray(reader.ReadVector3(), reader.ReadVector3()); }
            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                if (NetworkServer.active)
                {
                    float distance = F(arcRange);
                    bool hitTerrain = Ground(Aim, distance, out RaycastHit wall);
                    if (hitTerrain) distance = wall.distance;
                    HurtBox target = null;
                    TeamMask enemies = TeamMask.GetEnemyTeams(Team(characterBody));
                    foreach (RaycastHit hit in Physics.SphereCastAll(Aim, Mathf.Max(.01f, F(arcHitRadius)), distance,
                        LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide).OrderBy(hit => hit.distance))
                    {
                        var box = hit.collider.GetComponent<HurtBox>();
                        if (!box || !box.healthComponent || !Alive(box.healthComponent.body) || !enemies.HasTeam(Team(box.healthComponent.body))) continue;
                        target = box;
                        break;
                    }
                    Vector3 from = FindModelChild("Muzzle") ? FindModelChild("Muzzle").position : characterBody.corePosition;
                    Vector3 to = target ? target.transform.position : Aim.GetPoint(distance);
                    Spawn(EffectKind.Bolt, characterBody, target ? target.healthComponent.body : null, from, to, 0f, .15f, true, true);
                    if (target)
                    {
                        // Every additional strike originates at the direct hit,
                        // with one fixed search radius and no recursive hopping.
                        HurtBox[] additional = Around(characterBody, to, F(arcChainRange))
                            .Where(box => box && box.healthComponent && box.healthComponent != target.healthComponent && box.healthComponent.alive)
                            .Take(Mathf.Max(0, C.Get(arcChains))).ToArray();
                        bool crit = RollCrit();
                        float damage = damageStat * F(arcDamage) / 100f;
                        ArcHit(characterBody, target, damage, crit);
                        foreach (HurtBox extra in additional)
                            OrbManager.instance.AddOrb(new ArcJump { origin = to, target = extra, Owner = characterBody,
                                DamageValue = damage, Crit = crit });
                    }
                    else if (hitTerrain)
                    {
                        Vector3 start = wall.point + wall.normal * .1f;
                        HurtBox[] redirects = Around(characterBody, wall.point, F(arcTerrainRange))
                            .Where(box => box && box.healthComponent && box.healthComponent.alive &&
                                !Physics.Linecast(start, box.transform.position, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                            .Take(Mathf.Max(0, C.Get(arcTerrainTargets))).ToArray();
                        bool crit = RollCrit();
                        foreach (HurtBox redirect in redirects)
                            OrbManager.instance.AddOrb(new ArcJump { origin = start, target = redirect, Owner = characterBody,
                                DamageValue = damageStat * F(arcDamage) / 100f, Crit = crit });
                    }
                }
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        private static void ArcHit(CharacterBody owner, HurtBox target, float damage, bool crit)
        {
            Damage(owner, target, damage, F(arcProc), 0f, 0f, crit, DamageSource.Primary);
            if (!Alive(owner) || !target || !target.healthComponent || !target.healthComponent.alive || F(arcStun) <= 0f) return;
            var hurt = target.healthComponent.GetComponent<SetStateOnHurt>();
            if (hurt && Util.CheckRoll(Mathf.Clamp(F(arcStunChance), 0f, 100f), owner.master)) hurt.SetStun(F(arcStun));
        }
        private sealed class ArcJump : Orb
        {
            internal CharacterBody Owner;
            internal float DamageValue;
            internal bool Crit;
            public override void Begin()
            {
                duration = Mathf.Max(.08f, distanceToTarget / 150f);
                Spawn(EffectKind.Bolt, Owner, target ? target.healthComponent.body : null, origin,
                    target ? target.transform.position : origin, 0f, duration, varyArc: true);
            }
            public override void OnArrival()
            {
                if (!target) return;
                ArcHit(Owner, target, DamageValue, Crit);
            }
        }

        public sealed class AimBarrage : BaseState
        {
            private GameObject indicator;
            private Vector3 point;
            private bool valid;
            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                if (isAuthority && targetPrefab)
                {
                    indicator = Instantiate(targetPrefab);
                    indicator.SetActive(false);
                }
                UpdateAim();
            }
            public override void Update()
            {
                base.Update();
                UpdateAim();
            }
            private void UpdateAim()
            {
                if (!isAuthority) return;
                if (Ground(GetAimRay(), F(barrageRange), out RaycastHit hit))
                {
                    valid = true; point = hit.point;
                    if (indicator)
                    {
                        indicator.SetActive(true);
                        indicator.transform.position = point;
                        indicator.transform.up = hit.normal;
                        indicator.transform.localScale = Vector3.one * F(barrageIndicatorRadius);
                    }
                }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                UpdateAim();
                if (isAuthority && inputBank && (inputBank.skill2.justReleased || fixedAge >= TimeValue(barrageAimTime)))
                {
                    if (valid) outer.SetNextState(new DeployBarrage { Point = point });
                    else outer.SetNextStateToMain();
                }
            }
            public override void OnExit()
            {
                if (indicator) Destroy(indicator);
                PlayCrossfade("Gesture, Additive", "Remove_Cage", .2f);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class DeployBarrage : BaseState
        {
            internal Vector3 Point;
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Point); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Point = reader.ReadVector3(); }
            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                if (NetworkServer.active && barrageProjectile)
                    ProjectileManager.instance.FireProjectile(new FireProjectileInfo {
                        projectilePrefab = barrageProjectile, position = Point, rotation = Quaternion.identity,
                        owner = gameObject, damage = damageStat * F(barrageDamage) / 100f,
                        crit = RollCrit(), comboNumber = BarrageCombo
                    });
                PlayCrossfade("Gesture, Additive", "Create_Cage", .2f);
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                PlayCrossfade("Gesture, Additive", "Remove_Cage", .2f);
                base.OnExit();
            }
        }

        public sealed class EMP : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                {
                    bool crit = RollCrit();
                    Spawn(EffectKind.EMP, characterBody, null, characterBody.corePosition, Vector3.zero, F(empRadius), .55f);
                    Impact(characterBody.corePosition, F(empRadius));
                    characterBody.AddTimedBuff(speedBuff, F(empTime));
                    foreach (HurtBox box in Around(characterBody, characterBody.corePosition, F(empRadius)))
                    {
                        CharacterBody body = box.healthComponent.body;
                        Damage(characterBody, box, damageStat * F(empDamage) / 100f, F(empProc), F(empShock), 0f, crit, DamageSource.Utility);
                        if (!Alive(body) || body.isBoss) continue;
                        Vector3 direction = body.corePosition - characterBody.corePosition;
                        if (direction.sqrMagnitude < .01f) direction = transform.forward;
                        var force = PhysForceInfo.Create();
                        force.force = (direction.normalized + Vector3.up * .2f).normalized * F(empPush);
                        force.massIsOne = true; force.ignoreGroundStick = true;
                        var motor = body.GetComponent<IPhysMotor>();
                        if (motor != null) motor.ApplyForceImpulse(in force);
                        else body.healthComponent.TakeDamageForce(force);
                    }
                }
                if (isAuthority) outer.SetNextStateToMain();
            }
        }

        public sealed class ChargeThunder : BaseState
        {
            private GameObject indicator, chargeEffect;
            private Vector3 point;
            private bool valid;
            private uint chargingSound, holdSound;
            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                if (isAuthority && targetPrefab) indicator = Instantiate(targetPrefab);
                if (chargeArt) chargeEffect = Visual.Art(chargeArt, FindModelChild("Muzzle") ?? transform);
                if (NetworkClient.active) chargingSound = Util.PlaySound("Play_railgunner_R_gun_chargeUp", gameObject);
                UpdateAim(0f);
            }
            private void UpdateAim(float fraction)
            {
                if (!isAuthority) return;
                if (Ground(GetAimRay(), F(thunderRange), out RaycastHit hit))
                {
                    valid = true; point = hit.point;
                    if (indicator)
                    {
                        indicator.transform.position = point;
                        indicator.transform.up = hit.normal;
                    }
                }
                if (indicator) indicator.transform.localScale = Vector3.one * Mathf.Lerp(F(thunderRadiusMin), F(thunderRadiusMax), fraction);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float fraction = Mathf.Clamp01(fixedAge / TimeValue(chargeTime));
                if (chargeEffect) chargeEffect.transform.localScale = Vector3.one * Mathf.Lerp(.4f, 1.3f, fraction);
                if (NetworkClient.active && fraction >= 1f && holdSound == 0u)
                {
                    if (chargingSound != 0u) AkSoundEngine.StopPlayingID(chargingSound);
                    chargingSound = 0u;
                    holdSound = Util.PlaySound("Play_railgunner_R_gun_timer_loop", gameObject);
                }
                if (!isAuthority || !inputBank) return;
                UpdateAim(fraction);
                float display = fraction < 1f ? fraction : Mathf.Clamp01(1f - (fixedAge - F(chargeTime)) / Mathf.Max(.02f, F(holdTime)));
                RemoteDroneSkills.ChargeDisplay(this, display, tint: Color.Lerp(Red, Color.white, fraction));
                if (inputBank.skill4.down && fixedAge < F(chargeTime) + F(holdTime)) return;
                if (valid) outer.SetNextState(new FireThunder { Point = point, Fraction = fraction,
                    ChargedFor = Mathf.Min(fixedAge, F(chargeTime)) });
                else outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (indicator) Destroy(indicator);
                if (chargeEffect) Destroy(chargeEffect);
                if (chargingSound != 0u) AkSoundEngine.StopPlayingID(chargingSound);
                if (holdSound != 0u)
                {
                    AkSoundEngine.StopPlayingID(holdSound);
                    Util.PlaySound("Stop_railgunner_R_gun_timer_loop", gameObject);
                }
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class FireThunder : BaseState
        {
            internal Vector3 Point;
            internal float Fraction, ChargedFor;
            public override void OnSerialize(NetworkWriter writer)
            { base.OnSerialize(writer); writer.Write(Point); writer.Write(Fraction); writer.Write(ChargedFor); }
            public override void OnDeserialize(NetworkReader reader)
            { base.OnDeserialize(reader); Point = reader.ReadVector3(); Fraction = reader.ReadSingle(); ChargedFor = reader.ReadSingle(); }
            public override void OnEnter()
            {
                base.OnEnter();
                float fraction = Mathf.Clamp01(Fraction);
                float overload = Mathf.Min(F(overloadMaximum), Mathf.Max(0f, ChargedFor) * F(overloadPerSecond));
                var control = GetComponent<Control>();
                if (control && (isAuthority || NetworkServer.active))
                    control.Overload(overload);
                if (NetworkServer.active)
                {
                    bool crit = RollCrit();
                    bool full = fraction >= .999f;
                    Strike(characterBody, Point, damageStat * Mathf.Lerp(F(thunderDamageMin), F(thunderDamageMax), fraction) / 100f,
                        Mathf.Lerp(F(thunderRadiusMin), F(thunderRadiusMax), fraction), F(thunderProc), crit, full);
                    int minimum = Mathf.Max(0, C.Get(orbCountMin));
                    int count = Mathf.RoundToInt(Mathf.Lerp(minimum, Mathf.Max(minimum, C.Get(orbCountMax)), fraction));
                    float offset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    for (int i = 0; i < count; i++)
                    {
                        float angle = offset + i * Mathf.PI * 2f / count;
                        Vector3 velocity = new Vector3(Mathf.Cos(angle) * F(orbSpeed), F(orbUpSpeed), Mathf.Sin(angle) * F(orbSpeed));
                        Visual visual = Spawn(EffectKind.Orb, characterBody, null, Point + Vector3.up * .5f, Vector3.zero, .4f, TimeValue(orbLifetime));
                        if (!visual) continue;
                        var flight = visual.gameObject.AddComponent<ThunderOrb>();
                        flight.Owner = characterBody; flight.Velocity = velocity;
                        flight.DamageValue = damageStat * Mathf.Lerp(F(orbDamageMin), F(orbDamageMax), fraction) / 100f;
                        flight.Radius = Mathf.Lerp(F(orbRadiusMin), F(orbRadiusMax), fraction);
                        flight.Crit = crit; flight.FullCharge = full;
                    }
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
            private Control control;
            private LoopSoundManager.SoundLoopPtr coolingLoop;
            private string rebootSound;
            public override void OnSerialize(NetworkWriter writer) { base.OnSerialize(writer); writer.Write(Duration); }
            public override void OnDeserialize(NetworkReader reader) { base.OnDeserialize(reader); Duration = reader.ReadSingle(); }
            public override void OnEnter()
            {
                base.OnEnter();
                control = GetComponent<Control>();
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
                base.FixedUpdate();
                float remaining = control ? control.OverloadRemaining : Mathf.Max(0f, Duration - fixedAge);
                RemoteDroneSkills.ChargeDisplay(this, Mathf.Clamp01(remaining / Mathf.Max(.02f, Duration)), true);
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

        private static void Strike(CharacterBody owner, Vector3 point, float damage, float radius, float proc, bool crit, bool full)
        {
            if (!NetworkServer.active || !Alive(owner)) return;
            Impact(point, radius);
            Spawn(EffectKind.Bolt, owner, null, point + Vector3.up * 60f, point, 0f, .3f);
            Spawn(EffectKind.Strike, owner, null, point, Vector3.zero, radius, .45f);
            HurtBox[] direct = Around(owner, point, radius);
            // Capture paths before the blast: a killed origin still produces its chains.
            var chains = new List<ThunderChain>();
            if (full)
                foreach (HurtBox origin in direct)
                {
                    if (!origin || !origin.healthComponent || !origin.healthComponent.alive) continue;
                    Vector3 start = origin.healthComponent.body ? origin.healthComponent.body.corePosition : origin.transform.position;
                    foreach (HurtBox target in Around(owner, start, F(chainRange)))
                        if (target && target.healthComponent && target.healthComponent != origin.healthComponent && target.healthComponent.alive)
                            chains.Add(new ThunderChain { origin = start, target = target, Owner = owner,
                                DamageValue = damage * F(chainDamage) / 100f, Crit = crit });
                }
            foreach (HurtBox target in direct)
                Damage(owner, target, damage, proc, F(thunderShock), 0f, crit, DamageSource.Special);
            foreach (ThunderChain chain in chains) OrbManager.instance.AddOrb(chain);
        }

        private sealed class ThunderChain : Orb
        {
            internal CharacterBody Owner;
            internal float DamageValue;
            internal bool Crit;
            public override void Begin()
            {
                duration = Mathf.Max(.08f, distanceToTarget / 120f);
                Spawn(EffectKind.Bolt, Owner, target && target.healthComponent ? target.healthComponent.body : null,
                    origin, target ? target.transform.position : origin, 0f, duration);
            }
            public override void OnArrival() =>
                Damage(Owner, target, DamageValue, F(chainProc), F(thunderShock), F(chainWeak), Crit, DamageSource.Special);
        }

        private sealed class ThunderOrb : MonoBehaviour
        {
            internal CharacterBody Owner;
            internal Vector3 Velocity;
            internal float DamageValue, Radius;
            internal bool Crit, FullCharge;
            private float age;
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                age += Time.fixedDeltaTime;
                if (!Alive(Owner) || age >= TimeValue(orbLifetime))
                { NetworkServer.Destroy(gameObject); return; }
                Velocity += Vector3.down * F(orbGravity) * Time.fixedDeltaTime;
                Vector3 movement = Velocity * Time.fixedDeltaTime;
                if (movement.sqrMagnitude > .000001f && Physics.SphereCast(transform.position, .25f,
                    movement.normalized, out RaycastHit hit, movement.magnitude, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
                {
                    Strike(Owner, hit.point, DamageValue, Radius, F(orbProc), Crit, FullCharge);
                    NetworkServer.Destroy(gameObject);
                    return;
                }
                else transform.position += movement;
            }
        }

        private enum EffectKind : byte { Bolt, Strike, EMP, Orb }

        internal static void RegisterPrefabs(ContentPack content)
        {
            if (!primary) return;
            var template = new GameObject("3QS Bombardment Visual Template");
            template.SetActive(false);
            template.AddComponent<NetworkIdentity>();
            var movement = template.AddComponent<NetworkTransform>();
            movement.transformSyncMode = NetworkTransform.TransformSyncMode.SyncTransform;
            movement.sendInterval = .05f;
            template.AddComponent<Visual>();
            visualPrefab = R2API.PrefabAPI.InstantiateClone(template, "3QSBombardmentDroneVisual", true);
            visualPrefab.SetActive(true);
            UnityEngine.Object.Destroy(template);
            content.networkedObjectPrefabs.Add(new[] { visualPrefab });
            GameObject nativeImpact = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/LightningStrikeImpact");
            if (nativeImpact)
            {
                // Keep the native EffectComponent, sounds, and animation scripts.
                // Only this registered copy owns the recoloured materials.
                lightningImpact = R2API.PrefabAPI.InstantiateClone(nativeImpact, "3QSBombardmentRedLightningImpact", false);
                RecolourLightning(lightningImpact);
                content.effectDefs.Add(new[] { new EffectDef(lightningImpact) });
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Bombardment Drone: native lightning impact effect is unavailable.");
        }

        private static Visual Spawn(EffectKind kind, CharacterBody owner, CharacterBody target,
            Vector3 origin, Vector3 end, float radius, float lifetime, bool playFireSound = false, bool varyArc = false)
        {
            if (!NetworkServer.active || !visualPrefab) return null;
            GameObject obj = UnityEngine.Object.Instantiate(visualPrefab, origin, Quaternion.identity);
            var visual = obj.GetComponent<Visual>();
            visual.Kind = kind; visual.Owner = owner; visual.Target = target;
            visual.Origin = origin; visual.End = end; visual.Radius = radius;
            visual.FireSound = playFireSound;
            if (varyArc)
            {
                // Pick once on the server so every observer sees the same shot.
                visual.ArcRoll = UnityEngine.Random.Range(0f, 360f);
                visual.ArcPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            }
            visual.Lifetime = Mathf.Max(.02f, lifetime); visual.StartedAt = Run.FixedTimeStamp.now.t;
            NetworkServer.Spawn(obj);
            if (kind != EffectKind.Orb) UnityEngine.Object.Destroy(obj, visual.Lifetime + .1f);
            return visual;
        }

        private static void Impact(Vector3 point, float radius)
        {
            LoadAssets();
            if (lightningImpact) EffectManager.SpawnEffect(lightningImpact,
                new EffectData { origin = point, scale = Mathf.Max(.5f, radius), color = Red }, true);
        }

        private static Color RedTint(Color colour)
        {
            float intensity = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            return new Color(Red.r * intensity, Red.g * intensity, Red.b * intensity, colour.a);
        }

        private static Gradient RedGradient(Gradient original)
        {
            var gradient = new Gradient { mode = original.mode };
            gradient.SetKeys(original.colorKeys.Select(key => new GradientColorKey(RedTint(key.color), key.time)).ToArray(), original.alphaKeys);
            return gradient;
        }

        private static ParticleSystem.MinMaxGradient RedGradient(ParticleSystem.MinMaxGradient original)
        {
            switch (original.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(RedTint(original.color));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(RedTint(original.colorMin), RedTint(original.colorMax));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(RedGradient(original.gradientMin), RedGradient(original.gradientMax));
                default:
                    var gradient = new ParticleSystem.MinMaxGradient(RedGradient(original.gradient));
                    gradient.mode = original.mode;
                    return gradient;
            }
        }

        private static void RecolourLightning(GameObject effect)
        {
            var resources = effect.AddComponent<ArtResources>();
            resources.Ramp = new Texture2D(32, 1, TextureFormat.RGBA32, false);
            resources.Ramp.wrapMode = TextureWrapMode.Clamp;
            for (int i = 0; i < 32; i++)
                resources.Ramp.SetPixel(i, 0, Color.Lerp(new Color(.04f, 0f, 0f, 0f), Red, i / 31f));
            resources.Ramp.Apply();
            foreach (Renderer renderer in effect.GetComponentsInChildren<Renderer>(true))
            {
                Material[] originals = renderer.sharedMaterials;
                var materials = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    if (!originals[i]) continue;
                    Material material = materials[i] = new Material(originals[i]);
                    resources.Materials.Add(material);
                    foreach (string property in new[] { "_TintColor", "_Color", "_EmissionColor" })
                        if (material.HasProperty(property)) material.SetColor(property, RedTint(material.GetColor(property)));
                    if (material.HasProperty("_RemapTex")) material.SetTexture("_RemapTex", resources.Ramp);
                }
                renderer.sharedMaterials = materials;
                if (renderer is LineRenderer line) line.colorGradient = RedGradient(line.colorGradient);
                else if (renderer is TrailRenderer trail) trail.colorGradient = RedGradient(trail.colorGradient);
            }
            foreach (ParticleSystem particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = RedGradient(main.startColor);
                var lifetime = particles.colorOverLifetime;
                if (lifetime.enabled) lifetime.color = RedGradient(lifetime.color);
                var speed = particles.colorBySpeed;
                if (speed.enabled) speed.color = RedGradient(speed.color);
                var trails = particles.trails;
                if (trails.enabled)
                {
                    trails.colorOverLifetime = RedGradient(trails.colorOverLifetime);
                    trails.colorOverTrail = RedGradient(trails.colorOverTrail);
                }
            }
            foreach (Light light in effect.GetComponentsInChildren<Light>(true)) light.color = RedTint(light.color);
        }

        private sealed class ArtResources : MonoBehaviour
        {
            internal readonly List<Material> Materials = new List<Material>();
            internal Texture2D Ramp;
            private void OnDestroy()
            {
                foreach (Material material in Materials) if (material) Destroy(material);
                if (Ramp) Destroy(Ramp);
            }
        }

        private sealed class Visual : NetworkBehaviour
        {
            internal EffectKind Kind;
            internal CharacterBody Owner, Target;
            internal Vector3 Origin, End;
            internal float Radius, Lifetime, StartedAt;
            internal float ArcRoll, ArcPhase;
            internal bool FireSound;
            private readonly List<LineRenderer> lines = new List<LineRenderer>();
            private GameObject art;

            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState) return false;
                writer.Write((byte)Kind);
                writer.Write(Owner ? Owner.gameObject : null); writer.Write(Target ? Target.gameObject : null);
                writer.Write(Origin); writer.Write(End); writer.Write(Radius); writer.Write(Lifetime); writer.Write(StartedAt);
                writer.Write(FireSound);
                writer.Write(ArcRoll); writer.Write(ArcPhase);
                return true;
            }
            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (!initialState) return;
                Kind = (EffectKind)reader.ReadByte();
                GameObject owner = reader.ReadGameObject(), target = reader.ReadGameObject();
                Owner = owner ? owner.GetComponent<CharacterBody>() : null;
                Target = target ? target.GetComponent<CharacterBody>() : null;
                Origin = reader.ReadVector3(); End = reader.ReadVector3();
                Radius = reader.ReadSingle(); Lifetime = reader.ReadSingle(); StartedAt = reader.ReadSingle();
                FireSound = reader.ReadBoolean();
                ArcRoll = reader.ReadSingle(); ArcPhase = reader.ReadSingle();
            }
            public override void OnStartClient()
            {
                base.OnStartClient();
                LoadAssets();
                if (FireSound && Owner && !string.IsNullOrEmpty(arcFireSound)) Util.PlaySound(arcFireSound, Owner.gameObject);
                if (Kind == EffectKind.Bolt) lines.Add(Line("Lightning", .1f, 18));
                else if (Kind == EffectKind.Orb)
                {
                    if (orbArt) art = Art(orbArt, transform);
                    for (int i = 0; i < 3; i++) lines.Add(Line("Orb Ring", .07f, 32));
                }
                else
                    for (int i = 0; i < (Kind == EffectKind.EMP ? 3 : 2); i++) lines.Add(Line("Pulse", .14f, 64));
            }

            internal static GameObject Art(GameObject prefab, Transform parent)
            {
                if (!prefab) return null;
                var holder = new GameObject("Bombardment Electrical Art");
                holder.SetActive(false);
                holder.transform.SetParent(parent, false);
                GameObject clone = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
                foreach (MonoBehaviour script in clone.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                var resources = holder.AddComponent<ArtResources>();
                resources.Ramp = new Texture2D(32, 1, TextureFormat.RGBA32, false);
                resources.Ramp.wrapMode = TextureWrapMode.Clamp;
                for (int i = 0; i < 32; i++)
                    resources.Ramp.SetPixel(i, 0, Color.Lerp(new Color(.06f, 0f, .01f, 0f), Red, i / 31f));
                resources.Ramp.Apply();
                foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] original = renderer.sharedMaterials;
                    var materials = new Material[original.Length];
                    for (int i = 0; i < original.Length; i++)
                    {
                        if (!original[i]) continue;
                        Material material = materials[i] = new Material(original[i]);
                        resources.Materials.Add(material);
                        if (material.HasProperty("_RemapTex")) material.SetTexture("_RemapTex", resources.Ramp);
                        if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", Red);
                        if (material.HasProperty("_Color")) material.SetColor("_Color", Red);
                    }
                    renderer.sharedMaterials = materials;
                }
                foreach (ParticleSystem system in clone.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = system.main;
                    main.startColor = Red;
                }
                holder.SetActive(true);
                foreach (ParticleSystem system in clone.GetComponentsInChildren<ParticleSystem>(true)) system.Play();
                return holder;
            }

            private LineRenderer Line(string name, float width, int count)
            {
                var line = new GameObject(name).AddComponent<LineRenderer>();
                line.transform.SetParent(transform, false);
                line.useWorldSpace = true;
                line.positionCount = count;
                line.widthMultiplier = width;
                line.sharedMaterial = Graphic.defaultGraphicMaterial;
                line.startColor = line.endColor = Red;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                return line;
            }
            private void LateUpdate()
            {
                float age = Mathf.Max(0f, Run.FixedTimeStamp.now.t - StartedAt);
                float progress = Mathf.Clamp01(age / Mathf.Max(.02f, Lifetime));
                Color tint = Color.Lerp(Red, new Color(1f, .8f, .8f, 0f), progress);
                foreach (LineRenderer line in lines)
                {
                    line.startColor = line.endColor = Kind == EffectKind.Orb ? Red : tint;
                    line.enabled = Kind == EffectKind.Orb || progress < 1f;
                }
                if (Kind == EffectKind.Bolt && lines.Count > 0)
                {
                    Vector3 end = Target ? Target.corePosition : End;
                    Vector3 direction = end - Origin;
                    Vector3 axis = direction.sqrMagnitude > .000001f ? direction.normalized : Vector3.forward;
                    Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > .98f ? Vector3.right : Vector3.up;
                    Vector3 side = Quaternion.AngleAxis(ArcRoll, axis) * Vector3.Cross(axis, reference).normalized;
                    Vector3 other = Vector3.Cross(axis, side).normalized;
                    for (int i = 0; i < lines[0].positionCount; i++)
                    {
                        float t = i / (float)(lines[0].positionCount - 1);
                        float envelope = Mathf.Sin(t * Mathf.PI) * Mathf.Min(1f, direction.magnitude * .05f);
                        float phase = Mathf.Floor(age * 25f) + ArcPhase;
                        Vector3 jitter = side * Mathf.Sin(i * 13.7f + phase * 2.1f) + other * Mathf.Cos(i * 7.3f + phase * 1.3f);
                        lines[0].SetPosition(i, Vector3.Lerp(Origin, end, t) + jitter * envelope);
                    }
                    return;
                }
                for (int ring = 0; ring < lines.Count; ring++)
                {
                    LineRenderer line = lines[ring];
                    float radius = Kind == EffectKind.Orb ? .3f : Radius * Mathf.Lerp(.08f, 1f, Mathf.Sqrt(progress));
                    for (int i = 0; i < line.positionCount; i++)
                    {
                        float angle = i * Mathf.PI * 2f / (line.positionCount - 1);
                        Vector3 offset = new Vector3(Mathf.Cos(angle), .03f + ring * .12f, Mathf.Sin(angle)) * radius;
                        if (Kind == EffectKind.EMP || Kind == EffectKind.Orb)
                        {
                            if (ring == 1) offset = new Vector3(offset.x, offset.z, offset.y);
                            if (ring == 2) offset = new Vector3(offset.y, offset.x, offset.z);
                        }
                        line.SetPosition(i, transform.position + offset);
                    }
                }
            }
        }
    }
}
