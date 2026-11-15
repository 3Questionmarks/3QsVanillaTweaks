using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Orbs;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    // Only installed on EmergencyDroneBodyRemoteOp. Never changes the AI drone prefab.
    internal static class RemoteEmergencyDrone
    {
        private const string PrimaryMachine = "3QSRemoteEmergencyPrimary";
        private const string RestorationMachine = "3QSRemoteEmergencyRestoration";
        private const string NovaMachine = "3QSRemoteEmergencyNova";
        private const string OverdriveMachine = "3QSRemoteEmergencyOverdrive";
        internal static readonly Type[] States = { typeof(EmergencyHealBeams), typeof(PiercingRestoration), typeof(HealingNova), typeof(Overdrive),
            typeof(EnhancedHealBeams), typeof(EnhancedSalvo), typeof(EnhancedNova) };
        private static ConfigEntry<bool> enabled, primaryEnabled, secondaryEnabled, novaEnabled, overdriveEnabled;
        private static ConfigEntry<int> beamTargets;
        private static ConfigEntry<float> beamRange, beamAngle, beamRateMultiplier, beamCooldown, shotDotDamage, shotDotInterval, shotDotDuration, shotLeech;
        private static ConfigEntry<float> shotDamage, shotHeal, shotProc, shotSlow, shotRange, shotAngle, shotCooldown, shotBounceRange;
        private static ConfigEntry<int> shotBounces;
        private static ConfigEntry<float> novaCharge, novaMinimumCharge, novaHold, novaMin, novaMax, novaRadius, novaFollowup, novaCooldown;
        private static ConfigEntry<float> novaMoveMin, novaMoveMax, novaArmourMin, novaArmourMax, novaBuffDuration;
        private static ConfigEntry<float> novaRegeneration, novaRegenerationDuration, novaKnockback;
        private static ConfigEntry<float> overdriveDuration, overdriveCooldown, overdriveSalvoRecharge, overdriveBeamMultiplier, overdriveDot, overdriveDotDuration, overdriveAttackBuff, overdriveSpeedBuff, overdriveDamageReduction;
        private static ConfigEntry<int> overdriveBounces;
        private static SkillDef primary, secondary, utility, special, enhancedPrimary, enhancedSecondary, enhancedUtility;
        private static BuffDef novaBuff, novaRegenBuff, overdriveBuff, overdriveAllyBuff;
        private static Sprite novaIcon, novaRegenIcon;
        private static GameObject orbEffect;
        private static bool harmonyInstalled;

        private const string Root = "Remote Operation - Emergency Drone";

        // Verified Wwise events from the November 4 skill-sound log.
        private const string BeamAttachSound = "Play_seeker_skill1_fire_orb";
        private const string SalvoLaunchSound = "Play_treeBot_m1_shoot";
        private const string SalvoEnemyImpactSound = "Play_seeker_skill1_fire";
        private const string SalvoAllyImpactSound = "Play_item_proc_TPhealingNova_hitPlayer";
        private const string NovaChargeSound = "Play_seeker_skill4_start";
        private const string NovaStopChargeSound = "Stop_seeker_skill4_start";
        private const string NovaChargedSound = "Play_seeker_skill4_win";
        private const string NovaReleaseSound = "Play_seeker_skill3_explo";
        private const string NovaFollowupSound = "Play_item_proc_TPhealingNova_hitPlayer";
        private const string OverdriveStartSound = "Play_seeker_skill4_win";
        private const string OverdriveEndSound = "Play_item_proc_personal_shield_end";

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable player-controlled Emergency Drone abilities; does not change AI drones.");
            string primarySection = Root + " - Healing Beams";
            primaryEnabled = C.BindStartupBool(primarySection, "Changes Enabled", true,
                "Use native healing beams with selectable ally targets; only applies to the player-controlled Emergency Drone.");
            beamTargets = RemoteDroneSkills.Number(primarySection, "Maximum Beams", 8, "Maximum simultaneous healing targets.");
            beamRange = RemoteDroneSkills.Number(primarySection, "Target Range (m)", 60f, "Maximum distance to healing targets.");
            beamAngle = RemoteDroneSkills.Number(primarySection, "Initial Target Angle (degrees)", 18f, "Aim tolerance for the selected ally.");
            beamRateMultiplier = RemoteDroneSkills.Number(primarySection, "Native Healing Rate Multiplier", 1f,
                "Multiplier applied to the native HealBeamController healing rate. Vanilla: 1.");
            beamCooldown = RemoteDroneSkills.Number(primarySection, "Cooldown (s)", 0f,
                "Healing beam cooldown. Vanilla 10");
            string s = Root + " - Siphoning Salvo";
            secondaryEnabled = C.BindStartupBool(s, "Changes Enabled", true, "Enable Siphoning Salvo as Secondary.");
            shotDamage = RemoteDroneSkills.Number(s, "Damage (%)", 250f, "Damage to enemies based on the drone's damage.");
            shotHeal = RemoteDroneSkills.Number(s, "Ally Healing (%)", 250f, "Healing to allies based on the drone's damage; excess becomes barrier.");
            shotProc = RemoteDroneSkills.Number(s, "Proc Coefficient", 1f, "Proc coefficient per enemy struck.");
            shotSlow = RemoteDroneSkills.Number(s, "Slow Duration (s)", 3f, "Slow duration on enemy hits.");
            shotRange = RemoteDroneSkills.Number(s, "Target Range (m)", 80f, "Maximum initial target range.");
            shotAngle = RemoteDroneSkills.Number(s, "Target Angle (degrees)", 12f, "Aim tolerance for initial target selection.");
            shotCooldown = RemoteDroneSkills.Number(s, "Recharge Time (s)", 2f, "Base time to restore one spent orb).");
            shotDotDamage = RemoteDroneSkills.Number(s, "Leech Damage Per Stack Per Tick (%)", 10f,
                "The damage the leech effect does when applied with this skill, based on this drone's damage.");
            shotDotInterval = RemoteDroneSkills.Number(s, "Leech Tick Interval (s)", .2f, "Time between damage-over-time ticks when applied with this skill.");
            shotDotDuration = RemoteDroneSkills.Number(s, "Leech Duration (s)", 3f, "Duration of leech when attacking a slowed target.");
            shotLeech = RemoteDroneSkills.Number(s, "Leech Healing Per Stack (%)", 10f,
                "Each Leech stack restores this percentage of damage dealt to the affected enemy to the attacker.");
            shotBounceRange = RemoteDroneSkills.Number(s, "Bounce Range (m)", 20f, "Maximum distance between Siphoning Salvo bounce targets.");
            shotBounces = RemoteDroneSkills.Number(s, "Additional Bounces", 0, "Extra bounces without Overdrive.");
            s = Root + " - Healing Nova";
            novaEnabled = C.BindStartupBool(s, "Changes Enabled", true, "Enable the charged Healing Nova Utility.");
            novaCharge = RemoteDroneSkills.Number(s, "Charge Time (s)", 2f, "Time to reach maximum charge at base attack speed.");
            novaMinimumCharge = RemoteDroneSkills.Number(s, "Minimum Charge Time (s)", .2f, "Minimum time before Healing Nova can fire, even if the button is released immediately.");
            novaHold = RemoteDroneSkills.Number(s, "Full Charge Hold (s)", 2f, "Additional time before automatic release.");
            novaMin = RemoteDroneSkills.Number(s, "Minimum Healing (%)", 200f, "Initial pulse healing as a percentage of the drone's damage.");
            novaMax = RemoteDroneSkills.Number(s, "Maximum Healing (%)", 800f, "Initial pulse healing at full charge, based on drone damage.");
            novaRadius = RemoteDroneSkills.Number(s, "Radius (m)", 25f, "Radius of all Nova pulses.");
            novaFollowup = RemoteDroneSkills.Number(s, "Followup Healing (%)", 50f, "Follow-up pulses heal for this percentage of the initial pulse.");
            novaCooldown = RemoteDroneSkills.Number(s, "Cooldown (s)", 12f, "Utility cooldown after releasing the Nova.");
            novaMoveMin = RemoteDroneSkills.Number(s, "Minimum Movement Speed Bonus (%)", 10f, "Movement speed bonus at minimum charge.");
            novaMoveMax = RemoteDroneSkills.Number(s, "Maximum Movement Speed Bonus (%)", 50f, "Movement speed bonus at full charge.");
            novaArmourMin = RemoteDroneSkills.Number(s, "Minimum Armour Bonus", 10f, "Armour at minimum charge.");
            novaArmourMax = RemoteDroneSkills.Number(s, "Maximum Armour Bonus", 50f, "Armour at full charge.");
            novaBuffDuration = RemoteDroneSkills.Number(s, "Buff Duration (s)", 6f, "The duration of the buffs applied; Buffs duration is refreshed by healing pulses.");
            novaRegeneration = RemoteDroneSkills.Number(s, "Full Charge Regeneration (% Max HP/s)", 5f, "Full charge only: percentage of recipient maximum health healed each second.");
            novaRegenerationDuration = RemoteDroneSkills.Number(s, "Full Charge Regeneration Duration (s)", 3f, "Duration of full-charge regeneration.");
            novaKnockback = RemoteDroneSkills.Number(s, "Enemy Push Force", 50f, "Push strength against enemies caught in the initial Healing Nova pulse.");
            s = Root + " - Overdrive";
            overdriveEnabled = C.BindStartupBool(s, "Changes Enabled", true, "Enable Overdrive Special.");
            overdriveDuration = RemoteDroneSkills.Number(s, "Duration (s)", 15f, "Overdrive duration.");
            overdriveCooldown = RemoteDroneSkills.Number(s, "Cooldown (s)", 15f, "Cooldown after duration.");
            overdriveBeamMultiplier = RemoteDroneSkills.Number(s, "Primary Beam Multiplier", 1.5f, "Native primary beam healing multiplier during Overdrive.");
            overdriveSalvoRecharge = RemoteDroneSkills.Number(s, "Enhanced Siphoning Salvo Recharge Time (s)", 1f,
                "Base time to recharge one enhanced Secondary stock during Overdrive.");
            overdriveBounces = RemoteDroneSkills.Number(s, "Enhanced Siphoning Salvo Bonus Bounces", 2, "Additional Siphoning Salvo bounces while Overdrive is active.");
            overdriveDot = RemoteDroneSkills.Number(s, "Secondary Damage Over Time (%/s)", 50f, "Enemy damage per second during Overdrive's Secondary debuff, based on drone damage.");
            overdriveDotDuration = RemoteDroneSkills.Number(s, "Secondary Buff/Debuff Duration (s)", 3f, "Duration of enhanced Secondary effects.");
            overdriveAttackBuff = RemoteDroneSkills.Number(s, "Secondary Ally Attack Speed (%)", 30f, "Attack speed bonus from the enhanced Secondary.");
            overdriveSpeedBuff = RemoteDroneSkills.Number(s, "Secondary Ally Movement Speed (%)", 30f, "Movement speed bonus from the enhanced Secondary.");
            overdriveDamageReduction = RemoteDroneSkills.Number(s, "Secondary Ally Damage Reduction (%)", 30f, "Damage reduction from the enhanced Secondary.");
        }

        internal static BuffDef[] Buffs => enabled != null && enabled.Value
            ? new[] { novaBuff, novaRegenBuff, overdriveBuff, overdriveAllyBuff }.Where(b => b).ToArray()
            : Array.Empty<BuffDef>();

        private static BuffDef CreateBuff(string name, Color colour, bool debuff)
        {
            var buff = ScriptableObject.CreateInstance<BuffDef>();
            buff.name = "3QSRemoteEmergency" + name;
            buff.canStack = false;
            buff.isDebuff = debuff;
            buff.buffColor = colour;
            buff.iconSprite = debuff ? RoR2Content.Buffs.Slow50?.iconSprite : RoR2Content.Buffs.Warbanner?.iconSprite;
            return buff;
        }

        // Self-contained, opaque-background status icons; no shader lookup or asset bundle.
        private static Sprite MakeNovaStatusIcon(Color accent)
        {
            const int size = 48;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "3QS Emergency Nova Status Icon";
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[size * size];
            var colour = (Color32)accent;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - (size - 1) * .5f;
                float dy = y - (size - 1) * .5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                bool ring = distance >= 16f && distance <= 20f;
                bool cross = (Mathf.Abs(dx) < 3.3f && Mathf.Abs(dy) < 12f) ||
                             (Mathf.Abs(dy) < 3.3f && Mathf.Abs(dx) < 12f);
                pixels[y * size + x] = ring || cross ? colour : new Color32(0, 0, 0, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }

        internal static void Register()
        {
            if (enabled == null || !enabled.Value) return;
            if (primaryEnabled.Value)
                primary = RemoteDroneSkills.MakeSkill("EmergencyBeams", "Healing Beams",
                    "Connect healing beams to nearby allies. Requires a selected ally.", typeof(EmergencyHealBeams),
                    PrimaryMachine, 0f, null);
            if (secondaryEnabled.Value)
            {
                secondary = RemoteDroneSkills.MakeSkill("EmergencyRestoration", "Siphoning Salvo",
                    "<style=cIsUtility>Rapid.</style> Fire all available orbs. <style=cIsUtility>Slow</style> enemies and inflict <style=cIsDamage>Leech</style> on Slowed targets.",
                    typeof(PiercingRestoration), RestorationMachine, C.Get(shotCooldown), null);
                secondary.attackSpeedBuffsRestockSpeed = false; // Explicit recharge scaling runs on the controlled drone.
                secondary.attackSpeedBuffsRestockSpeed_Multiplier = 1f;
                secondary.keywordTokens = new[] { "KEYWORD_RAPID" };
                secondary.mustKeyPress = false; // Repeated volleys are allowed with a short held-input interval.
            }
            if (novaEnabled.Value)
                utility = RemoteDroneSkills.MakeSkill("EmergencyNova", "Healing Nova",
                    "Charge a <style=cIsHealing>healing nova</style>. Full charge grants <style=cIsHealing>regeneration</style>; subsequent pulses refresh its bonuses.",
                    typeof(HealingNova), NovaMachine, C.Get(novaCooldown), null, true);
            if (overdriveEnabled.Value)
                special = RemoteDroneSkills.MakeSkill("EmergencyOverdrive", "Overdrive",
                    "<style=cIsUtility>Empower</style> Healing Beams, Siphoning Salvo and Healing Nova for <style=cIsUtility>15 seconds</style>.",
                    typeof(Overdrive), OverdriveMachine, C.Get(overdriveCooldown), null);
            R2API.LanguageAPI.Add("KEYWORD_RAPID",
                "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
            novaBuff = CreateBuff("Nova", new Color(.25f, .85f, 1f), false);
            novaRegenBuff = CreateBuff("NovaRegen", new Color(.4f, 1f, .6f), false);
            novaIcon = MakeNovaStatusIcon(new Color(.2f, .9f, 1f));
            novaRegenIcon = MakeNovaStatusIcon(new Color(.4f, 1f, .5f));
            novaBuff.iconSprite = novaIcon;
            novaRegenBuff.iconSprite = novaRegenIcon;
            overdriveBuff = CreateBuff("Overdrive", new Color(1f, .75f, .2f), false);
            overdriveAllyBuff = CreateBuff("OverdriveAlly", new Color(.3f, .9f, .55f), false);
            // User-facing status names and descriptions. Keep tokens separate from
            // the BuffDef internal name for UI mods that support buff tooltips.
            R2API.LanguageAPI.Add("3QS_EMERGENCY_NOVA_BUFF_NAME", "Healing Nova");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_NOVA_BUFF_DESCRIPTION", "Increased movement speed and armour. Strength scales with Healing Nova charge; further pulses refresh the duration.");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_NOVA_REGEN_NAME", "Nova Regeneration");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_NOVA_REGEN_DESCRIPTION", "Regenerates a percentage of maximum health each second after a fully charged Healing Nova.");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_OVERDRIVE_BUFF_NAME", "Overdrive");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_OVERDRIVE_BUFF_DESCRIPTION", "Empowers the Emergency Drone's Primary, Secondary and Utility skills for the remaining duration.");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_OVERDRIVE_ALLY_NAME", "Overdrive Support");
            R2API.LanguageAPI.Add("3QS_EMERGENCY_OVERDRIVE_ALLY_DESCRIPTION", "Increased attack speed and movement speed, and reduced incoming damage from Enhanced Siphoning Salvo.");
            // Separate skill definitions let the game show their enhanced icons and
            // execute enhanced states without scene-wide beam scans during Overdrive.
            enhancedPrimary = EnhancedDefinition(primary, "EmergencyBeamsOverdrive", typeof(EnhancedHealBeams),
                "Attach enhanced healing beams to allies. <style=cIsHealing>Overhealing grants barrier.</style>");
            enhancedSecondary = EnhancedDefinition(secondary, "EmergencySalvoOverdrive", typeof(EnhancedSalvo),
                "<style=cIsUtility>Rapid.</style> Fire bouncing orbs that <style=cIsUtility>Slow</style> enemies and inflict <style=cIsDamage>Leech</style> on Slowed targets.");
            enhancedUtility = EnhancedDefinition(utility, "EmergencyNovaOverdrive", typeof(EnhancedNova),
                "Charge a nova that <style=cIsHealing>heals allies</style> and <style=cIsDamage>damages enemies</style>, followed by additional pulses.");
            Refresh();
        }

        private static SkillDef EnhancedDefinition(SkillDef original, string id, Type state, string description)
        {
            if (!original) return null;
            SkillDef def = UnityEngine.Object.Instantiate(original);
            ((ScriptableObject)def).name = def.skillName = "3QSRemote" + id;
            def.skillNameToken = "3QS_REMOTE_" + id.ToUpperInvariant() + "_NAME";
            def.skillDescriptionToken = "3QS_REMOTE_" + id.ToUpperInvariant() + "_DESCRIPTION";
            def.activationState = new SerializableEntityStateType(state);
            def.fullRestockOnAssign = false; // preserve stocks across Overdrive transitions
            if (id.Contains("Salvo"))
            {
                // Show the vanilla Rapid keyword while the explicit recharge controller
                // handles stock recovery on this remote-controlled body.
                def.attackSpeedBuffsRestockSpeed = false;
                def.attackSpeedBuffsRestockSpeed_Multiplier = 1f;
                def.keywordTokens = new[] { "KEYWORD_RAPID" };
            }
            def.mustKeyPress = false;
            R2API.LanguageAPI.Add(def.skillNameToken, id.Contains("Beams") ? "Healing Beams+" :
                id.Contains("Salvo") ? "Siphoning Salvo+" : "Healing Nova+");
            R2API.LanguageAPI.Add(def.skillDescriptionToken, description);
            RemoteDroneSkills.Definitions.Add(def);
            return def;
        }

        internal static void Refresh()
        {
            if (primary)
            {
                Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary,
                    () => $"\nBeam Range: {C.Get(beamRange):0.##}m\nMaximum Beams: {C.Get(beamTargets)}", () => 0f);
                if (enhancedPrimary)
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(enhancedPrimary,
                        () => $"\nBeam Range: {C.Get(beamRange):0.##}m\nMaximum Beams: {C.Get(beamTargets)}\nHealing Multiplier: x{C.Get(overdriveBeamMultiplier):0.##}", () => 0f);
                // Zero retains the native skill's cooldown, captured from its original family.
                if (C.Get(beamCooldown) > 0f) primary.baseRechargeInterval = C.Get(beamCooldown);
                if (enhancedPrimary) enhancedPrimary.baseRechargeInterval = primary.baseRechargeInterval;
                Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                    $"Attach up to <style=cIsHealing>{C.Get(beamTargets)} healing beams</style> to allies, each restoring <style=cIsHealing>{50f * C.Get(beamRateMultiplier):0.##}% damage per second</style>. Beams remain attached until detached.");
                if (enhancedPrimary)
                    Tweaks.Survivors.SurvivorSettings.SetSkillText(enhancedPrimary, () =>
                        $"Attach up to <style=cIsHealing>{C.Get(beamTargets)} enhanced healing beams</style> to allies, each restoring <style=cIsHealing>{50f * C.Get(beamRateMultiplier) * C.Get(overdriveBeamMultiplier):0.##}% damage per second</style>. <style=cIsHealing>Overhealing grants barrier.</style> Beams remain attached until detached.");
            }
            if (secondary)
            {
                secondary.baseRechargeInterval = Mathf.Max(.05f, C.Get(shotCooldown));
                // One stock is enough to fire; the payment hook consumes the actual amount
                // available and passes the exact volley size to the projectile state.
                secondary.requiredStock = secondary.stockToConsume = 1;
                secondary.attackSpeedBuffsRestockSpeed = false; // Explicit recharge scaling runs on the controlled drone.
                secondary.attackSpeedBuffsRestockSpeed_Multiplier = 1f;
                secondary.keywordTokens = new[] { "KEYWORD_RAPID" };
                if (enhancedSecondary)
                {
                    enhancedSecondary.baseMaxStock = secondary.baseMaxStock;
                    enhancedSecondary.baseRechargeInterval = Mathf.Max(.05f, C.Get(overdriveSalvoRecharge));
                    enhancedSecondary.rechargeStock = secondary.rechargeStock;
                    enhancedSecondary.requiredStock = enhancedSecondary.stockToConsume = 1;
                    enhancedSecondary.attackSpeedBuffsRestockSpeed = false;
                    enhancedSecondary.attackSpeedBuffsRestockSpeed_Multiplier = 1f;
                    enhancedSecondary.keywordTokens = new[] { "KEYWORD_RAPID" };
                }
                Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                    $"<style=cIsUtility>Rapid.</style> Fire all available orbs, dealing <style=cIsDamage>{C.Get(shotDamage):0.##}% damage</style> to enemies or <style=cIsHealing>healing allies for {C.Get(shotHeal):0.##}% damage</style>. <style=cIsHealing>Overhealing grants barrier.</style> <style=cIsUtility>Slow</style> enemies for <style=cIsUtility>{C.Get(shotSlow):0.##} seconds</style>; hits against Slowed enemies inflict <style=cIsDamage>Leech</style>.");
                if (enhancedSecondary)
                    Tweaks.Survivors.SurvivorSettings.SetSkillText(enhancedSecondary, () =>
                        $"<style=cIsUtility>Rapid.</style> Fire all available orbs for <style=cIsDamage>{C.Get(shotDamage):0.##}% damage</style> to enemies or <style=cIsHealing>{C.Get(shotHeal):0.##}% healing</style> to allies, bouncing up to <style=cIsUtility>{Mathf.Max(0, C.Get(shotBounces)) + Mathf.Max(0, C.Get(overdriveBounces))} additional times</style>. <style=cIsHealing>Overhealing grants barrier.</style> <style=cIsUtility>Slow</style> enemies for <style=cIsUtility>{C.Get(shotSlow):0.##} seconds</style> and inflict <style=cIsDamage>Leech</style> on Slowed targets. Allies gain <style=cIsUtility>{C.Get(overdriveAttackBuff):0.##}% attack speed</style>, <style=cIsUtility>{C.Get(overdriveSpeedBuff):0.##}% movement speed</style>, and <style=cIsHealing>{C.Get(overdriveDamageReduction):0.##}% damage reduction</style> for <style=cIsUtility>{C.Get(overdriveDotDuration):0.##} seconds</style>.");
                Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary,
                    () => $"\nTarget Range: {C.Get(shotRange):0.#}m\nOverdrive Bounces: +{C.Get(overdriveBounces)}", () => C.Get(shotProc));
                if (enhancedSecondary)
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(enhancedSecondary,
                        () => $"\nTarget Range: {C.Get(shotRange):0.##}m\nBounce Range: {C.Get(shotBounceRange):0.##}m\nMaximum Extra Bounces: {C.Get(shotBounces) + C.Get(overdriveBounces)}", () => C.Get(shotProc));
            }
            if (utility)
            {
                utility.baseRechargeInterval = Mathf.Max(.05f, C.Get(novaCooldown));
                if (enhancedUtility) enhancedUtility.baseRechargeInterval = utility.baseRechargeInterval;
                Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                    $"Charge a nova, <style=cIsHealing>healing allies for {C.Get(novaMin):0.##}–{C.Get(novaMax):0.##}% damage</style>, followed by <style=cIsHealing>2 additional pulses</style> at <style=cIsHealing>{C.Get(novaFollowup):0.##}% strength</style>. Push enemies away and grant allies <style=cIsUtility>{C.Get(novaMoveMin):0.##}–{C.Get(novaMoveMax):0.##}% movement speed</style> and <style=cIsHealing>{C.Get(novaArmourMin):0.##}–{C.Get(novaArmourMax):0.##} armour</style> for <style=cIsUtility>{C.Get(novaBuffDuration):0.##} seconds</style>. Subsequent pulses refresh the bonuses. At full charge, also grant <style=cIsHealing>{C.Get(novaRegeneration):0.##}% maximum health regeneration per second</style> for <style=cIsUtility>{C.Get(novaRegenerationDuration):0.##} seconds</style>.");
                if (enhancedUtility)
                    Tweaks.Survivors.SurvivorSettings.SetSkillText(enhancedUtility, () =>
                        $"Charge an empowered nova, <style=cIsHealing>healing allies</style> and <style=cIsDamage>damaging enemies</style> for <style=cIsDamage>{C.Get(novaMin):0.##}–{C.Get(novaMax):0.##}% damage</style>, followed by <style=cIsHealing>4 additional pulses</style> at <style=cIsHealing>{C.Get(novaFollowup):0.##}% strength</style>. Push enemies away and grant allies <style=cIsUtility>{C.Get(novaMoveMin):0.##}–{C.Get(novaMoveMax):0.##}% movement speed</style> and <style=cIsHealing>{C.Get(novaArmourMin):0.##}–{C.Get(novaArmourMax):0.##} armour</style> for <style=cIsUtility>{C.Get(novaBuffDuration):0.##} seconds</style>. Subsequent pulses refresh the bonuses. At full charge, also grant <style=cIsHealing>{C.Get(novaRegeneration):0.##}% maximum health regeneration per second</style> for <style=cIsUtility>{C.Get(novaRegenerationDuration):0.##} seconds</style>.");
                Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                    () => $"\nRadius: {C.Get(novaRadius):0.#}m\nCharge: {C.Get(novaCharge):0.#}s\nFull Charge Hold: {C.Get(novaHold):0.#}s", () => 0f);
                if (enhancedUtility)
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(enhancedUtility,
                        () => $"\nRadius: {C.Get(novaRadius):0.##}m\nCharge Time: {C.Get(novaCharge):0.##}s\nFull Charge Hold: {C.Get(novaHold):0.##}s", () => 0f);
            }
            if (special)
            {
                Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special,
                    () => $"\nDuration: {C.Get(overdriveDuration):0.##}s\nEnhanced Beam Multiplier: x{C.Get(overdriveBeamMultiplier):0.##}", () => 0f);
                special.baseRechargeInterval = Mathf.Max(.05f, C.Get(overdriveCooldown));
                special.stockToConsume = 1;
                special.requiredStock = 1;
                Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                    $"Enter <style=cIsUtility>Overdrive</style> for <style=cIsUtility>{C.Get(overdriveDuration):0.#} seconds</style>, empowering Healing Beams, Siphoning Salvo and Healing Nova.");
            }
        }

        internal static void Install(GameObject prefab)
        {
            if (!prefab || enabled == null || !enabled.Value) return;
            SkillLocator loc = prefab.GetComponent<SkillLocator>();
            // At this loadout stage GenericSkill.skillDef can still be null. The
            // actual native icon is held in the original SkillFamily variants.
            SkillDef vanillaPrimary = FirstVariant(loc ? loc.primary : null);
            Sprite fallback = vanillaPrimary ? vanillaPrimary.icon : null;
            if (!fallback)
            {
                GameObject donor = BodyCatalog.FindBodyPrefab("EmergencyDroneBody");
                fallback = FirstVariant(donor ? donor.GetComponent<SkillLocator>()?.primary : null)?.icon;
            }
            if (!fallback) fallback = RemoteDroneSkills.VanillaIcon("Drone2Body", "Primary", null);
            if (primary)
            {
                primary.icon = fallback;
                if (vanillaPrimary && C.Get(beamCooldown) <= 0f)
                    primary.baseRechargeInterval = vanillaPrimary.baseRechargeInterval;
                if (enhancedPrimary) enhancedPrimary.baseRechargeInterval = primary.baseRechargeInterval;
                RemoteDroneSkills.AddMachine(prefab, PrimaryMachine);
                RemoteDroneSkills.SetSkill(prefab, "Primary", primary);
            }
            if (secondary)
            {
                // Leeching Seed's green life-drain icon suits Siphoning Salvo.
                secondary.icon = RoR2Content.Items.Seed ? PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(RoR2Content.Items.Seed.itemIndex))?.iconSprite : null;
                if (!secondary.icon) secondary.icon = RemoteDroneSkills.VanillaIcon("TreebotBody", "Secondary", fallback);
                RemoteDroneSkills.AddMachine(prefab, RestorationMachine);
                RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary);
            }
            if ((primary || secondary) && !prefab.GetComponent<EmergencyTargetIndicator>())
                prefab.AddComponent<EmergencyTargetIndicator>();
            if (utility)
            {
                // Lepton Daisy's radial healing pulse is a clearer visual match than REX's root.
                utility.icon = RoR2Content.Items.NovaOnHeal
                    ? PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(RoR2Content.Items.NovaOnHeal.itemIndex))?.iconSprite
                    : null;
                if (!utility.icon) utility.icon = RemoteDroneSkills.VanillaIcon("SeekerBody", "Special", fallback);
                RemoteDroneSkills.AddMachine(prefab, NovaMachine);
                RemoteDroneSkills.SetSkill(prefab, "Utility", utility);
            }
            if (special)
            {
                // Gorag's Opus conveys an empowered, temporary support mode.
                special.icon = RoR2Content.Equipment.DroneBackup
                    ? PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex(RoR2Content.Equipment.DroneBackup.equipmentIndex))?.iconSprite
                    : null;
                if (!special.icon) special.icon = RemoteDroneSkills.VanillaIcon("CaptainBody", "Special", fallback);
                RemoteDroneSkills.AddMachine(prefab, OverdriveMachine);
                RemoteDroneSkills.SetSkill(prefab, "Special", special);
            }
            if (enhancedPrimary) enhancedPrimary.icon = TintedIcon(primary.icon, new Color(.15f, .95f, 1f));
            if (enhancedSecondary) enhancedSecondary.icon = TintedIcon(secondary.icon, new Color(.88f, .28f, 1f));
            if (enhancedUtility) enhancedUtility.icon = TintedIcon(utility.icon, new Color(1f, .38f, .12f));
            if (!prefab.GetComponent<EmergencyStatus>()) prefab.AddComponent<EmergencyStatus>();
            if (!prefab.GetComponent<OverdriveController>()) prefab.AddComponent<OverdriveController>();
            if (!prefab.GetComponent<OverdriveAura>()) prefab.AddComponent<OverdriveAura>();
            if (!prefab.GetComponent<RapidSalvoRecharge>()) prefab.AddComponent<RapidSalvoRecharge>();
            if (!harmonyInstalled)
            {
                harmonyInstalled = true;
                new Harmony("3Questionmarks.3QsVanillaTweaks.EmergencyDrone")
                    .Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)),
                        postfix: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(AfterStats)));
                new Harmony("3Questionmarks.3QsVanillaTweaks.EmergencyDrone.Target")
                    .Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                        postfix: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(RequirePrimaryTarget)));
                new Harmony("3Questionmarks.3QsVanillaTweaks.EmergencyDrone.TargetReady")
                    .Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)),
                        postfix: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(RequirePrimaryTarget)));
                new Harmony("3Questionmarks.3QsVanillaTweaks.EmergencyDrone.StockPayment")
                    .Patch(AccessTools.Method(typeof(SkillDef), nameof(SkillDef.OnExecute)),
                        prefix: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(BeforeVolleyPayment)),
                        finalizer: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(AfterVolleyPayment)));
                new Harmony("3Questionmarks.3QsVanillaTweaks.EmergencyDrone.Defence")
                    .Patch(AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.TakeDamage)),
                        prefix: new HarmonyMethod(typeof(RemoteEmergencyDrone), nameof(BeforeDamage)));
            }
            LoadEffects();
        }

        private static SkillDef FirstVariant(GenericSkill slot)
        {
            if (!slot) return null;
            if (slot.skillDef) return slot.skillDef;
            SkillFamily family = slot.skillFamily;
            if (family && family.variants != null)
                foreach (var variant in family.variants)
                    if (variant.skillDef && variant.skillDef.icon) return variant.skillDef;
            return null;
        }

        // Produce a gold-accented copy of the original icon once at setup time.
        // RenderTexture readback also works with non-readable vanilla textures.
        private static Sprite TintedIcon(Sprite sprite, Color accent)
        {
            if (!sprite || !sprite.texture) return sprite;
            RenderTexture previous = RenderTexture.active;
            RenderTexture temp = null;
            try
            {
                Rect area = sprite.textureRect;
                int w = Mathf.Max(1, Mathf.RoundToInt(area.width));
                int h = Mathf.Max(1, Mathf.RoundToInt(area.height));
                temp = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(sprite.texture, temp);
                RenderTexture.active = temp;
                Texture2D texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(Mathf.RoundToInt(area.x), Mathf.RoundToInt(area.y), w, h), 0, 0);
                texture.Apply();
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color col = pixels[i];
                    float brightness = Mathf.Max(col.r, Mathf.Max(col.g, col.b));
                    Color tinted = Color.Lerp(col, accent * brightness, .6f);
                    tinted.a = col.a;
                    pixels[i] = tinted;
                }
                texture.SetPixels(pixels);
                texture.Apply();
                Vector2 pivot = new Vector2(sprite.pivot.x / Mathf.Max(1f, sprite.rect.width),
                    sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));
                return Sprite.Create(texture, new Rect(0, 0, w, h), pivot, sprite.pixelsPerUnit);
            }
            catch (Exception ex)
            {
                ThreeQsVanillaTweaks.Log.Warning("Emergency Drone: unable to tint enhanced icon: " + ex.Message);
                return sprite;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temp) RenderTexture.ReleaseTemporary(temp);
            }
        }

        // The native restock flag was not scaling the remote drone's secondary in-game.
        // Apply the bonus progress explicitly once per physics tick, with the native
        // scaling disabled to avoid counting attack speed twice.
        private sealed class RapidSalvoRecharge : MonoBehaviour
        {
            private CharacterBody body;
            private SkillLocator locator;
            private void Awake()
            {
                body = GetComponent<CharacterBody>();
                locator = GetComponent<SkillLocator>();
            }
            private void FixedUpdate()
            {
                if (!body || !locator || !locator.secondary) return;
                GenericSkill slot = locator.secondary;
                SkillDef def = slot.skillDef;
                if (def != secondary && def != enhancedSecondary) return;
                if (slot.stock >= slot.maxStock) return;
                float speed = Mathf.Max(.01f, body.attackSpeed);
                // Native recharge already advances by one second per second.
                // Apply half of the attack-speed bonus (or penalty) to stock recovery.
                // The native recharge already supplies the base 1x progress.
                slot.rechargeStopwatch += Time.fixedDeltaTime * (speed - 1f) * 0.5f;
            }
        }

        private sealed class VolleyPayment : MonoBehaviour
        {
            internal int Count;
            internal int TakeCount()
            {
                int count = Count;
                Count = 0;
                return count;
            }
        }

        private static void BeforeVolleyPayment(SkillDef __instance, GenericSkill skillSlot, out int? __state)
        {
            __state = null;
            if ((__instance != secondary && __instance != enhancedSecondary) || !skillSlot) return;
            int available = Mathf.Max(1, skillSlot.stock);
            (skillSlot.GetComponent<VolleyPayment>() ?? skillSlot.gameObject.AddComponent<VolleyPayment>()).Count = available;
            __state = __instance.stockToConsume;
            __instance.stockToConsume = available;
        }

        private static Exception AfterVolleyPayment(SkillDef __instance, int? __state, Exception __exception)
        {
            if (__state.HasValue) __instance.stockToConsume = __state.Value;
            return __exception;
        }

        private static void LoadEffects()
        {
            if (orbEffect) return;
            var native = EntityStateCatalog.InstantiateState(typeof(EntityStates.Drone.Command.CommandHealShot));
            orbEffect = RemoteDroneSkills.NativeGameObject(native, "orbEffectPrefab") ??
                LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OrbEffects/HealthOrbEffect");
        }

        private static void AfterStats(CharacterBody __instance)
        {
            if (!__instance) return;
            if (overdriveAllyBuff && __instance.HasBuff(overdriveAllyBuff))
            {
                __instance.attackSpeed *= 1f + Mathf.Max(0f, C.Get(overdriveAttackBuff)) / 100f;
                __instance.moveSpeed *= 1f + Mathf.Max(0f, C.Get(overdriveSpeedBuff)) / 100f;
            }
            EmergencyStatus status = __instance.GetComponent<EmergencyStatus>();
            if (status && status.HasNovaBuff)
            {
                __instance.moveSpeed *= 1f + status.MoveBonus / 100f;
                __instance.armor += status.ArmourBonus;
            }
        }

        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (damageInfo != null && overdriveAllyBuff && __instance && __instance.body &&
                __instance.body.HasBuff(overdriveAllyBuff))
                damageInfo.damage *= 1f - Mathf.Clamp01(C.Get(overdriveDamageReduction) / 100f);
        }

        private static HurtBox Target(Vector3 origin, Vector3 direction, float radius, float cone,
            GameObject owner, HashSet<HealthComponent> exclude = null)
        {
            var search = new BullseyeSearch
            {
                searchOrigin = origin, searchDirection = direction,
                maxDistanceFilter = Mathf.Max(0f, radius), maxAngleFilter = cone,
                teamMaskFilter = TeamMask.allButNeutral, filterByLoS = exclude == null,
                filterByDistinctEntity = true,
                sortMode = exclude == null ? BullseyeSearch.SortMode.Angle : BullseyeSearch.SortMode.Distance
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(owner);
            return search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive &&
                (exclude == null || !exclude.Contains(h.healthComponent)));
        }

        private static HurtBox FindAllyTarget(Ray aim, CharacterBody owner, float angle)
        {
            if (!owner || !owner.teamComponent) return null;
            var mask = TeamMask.none;
            mask.AddTeam(owner.teamComponent.teamIndex);
            var search = new BullseyeSearch
            {
                searchOrigin = aim.origin, searchDirection = aim.direction,
                maxDistanceFilter = Mathf.Max(0f, C.Get(beamRange)),
                maxAngleFilter = Mathf.Clamp(angle, 0f, 180f),
                teamMaskFilter = mask, sortMode = BullseyeSearch.SortMode.Angle,
                filterByLoS = false, filterByDistinctEntity = true
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(owner.gameObject);
            return search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive);
        }

        private static void RequirePrimaryTarget(GenericSkill __instance, ref bool __result)
        {
            if (!__result || !__instance || (__instance.skillDef != primary &&
                __instance.skillDef != enhancedPrimary && __instance.skillDef != secondary &&
                __instance.skillDef != enhancedSecondary)) return;
            CharacterBody owner = __instance.GetComponent<CharacterBody>();
            InputBankTest input = __instance.GetComponent<InputBankTest>();
            if (!owner || !input) { __result = false; return; }
            Ray aim = input.GetAimRay();
            __result = (__instance.skillDef == primary || __instance.skillDef == enhancedPrimary)
                ? FindAllyTarget(aim, owner, C.Get(beamAngle)) != null
                : Target(aim.origin, aim.direction, C.Get(shotRange),
                    Mathf.Clamp(C.Get(shotAngle), 0f, 180f), owner.gameObject) != null;
        }

        private sealed class EmergencyTargetIndicator : MonoBehaviour
        {
            private Indicator primaryIndicator;
            private Indicator secondaryIndicator;
            private CharacterBody owner;
            private InputBankTest input;
            private void Awake()
            {
                owner = GetComponent<CharacterBody>(); input = GetComponent<InputBankTest>();
                GameObject prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator");
                primaryIndicator = new Indicator(gameObject, prefab);
                secondaryIndicator = new Indicator(gameObject, prefab);
            }
            private void FixedUpdate()
            {
                bool active = owner && owner.hasEffectiveAuthority && input;
                Ray ray = active ? input.GetAimRay() : default(Ray);
                HurtBox primaryTarget = active ? FindAllyTarget(ray, owner, C.Get(beamAngle)) : null;
                HurtBox secondaryTarget = active ? Target(ray.origin, ray.direction, C.Get(shotRange),
                    Mathf.Clamp(C.Get(shotAngle), 0f, 180f), gameObject) : null;
                // Show the secondary lock only while the secondary button is held;
                // otherwise show the ally lock for native-style healing beams.
                bool showSecondary = active && (input.skill2.down || (!primaryTarget && secondaryTarget));
                if (primaryIndicator != null)
                {
                    primaryIndicator.active = active && !showSecondary && primaryTarget;
                    primaryIndicator.targetTransform = primaryTarget ? primaryTarget.transform : null;
                }
                if (secondaryIndicator != null)
                {
                    secondaryIndicator.active = active && showSecondary && secondaryTarget;
                    secondaryIndicator.targetTransform = secondaryTarget ? secondaryTarget.transform : null;
                }
            }
            private void OnDisable()
            {
                if (primaryIndicator != null) primaryIndicator.active = false;
                if (secondaryIndicator != null) secondaryIndicator.active = false;
            }
        }

        public class EmergencyHealBeams : BaseState
        {
            protected virtual bool Enhanced => false;
            public override void OnEnter()
            {
                base.OnEnter();
                if (!NetworkServer.active || !characterBody) return;
                HurtBox first = FindAllyTarget(GetAimRay(), characterBody, C.Get(beamAngle));
                if (!first) return;
                GameObject prefab = EntityStates.Drone.DroneWeapon.HealBeam.healBeamPrefab;
                if (!prefab)
                {
                    var native = EntityStateCatalog.InstantiateState(typeof(EntityStates.Drone.DroneWeapon.StartHealBeam))
                        as EntityStates.Drone.DroneWeapon.StartHealBeam;
                    prefab = native?.healBeamPrefab;
                }
                if (!prefab) return;
                var targets = new List<HurtBox> { first };
                var already = new HashSet<HealthComponent> { first.healthComponent };
                var mask = TeamMask.none;
                mask.AddTeam(characterBody.teamComponent.teamIndex);
                var search = new BullseyeSearch
                {
                    searchOrigin = characterBody.corePosition, searchDirection = Vector3.forward,
                    maxDistanceFilter = Mathf.Max(0f, C.Get(beamRange)), maxAngleFilter = 180f,
                    teamMaskFilter = mask, sortMode = BullseyeSearch.SortMode.Distance,
                    filterByLoS = false, filterByDistinctEntity = true
                };
                search.RefreshCandidates(); search.FilterOutGameObject(gameObject);
                foreach (HurtBox box in search.GetResults())
                {
                    if (targets.Count >= Mathf.Max(1, C.Get(beamTargets))) break;
                    if (box && box.healthComponent && box.healthComponent.alive && already.Add(box.healthComponent)) targets.Add(box);
                }
                foreach (HurtBox target in targets)
                {
                    Transform muzzle = FindModelChild("Muzzle") ?? transform;
                    GameObject objectInstance = UnityEngine.Object.Instantiate(prefab, muzzle);
                    HealBeamController beam = objectInstance.GetComponent<HealBeamController>();
                    if (!beam) { UnityEngine.Object.Destroy(objectInstance); continue; }
                    // Existing beams follow the current Overdrive buff, not the state
                    // in which they were initially fired. This also updates normal
                    // beams when Overdrive begins without requiring a new cast.
                    var effects = objectInstance.AddComponent<EmergencyBeamEffects>();
                    effects.Source = characterBody;
                    effects.Target = target.healthComponent;
                    effects.Beam = beam;
                    effects.BaseHealRate = beam.healRate;
                    effects.RefreshHealingRate();
                    beam.breakOnTargetFullyHealed = false;
                    beam.target = target;
                    if (beam.ownership != null) beam.ownership.ownerObject = gameObject;
                    var monitor = objectInstance.AddComponent<EmergencyBeamMonitor>();
                    monitor.Owner = transform;
                    monitor.Target = target;
                    monitor.MaximumRange = Mathf.Max(0f, C.Get(beamRange));
                    NetworkServer.Spawn(objectInstance);
                }
                // One cue per successful activation, not once per attached beam.
                Util.PlaySound(BeamAttachSound, gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        // Keep native beam effects alive until their target is genuinely detached.
        // The native controller handles its own other break conditions.
        private sealed class EmergencyBeamMonitor : MonoBehaviour
        {
            internal Transform Owner;
            internal HurtBox Target;
            internal float MaximumRange;

            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (!Owner || !Target || !Target.healthComponent || !Target.healthComponent.alive ||
                    (Owner.position - Target.transform.position).sqrMagnitude > MaximumRange * MaximumRange)
                {
                    var beam = GetComponent<HealBeamController>();
                    if (beam) beam.BreakServer();
                    else Destroy(gameObject);
                }
            }
        }

        // Healing strength and overheal barrier are determined by the drone's
        // CURRENT buff, rather than by which version of the skill spawned the beam.
        private sealed class EmergencyBeamEffects : MonoBehaviour
        {
            internal CharacterBody Source;
            internal HealthComponent Target;
            internal HealBeamController Beam;
            internal float BaseHealRate;
            private float lastMultiplier = -1f;

            internal void RefreshHealingRate()
            {
                if (!Source || !Beam) return;
                bool overdrive = overdriveBuff && Source.HasBuff(overdriveBuff);
                float multiplier = Mathf.Max(0f, C.Get(beamRateMultiplier)) *
                    (overdrive ? Mathf.Max(0f, C.Get(overdriveBeamMultiplier)) : 1f);
                if (!Mathf.Approximately(multiplier, lastMultiplier))
                {
                    Beam.healRate = BaseHealRate * multiplier;
                    lastMultiplier = multiplier;
                }
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || !Source || !Target || !Target.alive) return;
                RefreshHealingRate();
                if (!overdriveBuff || !Source.HasBuff(overdriveBuff)) return;
                if (Target.health < Target.fullHealth - .01f) return;

                // Match the existing full-health barrier behaviour, but only while
                // Overdrive is actually active. On expiry it stops immediately.
                float healingPerSecond = Source.damage * .5f *
                    Mathf.Max(0f, C.Get(beamRateMultiplier)) *
                    Mathf.Max(0f, C.Get(overdriveBeamMultiplier));
                if (healingPerSecond > 0f)
                    Target.AddBarrier(healingPerSecond * Time.fixedDeltaTime);
            }
        }

        public sealed class EnhancedHealBeams : EmergencyHealBeams
        {
            protected override bool Enhanced => true;
        }

        private static void RestoreOrDamage(CharacterBody source, HurtBox hit, float damage, bool crit, bool enhanced)
        {
            if (!source || !hit || !hit.healthComponent) return;
            HealthComponent health = hit.healthComponent;
            CharacterBody recipient = health.body;
            if (!recipient || !recipient.teamComponent || !source.teamComponent) return;
            bool ally = recipient.teamComponent.teamIndex == source.teamComponent.teamIndex;
            if (ally)
            {
                float amount = source.damage * Mathf.Max(0f, C.Get(shotHeal)) / 100f;
                float missing = Mathf.Max(0f, health.fullHealth - health.health);
                health.Heal(amount, default(ProcChainMask));
                if (amount > missing) health.AddBarrier(amount - missing);
                Util.PlaySound(SalvoAllyImpactSound, recipient.gameObject);
                if (enhanced)
                    recipient.AddTimedBuff(overdriveAllyBuff, Mathf.Max(0f, C.Get(overdriveDotDuration)));
            }
            else
            {
                DamageInfo damageInfo = new DamageInfo
                {
                    attacker = source.gameObject, inflictor = source.gameObject, damage = damage,
                    crit = crit, procCoefficient = Mathf.Max(0f, C.Get(shotProc)),
                    position = hit.transform.position
                };
                health.TakeDamage(damageInfo);
                GlobalEventManager.instance.OnHitEnemy(damageInfo, health.gameObject);
                Util.PlaySound(SalvoEnemyImpactSound, recipient.gameObject);
                // Check Slow BEFORE applying this orb's Slow. First orb primes the target;
                // subsequent orbs in the same volley can apply independent Leech stacks.
                bool wasSlowed = recipient.HasBuff(RoR2Content.Buffs.Slow50);
                if (wasSlowed)
                    RemoteLeech.Apply(recipient, source, C.Get(shotDotDuration),
                        C.Get(shotDotInterval), C.Get(shotDotDamage), C.Get(shotLeech));
                if (C.Get(shotSlow) > 0f) recipient.AddTimedBuff(RoR2Content.Buffs.Slow50, C.Get(shotSlow));
            }
        }

        public class PiercingRestoration : BaseState
        {
            protected virtual bool Enhanced => false;
            public override void OnEnter()
            {
                base.OnEnter();
                if (!NetworkServer.active) return;
                Ray ray = GetAimRay();
                HurtBox target = Target(ray.origin, ray.direction, C.Get(shotRange),
                    Mathf.Clamp(C.Get(shotAngle), 0f, 180f), gameObject);
                if (!target) return;

                // The SkillDef.OnExecute hook snapshots and consumes exactly the available
                // stocks, including when there is only one orb left in the magazine.
                int orbCount = (GetComponent<VolleyPayment>()?.TakeCount() ?? 0);
                if (orbCount < 1) orbCount = Mathf.Max(1, characterBody.skillLocator.secondary.stock + 1);
                int extra = Mathf.Max(0, C.Get(shotBounces)) +
                    (Enhanced ? Mathf.Max(0, C.Get(overdriveBounces)) : 0);
                float damage = damageStat * Mathf.Max(0f, C.Get(shotDamage)) / 100f;
                Vector3 right = transform.right;
                for (int i = 0; i < orbCount; i++)
                {
                    float offset = (i - (orbCount - 1) * .5f) * .65f;
                    OrbManager.instance.AddOrb(new RestorationOrb
                    {
                        origin = ray.origin + right * offset,
                        target = target,
                        source = characterBody,
                        damage = damage,
                        enhanced = Enhanced,
                        crit = Util.CheckRoll(critStat, characterBody.master),
                        remaining = extra,
                        visited = new HashSet<HealthComponent>()
                    });
                }
                RemoteDroneSkills.PlayPhaseMuzzle(this);
                Util.PlaySound(SalvoLaunchSound, gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && fixedAge >= .1f) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        public sealed class EnhancedSalvo : PiercingRestoration
        {
            protected override bool Enhanced => true;
        }

        private sealed class RestorationOrb : Orb
        {
            internal CharacterBody source;
            internal float damage;
            internal bool enhanced;
            internal bool crit;
            internal int remaining;
            internal HashSet<HealthComponent> visited;
            public override void Begin()
            {
                if (!target) return;
                duration = Mathf.Clamp(Vector3.Distance(origin, target.transform.position) / 65f, .08f, .65f);
                if (orbEffect)
                {
                    EffectData effect = new EffectData { origin = origin, genericFloat = duration, scale = 1f };
                    effect.SetHurtBoxReference(target);
                    EffectManager.SpawnEffect(orbEffect, effect, true);
                }
            }
            public override void OnArrival()
            {
                if (!source || !target || !target.healthComponent || !target.healthComponent.alive) return;
                HealthComponent health = target.healthComponent;
                if (!visited.Add(health)) return;
                RestoreOrDamage(source, target, damage, crit, enhanced);
                if (remaining <= 0) return;
                HurtBox next = Target(target.transform.position, Vector3.forward, Mathf.Max(0f, C.Get(shotBounceRange)),
                    180f, source.gameObject, visited);
                if (next) OrbManager.instance.AddOrb(new RestorationOrb
                {
                    origin = target.transform.position, target = next, source = source,
                    damage = damage, enhanced = enhanced, crit = crit, remaining = remaining - 1, visited = visited
                });
            }
        }

        public class HealingNova : BaseState
        {
            protected virtual bool Enhanced => false;
            private float nextChargePulse;
            private bool reachedFullCharge;
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority) Util.PlaySound(NovaChargeSound, gameObject);
                RemoteDroneSkills.ChargeDisplay(this, 0f);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                float speed = Mathf.Max(.01f, attackSpeedStat);
                float minimumTime = Mathf.Max(.01f, C.Get(novaMinimumCharge));
                float chargeTime = Mathf.Max(minimumTime, C.Get(novaCharge) / speed);
                float fraction = Mathf.Clamp01((fixedAge - minimumTime) / Mathf.Max(.001f, chargeTime - minimumTime));
                RemoteDroneSkills.ChargeDisplay(this, fraction);
                RemoteDroneSkills.PlayPhaseChargePulse(this, ref nextChargePulse);
                if (isAuthority && !reachedFullCharge && fraction >= 1f)
                {
                    reachedFullCharge = true;
                    Util.PlaySound(NovaChargedSound, gameObject);
                }
                if (!isAuthority || fixedAge < minimumTime ||
                    (inputBank.skill3.down && fixedAge < chargeTime + Mathf.Max(0f, C.Get(novaHold)))) return;
                // Local-only geometric cue. Does not rely on invalid EffectCatalog assets.
                NovaPulseVisuals.Begin(characterBody, C.Get(novaRadius), Enhanced);
                if (NetworkServer.active)
                    PulseController.Begin(characterBody, fraction, damageStat, Enhanced);
                // The server-owned pulse controller plays the release and follow-up cues.
                outer.SetNextStateToMain();
            }
            public override void OnExit()
            {
                if (isAuthority) Util.PlaySound(NovaStopChargeSound, gameObject);
                RemoteDroneSkills.HideCharge(this);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class EnhancedNova : HealingNova
        {
            protected override bool Enhanced => true;
        }

        public sealed class Overdrive : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                {
                    characterBody.AddTimedBuff(overdriveBuff, Mathf.Max(.01f, C.Get(overdriveDuration)));
                }
                // Disable another activation immediately; cooldown will start once the buff ends.
                var specialSlot = characterBody ? characterBody.skillLocator?.special : null;
                if (specialSlot)
                {
                    GetComponent<OverdriveController>()?.CaptureSpecialStock(specialSlot);
                    specialSlot.stock = 0;
                    specialSlot.rechargeStopwatch = 0f;
                }
                Util.PlaySound(OverdriveStartSound, gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate(); if (isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        // Checks a single replicated buff on its own body. Skill definitions are swapped
        // only on buff transitions; no scene scans or per-beam changes are performed.
        private sealed class OverdriveController : MonoBehaviour
        {
            private CharacterBody body;
            private SkillLocator skills;
            private bool applied;
            private int remainingSpecialStock;
            internal void CaptureSpecialStock(GenericSkill slot)
            {
                if (slot) remainingSpecialStock = Mathf.Max(0, slot.stock);
            }
            private void Awake()
            {
                body = GetComponent<CharacterBody>();
                skills = GetComponent<SkillLocator>();
            }

            private void FixedUpdate()
            {
                if (!body || !skills || !overdriveBuff) return;
                bool shouldApply = body.HasBuff(overdriveBuff);
                // Vanilla cooldown reduction can restore stock before Overdrive ends.
                // Keep the Special unavailable for the entire buff, and restart its
                // normal cooldown only when Overdrive expires.
                if (shouldApply && skills.special)
                {
                    skills.special.stock = 0;
                    skills.special.rechargeStopwatch = 0f;
                }
                if (shouldApply == applied) return;
                if (!shouldApply && applied && skills.special)
                {
                    // A second Lysate Cell charge should survive the temporary Overdrive lockout.
                    skills.special.stock = Mathf.Clamp(remainingSpecialStock, 0, skills.special.maxStock);
                    skills.special.rechargeStopwatch = 0f;
                }
                if (applied && !shouldApply && body.hasEffectiveAuthority)
                    Util.PlaySound(OverdriveEndSound, gameObject);
                SetEnhanced(shouldApply);
            }

            private void Swap(GenericSkill slot, SkillDef def, bool enabled)
            {
                if (!slot || !def) return;
                int oldStock = slot.stock;
                float oldRecharge = slot.rechargeStopwatch;
                if (enabled)
                    slot.SetSkillOverride(gameObject, def, GenericSkill.SkillOverridePriority.Contextual);
                else
                    slot.UnsetSkillOverride(gameObject, def, GenericSkill.SkillOverridePriority.Contextual);
                // The vanilla override path may restock a slot when assigned.
                slot.stock = Mathf.Clamp(oldStock, 0, slot.maxStock);
                slot.rechargeStopwatch = oldRecharge;
            }

            private void SetEnhanced(bool enabled)
            {
                Swap(skills.primary, enhancedPrimary, enabled);
                Swap(skills.secondary, enhancedSecondary, enabled);
                Swap(skills.utility, enhancedUtility, enabled);
                applied = enabled;
            }

            private void OnDisable()
            {
                if (applied) SetEnhanced(false);
            }
        }

        private sealed class EmergencyStatus : MonoBehaviour
        {
            internal float MoveBonus, ArmourBonus, RegenerationRemaining;
            internal float BuffTime;
            internal bool HasNovaBuff => BuffTime > 0f;
            private float regenerationAccumulator;
            internal void GiveNova(float charge)
            {
                BuffTime = Mathf.Max(.01f, C.Get(novaBuffDuration));
                MoveBonus = Mathf.Lerp(C.Get(novaMoveMin), C.Get(novaMoveMax), charge);
                ArmourBonus = Mathf.Lerp(C.Get(novaArmourMin), C.Get(novaArmourMax), charge);
                if (charge >= 1f) RegenerationRemaining = Mathf.Max(0f, C.Get(novaRegenerationDuration));
                GetComponent<CharacterBody>()?.RecalculateStats();
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                float dt = Time.fixedDeltaTime;
                if (BuffTime > 0f)
                {
                    BuffTime -= dt;
                    if (BuffTime <= 0f) GetComponent<CharacterBody>()?.RecalculateStats();
                }
                if (RegenerationRemaining > 0f)
                {
                    RegenerationRemaining -= dt;
                    regenerationAccumulator += dt;
                    while (regenerationAccumulator >= .2f)
                    {
                        regenerationAccumulator -= .2f;
                        var h = GetComponent<HealthComponent>();
                        if (h && h.alive) h.Heal(h.fullHealth * Mathf.Max(0f, C.Get(novaRegeneration)) / 100f * .2f, default(ProcChainMask));
                    }
                }
                else regenerationAccumulator = 0f;
            }
        }

        // Lightweight local visuals: one line renderer per ring, no particle spam,
        // scene-wide queries, networking allocations or effect-catalog lookups.
        private sealed class VisualRing : MonoBehaviour
        {
            private const int Segments = 64;
            private LineRenderer line;
            private Material ringMaterial;
            internal float Radius = 2f;
            internal Color Tint = Color.cyan;
            internal float Width = .13f;
            internal void Setup(Color tint, float width)
            {
                Tint = tint; Width = width;
                line = gameObject.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.loop = true;
                line.positionCount = Segments + 1;
                line.startWidth = line.endWidth = width;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                // The default LineRenderer material renders magenta in this RoR2 build.
                // Borrow an existing vanilla VFX material instead of looking up a
                // built-in shader that does not exist in the game's shader catalogue.
                try
                {
                    GameObject effect = LegacyResourcesAPI.Load<GameObject>(
                        "Prefabs/Effects/OrbEffects/HealthOrbEffect");
                    var renderer = effect ? effect.GetComponentInChildren<ParticleSystemRenderer>(true) : null;
                    Material existing = renderer ? renderer.sharedMaterial : null;
                    if (existing)
                    {
                        ringMaterial = new Material(existing);
                        line.sharedMaterial = ringMaterial;
                    }
                    else line.enabled = false; // never display a bright magenta placeholder
                }
                catch (Exception) { line.enabled = false; }
                Redraw(1f);
            }
            internal void Redraw(float opacity)
            {
                if (!line) return;
                Color color = Tint; color.a *= Mathf.Clamp01(opacity);
                line.startColor = line.endColor = color;
                for (int i = 0; i <= Segments; ++i)
                {
                    float theta = (float)i / Segments * Mathf.PI * 2f;
                    line.SetPosition(i, new Vector3(Mathf.Cos(theta) * Radius, 0f, Mathf.Sin(theta) * Radius));
                }
            }
            private void OnDestroy()
            {
                if (ringMaterial) Destroy(ringMaterial);
            }
        }

        private sealed class OverdriveAura : MonoBehaviour
        {
            private CharacterBody body;
            private VisualRing ring;
            private float clock;
            private void Awake() { body = GetComponent<CharacterBody>(); }
            private void LateUpdate()
            {
                bool active = body && overdriveBuff && body.HasBuff(overdriveBuff);
                if (!active)
                {
                    if (ring) ring.gameObject.SetActive(false);
                    return;
                }
                if (!ring)
                {
                    GameObject obj = new GameObject("3QS Emergency Overdrive Aura");
                    obj.transform.SetParent(transform, false);
                    ring = obj.AddComponent<VisualRing>();
                    ring.Setup(new Color(1f, .8f, .2f, .9f), .18f);
                }
                if (!ring.gameObject.activeSelf) ring.gameObject.SetActive(true);
                clock += Time.deltaTime;
                ring.transform.localPosition = new Vector3(0f, 1.3f + .12f * Mathf.Sin(clock * 3f), 0f);
                ring.transform.localRotation = Quaternion.Euler(18f, clock * 48f, 14f);
                ring.Radius = 1.9f + .12f * Mathf.Sin(clock * 4f);
                ring.Redraw(.8f + .2f * Mathf.Sin(clock * 3f));
            }
        }

        private sealed class NovaPulseVisuals : MonoBehaviour
        {
            private CharacterBody source;
            private float radius, age;
            private bool enhanced;
            private int nextPulse;
            private static readonly float[] normal = { 0f, 2f, 4f };
            private static readonly float[] empowered = { 0f, 1f, 2f, 3f, 4f };
            internal static void Begin(CharacterBody source, float radius, bool enhanced)
            {
                if (!source) return;
                var visual = source.gameObject.AddComponent<NovaPulseVisuals>();
                visual.source = source;
                visual.radius = Mathf.Max(1f, radius);
                visual.enhanced = enhanced;
                visual.Emit();
                visual.nextPulse = 1;
            }
            private void Update()
            {
                if (!source) { Destroy(this); return; }
                age += Time.deltaTime;
                var times = enhanced ? empowered : normal;
                while (nextPulse < times.Length && age >= times[nextPulse])
                {
                    Emit(); ++nextPulse;
                }
                if (nextPulse >= times.Length) Destroy(this);
            }
            private void Emit()
            {
                var obj = new GameObject("3QS Healing Nova Pulse");
                obj.transform.position = source.corePosition;
                var ring = obj.AddComponent<VisualRing>();
                ring.Setup(enhanced ? new Color(.5f, 1f, .75f, .95f) : new Color(.25f, 1f, .5f, .95f), .22f);
                var wave = obj.AddComponent<ExpandingNovaRing>();
                wave.Ring = ring;
                wave.MaxRadius = radius;
            }
        }

        private sealed class ExpandingNovaRing : MonoBehaviour
        {
            internal VisualRing Ring;
            internal float MaxRadius;
            private float time;
            private void Update()
            {
                time += Time.deltaTime;
                float t = Mathf.Clamp01(time / .65f);
                if (Ring)
                {
                    Ring.Radius = Mathf.Lerp(1f, MaxRadius, t);
                    Ring.Redraw(1f - t);
                }
                if (t >= 1f) Destroy(gameObject);
            }
        }

        private sealed class PulseController : MonoBehaviour
        {
            private CharacterBody source;
            private float healing, charge, age;
            private bool overdrive;
            private int nextPulse;
            private static readonly float[] normalPulseTimes = { 0f, 2f, 4f };
            private static readonly float[] overdrivePulseTimes = { 0f, 1f, 2f, 3f, 4f };
            internal static void Begin(CharacterBody source, float fraction, float damage, bool enhanced)
            {
                if (!source || !NetworkServer.active) return;
                var controller = source.gameObject.AddComponent<PulseController>();
                controller.source = source;
                controller.charge = fraction;
                controller.healing = damage * Mathf.Lerp(C.Get(novaMin), C.Get(novaMax), fraction) / 100f;
                controller.overdrive = enhanced;
                controller.SendPulse();
                controller.nextPulse = 1;
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !source) { Destroy(this); return; }
                age += Time.fixedDeltaTime;
                var times = overdrive ? overdrivePulseTimes : normalPulseTimes;
                while (nextPulse < times.Length && age >= times[nextPulse])
                {
                    SendPulse(); ++nextPulse;
                }
                if (nextPulse >= times.Length) Destroy(this);
            }
            private void SendPulse()
            {
                if (!source) return;
                bool first = nextPulse == 0;
                float amount = healing * (first ? 1f : Mathf.Max(0f, C.Get(novaFollowup)) / 100f);
                Vector3 centre = source.corePosition;
                float radius = Mathf.Max(0f, C.Get(novaRadius));
                var seen = new HashSet<HealthComponent>();
                Collider[] contacts = Physics.OverlapSphere(centre, radius, LayerIndex.entityPrecise.mask);
                foreach (Collider collider in contacts)
                {
                    HurtBox hurtBox = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                    HealthComponent health = hurtBox ? hurtBox.healthComponent : null;
                    if (!health || !health.alive || !seen.Add(health)) continue;
                    CharacterBody body = health.body;
                    if (!body || !body.teamComponent || !source.teamComponent) continue;
                    if (body.teamComponent.teamIndex == source.teamComponent.teamIndex)
                    {
                        health.Heal(amount, default(ProcChainMask));
                        (body.GetComponent<EmergencyStatus>() ?? body.gameObject.AddComponent<EmergencyStatus>()).GiveNova(charge);
                        body.AddTimedBuff(novaBuff, Mathf.Max(.01f, C.Get(novaBuffDuration)));
                        if (charge >= 1f) body.AddTimedBuff(novaRegenBuff, Mathf.Max(.01f, C.Get(novaRegenerationDuration)));
                    }
                    else if (first)
                    {
                        // Apply real impulse instead of changing CharacterMotor.velocity directly:
                        // the movement update can immediately overwrite manual velocity edits.
                        Vector3 away = body.corePosition - centre;
                        away.y = 0f;
                        if (away.sqrMagnitude < .0001f) away = source.transform.forward;
                        away.Normalize();
                        Vector3 impulse = (away + Vector3.up * .25f).normalized * Mathf.Max(0f, C.Get(novaKnockback));
                        var force = new PhysForceInfo
                        {
                            force = impulse,
                            massIsOne = true,
                            ignoreGroundStick = true,
                            resetVelocity = true
                        };
                        health.TakeDamageForce(force);
                    }
                    if (overdrive && body.teamComponent.teamIndex != source.teamComponent.teamIndex)
                    {
                        DamageInfo info = new DamageInfo { attacker = source.gameObject, inflictor = source.gameObject,
                            damage = amount, position = body.corePosition, procCoefficient = .2f };
                        health.TakeDamage(info); GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
                    }
                }
                // Include the caster even if the physics query does not return its hurtbox.
                if (source.healthComponent && !seen.Contains(source.healthComponent))
                {
                    source.healthComponent.Heal(amount, default(ProcChainMask));
                    (source.GetComponent<EmergencyStatus>() ?? source.gameObject.AddComponent<EmergencyStatus>()).GiveNova(charge);
                    source.AddTimedBuff(novaBuff, Mathf.Max(.01f, C.Get(novaBuffDuration)));
                    if (charge >= 1f)
                        source.AddTimedBuff(novaRegenBuff, Mathf.Max(.01f, C.Get(novaRegenerationDuration)));
                }
                // Deliberately do not pass TeleporterHealNovaPulse to EffectManager:
                // it is a controller, not a registered impact effect.
                Util.PlaySound(first ? NovaReleaseSound : NovaFollowupSound, source.gameObject);
            }
        }

    }
}
