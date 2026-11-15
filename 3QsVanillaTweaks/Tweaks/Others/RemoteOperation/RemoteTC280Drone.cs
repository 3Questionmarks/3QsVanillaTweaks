using System;
using System.Linq;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Drone.DroneWeapon;
using EntityStates.Toolbot;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.UI;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;
using LG = Tweaks.ModCompatibility.LookingGlassSkillStats;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteTC280Drone
    {
        internal const string Root = "Remote Operation - TC-280 Prototype";
        private const string GunName = "Heavy Machine Gun", RocketName = "Twin Rocket";
        private const string ThrusterName = "Auxiliary Thrusters", ArsenalName = "Full Arsenal";
        private const string GunMachine = "3QSTC280Gun", RocketMachine = "3QSTC280Rocket";
        private const string ThrusterMachine = "3QSTC280Thruster", ArsenalMachine = "3QSTC280Arsenal";
        private const string OverheatSound = "Play_item_proc_igniteOnKill_Loop";
        private const float MinimumCooldown = .5f;
        private const GenericSkill.SkillOverridePriority ArsenalOverride = GenericSkill.SkillOverridePriority.Upgrade;

        internal static readonly Type[] States = { typeof(MachineGun), typeof(LaunchRockets),
            typeof(Thrusters), typeof(Arsenal) };

        private static ConfigEntry<bool> enabled, gunOn, rocketOn, thrusterOn, arsenalOn, attackSpeedScaling;
        private static ConfigEntry<float> gunDamage, gunProc, startRate, maxRate, spinUp, wideSpread, tightSpread, firingSlow;
        private static ConfigEntry<float> spinDown, spinDownDelay, heatBuildTime, heatCoolDelay, heatCoolTime;
        private static ConfigEntry<float> rocketDamage, rocketRadius, rocketRecharge, rocketProcOverride, rocketSalvoInterval;
        private static ConfigEntry<float> thrustSpeed, fuelCapacity, fuelRechargeDelay, fuelRefillTime, fuelPerStock;
        private static ConfigEntry<float> arsenalDuration, arsenalRecharge, arsenalStartRate, arsenalMaxRate, arsenalSpinUp,
            arsenalWideSpread, arsenalTightSpread, arsenalRocketRate, lockRange, lockAngle, turnSpeed;

        private static SkillDef primary, secondary, utility, special, arsenalPrimary, arsenalSecondary;
        private static BuffDef thrusterBuff, arsenalBuff;
        private static GameObject rocketPrefab, arsenalActivationEffect, arsenalAuraPrefab, barrelFirePrefab;
        private static bool assetsLoaded;
        [ThreadStatic] private static CharacterBody firingRockets;
        [ThreadStatic] private static Vector3 rocketDestination;
        [ThreadStatic] private static HurtBox rocketTarget;

        private static readonly System.Reflection.MethodInfo SetMoveSpeed =
            AccessTools.PropertySetter(typeof(CharacterBody), nameof(CharacterBody.moveSpeed));
        private static float Value(ConfigEntry<float> entry) => Mathf.Max(0f, C.Get(entry));
        private static float PositiveTime(ConfigEntry<float> entry) => Mathf.Max(.02f, Value(entry));
        private static ConfigEntry<float> Bind(string group, string key, float value, string description) =>
            RemoteDroneSkills.Number(Root + " - " + group, key, value, description);
        private static ConfigEntry<bool> Toggle(string group) =>
            C.BindStartupBool(Root + " - " + group, "Changes Enabled", true, "Enable this skill for the player-controlled TC-280. Restart to apply.");

        internal static void Bind()
        {
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable the player-controlled TC-280 skill kit. Restart to apply.");
            gunOn = Toggle(GunName);
            rocketOn = Toggle(RocketName);
            thrusterOn = Toggle(ThrusterName);
            arsenalOn = Toggle(ArsenalName);

            gunDamage = Bind(GunName, "Bullet Damage (%)", 220f, "Damage per bullet.");
            gunProc = Bind(GunName, "Proc Coefficient", .5f, "Proc coefficient per bullet in both firing modes.");
            startRate = Bind(GunName, "Initial Fire Rate (shots/s)", 10f / 3f, "Fire rate before spin-up at base attack speed.");
            maxRate = Bind(GunName, "Full Spin Fire Rate (shots/s)", 10f, "Fire rate at full spin-up and base attack speed.");
            spinUp = Bind(GunName, "Spin-Up Time (s)", 4f, "Seconds of held fire to reach full fire rate and accuracy.");
            wideSpread = Bind(GunName, "Initial Spread (degrees)", 8f, "Maximum spread when starting to fire.");
            tightSpread = Bind(GunName, "Full Spin Spread (degrees)", 1f, "Maximum spread after spin-up.");
            firingSlow = Bind(GunName, "Movement Penalty (%)", 40f, "Movement speed reduction while firing.");
            attackSpeedScaling = C.BindBool(Root + " - " + GunName, "Scale Fire Rate With Attack Speed", true,
                "Attack speed scales the initial and full-spin fire rates in both modes. Spin-up time stays fixed.");
            spinDown = Bind(GunName, "Spin-Down Time (s)", 1f, "Time for fully spun-up barrels to stop after releasing Primary.");
            spinDownDelay = Bind(GunName, "Spin-Down Delay (s)", .5f, "Time after releasing Primary before the barrels begin slowing down.");
            heatBuildTime = Bind(GunName, "Time To Overheat (s)", 14f, "Continuous firing time from cold to overheated, in either firing mode.");
            heatCoolDelay = Bind(GunName, "Cooling Delay (s)", 1f, "Time after firing stops before heat begins to dissipate.");
            heatCoolTime = Bind(GunName, "Cooling Time (s)", 5f, "Time to cool completely from maximum heat once cooling begins.");

            rocketDamage = Bind(RocketName, "Damage Per Rocket (%)", 400f, "Damage dealt to each enemy in a rocket's blast.");
            rocketRadius = Bind(RocketName, "Blast Radius (m)", 6f, "Damage radius per rocket.");
            rocketRecharge = Bind(RocketName, "Recharge (s)", 6f, "Base time to regain one rocket salvo.");
            rocketProcOverride = Bind(RocketName, "Proc Coefficient Override", -1f, "Effective proc coefficient per explosion. Use -1 for the native rocket value.");
            rocketSalvoInterval = Bind(RocketName, "Salvo Interval (s)", .5f, "Minimum time between salvos when holding Secondary.");

            thrustSpeed = Bind(ThrusterName, "Thrust Speed (m/s)", 20f, "Additional flight velocity the motor accelerates toward in the movement direction while boosting.");
            fuelCapacity = Bind(ThrusterName, "Fuel Capacity (s)", 5f, "Continuous boost time on a full fuel gauge.");
            fuelRechargeDelay = Bind(ThrusterName, "Fuel Recharge Delay (s)", 1f, "Cooldown after boosting stops and before fuel starts refilling.");
            fuelRefillTime = Bind(ThrusterName, "Fuel Refill Time (s)", 12f, "Time to refill one base tank before cooldown reduction.");
            fuelPerStock = Bind(ThrusterName, "Extra Fuel Per Stock (%)", 50f,
                "When you have more than one utility stock, each stock adds this percentage of the base fuel capacity.");

            arsenalDuration = Bind(ArsenalName, "Duration (s)", 15f, "Time spent in Full Arsenal.");
            arsenalRecharge = Bind(ArsenalName, "Cooldown After Expiry (s)", 30f, "Cooldown beginning when Full Arsenal ends.");
            arsenalStartRate = Bind(ArsenalName, "Initial Fire Rate (shots/s)", 5f, "Initial gun fire rate at base attack speed.");
            arsenalMaxRate = Bind(ArsenalName, "Full Spin Fire Rate (shots/s)", 20f, "Gun fire rate at full spin-up and base attack speed.");
            arsenalSpinUp = Bind(ArsenalName, "Spin-Up Time (s)", 2f, "Seconds of held fire to reach full fire rate and accuracy.");
            arsenalWideSpread = Bind(ArsenalName, "Initial Spread (degrees)", 5f, "Maximum spread when starting to fire.");
            arsenalTightSpread = Bind(ArsenalName, "Full Spin Spread (degrees)", 0f, "Maximum spread after spin-up.");
            arsenalRocketRate = Bind(ArsenalName, "Rocket Recharge Speed Multiplier", 4f, "Twin Rocket recharge speed while Full Arsenal is active.");
            lockRange = Bind(ArsenalName, "Rocket Lock-On Range (m)", 100f, "Maximum range for locking onto a visible enemy.");
            lockAngle = Bind(ArsenalName, "Rocket Lock-On Half Angle (degrees)", 25f, "Maximum angle between your aim and the lock-on target.");
            turnSpeed = Bind(ArsenalName, "Rocket Turn Speed (degrees/s)", 360f, "Maximum turn speed of each homing rocket.");
        }

        internal static BuffDef[] Buffs => thrusterBuff ? new[] { thrusterBuff, arsenalBuff } : Array.Empty<BuffDef>();

        internal static void Register()
        {
            if (!enabled.Value) return;
            primary = RemoteDroneSkills.MakeSkill("TC280MachineGun", GunName, "", typeof(MachineGun), GunMachine, 0f, null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>(), useStockConfig: false);
            secondary = RemoteDroneSkills.MakeSkill("TC280TwinRocket", RocketName, "", typeof(LaunchRockets), RocketMachine, Value(rocketRecharge), null,
                customDefinition: ScriptableObject.CreateInstance<RocketSkill>());
            utility = RemoteDroneSkills.MakeSkill("TC280Thrusters", ThrusterName, "", typeof(Thrusters), ThrusterMachine, Value(fuelRechargeDelay), null,
                beginsOnEnd: true, customDefinition: ScriptableObject.CreateInstance<ThrusterSkill>());
            special = RemoteDroneSkills.MakeSkill("TC280Arsenal", ArsenalName, "", typeof(Arsenal), ArsenalMachine, Value(arsenalRecharge), null,
                beginsOnEnd: true, customDefinition: ScriptableObject.CreateInstance<SkillDef>());
            arsenalPrimary = RemoteDroneSkills.MakeSkill("TC280ArsenalMachineGun", "Overdriven Machine Gun", "", typeof(MachineGun), GunMachine, 0f, null,
                customDefinition: ScriptableObject.CreateInstance<SkillDef>(), useStockConfig: false);
            arsenalSecondary = RemoteDroneSkills.MakeSkill("TC280ArsenalTwinRocket", "Homing Twin Rocket", "", typeof(LaunchRockets), RocketMachine, Value(rocketRecharge), null,
                customDefinition: ScriptableObject.CreateInstance<RocketSkill>(), useStockConfig: false);
            primary.stockToConsume = arsenalPrimary.stockToConsume = 0;
            primary.hideCooldown = arsenalPrimary.hideCooldown = true;
            arsenalPrimary.fullRestockOnAssign = arsenalSecondary.fullRestockOnAssign = false;
            secondary.resetCooldownTimerOnUse = false;
            secondary.mustKeyPress = arsenalSecondary.mustKeyPress = false;
            special.mustKeyPress = true;
            special.isCooldownBlockedUntilManuallyReset = true;
            utility.isCombatSkill = false;
            thrusterBuff = MakeBuff("3QSTC280Thrusters", new Color(1f, .68f, .29f));
            arsenalBuff = MakeBuff("3QSTC280FullArsenal", new Color(1f, .44f, .18f));
            Refresh();
        }

        private static BuffDef MakeBuff(string name, Color colour)
        {
            var buff = ScriptableObject.CreateInstance<BuffDef>();
            buff.name = name;
            buff.buffColor = colour;
            return buff;
        }

        internal static void Refresh()
        {
            if (!primary) return;
            secondary.baseRechargeInterval = Value(rocketRecharge);
            arsenalSecondary.baseRechargeInterval = Mathf.Max(.5f, Value(rocketRecharge) / RocketRate);
            arsenalSecondary.baseMaxStock = secondary.baseMaxStock;
            arsenalSecondary.requiredStock = secondary.requiredStock;
            arsenalSecondary.rechargeStock = secondary.rechargeStock;
            secondary.stockToConsume = arsenalSecondary.stockToConsume = 1;
            utility.baseRechargeInterval = Mathf.Max(MinimumCooldown, Value(fuelRechargeDelay));
            utility.stockToConsume = 1;
            special.baseRechargeInterval = Value(arsenalRecharge);
            S.SetSkillText(primary, () => GunText(spinUp, false));
            S.SetSkillText(arsenalPrimary, () => GunText(arsenalSpinUp, true));
            S.SetSkillText(secondary, RocketText);
            S.SetSkillText(arsenalSecondary, () => RocketText() +
                $" Rockets home onto the marked enemy within <style=cIsUtility>{Value(lockRange):0.##}m</style>.");
            S.SetSkillText(utility, () => $"Hold to thrust in the direction you move. A full base tank lasts <style=cIsUtility>{Value(fuelCapacity):0.##}s</style>. Fuel refills after the cooldown. Extra utility stocks increase fuel capacity.");
            S.SetSkillText(special, () => $"<style=cIsUtility>Overdrive</style> the Heavy Machine Gun and Twin Rocket for <style=cIsUtility>{Value(arsenalDuration):0.##}s</style>.");
            LG.Register(primary, () => $"\nFire Rate: {Value(startRate):0.##}–{Value(maxRate):0.##}/s at base attack speed\nSpread: {Value(wideSpread):0.##}° to {Value(tightSpread):0.##}°\nAttack Speed Scaling: {(C.Get(attackSpeedScaling) ? "Yes" : "No")}", () => Value(gunProc));
            LG.Register(arsenalPrimary, () => $"\nFire Rate: {Value(arsenalStartRate):0.##}–{Value(arsenalMaxRate):0.##}/s at base attack speed\nSpread: {Value(arsenalWideSpread):0.##}° to {Value(arsenalTightSpread):0.##}°\nAttack Speed Scaling: {(C.Get(attackSpeedScaling) ? "Yes" : "No")}", () => Value(gunProc));
            LG.Register(secondary, () => $"\nBlast Radius: {Value(rocketRadius):0.##}m", RocketProc);
            LG.Register(arsenalSecondary, () => $"\nBlast Radius: {Value(rocketRadius):0.##}m\nLock-On Range: {Value(lockRange):0.##}m", RocketProc);
            LG.Register(utility, () => $"\nThrust Speed: {Value(thrustSpeed):0.##}m/s\nBase Fuel Refill Time: {Value(fuelRefillTime):0.##}s");
            LG.Register(special, () => $"\nCooldown After Expiry: {Value(arsenalRecharge):0.##}s");
        }

        private static string GunText(ConfigEntry<float> ramp, bool empowered) =>
            $"Fire for <style=cIsDamage>{Value(gunDamage):0.##}% damage</style> per shot. Hold to increase fire rate and tighten spread over <style=cIsUtility>{Value(ramp):0.##}s</style>. <style=cDeath>Move {Mathf.Clamp(Value(firingSlow), 0f, 100f):0.##}% slower</style> while firing. Shots at full heat <style=cIsDamage>Ignite</style> enemies" +
            (empowered ? "." : ". Every other shot at full heat <style=cDeath>ignites you</style>.");

        private static string RocketText() =>
            $"Fire two rockets for <style=cIsDamage>{Value(rocketDamage):0.##}% damage each</style> in a <style=cIsUtility>{Value(rocketRadius):0.##}m</style> blast.";

        internal static void Install(GameObject body)
        {
            if (!primary || body.GetComponent<Control>()) return;
            primary.icon = RemoteDroneSkills.VanillaIcon(body.name, "Primary", RemoteDroneSkills.VanillaIcon("CommandoBody", "Special", null));
            secondary.icon = RemoteDroneSkills.VanillaIcon(body.name, "Secondary", RemoteDroneSkills.VanillaIcon("EngiBody", "Secondary", null));
            utility.icon = TintIcon(RemoteDroneSkills.VanillaIcon("ToolbotBody", "Utility", primary.icon), new Color(.22f, .75f, 1f));
            special.icon = TintIcon(RemoteDroneSkills.VanillaIcon("ToolbotBody", "Special", primary.icon, 1), new Color(1f, .64f, .22f));
            arsenalPrimary.icon = TintIcon(primary.icon, new Color(1f, .48f, .16f));
            arsenalSecondary.icon = TintIcon(secondary.icon, new Color(1f, .48f, .16f));
            thrusterBuff.iconSprite = RoR2Content.Buffs.CloakSpeed.iconSprite;
            arsenalBuff.iconSprite = RoR2Content.Buffs.TeamWarCry.iconSprite;
            body.AddComponent<Control>();
            if (gunOn.Value) SetSlot(body, "Primary", GunMachine, primary);
            if (rocketOn.Value) SetSlot(body, "Secondary", RocketMachine, secondary);
            if (thrusterOn.Value) SetSlot(body, "Utility", ThrusterMachine, utility);
            if (arsenalOn.Value) SetSlot(body, "Special", ArsenalMachine, special);

            RoR2Application.onLoad += LoadAssets;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteTC280Drone");
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), nameof(CharacterBody.RecalculateStats)),
                postfix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(AfterStats)));
            if (gunOn.Value)
                harmony.Patch(AccessTools.Method(typeof(BurnEffectController), "Start"),
                    prefix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(SilenceSelfBurnLoop)));
            if (thrusterOn.Value)
            {
                harmony.Patch(AccessTools.Method(typeof(PlayerFlyState), "PerformInputs"),
                    postfix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(ApplyThrust)));
                harmony.Patch(AccessTools.Method(typeof(SkillIcon), "LateUpdate"),
                    postfix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(ShowThrusterCooldown)));
            }
            if (rocketOn.Value)
            {
                harmony.Patch(AccessTools.Method(typeof(ProjectileManager), "InitializeProjectile"),
                    postfix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(ConfigureRocket)));
                harmony.Patch(AccessTools.Method(typeof(ProjectileSingleTargetImpact), nameof(ProjectileSingleTargetImpact.OnProjectileImpact)),
                    prefix: new HarmonyMethod(typeof(RemoteTC280Drone), nameof(ReplaceSingleTargetImpact)));
            }
        }

        private static void SetSlot(GameObject body, string slot, string machine, SkillDef skill)
        {
            RemoteDroneSkills.AddMachine(body, machine);
            RemoteDroneSkills.SetSkill(body, slot, skill);
        }

        private static Sprite TintIcon(Sprite source, Color tint)
        {
            if (!source || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return source;
            RenderTexture previous = RenderTexture.active;
            var render = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source.texture, render);
                RenderTexture.active = render;
                Rect rect = source.textureRect;
                var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++)
                {
                    float light = pixels[i].grayscale;
                    var colour = Color.Lerp(tint * .11f, tint, Mathf.Clamp01(light * 1.55f));
                    colour = Color.Lerp(colour, Color.white, Mathf.Clamp01((light - .64f) / .36f));
                    colour.a = pixels[i].a;
                    pixels[i] = colour;
                }
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                texture.wrapMode = TextureWrapMode.Clamp;
                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                    new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
                    source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(render);
            }
        }

        private static void LoadAssets()
        {
            if (assetsLoaded) return;
            assetsLoaded = true;
            var preload = AccessTools.Method(typeof(EntityStatePreloadCatalog), "LoadEntityStateContent");
            object unloadMode = Enum.ToObject(preload.GetParameters()[1].ParameterType, 0);
            foreach (Type state in new[] { typeof(FireMegaTurret), typeof(FireTwinRocket), typeof(ToolbotDash) })
                preload.Invoke(null, new[] { (object)EntityStateCatalog.GetStateIndex(state), unloadMode });
            rocketPrefab = FireTwinRocket.projectilePrefab;
            arsenalActivationEffect = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/TeamWarCryActivation");
            arsenalAuraPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/TemporaryVisualEffects/TeamWarCryAura");
            if (NetworkClient.active || SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                barrelFirePrefab = Addressables.LoadAssetAsync<GameObject>(
                    "RoR2/Base/Grandparent/OverheatTemporaryVisualEffect.prefab").WaitForCompletion();
            if (rocketOn.Value && !rocketPrefab)
                ThreeQsVanillaTweaks.Log.Warning("Remote TC-280: native Twin Rocket projectile is unavailable.");
        }

        private static float RocketRate => Mathf.Max(.01f, Value(arsenalRocketRate));
        private static float RocketProc()
        {
            if (C.Get(rocketProcOverride) >= 0f) return Value(rocketProcOverride);
            var controller = rocketPrefab ? rocketPrefab.GetComponent<ProjectileController>() : null;
            var explosion = rocketPrefab ? rocketPrefab.GetComponent<ProjectileExplosion>() : null;
            return controller ? controller.procCoefficient * (explosion ? explosion.blastProcCoefficient : 1f) : 0f;
        }

        private static void AfterStats(CharacterBody __instance)
        {
            if (__instance.TryGetComponent(out Control control) && control.Firing)
                SetMoveSpeed.Invoke(__instance, new object[] { __instance.moveSpeed * (1f - Mathf.Clamp01(Value(firingSlow) / 100f)) });
        }

        private static void SilenceSelfBurnLoop(BurnEffectController __instance)
        {
            var effect = __instance.effectType;
            if (effect == null || effect != BurnEffectController.normalEffect ||
                !__instance.TryGetComponent(out DotController dots) || !dots.victimObject ||
                !dots.victimObject.TryGetComponent(out Control control) || !control.SelfBurnedThisHeatCycle) return;

            // Keep the vanilla burn visuals while the heat meter controls the barrel loop.
            __instance.effectType = new BurnEffectController.EffectParams
            {
                overlayMaterial = effect.overlayMaterial,
                fireEffectPrefab = effect.fireEffectPrefab
            };
        }

        private static void ApplyThrust(PlayerFlyState __instance)
        {
            if (!__instance.isAuthority || !__instance.gameObject.TryGetComponent(out Control control) || !control.Thrusting)
                return;
            var input = __instance.GetComponent<InputBankTest>();
            var motor = __instance.GetComponent<RigidbodyMotor>();
            if (input && input.skill3.down && motor && control.Fuel > 0f)
                motor.moveVector += motor.moveVector.normalized * Value(thrustSpeed);
        }

        private static void ShowThrusterCooldown(SkillIcon __instance)
        {
            GenericSkill slot = __instance.targetSkill;
            if (!slot || slot.skillDef != utility || !__instance.cooldownText ||
                !slot.TryGetComponent(out Control control) || control.ThrustCooldown <= 0f) return;
            __instance.cooldownText.SetText(Mathf.CeilToInt(control.ThrustCooldown).ToString());
            __instance.cooldownText.gameObject.SetActive(true);
        }

        private sealed class ThrusterSkill : SkillDef
        {
            public override bool IsReady(GenericSkill slot) =>
                slot.TryGetComponent(out Control control) && control.CanThrust && base.IsReady(slot);

            public override int GetRechargeStock(GenericSkill slot) => Mathf.Max(1, slot.maxStock);
        }

        private sealed class RocketSkill : SkillDef
        {
            public override bool CanExecute(GenericSkill slot) =>
                rocketPrefab && slot.TryGetComponent(out Control control) && control.CanFireRocket && base.CanExecute(slot);

            public override void OnExecute(GenericSkill slot)
            {
                if (!CanExecute(slot)) return;
                slot.GetComponent<Control>().DelayNextRocketSalvo();
                base.OnExecute(slot);
            }

            public override EntityState InstantiateNextState(GenericSkill slot)
            {
                // Capture the displayed lock before spending the stock hides the reticle.
                var launch = (LaunchRockets)base.InstantiateNextState(slot);
                var input = slot.GetComponent<InputBankTest>();
                var control = slot.GetComponent<Control>();
                launch.Aim = input ? input.GetAimRay() : new Ray(slot.transform.position, slot.transform.forward);
                launch.Destination = control.GetRocketDestination(launch.Aim);
                if (this == arsenalSecondary && control && ValidTarget(slot.characterBody, control.LockedTarget, launch.Aim.origin))
                    launch.Target = control.LockedTarget;
                return launch;
            }
        }

        private static Vector3 GetWeaponAimPoint(Ray aim, CharacterBody owner)
        {
            Ray cameraAim = CameraRigController.ModifyAimRayIfApplicable(aim, owner.gameObject, out float extraDistance);
            float distance = 1000f + extraDistance;
            Vector3 point = cameraAim.GetPoint(distance);
            foreach (RaycastHit hit in Physics.RaycastAll(cameraAim, distance,
                LayerIndex.world.mask | LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                var hurtBox = hit.collider.GetComponent<HurtBox>();
                if ((hurtBox && hurtBox.healthComponent && hurtBox.healthComponent.body == owner) ||
                    hit.collider.transform.IsChildOf(owner.transform)) continue;
                if (hit.distance >= distance) continue;
                distance = hit.distance;
                point = hit.point;
            }
            return point;
        }

        private static bool ValidTarget(CharacterBody owner, HurtBox target, Vector3 origin)
        {
            var victim = target ? target.healthComponent : null;
            float range = Value(lockRange);
            return victim && victim.alive && victim.body && victim.body.teamComponent && owner && owner.teamComponent &&
                TeamMask.GetEnemyTeams(owner.teamComponent.teamIndex).HasTeam(victim.body.teamComponent.teamIndex) &&
                (target.transform.position - origin).sqrMagnitude <= range * range;
        }

        private sealed class Control : MonoBehaviour
        {
            internal bool Firing;
            internal bool Arsenal { get; private set; }
            internal bool Thrusting { get; private set; }
            internal bool AtFullHeat => Heat >= 1f;
            internal bool SelfBurnedThisHeatCycle { get; private set; }
            internal float Spin { get; private set; }
            internal float Heat { get; private set; }
            internal float Fuel { get; private set; } = 1f;
            private float shotProgress = 1f;
            internal HurtBox LockedTarget { get; private set; }
            private CharacterBody body;
            private InputBankTest input;
            private SkillLocator skills;
            private Indicator indicator;
            private float nextSearch, nextRocketUse;
            private float heatCoolAt, spinDownAt;
            private float lastBulletHitAt = float.NegativeInfinity;
            private Vector3 lastBulletAim, lastBulletHit;
            private bool thrustersNeedRelease, burnOnNextShot, overheatEffectsActive, overheatAudioActive;
            private uint boostSoundId;
            private readonly BullseyeSearch search = new BullseyeSearch();
            private GameObject thrusterVisual, boostEmitter, overheatEmitter;
            private TemporaryVisualEffect arsenalAura;
            private TemporaryVisualEffect leftBarrelFire, rightBarrelFire;
            private static GUIStyle meterLabel;

            private GenericSkill ThrusterSlot => skills && skills.utility && skills.utility.skillDef == utility ? skills.utility : null;
            private bool ThrusterRecharging => ThrusterSlot is GenericSkill slot && slot.stock < slot.maxStock;
            private float FuelMaximum => ThrusterSlot is GenericSkill slot && slot.maxStock > 1
                ? 1f + slot.maxStock * Value(fuelPerStock) / 100f : 1f;

            internal bool CanThrust => !Thrusting && !thrustersNeedRelease && Fuel > 0f && !ThrusterRecharging;
            private bool CanSelfBurn => AtFullHeat && !Arsenal && body && body.damage > 0f && Value(gunDamage) > 0f;
            internal float ThrustCooldown => !Thrusting && ThrusterSlot is GenericSkill slot && slot.rechargeRate > 0f
                ? Mathf.Max(0f, slot.cooldownRemaining / slot.rechargeRate) : 0f;

            internal void BeginThrust()
            {
                Thrusting = true;
                if (!NetworkClient.active) return;
                LoadAssets();
                if (!string.IsNullOrEmpty(ToolbotDash.startSoundString))
                    boostSoundId = PlayQuietSound(ref boostEmitter, "TC-280 Thruster Audio", ToolbotDash.startSoundString, .45f);
            }

            internal void DrainFuel(float dt) => Fuel = Mathf.Max(0f, Fuel - dt / PositiveTime(fuelCapacity));

            internal void EndThrust()
            {
                Thrusting = false;
                thrustersNeedRelease = Fuel <= .0001f && input && input.skill3.down;
                StopBoostSound();
            }

            private void StopBoostSound()
            {
                if (boostSoundId == 0) return;
                AkSoundEngine.StopPlayingID(boostSoundId);
                boostSoundId = 0;
            }

            internal void AdvanceGun(float dt)
            {
                Spin = Mathf.Min(1f, Spin + dt / PositiveTime(Arsenal ? arsenalSpinUp : spinUp));
                Heat = Mathf.Min(1f, Heat + dt / PositiveTime(heatBuildTime));
                if (Heat >= .9999f) Heat = 1f;
            }

            internal bool ShouldSelfBurn()
            {
                if (!CanSelfBurn) return false;
                bool apply = burnOnNextShot;
                burnOnNextShot = !burnOnNextShot;
                if (apply) SelfBurnedThisHeatCycle = true;
                return apply;
            }

            private float GunRate()
            {
                float rate = Mathf.Lerp(Value(Arsenal ? arsenalStartRate : startRate),
                    Value(Arsenal ? arsenalMaxRate : maxRate), Spin);
                return C.Get(attackSpeedScaling) ? rate * Mathf.Max(0f, body.attackSpeed) : rate;
            }

            internal int TakeDueShots(float dt)
            {
                shotProgress += dt * GunRate();
                int shots = Mathf.FloorToInt(shotProgress);
                shotProgress -= shots;
                return shots;
            }

            internal void StopGun()
            {
                Firing = false;
                heatCoolAt = Time.fixedTime + Value(heatCoolDelay);
                spinDownAt = Time.fixedTime + Value(spinDownDelay);
            }

            internal bool CanFireRocket => input && input.skill2.down && Time.fixedTime >= nextRocketUse;

            internal void DelayNextRocketSalvo() => nextRocketUse = Time.fixedTime + PositiveTime(rocketSalvoInterval);

            internal void RememberBulletImpact(Vector3 aimDirection, Vector3 impact)
            {
                lastBulletAim = aimDirection;
                lastBulletHit = impact;
                lastBulletHitAt = Time.fixedTime;
            }

            internal Vector3 GetRocketDestination(Ray aim)
            {
                if (Time.fixedTime - lastBulletHitAt <= .25f && Vector3.Dot(lastBulletAim, aim.direction) >= .999f &&
                    Vector3.Dot(lastBulletHit - aim.origin, aim.direction) > .01f)
                    return lastBulletHit;
                return GetWeaponAimPoint(aim, body);
            }

            private void Awake()
            {
                body = GetComponent<CharacterBody>();
                input = GetComponent<InputBankTest>();
                skills = GetComponent<SkillLocator>();
                if (NetworkClient.active)
                {
                    GameObject reticle = LegacyResourcesAPI.Load<GameObject>("Prefabs/HuntressTrackingIndicator");
                    if (reticle) indicator = new Indicator(gameObject, reticle);
                }
            }

            private void FixedUpdate()
            {
                if (!body || !body.healthComponent || !body.healthComponent.alive) { ShowTarget(null); return; }
                float dt = Time.fixedDeltaTime;
                Fuel = Mathf.Min(Fuel, FuelMaximum);
                if (!Thrusting && ThrusterSlot is GenericSkill slot && slot.stock >= slot.maxStock && Fuel < FuelMaximum)
                {
                    float baseTime = PositiveTime(fuelRefillTime);
                    float refillInterval = Mathf.Min(baseTime, Mathf.Max(MinimumCooldown,
                        baseTime * slot.cooldownScale - slot.flatCooldownReduction)) + slot.temporaryCooldownPenalty;
                    Fuel = Mathf.Min(FuelMaximum, Fuel + dt * slot.rechargeRate / Mathf.Max(MinimumCooldown, refillInterval));
                }
                if (thrustersNeedRelease && input && !input.skill3.down)
                    thrustersNeedRelease = false;
                if (!Firing)
                {
                    if (Time.fixedTime >= spinDownAt)
                        Spin = Mathf.Max(0f, Spin - dt / PositiveTime(spinDown));
                    shotProgress = Mathf.Min(1f, shotProgress + dt * GunRate());
                    if (Time.fixedTime >= heatCoolAt)
                    {
                        Heat = Mathf.Max(0f, Heat - dt / PositiveTime(heatCoolTime));
                        burnOnNextShot = false;
                        if (Heat <= .0001f) SelfBurnedThisHeatCycle = false;
                    }
                }

                if (!body.hasEffectiveAuthority ||
                    !Arsenal || !input || !skills || !skills.secondary || skills.secondary.skillDef != arsenalSecondary ||
                    !skills.secondary.IsReady())
                {
                    ShowTarget(null);
                    nextSearch = 0f;
                    return;
                }

                Ray aim = input.GetAimRay();
                if (Time.fixedTime < nextSearch && ValidTarget(body, LockedTarget, aim.origin)) return;
                nextSearch = Time.fixedTime + .05f;
                search.searchOrigin = aim.origin;
                search.searchDirection = aim.direction;
                search.teamMaskFilter = TeamMask.GetEnemyTeams(body.teamComponent.teamIndex);
                search.maxDistanceFilter = Value(lockRange);
                search.maxAngleFilter = Mathf.Clamp(Value(lockAngle), 0f, 180f);
                search.filterByLoS = true;
                search.sortMode = BullseyeSearch.SortMode.Angle;
                search.RefreshCandidates();
                ShowTarget(search.GetResults().FirstOrDefault(target => ValidTarget(body, target, aim.origin)));
            }

            private void ShowTarget(HurtBox target)
            {
                LockedTarget = target;
                if (indicator == null) return;
                indicator.targetTransform = target ? target.transform : null;
                indicator.active = target;
            }

            internal void SetArsenal(bool active)
            {
                if (Arsenal == active) return;
                Arsenal = active;
                if (skills)
                {
                    SwapArsenalSkill(skills.primary, primary, arsenalPrimary, active);
                    SwapArsenalSkill(skills.secondary, secondary, arsenalSecondary, active);
                }
                if (!active)
                {
                    ShowTarget(null);
                    if (arsenalAura) arsenalAura.visualState = TemporaryVisualEffect.VisualState.Exit;
                    arsenalAura = null;
                    return;
                }
                if (!NetworkClient.active || !body) return;
                LoadAssets();
                Util.PlaySound("Play_teamWarCry_activate", gameObject);
                if (arsenalActivationEffect)
                {
                    var data = new EffectData { origin = body.corePosition };
                    data.SetNetworkedObjectReference(gameObject);
                    EffectManager.SpawnEffect(arsenalActivationEffect, data, false);
                }
                if (arsenalAuraPrefab)
                {
                    var aura = Instantiate(arsenalAuraPrefab, body.corePosition, Quaternion.identity);
                    arsenalAura = aura.GetComponent<TemporaryVisualEffect>();
                    if (arsenalAura)
                    {
                        arsenalAura.parentTransform = body.coreTransform;
                        arsenalAura.healthComponent = body.healthComponent;
                        arsenalAura.radius = body.bestFitRadius;
                        arsenalAura.visualState = TemporaryVisualEffect.VisualState.Enter;
                        if (aura.TryGetComponent(out LocalCameraEffect cameraEffect)) cameraEffect.targetCharacter = gameObject;
                    }
                    else Destroy(aura);
                }
            }

            private void SwapArsenalSkill(GenericSkill slot, SkillDef normal, SkillDef boosted, bool active)
            {
                if (!slot || (active && slot.skillDef != normal)) return;
                SkillDef from = active ? normal : boosted;
                SkillDef to = active ? boosted : normal;
                bool transfer = slot.skillDef == from;
                int stock = slot.stock;
                float oldInterval = slot.GetFinalRechargeInterval();
                float progress = oldInterval > 0f ? Mathf.Clamp01(slot.rechargeStopwatch / oldInterval) : 0f;
                if (active) slot.SetSkillOverride(this, boosted, ArsenalOverride);
                else slot.UnsetSkillOverride(this, boosted, ArsenalOverride);
                if (transfer && slot.skillDef == to)
                {
                    slot.stock = to.dontAllowPastMaxStocks ? Mathf.Min(stock, slot.maxStock) : stock;
                    slot.rechargeStopwatch = progress * slot.GetFinalRechargeInterval();
                }
            }

            private void LateUpdate()
            {
                if (!NetworkClient.active || !body) return;
                bool alive = body.healthComponent && body.healthComponent.alive;
                bool active = alive && Thrusting;
                if (active && !thrusterVisual) thrusterVisual = CreateThrusterVisual(body);
                else if (!active && thrusterVisual) { Destroy(thrusterVisual); thrusterVisual = null; }
                if (!alive) StopBoostSound();
                SetOverheatAudio(alive && CanSelfBurn);
                SetOverheatEffects(alive && AtFullHeat);
            }

            private void SetOverheatAudio(bool active)
            {
                if (overheatAudioActive == active) return;
                overheatAudioActive = active;
                if (!active)
                {
                    StopEmitterSounds(overheatEmitter);
                    return;
                }
                PlayQuietSound(ref overheatEmitter, "TC-280 Barrel Overheat Audio", OverheatSound, .28f);
            }

            private static void StopEmitterSounds(GameObject emitter)
            {
                if (emitter && AkSoundEngine.IsInitialized() && AkSoundEngine.IsGameObjectRegistered(emitter))
                    AkSoundEngine.StopAll(emitter);
            }

            private uint PlayQuietSound(ref GameObject emitter, string name, string sound, float volume)
            {
                if (!emitter)
                {
                    emitter = new GameObject(name);
                    emitter.transform.SetParent(body.coreTransform, false);
                    emitter.AddComponent<AkGameObj>();
                }
                foreach (var listener in AkAudioListener.DefaultListeners.ListenerList)
                    if (listener) AkSoundEngine.SetGameObjectOutputBusVolume(emitter, listener.gameObject, volume);
                return Util.PlaySound(sound, emitter);
            }

            private void SetOverheatEffects(bool active)
            {
                if (overheatEffectsActive == active) return;
                overheatEffectsActive = active;
                if (active)
                {
                    leftBarrelFire = StartBarrelFire("GatLeft");
                    rightBarrelFire = StartBarrelFire("GatRight");
                    return;
                }

                StopBarrelFire(leftBarrelFire);
                StopBarrelFire(rightBarrelFire);
                leftBarrelFire = rightBarrelFire = null;
            }

            private static void StopBarrelFire(TemporaryVisualEffect effect)
            {
                if (!effect) return;
                // The vanilla visual may have its own emitters. Stop those with the barrel loop.
                foreach (var child in effect.GetComponentsInChildren<Transform>(true))
                    StopEmitterSounds(child.gameObject);
                effect.gameObject.SetActive(false);
                Destroy(effect.gameObject);
            }

            private TemporaryVisualEffect StartBarrelFire(string muzzle)
            {
                var locator = body.modelLocator ? body.modelLocator.modelChildLocator : null;
                var barrel = locator ? locator.FindChild(muzzle) : null;
                if (!barrel || !barrelFirePrefab) return null;
                var fire = Instantiate(barrelFirePrefab, barrel.position, barrel.rotation);
                if (!fire.TryGetComponent(out TemporaryVisualEffect effect))
                {
                    Destroy(fire);
                    return null;
                }
                effect.parentTransform = barrel;
                effect.healthComponent = body.healthComponent;
                effect.radius = Mathf.Clamp(body.bestFitRadius * .22f, .25f, .8f);
                effect.visualState = TemporaryVisualEffect.VisualState.Enter;
                return effect;
            }

            private void OnGUI()
            {
                if (Event.current.type != EventType.Repaint || PauseManager.isPaused || !body || !body.hasEffectiveAuthority ||
                    !body.healthComponent || !body.healthComponent.alive || !skills) return;
                var camera = CameraRigController.readOnlyInstancesList.FirstOrDefault(rig =>
                    rig && rig.targetBody == body && rig.localUserViewer != null && rig.sceneCam && rig.hud && rig.hud.isActiveAndEnabled);
                if (!camera) return;
                var eventSystem = camera.localUserViewer.eventSystem;
                if (eventSystem && (eventSystem.cursorOpenerCount > 0 || eventSystem.cursorOpenerForGamepadCount > 0 ||
                    eventSystem.isCursorVisible)) return;
                Rect viewport = camera.sceneCam.pixelRect;
                float x = viewport.center.x;
                float y = Screen.height - viewport.center.y;
                float scale = Mathf.Clamp(viewport.height / 1080f, .75f, 1.6f);
                if (meterLabel == null) meterLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
                meterLabel.fontSize = Mathf.RoundToInt(10f * scale);
                Color saved = GUI.color;
                if (skills.utility && skills.utility.skillDef == utility)
                    DrawMeter(new Rect(x - 43f * scale, y + 29f * scale, 86f * scale, 5f * scale),
                        Fuel / FuelMaximum, CanThrust || Thrusting ? new Color(.25f, .8f, 1f) : new Color(.27f, .45f, .6f), "FUEL", false, scale);
                if (skills.primary && (skills.primary.skillDef == primary || skills.primary.skillDef == arsenalPrimary))
                {
                    DrawMeter(new Rect(x - 69f * scale, y - 19f * scale, 5f * scale, 38f * scale),
                        Spin, new Color(1f, .82f, .3f), "SPIN", true, scale);
                    Color heatColor = Color.Lerp(new Color(.98f, .78f, .33f), new Color(1f, .16f, .11f), Heat);
                    DrawMeter(new Rect(x + 64f * scale, y - 19f * scale, 5f * scale, 38f * scale),
                        Heat, heatColor, "HEAT", true, scale);
                }
                GUI.color = saved;
            }

            private static void DrawMeter(Rect bar, float fill, Color colour, string label, bool vertical, float scale)
            {
                GUI.color = new Color(.04f, .06f, .08f, .88f);
                GUI.DrawTexture(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Texture2D.whiteTexture);
                GUI.color = colour;
                Rect filled = vertical ? new Rect(bar.x, bar.yMax - bar.height * fill, bar.width, bar.height * fill)
                    : new Rect(bar.x, bar.y, bar.width * fill, bar.height);
                if (fill > 0f) GUI.DrawTexture(filled, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(new Rect(bar.center.x - 26f * scale, vertical ? bar.y - 15f * scale : bar.yMax + 1f,
                    52f * scale, 14f * scale), label, meterLabel);
            }

            private void OnDisable()
            {
                SetArsenal(false);
                ShowTarget(null);
                StopBoostSound();
                if (boostEmitter) Destroy(boostEmitter);
                boostEmitter = null;
                SetOverheatAudio(false);
                if (overheatEmitter) Destroy(overheatEmitter);
                overheatEmitter = null;
                SetOverheatEffects(false);
                if (thrusterVisual) Destroy(thrusterVisual);
                thrusterVisual = null;
            }
        }

        private static GameObject CreateThrusterVisual(CharacterBody body)
        {
            var visual = new GameObject("TC-280 Auxiliary Thrusters");
            visual.transform.SetParent(body.coreTransform, false);
            float scale = Mathf.Clamp(body.radius, .65f, 3f);
            var colour = new Color(.28f, .78f, 1f, .9f);
            var clear = new Color(.08f, .37f, 1f, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                var nozzle = new GameObject("Thruster exhaust");
                nozzle.transform.SetParent(visual.transform, false);
                nozzle.transform.localPosition = new Vector3(side * .55f, -.14f, -.8f) * scale;
                var trail = nozzle.AddComponent<TrailRenderer>();
                trail.sharedMaterial = Graphic.defaultGraphicMaterial;
                trail.time = .23f;
                trail.minVertexDistance = .08f;
                trail.startColor = colour;
                trail.endColor = clear;
                trail.startWidth = .23f * scale;
                trail.endWidth = 0f;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
            }
            return visual;
        }

        public sealed class MachineGun : BaseState
        {
            private static readonly int FireAnimation = Animator.StringToHash("FireGat");
            private Control control;
            private bool leftBarrel = true;
            private BulletAttack.HitCallback captureHit;
            private Vector3 shotAimDirection;

            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                control = GetComponent<Control>();
                captureHit = CaptureHit;
                control.Firing = true;
                characterBody.MarkAllStatsDirty();
                FireDueShots(0f);
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (!healthComponent || !healthComponent.alive || (isAuthority && (!inputBank || !inputBank.skill1.down)))
                {
                    if (isAuthority) outer.SetNextStateToMain();
                    return;
                }

                control.AdvanceGun(Time.fixedDeltaTime);
                FireDueShots(Time.fixedDeltaTime);
            }

            private void FireDueShots(float dt)
            {
                int shots = control.TakeDueShots(dt);
                if (shots <= 0) return;
                float spread = Spread(control.Spin, control.Arsenal);
                Ray aim = GetAimRay();
                shotAimDirection = aim.direction;
                Vector3 aimPoint = isAuthority ? GetWeaponAimPoint(aim, characterBody) : default;
                for (int i = 0; i < shots; i++) FireShot(spread, aim, aimPoint);
            }

            private static float Spread(float fraction, bool empowered) =>
                Mathf.Lerp(Value(empowered ? arsenalWideSpread : wideSpread), Value(empowered ? arsenalTightSpread : tightSpread), fraction);

            private bool CaptureHit(BulletAttack attack, ref BulletAttack.BulletHit hit)
            {
                bool continues = BulletAttack.defaultHitCallback(attack, ref hit);
                if (!continues) control.RememberBulletImpact(shotAimDirection, hit.point);
                return continues;
            }

            private void FireShot(float spread, Ray aim, Vector3 aimPoint)
            {
                string muzzle = leftBarrel ? "GatLeft" : "GatRight";
                leftBarrel = !leftBarrel;
                float damage = characterBody.damage * Value(gunDamage) / 100f;
                if (control.ShouldSelfBurn() && NetworkServer.active)
                {
                    // Native IgniteOnHit burns for half the triggering hit's damage.
                    var burn = new InflictDotInfo
                    {
                        attackerObject = gameObject, victimObject = gameObject, dotIndex = DotController.DotIndex.Burn,
                        damageMultiplier = 1f, totalDamage = damage * .5f
                    };
                    DotController.InflictDot(ref burn);
                }
                if (NetworkClient.active)
                {
                    if (!string.IsNullOrEmpty(FireMegaTurret.attackSoundString))
                        Util.PlaySound(FireMegaTurret.attackSoundString, gameObject);
                    PlayAnimation("Gesture, Additive", FireAnimation);
                    if (FireMegaTurret.effectPrefab)
                        EffectManager.SimpleMuzzleFlash(FireMegaTurret.effectPrefab, gameObject, muzzle, false);
                }
                if (!isAuthority) return;
                Transform barrel = FindModelChild(muzzle);
                Vector3 origin = barrel ? barrel.position : aim.origin;
                Vector3 direction = aimPoint - origin;
                new BulletAttack
                {
                    owner = gameObject, weapon = gameObject, muzzleName = muzzle, origin = origin,
                    aimVector = Vector3.Dot(direction, aim.direction) > .01f ? direction.normalized : aim.direction,
                    minSpread = 0f, maxSpread = spread, damage = damage,
                    procCoefficient = Value(gunProc), force = FireMegaTurret.force, isCrit = RollCrit(),
                    tracerEffectPrefab = FireMegaTurret.tracerEffectPrefab, hitEffectPrefab = FireMegaTurret.hitEffectPrefab,
                    hitCallback = captureHit,
                    damageType = new DamageTypeCombo(control.AtFullHeat ? DamageType.IgniteOnHit : DamageType.Generic,
                        DamageTypeExtended.Generic, DamageSource.Primary)
                }.Fire();
            }

            public override void OnExit()
            {
                if (control) control.StopGun();
                if (characterBody) characterBody.MarkAllStatsDirty();
                base.OnExit();
            }

            // The slot's Skill-priority input must not restart a held firing state.
            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class LaunchRockets : BaseState
        {
            internal Ray Aim;
            internal Vector3 Destination;
            internal HurtBox Target;
            public override void OnSerialize(NetworkWriter writer)
            {
                base.OnSerialize(writer);
                writer.Write(Aim.origin);
                writer.Write(Aim.direction);
                writer.Write(Destination);
                writer.Write(HurtBoxReference.FromHurtBox(Target));
            }

            public override void OnDeserialize(NetworkReader reader)
            {
                base.OnDeserialize(reader);
                Aim = new Ray(reader.ReadVector3(), reader.ReadVector3());
                Destination = reader.ReadVector3();
                Target = reader.ReadHurtBoxReference().ResolveHurtBox();
            }

            public override void OnEnter()
            {
                base.OnEnter();
                LoadAssets();
                HurtBox locked = ValidTarget(characterBody, Target, Aim.origin) ? Target : null;
                Transform leftPod = FindModelChild("GatLeft");
                Transform rightPod = FindModelChild("GatRight");
                Vector3 left = leftPod ? leftPod.position : Aim.origin;
                Vector3 right = rightPod ? rightPod.position : Aim.origin;
                Fire("GatLeft", left, locked);
                Fire("GatRight", right, locked);
                if (isAuthority) outer.SetNextStateToMain();
            }

            private void Fire(string muzzle, Vector3 origin, HurtBox target)
            {
                if (NetworkClient.active && FireTwinRocket.muzzleEffectPrefab)
                    EffectManager.SimpleMuzzleFlash(FireTwinRocket.muzzleEffectPrefab, gameObject, muzzle, false);
                if (!NetworkServer.active || !rocketPrefab || !ProjectileManager.instance) return;
                Vector3 destination = Destination;
                if (Vector3.Dot(destination - origin, Aim.direction) <= .01f)
                    destination = origin + Aim.direction * 1000f;
                CharacterBody previous = firingRockets;
                Vector3 previousDestination = rocketDestination;
                HurtBox previousTarget = rocketTarget;
                firingRockets = characterBody;
                rocketDestination = destination;
                rocketTarget = target;
                try
                {
                    // The native utility supplies Pocket I.C.B.M.'s extra shots and damage scaling,
                    // including the item's own 3QsVanillaTweaks config.
                    MissileUtils.FireMissile(origin, characterBody, default, target ? target.gameObject : null,
                        damageStat * Value(rocketDamage) / 100f, RollCrit(), rocketPrefab, DamageColorIndex.Default,
                        (destination - origin).normalized, FireTwinRocket.force, false);
                }
                finally
                {
                    firingRockets = previous;
                    rocketDestination = previousDestination;
                    rocketTarget = previousTarget;
                }
            }
        }

        private static void ConfigureRocket(ProjectileController projectileController, FireProjectileInfo fireProjectileInfo)
        {
            if (!firingRockets || fireProjectileInfo.owner != firingRockets.gameObject || fireProjectileInfo.projectilePrefab != rocketPrefab) return;
            var damage = projectileController.GetComponent<ProjectileDamage>();
            if (damage) damage.damageType.damageSource = DamageSource.Secondary;
            var explosion = projectileController.GetComponent<ProjectileExplosion>();
            if (explosion)
            {
                explosion.blastRadius = Value(rocketRadius);
                explosion.blastDamageCoefficient = 1f;
                explosion.falloffModel = BlastAttack.FalloffModel.None;
                if (C.Get(rocketProcOverride) >= 0f) explosion.blastProcCoefficient = 1f;
            }
            else if (projectileController.GetComponent<ProjectileSingleTargetImpact>())
                projectileController.gameObject.AddComponent<RocketSplash>().Radius = Value(rocketRadius);
            if (C.Get(rocketProcOverride) >= 0f) projectileController.procCoefficient = Value(rocketProcOverride);

            // Use the captured bullet impact or crosshair point for straight flight.
            // Full Arsenal can instead steer toward its marked hurtbox.
            if (projectileController.TryGetComponent(out ProjectileSphereTargetFinder finder)) finder.enabled = false;
            if (projectileController.TryGetComponent(out ProjectileDirectionalTargetFinder directionalFinder)) directionalFinder.enabled = false;
            if (projectileController.TryGetComponent(out ProjectileSteerTowardTarget steering)) steering.enabled = false;
            var simple = projectileController.GetComponent<ProjectileSimple>();
            if (simple) simple.oscillate = false;
            var guidance = projectileController.gameObject.AddComponent<RocketGuidance>();
            guidance.Initialize(fireProjectileInfo.rotation, rocketDestination, rocketTarget, simple);
        }

        [DefaultExecutionOrder(100)]
        private sealed class RocketGuidance : MonoBehaviour
        {
            private HurtBox target;
            private Rigidbody motor;
            private Quaternion heading;
            private Vector3 destination;
            private bool convergeOnPoint;
            private float constantSpeed;

            internal void Initialize(Quaternion rotation, Vector3 point, HurtBox marked, ProjectileSimple simple)
            {
                heading = rotation;
                destination = point;
                convergeOnPoint = !marked;
                target = marked;
                motor = GetComponent<Rigidbody>();
                if (motor) motor.useGravity = false;
                var nativeMissile = GetComponent<MissileController>();
                if (nativeMissile)
                {
                    nativeMissile.enabled = false;
                    if (!simple)
                    {
                        constantSpeed = nativeMissile.maxVelocity;
                        Destroy(gameObject, Mathf.Max(.02f, nativeMissile.deathTimer));
                    }
                }
                UpdateHeading();
            }

            private void FixedUpdate() => UpdateHeading();

            private void UpdateHeading()
            {
                if (target && target.healthComponent && target.healthComponent.alive)
                {
                    Vector3 direction = target.transform.position - transform.position;
                    if (direction.sqrMagnitude > .001f)
                        heading = Quaternion.RotateTowards(heading, Util.QuaternionSafeLookRotation(direction),
                            Value(turnSpeed) * Time.fixedDeltaTime);
                }
                else if (convergeOnPoint)
                {
                    Vector3 direction = destination - transform.position;
                    if (Vector3.Dot(direction, heading * Vector3.forward) > .001f)
                        heading = Util.QuaternionSafeLookRotation(direction);
                    else convergeOnPoint = false;
                }
                transform.rotation = heading;
                if (motor && !motor.isKinematic)
                {
                    motor.rotation = heading;
                    motor.angularVelocity = Vector3.zero;
                    motor.velocity = (heading * Vector3.forward) * (constantSpeed > 0f ? constantSpeed : motor.velocity.magnitude);
                }
            }
        }

        private sealed class RocketSplash : MonoBehaviour
        {
            internal float Radius;
            internal bool Detonated;
        }

        private static bool ReplaceSingleTargetImpact(ProjectileSingleTargetImpact __instance, ProjectileImpactInfo impactInfo)
        {
            var splash = __instance.GetComponent<RocketSplash>();
            if (!splash) return true;
            if (!NetworkServer.active || splash.Detonated || !impactInfo.collider) return false;
            var controller = __instance.GetComponent<ProjectileController>();
            HurtBox hurtBox = impactInfo.collider.GetComponent<HurtBox>();
            if (impactInfo.collider.gameObject == controller.owner ||
                (hurtBox && hurtBox.healthComponent && hurtBox.healthComponent.gameObject == controller.owner) ||
                (!hurtBox && !__instance.destroyOnWorld)) return false;

            splash.Detonated = true;
            var damage = __instance.GetComponent<ProjectileDamage>();
            if (damage)
            {
                var result = new BlastAttack
                {
                    attacker = controller.owner, inflictor = __instance.gameObject, teamIndex = controller.teamFilter.teamIndex,
                    position = impactInfo.estimatedPointOfImpact, radius = splash.Radius, baseDamage = damage.damage,
                    baseForce = damage.force, crit = damage.crit, damageType = damage.damageType, damageColorIndex = damage.damageColorIndex,
                    procChainMask = controller.procChainMask, procCoefficient = controller.procCoefficient, falloffModel = BlastAttack.FalloffModel.None
                }.Fire();
                if (result.hitCount > 0 && __instance.enemyHitSound)
                    EffectManager.SimpleSoundEffect(__instance.enemyHitSound.index, impactInfo.estimatedPointOfImpact, true);
            }
            if (__instance.hitSound)
                EffectManager.SimpleSoundEffect(__instance.hitSound.index, impactInfo.estimatedPointOfImpact, true);
            if (__instance.impactEffect)
                EffectManager.SimpleImpactEffect(__instance.impactEffect, impactInfo.estimatedPointOfImpact, -__instance.transform.forward, true);
            NetworkServer.Destroy(__instance.gameObject);
            return false;
        }

        public sealed class Thrusters : BaseState
        {
            private Control control;

            public override void OnEnter()
            {
                base.OnEnter();
                control = GetComponent<Control>();
                control.BeginThrust();
                if (NetworkServer.active) characterBody.AddBuff(thrusterBuff);
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && (!inputBank || !inputBank.skill3.down || !healthComponent || !healthComponent.alive))
                {
                    outer.SetNextStateToMain();
                    return;
                }
                control.DrainFuel(Time.fixedDeltaTime);
                if (isAuthority && control.Fuel <= .0001f) outer.SetNextStateToMain();
            }

            public override void OnExit()
            {
                if (control) control.EndThrust();
                if (NetworkServer.active && characterBody && characterBody.HasBuff(thrusterBuff))
                    characterBody.RemoveBuff(thrusterBuff);
                base.OnExit();
            }

            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }

        public sealed class Arsenal : BaseState
        {
            private Control control;
            private float duration;

            public override void OnEnter()
            {
                base.OnEnter();
                control = GetComponent<Control>();
                duration = Value(arsenalDuration);
                if (duration > 0f) control.SetArsenal(true);
                if (NetworkServer.active && duration > 0f) characterBody.AddTimedBuff(arsenalBuff, duration);
            }

            public override void FixedUpdate()
            {
                base.FixedUpdate();
                if (isAuthority && fixedAge >= duration) outer.SetNextStateToMain();
            }

            public override void OnExit()
            {
                if (control) control.SetArsenal(false);
                if (NetworkServer.active && characterBody) characterBody.ClearTimedBuffs(arsenalBuff.buffIndex);
                var skill = skillLocator ? skillLocator.special : null;
                if (skill && skill.skillDef == special) skill.SetBlockedCooldownSkillState(false);
                base.OnExit();
            }

            public override InterruptPriority GetMinimumInterruptPriority() => InterruptPriority.PrioritySkill;
        }
    }
}
