using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using RoR2.AsyncManagement;
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
    internal static class RemoteCleanupDrone
    {
        private const string VacuumMachine = "3QSRemoteCleanupVacuum", CannonMachine = "3QSRemoteCleanupCannon",
            BoostMachine = "3QSRemoteCleanupBoost", MassMachine = "3QSRemoteCleanupMass";
        internal static readonly Type[] States = { typeof(Vacuum), typeof(CancelVacuum), typeof(TrashCannon), typeof(SuctionBoost), typeof(MassRecycling) };
        private static ConfigEntry<bool> enabled, vacuumOn, cannonOn, boostOn, massOn;
        private static ConfigEntry<float> vacuumDps, vacuumRadius, vacuumPull, vacuumSlow, vacuumSpeedPenalty,
            vacuumMaxDuration, vacuumMinCooldown, vacuumMaxCooldown, vacuumProc, vacuumTrashRecovery,
            vacuumBoostRecovery, cannonDirect, cannonBlast, cannonRadius, cannonSpeed, cannonInterval, cannonSpread,
            cannonDirectProc, cannonBlastProc, boostLaunchSpeed, boostKnockback, boostDamage, boostRange, boostHalfAngle, boostProc,
            massThreshold, massRange, massCapture, massFailedCooldown, massCooldown, massBaseProcess, massHealthDivisor,
            massMaxProcess, massSpeedPenalty, smallThreshold, mediumThreshold, largeThreshold;
        private static ConfigEntry<int> cannonCount, cannonStarting, boostStarting, smallGold, smallHeal, smallAmmo, mediumGold, mediumHeal,
            mediumAmmo, largeGold, largeHeal, largeAmmo, hugeGold, hugeHeal, hugeAmmo;
        private static ConfigEntry<float> smallBoost, mediumBoost, largeBoost, hugeBoost;
        private static SkillDef primary, vacuumCancel, secondary, utility, special;
        private static GameObject cannonPrefab;
        private static BuffDef vacuumSlowBuff;
        private static GameObject nativeRecyclingSuckEffect;
        private static EntityStateGameObject rexSonicBoomEffect;
        private static string rexSonicBoomSound;
        private static readonly Collider[] targets = new Collider[128];
        private static readonly Collider[] sonicTargets = new Collider[1024];
        private static bool hooked;
        private static readonly Dictionary<EntityStates.Drone.Cleanup, float> nativeVacuumTicks = new Dictionary<EntityStates.Drone.Cleanup, float>();
        private static readonly Dictionary<EntityStates.Drone.Cleanup, float> nativeVacuumAge = new Dictionary<EntityStates.Drone.Cleanup, float>();
        private static readonly HashSet<HealthComponent> nativeVacuumHits = new HashSet<HealthComponent>();

        internal static void Bind()
        {
            string s = RemoteDroneSkills.CleanupSection;
            enabled = C.BindStartupBool(s, "Changes Enabled", true,
                "Add Cleanup Drone remote-operation abilities. AI Cleanup Drones are unchanged.");
            vacuumOn = C.BindStartupBool(s + " - Vacuum", "Changes Enabled", true, "Apply Changes to primary attack.");
            cannonOn = C.BindStartupBool(s + " - Trash Cannon", "Changes Enabled", true, "Add Trash Cannon.");
            boostOn = C.BindStartupBool(s + " - Suction Boost", "Changes Enabled", true, "Add Suction Boost.");
            massOn = C.BindStartupBool(s + " - Mass Recycling", "Changes Enabled", true, "Add Mass Recycling.");
            string v = s + " - Vacuum", t = s + " - Trash Cannon", b = s + " - Suction Boost", m = s + " - Mass Recycling";
            vacuumDps = N(v, "Damage Per Second (%)", 80f, "Total damage each second while enemies remain in Vacuum.");
            vacuumRadius = N(v, "Radius (m)", 24f, "Radius of the circular vacuum beneath the drone.");
            vacuumPull = N(v, "Pull Speed (m/s)", 10f, "Maximum additional movement applied towards the vacuum centre each second.");
            vacuumSlow = N(v, "Enemy Slow (%)", 50f, "Slow strength of enemies in the vacuum.");
            vacuumSpeedPenalty = N(v, "Movement Speed Penalty (%)", 30f, "Movement speed reduction while using Vacuum.");
            vacuumMaxDuration = N(v, "Maximum Duration (s)", 20f, "Maximum time Vacuum can be held continuously.");
            vacuumMinCooldown = N(v, "Minimum Cooldown (s)", 2f, "Cooldown after releasing Vacuum immediately.");
            vacuumMaxCooldown = N(v, "Maximum Cooldown (s)", 15f, "Cooldown after using Vacuum for its full duration.");
            vacuumProc = N(v, "Proc Coefficient Per Tick", .2f, "Proc coefficient of each of five Vacuum damage ticks per second.");
            vacuumTrashRecovery = N(v, "Trash Cannon Recharge Refunded (Stocks)", 1f, "Refund this many full Trash Cannon stocks for each successful recycling event.");
            vacuumBoostRecovery = N(v, "Suction Boost Recharge Refunded (Stocks)", .5f, "Refund this fraction of a Suction Boost stock for each successful recycling event (two 0.5 refunds grant one charge).");
            cannonCount = RemoteDroneSkills.Number(t, "Scraps Per Shot", 5, "Number of scrap projectiles launched per Trash Cannon stock.");
            cannonSpread = N(t, "Spread Angle (degrees)", 1.5f, "Angle from the centre of the shot to the outer scrap projectiles.");
            cannonDirect = N(t, "Direct Damage Per Scrap (%)", 100f, "Damage of each individual scrap on a direct hit.");
            cannonBlast = N(t, "Explosion Damage Per Scrap (%)", 50f, "Maximum area explosion damage from each scrap projectile near its centre; damage falls off towards the edge (SweetSpot). The direct target can also take this damage.");
            cannonRadius = N(t, "Explosion Radius (m)", 6f, "Radius of the Trash Cannon explosion.");
            cannonSpeed = N(t, "Projectile Speed (m/s)", 120f, "Travel speed of the compressed debris projectile.");
            cannonInterval = N(t, "Minimum Shot Interval (s)", .5f, "Shortest time between consecutive shots while holding Secondary.");
            cannonDirectProc = N(t, "Direct Hit Proc Coefficient", .5f, "Proc coefficient of the direct hit.");
            cannonBlastProc = N(t, "Explosion Proc Coefficient", .5f, "Proc coefficient of the explosion.");
            cannonStarting = RemoteDroneSkills.Number(t, "Starting Stocks", 10, "Initial Trash Cannon ammunition upon spawning.");
            boostStarting = RemoteDroneSkills.Number(b, "Starting Stocks", 3, "Sonic Boom charges available when the Cleanup Drone first spawns. Charges are only restored by recycling enemies.");
            boostLaunchSpeed = N(b, "Recoil Launch Speed (m/s)", 65f, "Initial backwards velocity from Sonic Boom. Momentum is preserved by the drone's normal flight physics; Vacuum's movement penalty reduces the launch speed.");
            boostKnockback = N(b, "Enemy Knockback Force", 3000f, "Knockback force against enemies inside the cone; vanilla-style force units.");
            boostDamage = N(b, "Damage (%)", 800f, "Damage dealt once to each enemy in the sonic blast cone.");
            boostRange = N(b, "Cone Range (m)", 18f, "How far the blast reaches from the drone.");
            boostHalfAngle = N(b, "Cone Half-Angle (degrees)", 45f, "Half the cone width (45 degrees produces a 90-degree-wide cone).");
            boostProc = N(b, "Proc Coefficient", .5f, "Proc coefficient of each enemy hit by the sonic blast.");
            massThreshold = N(m, "Execution Threshold (%)", 50f, "Maximum health percentage for a non-boss enemy to be targeted.");
            massRange = N(m, "Target Range (m)", 60f, "Maximum distance to an enemy being recycled.");
            massCapture = N(m, "Capture Timeout (s)", 3f, "Maximum time to pull the target into contact with the drone before the execution fails.");
            massFailedCooldown = N(m, "Failed Capture Cooldown (s)", 6.5f, "Cooldown if the target cannot be captured within the timeout.");
            massCooldown = N(m, "Cooldown (s)", 15f, "Cooldown after processing has completed.");
            massBaseProcess = N(m, "Base Processing Time (s)", 1.5f, "Minimum processing time before accounting for enemy health.");
            massHealthDivisor = N(m, "Processing Health Divisor", 200f, "Level-1 enemy health (including elite health modifiers) is divided by this amount and added to processing time.");
            massMaxProcess = N(m, "Maximum Processing Time (s)", 10f, "Maximum processing time.");
            massSpeedPenalty = N(m, "Processing Movement Speed Penalty (%)", 40f, "Movement speed reduction while processing.");
            smallThreshold = N(m, "Medium Size Minimum Base Health", 200f, "Small enemies have less base health than this.");
            mediumThreshold = N(m, "Large Size Minimum Base Health", 450f, "Medium enemies have less base health than this.");
            largeThreshold = N(m, "Very Large Size Minimum Base Health", 900f, "Large enemies have less base health than this.");
            smallGold = RemoteDroneSkills.Number(m, "Small Gold Nuggets", 1, "Gold nuggets from processing a Small enemy.");
            mediumGold = RemoteDroneSkills.Number(m, "Medium Gold Nuggets", 2, "Gold nuggets from processing a Medium enemy.");
            largeGold = RemoteDroneSkills.Number(m, "Large Gold Nuggets", 3, "Gold nuggets from processing a Large enemy.");
            hugeGold = RemoteDroneSkills.Number(m, "Very Large Gold Nuggets", 5, "Gold nuggets from processing a Very Large enemy.");
            smallHeal = RemoteDroneSkills.Number(m, "Small Healing Pickups", 2, "Healing pickups from processing a Small enemy.");
            mediumHeal = RemoteDroneSkills.Number(m, "Medium Healing Pickups", 3, "Healing pickups from processing a Medium enemy.");
            largeHeal = RemoteDroneSkills.Number(m, "Large Healing Pickups", 5, "Healing pickups from processing a Large enemy.");
            hugeHeal = RemoteDroneSkills.Number(m, "Very Large Healing Pickups", 8, "Healing pickups from processing a Very Large enemy.");
            smallAmmo = RemoteDroneSkills.Number(m, "Small Trash Cannon Recharge Refunded (Stocks)", 3, "Trash Cannon recharge intervals refunded by processing a Small enemy.");
            mediumAmmo = RemoteDroneSkills.Number(m, "Medium Trash Cannon Recharge Refunded (Stocks)", 6, "Trash Cannon recharge intervals refunded by processing a Medium enemy.");
            largeAmmo = RemoteDroneSkills.Number(m, "Large Trash Cannon Recharge Refunded (Stocks)", 9, "Trash Cannon recharge intervals refunded by processing a Large enemy.");
            hugeAmmo = RemoteDroneSkills.Number(m, "Very Large Trash Cannon Recharge Refunded (Stocks)", 12, "Trash Cannon recharge intervals refunded by processing a Very Large enemy.");
            smallBoost = N(m, "Small Suction Boost Recharge Refunded (Stocks)", 1f, "Suction Boost recharge intervals refunded by processing a Small enemy.");
            mediumBoost = N(m, "Medium Suction Boost Recharge Refunded (Stocks)", 2f, "Suction Boost recharge intervals refunded by processing a Medium enemy.");
            largeBoost = N(m, "Large Suction Boost Recharge Refunded (Stocks)", 3f, "Suction Boost recharge intervals refunded by processing a Large enemy.");
            hugeBoost = N(m, "Very Large Suction Boost Recharge Refunded (Stocks)", 5f, "Suction Boost recharge intervals refunded by processing a Very Large enemy.");
        }
        private static ConfigEntry<float> N(string s, string n, float v, string d) => RemoteDroneSkills.Number(s, n, v, d);
        private static float F(ConfigEntry<float> e) => Mathf.Max(0f, C.Get(e));
        private static int I(ConfigEntry<int> e) => Mathf.Max(0, C.Get(e));

        internal static void Register()
        {
            if (!enabled.Value) return;
            vacuumSlowBuff = ScriptableObject.CreateInstance<BuffDef>();
            vacuumSlowBuff.name = "3QSRemoteCleanupVacuumSlow";
            vacuumSlowBuff.buffColor = new Color(.6f, .55f, .4f);
            vacuumSlowBuff.isDebuff = true;
            vacuumSlowBuff.canStack = false;
            vacuumSlowBuff.iconSprite = RoR2Content.Buffs.Slow50 ? RoR2Content.Buffs.Slow50.iconSprite : null;
            primary = RemoteDroneSkills.MakeSkill("CleanupVacuum", "Vacuum", "Draw enemies beneath you into a damaging vacuum.",
                typeof(Vacuum), VacuumMachine, F(vacuumMaxCooldown), null, true);
            vacuumCancel = RemoteDroneSkills.MakeSkill("CleanupCancelVacuum", "Cancel Vacuum",
                "Deactivate the vacuum and begin its cooldown.", typeof(CancelVacuum), VacuumMachine, 0f, null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>());
            vacuumCancel.baseRechargeInterval = 0f;
            vacuumCancel.baseMaxStock = 1;
            vacuumCancel.rechargeStock = 0;
            vacuumCancel.stockToConsume = 0;
            vacuumCancel.requiredStock = 0;
            vacuumCancel.mustKeyPress = true;
            secondary = RemoteDroneSkills.MakeSkill("CleanupTrashCannon", "Trash Cannon", "Fire a spread of explosive scrap projectiles.",
                typeof(TrashCannon), CannonMachine, 999999f, null);
            utility = RemoteDroneSkills.MakeSkill("CleanupSuctionBoost", "Suction Boost", "Release a damaging sonic blast ahead, launching the drone backwards.",
                typeof(SuctionBoost), BoostMachine, 0f, null);
            special = RemoteDroneSkills.MakeSkill("CleanupMassRecycling", "Mass Recycling", "Execute and process a weakened enemy for rewards.",
                typeof(MassRecycling), MassMachine, F(massCooldown), null, true);
            primary.cancelSprintingOnActivation = false;
            utility.cancelSprintingOnActivation = false;
            utility.mustKeyPress = true; // One blast per press, not repeated while held.
            special.cancelSprintingOnActivation = false;
            Refresh();
        }
        internal static void Refresh()
        {
            if (!primary) return;
            primary.baseRechargeInterval = F(vacuumMaxCooldown);
            secondary.baseRechargeInterval = 0f; // UI has no cooldown overlay; stocks are recycled only.
            secondary.rechargeStock = 0;
            secondary.stockToConsume = 1;
            if (vacuumCancel) { vacuumCancel.baseRechargeInterval = 0f; vacuumCancel.rechargeStock = 0; vacuumCancel.stockToConsume = 0; vacuumCancel.requiredStock = 0; }
            utility.baseRechargeInterval = 0f; // Charges come exclusively from recycling.
            utility.rechargeStock = 0;
            utility.stockToConsume = 1;
            special.baseRechargeInterval = F(massCooldown);
            if (vacuumCancel) vacuumCancel.icon = primary.icon;
            Tweaks.Survivors.SurvivorSettings.SetSkillText(primary, () =>
                $"<style=cIsDamage>Slayer.</style> Vacuum nearby enemies, pulling them towards you and dealing <style=cIsDamage>{C.Get(vacuumDps):0.##}% damage per second</style> while <style=cIsUtility>Slowing</style> them by <style=cIsUtility>{C.Get(vacuumSlow):0.##}%</style>. Movement speed is reduced by {C.Get(vacuumSpeedPenalty):0.##}% while active. Enemies defeated in the Vacuum are <style=cIsDamage>Recycled</style> and processed into gold, health and ammunition for your other abilities.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(secondary, () =>
                $"Fire a cluster of <style=cIsDamage>{I(cannonCount)} pieces of scrap</style>, that each deal <style=cIsDamage>{C.Get(cannonDirect):0.##}% damage</style> on a direct hit and explode for up to <style=cIsDamage>{C.Get(cannonBlast):0.##}% damage</style>. Direct hits <style=cIsUtility>Stun</style> enemies for <style=cIsUtility>1 second</style>. <style=cIsDamage>Recycle</style> enemies to replenish ammunition.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(utility, () =>
                $"Release a powerful sonic blast in a cone, dealing <style=cIsDamage>{C.Get(boostDamage):0.##}% damage</style> and <style=cIsUtility>knocking back</style> enemies. The recoil launches you backwards. <style=cIsDamage>Recycle</style> enemies to replenish charges.");
            Tweaks.Survivors.SurvivorSettings.SetSkillText(special, () =>
                $"<style=cIsDamage>Execute</style> a non-boss enemy below <style=cIsDamage>{C.Get(massThreshold):0.##}% health</style>, pulling it into your recycling system to process. While processing, your movement speed is reduced by {C.Get(massSpeedPenalty):0.##}% and other abilities are unusable. Once <style=cIsDamage>Recycled</style>, the enemy produces gold, healing pickups and ability charges based on its health.");
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(primary, () =>
                $"\nDamage Per Second: {F(vacuumDps):0.##}%\nRadius: {F(vacuumRadius):0.##}m\nPull Speed: {F(vacuumPull):0.##}m/s\nSlow: {F(vacuumSlow):0.##}%\nMovement Speed Penalty: {F(vacuumSpeedPenalty):0.##}%\nMaximum Duration: {F(vacuumMaxDuration):0.##}s\nCooldown: {F(vacuumMinCooldown):0.##}–{F(vacuumMaxCooldown):0.##}s\nDamage Ticks: 5/s\nTrash Cannon Refund: {F(vacuumTrashRecovery):0.##} stock(s) per recycled enemy\nSuction Boost Refund: {F(vacuumBoostRecovery):0.##} stock(s) per recycled enemy", () => F(vacuumProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(secondary, () =>
                $"\nScraps Per Shot: {I(cannonCount)}\nDirect Damage: {F(cannonDirect):0.##}% per scrap\nExplosion Damage: Up to {F(cannonBlast):0.##}% per scrap\nSpread: {F(cannonSpread):0.##}°\nExplosion Radius: {F(cannonRadius):0.##}m\nProjectile Speed: {F(cannonSpeed):0.##}m/s\nDirect Hit Stun: 1s\nFire Interval: {F(cannonInterval):0.##}s\nExplosion Proc: {F(cannonBlastProc):0.##}\nNatural Recharge: None", () => F(cannonDirectProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(utility, () =>
                $"\nDamage: {F(boostDamage):0.##}%\nCone Range: {F(boostRange):0.##}m\nCone Angle: {F(boostHalfAngle)*2f:0.##}°\nRecoil Launch Speed: {F(boostLaunchSpeed):0.##}m/s\nEnemy Knockback Force: {F(boostKnockback):0.##}\nNatural Recharge: None", () => F(boostProc));
            Tweaks.ModCompatibility.LookingGlassSkillStats.Register(special, () =>
                $"\nExecution Threshold: {F(massThreshold):0.##}% health\nTarget Range: {F(massRange):0.##}m\nCapture Timeout: {F(massCapture):0.##}s\nMovement Speed Penalty: {F(massSpeedPenalty):0.##}%\nProcessing Time: {F(massBaseProcess):0.##}s + Adjusted Base Health/{F(massHealthDivisor):0.##}\nMaximum Processing Time: {F(massMaxProcess):0.##}s\nCooldown: {F(massCooldown):0.##}s\nFailed Capture Cooldown: {F(massFailedCooldown):0.##}s", () => 0f);
        }
        internal static void RegisterProjectiles(ContentPack content)
        {
            if (!enabled.Value) return;
            // Drifter's Cleanup uses a ProjectileFamily, not a fixed legacy-resources
            // projectile name. Read the actual prefabs from Drifter's body definition.
            GameObject donor = FindDrifterJunkProjectile();
            if (!donor)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: Drifter projectile family was unavailable; retaining the original FMJ fallback.");
                donor = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/FMJRamping");
            }
            if (!donor) { ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: no projectile donor available."); return; }
            cannonPrefab = R2API.PrefabAPI.InstantiateClone(donor, "3QSRemoteCleanupTrashCannonProjectile", true);
            // The fallback FMJ ghost is a separate Commando visual. Drifter's
            // shotgun projectile has no ghost: its Ring particle system and Light
            // are attached directly to the projectile. Install those locally when
            // the real Drifter prefab becomes available during gameplay.
            ProjectileController cannonController = cannonPrefab.GetComponent<ProjectileController>();
            if (cannonController)
            {
                // In this RoR2 version the current address field is named
                // ghostPrefabAddress, not ghostPrefabReference. An invalid address
                // falls back to the legacy direct prefab in ProjectileController.Awake(),
                // so clear both sources on the cloned FMJ donor.
                cannonController.ghostPrefabAddress = null;
                FieldInfo legacyGhostField = typeof(ProjectileController).GetField(
                    "ghostPrefab", BindingFlags.Instance | BindingFlags.Public);
                if (legacyGhostField != null)
                    legacyGhostField.SetValue(cannonController, null);
            }
            // The FMJRamping donor is used only for its registered projectile behaviour.
            // Strip its entire visual hierarchy at content-registration time. Merely
            // disabling renderers after the projectile spawns is not sufficient:
            // vanilla components can restart the Commando particles/trail afterwards.
            RemoveCommandoProjectileVisuals(cannonPrefab);
            if (!cannonPrefab.GetComponent<DrifterTrashVisual>())
                cannonPrefab.AddComponent<DrifterTrashVisual>();
            // The impact and damage for this skill are handled by TrashProjectileImpact;
            // disable donor collision/explosion behaviours to avoid a double hit.
            foreach (MonoBehaviour component in cannonPrefab.GetComponents<MonoBehaviour>())
            {
                if (!component) continue;
                string componentName = component.GetType().Name;
                if (componentName == "ProjectileImpactExplosion" ||
                    componentName == "ProjectileSingleTargetImpact" ||
                    componentName == "ProjectileOverlapAttack" ||
                    componentName == "ProjectileStickOnImpact" ||
                    componentName == "ProjectileImpactDamage") component.enabled = false;
            }
            ProjectileSimple simple = cannonPrefab.GetComponent<ProjectileSimple>();
            if (simple) simple.lifetime = Mathf.Max(6f, simple.lifetime);
            if (!cannonPrefab.GetComponent<ProjectileDamage>())
                cannonPrefab.AddComponent<ProjectileDamage>();
            cannonPrefab.AddComponent<TrashProjectileImpact>();
            content.projectilePrefabs.Add(new[] { cannonPrefab });
        }
        private static void RemoveCommandoProjectileVisuals(GameObject prefab)
        {
            // The custom TrashProjectileImpact performs its own collision checks;
            // no FMJ child objects are required for hit detection or damage.
            int childrenRemoved = 0;
            for (int i = prefab.transform.childCount - 1; i >= 0; --i)
            {
                Transform child = prefab.transform.GetChild(i);
                if (!child) continue;
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                ++childrenRemoved;
            }

            // Also remove root-level presentation components, while preserving
            // ProjectileController, ProjectileSimple, ProjectileDamage and networking.
            foreach (Renderer renderer in prefab.GetComponents<Renderer>())
                if (renderer) UnityEngine.Object.DestroyImmediate(renderer);
            foreach (ParticleSystem particles in prefab.GetComponents<ParticleSystem>())
                if (particles) UnityEngine.Object.DestroyImmediate(particles);
            foreach (Light light in prefab.GetComponents<Light>())
                if (light) UnityEngine.Object.DestroyImmediate(light);

            ThreeQsVanillaTweaks.Log.Info(
                "Cleanup Trash Cannon: removed FMJ visual hierarchy (" +
                childrenRemoved + " child objects) from registered projectile.");
        }

        private static GameObject drifterImpactVisual;
        private static GameObject cachedDrifterShotgun;

        // This component runs on each projectile instance (including clients).
        // The donor prefab is not available at the time our content pack registers,
        // so copying visuals onto cannonPrefab from the firing state's authority-
        // only OnEnter would leave remote clients displaying the wrong effect.
        private sealed class DrifterTrashVisual : MonoBehaviour
        {
            private bool installed;
            private float nextAttempt;

            private void Start() { InstallIfReady(); }

            private void Update()
            {
                if (installed || Time.time < nextAttempt) return;
                InstallIfReady();
            }

            private void InstallIfReady()
            {
                nextAttempt = Time.time + .25f;
                GameObject donor = FindDrifterJunkProjectile();
                if (!donor) return;

                Transform donorRing = donor.transform.Find("Ring");
                if (!donorRing)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: Drifter prefab has no Ring child; visual installation deferred.");
                    return;
                }

                // FMJRamping can contain direct renderers as well as a ghost.
                // Hide its direct visuals before attaching the real Drifter ones.
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                    if (renderer) renderer.enabled = false;
                foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
                    if (particles) particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                GameObject ring = UnityEngine.Object.Instantiate(donorRing.gameObject, transform, false);
                ring.name = "DrifterJunkBalletteRing";
                ring.transform.localPosition = donorRing.localPosition;
                ring.transform.localRotation = donorRing.localRotation;
                ring.transform.localScale = donorRing.localScale;
                foreach (Renderer renderer in ring.GetComponentsInChildren<Renderer>(true))
                    if (renderer) renderer.enabled = true;
                foreach (ParticleSystem particles in ring.GetComponentsInChildren<ParticleSystem>(true))
                    if (particles) particles.Play(true);

                Light sourceLight = donor.GetComponent<Light>();
                if (sourceLight)
                {
                    Light targetLight = gameObject.AddComponent<Light>();
                    targetLight.type = sourceLight.type;
                    targetLight.color = sourceLight.color;
                    targetLight.intensity = sourceLight.intensity;
                    targetLight.range = sourceLight.range;
                    targetLight.spotAngle = sourceLight.spotAngle;
                    targetLight.shadows = sourceLight.shadows;
                    targetLight.renderMode = sourceLight.renderMode;
                    targetLight.enabled = sourceLight.enabled;
                }

                ProjectileImpactExplosion impact = donor.GetComponent<ProjectileImpactExplosion>();
                if (impact && impact.impactEffect) drifterImpactVisual = impact.impactEffect;
                installed = true;
                ThreeQsVanillaTweaks.Log.Info("Cleanup Trash Cannon: installed Drifter Junk Ballette Ring + Light visuals; impact=" +
                    (drifterImpactVisual ? drifterImpactVisual.name : "<unavailable>"));
            }
        }

        // The real Cleanup shotgun projectile is a serialized field on
        // EntityStates.Drifter.AimMagicBag (confirmed by the Skill Prefabs log).
        // It is NOT the ordinary Drifter projectile-family candidate.
        private static GameObject FindDrifterShotgunPrefab()
        {
            if (cachedDrifterShotgun) return cachedDrifterShotgun;
            // Query the serialized state configuration directly. During content-pack
            // registration, Drifter's runtime projectile prefab may not yet be
            // instantiated, so Resources.FindObjectsOfTypeAll alone is insufficient.
            EntityStateConfiguration configuration = LegacyResourcesAPI.Load<EntityStateConfiguration>(
                "EntityStateConfigurations/EntityStates.Drifter.AimMagicBag");
            if (!configuration)
                foreach (EntityStateConfiguration candidate in Resources.FindObjectsOfTypeAll<EntityStateConfiguration>())
                    if (candidate && candidate.name.IndexOf("AimMagicBag", StringComparison.OrdinalIgnoreCase) >= 0)
                    { configuration = candidate; break; }
            if (configuration)
            {
                var state = new EntityStates.Drifter.AimMagicBag();
                configuration.BuildInstanceInitializer()?.Invoke(state);
                GameObject shotgunPrefab = state.shotgunShotPrefab != null ? state.shotgunShotPrefab.gameObject : null;
                if (shotgunPrefab && shotgunPrefab.GetComponent<ProjectileController>())
                {
                    cachedDrifterShotgun = shotgunPrefab;
                    ThreeQsVanillaTweaks.Log.Info("Cleanup Trash Cannon: resolved Drifter shotgun from state configuration: " + shotgunPrefab.name);
                    return shotgunPrefab;
                }
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: AimMagicBag configuration loaded, but shotgunShotPrefab was null.");
            }
            else
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: AimMagicBag state configuration unavailable during projectile registration.");
            // Already loaded asset is an alternative, not our primary dependency.
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
                if (candidate && candidate.name == "DrifterJunkBallette, Classic" &&
                    candidate.GetComponent<ProjectileController>()) return cachedDrifterShotgun = candidate;
            return null;
        }
        private static object ReadStateConfigMember(object owner, string member)
        {
            if (owner == null) return null;
            for (Type type = owner.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(member, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return field.GetValue(owner);
                PropertyInfo property = type.GetProperty(member, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(owner, null);
            }
            return null;
        }
        private static GameObject FindDrifterJunkProjectile()
        {
            try
            {
                return FindDrifterShotgunPrefab();
            }
            catch (Exception error)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: shotgun lookup failed: " + error.Message);
                return null;
            }
        }

        internal static void Install(GameObject prefab)
        {
            if (!enabled.Value || !primary || !prefab) return;
            SkillLocator native = prefab.GetComponent<SkillLocator>();
            Sprite icon = RemoteDroneSkills.VanillaIcon("CleanupDroneBody", "Primary", null);
            primary.icon = icon;
            secondary.icon = MakeMechanicalIcon(RemoteDroneSkills.VanillaIcon("DrifterBody", "Secondary", icon));
            utility.icon = MakeMechanicalIcon(RemoteDroneSkills.VanillaIcon("TreebotBody", "Utility", icon));
            LoadRexSonicBoomEffect();
            // Self-contained recycling glyph. Never inherit an icon from a different skill
            // (in particular Remote Disconnect), even when a vanilla lookup fails.
            special.icon = MakeMechanicalIcon(RemoteDroneSkills.VanillaIcon("DrifterBody", "Special", icon));
            // Preserve the original remote Cleanup Drone primary, its toggle SkillDef,
            // state machine, state configuration, VFX, audio and recycling pipeline.
            // The custom Vacuum skill is deliberately NOT installed: it was auto-
            // firing its cancel override while M1 was still held.
            if (vacuumOn.Value && native && native.primary && native.primary.skillDef)
            {
                native.primary.skillDef.baseRechargeInterval = F(vacuumMaxCooldown);
                native.primary.skillDef.beginSkillCooldownOnSkillEnd = true;
                ThreeQsVanillaTweaks.Log.Info("Cleanup Drone: using native Vacuum primary and cancel toggle.");
            }
            if (massOn.Value && !prefab.GetComponent<CleanupTargetIndicator>()) prefab.AddComponent<CleanupTargetIndicator>();
            if (cannonOn.Value) { RemoteDroneSkills.AddMachine(prefab, CannonMachine); RemoteDroneSkills.SetSkill(prefab, "Secondary", secondary); }
            if (boostOn.Value) { RemoteDroneSkills.AddMachine(prefab, BoostMachine); RemoteDroneSkills.SetSkill(prefab, "Utility", utility); }
            if (massOn.Value) { RemoteDroneSkills.AddMachine(prefab, MassMachine); RemoteDroneSkills.SetSkill(prefab, "Special", special); }
            if (!prefab.GetComponent<CleanupResources>()) prefab.AddComponent<CleanupResources>();
            if (!hooked)
            {
                hooked = true;
                var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.CleanupSkills");
                harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(AfterStats)));
                harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.IsReady)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(AfterAvailability)));
                harmony.Patch(AccessTools.Method(typeof(GenericSkill), nameof(GenericSkill.CanExecute)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(AfterAvailability)));
                // The projectile is resolved after vanilla content initialises. Configure
                // the individual native projectile instance, never Drifter's shared prefab.
                harmony.Patch(AccessTools.Method(typeof(ProjectileManager), "InitializeProjectile"),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(ConfigureNativeTrashProjectile)));
                harmony.Patch(AccessTools.Method(typeof(ProjectileController), nameof(ProjectileController.PerformImpact)),
                    prefix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeTrashDirectHit)));
                // Extend only the actual vanilla state, retaining serialized effects and
                // built-in toggle/processing logic. These hooks only affect this mod's
                // Cleanup Drone bodies (checked through CleanupResources).
                var cleanupType = typeof(EntityStates.Drone.Cleanup);
                harmony.Patch(AccessTools.Method(cleanupType, "OnEnter"),
                    prefix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumEnter)));
                harmony.Patch(AccessTools.Method(cleanupType, "FixedUpdate"),
                    prefix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumCancelOnMass)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumTick)));
                harmony.Patch(AccessTools.Method(cleanupType, "SpawnDrop"),
                    prefix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumCustomDrop)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumReward)));
                harmony.Patch(AccessTools.Method(cleanupType, "OnExit"),
                    prefix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumExit)),
                    postfix: new HarmonyMethod(typeof(RemoteCleanupDrone), nameof(NativeVacuumExitAudio)));

            }
        }
        private static bool NativeVacuumActive(EntityStates.Drone.Cleanup state)
        {
            return state is EntityStates.Drone.CleanupRemoteOp && state.gameObject &&
                state.GetComponent<CleanupResources>() && vacuumOn != null && vacuumOn.Value;
        }

        // The vanilla state receives its serialized configuration from the original
        // SkillDef. Adjust only configurable fields, *before* Cleanup.OnEnter reads them.
        private static void NativeVacuumEnter(EntityStates.Drone.Cleanup __instance)
        {
            if (!NativeVacuumActive(__instance)) return;
            if (__instance.suckEffectPrefab && __instance.suckEffectPrefab.gameObject)
                nativeRecyclingSuckEffect = __instance.suckEffectPrefab.gameObject;
            __instance.radius = F(vacuumRadius);
            __instance.force = F(vacuumPull);
            __instance.duration = Mathf.Max(0.1f, F(vacuumMaxDuration));
            __instance.GetComponent<CleanupResources>().vacuuming = true;
            if (__instance.characterBody) __instance.characterBody.MarkAllStatsDirty();
            nativeVacuumTicks[__instance] = 0f;
            nativeVacuumAge[__instance] = 0f;
        }

        private static bool NativeVacuumCancelOnMass(EntityStates.Drone.Cleanup __instance)
        {
            var resources = __instance.GetComponent<CleanupResources>();
            if (!resources || !resources.requestVacuumStop) return true;
            resources.requestVacuumStop = false;
            if (__instance.isAuthority) __instance.outer.SetNextStateToMain();
            return false;
        }
        // Keep native vacuum, pull, ward, queue, animations and sounds. Add only our
        // server-authoritative five damage ticks per second, with 0.2 proc each.
        private static void NativeVacuumTick(EntityStates.Drone.Cleanup __instance)
        {
            if (!NativeVacuumActive(__instance) || !__instance.characterBody) return;
            float age;
            nativeVacuumAge.TryGetValue(__instance, out age);
            nativeVacuumAge[__instance] = age + Time.fixedDeltaTime;
            if (!NetworkServer.active) return;
            float elapsed;
            nativeVacuumTicks.TryGetValue(__instance, out elapsed);
            elapsed += Time.fixedDeltaTime;
            if (elapsed < 0.2f)
            {
                nativeVacuumTicks[__instance] = elapsed;
                return;
            }
            elapsed -= 0.2f;
            nativeVacuumTicks[__instance] = elapsed;
            Vector3 centre = __instance.characterBody.corePosition;
            int count = Physics.OverlapSphereNonAlloc(centre, F(vacuumRadius), targets,
                LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
            nativeVacuumHits.Clear();
            for (int i = 0; i < count; ++i)
            {
                HurtBox box = targets[i] ? targets[i].GetComponent<HurtBox>() : null;
                HealthComponent health = box ? box.healthComponent : null;
                if (!health || !health.alive || !nativeVacuumHits.Add(health)) continue;
                CharacterBody victim = health.body;
                if (!victim || !victim.teamComponent || !__instance.characterBody.teamComponent ||
                    victim.teamComponent.teamIndex == __instance.characterBody.teamComponent.teamIndex) continue;
                if (vacuumSlowBuff && !Mathf.Approximately(F(vacuumSlow), 50f))
                    victim.AddTimedBuff(vacuumSlowBuff, .4f);
                DamageInfo damage = new DamageInfo {
                    attacker = __instance.gameObject, inflictor = __instance.gameObject,
                    damage = __instance.characterBody.damage * F(vacuumDps) / 500f *
                        (3f - 2f * Mathf.Clamp01(health.combinedHealth / Mathf.Max(1f, health.fullCombinedHealth))),
                    procCoefficient = F(vacuumProc), crit = Util.CheckRoll(__instance.characterBody.crit, __instance.characterBody.master),
                    position = victim.corePosition,
                    damageType = new DamageTypeCombo(DamageType.Generic,
                        DamageTypeExtended.Generic, DamageSource.Primary)
                };
                health.TakeDamage(damage);
                GlobalEventManager.instance.OnHitEnemy(damage, health.gameObject);
            }
        }

        // Called by native Cleanup.ProcessKills only after a completed processing step.
        // The native SpawnDrop creates the real networked gold/healing pickups.
        private static readonly HashSet<EntityStates.Drone.Cleanup> successfulNativeDrops = new HashSet<EntityStates.Drone.Cleanup>();
        private static bool NativeVacuumCustomDrop(EntityStates.Drone.Cleanup __instance)
        {
            if (!NetworkServer.active || !NativeVacuumActive(__instance)) return true;
            int drops = Mathf.Max(1, __instance.dropsPerKill);
            bool spawned = false;
            for (int i = 0; i < drops; i++)
                spawned |= SpawnNativePickup(__instance.characterBody, UnityEngine.Random.value < .5f);
            if (spawned) successfulNativeDrops.Add(__instance);
            return false; // Native method deliberately ignores collision with its own drone.
        }
        private static void NativeVacuumReward(EntityStates.Drone.Cleanup __instance)
        {
            if (NetworkServer.active && successfulNativeDrops.Remove(__instance))
                __instance.GetComponent<CleanupResources>()?.Recover(F(vacuumTrashRecovery), F(vacuumBoostRecovery));
        }

        private static void NativeVacuumExit(EntityStates.Drone.Cleanup __instance)
        {
            if (!NativeVacuumActive(__instance)) return;
            if (NetworkServer.active)
            {
                var controller = __instance.GetComponent<CleanupDroneController>();
                var processingResources = __instance.GetComponent<CleanupResources>();
                if (processingResources && controller && controller.killsToProcess > 0)
                    processingResources.QueueProcessing(controller.killsToProcess, Mathf.Max(.1f, __instance.processDuration),
                        Mathf.Max(1, __instance.dropsPerKill));
            }
            nativeVacuumTicks.Remove(__instance);
            float activeTime;
            nativeVacuumAge.TryGetValue(__instance, out activeTime);
            nativeVacuumAge.Remove(__instance);
            GenericSkill nativePrimary = __instance.skillLocator ? __instance.skillLocator.primary : null;
            if (nativePrimary)
            {
                float fraction = Mathf.Clamp01(activeTime / Mathf.Max(.1f, F(vacuumMaxDuration)));
                float cooldown = Mathf.Lerp(F(vacuumMinCooldown), F(vacuumMaxCooldown), fraction);
                var adjust = __instance.GetComponent<VacuumCooldownAdjust>() ?? __instance.gameObject.AddComponent<VacuumCooldownAdjust>();
                adjust.Schedule(nativePrimary, cooldown);
            }
            var resources = __instance.GetComponent<CleanupResources>();
            if (resources) resources.vacuuming = false;
            if (__instance.characterBody) __instance.characterBody.MarkAllStatsDirty();
        }

        // Cleanup.OnExit() stops its processing audio. Restart it only after that
        // native cleanup, if there are still kills queued for deferred recycling.
        private static void NativeVacuumExitAudio(EntityStates.Drone.Cleanup __instance)
        {
            if (!NativeVacuumActive(__instance) || !NetworkServer.active) return;
            var resources = __instance.GetComponent<CleanupResources>();
            if (resources) resources.StartDeferredProcessingAudio();
        }

        internal static BuffDef[] Buffs => enabled != null && enabled.Value && vacuumSlowBuff
            ? new[] { vacuumSlowBuff } : Array.Empty<BuffDef>();

        private static void AfterStats(CharacterBody __instance)
        {
            CleanupResources resources = __instance ? __instance.GetComponent<CleanupResources>() : null;
            if (resources)
            {
                if (resources.vacuuming) __instance.moveSpeed *= 1f - Mathf.Clamp01(F(vacuumSpeedPenalty) / 100f);
                if (resources.processing) __instance.moveSpeed *= 1f - Mathf.Clamp01(F(massSpeedPenalty) / 100f);
            }
            if (vacuumSlowBuff && __instance && __instance.HasBuff(vacuumSlowBuff))
                __instance.moveSpeed *= 2f * (1f - Mathf.Clamp01(F(vacuumSlow) / 100f));
        }
        private static void AfterAvailability(GenericSkill __instance, ref bool __result)
        {
            if (!__result || !__instance) return;
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            CleanupResources resources = body ? body.GetComponent<CleanupResources>() : null;
            if (!resources) return;
            if (resources.processing)
            { __result = false; return; }
            if (__instance.skillDef != special) return;
            InputBankTest bank = body.GetComponent<InputBankTest>();
            if (!bank) { __result = false; return; }
            __result = FindEnemy(bank.GetAimRay(), body, F(massRange), F(massThreshold) / 100f) != null;
        }
        public sealed class CleanupResources : MonoBehaviour
        {
            internal bool vacuuming, processing, requestVacuumStop;
            // Local HUD progress: 1 at the start of capture/processing, 0 at its end.
            internal float recyclingLockoutRemaining = 1f;
            private static readonly Color lockoutFillColour = new Color(.39f, .77f, .95f, .94f);
            private static readonly Color lockoutBackColour = new Color(.05f, .1f, .13f, .78f);
            private static readonly Color regularFillColour = new Color(.98f, .69f, .27f, .95f);
            private static readonly FieldInfo nativeProcessTimerField = AccessTools.Field(typeof(EntityStates.Drone.Cleanup), "processTimer");

            // The regular queue can be processed either by native Vacuum or by the
            // deferred CleanupResources queue after Vacuum has been released.
            private bool TryGetRegularProcessingProgress(out float progress, out int processCount)
            {
                progress = 0f;
                processCount = 0;
                if (queuedKills > 0)
                {
                    CleanupDroneController queuedController = GetComponent<CleanupDroneController>();
                    processCount = queuedKills + (queuedController ? Mathf.Max(0, queuedController.killsToProcess) : 0);
                    progress = Mathf.Clamp01(processTimer / Mathf.Max(.1f, processInterval));
                    return true;
                }

                CleanupDroneController controller = GetComponent<CleanupDroneController>();
                if (!controller || controller.killsToProcess <= 0) return false;
                foreach (EntityStateMachine machine in GetComponents<EntityStateMachine>())
                {
                    EntityStates.Drone.Cleanup native = machine ? machine.state as EntityStates.Drone.Cleanup : null;
                    if (native == null || !NativeVacuumActive(native)) continue;
                    float timer = Mathf.Max(.1f, native.processDuration);
                    if (nativeProcessTimerField != null)
                    {
                        object value = nativeProcessTimerField.GetValue(native);
                        if (value is float) timer = (float)value;
                    }
                    processCount = controller.killsToProcess;
                    progress = Mathf.Clamp01(timer / Mathf.Max(.1f, native.processDuration));
                    return true;
                }
                return false;
            }

            private static void DrawProcessingBar(float x, float y, float w, float h, float fraction, Color fill)
            {
                Color saved = GUI.color;
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(x - 2f, y - 2f, w + 4f, h + 4f), Texture2D.whiteTexture);
                GUI.color = lockoutBackColour;
                GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);
                fraction = Mathf.Clamp01(fraction);
                GUI.color = fill;
                GUI.DrawTexture(new Rect(x, y + h * (1f - fraction), w, h * fraction), Texture2D.whiteTexture);
                GUI.color = saved;
            }

            private void OnGUI()
            {
                // Only the locally controlled Cleanup Drone draws this reticle overlay.
                if (!body || !body.hasEffectiveAuthority) return;
                float h = 76f, w = 8f;
                float y = Screen.height * .5f - h * .5f;
                if (processing)
                    DrawProcessingBar(Screen.width * .5f + 30f, y, w, h,
                        recyclingLockoutRemaining, lockoutFillColour);
                float regularProgress;
                int regularCount;
                if (TryGetRegularProcessingProgress(out regularProgress, out regularCount))
                {
                    float barX = Screen.width * .5f - 30f - w;
                    DrawProcessingBar(barX, y, w, h, regularProgress, regularFillColour);
                    // Show the number of enemies still awaiting/completing regular recycling.
                    GUIStyle countStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 18,
                        fontStyle = FontStyle.Bold
                    };
                    Rect countRect = new Rect(barX - 39f, y + h * .5f - 15f, 32f, 30f);
                    Color original = GUI.color;
                    GUI.color = new Color(.05f, .07f, .09f, .85f);
                    GUI.DrawTexture(countRect, Texture2D.whiteTexture);
                    GUI.color = Color.black;
                    GUI.Label(new Rect(countRect.x + 1f, countRect.y + 1f, countRect.width, countRect.height), regularCount.ToString(), countStyle);
                    GUI.color = regularFillColour;
                    GUI.Label(countRect, regularCount.ToString(), countStyle);
                    GUI.color = original;
                }
            }
            private CharacterBody body;
            private bool initialised;
            private int queuedKills, dropsPerKill = 1;
            private float processInterval = 1f, processTimer;
            private bool deferredProcessingAudio;
            private Transform deferredProcessingEffect;
            internal void StartDeferredProcessingAudio()
            {
                if (deferredProcessingAudio || queuedKills <= 0) return;
                deferredProcessingAudio = true;
                if (!string.IsNullOrEmpty(EntityStates.Drone.Cleanup.processSoundString))
                    Util.PlaySound(EntityStates.Drone.Cleanup.processSoundString, gameObject);
                var modelLocator = body ? body.modelLocator : null;
                var modelTransform = modelLocator ? modelLocator.modelTransform : null;
                var childLocator = modelTransform ? modelTransform.GetComponent<ChildLocator>() : null;
                deferredProcessingEffect = childLocator ? childLocator.FindChild("ScrappingEffect") : null;
                if (deferredProcessingEffect) deferredProcessingEffect.gameObject.SetActive(true);
            }
            private void StopDeferredProcessingAudio()
            {
                if (!deferredProcessingAudio) return;
                deferredProcessingAudio = false;
                // Mass Recycling may still be processing a captured enemy.
                // Do not send the global stop event or hide the shared effect.
                if (!processing)
                {
                    if (!string.IsNullOrEmpty(EntityStates.Drone.Cleanup.endProcessSoundString))
                        Util.PlaySound(EntityStates.Drone.Cleanup.endProcessSoundString, gameObject);
                    if (deferredProcessingEffect) deferredProcessingEffect.gameObject.SetActive(false);
                }
                deferredProcessingEffect = null;
            }
            internal bool HasDeferredProcessing => queuedKills > 0;
            internal bool DeferredProcessingAudioActive => deferredProcessingAudio;
            private void OnDisable() { StopDeferredProcessingAudio(); }
            private void OnDestroy() { StopDeferredProcessingAudio(); }
            internal void QueueProcessing(int count, float interval, int drops)
            {
                queuedKills += Mathf.Max(0, count);
                processInterval = Mathf.Max(.1f, interval);
                dropsPerKill = Mathf.Max(1, drops);
                if (processTimer <= 0f) processTimer = processInterval;
            }
            private void Awake() { body = GetComponent<CharacterBody>(); }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || !body || !body.skillLocator) return;
                if (queuedKills > 0)
                {
                    processTimer -= Time.fixedDeltaTime;
                    if (processTimer <= 0f)
                    {
                        processTimer += processInterval;
                        queuedKills--;
                        bool spawned = false;
                        for (int i = 0; i < dropsPerKill; i++)
                            spawned |= SpawnNativePickup(body, UnityEngine.Random.value < .5f);
                        if (spawned) Recover(F(vacuumTrashRecovery), F(vacuumBoostRecovery));
                        Util.PlaySound("Play_GG_CleanupDrone_DropHealingOrb", gameObject);
                        if (queuedKills == 0) StopDeferredProcessingAudio();
                    }
                }
                if (initialised) return;
                initialised = true;
                GenericSkill cannon = body.skillLocator.secondary;
                if (cannon && cannon.skillDef == secondary)
                    cannon.stock = Mathf.Min(cannon.maxStock, I(cannonStarting));
                GenericSkill boostSkill = body.skillLocator.utility;
                if (boostSkill && boostSkill.skillDef == utility)
                    boostSkill.stock = Mathf.Min(boostSkill.maxStock, Mathf.Max(0, I(boostStarting)));
            }
            // Non-regenerating skills receive stocks directly. A fractional Suction Boost
            // refund is banked until it reaches a whole stock; it never advances a timer.
            private float boostRefundFraction;
            private static void RefundAmmo(GenericSkill skill, SkillDef expected, float amount)
            {
                if (skill && skill.skillDef == expected && amount >= 1f)
                    skill.stock = Mathf.Min(skill.maxStock, skill.stock + Mathf.FloorToInt(amount));
            }
            internal void Recover(float ammo, float boost)
            {
                if (!body || !body.skillLocator) return;
                RefundAmmo(body.skillLocator.secondary, secondary, ammo);
                GenericSkill skill = body.skillLocator.utility;
                if (!skill || skill.skillDef != utility || boost <= 0f) return;
                if (skill.stock >= skill.maxStock) { boostRefundFraction = 0f; return; }
                boostRefundFraction += boost;
                int recovered = Mathf.FloorToInt(boostRefundFraction + .0001f);
                if (recovered <= 0) return;
                int granted = Mathf.Min(recovered, skill.maxStock - skill.stock);
                skill.stock += granted;
                boostRefundFraction = skill.stock >= skill.maxStock ? 0f : boostRefundFraction - granted;
            }
        }
        private static bool ValidTarget(CharacterBody source, CharacterBody target, float threshold)
        {
            return source && target && source != target && target.healthComponent && target.healthComponent.alive &&
                !target.isBoss && target.teamComponent && source.teamComponent &&
                target.teamComponent.teamIndex != source.teamComponent.teamIndex &&
                target.healthComponent.fullHealth > 0f &&
                target.healthComponent.health / target.healthComponent.fullHealth <= threshold;
        }
        private static CharacterBody AimEnemy(BaseState state, float range, float threshold) =>
            FindEnemy(state.GetAimRay(), state.characterBody, range, threshold);
        private static CharacterBody FindEnemy(Ray ray, CharacterBody source, float range, float threshold)
        {
            CharacterBody best = null;
            float bestAngle = 25f, distance = Mathf.Max(0f, range);
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList)
            {
                if (!ValidTarget(source, body, threshold)) continue;
                Vector3 offset = body.corePosition - ray.origin;
                if (offset.sqrMagnitude > distance * distance) continue;
                float angle = Vector3.Angle(ray.direction, offset);
                if (angle < bestAngle) { best = body; bestAngle = angle; }
            }
            return best;
        }
        private static float BaseHealth(CharacterBody enemy)
        {
            // Actual level-1 base health, rather than its scaled maximum health.
            return enemy ? Mathf.Max(1f, enemy.baseMaxHealth) : 1f;
        }
        // Read the actual elite health coefficient instead of inferring it from maxHealth,
        // which would also include level scaling, items and temporary health modifiers.
        // Reflection keeps this compatible with versions exposing the catalog as a
        // field or property and with modded EliteDefs.
        private static float EliteAdjustedBaseHealth(CharacterBody enemy, float baseHealth)
        {
            if (!enemy || !enemy.isElite) return baseHealth;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            try
            {
                Type catalog = typeof(EliteCatalog);
                object collection = catalog.GetField("eliteDefs", flags)?.GetValue(null)
                    ?? catalog.GetProperty("eliteDefs", flags)?.GetValue(null, null);
                if (!(collection is IEnumerable defs)) return baseHealth;
                foreach (object def in defs)
                {
                    if (def == null) continue;
                    Type defType = def.GetType();
                    object equipment = defType.GetField("eliteEquipmentDef", flags)?.GetValue(def)
                        ?? defType.GetProperty("eliteEquipmentDef", flags)?.GetValue(def, null);
                    if (equipment == null) continue;
                    Type equipType = equipment.GetType();
                    BuffDef buff = (equipType.GetField("passiveBuffDef", flags)?.GetValue(equipment)
                        ?? equipType.GetProperty("passiveBuffDef", flags)?.GetValue(equipment, null)) as BuffDef;
                    if (!buff || !enemy.HasBuff(buff)) continue;
                    object value = defType.GetField("healthBoostCoefficient", flags)?.GetValue(def)
                        ?? defType.GetProperty("healthBoostCoefficient", flags)?.GetValue(def, null);
                    if (value != null)
                        return baseHealth * Mathf.Max(1f, Convert.ToSingle(value));
                }
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Mass Recycling: could not resolve elite health multiplier: " + e.Message);
            }
            return baseHealth;
        }
        private static int Tier(float hp) => hp < F(smallThreshold) ? 0 : hp < F(mediumThreshold) ? 1 : hp < F(largeThreshold) ? 2 : 3;
        // Use the very same prefab definitions and configuration as vanilla Cleanup.
        // The native methods themselves are private, so reproduce only their
        // spawning logic while letting vanilla pickup components do the rewards.
        private static bool SpawnNativePickup(CharacterBody source, bool healing)
        {
            if (!NetworkServer.active || !source || !source.teamComponent) return false;
            var native = healing
                ? EntityStates.Drone.Cleanup.healthPackPrefab
                : EntityStates.Drone.Cleanup.moneyPackPrefab;
            if (!native || !native.gameObject) return false;
            Transform location = null;
            Transform modelTransform = source.modelLocator ? source.modelLocator.modelTransform : null;
            var locator = modelTransform ? modelTransform.GetComponent<ChildLocator>() : null;
            if (locator != null && !string.IsNullOrEmpty(EntityStates.Drone.Cleanup.healthDropTransformString))
                location = locator.FindChild(EntityStates.Drone.Cleanup.healthDropTransformString);
            Vector3 position = location ? location.position : source.corePosition;
            GameObject pickup = UnityEngine.Object.Instantiate(native.gameObject, position, UnityEngine.Random.rotation);
            if (!pickup) return false;
            // Native Cleanup deliberately ignores its own spawned pickup collisions. Let
            // the player-controlled drone collect these when the general option is on.
            if (!Tweaks.Others.MultiplayerDrones.CanCollectOwnDronePickups)
            {
                Collider own = source.GetComponent<Collider>();
                Collider pickupCollider = pickup.GetComponent<Collider>();
                if (own && pickupCollider) Physics.IgnoreCollision(own, pickupCollider);
            }
            TeamFilter team = pickup.GetComponent<TeamFilter>();
            if (team) team.teamIndex = source.teamComponent.teamIndex;
            if (healing)
            {
                HealthPickup health = pickup.GetComponentInChildren<HealthPickup>();
                if (health)
                {
                    health.flatHealing = EntityStates.Drone.Cleanup.healthOrbFlatHealing;
                    health.fractionalHealing = EntityStates.Drone.Cleanup.healthOrbFractionalHealing;
                }
                pickup.transform.localScale = Vector3.one * EntityStates.Drone.Cleanup.healthOrbScale;
            }
            else
            {
                MoneyPickup money = pickup.GetComponentInChildren<MoneyPickup>();
                if (money) money.baseGoldReward = EntityStates.Drone.Cleanup.goldPackValue;
                pickup.transform.localScale = Vector3.one * EntityStates.Drone.Cleanup.goldPackScale;
            }
            NetworkServer.Spawn(pickup);
            return true;
        }
        private static void ProcessRewards(CharacterBody source, float hp)
        {
            if (!NetworkServer.active || !source) return;
            int tier = Tier(hp);
            int gold = Mathf.Max(0, tier == 0 ? I(smallGold) : tier == 1 ? I(mediumGold) : tier == 2 ? I(largeGold) : I(hugeGold));
            int heal = Mathf.Max(0, tier == 0 ? I(smallHeal) : tier == 1 ? I(mediumHeal) : tier == 2 ? I(largeHeal) : I(hugeHeal));
            int ammo = tier == 0 ? I(smallAmmo) : tier == 1 ? I(mediumAmmo) : tier == 2 ? I(largeAmmo) : I(hugeAmmo);
            float boost = tier == 0 ? F(smallBoost) : tier == 1 ? F(mediumBoost) : tier == 2 ? F(largeBoost) : F(hugeBoost);
            int spawnedGold = 0, spawnedHeal = 0;
            for (int i = 0; i < gold; i++) if (SpawnNativePickup(source, false)) spawnedGold++;
            for (int i = 0; i < heal; i++) if (SpawnNativePickup(source, true)) spawnedHeal++;
            source.GetComponent<CleanupResources>()?.Recover(ammo, boost);
            if (C.DebugOptionsEnabled)
                ThreeQsVanillaTweaks.Log.Info($"Cleanup Mass Recycling: tier {tier}, gold {spawnedGold}/{gold}, healing {spawnedHeal}/{heal}, ammo {ammo}, boost {boost}.");
        }
        public sealed class VacuumCooldownAdjust : MonoBehaviour
        {
            private GenericSkill skill;
            private float desiredCooldown;
            private int pendingFrames;
            internal void Schedule(GenericSkill slot, float value)
            {
                skill = slot; desiredCooldown = value; pendingFrames = 3;
            }
            private void FixedUpdate()
            {
                if (pendingFrames <= 0) return;
                if (--pendingFrames != 0 || !skill) return;
                // Apply AFTER native skill-end/cancel has changed the stock timer.
                skill.rechargeStopwatch = Mathf.Max(0f, skill.finalRechargeInterval - desiredCooldown);
            }
        }
        // The native CleanupRemoteOp asset has serialised references which do not
        // initialise on our separately registered subclass. Use the native components,
        // sounds and pickup prefab, but manage this state ourselves to avoid the
        // repeating null-reference exceptions in Cleanup.OnEnter/ProcessKills/OnExit.
        public sealed class Vacuum : BaseState
        {
            private CleanupResources resources;
            private GenericSkill primarySkill;
            private BuffWard ward;
            private RadialForce radialForce;
            private Transform processingVisual;
            private bool processingVisualActive;
            private float tick, processClock;
            private int queued;
            private readonly HashSet<HealthComponent> hit = new HashSet<HealthComponent>();
            public override void OnEnter()
            {
                base.OnEnter();
                resources = GetComponent<CleanupResources>();
                primarySkill = skillLocator ? skillLocator.primary : null;
                if (resources) resources.vacuuming = true;
                if (characterBody) characterBody.MarkAllStatsDirty();
                ward = GetComponent<BuffWard>();
                if (ward)
                {
                    ward.Networkradius = F(vacuumRadius);
                    ward.enabled = true;
                }
                if (NetworkServer.active && teamComponent)
                {
                    TeamFilter filter = GetComponent<TeamFilter>();
                    if (filter) filter.teamIndex = teamComponent.teamIndex;
                }
                radialForce = GetComponent<RadialForce>();
                if (radialForce)
                {
                    radialForce.radius = F(vacuumRadius);
                    radialForce.forceMagnitude = F(vacuumPull);
                    radialForce.enabled = true;
                }
                processingVisual = FindModelChild("ScrappingEffect");
                if (processingVisual) processingVisual.gameObject.SetActive(false);
                if (primarySkill) primarySkill.SetSkillOverride(this, vacuumCancel, GenericSkill.SkillOverridePriority.Contextual);
                if (NetworkServer.active) GlobalEventManager.onDeathProcServer += OnDeath;
                Util.PlaySound("Play_cleanUpDrone_vacuum_start", gameObject);
                Util.PlaySound("Play_cleanUpDrone_vacuum_loop", gameObject);
            }
            private void OnDeath(DamageReport report)
            {
                if (!NetworkServer.active || !characterBody || report == null || !report.victimBody) return;
                var enemy = report.victimBody;
                if (!enemy.teamComponent || !teamComponent || enemy.teamComponent.teamIndex == teamComponent.teamIndex) return;
                if ((enemy.corePosition - characterBody.corePosition).sqrMagnitude > F(vacuumRadius) * F(vacuumRadius)) return;
                ++queued;
                if (report.victim) report.victim.NetworkdestroyModelOnDeath = true;
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && fixedAge >= Mathf.Max(.1f, F(vacuumMaxDuration)))
                { outer.SetNextStateToMain(); return; }
                tick += Time.fixedDeltaTime;
                if (NetworkServer.active)
                {
                    processClock += Time.fixedDeltaTime;
                    if (queued > 0 && processClock >= 1f)
                    {
                        processClock -= 1f;
                        --queued;
                        // Native Cleanup chooses a gold or health pack for each drop.
                        bool produced = UnityEngine.Random.value < .5f
                            ? SpawnNativePickup(characterBody, true)
                            : SpawnNativePickup(characterBody, false);
                        if (produced) resources?.Recover(F(vacuumTrashRecovery), F(vacuumBoostRecovery));
                        Util.PlaySound("Play_GG_CleanupDrone_DropHealingOrb", gameObject);
                    }
                    bool processing = queued > 0;
                    if (processing != processingVisualActive)
                    {
                        processingVisualActive = processing;
                        if (processingVisual) processingVisual.gameObject.SetActive(processing);
                        Util.PlaySound(processing ? "Play_cleanUpDrone_vacuum_process" : "Play_cleanUpDrone_vacuum_process_end", gameObject);
                    }
                }
                if (tick < .2f) return;
                tick -= .2f;
                if (!NetworkServer.active) return;
                Vector3 centre = characterBody ? characterBody.corePosition : transform.position;
                int count = Physics.OverlapSphereNonAlloc(centre, F(vacuumRadius), targets,
                    LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide);
                hit.Clear();
                for (int i = 0; i < count; ++i)
                {
                    HurtBox box = targets[i] ? targets[i].GetComponent<HurtBox>() : null;
                    HealthComponent health = box ? box.healthComponent : null;
                    if (!health || !health.alive || !hit.Add(health)) continue;
                    CharacterBody victim = health.body;
                    if (!victim || !victim.teamComponent || !teamComponent || victim.teamComponent.teamIndex == teamComponent.teamIndex) continue;
                    if (vacuumSlowBuff && !Mathf.Approximately(F(vacuumSlow), 50f)) victim.AddTimedBuff(vacuumSlowBuff, .4f);
                    DamageInfo damage = new DamageInfo { attacker = gameObject, inflictor = gameObject,
                        damage = damageStat * F(vacuumDps) / 500f, procCoefficient = F(vacuumProc),
                        crit = RollCrit(), position = victim.corePosition,
                        damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Primary) };
                    health.TakeDamage(damage);
                    GlobalEventManager.instance.OnHitEnemy(damage, health.gameObject);
                }
            }
            public override void OnExit()
            {
                if (NetworkServer.active) GlobalEventManager.onDeathProcServer -= OnDeath;
                if (primarySkill) primarySkill.UnsetSkillOverride(this, vacuumCancel, GenericSkill.SkillOverridePriority.Contextual);
                if (ward) ward.enabled = false;
                if (radialForce) radialForce.enabled = false;
                if (processingVisual) processingVisual.gameObject.SetActive(false);
                if (resources) resources.vacuuming = false;
                if (characterBody) characterBody.MarkAllStatsDirty();
                if (primarySkill)
                {
                    float used = Mathf.Clamp01(fixedAge / Mathf.Max(.1f, F(vacuumMaxDuration)));
                    float cooldown = Mathf.Lerp(F(vacuumMinCooldown), F(vacuumMaxCooldown), used);
                    var adjust = GetComponent<VacuumCooldownAdjust>() ?? gameObject.AddComponent<VacuumCooldownAdjust>();
                    adjust.Schedule(primarySkill, Mathf.Max(0f, F(vacuumMaxCooldown) - cooldown));
                }
                Util.PlaySound("Stop_cleanUpDrone_vacuum_loop", gameObject);
                Util.PlaySound("Play_cleanUpDrone_vacuum_end", gameObject);
                base.OnExit();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Any;
        }
        public sealed class CancelVacuum : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (isAuthority) outer.SetNextStateToMain();
            }
        }
        // Mark only native Drifter projectiles fired by the player-controlled
        // Cleanup Drone. This component is never installed on Drifter's prefab.
        private sealed class NativeTrashProjectileMarker : MonoBehaviour
        {
            internal bool dealtDirectHit;
        }

        private static void ConfigureNativeTrashProjectile(ProjectileController projectileController,
            FireProjectileInfo fireProjectileInfo)
        {
            if (!projectileController || !fireProjectileInfo.owner ||
                !fireProjectileInfo.owner.GetComponent<CleanupResources>()) return;
            GameObject native = FindDrifterJunkProjectile();
            if (!native || fireProjectileInfo.projectilePrefab != native) return;

            ProjectileImpactExplosion impact = projectileController.GetComponent<ProjectileImpactExplosion>();
            ProjectileDamage projectileDamage = projectileController.GetComponent<ProjectileDamage>();
            if (!impact || !projectileDamage)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: native projectile lacks ProjectileImpactExplosion or ProjectileDamage.");
                return;
            }

            projectileController.gameObject.AddComponent<NativeTrashProjectileMarker>();
            // FireProjectileInfo.damage supplies direct-hit damage. Native explosions
            // multiply that value by blastDamageCoefficient.
            float directPercent = Mathf.Max(0f, F(cannonDirect));
            float blastPercent = Mathf.Max(0f, F(cannonBlast));
            float basisPercent = Mathf.Max(.0001f, Mathf.Max(directPercent, blastPercent));
            // Native explosion procs independently of our manually applied direct hit.
            projectileController.procCoefficient = 1f;
            impact.blastRadius = F(cannonRadius);
            impact.blastDamageCoefficient = blastPercent / basisPercent;
            impact.blastProcCoefficient = F(cannonBlastProc);
            impact.falloffModel = BlastAttack.FalloffModel.SweetSpot;
            // Keep Drifter's original impactEffect, renderer, particles and lifetime.
            // No FMJ visual hierarchy is involved.
        }

        private static void NativeTrashDirectHit(ProjectileController __instance, ProjectileImpactInfo impactInfo)
        {
            NativeTrashProjectileMarker marker = __instance ? __instance.GetComponent<NativeTrashProjectileMarker>() : null;
            if (!marker || marker.dealtDirectHit || !NetworkServer.active || F(cannonDirect) <= 0f) return;
            HurtBox hurt = impactInfo.collider ? impactInfo.collider.GetComponent<HurtBox>() : null;
            HealthComponent health = hurt ? hurt.healthComponent : null;
            if (!health || !health.alive || !__instance.owner || health.gameObject == __instance.owner) return;
            CharacterBody attacker = __instance.owner.GetComponent<CharacterBody>();
            if (!attacker || !attacker.teamComponent || !health.body || !health.body.teamComponent ||
                attacker.teamComponent.teamIndex == health.body.teamComponent.teamIndex) return;

            marker.dealtDirectHit = true;
            ProjectileDamage damage = __instance.GetComponent<ProjectileDamage>();
            if (!damage) return;
            DamageInfo info = new DamageInfo
            {
                attacker = __instance.owner,
                inflictor = __instance.gameObject,
                damage = damage.damage * F(cannonDirect) /
                    Mathf.Max(.0001f, Mathf.Max(F(cannonDirect), F(cannonBlast))),
                crit = damage.crit,
                procCoefficient = F(cannonDirectProc),
                position = impactInfo.estimatedPointOfImpact,
                damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Secondary)
            };
            health.TakeDamage(info);
            GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
            SetStateOnHurt hurtState = health.GetComponent<SetStateOnHurt>();
            if (hurtState) hurtState.SetStun(1f);
        }

        public sealed class TrashCannon : BaseState
        {
            public override void OnEnter()
            {
                base.OnEnter();
                if (!isAuthority) return;
                GameObject native = FindDrifterJunkProjectile();
                if (native && ProjectileCatalog.GetProjectileIndex(native) >= 0)
                {
                    Ray aim = GetAimRay();
                    int scrapCount = Mathf.Clamp(I(cannonCount), 1, 64);
                    float spread = Mathf.Clamp(F(cannonSpread), 0f, 89f);
                    Quaternion aimRotation = Util.QuaternionSafeLookRotation(aim.direction);
                    float damagePerScrap = damageStat * Mathf.Max(.0001f, Mathf.Max(F(cannonDirect), F(cannonBlast))) / 100f;
                    // One projectile travels down the crosshair. Remaining projectiles
                    // form a symmetric ring, so the spread stays centred on the aim ray.
                    for (int i = 0; i < scrapCount; i++)
                    {
                        Quaternion rotation = aimRotation;
                        if (i > 0)
                        {
                            float azimuth = 360f * (i - 1) / Mathf.Max(1, scrapCount - 1) + 45f;
                            rotation = aimRotation * Quaternion.AngleAxis(azimuth, Vector3.forward)
                                * Quaternion.AngleAxis(spread, Vector3.up);
                        }
                        ProjectileManager.instance.FireProjectile(new FireProjectileInfo
                        {
                            projectilePrefab = native,
                            position = aim.origin,
                            rotation = rotation,
                            owner = gameObject,
                            damage = damagePerScrap,
                            crit = RollCrit(),
                            speedOverride = Mathf.Max(1f, F(cannonSpeed))
                        });
                    }
                }
                else
                {
                    ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: native Drifter projectile unavailable or not registered; no fallback projectile fired.");
                }
                Util.PlaySound("Play_drifter_cleanup_throw_launch", gameObject);
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (fixedAge >= Mathf.Max(.05f, F(cannonInterval)) && isAuthority) outer.SetNextStateToMain();
            }
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.Skill;
        }
        private sealed class TrashProjectileImpact : MonoBehaviour
        {
            private ProjectileController controller;
            private ProjectileDamage damage;
            private Vector3 last;
            private float lived;
            private bool finished;
            private void Awake()
            {
                controller = GetComponent<ProjectileController>();
                damage = GetComponent<ProjectileDamage>(); last = transform.position;
            }
            private void FixedUpdate()
            {
                if (!NetworkServer.active || finished || !controller || !damage) return;
                lived += Time.fixedDeltaTime;
                Vector3 end = transform.position, delta = end - last;
                if (delta.sqrMagnitude > .0001f && Physics.SphereCast(last, .4f, delta.normalized, out RaycastHit hit,
                    delta.magnitude, LayerIndex.entityPrecise.mask | LayerIndex.world.mask, QueryTriggerInteraction.Collide))
                {
                    HurtBox hurt = hit.collider.GetComponent<HurtBox>();
                    HealthComponent health = hurt ? hurt.healthComponent : null;
                    if (health && health.alive && controller.owner)
                    {
                        CharacterBody attacker = controller.owner.GetComponent<CharacterBody>();
                        if (attacker && health.body && attacker.teamComponent && health.body.teamComponent &&
                            attacker.teamComponent.teamIndex != health.body.teamComponent.teamIndex)
                        {
                            DamageInfo info = new DamageInfo { attacker = controller.owner, inflictor = gameObject,
                                damage = damage.damage, crit = damage.crit, procCoefficient = F(cannonDirectProc),
                                position = hit.point, damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Secondary) };
                            health.TakeDamage(info); GlobalEventManager.instance.OnHitEnemy(info, health.gameObject);
                            var hurtState = health.GetComponent<SetStateOnHurt>();
                            if (hurtState) hurtState.SetStun(1f);
                        }
                    }
                    Explode(hit.point); return;
                }
                last = end;
                if (lived > 6f) Explode(end);
            }
            private void Explode(Vector3 point)
            {
                if (finished) return; finished = true;
                CharacterBody attacker = controller && controller.owner ? controller.owner.GetComponent<CharacterBody>() : null;
                if (attacker && attacker.teamComponent)
                    new BlastAttack { attacker = controller.owner, inflictor = gameObject,
                        teamIndex = attacker.teamComponent.teamIndex, position = point, radius = F(cannonRadius),
                        baseDamage = attacker.damage * F(cannonBlast) / 100f, crit = damage && damage.crit,
                        procCoefficient = F(cannonBlastProc), falloffModel = BlastAttack.FalloffModel.SweetSpot }.Fire();
                GameObject blastVisual = drifterImpactVisual;
                if (!blastVisual)
                {
                    GameObject donor = FindDrifterJunkProjectile();
                    ProjectileImpactExplosion impact = donor ? donor.GetComponent<ProjectileImpactExplosion>() : null;
                    if (impact && impact.impactEffect) blastVisual = drifterImpactVisual = impact.impactEffect;
                }
                if (!blastVisual)
                    blastVisual = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/OmniExplosionVFX");
                if (!blastVisual)
                    blastVisual = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/ImpactEffects/EngiGrenadeExplosion");
                if (blastVisual)
                    EffectManager.SpawnEffect(blastVisual, new EffectData { origin = point,
                        scale = blastVisual == drifterImpactVisual ? 1f : 2f * F(cannonRadius) }, true);
                else
                    ThreeQsVanillaTweaks.Log.Warning("Cleanup Trash Cannon: no explosion visual was resolved.");
                Util.PlaySound("Play_drifter_cleanup_throw_launch", gameObject);
                NetworkServer.Destroy(gameObject);
            }
        }
        private static Sprite trashCannonIcon;
        // A self-contained 96px skill icon: compressed scrap bursting from a cannon.
        // Uses the same cool steel / cyan palette as the Cleanup Drone's other icons.
        private static Sprite MakeTrashCannonIcon()
        {
            if (trashCannonIcon) return trashCannonIcon;
            const int size = 96;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "3QS_TrashCannonIconTexture";
            var pixels = new Color32[size * size];
            Color32 background = new Color32(31, 53, 69, 255);
            Color32 outline = new Color32(16, 35, 49, 255);
            Color32 steel = new Color32(117, 167, 188, 255);
            Color32 highlight = new Color32(200, 233, 240, 255);
            Color32 debris = new Color32(79, 123, 145, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Dark opaque background, with a subtle lighter centre.
                float dx = x - 48f, dy = y - 48f;
                bool muzzle = x >= 19 && x <= 35 && y >= 32 && y <= 60;
                bool barrel = x >= 35 && x <= 54 && y >= 37 && y <= 55;
                bool muzzleHole = x >= 23 && x <= 30 && y >= 37 && y <= 55;
                bool core = (dx - 17f) * (dx - 17f) + (dy + 1f) * (dy + 1f) < 14f * 14f;
                bool coreInner = (dx - 17f) * (dx - 17f) + (dy + 1f) * (dy + 1f) < 9f * 9f;
                bool shardA = x >= 69 && x <= 80 && y >= 64 && y <= 76 && x - y >= 0;
                bool shardB = x >= 74 && x <= 86 && y >= 19 && y <= 33 && x + y >= 103;
                bool shardC = x >= 53 && x <= 64 && y >= 70 && y <= 82 && x + y >= 130;
                Color32 colour = background;
                if (muzzle || barrel) colour = steel;
                if (muzzleHole) colour = outline;
                if (core) colour = debris;
                if (coreInner) colour = highlight;
                if (shardA || shardB || shardC) colour = highlight;
                // A scattering of small fragments in front of the barrel.
                if ((x >= 57 && x <= 62 && y >= 22 && y <= 27) ||
                    (x >= 78 && x <= 83 && y >= 45 && y <= 50) ||
                    (x >= 57 && x <= 62 && y >= 62 && y <= 67)) colour = steel;
                pixels[y * size + x] = colour;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            trashCannonIcon = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
            trashCannonIcon.name = "3QS_TrashCannonIcon";
            return trashCannonIcon;
        }

        private static Sprite recyclingIcon;
        private static Sprite MakeRecyclingIcon()
        {
            if (recyclingIcon) return recyclingIcon;
            const int size = 96;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "3QS_MassRecyclingIconTexture";
            Color32[] data = new Color32[size * size];
            Color32 metal = new Color32(116, 160, 184, 255);
            Color32 bright = new Color32(197, 235, 246, 255);
            Color32 core = new Color32(42, 92, 125, 255);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - 47.5f, dy = y - 47.5f;
                float radius = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx);
                // Twelve square teeth and a circular mechanical housing.
                bool tooth = radius >= 30f && radius < 38f &&
                    Mathf.Abs(Mathf.Sin(angle * 6f)) > .72f;
                bool ring = radius >= 26f && radius < 32f;
                bool centre = radius < 15f;
                bool inlet = Mathf.Abs(dx) < 5f && dy > -15f && dy < 15f;
                bool slot = Mathf.Abs(dy + 8f) < 3f && Mathf.Abs(dx) < 18f;
                if (ring || tooth) data[y * size + x] = metal;
                if (centre) data[y * size + x] = core;
                if (inlet || slot) data[y * size + x] = bright;
            }
            texture.SetPixels32(data);
            texture.Apply(false, false);
            recyclingIcon = Sprite.Create(texture, new Rect(0, 0, size, size),
                new Vector2(.5f, .5f), 100f);
            recyclingIcon.name = "3QS_MassRecyclingIcon";
            return recyclingIcon;
        }

        // Recolour the vanilla REX icon without touching the shared REX texture.
        // ReadPixels also works when the original atlas is marked non-readable.
        private static Sprite MakeMechanicalIcon(Sprite original)
        {
            if (!original || !original.texture) return original;
            RenderTexture temporary = null;
            RenderTexture previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                Rect rect = original.textureRect;
                int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
                int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
                temporary = RenderTexture.GetTemporary(original.texture.width, original.texture.height, 0,
                    RenderTextureFormat.ARGB32);
                Graphics.Blit(original.texture, temporary);
                RenderTexture.active = temporary;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
                Color[] colours = pixels.GetPixels();
                for (int i = 0; i < colours.Length; i++)
                {
                    Color c = colours[i];
                    float lum = .2126f * c.r + .7152f * c.g + .0722f * c.b;
                    Color tinted = new Color(Mathf.Clamp01(lum * .70f),
                        Mathf.Clamp01(lum * .88f), Mathf.Clamp01(lum * 1.13f), 1f);
                    // Composite transparent pixels against an opaque steel-blue
                    // background to match the other HUD skill tiles.
                    Color background = new Color(.12f, .20f, .26f, 1f);
                    colours[i] = Color.Lerp(background, tinted, c.a);
                }
                pixels.SetPixels(colours);
                pixels.Apply(false, false);
                return Sprite.Create(pixels, new Rect(0f, 0f, width, height),
                    new Vector2(.5f, .5f), original.pixelsPerUnit);
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: icon recolour failed: " + e.Message);
                if (pixels) UnityEngine.Object.Destroy(pixels);
                return original;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }

        // REX stores the sound and the effect in its serialized state configuration.
        // The effect is an EntityStateGameObject, NOT a UnityEngine.GameObject.
        private static void LoadRexSonicBoomEffect()
        {
            if (rexSonicBoomEffect != null) return;
            try
            {
                EntityStates.Treebot.Weapon.FireSonicBoom state =
                    new EntityStates.Treebot.Weapon.FireSonicBoom();
                EntityStateConfiguration config = LegacyResourcesAPI.Load<EntityStateConfiguration>(
                    "EntityStateConfigurations/EntityStates.Treebot.Weapon.FireSonicBoom");
                if (!config)
                {
                    // The resources path differs across RoR2 releases. Check already
                    // loaded configurations before falling back to a known local effect.
                    foreach (EntityStateConfiguration candidate in Resources.FindObjectsOfTypeAll<EntityStateConfiguration>())
                    {
                        if (candidate && candidate.name.IndexOf("FireSonicBoom", StringComparison.OrdinalIgnoreCase) >= 0)
                        { config = candidate; break; }
                    }
                }
                if (config) config.BuildInstanceInitializer()?.Invoke(state);
                rexSonicBoomEffect = state.fireEffectPrefab;
                rexSonicBoomSound = state.sound;
                if (rexSonicBoomEffect == null)
                    ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: REX fireEffectPrefab was not resolved from its state configuration.");
                else
                    ThreeQsVanillaTweaks.Log.Info("Cleanup Sonic Boom: REX effect resolved from state configuration.");
                if (string.IsNullOrEmpty(rexSonicBoomSound))
                    ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: REX state sound was not configured; using fallback events.");
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: REX state-configuration lookup failed: " + e.Message);
            }
        }
        private static bool HasMotorSetPosition(Type motorType)
        {
            foreach (MethodInfo method in motorType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (method.Name == "SetPosition") return true;
            return false;
        }

        // REX supplies a complete, game-compatible effect prefab. Do not construct a
        // particle renderer: Shader.Find("Particles/Standard Unlit") throws in RoR2's
        // LegacyShaderAPI, aborting the entire skill before audio and damage execute.
        private static void PlaySonicBoomEffect(Vector3 origin, Vector3 direction)
        {
            if (rexSonicBoomEffect == null) LoadRexSonicBoomEffect();
            if (rexSonicBoomEffect == null) return;
            try
            {
                EffectManager.SpawnEffect(rexSonicBoomEffect, new EffectData {
                    origin = origin, rotation = Util.QuaternionSafeLookRotation(direction)
                }, false);
            }
            catch (Exception error)
            {
                ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: native effect failed: " + error.Message);
            }
        }

        public sealed class SuctionBoost : BaseState
        {
            private Vector3 forward;
            private bool launched;
            public override void OnEnter()
            {
                base.OnEnter();
                forward = GetAimRay().direction.normalized;
                if (forward.sqrMagnitude < .01f) forward = transform.forward;
                Vector3 origin = characterBody ? characterBody.corePosition : transform.position;
                // Never let a missing visual abort sound, damage, or recoil.
                // REX's configured audio is loaded from the same state as its VFX.
                Util.PlaySound(!string.IsNullOrEmpty(rexSonicBoomSound)
                    ? rexSonicBoomSound : "Play_treeBot_shift_shoot", gameObject);
                PlaySonicBoomEffect(origin, forward);

                // Physical recoil movement uses the existing drone dash mover, which
                // supports the drone's flight controller more reliably than velocity.
                if (NetworkServer.active)
                {
                    float range = Mathf.Max(.1f, F(boostRange));
                    // BullseyeSearch only returns targetable hurtboxes; it misses
                    // ordinary enemies in some drone configurations. Use the actual
                    // physics hurtbox colliders and apply the cone test ourselves.
                    Collider[] nearby = Physics.OverlapSphere(origin, range, ~0, QueryTriggerInteraction.Collide);
                    float minDot = Mathf.Cos(Mathf.Clamp(F(boostHalfAngle), 1f, 179f) * Mathf.Deg2Rad);
                    HashSet<HealthComponent> hit = new HashSet<HealthComponent>();
                    int damagedTargets = 0;
                    foreach (Collider collider in nearby)
                    {
                        if (!collider) continue;
                        HurtBox hb = collider.GetComponent<HurtBox>() ?? collider.GetComponentInParent<HurtBox>();
                        if (!hb || !hb.healthComponent) continue;
                        HealthComponent enemy = hb.healthComponent;
                        if (!enemy.alive || !enemy.body ||
                            !FriendlyFireManager.ShouldSplashHitProceed(enemy, GetTeam())) continue;
                        Vector3 delta = enemy.body.corePosition - origin;
                        if (delta.sqrMagnitude > range * range ||
                            (delta.sqrMagnitude > .01f && Vector3.Dot(delta.normalized, forward) < minDot)) continue;
                        if (!hit.Add(enemy)) continue;
                        Vector3 push = delta.sqrMagnitude > .01f ? delta.normalized : forward;
                        DamageInfo damageInfo = new DamageInfo {
                            attacker = gameObject, inflictor = gameObject,
                            damage = damageStat * Mathf.Max(0f, F(boostDamage)) / 100f,
                            crit = RollCrit(),
                            procCoefficient = Mathf.Max(0f, F(boostProc)), position = hb.transform.position,
                            force = Vector3.zero,
                            inflictedHurtbox = hb,
                            damageType = new DamageTypeCombo(DamageType.Generic, DamageTypeExtended.Generic, DamageSource.Utility)
                        };
                        // Follow REX: use TakeDamageForce for knockback, so even
                        // heavy character motors receive it, then apply damage.
                        enemy.TakeDamageForce(push * Mathf.Max(0f, F(boostKnockback)), true, true);
                        enemy.TakeDamage(damageInfo);
                        damagedTargets++;
                        if (GlobalEventManager.instance)
                            GlobalEventManager.instance.OnHitEnemy(damageInfo, enemy.gameObject);
                    }
                    if (damagedTargets == 0)
                        ThreeQsVanillaTweaks.Log.Warning($"Cleanup Sonic Boom: physics query returned {nearby.Length} colliders, but no valid enemies inside the cone.");
                }
            }
            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!isAuthority) return;

                // A single impulse, not a scripted dash. The drone keeps its
                // momentum after this skill state has returned to its normal state.
                if (!launched)
                {
                    launched = true;
                    float speed = Mathf.Max(0f, F(boostLaunchSpeed));
                    CleanupResources resources = gameObject.GetComponent<CleanupResources>();
                    if (resources && resources.vacuuming)
                        speed *= 1f - Mathf.Clamp01(F(vacuumSpeedPenalty) / 100f);
                    ApplyRecoilVelocity(-forward * speed);
                }

                if (fixedAge >= .1f)
                    outer.SetNextStateToMain();
            }

            private void ApplyRecoilVelocity(Vector3 deltaVelocity)
            {
                if (deltaVelocity.sqrMagnitude <= 0f) return;

                // Flight-controlled drones use PseudoCharacterMotor. Applying the
                // impulse there (rather than moving the transform) allows the normal
                // flight physics to carry it forwards and gradually slow it down.
                Component pseudo = gameObject.GetComponent("PseudoCharacterMotor");
                if (pseudo)
                {
                    try
                    {
                        Type type = pseudo.GetType();
                        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                        PropertyInfo property = type.GetProperty("velocityAuthority", flags);
                        if (property != null && property.CanRead && property.CanWrite && property.PropertyType == typeof(Vector3))
                        {
                            Vector3 current = (Vector3)property.GetValue(pseudo, null);
                            property.SetValue(pseudo, current + deltaVelocity, null);
                            return;
                        }
                        FieldInfo field = type.GetField("velocityAuthority", flags);
                        if (field != null && field.FieldType == typeof(Vector3))
                        {
                            field.SetValue(pseudo, (Vector3)field.GetValue(pseudo) + deltaVelocity);
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        ThreeQsVanillaTweaks.Log.Warning("Cleanup Sonic Boom: flight velocity impulse failed: " + e.Message);
                    }
                }

                if (characterMotor)
                {
                    // CharacterMotor is used by some alternate drone bodies.
                    characterMotor.velocity += deltaVelocity;
                    return;
                }

                Rigidbody rb = gameObject.GetComponent<Rigidbody>();
                if (rb && !rb.isKinematic)
                    rb.velocity += deltaVelocity;
            }

            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
        private sealed class CleanupTargetIndicator : MonoBehaviour
        {
            private Indicator indicator;
            private CharacterBody owner;
            private InputBankTest input;
            private void Awake()
            {
                owner = GetComponent<CharacterBody>();
                input = GetComponent<InputBankTest>();
                GameObject prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator");
                if (prefab) indicator = new Indicator(gameObject, prefab);
            }
            private void FixedUpdate()
            {
                if (indicator == null) return;
                // Only display a valid execution target when Mass Recycling can
                // actually be activated. Do not leave the marker visible during cooldown.
                GenericSkill specialSlot = owner && owner.skillLocator ? owner.skillLocator.special : null;
                bool ready = specialSlot && specialSlot.skillDef == special &&
                    specialSlot.stock >= Mathf.Max(1, specialSlot.skillDef.requiredStock);
                CharacterBody target = ready && owner && owner.hasEffectiveAuthority && input
                    ? FindEnemy(input.GetAimRay(), owner, F(massRange), F(massThreshold) / 100f) : null;
                indicator.active = target;
                indicator.targetTransform = target ? target.transform : null;
            }
            private void OnDisable() { if (indicator != null) indicator.active = false; }
        }
        public sealed class MassRecycling : BaseState
        {
            private CharacterBody target;
            private CharacterBody protectedTarget;
            private Transform scrappingEffect;
            private float baseHealth, processingHealth, processingAge, processDuration;
            private bool executed, failed, cooldownScheduled, immunityApplied, processingEffectActive;
            private CleanupResources resources;
            private GenericSkill specialSkill;

            public override void OnEnter()
            {
                base.OnEnter();
                resources = GetComponent<CleanupResources>();
                specialSkill = skillLocator ? skillLocator.special : null;
                target = AimEnemy(this, F(massRange), F(massThreshold) / 100f);
                if (!target || (target.healthComponent && target.healthComponent.godMode)) { Fail(); return; }

                // Reuse the actual native Vacuum effect when that state is active.
                if (!nativeRecyclingSuckEffect)
                {
                    foreach (EntityStateMachine machine in gameObject.GetComponents<EntityStateMachine>())
                    {
                        var native = machine.state as EntityStates.Drone.Cleanup;
                        if (native != null && native.suckEffectPrefab && native.suckEffectPrefab.gameObject)
                        {
                            nativeRecyclingSuckEffect = native.suckEffectPrefab.gameObject;
                            break;
                        }
                    }
                }

                baseHealth = BaseHealth(target);
                processingHealth = EliteAdjustedBaseHealth(target, baseHealth);

                // Lock other abilities and stop the native vacuum from the moment the
                // execution is triggered, not only when the victim reaches the drone.
                if (resources)
                {
                    resources.processing = true;
                    resources.recyclingLockoutRemaining = 1f;
                    resources.requestVacuumStop = true;
                }
                if (characterBody) characterBody.MarkAllStatsDirty();

                // Only the server owns the temporary immunity. Using a single buff
                // stack avoids resetting any immunity that other systems may grant.
                if (NetworkServer.active)
                {
                    target.AddBuff(RoR2Content.Buffs.Immune);
                    protectedTarget = target;
                    immunityApplied = true;
                    SetStateOnHurt hurtState = target.GetComponent<SetStateOnHurt>();
                    if (hurtState) hurtState.SetStun(Mathf.Max(.35f, F(massCapture)));
                }
                Util.PlaySound("Play_GG_CleanupDrone_Vacuum_AOE", gameObject);
            }

            private void ReleaseTargetImmunity()
            {
                if (!immunityApplied) return;
                if (NetworkServer.active && protectedTarget)
                    protectedTarget.RemoveBuff(RoR2Content.Buffs.Immune);
                immunityApplied = false;
                protectedTarget = null;
            }

            private void StartNativeProcessingEffects()
            {
                if (processingEffectActive) return;
                processingEffectActive = true;
                scrappingEffect = FindModelChild("ScrappingEffect");
                if (scrappingEffect) scrappingEffect.gameObject.SetActive(true);
                if ((!resources || !resources.DeferredProcessingAudioActive) &&
                    !string.IsNullOrEmpty(EntityStates.Drone.Cleanup.processSoundString))
                    Util.PlaySound(EntityStates.Drone.Cleanup.processSoundString, gameObject);
            }

            private void StopNativeProcessingEffects()
            {
                if (!processingEffectActive) return;
                processingEffectActive = false;
                // The two recycling mechanisms share the same looping Wwise sound
                // and model child. Only the final one to finish should stop them.
                if (resources && resources.HasDeferredProcessing)
                {
                    resources.StartDeferredProcessingAudio();
                    return;
                }
                if (scrappingEffect) scrappingEffect.gameObject.SetActive(false);
                if (!string.IsNullOrEmpty(EntityStates.Drone.Cleanup.endProcessSoundString))
                    Util.PlaySound(EntityStates.Drone.Cleanup.endProcessSoundString, gameObject);
            }

            private void Fail()
            {
                failed = true;
                ReleaseTargetImmunity();
                StopNativeProcessingEffects();
                if (specialSkill && !cooldownScheduled)
                {
                    cooldownScheduled = true;
                    var adjust = GetComponent<VacuumCooldownAdjust>() ?? gameObject.AddComponent<VacuumCooldownAdjust>();
                    adjust.Schedule(specialSkill, F(massFailedCooldown));
                }
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (failed) { if (isAuthority) outer.SetNextStateToMain(); return; }
                if (!executed)
                {
                    if (resources) resources.recyclingLockoutRemaining =
                        1f - Mathf.Clamp01(fixedAge / Mathf.Max(.1f, F(massCapture)));
                    if (!target || !target.healthComponent || !target.healthComponent.alive ||
                        (!immunityApplied && target.healthComponent.godMode))
                    { Fail(); return; }
                    // Use hurtbox surfaces for both movement and contact. Pulling body
                    // centres into each other can embed large colliders in geometry;
                    // collision resolution then flings them vertically.
                    Vector3 droneCentre = characterBody ? characterBody.corePosition : transform.position;
                    Vector3 dronePoint = droneCentre;
                    if (characterBody && characterBody.mainHurtBox && characterBody.mainHurtBox.collider)
                        dronePoint = characterBody.mainHurtBox.collider.ClosestPoint(target.corePosition);
                    Vector3 targetPoint = target.corePosition;
                    if (target.mainHurtBox && target.mainHurtBox.collider)
                        targetPoint = target.mainHurtBox.collider.ClosestPoint(dronePoint);
                    Vector3 gap = dronePoint - targetPoint;
                    float gapDistance = gap.magnitude;
                    const float contactDistance = 1.5f;
                    bool isTouching = gapDistance <= contactDistance;

                    if (NetworkServer.active)
                    {
                        SetStateOnHurt stun = target.GetComponent<SetStateOnHurt>();
                        if (stun) stun.SetStun(.35f);

                        // Zero inherited launch/knockback momentum both before and
                        // after movement. Also handles flying rigidbody-based enemies.
                        if (target.characterMotor)
                            target.characterMotor.velocity = Vector3.zero;
                        Rigidbody physicsBody = target.GetComponent<Rigidbody>();
                        if (physicsBody && !physicsBody.isKinematic)
                        {
                            physicsBody.velocity = Vector3.zero;
                            physicsBody.angularVelocity = Vector3.zero;
                        }

                        if (!isTouching && gapDistance > .0001f)
                        {
                            // Never move farther than the remaining surface gap.
                            // Keeping a slight margin avoids body interpenetration
                            // and the large upwards impulses that can follow.
                            float speed = Mathf.Clamp(gapDistance * 3f, 20f, 75f);
                            float distance = Mathf.Min(speed * Time.fixedDeltaTime,
                                Mathf.Max(0f, gapDistance - 0.35f));
                            Vector3 displacement = gap / gapDistance * distance;
                            // Grounded enemies use a KinematicCharacterMotor that
                            // overwrites transform.position each update. Move the
                            // underlying motor, never TeleportHelper (which may snap
                            // large bodies onto high/invalid navmesh positions).
                            Vector3 nextPosition = target.transform.position + displacement;
                            bool positioned = false;
                            if (target.characterMotor)
                            {
                                object motor = null;
                                for (Type t = target.characterMotor.GetType(); t != null && motor == null; t = t.BaseType)
                                {
                                    const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                                    foreach (FieldInfo field in t.GetFields(flags))
                                        if (field.Name.IndexOf("motor", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                            field.FieldType != typeof(CharacterMotor))
                                        {
                                            object candidate = field.GetValue(target.characterMotor);
                                            if (candidate != null && HasMotorSetPosition(candidate.GetType()))
                                            { motor = candidate; break; }
                                        }
                                    if (motor == null)
                                        foreach (PropertyInfo property in t.GetProperties(flags))
                                            if (property.Name.IndexOf("motor", StringComparison.OrdinalIgnoreCase) >= 0 && property.CanRead)
                                            {
                                                object candidate = null;
                                                try { candidate = property.GetValue(target.characterMotor, null); } catch { }
                                                if (candidate != null && HasMotorSetPosition(candidate.GetType()))
                                                { motor = candidate; break; }
                                            }
                                }
                                if (motor != null)
                                {
                                    try
                                    {
                                        // KCC overloads may differ across game releases.
                                        foreach (MethodInfo method in motor.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                        {
                                            if (method.Name != "SetPosition") continue;
                                            ParameterInfo[] args = method.GetParameters();
                                            if (args.Length == 1 && args[0].ParameterType == typeof(Vector3))
                                            { method.Invoke(motor, new object[] { nextPosition }); positioned = true; break; }
                                            if (args.Length == 2 && args[0].ParameterType == typeof(Vector3) && args[1].ParameterType == typeof(bool))
                                            { method.Invoke(motor, new object[] { nextPosition, true }); positioned = true; break; }
                                        }
                                    }
                                    catch (Exception e)
                                    { ThreeQsVanillaTweaks.Log.Warning("Cleanup Mass Recycling: motor pull failed: " + e.Message); }
                                }
                                if (!positioned)
                                {
                                    // Unlike a transform move, velocity is integrated by
                                    // CharacterMotor even on grounded characters.
                                    target.characterMotor.velocity = displacement / Mathf.Max(Time.fixedDeltaTime, .001f);
                                }
                            }
                            if (!positioned && !target.characterMotor && physicsBody)
                            {
                                physicsBody.MovePosition(nextPosition);
                                positioned = true;
                            }
                            if (!positioned && !target.characterMotor && !physicsBody)
                                target.transform.position = nextPosition;
                            if (physicsBody && !physicsBody.isKinematic)
                            {
                                physicsBody.velocity = Vector3.zero;
                                physicsBody.angularVelocity = Vector3.zero;
                            }
                            Physics.SyncTransforms();
                        }
                        // Test contact again after movement instead of waiting for
                        // the following frame, without making the bodies overlap.
                        if (!isTouching)
                        {
                            dronePoint = droneCentre;
                            if (characterBody && characterBody.mainHurtBox && characterBody.mainHurtBox.collider)
                                dronePoint = characterBody.mainHurtBox.collider.ClosestPoint(target.corePosition);
                            targetPoint = target.corePosition;
                            if (target.mainHurtBox && target.mainHurtBox.collider)
                                targetPoint = target.mainHurtBox.collider.ClosestPoint(dronePoint);
                            isTouching = (targetPoint - dronePoint).sqrMagnitude <= contactDistance * contactDistance;
                        }
                    }
                    if (isTouching)
                    {
                        if (NetworkServer.active)
                        {
                            // Immunity must be released before the actual execute.
                            ReleaseTargetImmunity();
                            target.healthComponent.NetworkdestroyModelOnDeath = true;
                            target.healthComponent.Suicide(gameObject, gameObject, DamageType.Generic);
                            executed = !target.healthComponent.alive;
                        }
                        else executed = true;
                        if (!executed && NetworkServer.active) { Fail(); return; }

                        // Reproduce the native death-to-drone suction tether effect.
                        // Cache the source prefab from the vanilla Vacuum state so its
                        // correct DLC asset is used instead of guessing a resource path.
                        if (nativeRecyclingSuckEffect && target)
                        {
                            ChildLocator children = GetModelChildLocator();
                            if (children)
                            {
                                EffectData effectData = new EffectData
                                {
                                    origin = target.corePosition,
                                    genericFloat = .33f
                                };
                                effectData.SetChildLocatorTransformReference(gameObject, children.FindChildIndex("Muzzle"));
                                EffectManager.SpawnEffect(nativeRecyclingSuckEffect, effectData, true);
                            }
                        }
                        StartNativeProcessingEffects();
                        processDuration = Mathf.Min(F(massMaxProcess), F(massBaseProcess) +
                            processingHealth / Mathf.Max(1f, F(massHealthDivisor)));
                    }
                    else if (fixedAge >= Mathf.Max(.1f, F(massCapture)))
                    { Fail(); return; }
                    return;
                }

                processingAge += Time.fixedDeltaTime;
                if (resources) resources.recyclingLockoutRemaining =
                    1f - Mathf.Clamp01(processingAge / Mathf.Max(.1f, processDuration));
                if (processingAge < processDuration) return;
                if (NetworkServer.active)
                {
                    Util.PlaySound("Play_GG_CleanupDrone_DropHealingOrb", gameObject);
                    ProcessRewards(characterBody, baseHealth);
                }
                StopNativeProcessingEffects();
                if (isAuthority) outer.SetNextStateToMain();
            }

            public override void OnExit()
            {
                ReleaseTargetImmunity();
                StopNativeProcessingEffects();
                if (resources) { resources.processing = false; resources.recyclingLockoutRemaining = 1f; }
                if (characterBody) characterBody.MarkAllStatsDirty();
                base.OnExit();
            }

            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
    }
}
