using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    // Only the separately registered MissileDroneBodyRemoteOp receives these skills.
    internal static class RemoteMissileDrone
    {
        private const string Root = "Remote Operation - Missile Drone";
        private const string PrimaryMachine = "3QSMissilePrimary";
        private const string SecondaryMachine = "3QSMissileSecondary";
        private const string UtilityMachine = "3QSMissileUtility";
        private const string SpecialMachine = "3QSMissileSpecial";
        internal static readonly Type[] States = { typeof(GuidedMissile), typeof(MissilePainting), typeof(StickyBombCast), typeof(StickyBombDetonate), typeof(MissileBarrage) };

        private static ConfigEntry<bool> enabled, primaryEnabled, secondaryEnabled, utilityEnabled, specialEnabled;
        private static ConfigEntry<float> primaryDamage, primaryRadius, primaryHitboxScale, primaryProc, primaryInterval, primaryRange, primaryTurnSpeed;
        private static ConfigEntry<float> secondaryDamage, secondaryRadius, secondaryProc, secondaryCooldown, secondaryReuseDelay, paintRange, paintAngle, paintInterval, launchInterval;
        private static ConfigEntry<int> missilesPerMark, maximumMarks;
        private static ConfigEntry<float> bombDamage, bombRadius, bombProc, bombCooldown, bombLaunchSpeed, bombGravityScale, bombVisualScale, bombEnemyForce, bombSelfImpulse, bombLifetime, bombDetonationDelay, bombVfxScaleMultiplier;
        private static ConfigEntry<float> specialDamage, specialRadius, specialProc, specialCooldown, barrageRange, barrageAreaRadius, barrageInterval, barrageFlightHeight;
        private static ConfigEntry<int> barrageCount;
        private static SkillDef primary, secondary, utility, special, barrageConfirm, bombDetonate;
        private static GameObject nativeProjectile, nativeMuzzleFlash, painterProjectile;
        // Tag only effects spawned by this module. The vanilla registered prefab stays untouched.
        private static readonly Color missileBlastVisualTag = new Color(0.137f, 0.271f, 0.619f, 0.913f);
        private static bool patched;
        private static bool warnedMissingProjectile;
        private static bool warnedMissingExplosion;
        private static DamageSource? pendingLaunchSource;
        // The ProjectileManager initialization hook is invoked synchronously by FireProjectile.
        // The pending impact is scoped to one special missile spawn (including I.C.B.M. copies).
        private static readonly Dictionary<GameObject, StickyBombFlight> activeStickyBombs = new Dictionary<GameObject, StickyBombFlight>();
        private static readonly HashSet<GameObject> armedStickyBombOwners = new HashSet<GameObject>();
        private static readonly HashSet<GameObject> bombInputReleased = new HashSet<GameObject>();
        private static readonly Dictionary<GameObject, float> stickyBombArmedTimes = new Dictionary<GameObject, float>();
        private static readonly HashSet<GameObject> pendingStickyBombDetonations = new HashSet<GameObject>();
        private static bool pendingBarrageImpact;
        private static Vector3 pendingBarragePoint;
        private static BarrageMarkerLifetime pendingBarrageMarker;
        // Only this clone is altered; Engineer keeps the original crosshair prefab.
        private static GameObject missilePainterCrosshairPrefab;
        private static readonly Dictionary<CharacterBody, int> paintedTargetCounts = new Dictionary<CharacterBody, int>();
        private static readonly Dictionary<CharacterBody, float> nextPaintingUseTimes = new Dictionary<CharacterBody, float>();

        private static GameObject GetMissilePainterCrosshair(GameObject original)
        {
            if (missilePainterCrosshairPrefab) return missilePainterCrosshairPrefab;
            if (!original) return null;
            GameObject clone = UnityEngine.Object.Instantiate(original);
            clone.name = "3Qs Missile Painting Crosshair";
            UnityEngine.Object.DontDestroyOnLoad(clone);
            CrosshairController controller = clone.GetComponent<CrosshairController>();
            if (controller != null && controller.skillStockSpriteDisplays != null)
            {
                // The vanilla Engineer prefab looks at Utility (slot 2). Painting is
                // Secondary (slot 1) on a remote-operated Missile Drone.
                var displays = controller.skillStockSpriteDisplays;
                for (int i = 0; i < displays.Length; i++)
                {
                    var display = displays[i];
                    display.skillSlot = SkillSlot.Secondary;
                    // The zero-stock ReadyContainer is managed by our own component,
                    // which checks actual painted targets, rather than remaining stock.
                    if (display.target && display.target.name == "ReadyContainer")
                    {
                        display.minimumStockCountToBeValid = int.MaxValue;
                        display.maximumStockCountToBeValid = int.MaxValue;
                    }
                    displays[i] = display;
                }
            }
            clone.AddComponent<MissilePaintingCrosshair>();
            missilePainterCrosshairPrefab = clone;
            return clone;
        }

        // Execute after CrosshairController.LateUpdate so its stock-display pass
        // cannot overwrite the real painted-target indicator state.
        [DefaultExecutionOrder(1000)]
        public sealed class MissilePaintingCrosshair : MonoBehaviour
        {
            private HudElement hudElement;
            private GameObject readyContainer;

            private void Awake()
            {
                hudElement = GetComponent<HudElement>();
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                    if (child.name == "ReadyContainer") { readyContainer = child.gameObject; break; }
            }

            private void LateUpdate()
            {
                if (!readyContainer || !hudElement) return;
                CharacterBody body = hudElement.targetCharacterBody;
                int count;
                readyContainer.SetActive(body && paintedTargetCounts.TryGetValue(body, out count) && count > 0);
            }
        }


        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true,
                "Enable the player-controlled Missile Drone's custom skills. AI-controlled Missile Drones are unchanged. Restart to apply.");
            string p = Root + " - Guided Missile";
            primaryEnabled = C.BindStartupBool(p, "Changes Enabled", true, "Enable Guided Missile primary. Restart to apply.");
            primaryDamage = RemoteDroneSkills.Number(p, "Damage (%)", 250f, "Damage of each guided missile.");
            primaryRadius = RemoteDroneSkills.Number(p, "Explosion Radius (m)", 1f, "Explosion radius of each guided missile.");
            primaryHitboxScale = RemoteDroneSkills.Number(p, "Projectile Hitbox Scale", 1.5f, "Multiplier for each guided missile's collision hitbox, without enlarging its visual model. Vanilla: 1.");
            primaryProc = RemoteDroneSkills.Number(p, "Proc Coefficient", 1f, "Proc coefficient per guided missile.");
            primaryInterval = RemoteDroneSkills.Number(p, "Firing Interval (s)", 1f, "Time between missiles at base attack speed.");
            primaryRange = RemoteDroneSkills.Number(p, "Cursor Raycast Range (m)", 200f, "Maximum raycast distance used to steer missiles toward the cursor.");
            primaryTurnSpeed = RemoteDroneSkills.Number(p, "Rocket Turn Speed (degrees/s)", 240f, "Maximum in-flight steering speed of Hurricane-style missiles.");
            p = Root + " - Missile Painting";
            secondaryEnabled = C.BindStartupBool(p, "Changes Enabled", true, "Enable Thermal-Harpoon-style missile painting. Restart to apply.");
            secondaryDamage = RemoteDroneSkills.Number(p, "Damage Per Missile (%)", 200f, "Damage of each painted missile.");
            secondaryRadius = RemoteDroneSkills.Number(p, "Explosion Radius (m)", 2.5f, "Explosion radius of each painted missile.");
            secondaryProc = RemoteDroneSkills.Number(p, "Proc Coefficient", .5f, "Proc coefficient per painted missile.");
            secondaryCooldown = RemoteDroneSkills.Number(p, "Cooldown Per Stock (s)", 3f, "Time to recharge one painting charge.");
            secondaryReuseDelay = RemoteDroneSkills.Number(p, "Delay Between Volleys (s)", 1f, "Minimum delay after firing a painting volley before another can begin. Separate from stock recharge.");
            missilesPerMark = RemoteDroneSkills.Number(p, "Missiles Per Mark", 4, "Number of homing missiles fired for each painted target.");
            maximumMarks = RemoteDroneSkills.Number(p, "Maximum Marks Per Volley", 4, "Maximum distinct painted targets during one activation.");
            paintRange = RemoteDroneSkills.Number(p, "Painting Range (m)", 80f, "Maximum range to paint enemies.");
            paintAngle = RemoteDroneSkills.Number(p, "Painting Angle (degrees)", 12f, "Half-angle of the target acquisition cone.");
            paintInterval = RemoteDroneSkills.Number(p, "Marking Interval (s)", .25f, "Time between new target marks at base attack speed.");
            launchInterval = RemoteDroneSkills.Number(p, "Missile Launch Interval (s)", .08f, "Delay between missiles in a marked volley, divided by attack speed.");
            p = Root + " - Sticky Bomb";
            utilityEnabled = C.BindStartupBool(p, "Changes Enabled", true, "Replace Heavy Dash with a manually detonated sticky bomb. Restart to apply.");
            bombDamage = RemoteDroneSkills.Number(p, "Explosion Damage (%)", 600f, "Damage when manually detonated. Impact deals no damage.");
            bombRadius = RemoteDroneSkills.Number(p, "Explosion Radius (m)", 12f, "Blast radius; damage uses SweetSpot falloff.");
            bombProc = RemoteDroneSkills.Number(p, "Proc Coefficient", 1f, "Proc coefficient of the bomb explosion.");
            bombCooldown = RemoteDroneSkills.Number(p, "Cooldown (s)", 6f, "Cooldown begins on detonation.");
            bombLaunchSpeed = RemoteDroneSkills.Number(p, "Launch Speed (m/s)", 30f, "Initial bomb velocity before gravity takes over.");
            bombGravityScale = RemoteDroneSkills.Number(p, "Gravity Multiplier", 0.2f, "Gravity affecting the flying sticky bomb. 1 = normal gravity; 0 = no gravity.");
            bombVisualScale = RemoteDroneSkills.Number(p, "Projectile Visual Scale", 2.5f, "Multiplier for the sticky bomb visual model and effects. Does not enlarge its collision hitbox.");
            bombEnemyForce = RemoteDroneSkills.Number(p, "Enemy Knockback Force", 9000f, "Outward explosion force on enemies.");
            bombSelfImpulse = RemoteDroneSkills.Number(p, "Self Propulsion (m/s)", 75f, "Outward velocity impulse applied to the drone; physically adds to existing momentum.");
            bombLifetime = RemoteDroneSkills.Number(p, "Maximum Lifetime (s)", 30f, "Automatically detonates and starts cooldown if the bomb is not triggered before this time.");
            bombDetonationDelay = RemoteDroneSkills.Number(p, "Minimum Detonation Delay (s)", 0.18f, "Minimum time after launching before Detonate Bomb can activate. The Utility key must also be released before pressing it again.");
            bombVfxScaleMultiplier = RemoteDroneSkills.Number(p, "Explosion VFX Scale Multiplier", 0.5f, "Visual calibration for Sticky Bomb explosions (relative to the blast radius). Other missile explosions use their configured radius with a fixed 0.5x VFX calibration.");
            p = Root + " - Missile Barrage";
            specialEnabled = C.BindStartupBool(p, "Changes Enabled", true, "Enable designated-area Missile Barrage. Restart to apply.");
            specialDamage = RemoteDroneSkills.Number(p, "Damage Per Missile (%)", 400f, "Damage dealt by each barrage missile.");
            specialRadius = RemoteDroneSkills.Number(p, "Explosion Radius (m)", 5.5f, "Explosion radius of each barrage missile.");
            specialProc = RemoteDroneSkills.Number(p, "Proc Coefficient", .3f, "Proc coefficient per barrage missile.");
            specialCooldown = RemoteDroneSkills.Number(p, "Cooldown (s)", 20f, "Missile Barrage cooldown.");
            barrageCount = RemoteDroneSkills.Number(p, "Missiles", 20, "Number of missiles launched into the selected area.");
            barrageAreaRadius = RemoteDroneSkills.Number(p, "Target Area Radius (m)", 6f, "Radius of the designated impact area.");
            barrageRange = RemoteDroneSkills.Number(p, "Designation Range (m)", 120f, "Maximum distance to designate the barrage centre.");
            barrageInterval = RemoteDroneSkills.Number(p, "Launch Interval (s)", .09f, "Time between barrage missiles, divided by attack speed.");
            barrageFlightHeight = RemoteDroneSkills.Number(p, "Flight Height Above Area (m)", 70f, "Height above the higher of the launch position or target area where missiles travel before descending.");
        }

        internal static void Register()
        {
            if (enabled == null || !enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("MissileGuided", "Guided Missile", "Fire a missile guided toward your cursor.",
                typeof(GuidedMissile), PrimaryMachine, 0f, null);
            secondary = RemoteDroneSkills.MakeSkill("MissilePainting", "Missile Painting", "Hold to paint enemies. Release to fire homing missile volleys.",
                typeof(MissilePainting), SecondaryMachine, C.Get(secondaryCooldown), null, true,
                ScriptableObject.CreateInstance<SkillDef>());
            // Spent ONLY when a valid painted target's barrage starts; cancelled or empty
            // painting never consumes ammunition. The base SkillDef has no stock side effects.
            secondary.stockToConsume = 0;
            secondary.baseMaxStock = Mathf.Max(1, C.Get(maximumMarks));
            secondary.rechargeStock = 1;
            secondary.requiredStock = 1;
            utility = RemoteDroneSkills.MakeSkill("MissileHeavyDash", "Sticky Bomb", "Launch a gravity-affected sticky bomb. Press again to detonate it and propel yourself away.",
                typeof(StickyBombCast), UtilityMachine, C.Get(bombCooldown), null);
            // While a bomb is armed, swap Utility to an immediately usable contextual
            // detonation skill. The original Utility charge is not spent until it detonates.
            bombDetonate = ScriptableObject.CreateInstance<SkillDef>();
            bombDetonate.skillName = "3QSRemoteMissileDetonateBomb";
            bombDetonate.skillNameToken = "3QS_REMOTE_MISSILE_DETONATE_NAME";
            bombDetonate.skillDescriptionToken = "3QS_REMOTE_MISSILE_DETONATE_DESCRIPTION";
            bombDetonate.activationState = new SerializableEntityStateType(typeof(StickyBombDetonate));
            bombDetonate.activationStateMachineName = UtilityMachine;
            bombDetonate.baseMaxStock = 1;
            bombDetonate.requiredStock = 0;
            bombDetonate.stockToConsume = 0;
            bombDetonate.rechargeStock = 0;
            bombDetonate.baseRechargeInterval = 0f;
            bombDetonate.beginSkillCooldownOnSkillEnd = false;
            bombDetonate.mustKeyPress = true;
            bombDetonate.cancelSprintingOnActivation = false;
            bombDetonate.isCombatSkill = true;
            R2API.LanguageAPI.Add(bombDetonate.skillNameToken, "Detonate Bomb");
            R2API.LanguageAPI.Add(bombDetonate.skillDescriptionToken,
                "Detonate the launched sticky bomb, damaging and knocking back nearby enemies and propelling yourself away.");
            RemoteDroneSkills.Definitions.Add(bombDetonate);
            special = RemoteDroneSkills.MakeSkill("MissileBarrage", "Missile Barrage", "Designate an area, then confirm with Primary to unleash explosive missiles that stun on hit.",
                typeof(MissileBarrage), SpecialMachine, C.Get(specialCooldown), null, false,
                ScriptableObject.CreateInstance<SkillDef>());
            // Dedicated contextual Primary for selecting the barrage location. Unlike
            // Engineer's dummy, this has the correct player-facing name and tooltip.
            barrageConfirm = ScriptableObject.CreateInstance<SkillDef>();
            barrageConfirm.skillName = "3QSRemoteMissileBarrageConfirm";
            barrageConfirm.skillNameToken = "3QS_REMOTE_MISSILE_BARRAGE_CONFIRM_NAME";
            barrageConfirm.skillDescriptionToken = "3QS_REMOTE_MISSILE_BARRAGE_CONFIRM_DESCRIPTION";
            barrageConfirm.activationState = new SerializableEntityStateType(typeof(Idle));
            barrageConfirm.activationStateMachineName = PrimaryMachine;
            barrageConfirm.baseMaxStock = 1;
            barrageConfirm.requiredStock = 0;
            barrageConfirm.stockToConsume = 0;
            barrageConfirm.rechargeStock = 0;
            barrageConfirm.baseRechargeInterval = 0f;
            barrageConfirm.mustKeyPress = true;
            barrageConfirm.isCombatSkill = false;
            R2API.LanguageAPI.Add(barrageConfirm.skillNameToken, "Launch Barrage");
            R2API.LanguageAPI.Add(barrageConfirm.skillDescriptionToken,
                "Confirm the designated area and launch your missile barrage.");
            RemoteDroneSkills.Definitions.Add(barrageConfirm);
            primary.mustKeyPress = false;
            secondary.mustKeyPress = true;
            utility.mustKeyPress = true;
            utility.stockToConsume = 0; // Spend the charge on detonation, not launch.
            special.mustKeyPress = true;
            primary.cancelSprintingOnActivation = false;
            secondary.cancelSprintingOnActivation = false;
            utility.cancelSprintingOnActivation = false;
            special.cancelSprintingOnActivation = false;
            Refresh();
            if (!patched)
            {
                patched = true;
                // ProjectileManager initializes a projectile before it is network-spawned.
                // Adjust the spawned instance, never the shared vanilla prefab.
                var missileHarmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteMissileProjectile");
                missileHarmony.Patch(AccessTools.Method(typeof(ProjectileManager), "InitializeProjectile"),
                    postfix: new HarmonyMethod(typeof(RemoteMissileDrone), nameof(OnProjectileInitialized)));
                // MicroMissileProjectile has ProjectileSingleTargetImpact, not ProjectileExplosion.
                // Replace only configured remote-drone impacts with radial damage.
                missileHarmony.Patch(AccessTools.Method(typeof(ProjectileSingleTargetImpact), "OnProjectileImpact"),
                    prefix: new HarmonyMethod(typeof(RemoteMissileDrone), nameof(OnSingleTargetMissileImpact)));
                // The vanilla MissileExplosionVFX is already in EffectCatalog, but its
                // EffectComponent.applyScale is false. Enable it ONLY while our tagged
                // instance resets; pooled and vanilla effects keep their original setting.
                missileHarmony.Patch(AccessTools.Method(typeof(EffectComponent), "Reset"),
                    prefix: new HarmonyMethod(typeof(RemoteMissileDrone), nameof(BeforeMissileVisualReset)),
                    postfix: new HarmonyMethod(typeof(RemoteMissileDrone), nameof(AfterMissileVisualReset)));
            }
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = 0f;
            // Secondary ammo is manually consumed when its missiles actually launch.
            secondary.stockToConsume = 0;
            secondary.baseMaxStock = Mathf.Max(1, C.Get(maximumMarks));
            secondary.rechargeStock = 1;
            secondary.requiredStock = 1;
            secondary.baseRechargeInterval = Mathf.Max(0f, C.Get(secondaryCooldown));
            utility.baseRechargeInterval = Mathf.Max(0f, C.Get(bombCooldown));
            utility.beginSkillCooldownOnSkillEnd = false;
            utility.stockToConsume = 0;
            special.baseRechargeInterval = Mathf.Max(0f, C.Get(specialCooldown));
            // Targeting is free. The charge and cooldown begin only on M1 confirmation.
            special.beginSkillCooldownOnSkillEnd = false;
            special.stockToConsume = 0;
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"Fire a missile that follows your cursor for <style=cIsDamage>{C.Get(primaryDamage):0.##}% damage</style>, exploding in a {C.Get(primaryRadius):0.##}m radius.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Hold to paint up to <style=cIsUtility>{Mathf.Max(1, C.Get(maximumMarks))} enemies</style>. Release to fire <style=cIsDamage>{Mathf.Max(1, C.Get(missilesPerMark))} homing missiles</style> per target, dealing <style=cIsDamage>{C.Get(secondaryDamage):0.##}% damage</style> each.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Launch a <style=cIsUtility>sticky bomb</style>. Press again to detonate for <style=cIsDamage>{C.Get(bombDamage):0.##}% damage</style> in a {C.Get(bombRadius):0.##}m radius, knocking enemies and yourself away.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Designate an area and launch <style=cIsDamage>{Mathf.Max(1, C.Get(barrageCount))} missiles</style>, each dealing <style=cIsDamage>{C.Get(specialDamage):0.##}% damage</style> across a {C.Get(barrageAreaRadius):0.##}m radius and <style=cIsUtility>stunning enemies for 1 second</style>.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () =>
                $"\nDamage: {C.Get(primaryDamage):0.##}%\nExplosion Radius: {C.Get(primaryRadius):0.##}m\nProjectile Hitbox Scale: {C.Get(primaryHitboxScale):0.##}x\nFire Interval: {C.Get(primaryInterval):0.##}s\nCursor Range: {C.Get(primaryRange):0.##}m\nTurn Speed: {C.Get(primaryTurnSpeed):0.##}°/s\nPocket I.C.B.M.: Compatible", () => Mathf.Max(0f, C.Get(primaryProc)));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () =>
                $"\nDamage Per Missile: {C.Get(secondaryDamage):0.##}%\nMissiles Per Mark: {C.Get(missilesPerMark)}\nMaximum Targets: {C.Get(maximumMarks)}\nExplosion Radius: {C.Get(secondaryRadius):0.##}m\nPainting Range: {C.Get(paintRange):0.##}m\nPainting Angle: {C.Get(paintAngle):0.##}°\nStock Recharge: {C.Get(secondaryCooldown):0.##}s\nDelay Between Volleys: {C.Get(secondaryReuseDelay):0.##}s\nPocket I.C.B.M.: Compatible", () => Mathf.Max(0f, C.Get(secondaryProc)));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility, () =>
                $"\nExplosion Damage: {C.Get(bombDamage):0.##}%\nRadius: {C.Get(bombRadius):0.##}m\nLaunch Speed: {C.Get(bombLaunchSpeed):0.##}m/s\nGravity: {C.Get(bombGravityScale):0.##}x\nVisual Size: {C.Get(bombVisualScale):0.##}x\nEnemy Knockback: {C.Get(bombEnemyForce):0.##}\nSelf Propulsion: {C.Get(bombSelfImpulse):0.##}m/s\nMinimum Detonation Delay: {C.Get(bombDetonationDelay):0.##}s\nExplosion VFX Scale: {C.Get(bombVfxScaleMultiplier):0.##}x\nCooldown: {C.Get(bombCooldown):0.##}s\nFalloff: SweetSpot", () => Mathf.Max(0f, C.Get(bombProc)));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () =>
                $"\nMissiles: {C.Get(barrageCount)}\nDamage Per Missile: {C.Get(specialDamage):0.##}%\nExplosion Radius: {C.Get(specialRadius):0.##}m\nTarget Area Radius: {C.Get(barrageAreaRadius):0.##}m\nDesignation Range: {C.Get(barrageRange):0.##}m\nFlight Height: {C.Get(barrageFlightHeight):0.##}m\nStun: 1s on hit\nPocket I.C.B.M.: Compatible", () => Mathf.Max(0f, C.Get(specialProc)));
        }

        // The contextual detonation skill has its own sprite, not a shared reference
        // to the launch icon. GPU readback avoids unreadable imported sprite textures.
        private static Sprite CreateTintedIcon(Sprite source, Color tint)
        {
            if (!source || !source.texture) return null;
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Texture2D original = source.texture;
                temporary = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(original, temporary);
                RenderTexture.active = temporary;
                Texture2D copy = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
                Color32[] pixels = copy.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 c = pixels[i];
                    c.r = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * tint.r), 0, 255);
                    c.g = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * tint.g), 0, 255);
                    c.b = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * tint.b), 0, 255);
                    pixels[i] = c;
                }
                copy.SetPixels32(pixels);
                copy.Apply(false, false);
                return Sprite.Create(copy, source.rect,
                    new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
                    source.pixelsPerUnit);
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Missile Sticky Bomb: icon tint failed: " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !prefab || !primary) return;
            Sprite nativeIcon = RemoteDroneSkills.VanillaIcon("MissileDroneBody", "Primary", null);
            primary.icon = nativeIcon;
            secondary.icon = RemoteDroneSkills.VanillaIcon("EngiBody", "Utility", nativeIcon, 1);
            utility.icon = RemoteDroneSkills.VanillaIcon("RailgunnerBody", "Utility", nativeIcon);
            // Use a separate vanilla bombardment-themed icon until a dedicated sprite is supplied.
            special.icon = RemoteDroneSkills.VanillaIcon("CaptainBody", "Special", nativeIcon);
            if (barrageConfirm) barrageConfirm.icon = special.icon;
            if (bombDetonate) bombDetonate.icon = CreateTintedIcon(utility.icon, new Color(1f, .47f, .21f, 1f)) ?? utility.icon;
            if (primaryEnabled.Value) { RemoteDroneSkills.AddMachine(prefab, PrimaryMachine); RemoteDroneSkills.SetSkill(prefab, "Primary", primary); }
            if (secondaryEnabled.Value) { RemoteDroneSkills.AddMachine(prefab, SecondaryMachine); RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary); }
            if (utilityEnabled.Value) { RemoteDroneSkills.AddMachine(prefab, UtilityMachine); RemoteDroneSkills.SetSkill(prefab, "Utility", utility); }
            if (specialEnabled.Value) { RemoteDroneSkills.AddMachine(prefab, SpecialMachine); RemoteDroneSkills.SetSkill(prefab, "Special", special); }
        }

        // The static field is populated by vanilla's FireMissileBarrage state configuration.
        // Do not substitute another character's projectile if it is unavailable.
        private static GameObject MissilePrefab
        {
            get
            {
                if (nativeProjectile) return nativeProjectile;
                nativeProjectile = EntityStates.Drone.DroneWeapon.FireMissileBarrage.projectilePrefab;
                nativeMuzzleFlash = EntityStates.Drone.DroneWeapon.FireMissileBarrage.effectPrefab;
                if (!nativeProjectile)
                {
                    EntityStateConfiguration config = LegacyResourcesAPI.Load<EntityStateConfiguration>(
                        "EntityStateConfigurations/EntityStates.Drone.DroneWeapon.FireMissileBarrage");
                    if (config)
                    {
                        var state = new EntityStates.Drone.DroneWeapon.FireMissileBarrage();
                        config.BuildInstanceInitializer()?.Invoke(state);
                        nativeProjectile = EntityStates.Drone.DroneWeapon.FireMissileBarrage.projectilePrefab;
                        nativeMuzzleFlash = EntityStates.Drone.DroneWeapon.FireMissileBarrage.effectPrefab;
                    }
                }
                if (!nativeProjectile && !warnedMissingProjectile)
                {
                    warnedMissingProjectile = true;
                    ThreeQsVanillaTweaks.Log.Warning("Remote Missile Drone: native FireMissileBarrage.projectilePrefab is unavailable; untargeted missile skills cannot fire.");
                }
                return nativeProjectile;
            }
        }

        private static GameObject PainterProjectile
        {
            get
            {
                if (painterProjectile) return painterProjectile;
                painterProjectile = EntityStates.Engi.EngiMissilePainter.Fire.projectilePrefab;
                if (!painterProjectile)
                {
                    EntityStateConfiguration config = LegacyResourcesAPI.Load<EntityStateConfiguration>(
                        "EntityStateConfigurations/EntityStates.Engi.EngiMissilePainter.Fire");
                    if (config)
                    {
                        var state = new EntityStates.Engi.EngiMissilePainter.Fire();
                        config.BuildInstanceInitializer()?.Invoke(state);
                        painterProjectile = EntityStates.Engi.EngiMissilePainter.Fire.projectilePrefab;
                    }
                }
                return painterProjectile;
            }
        }

        private static bool IsMissileVisual(EffectComponent component)
        {
            if (!component || component.effectData == null ||
                !component.gameObject.name.StartsWith("MissileExplosionVFX", StringComparison.Ordinal))
                return false;
            Color c = component.effectData.color;
            return Mathf.Abs(c.r - missileBlastVisualTag.r) < .002f &&
                   Mathf.Abs(c.g - missileBlastVisualTag.g) < .002f &&
                   Mathf.Abs(c.b - missileBlastVisualTag.b) < .002f &&
                   Mathf.Abs(c.a - missileBlastVisualTag.a) < .002f;
        }

        private static void BeforeMissileVisualReset(EffectComponent __instance, out bool __state)
        {
            __state = false;
            if (!IsMissileVisual(__instance)) return;
            __state = __instance.applyScale;
            __instance.applyScale = true;
        }

        private static void AfterMissileVisualReset(EffectComponent __instance, bool __state)
        {
            if (IsMissileVisual(__instance)) __instance.applyScale = __state;
        }

        private static void SpawnMissileExplosion(GameObject effect, Vector3 origin, float radius, float scaleMultiplier)
        {
            if (!effect)
            {
                if (!warnedMissingExplosion)
                {
                    warnedMissingExplosion = true;
                    ThreeQsVanillaTweaks.Log.Warning("Remote Missile Drone: missing native MissileExplosionVFX prefab; visual explosion cannot be spawned.");
                }
                return;
            }
            // EffectManager resolves a registered EffectIndex on both the server and
            // clients. The marker makes Reset temporarily apply the specified scale.
            EffectManager.SpawnEffect(effect, new EffectData
            {
                origin = origin,
                scale = Mathf.Max(.01f, radius * scaleMultiplier),
                color = missileBlastVisualTag
            }, true);
        }

        private static void OnProjectileInitialized(ProjectileController projectileController, FireProjectileInfo fireProjectileInfo)
        {
            if (!projectileController || !fireProjectileInfo.owner || !primary ||
                (fireProjectileInfo.projectilePrefab != nativeProjectile &&
                 fireProjectileInfo.projectilePrefab != painterProjectile)) return;
            CharacterBody owner = fireProjectileInfo.owner.GetComponent<CharacterBody>();
            if (!owner || !RemoteDroneSkills.IsMissile(owner)) return;
            ProjectileDamage damage = projectileController.GetComponent<ProjectileDamage>();
            if (!damage) return;
            if (fireProjectileInfo.projectilePrefab == nativeProjectile &&
                fireProjectileInfo.damageTypeOverride.HasValue &&
                fireProjectileInfo.damageTypeOverride.Value.damageSource == DamageSource.Utility)
            {
                SetupStickyBombProjectile(projectileController, owner);
                return;
            }
            // The launch context is explicit and survives initialization order changes.
            DamageSource source = pendingLaunchSource.HasValue
                ? pendingLaunchSource.Value : damage.damageType.damageSource;
            ConfigEntry<float> radius = source == DamageSource.Secondary ? secondaryRadius :
                source == DamageSource.Special ? specialRadius : primaryRadius;
            ConfigEntry<float> proc = source == DamageSource.Secondary ? secondaryProc :
                source == DamageSource.Special ? specialProc : primaryProc;
            ProjectileExplosion explosion = projectileController.GetComponent<ProjectileExplosion>();
            if (explosion)
            {
                explosion.blastRadius = Mathf.Max(0f, C.Get(radius));
                explosion.preserveExplosionScale = false;
            }
            else if ((source == DamageSource.Primary || source == DamageSource.Special) &&
                     fireProjectileInfo.projectilePrefab == nativeProjectile)
            {
                ProjectileSingleTargetImpact single = projectileController.GetComponent<ProjectileSingleTargetImpact>();
                if (single)
                {
                    ConfiguredMissileBlast configured = projectileController.gameObject.AddComponent<ConfiguredMissileBlast>();
                    configured.radius = Mathf.Max(0f, C.Get(radius));
                    configured.effectPrefab = single.impactEffect;
                    configured.source = source;
                }
                else if (!warnedMissingExplosion)
                {
                    warnedMissingExplosion = true;
                    ThreeQsVanillaTweaks.Log.Warning("Remote Missile Drone: projectile has no supported impact component.");
                }
            }
            // Enlarge only Primary projectile collision volumes, leaving the missile
            // model, Secondary harpoons, and Special barrage hitboxes untouched.
            // Unity Collider dimensions are local-space; scaling these fields does not
            // change the whole projectile transform or its movement speed.
            if (source == DamageSource.Primary && fireProjectileInfo.projectilePrefab == nativeProjectile)
            {
                float hitboxScale = Mathf.Max(0.01f, C.Get(primaryHitboxScale));
                if (!Mathf.Approximately(hitboxScale, 1f))
                {
                    foreach (Collider collider in projectileController.GetComponentsInChildren<Collider>(true))
                    {
                        if (!collider) continue;
                        SphereCollider sphere = collider as SphereCollider;
                        if (sphere) { sphere.radius *= hitboxScale; continue; }
                        CapsuleCollider capsule = collider as CapsuleCollider;
                        if (capsule)
                        {
                            capsule.radius *= hitboxScale;
                            capsule.height = Mathf.Max(capsule.height, capsule.radius * 2f);
                            continue;
                        }
                        BoxCollider box = collider as BoxCollider;
                        if (box) box.size *= hitboxScale;
                    }
                }
            }
            // The vanilla MissileController independently finds enemies, rotates toward
            // them, and overwrites velocity every physics tick. Disabling only target
            // acquisition is not sufficient: replace its propulsion on these two
            // non-homing skills, while leaving Engineer's Secondary missile unchanged.
            if ((source == DamageSource.Primary || source == DamageSource.Special) &&
                fireProjectileInfo.projectilePrefab == nativeProjectile)
            {
                var missile = projectileController.GetComponent<MissileController>();
                var simple = projectileController.GetComponent<ProjectileSimple>();
                var targetFinder = projectileController.GetComponent<ProjectileSphereTargetFinder>();
                var steer = projectileController.GetComponent<ProjectileSteerTowardTarget>();
                var targetComponent = projectileController.GetComponent<ProjectileTargetComponent>();
                if (missile) missile.enabled = false;
                if (targetFinder) targetFinder.enabled = false;
                if (steer) steer.enabled = false;
                if (simple) simple.enabled = false;
                if (targetComponent) targetComponent.target = null;

                if (!projectileController.GetComponent<NonHomingMissileFlight>())
                {
                    var flight = projectileController.gameObject.AddComponent<NonHomingMissileFlight>();
                    flight.owner = owner;
                    flight.guideWithPrimary = source == DamageSource.Primary;
                    if (source == DamageSource.Special && pendingBarrageImpact)
                    {
                        flight.barrageFlight = true;
                        flight.barrageImpactPoint = pendingBarragePoint;
                        flight.barrageCruiseAltitude = Mathf.Max(flight.transform.position.y, pendingBarragePoint.y) +
                            Mathf.Max(10f, C.Get(barrageFlightHeight));
                    }
                    Rigidbody projectileRigidbody = projectileController.GetComponent<Rigidbody>();
                    flight.flightSpeed = missile && missile.maxVelocity > 0f
                        ? missile.maxVelocity : simple && simple.desiredForwardSpeed > 0f
                            ? simple.desiredForwardSpeed
                            : projectileRigidbody ? projectileRigidbody.velocity.magnitude : 0f;
                    if (flight.flightSpeed <= 0f)
                        ThreeQsVanillaTweaks.Log.Warning(
                            "Remote Missile Drone: projectile has no readable forward speed; inspect its prefab movement components.");
                    flight.lifeTime = missile && missile.deathTimer > 0f
                        ? missile.deathTimer : simple && simple.lifetime > 0f
                            ? simple.lifetime : 10f;
                    if (flight.barrageFlight)
                    {
                        flight.SetBarrageCollisionEnabled(false);
                        float climb = Mathf.Max(0f, flight.barrageCruiseAltitude - projectileController.transform.position.y);
                        float cross = Vector2.Distance(
                            new Vector2(projectileController.transform.position.x, projectileController.transform.position.z),
                            new Vector2(flight.barrageImpactPoint.x, flight.barrageImpactPoint.z));
                        float descent = Mathf.Max(0f, flight.barrageCruiseAltitude - flight.barrageImpactPoint.y);
                        flight.lifeTime = Mathf.Max(flight.lifeTime, (climb + cross + descent) /
                            Mathf.Max(1f, flight.flightSpeed) + 8f);
                    }
                }
            }
            // The marker is removed after every projectile spawned for this barrage
            // has been destroyed by impact or expiry, not after a guessed flight timer.
            if (source == DamageSource.Special && pendingBarrageImpact && pendingBarrageMarker)
            {
                BarrageMissileLifetime tracker = projectileController.gameObject.AddComponent<BarrageMissileLifetime>();
                tracker.marker = pendingBarrageMarker;
                pendingBarrageMarker.TrackMissile();
            }
            // Preserve the native projectile's impact damage coefficient and VFX.
            // If a vanilla projectile has no ProjectileExplosion, it retains its native collision behaviour.
            projectileController.procCoefficient = Mathf.Max(0f, C.Get(proc));
        }

        // ProjectileController dispatches impact interfaces on collision, even if a Behaviour
        // is disabled. Patch the existing single-target handler instead of adding a second
        // damage handler, which would double-hit or fail to run before destruction.
        private static bool OnSingleTargetMissileImpact(ProjectileSingleTargetImpact __instance, ProjectileImpactInfo impactInfo)
        {
            if (__instance.GetComponent<StickyBombFlight>()) return false;
            ConfiguredMissileBlast configured = __instance.GetComponent<ConfiguredMissileBlast>();
            if (!configured) return true; // Vanilla and other projectiles are unchanged.
            if (configured.detonated) return false;
            ProjectileController controller = __instance.GetComponent<ProjectileController>();
            if (!controller || !impactInfo.collider) return false;
            if (impactInfo.collider.gameObject == controller.owner) return false;
            HurtBox hurtBox = impactInfo.collider.GetComponent<HurtBox>();
            if (hurtBox && hurtBox.healthComponent &&
                hurtBox.healthComponent.gameObject == controller.owner) return false;
            if (!hurtBox && !__instance.destroyOnWorld) return false;
            configured.detonated = true;
            if (NetworkServer.active)
            {
                ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
                if (damage)
                {
                    new BlastAttack
                    {
                        position = impactInfo.estimatedPointOfImpact,
                        radius = configured.radius,
                        baseDamage = damage.damage,
                        baseForce = damage.force,
                        attacker = controller.owner,
                        inflictor = __instance.gameObject,
                        teamIndex = controller.teamFilter.teamIndex,
                        crit = damage.crit,
                        procChainMask = controller.procChainMask,
                        procCoefficient = controller.procCoefficient,
                        damageColorIndex = damage.damageColorIndex,
                        damageType = damage.damageType,
                        falloffModel = BlastAttack.FalloffModel.None
                    }.Fire();
                }
                if (configured.effectPrefab)
                {
                    // The registered effect clone has applyScale enabled, so its particle
                    // visuals resize with the configured blast radius on every client.
                    SpawnMissileExplosion(configured.effectPrefab,
                        impactInfo.estimatedPointOfImpact, configured.radius, .5f);
                }
            }
            UnityEngine.Object.Destroy(__instance.gameObject);
            return false;
        }

        private sealed class ConfiguredMissileBlast : MonoBehaviour
        {
            internal float radius;
            internal GameObject effectPrefab;
            internal DamageSource source;
            internal bool detonated;
        }

        private static HurtBox FindTarget(BaseState state, float range, float angle)
        {
            CharacterBody body = state.GetComponent<CharacterBody>();
            if (!body || !body.teamComponent) return null;
            InputBankTest bank = state.GetComponent<InputBankTest>();
            if (!bank) return null;
            var search = new BullseyeSearch
            {
                filterByDistinctEntity = true, filterByLoS = true,
                minDistanceFilter = 0f, maxDistanceFilter = Mathf.Max(0f, range),
                minAngleFilter = 0f, maxAngleFilter = Mathf.Max(0f, angle),
                viewer = body, searchOrigin = bank.aimOrigin, searchDirection = bank.aimDirection,
                sortMode = BullseyeSearch.SortMode.DistanceAndAngle,
                teamMaskFilter = TeamMask.GetUnprotectedTeams(body.teamComponent.teamIndex)
            };
            search.RefreshCandidates();
            search.FilterOutGameObject(state.gameObject);
            foreach (HurtBox candidate in search.GetResults())
                if (candidate && candidate.healthComponent && candidate.healthComponent.alive) return candidate;
            return null;
        }

        private static Vector3 Muzzle(BaseState state)
        {
            ModelLocator model = state.GetComponent<ModelLocator>();
            if (model && model.modelTransform)
            {
                ChildLocator locator = model.modelTransform.GetComponent<ChildLocator>();
                if (locator)
                {
                    Transform muzzle = locator.FindChild("Muzzle");
                    if (muzzle) return muzzle.position;
                }
            }
            InputBankTest bank = state.GetComponent<InputBankTest>();
            return bank ? bank.aimOrigin : state.transform.position;
        }

        private static void Fire(BaseState state, DamageSource source, float percent, GameObject target, Vector3 direction, Vector3? barrageImpact = null, Vector3? barrageAreaCentre = null, BarrageMarkerLifetime barrageMarker = null)
        {
            GameObject projectile = source == DamageSource.Secondary ? PainterProjectile : MissilePrefab;
            CharacterBody body = state.GetComponent<CharacterBody>();
            if (!projectile || !body || !ProjectileManager.instance) return;
            Vector3 origin = Muzzle(state);
            Vector3 forward = direction.sqrMagnitude > .0001f ? direction.normalized : state.transform.forward;
            Quaternion rotation = Util.QuaternionSafeLookRotation(forward);
            float damage = body.damage * Mathf.Max(0f, percent) * .01f;
            bool crit = body.RollCrit();
            int icbm = body.inventory ? body.inventory.GetItemCountEffective(DLC1Content.Items.MoreMissile) : 0;
            if (icbm > 0) damage *= MissileUtils.GetMoreMissileDamageMultiplier(icbm);
            var shot = new FireProjectileInfo
            {
                projectilePrefab = projectile, owner = body.gameObject, position = origin,
                rotation = rotation, target = target, damage = damage, force = 0f,
                crit = crit, speedOverride = -1f, fuseOverride = -1f, childFuseOverride = -1f,
                damageTypeOverride = new DamageTypeCombo(source == DamageSource.Special ? DamageType.Stun1s : DamageType.Generic, DamageTypeExtended.Generic, source)
            };
            bool oldPending = pendingBarrageImpact;
            DamageSource? oldLaunchSource = pendingLaunchSource;
            Vector3 oldPoint = pendingBarragePoint;
            BarrageMarkerLifetime oldMarker = pendingBarrageMarker;
            try
            {
                pendingLaunchSource = source;
                pendingBarrageImpact = source == DamageSource.Special && barrageImpact.HasValue;
                pendingBarrageMarker = pendingBarrageImpact ? barrageMarker : null;
                if (pendingBarrageImpact) pendingBarragePoint = barrageImpact.Value;
                ProjectileManager.instance.FireProjectile(shot);
                // Pocket I.C.B.M. adds two separate rockets. Give special copies
                // independent impact points inside the designated area instead of
                // stacking all three on the same location.
                if (icbm > 0)
                {
                    float sideAngle = source == DamageSource.Primary ? 25f : 45f;
                    shot.rotation = Util.QuaternionSafeLookRotation(Quaternion.AngleAxis(sideAngle, Vector3.up) * forward);
                    if (pendingBarrageImpact && barrageAreaCentre.HasValue)
                        pendingBarragePoint = GetBarrageCopyImpact(barrageAreaCentre.Value, barrageImpact.Value);
                    ProjectileManager.instance.FireProjectile(shot);
                    shot.rotation = Util.QuaternionSafeLookRotation(Quaternion.AngleAxis(-sideAngle, Vector3.up) * forward);
                    if (pendingBarrageImpact && barrageAreaCentre.HasValue)
                        pendingBarragePoint = GetBarrageCopyImpact(barrageAreaCentre.Value, barrageImpact.Value);
                    ProjectileManager.instance.FireProjectile(shot);
                }
            }
            finally
            {
                pendingBarrageImpact = oldPending;
                pendingLaunchSource = oldLaunchSource;
                pendingBarragePoint = oldPoint;
                pendingBarrageMarker = oldMarker;
            }
            if (nativeMuzzleFlash)
                EffectManager.SimpleMuzzleFlash(nativeMuzzleFlash, body.gameObject, "Muzzle", false);
        }

        private static Vector3 GetBarrageCopyImpact(Vector3 centre, Vector3 originalImpact)
        {
            // Preserve the ground height and keep the additional missile inside
            // the configured horizontal target disk.
            Vector2 offset = UnityEngine.Random.insideUnitCircle * Mathf.Max(0f, C.Get(barrageAreaRadius));
            return new Vector3(centre.x + offset.x, originalImpact.y, centre.z + offset.y);
        }

        // The area marker persists independently of the skill state and is removed
        // when the last spawned missile is destroyed (impact or lifetime expiry).
        // This tracks the local/host-side projectile instances; client-only
        // multiplayer tracking requires a networked projectile lifecycle signal.
        public sealed class BarrageMarkerLifetime : MonoBehaviour
        {
            private int activeMissiles;
            private bool launchComplete;
            internal void TrackMissile() { activeMissiles++; }
            internal void MissileFinished()
            {
                activeMissiles = Mathf.Max(0, activeMissiles - 1);
                TryComplete();
            }
            internal void FinishLaunching()
            {
                launchComplete = true;
                TryComplete();
            }
            private void TryComplete()
            {
                if (launchComplete && activeMissiles == 0)
                    UnityEngine.Object.Destroy(gameObject);
            }
        }

        public sealed class BarrageMissileLifetime : MonoBehaviour
        {
            internal BarrageMarkerLifetime marker;
            private void OnDestroy()
            {
                if (marker) marker.MissileFinished();
            }
        }

        // Controls the original Missile Drone projectile without its native enemy
        // seeking. A primary rocket turns only while M1 is held; a special rocket
        // follows an explicit ascent / overhead transit / descent route.
        public sealed class NonHomingMissileFlight : MonoBehaviour
        {
            internal CharacterBody owner;
            internal bool guideWithPrimary;
            internal bool barrageFlight;
            internal Vector3 barrageImpactPoint;
            internal float barrageCruiseAltitude;
            private int barragePhase; // 0: ascend; 1: cross overhead; 2: descend
            private Vector3 descentDirection = Vector3.down;
            internal float flightSpeed;
            internal float lifeTime;
            private Rigidbody projectileBody;
            private Collider[] barrageColliders;
            private Behaviour[] barrageImpactBehaviours;
            private bool barrageCollisionEnabled = true;
            private float age;

            internal void SetBarrageCollisionEnabled(bool enabled)
            {
                if (!barrageFlight || barrageCollisionEnabled == enabled) return;
                if (barrageColliders == null) barrageColliders = GetComponentsInChildren<Collider>(true);
                // A disabled collider alone is not enough: ProjectileImpactExplosion
                // also has autonomous lifetime/distance detonation logic. Suspend all
                // projectile impact behaviours until descent, then restore them.
                if (barrageImpactBehaviours == null)
                {
                    var allBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
                    var impacts = new List<Behaviour>();
                    foreach (MonoBehaviour behaviour in allBehaviours)
                        if (behaviour && behaviour is IProjectileImpactBehavior)
                            impacts.Add(behaviour);
                    barrageImpactBehaviours = impacts.ToArray();
                }
                foreach (Behaviour impactBehaviour in barrageImpactBehaviours)
                    if (impactBehaviour) impactBehaviour.enabled = enabled;
                foreach (Collider hitbox in barrageColliders)
                    if (hitbox) hitbox.enabled = enabled;
                barrageCollisionEnabled = enabled;
            }

            private void Awake()
            {
                projectileBody = GetComponent<Rigidbody>();
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                age += Time.fixedDeltaTime;
                if (lifeTime > 0f && age >= lifeTime)
                {
                    UnityEngine.Object.Destroy(gameObject);
                    return;
                }
                if (barrageFlight)
                {
                    // Launch vertically, fly across at a safe height, and only then
                    // descend on the assigned impact point. No enemy-target component
                    // participates at any stage.
                    Vector3 destination;
                    switch (barragePhase)
                    {
                        case 0:
                            destination = new Vector3(transform.position.x, barrageCruiseAltitude, transform.position.z);
                            if (transform.position.y >= barrageCruiseAltitude - 2f)
                            {
                                barragePhase = 1;
                                destination = new Vector3(barrageImpactPoint.x, barrageCruiseAltitude, barrageImpactPoint.z);
                            }
                            break;
                        case 1:
                            destination = new Vector3(barrageImpactPoint.x, barrageCruiseAltitude, barrageImpactPoint.z);
                            if (Vector2.Distance(new Vector2(transform.position.x, transform.position.z),
                                new Vector2(barrageImpactPoint.x, barrageImpactPoint.z)) < 2.5f)
                            {
                                barragePhase = 2;
                                // Lock the descent trajectory. Continuously homing to
                                // the impact point makes a missile turn back after it
                                // passes that coordinate, even above a bottomless pit.
                                Vector3 descent = barrageImpactPoint - transform.position;
                                descentDirection = descent.sqrMagnitude > .001f
                                    ? descent.normalized : Vector3.down;
                                if (descentDirection.y >= -.01f) descentDirection = Vector3.down;
                                SetBarrageCollisionEnabled(true);
                                destination = transform.position + descentDirection * 1000f;
                            }
                            break;
                        default:
                            // Fly past the designated point until striking real
                            // geometry/enemy or reaching the projectile lifetime.
                            destination = transform.position + descentDirection * 1000f;
                            break;
                    }
                    Vector3 displacement = destination - transform.position;
                    if (displacement.sqrMagnitude > .001f)
                        transform.rotation = Util.QuaternionSafeLookRotation(displacement.normalized);
                }
                else if (guideWithPrimary && owner && owner.inputBank && owner.inputBank.skill1.down)
                {
                    InputBankTest bank = owner.inputBank;
                    Ray ray = new Ray(bank.aimOrigin, bank.aimDirection);
                    float range = Mathf.Max(10f, C.Get(primaryRange));
                    Vector3 cursorPoint = ray.GetPoint(range);
                    RaycastHit[] hits = Physics.RaycastAll(ray, range, ~0,
                        QueryTriggerInteraction.Collide);
                    float nearest = float.PositiveInfinity;
                    foreach (RaycastHit hit in hits)
                    {
                        if (!hit.collider || hit.distance <= .1f || hit.distance >= nearest) continue;
                        if (hit.collider.transform.IsChildOf(owner.transform)) continue;
                        if (hit.collider.GetComponentInParent<ProjectileController>()) continue;
                        if (hit.collider.isTrigger && !hit.collider.GetComponent<HurtBox>()) continue;
                        nearest = hit.distance;
                        cursorPoint = hit.point;
                    }
                    Vector3 desired = cursorPoint - transform.position;
                    if (desired.sqrMagnitude > .01f)
                    {
                        Vector3 direction = Vector3.RotateTowards(transform.forward, desired.normalized,
                            Mathf.Max(0f, C.Get(primaryTurnSpeed)) * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
                        transform.rotation = Util.QuaternionSafeLookRotation(direction);
                    }
                }
                // This is the sole velocity writer for these projectiles: the native
                // controller and ProjectileSimple are disabled above.
                if (projectileBody && !projectileBody.isKinematic)
                    projectileBody.velocity = transform.forward * flightSpeed;
            }
        }

        public sealed class GuidedMissile : BaseSkillState
        {
            private float firingProgress;
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority) FireShot();
            }
            private void FireShot()
            {
                Vector3 direction = inputBank ? inputBank.aimDirection : transform.forward;
                Fire(this, DamageSource.Primary, C.Get(primaryDamage), null, direction);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority) return;
                // Stay in the firing state while held, instead of depending on the
                // body's main state to reactivate this skill between every missile.
                if (!inputBank || !inputBank.skill1.down)
                {
                    outer.SetNextStateToMain();
                    return;
                }
                float interval = Mathf.Max(.05f, C.Get(primaryInterval));
                firingProgress += GetDeltaTime() * Mathf.Max(.1f, attackSpeedStat) / interval;
                if (firingProgress >= 1f)
                {
                    firingProgress -= 1f;
                    FireShot();
                }
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        public sealed class MissilePainting : BaseSkillState
        {
            private readonly List<HurtBox> marks = new List<HurtBox>();
            private readonly Dictionary<HurtBox, Indicator> indicators = new Dictionary<HurtBox, Indicator>();
            private Indicator highlighted;
            private CrosshairUtils.OverrideRequest crosshair;
            private float nextMark, fireTime;
            private int targetIndex, missileIndex;
            private bool releasing;
            private bool firedAny;
            private bool blockedByReuseDelay;
            private GameObject indicatorPrefab;
            public override void OnEnter()
            {
                base.OnEnter();
                if (!isAuthority) return;
                float nextAllowed;
                blockedByReuseDelay = characterBody &&
                    nextPaintingUseTimes.TryGetValue(characterBody, out nextAllowed) &&
                    Time.time < nextAllowed;
                if (blockedByReuseDelay) return;
                paintedTargetCounts[characterBody] = 0;
                // Same indicators used by the Engineer's Thermal Harpoons.
                indicatorPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/EngiMissileTrackingIndicator");
                // Try immediately; if the vanilla state configuration is still
                // initializing on the first use, FixedUpdate retries until available.
                TryRequestCrosshair();
            }
            private void TryRequestCrosshair()
            {
                if (crosshair != null || !characterBody) return;
                if (!EntityStates.Engi.EngiMissilePainter.Paint.crosshairOverridePrefab ||
                    !EntityStates.Engi.EngiMissilePainter.Paint.stickyTargetIndicatorPrefab)
                {
                    EntityStateConfiguration painterConfig = LegacyResourcesAPI.Load<EntityStateConfiguration>(
                        "EntityStateConfigurations/EntityStates.Engi.EngiMissilePainter.Paint");
                    if (painterConfig)
                    {
                        var vanillaPaint = new EntityStates.Engi.EngiMissilePainter.Paint();
                        painterConfig.BuildInstanceInitializer()?.Invoke(vanillaPaint);
                    }
                }
                GameObject highlightedPrefab = EntityStates.Engi.EngiMissilePainter.Paint.stickyTargetIndicatorPrefab;
                if (highlighted == null && highlightedPrefab)
                    highlighted = new Indicator(gameObject, highlightedPrefab);
                GameObject crosshairPrefab = GetMissilePainterCrosshair(
                    EntityStates.Engi.EngiMissilePainter.Paint.crosshairOverridePrefab);
                if (crosshairPrefab)
                    crosshair = CrosshairUtils.RequestOverrideForBody(characterBody,
                        crosshairPrefab, CrosshairUtils.OverridePriority.Skill);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority) return;
                if (blockedByReuseDelay) { outer.SetNextStateToMain(); return; }
                TryRequestCrosshair();
                if (!releasing)
                {
                    int capacity = Mathf.Min(Mathf.Max(0, activatorSkillSlot.stock),
                        Mathf.Max(1, C.Get(maximumMarks)));
                    HurtBox target = FindTarget(this, C.Get(paintRange), C.Get(paintAngle));
                    if (highlighted != null)
                    {
                        highlighted.targetTransform = target && !marks.Contains(target) && marks.Count < capacity
                            ? target.transform : null;
                        highlighted.active = highlighted.targetTransform;
                    }
                    // Hold secondary to select targets. Reaching capacity must NOT fire.
                    if (inputBank && inputBank.skill2.down && marks.Count < capacity &&
                        fixedAge >= nextMark && target && !marks.Contains(target))
                    {
                        marks.Add(target);
                        paintedTargetCounts[characterBody] = marks.Count;
                        if (indicatorPrefab)
                        {
                            var marker = new Indicator(gameObject, indicatorPrefab);
                            marker.targetTransform = target.transform;
                            marker.active = true;
                            indicators.Add(target, marker);
                        }
                        nextMark = fixedAge + Mathf.Max(.02f, C.Get(paintInterval)) /
                            Mathf.Max(.1f, attackSpeedStat);
                    }
                    if (inputBank && inputBank.skill2.down) return;
                    // Release with zero targets -> no missiles, no ammo consumed.
                    if (marks.Count == 0) { outer.SetNextStateToMain(); return; }
                    releasing = true;
                    if (highlighted != null) highlighted.active = false;
                    return;
                }
                if (fixedAge < fireTime) return;
                while (targetIndex < marks.Count &&
                    (!marks[targetIndex] || !marks[targetIndex].healthComponent ||
                     !marks[targetIndex].healthComponent.alive))
                { targetIndex++; missileIndex = 0; }
                if (targetIndex >= marks.Count) { outer.SetNextStateToMain(); return; }
                if (missileIndex == 0)
                {
                    // One stock pays for all missiles against one marked target.
                    if (!activatorSkillSlot || activatorSkillSlot.stock <= 0)
                    { outer.SetNextStateToMain(); return; }
                    activatorSkillSlot.DeductStock(1);
                    firedAny = true;
                }
                HurtBox painted = marks[targetIndex];
                Vector3 direction = (painted.transform.position - Muzzle(this)).normalized;
                Fire(this, DamageSource.Secondary, C.Get(secondaryDamage), painted.gameObject, direction);
                missileIndex++;
                if (missileIndex >= Mathf.Max(1, C.Get(missilesPerMark)))
                { missileIndex = 0; targetIndex++; }
                fireTime = fixedAge + Mathf.Max(.02f, C.Get(launchInterval)) /
                    Mathf.Max(.1f, attackSpeedStat);
            }
            public override void OnExit()
            {
                foreach (Indicator marker in indicators.Values) marker.active = false;
                if (highlighted != null) highlighted.active = false;
                crosshair?.Dispose();
                if (characterBody)
                {
                    paintedTargetCounts.Remove(characterBody);
                    if (firedAny) nextPaintingUseTimes[characterBody] =
                        Time.time + Mathf.Max(0f, C.Get(secondaryReuseDelay));
                }
                indicators.Clear();
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        private static void SetupStickyBombProjectile(ProjectileController controller, CharacterBody owner)
        {
            // Reuse the already-networked vanilla projectile, changing ONLY its instance.
            // ProjectileManager supplies the NetworkIdentity and movement replication.
            foreach (MonoBehaviour behaviour in controller.GetComponents<MonoBehaviour>())
            {
                if (behaviour is MissileController || behaviour is ProjectileSimple ||
                    behaviour is ProjectileSphereTargetFinder || behaviour is ProjectileSteerTowardTarget ||
                    behaviour is ProjectileSingleTargetImpact)
                    behaviour.enabled = false;
            }
            Rigidbody body = controller.GetComponent<Rigidbody>();
            if (body)
            {
                body.isKinematic = false;
                body.useGravity = true;
                body.drag = 0f;
                body.velocity = controller.transform.forward * Mathf.Max(0f, C.Get(bombLaunchSpeed));
            }
            StickyBombFlight bomb = controller.gameObject.AddComponent<StickyBombFlight>();
            bomb.owner = owner;
            bomb.controller = controller;
            bomb.physicsBody = body;
            bomb.baseDamage = owner.damage * Mathf.Max(0f, C.Get(bombDamage)) / 100f;
            bomb.crit = owner.RollCrit();
            bomb.spawnTime = Time.time;
            activeStickyBombs[owner.gameObject] = bomb;
            if (NetworkServer.active && pendingStickyBombDetonations.Remove(owner.gameObject))
                bomb.detonateOnNextFixedUpdate = true;
            // Prevent premature collision with the drone firing the bomb.
            foreach (Collider projectileCollider in controller.GetComponentsInChildren<Collider>(true))
                foreach (Collider ownerCollider in owner.GetComponentsInChildren<Collider>(true))
                    if (projectileCollider && ownerCollider)
                        Physics.IgnoreCollision(projectileCollider, ownerCollider, true);
        }

        private static void SetStickyBombDetonationAvailable(GameObject bodyObject, bool available)
        {
            if (!bodyObject || !bombDetonate) return;
            CharacterBody body = bodyObject.GetComponent<CharacterBody>();
            if (!body || !body.skillLocator || !body.skillLocator.utility) return;
            GenericSkill slot = body.skillLocator.utility;
            if (available)
                slot.SetSkillOverride(body, bombDetonate, GenericSkill.SkillOverridePriority.Contextual);
            else
                slot.UnsetSkillOverride(body, bombDetonate, GenericSkill.SkillOverridePriority.Contextual);
        }

        // Access is per body. Client and server each track their own replicated projectile.
        // Server executes damage and physics; authority decides when to activate the skill.
        public sealed class StickyBombCast : BaseSkillState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                // Launch and detonation have different entity states. A held
                // button cannot re-enter launch and count as the second press.
                if (armedStickyBombOwners.Contains(gameObject)) return;
                armedStickyBombOwners.Add(gameObject);
                bombInputReleased.Remove(gameObject);
                stickyBombArmedTimes[gameObject] = Time.time;
                SetStickyBombDetonationAvailable(gameObject, true);

                    if (isAuthority)
                    {
                        GameObject projectile = MissilePrefab;
                        if (projectile && ProjectileManager.instance)
                        {
                            Vector3 direction = inputBank ? inputBank.aimDirection : transform.forward;
                            if (direction.sqrMagnitude < .001f) direction = transform.forward;
                            direction.Normalize();
                            Vector3 origin = Muzzle(this) + direction * 1.1f;
                            var shot = new FireProjectileInfo
                            {
                                projectilePrefab = projectile, owner = gameObject, position = origin,
                                rotation = Util.QuaternionSafeLookRotation(direction),
                                damage = 0f, force = 0f, crit = false,
                                speedOverride = -1f, fuseOverride = -1f, childFuseOverride = -1f,
                                damageTypeOverride = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Utility)
                            };
                            ProjectileManager.instance.FireProjectile(shot);
                        }
                    }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && fixedAge >= .08f) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class StickyBombDetonate : BaseSkillState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                float armedAt;
                if (!armedStickyBombOwners.Contains(gameObject) ||
                    !stickyBombArmedTimes.TryGetValue(gameObject, out armedAt)) return;
                // Only the controlling peer validates input release. Non-authority
                // peers execute the replicated state instead of examining stale input.
                if (isAuthority && (!bombInputReleased.Contains(gameObject) ||
                    Time.time - armedAt < Mathf.Max(0.01f, C.Get(bombDetonationDelay)))) return;

                armedStickyBombOwners.Remove(gameObject);
                bombInputReleased.Remove(gameObject);
                stickyBombArmedTimes.Remove(gameObject);
                SetStickyBombDetonationAvailable(gameObject, false);
                StickyBombFlight bomb;
                if (activeStickyBombs.TryGetValue(gameObject, out bomb) && bomb)
                    bomb.Detonate();
                else if (NetworkServer.active)
                    pendingStickyBombDetonations.Add(gameObject);
                if (skillLocator && skillLocator.utility)
                    skillLocator.utility.DeductStock(1);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && fixedAge >= .08f) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class StickyBombFlight : MonoBehaviour
        {
            internal CharacterBody owner;
            internal ProjectileController controller;
            internal Rigidbody physicsBody;
            internal float baseDamage;
            internal bool crit;
            internal float spawnTime;
            internal bool detonateOnNextFixedUpdate;
            private ProjectileGhostController scaledGhost;
            private Vector3 originalGhostScale;
            private bool stuck, detonated;
            private Transform attachedTo;
            private Vector3 attachedLocalPosition;
            private Quaternion attachedLocalRotation;

            private void FixedUpdate()
            {
                if (detonated) return;
                if (owner && armedStickyBombOwners.Contains(owner.gameObject) &&
                    owner.inputBank && !owner.inputBank.skill3.down)
                    bombInputReleased.Add(owner.gameObject);
                // The projectile ghost may be attached after the projectile's
                // initialization callback. Resize only the ghost, never its
                // physical collider or the shared prefab.
                if (controller && controller.ghost && controller.ghost != scaledGhost)
                {
                    RestoreGhostScale();
                    scaledGhost = controller.ghost;
                    originalGhostScale = scaledGhost.transform.localScale;
                }
                if (scaledGhost)
                    scaledGhost.transform.localScale = originalGhostScale * Mathf.Max(.01f, C.Get(bombVisualScale));
                // Preserve Rigidbody gravity for a natural ballistic arc while
                // counteracting part of the acceleration when configured below 1.
                if (!stuck && physicsBody && !physicsBody.isKinematic)
                {
                    float gravityMultiplier = Mathf.Max(0f, C.Get(bombGravityScale));
                    physicsBody.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
                }
                if (detonateOnNextFixedUpdate && NetworkServer.active)
                {
                    Detonate();
                    return;
                }
                if (stuck && attachedTo)
                {
                    transform.position = attachedTo.TransformPoint(attachedLocalPosition);
                    transform.rotation = attachedTo.rotation * attachedLocalRotation;
                }
                if (Time.time - spawnTime > Mathf.Max(1f, C.Get(bombLifetime)))
                {
                    if (owner)
                    {
                        armedStickyBombOwners.Remove(owner.gameObject);
                        bombInputReleased.Remove(owner.gameObject);
                        stickyBombArmedTimes.Remove(owner.gameObject);
                        SetStickyBombDetonationAvailable(owner.gameObject, false);
                    }
                    if (NetworkServer.active)
                    {
                        Detonate();
                        if (owner && owner.skillLocator && owner.skillLocator.utility)
                            owner.skillLocator.utility.DeductStock(1);
                    }
                }
            }
            private void OnCollisionEnter(Collision collision)
            {
                if (!NetworkServer.active || detonated || stuck || !collision.collider || collision.contactCount == 0)
                    return;
                Collider collider = collision.collider;
                if (owner && collider.transform.IsChildOf(owner.transform)) return;
                HurtBox hurt = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                bool terrain = (LayerIndex.world.mask & (1 << collider.gameObject.layer)) != 0;
                if (!hurt && !terrain) return;
                ContactPoint contact = collision.GetContact(0);
                Stick(collider, hurt, contact.point, contact.normal);
            }
            private void OnTriggerEnter(Collider other)
            {
                if (!NetworkServer.active || detonated || stuck || !other) return;
                if (owner && other.transform.IsChildOf(owner.transform)) return;
                HurtBox hurt = other.GetComponent<HurtBox>() ?? other.GetComponentInParent<HurtBox>();
                if (!hurt || !hurt.healthComponent) return;
                // Enemy hurtboxes can be triggers, so OnCollisionEnter alone is insufficient.
                Vector3 point = other.ClosestPoint(transform.position);
                Vector3 normal = transform.position - point;
                if (normal.sqrMagnitude < .0001f) normal = -transform.forward;
                Stick(other, hurt, point, normal.normalized);
            }
            private void Stick(Collider collider, HurtBox hurt, Vector3 point, Vector3 normal)
            {
                stuck = true;
                transform.position = point + normal * .06f;
                if (physicsBody)
                {
                    physicsBody.velocity = Vector3.zero;
                    physicsBody.angularVelocity = Vector3.zero;
                    physicsBody.isKinematic = true;
                }
                attachedTo = hurt && hurt.healthComponent ? hurt.healthComponent.transform : collider.transform;
                if (attachedTo)
                {
                    attachedLocalPosition = attachedTo.InverseTransformPoint(transform.position);
                    attachedLocalRotation = Quaternion.Inverse(attachedTo.rotation) * transform.rotation;
                }
            }
            internal void Detonate()
            {
                if (detonated) return;
                detonated = true;
                if (owner && activeStickyBombs.ContainsKey(owner.gameObject) &&
                    activeStickyBombs[owner.gameObject] == this)
                    activeStickyBombs.Remove(owner.gameObject);
                if (NetworkServer.active && owner)
                {
                    Vector3 origin = transform.position;
                    float radius = Mathf.Max(.1f, C.Get(bombRadius));
                    new BlastAttack
                    {
                        attacker = owner.gameObject,
                        inflictor = gameObject,
                        position = origin,
                        radius = radius,
                        baseDamage = baseDamage,
                        baseForce = Mathf.Max(0f, C.Get(bombEnemyForce)),
                        teamIndex = owner.teamComponent ? owner.teamComponent.teamIndex : TeamIndex.None,
                        crit = crit,
                        procCoefficient = Mathf.Max(0f, C.Get(bombProc)),
                        falloffModel = BlastAttack.FalloffModel.SweetSpot,
                        damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Utility),
                        attackerFiltering = AttackerFiltering.NeverHitSelf
                    }.Fire();
                    GameObject projectile = MissilePrefab;
                    ProjectileSingleTargetImpact impact = projectile ? projectile.GetComponent<ProjectileSingleTargetImpact>() : null;
                    SpawnMissileExplosion(impact ? impact.impactEffect : null,
                        origin, radius, Mathf.Max(.01f, C.Get(bombVfxScaleMultiplier)));
                }
                if (owner && (NetworkServer.active || Util.HasEffectiveAuthority(owner.gameObject)))
                {
                    Vector3 origin = transform.position;
                    float radius = Mathf.Max(.1f, C.Get(bombRadius));
                    float impulseStrength = Mathf.Max(0f, C.Get(bombSelfImpulse));
                    Vector3 offset = owner.corePosition - origin;
                    float distance = offset.magnitude;
                    if (distance < radius && impulseStrength > 0f)
                    {
                        Vector3 direction = distance > .15f ? offset / distance : owner.transform.forward;
                        direction = (direction + Vector3.up * .55f).normalized;
                        ApplyDroneVelocityImpulse(owner.gameObject, direction * impulseStrength *
                            Mathf.Lerp(0.75f, 1f, 1f - distance / radius));
                    }
                }
                if (NetworkServer.active) NetworkServer.Destroy(gameObject);
            }
            private void RestoreGhostScale()
            {
                // Effects can be pooled by the game: don't leave a changed
                // transform scale on a ghost reused by vanilla projectiles.
                if (scaledGhost) scaledGhost.transform.localScale = originalGhostScale;
                scaledGhost = null;
            }
            private void OnDestroy()
            {
                RestoreGhostScale();
                if (owner && activeStickyBombs.ContainsKey(owner.gameObject) &&
                    activeStickyBombs[owner.gameObject] == this)
                {
                    activeStickyBombs.Remove(owner.gameObject);
                    // Predicted projectiles are destroyed during server handoff.
                    // Their destruction must not reset the two-press state.
                }
            }
        }

        private static void ApplyDroneVelocityImpulse(GameObject body, Vector3 impulse)
        {
            if (!body) return;
            Component pseudo = body.GetComponent("PseudoCharacterMotor");
            if (pseudo)
            {
                try
                {
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                    PropertyInfo property = pseudo.GetType().GetProperty("velocityAuthority", flags);
                    if (property != null && property.CanRead && property.CanWrite && property.PropertyType == typeof(Vector3))
                    {
                        property.SetValue(pseudo, (Vector3)property.GetValue(pseudo, null) + impulse, null);
                        return;
                    }
                    FieldInfo field = pseudo.GetType().GetField("velocityAuthority", flags);
                    if (field != null && field.FieldType == typeof(Vector3))
                    {
                        field.SetValue(pseudo, (Vector3)field.GetValue(pseudo) + impulse);
                        return;
                    }
                }
                catch (Exception e)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Missile Sticky Bomb: failed to apply drone flight impulse: " + e.Message);
                }
            }
            CharacterMotor motor = body.GetComponent<CharacterMotor>();
            if (motor) { motor.velocity += impulse; return; }
            Rigidbody rigid = body.GetComponent<Rigidbody>();
            if (rigid && !rigid.isKinematic) rigid.AddForce(impulse, ForceMode.VelocityChange);
        }

        // A cancelled targeting state restores its Special skill while the cancel key
        // may still be held. Temporarily disable the slot until the player releases it,
        // so holding Special cannot immediately re-enter MissileBarrage.
        public sealed class BarrageCancelReleaseLock : MonoBehaviour
        {
            private GenericSkill specialSlot;
            private InputBankTest inputBank;
            private SkillDef disabledSkill;
            private bool applied;

            internal void Arm(GenericSkill slot, InputBankTest bank)
            {
                specialSlot = slot;
                inputBank = bank;
                disabledSkill = CharacterBody.CommonAssets.disabledSkill;
                if (specialSlot && disabledSkill)
                {
                    specialSlot.SetSkillOverride(this, disabledSkill,
                        GenericSkill.SkillOverridePriority.Contextual);
                    applied = true;
                }
                else
                {
                    Destroy(this);
                }
            }

            private void FixedUpdate()
            {
                // Do not unlock while the cancellation press remains held.
                if (!inputBank || !inputBank.skill4.down)
                    Destroy(this);
            }

            private void OnDestroy()
            {
                if (applied && specialSlot)
                    specialSlot.UnsetSkillOverride(this, disabledSkill,
                        GenericSkill.SkillOverridePriority.Contextual);
            }
        }

        public sealed class MissileBarrage : BaseSkillState
        {
            private Vector3 centre, right, up;
            private int fired;
            private float nextShot;
            private bool confirmed;
            private GameObject targetRing;
            private LineRenderer ringRenderer;
            private BarrageMarkerLifetime markerLifetime;
            private SkillDef primaryOverride;
            private SkillDef cancelOverride;
            private SkillDef disabledOverride;
            private bool specialReleased;
            public override void OnEnter()
            {
                base.OnEnter();
                if (!isAuthority) return;
                // Engineer's contextual dummy prevents primary fire while M1 confirms
                // the target location, as in the vanilla target designation UI.
                primaryOverride = barrageConfirm;
                if (primaryOverride && skillLocator && skillLocator.primary)
                    skillLocator.primary.SetSkillOverride(this, primaryOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                // Disable Secondary and Utility during designation. Special becomes cancel.
                disabledOverride = CharacterBody.CommonAssets.disabledSkill;
                cancelOverride = SkillCatalog.GetSkillDef(
                    SkillCatalog.FindSkillIndexByName("EngiCancelTargetingDummy"));
                if (skillLocator && disabledOverride)
                {
                    if (skillLocator.secondary) skillLocator.secondary.SetSkillOverride(this, disabledOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                    if (skillLocator.utility) skillLocator.utility.SetSkillOverride(this, disabledOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                }
                if (skillLocator && skillLocator.special && cancelOverride)
                    skillLocator.special.SetSkillOverride(this, cancelOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                // Huntress Arrow Rain exposes its real targeting visual through the
                // state configuration. Clone the prefab for this drone, never modify
                // Huntress's original asset or require an unverified resource path.
                GameObject arrowRainIndicator = EntityStates.Huntress.ArrowRain.areaIndicatorPrefab;
                if (arrowRainIndicator)
                {
                    targetRing = UnityEngine.Object.Instantiate(arrowRainIndicator);
                    targetRing.name = "3Qs Missile Barrage Area Indicator";
                    // ArrowRain.OnEnter applies the radius directly as localScale.
                    float radius = Mathf.Max(.1f, C.Get(barrageAreaRadius));
                    targetRing.transform.localScale = Vector3.one * radius;
                }
                else
                {
                    // Fall back to the pre-existing ring if Huntress's state
                    // configuration has not populated areaIndicatorPrefab yet.
                    targetRing = new GameObject("3Qs Missile Barrage Targeting Ring (Fallback)");
                    ringRenderer = targetRing.AddComponent<LineRenderer>();
                    ringRenderer.useWorldSpace = true;
                    ringRenderer.loop = true;
                    ringRenderer.positionCount = 49;
                    ringRenderer.startWidth = ringRenderer.endWidth = .15f;
                    ringRenderer.startColor = ringRenderer.endColor = new Color(1f, .55f, .18f, .85f);
                    ThreeQsVanillaTweaks.Log.Warning(
                        "Remote Missile Drone: Huntress ArrowRain.areaIndicatorPrefab is unavailable; using fallback targeting ring.");
                }
                markerLifetime = targetRing.AddComponent<BarrageMarkerLifetime>();
            }
            private void UpdateTarget()
            {
                Ray ray = GetAimRay();
                float distance = Mathf.Max(1f, C.Get(barrageRange));
                RaycastHit hit;
                bool worldHit = Physics.Raycast(ray, out hit, distance, LayerIndex.world.mask,
                    QueryTriggerInteraction.Ignore);
                centre = worldHit ? hit.point : ray.GetPoint(distance);
                Vector3 normal = worldHit ? hit.normal : Vector3.up;
                right = Vector3.Cross(normal, ray.direction).normalized;
                if (right.sqrMagnitude < .001f) right = Vector3.Cross(normal, Vector3.forward).normalized;
                if (right.sqrMagnitude < .001f) right = Vector3.right;
                up = Vector3.Cross(normal, right).normalized;
                if (targetRing && !ringRenderer)
                {
                    // Match ArrowRain.UpdateAreaIndicator: project onto world
                    // geometry and orient the visual to the surface normal.
                    targetRing.transform.position = centre;
                    targetRing.transform.up = normal;
                    targetRing.transform.localScale = Vector3.one * Mathf.Max(.1f, C.Get(barrageAreaRadius));
                }
                if (ringRenderer)
                {
                    float radius = Mathf.Max(.1f, C.Get(barrageAreaRadius));
                    for (int i = 0; i < 49; i++)
                    {
                        float angle = i * Mathf.PI * 2f / 48f;
                        ringRenderer.SetPosition(i, centre + normal * .15f +
                            (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
                    }
                }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority) return;
                if (!confirmed)
                {
                    // Do not treat the original Special press as a cancellation.
                    if (inputBank && !inputBank.skill4.down) specialReleased = true;
                    if (specialReleased && inputBank && inputBank.skill4.down)
                    {
                        // Keep Special unusable after leaving this state until the
                        // cancellation press has been released. The lock owns its
                        // override independently of the targeting state.
                        if (skillLocator && skillLocator.special)
                        {
                            BarrageCancelReleaseLock releaseLock =
                                gameObject.GetComponent<BarrageCancelReleaseLock>();
                            if (!releaseLock)
                                releaseLock = gameObject.AddComponent<BarrageCancelReleaseLock>();
                            releaseLock.Arm(skillLocator.special, inputBank);
                        }
                        outer.SetNextStateToMain();
                        return;
                    }
                    UpdateTarget();
                    // No automatic firing: only pressing M1 locks in the selected area.
                    if (!inputBank || !inputBank.skill1.justPressed) return;
                    confirmed = true;
                    // Restore the actual special SkillDef BEFORE spending stock.
                    // While the contextual cancel dummy is installed its zero-recharge
                    // definition prevents the slot from beginning the real cooldown.
                    RestoreSpecialOverride();
                    if (skillLocator && skillLocator.special)
                        skillLocator.special.DeductStock(1);
                    if (ringRenderer) ringRenderer.startColor = ringRenderer.endColor =
                        new Color(1f, .4f, .18f, .65f);
                }
                int count = Mathf.Max(1, C.Get(barrageCount));
                if (fired >= count)
                {
                    if (markerLifetime) markerLifetime.FinishLaunching();
                    outer.SetNextStateToMain();
                    return;
                }
                if (fixedAge < nextShot) return;
                float radius = Mathf.Max(0f, C.Get(barrageAreaRadius));
                Vector2 position = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 impact = centre + right * position.x + up * position.y;
                // The actual flight path is assigned through the projectile-init hook.
                // Always fire upward first; each missile travels above its individual
                // impact point before diving straight down into the designated area.
                Fire(this, DamageSource.Special, C.Get(specialDamage), null, Vector3.up, impact, centre, markerLifetime);
                fired++;
                nextShot = fixedAge + Mathf.Max(.02f, C.Get(barrageInterval)) /
                    Mathf.Max(.1f, attackSpeedStat);
            }
            private void RestoreSpecialOverride()
            {
                if (cancelOverride && skillLocator && skillLocator.special)
                {
                    skillLocator.special.UnsetSkillOverride(this, cancelOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                    cancelOverride = null;
                }
            }

            public override void OnExit()
            {
                if (primaryOverride && skillLocator && skillLocator.primary)
                    skillLocator.primary.UnsetSkillOverride(this, primaryOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                if (disabledOverride && skillLocator)
                {
                    if (skillLocator.secondary) skillLocator.secondary.UnsetSkillOverride(this, disabledOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                    if (skillLocator.utility) skillLocator.utility.UnsetSkillOverride(this, disabledOverride,
                        GenericSkill.SkillOverridePriority.Contextual);
                }
                RestoreSpecialOverride();
                if (targetRing && !confirmed)
                    UnityEngine.Object.Destroy(targetRing);
                else if (targetRing && markerLifetime)
                    markerLifetime.FinishLaunching();
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
    }
}
