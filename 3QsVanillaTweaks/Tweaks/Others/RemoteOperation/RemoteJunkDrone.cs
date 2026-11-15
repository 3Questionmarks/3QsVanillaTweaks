using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteJunkDrone
    {
        private const string CubeMachine = "3QSRemoteJunkCube", SalvageMachine = "3QSRemoteJunkSalvage",
            CloakMachine = "3QSRemoteJunkCloak", SurpriseMachine = "3QSRemoteJunkSurprise";
        internal static readonly Type[] States = { typeof(FireCube), typeof(Salvage), typeof(SlipAway), typeof(SurpriseState) };

        private static ConfigEntry<bool> enabled, primaryOn, secondaryOn, utilityOn, specialOn;
        private static ConfigEntry<bool> cubeRapid;
        private static ConfigEntry<float> cubeDamage, cubeProc, cubeRadius, cubeSpeed, cubeArc, cubeFuse, cubeForce, cubeCooldown,
            cubeCast, cubeAttackSpeedCooldownScaling;
        private static ConfigEntry<float> salvageRange, salvageAngle, salvageCooldown;
        private static ConfigEntry<bool> salvageCommon, salvageUncommon, salvageLegendary, salvageBoss;
        private static ConfigEntry<float> cloakSpeed, cloakDuration, cloakCooldown;
        private static ConfigEntry<bool> attacksBreakCloak;
        private static ConfigEntry<float> surpriseCooldown, surpriseDifficulty, surpriseForwardForce,
            surpriseDropRadius, surpriseDropHeight;
        private static ConfigEntry<int> surpriseMaximumItems, surpriseItemsPerTier;
        private static SkillDef primary, secondary, utility, special;
        private static BuffDef speedBuff;
        private static GameObject cubeExplosionEffect;
        private static string cubeExplosionSound;
        private static float observedCubeAnimationDuration = 1f;
        private static bool harmonyInstalled;

        internal static void Bind()
        {
            string s = RemoteDroneSkills.JunkSection;
            enabled = C.BindStartupBool(s, "Changes Enabled", true,
                "Add Junk Drone remote-operation skills. AI-controlled Junk Drones are unchanged.");
            primaryOn = C.BindStartupBool(s + " - Junk Cube", "Changes Enabled", true, "Add Junk Cube.");
            secondaryOn = C.BindStartupBool(s + " - Salvage", "Changes Enabled", true, "Add Salvage.");
            utilityOn = C.BindStartupBool(s + " - Slip Away", "Changes Enabled", true, "Add Slip Away.");
            specialOn = C.BindStartupBool(s + " - Surprise!", "Changes Enabled", true, "Move Surprise! to the special slot.");

            cubeDamage = RemoteDroneSkills.Number(s + " - Junk Cube", "Damage (%)", 600f, "Explosion damage.");
            cubeProc = RemoteDroneSkills.Number(s + " - Junk Cube", "Proc Coefficient", 0.8f, "Explosion proc coefficient.");
            cubeRadius = RemoteDroneSkills.Number(s + " - Junk Cube", "Explosion Radius (m)", 8f, "Explosion radius.");
            cubeSpeed = RemoteDroneSkills.Number(s + " - Junk Cube", "Launch Speed (m/s)", 60f, "Forward launch speed.");
            cubeArc = RemoteDroneSkills.Number(s + " - Junk Cube", "Upward Launch Speed (m/s)", 10f, "Additional upward speed that gives the cube an arc.");
            cubeFuse = RemoteDroneSkills.Number(s + " - Junk Cube", "Fuse (s)", 3f, "Time before the cube explodes if it has not hit anything.");
            cubeForce = RemoteDroneSkills.Number(s + " - Junk Cube", "Force", 800f, "Force applied by the explosion.");
            cubeCooldown = RemoteDroneSkills.Number(s + " - Junk Cube", "Cooldown (s)", 5f, "Base cooldown of the skill.");
            cubeRapid = C.BindBool(s + " - Junk Cube", "Rapid", true,
                "Reduce the cooldown with attack speed.");
            cubeAttackSpeedCooldownScaling = RemoteDroneSkills.Number(s + " - Junk Cube",
                "Attack Speed Cooldown Scaling (%)", 50f,
                "Effectiveness of attack speed at reducing the cooldown while Rapid is enabled.");
            cubeCast = RemoteDroneSkills.Number(s + " - Junk Cube", "Attack Animation Duration (s)", -1f,
                "Delay between launches. -1 uses the full duration of the Junk Drone's opening animation.");

            salvageRange = RemoteDroneSkills.Number(s + " - Salvage", "Target Range (m)", 60f, "Maximum distance to an item pickup.");
            salvageAngle = RemoteDroneSkills.Number(s + " - Salvage", "Target Angle (degrees)", 15f, "Maximum angle from the crosshair to an item pickup.");
            salvageCooldown = RemoteDroneSkills.Number(s + " - Salvage", "Cooldown (s)", 20f, "Salvage cooldown.");
            salvageCommon = C.BindBool(s + " - Salvage", "Allow Common Items", true, "Allow Common items to become White Scrap.");
            salvageUncommon = C.BindBool(s + " - Salvage", "Allow Uncommon Items", true, "Allow Uncommon items to become Green Scrap.");
            salvageLegendary = C.BindBool(s + " - Salvage", "Allow Legendary Items", true, "Allow Legendary items to become Red Scrap.");
            salvageBoss = C.BindBool(s + " - Salvage", "Allow Boss Items", true, "Allow Boss items to become Yellow Scrap.");

            cloakSpeed = RemoteDroneSkills.Number(s + " - Slip Away", "Movement Speed (%)", 50f, "Movement speed gained while cloaked.");
            cloakDuration = RemoteDroneSkills.Number(s + " - Slip Away", "Duration (s)", 4f, "Cloak and movement speed duration.");
            cloakCooldown = RemoteDroneSkills.Number(s + " - Slip Away", "Cooldown (s)", 10f, "Slip Away cooldown.");
            attacksBreakCloak = C.BindBool(s + " - Slip Away", "Attacking Breaks Cloak", false, "End Slip Away when another combat skill is activated.");

            surpriseCooldown = RemoteDroneSkills.Number(s + " - Surprise!", "Cooldown (s)", 120f, "Surprise! cooldown. Vanilla: 120s.");
            surpriseMaximumItems = RemoteDroneSkills.Number(s + " - Surprise!", "Maximum Items", 5, "Maximum temporary items dropped. Vanilla: 5.");
            surpriseDifficulty = RemoteDroneSkills.Number(s + " - Surprise!", "Items Per Difficulty Level", .7f, "Items dropped per ambient difficulty level before limits. Vanilla: 0.7.");
            surpriseItemsPerTier = RemoteDroneSkills.Number(s + " - Surprise!", "Extra Items Per Drone Tier", 2, "Extra items dropped per drone upgrade tier. (Doesn't do anything for actually cuz you cant level up a player controlled drone) Vanilla: 2.");
            surpriseForwardForce = RemoteDroneSkills.Number(s + " - Surprise!", "Forward Drop Force", 20f, "How far forward dropped items are launched. Vanilla: 20.");
            surpriseDropRadius = RemoteDroneSkills.Number(s + " - Surprise!", "Drop Radius Per Item", .6f, "Spacing between dropped items. Vanilla: 0.6.");
            surpriseDropHeight = RemoteDroneSkills.Number(s + " - Surprise!", "Drop Height", 7f, "Upward velocity of dropped items. Vanilla: 7.");
        }

        internal static void RegisterProjectiles(ContentPack content)
        {
            // The large Junk Cube explosion is loaded lazily after the entity
            // state catalogue has applied its vanilla state configuration.
        }

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("JunkCube", "Junk Cube", "Launch an explosive Junk Cube.",
                typeof(FireCube), CubeMachine, cubeCooldown.Value, null, customDefinition: ScriptableObject.CreateInstance<JunkCubeSkillDef>());
            secondary = RemoteDroneSkills.MakeSkill("JunkSalvage", "Salvage", "Turn a targeted item into scrap of the same rarity.", typeof(Salvage), SalvageMachine, salvageCooldown.Value, null);
            utility = RemoteDroneSkills.MakeSkill("JunkSlipAway", "Slip Away", "Cloak and move faster for a short duration.", typeof(SlipAway), CloakMachine, cloakCooldown.Value, null);
            utility.cancelSprintingOnActivation = false;
            utility.isCombatSkill = false;
            special = RemoteDroneSkills.MakeSkill("JunkSurprise", "Surprise!", "Drop temporary items based on the current difficulty.", typeof(SurpriseState), SurpriseMachine, surpriseCooldown.Value, null);
            R2API.LanguageAPI.Add("KEYWORD_RAPID",
                "<style=cKeywordName>Rapid</style><style=cSub>Cooldown is reduced by attack speed.</style>");
            speedBuff = ScriptableObject.CreateInstance<BuffDef>();
            speedBuff.name = "3QSRemoteJunkSlipAwaySpeed";
            speedBuff.buffColor = new Color(.62f, .75f, .68f);
            speedBuff.canStack = false;
            speedBuff.isDebuff = false;
            Refresh();
        }

        internal static BuffDef[] Buffs => enabled.Value && speedBuff ? new[] { speedBuff } : Array.Empty<BuffDef>();

        private sealed class JunkCubeSkillDef : SkillDef
        {
            public override bool CanExecute(GenericSkill skillSlot)
            {
                CubeUseLockout lockout = GetLockout(skillSlot);
                return (!lockout || Time.fixedTime >= lockout.nextAllowedTime) && base.CanExecute(skillSlot);
            }

            public override bool IsReady(GenericSkill skillSlot)
            {
                CubeUseLockout lockout = GetLockout(skillSlot);
                return (!lockout || Time.fixedTime >= lockout.nextAllowedTime) && base.IsReady(skillSlot);
            }

            public override void OnExecute(GenericSkill skillSlot)
            {
                int stockBeforeUse = skillSlot.stock;
                CubeUseLockout lockout = GetLockout(skillSlot);
                if (lockout) lockout.nextAllowedTime = Time.fixedTime + CubeMinimumAttackDelay();
                base.OnExecute(skillSlot);
                // Always spend exactly one charge, irrespective of an older
                // generated Stocks Consumed value.
                skillSlot.stock = Mathf.Max(0, stockBeforeUse - 1);
            }

            private static CubeUseLockout GetLockout(GenericSkill skillSlot)
            {
                if (!skillSlot) return null;
                CubeUseLockout result = skillSlot.GetComponent<CubeUseLockout>();
                if (!result) result = skillSlot.gameObject.AddComponent<CubeUseLockout>();
                return result;
            }
        }

        private sealed class CubeUseLockout : MonoBehaviour
        {
            internal float nextAllowedTime;
        }

        private static float CubeReleaseTime()
        {
            float releaseTime = EntityStates.Drone.DroneJunk.Surprise.dropDelay;
            return Mathf.Max(.05f, releaseTime > 0f ? releaseTime : 1f);
        }

        private static float CubeMinimumAttackDelay()
        {
            float configuredDuration = C.Get(cubeCast);
            float configuredMinimum = configuredDuration < 0f ? 0f : configuredDuration;
            return Mathf.Max(CubeReleaseTime(), observedCubeAnimationDuration, configuredMinimum);
        }

        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = Mathf.Max(0f, C.Get(cubeCooldown));
            primary.attackSpeedBuffsRestockSpeed = C.Get(cubeRapid);
            primary.attackSpeedBuffsRestockSpeed_Multiplier = Mathf.Max(0f,
                C.Get(cubeAttackSpeedCooldownScaling)) / 100f;
            primary.keywordTokens = C.Get(cubeRapid) ? new[] { "KEYWORD_RAPID" } : Array.Empty<string>();
            // Junk Cube spends and restores one configured charge at a time.
            primary.requiredStock = 1;
            primary.stockToConsume = 1;
            secondary.baseRechargeInterval = Mathf.Max(0f, C.Get(salvageCooldown));
            utility.baseRechargeInterval = Mathf.Max(0f, C.Get(cloakCooldown));
            special.baseRechargeInterval = Mathf.Max(0f, C.Get(surpriseCooldown));
            EntityStates.Drone.DroneJunk.Surprise.maxItemCount = Mathf.Max(1, C.Get(surpriseMaximumItems));
            EntityStates.Drone.DroneJunk.Surprise.itemsToDropCoefficient = Mathf.Max(0f, C.Get(surpriseDifficulty));
            EntityStates.Drone.DroneJunk.Surprise.extraItemsPerTier = Mathf.Max(0, C.Get(surpriseItemsPerTier));
            EntityStates.Drone.DroneJunk.Surprise.forwardDropForce = C.Get(surpriseForwardForce);
            EntityStates.Drone.DroneJunk.Surprise.dropRadiusCoefficient = Mathf.Max(0f, C.Get(surpriseDropRadius));
            EntityStates.Drone.DroneJunk.Surprise.dropHeight = C.Get(surpriseDropHeight);

            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"{(C.Get(cubeRapid) ? "<style=cIsUtility>Rapid.</style> " : "")}Launch an explosive Junk Cube that detonates on impact or after <style=cIsUtility>{C.Get(cubeFuse):0.##} seconds</style>, dealing <style=cIsDamage>{C.Get(cubeDamage):0.##}% damage</style> to nearby enemies.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Convert a targeted item into <style=cIsUtility>scrap of the same rarity</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"<style=cIsUtility>Cloak</style> and increase movement speed by <style=cIsUtility>{C.Get(cloakSpeed):0.##}%</style> for <style=cIsUtility>{C.Get(cloakDuration):0.##} seconds</style>.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"Drop up to <style=cIsUtility>{C.Get(surpriseMaximumItems)} temporary items</style>, scaling with the current difficulty.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary,
                () => $"\nExplosion Radius: {C.Get(cubeRadius):0.##}m\nFuse: {C.Get(cubeFuse):0.##}s\nStocks Recharged: {primary.rechargeStock}", () => C.Get(cubeProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary,
                () => $"\nTarget Range: {C.Get(salvageRange):0.##}m", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility,
                () => $"\nCloak Duration: {C.Get(cloakDuration):0.##}s\nMovement Speed: +{C.Get(cloakSpeed):0.##}%", () => 0f);
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special,
                () => $"\nMaximum Items: {C.Get(surpriseMaximumItems)}\nItems per Tier: {C.Get(surpriseItemsPerTier)}", () => 0f);
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !primary) return;
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            SkillDef native = locator && locator.primary && locator.primary.skillFamily && locator.primary.skillFamily.variants.Length > 0
                ? locator.primary.skillFamily.variants[0].skillDef : null;
            Sprite fallback = native ? native.icon : null;
            primary.icon = RemoteDroneSkills.VanillaIcon("DrifterBody", "Secondary", fallback, 1);
            secondary.icon = RemoteDroneSkills.VanillaIcon("DrifterBody", "Utility", fallback);
            utility.icon = RemoteDroneSkills.VanillaIcon("Bandit2Body", "Utility", fallback);
            special.icon = fallback;
            if (RoR2Content.Buffs.CloakSpeed) speedBuff.iconSprite = RoR2Content.Buffs.CloakSpeed.iconSprite;

            if (primaryOn.Value) { RemoteDroneSkills.AddMachine(prefab, CubeMachine); RemoteDroneSkills.SetSkill(prefab, "Primary", primary); }
            if (secondaryOn.Value) { RemoteDroneSkills.AddMachine(prefab, SalvageMachine); RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary); }
            if (utilityOn.Value) { RemoteDroneSkills.AddMachine(prefab, CloakMachine); RemoteDroneSkills.SetSkill(prefab, "Utility", utility); }
            if (specialOn.Value) { RemoteDroneSkills.AddMachine(prefab, SurpriseMachine); RemoteDroneSkills.SetSkill(prefab, "Special", special); }
            if (secondaryOn.Value && !prefab.GetComponent<SalvageIndicator>()) prefab.AddComponent<SalvageIndicator>();

            if (!harmonyInstalled)
            {
                harmonyInstalled = true;
                var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteJunkDrone");
                harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)),
                    postfix: new HarmonyMethod(typeof(RemoteJunkDrone), nameof(AfterStats)));
                harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                    postfix: new HarmonyMethod(typeof(RemoteJunkDrone), nameof(AfterAvailability)));
                harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)),
                    postfix: new HarmonyMethod(typeof(RemoteJunkDrone), nameof(AfterAvailability)));
                harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.OnSkillActivated)),
                    postfix: new HarmonyMethod(typeof(RemoteJunkDrone), nameof(AfterSkillActivated)));
            }
        }

        private static void AfterStats(CharacterBody __instance)
        {
            if (speedBuff && __instance.HasBuff(speedBuff))
                __instance.moveSpeed *= 1f + Mathf.Max(0f, C.Get(cloakSpeed)) / 100f;
        }

        private static void AfterSkillActivated(CharacterBody __instance, GenericSkill skill)
        {
            if (!NetworkServer.active || !C.Get(attacksBreakCloak) || !speedBuff || !__instance.HasBuff(speedBuff) ||
                !skill || skill.skillDef == utility || !skill.skillDef.isCombatSkill) return;
            __instance.ClearTimedBuffs(RoR2Content.Buffs.Cloak);
            __instance.ClearTimedBuffs(speedBuff);
        }

        private static void AfterAvailability(GenericSkill __instance, ref bool __result)
        {
            if (!__result) return;
            if (__instance.skillDef == special)
            {
                __result = !SurpriseDisabledHere();
                return;
            }
            if (__instance.skillDef != secondary) return;
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            InputBankTest input = __instance.GetComponent<InputBankTest>();
            __result = body && input && FindSalvageTarget(input.GetAimRay()) != null;
        }

        private static bool SurpriseDisabledHere()
        {
            SceneDef scene = SceneCatalog.GetSceneDefForCurrentScene();
            string name = scene ? scene.baseSceneName : UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            return string.Equals(name, "bazaar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "bazaarbetween", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "computationalexchange", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TierAllowed(ItemTier tier) =>
            (tier == ItemTier.Tier1 && C.Get(salvageCommon)) ||
            (tier == ItemTier.Tier2 && C.Get(salvageUncommon)) ||
            (tier == ItemTier.Tier3 && C.Get(salvageLegendary)) ||
            (tier == ItemTier.Boss && C.Get(salvageBoss));

        private static PickupIndex ScrapFor(GenericPickupController pickup)
        {
            if (!pickup || pickup.pickup.isTempItem || pickup.pickup.isRental || pickup.GetComponentInParent<ShopTerminalBehavior>() ||
                pickup.GetComponentInParent<PickupPickerController>()) return PickupIndex.none;
            PickupDef def = PickupCatalog.GetPickupDef(pickup.pickup.pickupIndex);
            ItemDef item = def == null || def.itemIndex == ItemIndex.None ? null : ItemCatalog.GetItemDef(def.itemIndex);
            if (!item || item.hidden || item.isConsumed || !item.canRemove || item.ContainsTag(ItemTag.ObjectiveRelated) ||
                item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap) || !TierAllowed(item.tier)) return PickupIndex.none;
            return PickupCatalog.FindScrapIndexForItemTier(item.tier);
        }

        private static GenericPickupController FindSalvageTarget(Ray aim)
        {
            float range = Mathf.Max(0f, C.Get(salvageRange));
            float angle = Mathf.Clamp(C.Get(salvageAngle), 0f, 180f);
            GenericPickupController best = null;
            float bestAngle = angle, bestDistance = range * range;
            foreach (GenericPickupController pickup in InstanceTracker.GetInstancesList<GenericPickupController>())
            {
                if (!pickup || !pickup.gameObject.activeInHierarchy || ScrapFor(pickup) == PickupIndex.none) continue;
                Vector3 offset = pickup.transform.position - aim.origin;
                float distance = offset.sqrMagnitude;
                float candidateAngle = Vector3.Angle(aim.direction, offset);
                if (distance > range * range || candidateAngle > bestAngle ||
                    (Mathf.Approximately(candidateAngle, bestAngle) && distance >= bestDistance)) continue;
                best = pickup; bestAngle = candidateAngle; bestDistance = distance;
            }
            return best;
        }

        private sealed class SalvageIndicator : MonoBehaviour
        {
            private CharacterBody body;
            private InputBankTest input;
            private Indicator indicator;
            private GameObject configuredVisualizer;
            private void Awake()
            {
                body = GetComponent<CharacterBody>(); input = GetComponent<InputBankTest>();
                indicator = new Indicator(gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator"));
            }
            private void FixedUpdate()
            {
                if (indicator == null) return;
                GenericSkill salvageSkill = body && body.skillLocator ? body.skillLocator.secondary : null;
                indicator.active = body && body.hasEffectiveAuthority && salvageSkill &&
                    salvageSkill.skillDef == secondary && salvageSkill.stock > 0;
                ConfigureBinding();
                GenericPickupController target = indicator.active && input ? FindSalvageTarget(input.GetAimRay()) : null;
                indicator.targetTransform = target ? target.transform : null;
            }
            private void ConfigureBinding()
            {
                GameObject visualizer = indicator.visualizerInstance;
                if (!visualizer || visualizer == configuredVisualizer) return;
                configuredVisualizer = visualizer;
                foreach (InputBindingDisplayController binding in
                    visualizer.GetComponentsInChildren<InputBindingDisplayController>(true))
                {
                    binding.actionName = "SecondarySkill";
                    // Re-enabling refreshes the displayed glyph immediately and
                    // keeps it responsive to the player's current key bindings.
                    binding.enabled = false;
                    binding.enabled = true;
                }
            }
            private void OnDisable()
            {
                configuredVisualizer = null;
                if (indicator != null) indicator.active = false;
            }
        }

        public sealed class FireCube : BaseState
        {
            private bool fired;
            private bool animationMeasured;
            private float releaseTime;
            private float duration;
            public override void OnEnter()
            {
                base.OnEnter();
                PlayCrossfade("Gesture, Override", "BombsAway", .2f);
                releaseTime = CubeReleaseTime();
                float clipDuration = GetAnimationDuration();
                if (clipDuration > 0f)
                {
                    animationMeasured = true;
                    observedCubeAnimationDuration = clipDuration;
                }
                duration = CubeMinimumAttackDelay();
                ExtendLockout();
            }

            private float GetAnimationDuration()
            {
                Animator animator = GetModelAnimator();
                if (!animator) return 0f;
                int layer = animator.GetLayerIndex("Gesture, Override");
                if (layer < 0) return 0f;
                AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(layer);
                if (next.IsName("BombsAway")) return next.length;
                AnimatorStateInfo current = animator.GetCurrentAnimatorStateInfo(layer);
                return current.IsName("BombsAway") ? current.length : 0f;
            }

            private void ExtendLockout()
            {
                CubeUseLockout lockout = GetComponent<CubeUseLockout>();
                if (lockout) lockout.nextAllowedTime = Mathf.Max(lockout.nextAllowedTime,
                    Time.fixedTime + Mathf.Max(0f, duration - fixedAge));
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!animationMeasured)
                {
                    float clipDuration = GetAnimationDuration();
                    if (clipDuration > 0f)
                    {
                        animationMeasured = true;
                        observedCubeAnimationDuration = clipDuration;
                        duration = CubeMinimumAttackDelay();
                        ExtendLockout();
                    }
                }
                // Release at the native opening frame, but retain the state and
                // lockout until the complete animation has finished.
                if (!fired && fixedAge >= releaseTime)
                {
                    fired = true;
                    if (NetworkServer.active) SpawnCube(GetAimRay());
                }
                if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
            }
            private void SpawnCube(Ray aim)
            {
                CharacterBody prefab = DLC3Content.BodyPrefabs.JunkCubePrefab;
                if (!prefab)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Remote Junk Drone: Drifter Junk Cube body is unavailable.");
                    return;
                }
                string spawnChild = EntityStates.Drone.DroneJunk.Surprise.itemSpawnLocationString;
                Transform muzzle = (!string.IsNullOrEmpty(spawnChild) ? FindModelChild(spawnChild) : null) ??
                    FindModelChild("Muzzle") ?? transform;
                Vector3 origin = muzzle.position + aim.direction * .5f;
                GameObject cube = UnityEngine.Object.Instantiate(prefab.gameObject, origin,
                    Util.QuaternionSafeLookRotation(aim.direction));
                TeamIndex team = teamComponent ? teamComponent.teamIndex : TeamIndex.Player;
                TeamFilter filter = cube.GetComponent<TeamFilter>();
                if (filter) filter.teamIndex = team;
                ProjectileController controller = cube.GetComponent<ProjectileController>();
                if (controller)
                {
                    controller.Networkowner = gameObject;
                    controller.IgnoreCollisionsWithOwner(true);
                }
                HealthComponent health = cube.GetComponent<HealthComponent>();
                if (health) health.godMode = true;
                var detonation = cube.AddComponent<RemoteCubeDetonation>();
                detonation.owner = gameObject;
                detonation.team = team;
                detonation.damage = damageStat * Mathf.Max(0f, C.Get(cubeDamage)) / 100f;
                detonation.crit = RollCrit();
                detonation.launchVelocity = aim.direction.normalized * Mathf.Max(.1f, C.Get(cubeSpeed)) +
                    Vector3.up * C.Get(cubeArc);
                Rigidbody body = cube.GetComponent<Rigidbody>();
                if (body) body.velocity = detonation.launchVelocity;
                NetworkServer.Spawn(cube);
                Util.PlaySound("Play_drone_attack", gameObject);
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }

        private sealed class RemoteCubeDetonation : MonoBehaviour
        {
            internal GameObject owner;
            internal TeamIndex team;
            internal float damage;
            internal bool crit;
            internal Vector3 launchVelocity;
            private float age;
            private bool exploded;
            private bool launchApplied;
            private Vector3 previousPosition;

            private void Awake()
            {
                previousPosition = transform.position;
            }

            private void Start()
            {
                // The native controller bounces Junk Cubes from entities. These
                // launched remote-operation cubes instead detonate on contact.
                JunkCubeController nativeController = GetComponent<JunkCubeController>();
                if (!nativeController) return;
                if (nativeController.triggerEvent && nativeController.triggerEvent.onTriggerEnter != null)
                    nativeController.triggerEvent.onTriggerEnter.RemoveListener(nativeController.OnEntityCollision);
                nativeController.enabled = false;
            }

            private void OnCollisionEnter(Collision collision)
            {
                if (NetworkServer.active && !exploded && collision != null && IsEnemy(collision.collider)) Explode();
            }

            private void OnTriggerEnter(Collider other)
            {
                if (NetworkServer.active && !exploded && IsEnemy(other)) Explode();
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active || exploded) return;
                age += Time.fixedDeltaTime;
                // Apply once after the cube's native spawn state has initialized,
                // then allow gravity to produce a natural ballistic arc.
                if (!launchApplied)
                {
                    launchApplied = true;
                    Rigidbody body = GetComponent<Rigidbody>();
                    if (body) body.velocity = launchVelocity;
                    CharacterMotor motor = GetComponent<CharacterMotor>();
                    if (motor) motor.velocity = launchVelocity;
                }
                Vector3 currentPosition = transform.position;
                bool enemy = HitEnemyBetween(previousPosition, currentPosition);
                previousPosition = currentPosition;
                if (enemy || age >= Mathf.Max(.05f, C.Get(cubeFuse))) Explode();
            }

            private bool HitEnemyBetween(Vector3 start, Vector3 end)
            {
                foreach (Collider candidate in Physics.OverlapCapsule(start, end, 1.5f,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
                {
                    if (IsEnemy(candidate)) return true;
                }
                return false;
            }

            private bool IsEnemy(Collider candidate)
            {
                if (!candidate) return false;
                HurtBox hurtBox = candidate.GetComponent<HurtBox>() ?? candidate.GetComponentInParent<HurtBox>();
                HealthComponent target = hurtBox ? hurtBox.healthComponent :
                    candidate.GetComponent<HealthComponent>() ?? candidate.GetComponentInParent<HealthComponent>();
                if (!target)
                {
                    EntityLocator locator = candidate.GetComponent<EntityLocator>() ??
                        candidate.GetComponentInParent<EntityLocator>();
                    if (locator && locator.entity) target = locator.entity.GetComponent<HealthComponent>();
                }
                return target && target.alive && target.gameObject != gameObject &&
                    FriendlyFireManager.ShouldDirectHitProceed(target, team);
            }

            private void Explode()
            {
                if (exploded) return;
                exploded = true;
                LoadCubeExplosion();
                if (cubeExplosionEffect)
                    EffectManager.SpawnEffect(cubeExplosionEffect,
                        new EffectData { origin = transform.position, rotation = Quaternion.identity }, true);
                if (!string.IsNullOrEmpty(cubeExplosionSound)) Util.PlaySound(cubeExplosionSound, gameObject);
                else Util.PlaySound("Play_item_lunar_specialReplace_explode", gameObject);
                new BlastAttack {
                    attacker = owner, inflictor = gameObject, teamIndex = team,
                    position = transform.position, radius = Mathf.Max(0f, C.Get(cubeRadius)),
                    baseDamage = damage, baseForce = Mathf.Max(0f, C.Get(cubeForce)), crit = crit,
                    procCoefficient = Mathf.Max(0f, C.Get(cubeProc)), falloffModel = BlastAttack.FalloffModel.None,
                    damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Primary),
                    attackerFiltering = AttackerFiltering.NeverHitSelf
                }.Fire();
                NetworkServer.Destroy(gameObject);
            }
        }

        private static void LoadCubeExplosion()
        {
            if (cubeExplosionEffect || !string.IsNullOrEmpty(cubeExplosionSound)) return;
            var state = new EntityStates.JunkCube.DeathStateLarge();
            EntityStateCatalog.InitializeStateFields(state);
            object wrappedEffect = AccessTools.Field(typeof(EntityStates.JunkCube.DeathState),
                "explosionEffectPrefab")?.GetValue(state);
            if (wrappedEffect != null)
            {
                MethodInfo conversion = wrappedEffect.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "op_Implicit" &&
                        method.ReturnType == typeof(GameObject));
                if (conversion != null)
                    cubeExplosionEffect = conversion.Invoke(null, new[] { wrappedEffect }) as GameObject;
            }
            cubeExplosionSound = state.deathSoundString;
        }

        public sealed class Salvage : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                GenericPickupController target = FindSalvageTarget(GetAimRay());
                if (NetworkServer.active && target)
                {
                    PickupIndex scrap = ScrapFor(target);
                    if (scrap != PickupIndex.none)
                    {
                        target.Network_pickupState = new UniquePickup(scrap);
                        target.ForcePickupDisplayUpdate();
                        GameObject effect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/OmniEffect/OmniRecycleEffect");
                        Transform display = target.pickupDisplay ? target.pickupDisplay.transform : target.transform;
                        if (effect) EffectManager.SimpleEffect(effect, display.position, Quaternion.identity, true);
                    }
                }
                if (isAuthority)
                {
                    if (target) Util.PlaySound("Play_GG_CHAR_Drifter_AltSec_JumpPad_Create", gameObject);
                    outer.SetNextStateToMain();
                }
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class SlipAway : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (NetworkServer.active)
                {
                    float duration = Mathf.Max(0f, C.Get(cloakDuration));
                    characterBody.AddTimedBuff(RoR2Content.Buffs.Cloak, duration);
                    characterBody.AddTimedBuff(speedBuff, duration);
                }
                if (!string.IsNullOrEmpty(EntityStates.Bandit2.StealthMode.enterStealthSound))
                    Util.PlaySound(EntityStates.Bandit2.StealthMode.enterStealthSound, gameObject);
                if (EntityStates.Bandit2.StealthMode.smokeBombEffectPrefab)
                    EffectManager.SimpleMuzzleFlash(EntityStates.Bandit2.StealthMode.smokeBombEffectPrefab, gameObject, "Muzzle", false);
                if (isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class SurpriseState : EntityStates.Drone.DroneJunk.Surprise
        {
            public override void OnEnter()
            {
                base.OnEnter();
                // Vanilla Surprise already plays its own cast sound.
            }
            public override void OnExit()
            {
                // Vanilla Surprise! explicitly recharges Primary. It now belongs to Special.
                GenericSkill original = skillLocator.primary;
                skillLocator.primary = skillLocator.special;
                try { base.OnExit(); }
                finally { skillLocator.primary = original; }
            }
        }
    }
}
