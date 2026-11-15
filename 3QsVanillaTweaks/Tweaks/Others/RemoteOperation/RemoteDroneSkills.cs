using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using RoR2.WwiseUtils;
using C = Config.ConfigManager;

namespace Tweaks.Others.RemoteOperation
{
    // Player-only drone bodies are distinct from purchasable AI drone bodies.
    internal static class RemoteDroneSkills
    {
        internal const string GunnerSection = "Remote Operation - Gunner Drone";
        internal const string HealingSection = "Remote Operation - Healing Drone";
        internal const string JunkSection = "Remote Operation - Junk Drone";
        internal const string EmergencySection = "Remote Operation - Emergency Drone";
        internal const string IncineratorSection = "Remote Operation - Incinerator Drone";
        internal const string CleanupSection = "Remote Operation - Cleanup Drone";
        internal const string MissileSection = "Remote Operation - Missile Drone";
        internal const string JailerSection = "Remote Operation - Jailer Drone";
        internal const string BarrierSection = "Remote Operation - Barrier Drone";
        internal const string BombardmentSection = "Remote Operation - Bombardment Drone";
        internal const string FreezeSection = "Remote Operation - Frost Drone";
        private static ConfigEntry<bool> enabled;
        internal static GameObject GunnerBody, HealingBody, JunkBody, EmergencyBody, IncineratorBody, CleanupBody, MissileBody, JailerBody, BarrierBody, BombardmentBody, FreezeBody;
        internal static readonly List<SkillDef> Definitions = new List<SkillDef>();
        internal static readonly List<SkillFamily> Families = new List<SkillFamily>();
        private static readonly Dictionary<SkillDef, SkillFamily> SkillFamilies = new Dictionary<SkillDef, SkillFamily>();
        // Continuous input is allowed; a short per-slot interval prevents one held
        // frame from spending multiple stocks through successive state transitions.
        // The flamethrower has its own continuous fuel-cycle accounting.
        internal class RepeatingSkillDef : SkillDef
        {
            private static RepeatDelay GetDelay(GenericSkill slot)
            {
                if (!slot) return null;
                var delay = slot.GetComponent<RepeatDelay>();
                return delay ? delay : slot.gameObject.AddComponent<RepeatDelay>();
            }
            public override bool CanExecute(GenericSkill slot)
            {
                var delay = GetDelay(slot);
                return (!delay || delay.Allows(slot)) && base.CanExecute(slot);
            }
            public override bool IsReady(GenericSkill slot)
            {
                var delay = GetDelay(slot);
                return (!delay || delay.Allows(slot)) && base.IsReady(slot);
            }
            public override void OnExecute(GenericSkill slot)
            {
                var delay = GetDelay(slot);
                if (delay) delay.Register(slot);
                int stockBefore = slot.stock;
                base.OnExecute(slot);
                if (skillName != null &&
                    (skillName.Contains("EmergencyRestoration") || skillName.Contains("EmergencySalvoOverdrive")))
                    return; // Siphoning Salvo intentionally spends all available orbs.
                slot.stock = Mathf.Max(0, stockBefore - 1);
            }
        }

        private sealed class RepeatDelay : MonoBehaviour
        {
            private readonly Dictionary<GenericSkill, float> nextAllowed = new Dictionary<GenericSkill, float>();
            internal bool Allows(GenericSkill slot) => !nextAllowed.TryGetValue(slot, out float until) || Time.time >= until;
            internal void Register(GenericSkill slot)
            {
                // Held-input repeats are spaced out, including instant states such as Salvage.
                // Longer animations already impose their own natural repeat interval.
                if (slot) nextAllowed[slot] = Time.time + 0.35f;
            }
        }

        private sealed class StockSettings
        {
            internal ConfigEntry<int> Maximum, Recharge, Required, Consumed;
        }
        private static readonly Dictionary<SkillDef, StockSettings> Stocks = new Dictionary<SkillDef, StockSettings>();
        private static readonly Dictionary<string, StockSettings> StockConfigs = new Dictionary<string, StockSettings>();
        private static readonly FieldInfo FamilyField = AccessTools.Field(typeof(GenericSkill), "_skillFamily");
        private static readonly FieldInfo MachinesField = AccessTools.Field(typeof(NetworkStateMachine), "stateMachines");
        private static readonly FieldInfo BodySlotsField = AccessTools.Field(typeof(BodyCatalog), "skillSlots");
        private static GameObject phaseMuzzleEffect;
        private static string phaseFireSound;
        private static bool phaseAssetsLoaded;

        // Character-specific events are unavailable after the original body has
        // been replaced by a remote drone. Keep their banks loaded for its life.
        private sealed class NativeSkillSoundBanks : MonoBehaviour
        {
            private readonly List<string> loaded = new List<string>();
            [SerializeField] internal string[] additionalBodies = Array.Empty<string>();

            private void OnEnable()
            {
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (string bodyName in new[] { "CommandoBody", "TreebotBody", "DrifterBody", "Bandit2Body", "RailgunnerBody", "ChefBody", "MageBody", "SeekerBody", "MercBody", "HuntressBody" }.Concat(additionalBodies))
                {
                    GameObject source = BodyCatalog.FindBodyPrefab(bodyName);
                    if (!source) continue;
                    foreach (AkBank bank in source.GetComponentsInChildren<AkBank>(true))
                        if (bank.data != null && bank.data.IsValid()) names.Add(bank.data.Name);
                    foreach (SoundbankLoader loader in source.GetComponentsInChildren<SoundbankLoader>(true))
                        if (loader.soundbankStrings != null)
                            foreach (string bankName in loader.soundbankStrings)
                                if (!string.IsNullOrEmpty(bankName)) names.Add(bankName);
                }
                foreach (string name in names)
                {
                    AkBankManager.LoadBank(name, false, false);
                    loaded.Add(name);
                }
                if (names.Count == 0)
                    ThreeQsVanillaTweaks.Log.Warning("Remote drone sounds: no native character sound banks were found.");
                else if (C.DebugOptionsEnabled)
                    ThreeQsVanillaTweaks.Log.Info("Remote drone sounds: loaded " + string.Join(", ", names));
            }

            private void OnDisable()
            {
                foreach (string name in loaded) AkBankManager.UnloadBank(name);
                loaded.Clear();
            }
        }

        internal static void Init()
        {
            enabled = C.BindStartupBool("Remote Operation - Skills", "Changes Enabled", true,
                "Add player-controlled drone skills. AI-controlled drones are unchanged. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            RemoteGunnerDrone.Bind();
            RemoteHealingDrone.Bind();
            RemoteJunkDrone.Bind();
            RemoteEmergencyDrone.Bind();
            RemoteIncineratorDrone.Bind();
            RemoteCleanupDrone.Bind();
            RemoteMissileDrone.Bind();
            RemoteJailerDrone.Bind();
            RemoteBarrierDrone.Bind();
            RemoteBombardmentDrone.Bind();
            RemoteFreezeDrone.Bind();
            RemoteTC280Drone.Bind();
            foreach (var skill in new[] {
                ("GunnerFusillade", "Fusillade"), ("GunnerChargedShot", "Charged Shot"),
                ("GunnerDash", "Dash"), ("GunnerRapidBurst", "Rapid Burst"),
                ("HealingBeam", "Heal Beam"), ("HealingChargedShot", "Charged Shot"),
                ("HealingDash", "Dash"), ("HealingRescue", "Rescue"),
                ("JunkCube", "Junk Cube"), ("JunkSalvage", "Salvage"),
                ("JunkSlipAway", "Slip Away"), ("JunkSurprise", "Surprise!"),
                ("EmergencyBeams", "Healing Beams"), ("EmergencyRestoration", "Siphoning Salvo"),
                ("EmergencyNova", "Healing Nova"), ("EmergencyOverdrive", "Overdrive"),
                ("IncineratorFlamethrower", "Flamethrower"), ("IncineratorGlaze", "Fuel Glaze"),
                ("IncineratorJet", "Flame Jet"), ("IncineratorInferno", "Inferno"),
                ("CleanupVacuum", "Vacuum"), ("CleanupCancelVacuum", "Cancel Vacuum"), ("CleanupTrashCannon", "Trash Cannon"),
                ("CleanupSuctionBoost", "Suction Boost"), ("CleanupMassRecycling", "Mass Recycling"),
                ("MissileGuided", "Guided Missile"), ("MissilePainting", "Missile Painting"),
                ("MissileHeavyDash", "Heavy Dash"), ("MissileBarrage", "Missile Barrage"),
                ("JailerRestraintBolt", "Restraint Bolt"), ("JailerLockdown", "Lockdown"),
                ("JailerPrisonTransfer", "Prison Transfer"), ("JailerContainmentCage", "Containment Cage"),
                ("BarrierOrb", "Barrier Orb"), ("BarrierBeam", "Barrier Buildup"),
                ("BarrierShield", "Shield Projection"), ("BarrierField", "Recharge Field"),
                ("BombardmentArc", "Arc Lightning"), ("BombardmentEMP", "EMP"),
                ("BombardmentThunderbolt", "Orbital Thunderbolt"),
                ("TC280TwinRocket", "Twin Rocket"), ("TC280Thrusters", "Auxiliary Thrusters"),
                ("TC280Arsenal", "Full Arsenal") })
                BindStocks(skill.Item1, skill.Item2);
            C.EffectiveSettingsChanged += Refresh;
            ContentManager.collectContentPackProviders += add => add(new DroneContent());
            // Loadout snapshots each body's skill slots during its system initializer.
            // Install the extra slots immediately before that snapshot, not onLoad.
            new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteDroneSkills.Loadout")
                .Patch(AccessTools.Method(typeof(Loadout.BodyLoadoutManager), "Init"),
                    prefix: new HarmonyMethod(typeof(RemoteDroneSkills), nameof(Install)));
        }

        private static void Refresh()
        {
            foreach (var pair in Stocks)
            {
                pair.Key.baseMaxStock = Mathf.Max(1, C.Get(pair.Value.Maximum));
                pair.Key.rechargeStock = Mathf.Max(1, C.Get(pair.Value.Recharge));
                pair.Key.requiredStock = Mathf.Max(1, C.Get(pair.Value.Required));
                pair.Key.stockToConsume = Mathf.Max(0, C.Get(pair.Value.Consumed));
            }
            RemoteGunnerDrone.Refresh();
            RemoteHealingDrone.Refresh();
            RemoteJunkDrone.Refresh();
            RemoteEmergencyDrone.Refresh();
            RemoteIncineratorDrone.Refresh();
            RemoteCleanupDrone.Refresh();
            RemoteMissileDrone.Refresh();
            RemoteJailerDrone.Refresh();
            RemoteBarrierDrone.Refresh();
            RemoteBombardmentDrone.Refresh();
            RemoteFreezeDrone.Refresh();
            RemoteTC280Drone.Refresh();
        }

        internal static ConfigEntry<float> Number(string section, string name, float value, string description) =>
            C.Bind(section, name, value, description);
        internal static ConfigEntry<int> Number(string section, string name, int value, string description) =>
            C.Bind(section, name, value, description);

        private static void BindStocks(string name, string title)
        {
            string section = (name.StartsWith("Gunner", StringComparison.Ordinal) ? GunnerSection :
                name.StartsWith("Healing", StringComparison.Ordinal) ? HealingSection :
                name.StartsWith("Emergency", StringComparison.Ordinal) ? EmergencySection :
                name.StartsWith("Incinerator", StringComparison.Ordinal) ? IncineratorSection :
                name.StartsWith("Cleanup", StringComparison.Ordinal) ? CleanupSection :
                name.StartsWith("Missile", StringComparison.Ordinal) ? MissileSection :
                name.StartsWith("Jailer", StringComparison.Ordinal) ? JailerSection :
                name.StartsWith("Barrier", StringComparison.Ordinal) ? BarrierSection :
                name.StartsWith("Bombardment", StringComparison.Ordinal) ? BombardmentSection :
                name.StartsWith("TC280", StringComparison.Ordinal) ? RemoteTC280Drone.Root : JunkSection) + " - " + title;
            bool cube = name == "JunkCube";
            StockConfigs[name] = new StockSettings
            {
                Maximum = Number(section, "Base Stocks", name == "JailerLockdown" || name == "TC280TwinRocket" ? 2 : name == "BarrierOrb" || name == "MissilePainting" ? 4 : name == "CleanupTrashCannon" ? 20 : name == "CleanupSuctionBoost" ? 5 :
                    name == "IncineratorFlamethrower" ? 40 : name == "IncineratorJet" ? 2 : name == "EmergencyRestoration" ? 4 : cube ? 4 : name.EndsWith("Dash", StringComparison.Ordinal) && name != "MissileHeavyDash" ? 2 : 1,
                    "Maximum charges for this skill."),
                Recharge = Number(section, "Stocks Recharged", 1, "Charges restored after a cooldown."),
                Required = Number(section, "Stocks Required", 1,
                    name == "EmergencyRestoration" ? "Siphoning Salvo can fire whenever at least one orb is available." : "Charges needed to use the skill."),
                Consumed = Number(section, "Stocks Consumed", 1,
                    name == "EmergencyRestoration" ? "Siphoning Salvo automatically consumes every available orb when activated." : "Charges spent on use.")
            };
        }

        internal static SkillDef MakeSkill(string name, string title, string description, Type state,
            string machine, float cooldown, Sprite icon, bool beginsOnEnd = false, SkillDef customDefinition = null,
            bool useStockConfig = true)
        {
            var def = customDefinition ? customDefinition :
                name == "IncineratorFlamethrower" ? ScriptableObject.CreateInstance<SkillDef>() :
                ScriptableObject.CreateInstance<RepeatingSkillDef>();
            ((ScriptableObject)def).name = def.skillName = "3QSRemote" + name;
            def.skillNameToken = "3QS_REMOTE_" + name.ToUpperInvariant() + "_NAME";
            def.skillDescriptionToken = "3QS_REMOTE_" + name.ToUpperInvariant() + "_DESCRIPTION";
            def.activationState = new SerializableEntityStateType(state);
            def.activationStateMachineName = machine;
            def.baseRechargeInterval = cooldown;
            def.baseMaxStock = def.rechargeStock = def.requiredStock = def.stockToConsume = 1;
            def.beginSkillCooldownOnSkillEnd = beginsOnEnd;
            def.mustKeyPress = false;
            def.isCombatSkill = true;
            def.icon = icon;
            def.keywordTokens = Array.Empty<string>();
            if (useStockConfig)
            {
                Stocks[def] = StockConfigs[name];
                def.baseMaxStock = Mathf.Max(1, C.Get(Stocks[def].Maximum));
                def.rechargeStock = Mathf.Max(1, C.Get(Stocks[def].Recharge));
                def.requiredStock = Mathf.Max(1, C.Get(Stocks[def].Required));
                def.stockToConsume = Mathf.Max(0, C.Get(Stocks[def].Consumed));
            }
            R2API.LanguageAPI.Add(def.skillNameToken, title);
            R2API.LanguageAPI.Add(def.skillDescriptionToken, description);
            Definitions.Add(def);
            SkillFamily family = ScriptableObject.CreateInstance<SkillFamily>();
            ((ScriptableObject)family).name = def.skillName + "Family";
            family.variants = new[] { new SkillFamily.Variant { skillDef = def } };
            SkillFamilies[def] = family;
            Families.Add(family);
            return def;
        }

        internal static void SetSkill(GameObject prefab, string slotName, SkillDef def)
        {
            if (!prefab || !def) return;
            SkillLocator locator = prefab.GetComponent<SkillLocator>();
            if (!locator) return;
            GenericSkill slot = slotName == "Primary" ? locator.primary : slotName == "Secondary" ? locator.secondary :
                slotName == "Utility" ? locator.utility : locator.special;
            if (!slot) slot = prefab.AddComponent<GenericSkill>();
            slot.skillName = slotName;
            slot.hideInCharacterSelect = true;
            FamilyField.SetValue(slot, SkillFamilies[def]);
            if (slotName == "Primary") locator.primary = slot;
            else if (slotName == "Secondary") locator.secondary = slot;
            else if (slotName == "Utility") locator.utility = slot;
            else locator.special = slot;
            AccessTools.Field(typeof(SkillLocator), "allSkills")?.SetValue(locator, prefab.GetComponents<GenericSkill>());
        }

        internal static void AddMachine(GameObject prefab, string name)
        {
            if (!prefab || prefab.GetComponents<EntityStateMachine>().Any(m => m.customName == name)) return;
            EntityStateMachine machine = prefab.AddComponent<EntityStateMachine>();
            machine.customName = name;
            machine.initialStateType = machine.mainStateType = new SerializableEntityStateType(typeof(Idle));
            NetworkStateMachine network = prefab.GetComponent<NetworkStateMachine>();
            if (network && MachinesField != null)
            {
                var old = MachinesField.GetValue(network) as EntityStateMachine[] ?? Array.Empty<EntityStateMachine>();
                MachinesField.SetValue(network, old.Concat(new[] { machine }).ToArray());
            }
        }

        private static void Install()
        {
            GunnerBody = BodyCatalog.FindBodyPrefab("Drone1BodyRemoteOp");
            HealingBody = BodyCatalog.FindBodyPrefab("Drone2BodyRemoteOp");
            JunkBody = BodyCatalog.FindBodyPrefab("JunkDroneBodyRemoteOp");
            EmergencyBody = BodyCatalog.FindBodyPrefab("EmergencyDroneBodyRemoteOp");
            if (!EmergencyBody)
                EmergencyBody = BodyCatalog.allBodyPrefabs.FirstOrDefault(prefab => prefab &&
                    prefab.name.IndexOf("Emergency", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    prefab.name.IndexOf("RemoteOp", StringComparison.OrdinalIgnoreCase) >= 0);
            if (GunnerBody && !GunnerBody.GetComponent<NativeSkillSoundBanks>()) GunnerBody.AddComponent<NativeSkillSoundBanks>();
            if (HealingBody && !HealingBody.GetComponent<NativeSkillSoundBanks>()) HealingBody.AddComponent<NativeSkillSoundBanks>();
            if (JunkBody && !JunkBody.GetComponent<NativeSkillSoundBanks>()) JunkBody.AddComponent<NativeSkillSoundBanks>();
            if (GunnerBody) RemoteGunnerDrone.Install(GunnerBody);
            else ThreeQsVanillaTweaks.Log.Warning("Remote drone skill setup: Gunner Drone player body is unavailable.");
            if (HealingBody) RemoteHealingDrone.Install(HealingBody);
            else ThreeQsVanillaTweaks.Log.Warning("Remote drone skill setup: Healing Drone player body is unavailable.");
            RemoteLeech.Install();
            IncineratorBody = BodyCatalog.FindBodyPrefab("FlameDroneBodyRemoteOp");
            if (IncineratorBody)
            {
                if (!IncineratorBody.GetComponent<NativeSkillSoundBanks>())
                    IncineratorBody.AddComponent<NativeSkillSoundBanks>();
                RemoteIncineratorDrone.Install(IncineratorBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Incinerator Drone player body is unavailable.");
            if (EmergencyBody && !EmergencyBody.GetComponent<NativeSkillSoundBanks>())
                EmergencyBody.AddComponent<NativeSkillSoundBanks>();
            if (EmergencyBody) RemoteEmergencyDrone.Install(EmergencyBody);
            else ThreeQsVanillaTweaks.Log.Warning("Remote drone skill setup: Emergency Drone player body is unavailable.");
            if (JunkBody) RemoteJunkDrone.Install(JunkBody);
            else ThreeQsVanillaTweaks.Log.Warning("Remote drone skill setup: Junk Drone player body is unavailable.");
            CleanupBody = BodyCatalog.FindBodyPrefab("CleanupDroneBodyRemoteOp");
            if (CleanupBody)
            {
                if (!CleanupBody.GetComponent<NativeSkillSoundBanks>()) CleanupBody.AddComponent<NativeSkillSoundBanks>();
                RemoteCleanupDrone.Install(CleanupBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Cleanup Drone player body is unavailable.");
            MissileBody = BodyCatalog.FindBodyPrefab("MissileDroneBodyRemoteOp");
            if (MissileBody)
            {
                if (!MissileBody.GetComponent<NativeSkillSoundBanks>()) MissileBody.AddComponent<NativeSkillSoundBanks>();
                RemoteMissileDrone.Install(MissileBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Missile Drone player body is unavailable.");
            JailerBody = BodyCatalog.FindBodyPrefab("JailerDroneBodyRemoteOp");
            if (JailerBody)
            {
                if (!JailerBody.GetComponent<NativeSkillSoundBanks>()) JailerBody.AddComponent<NativeSkillSoundBanks>();
                RemoteJailerDrone.Install(JailerBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Jailer Drone player body is unavailable.");
            RefreshBodySlots(GunnerBody);
            RefreshBodySlots(HealingBody);
            RefreshBodySlots(JunkBody);
            RefreshBodySlots(EmergencyBody);
            RefreshBodySlots(IncineratorBody);
            RefreshBodySlots(CleanupBody);
            RefreshBodySlots(MissileBody);
            RefreshBodySlots(JailerBody);
            BarrierBody = BodyCatalog.FindBodyPrefab("RechargeDroneBodyRemoteOp");
            if (BarrierBody)
            {
                RemoteBarrierDrone.Install(BarrierBody);
                if (!BarrierBody.GetComponent<NativeSkillSoundBanks>())
                    BarrierBody.AddComponent<NativeSkillSoundBanks>().additionalBodies =
                        new[] { "EngiBody", "EnforcerBody", "Enforcer2Body" };
                RefreshBodySlots(BarrierBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Barrier Drone player body is unavailable.");
            BombardmentBody = BodyCatalog.FindBodyPrefab("BombardmentDroneBodyRemoteOp");
            if (BombardmentBody)
            {
                RemoteBombardmentDrone.Install(BombardmentBody);
                if (!BombardmentBody.GetComponent<NativeSkillSoundBanks>())
                    BombardmentBody.AddComponent<NativeSkillSoundBanks>().additionalBodies =
                        new[] { "BombardmentDroneBody", "VagrantBody" };
                RefreshBodySlots(BombardmentBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Bombardment Drone player body is unavailable.");
            FreezeBody = BodyCatalog.FindBodyPrefab("CopycatDroneBodyRemoteOp");
            if (FreezeBody)
            {
                RemoteFreezeDrone.Install(FreezeBody);
                if (!FreezeBody.GetComponent<NativeSkillSoundBanks>())
                    FreezeBody.AddComponent<NativeSkillSoundBanks>().additionalBodies = new[] { "CopycatDroneBody" };
                RefreshBodySlots(FreezeBody);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote Frost Drone player body is unavailable.");
            GameObject tc280Body = BodyCatalog.FindBodyPrefab("MegaDroneBodyRemoteOp");
            if (tc280Body)
            {
                RemoteTC280Drone.Install(tc280Body);
                if (!tc280Body.GetComponent<NativeSkillSoundBanks>())
                    tc280Body.AddComponent<NativeSkillSoundBanks>().additionalBodies = new[] { "MegaDroneBody", "ToolbotBody" };
                RefreshBodySlots(tc280Body);
            }
            else ThreeQsVanillaTweaks.Log.Warning("Remote TC-280 player body is unavailable.");
        }

        private static void RefreshBodySlots(GameObject prefab)
        {
            if (!prefab || BodySlotsField?.GetValue(null) is not GenericSkill[][] slots) return;
            BodyIndex index = BodyCatalog.FindBodyIndex(prefab);
            if (index != BodyIndex.None && (int)index < slots.Length)
                slots[(int)index] = prefab.GetComponents<GenericSkill>();
        }

        internal static bool IsMissile(CharacterBody body) => body && body.isRemoteOp && MissileBody && body.bodyIndex == MissileBody.GetComponent<CharacterBody>().bodyIndex;
        internal static bool IsGunner(CharacterBody body) => body && body.isRemoteOp && GunnerBody && body.bodyIndex == GunnerBody.GetComponent<CharacterBody>().bodyIndex;
        internal static bool IsHealing(CharacterBody body) => body && body.isRemoteOp && HealingBody && body.bodyIndex == HealingBody.GetComponent<CharacterBody>().bodyIndex;
        internal static bool IsJunk(CharacterBody body) => body && body.isRemoteOp && JunkBody && body.bodyIndex == JunkBody.GetComponent<CharacterBody>().bodyIndex;

        internal static Sprite VanillaIcon(string bodyName, string slotName, Sprite fallback, int variant = 0)
        {
            GameObject prefab = BodyCatalog.FindBodyPrefab(bodyName);
            SkillLocator locator = prefab ? prefab.GetComponent<SkillLocator>() : null;
            GenericSkill skill = slotName == "Primary" ? locator?.primary : slotName == "Secondary" ? locator?.secondary :
                slotName == "Utility" ? locator?.utility : locator?.special;
            SkillFamily family = skill ? skill.skillFamily : null;
            return family && family.variants.Length > variant && family.variants[variant].skillDef && family.variants[variant].skillDef.icon
                ? family.variants[variant].skillDef.icon : fallback;
        }

        internal static Vector3 MoveDirection(BaseState state)
        {
            InputBankTest bank = state.GetComponent<InputBankTest>();
            Vector3 movement = bank ? bank.moveVector : Vector3.zero;
            return (movement.sqrMagnitude > .01f ? movement : state.transform.forward).normalized;
        }

        internal static void ChargeDisplay(BaseState state, float fraction, bool fieryCountdown = false, Color? tint = null)
        {
            if (!state.isAuthority) return;
            var display = state.GetComponent<ChargeIndicator>() ?? state.gameObject.AddComponent<ChargeIndicator>();
            display.Progress = Mathf.Clamp01(fraction);
            display.FieryCountdown = fieryCountdown;
            display.Tint = tint;
            display.Active = true;
        }

        internal static void HideCharge(BaseState state)
        {
            if (state.isAuthority && state.GetComponent<ChargeIndicator>() is ChargeIndicator display)
                display.Active = false;
        }

        private static void LoadPhaseAssets()
        {
            if (phaseAssetsLoaded) return;
            phaseAssetsLoaded = true;
            var phase = EntityStateCatalog.InstantiateState(typeof(EntityStates.Commando.CommandoWeapon.FireFMJ))
                as GenericProjectileBaseState;
            if (phase != null)
            {
                LegacyResourcesAPI.Load<EntityStateConfiguration>(
                    "EntityStateConfigurations/EntityStates.Commando.CommandoWeapon.FireFMJ")
                    ?.BuildInstanceInitializer()?.Invoke(phase);
                phaseMuzzleEffect = phase.effectPrefab;
                phaseFireSound = phase.attackSoundString;
            }
        }

        internal static string PhaseFireSound
        {
            get
            {
                LoadPhaseAssets();
                // FireFMJ's serialized state data is not always populated when instantiated
                // outside Commando's skill family. This is Phase Round's native Wwise event.
                return string.IsNullOrEmpty(phaseFireSound) ? "Play_commando_M2" : phaseFireSound;
            }
        }

        internal static void PlayPhaseFire(BaseState state)
        {
            if (state == null || !state.gameObject) return;
            Util.PlaySound(PhaseFireSound, state.gameObject);
        }

        internal static void PlayPhaseMuzzle(BaseState state)
        {
            LoadPhaseAssets();
            if (phaseMuzzleEffect) EffectManager.SimpleMuzzleFlash(phaseMuzzleEffect, state.gameObject, "Muzzle", false);
        }

        internal static void PlayPhaseChargePulse(BaseState state, ref float nextPulse)
        {
            if (state.fixedAge < nextPulse) return;
            nextPulse = state.fixedAge + .15f;
            PlayPhaseMuzzle(state);
        }

        internal static void PlayNativeDashEffect(BaseState state, Vector3 direction, bool ending)
        {
            // Keep the previously used dash trail while omitting Blink/teleport audio.
            // The movement is handled by the dash states themselves.
            GameObject effect = EntityStates.Huntress.BlinkState.blinkPrefab;
            if (effect)
                EffectManager.SpawnEffect(effect, new EffectData {
                    origin = Util.GetCorePosition(state.gameObject),
                    rotation = Util.QuaternionSafeLookRotation(direction)
                }, false);
        }

        internal static GameObject NativeGameObject(object state, string fieldName)
        {
            if (state == null) return null;
            object value = AccessTools.Field(state.GetType(), fieldName)?.GetValue(state);
            if (value is GameObject direct) return direct;
            MethodInfo conversion = value?.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(method => method.Name == "op_Implicit" && method.ReturnType == typeof(GameObject));
            return conversion?.Invoke(null, new[] { value }) as GameObject;
        }

        private sealed class ChargeIndicator : MonoBehaviour
        {
            internal float Progress;
            internal bool Active;
            internal bool FieryCountdown;
            internal Color? Tint;
            private static Texture2D pixel;
            private void OnGUI()
            {
                if (!Active) return;
                if (!pixel)
                {
                    pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply();
                }
                float x = Screen.width * .5f - 45f, y = Screen.height * .5f + 27f;
                Color old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, .8f);
                GUI.DrawTexture(new Rect(x - 2f, y - 2f, 94f, 9f), pixel);
                GUI.color = Tint ?? (FieryCountdown
                    ? Color.Lerp(new Color(.85f, .09f, .025f), new Color(1f, .48f, .08f), Progress)
                    : Color.Lerp(new Color(.3f, .85f, 1f), new Color(1f, .8f, .3f), Progress));
                GUI.DrawTexture(new Rect(x, y, 90f * Progress, 5f), pixel);
                GUI.color = old;
            }
        }

        private sealed class DroneContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.RemoteDroneSkills";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                RemoteLeech.Register();
                RemoteGunnerDrone.Register();
                RemoteHealingDrone.Register();
                RemoteJunkDrone.Register();
                RemoteEmergencyDrone.Register();
                RemoteIncineratorDrone.Register();
                RemoteCleanupDrone.Register();
                RemoteMissileDrone.Register();
                RemoteJailerDrone.Register();
                RemoteBarrierDrone.Register();
                RemoteBombardmentDrone.Register();
                RemoteFreezeDrone.Register();
                RemoteTC280Drone.Register();
                content.skillDefs.Add(Definitions.ToArray());
                content.skillFamilies.Add(Families.ToArray());
                content.entityStateTypes.Add(RemoteGunnerDrone.States.Concat(RemoteHealingDrone.States).Concat(RemoteJunkDrone.States).Concat(RemoteEmergencyDrone.States).Concat(RemoteIncineratorDrone.States).Concat(RemoteCleanupDrone.States).Concat(RemoteMissileDrone.States).Concat(RemoteJailerDrone.States).Concat(RemoteBarrierDrone.States).Concat(RemoteBombardmentDrone.States).Concat(RemoteFreezeDrone.States).Concat(RemoteTC280Drone.States).ToArray());
                content.buffDefs.Add(RemoteHealingDrone.Buffs.Concat(RemoteJunkDrone.Buffs).Concat(RemoteEmergencyDrone.Buffs).Concat(RemoteCleanupDrone.Buffs).Concat(RemoteJailerDrone.Buffs).Concat(RemoteBarrierDrone.Buffs).Concat(RemoteBombardmentDrone.Buffs).Concat(RemoteFreezeDrone.Buffs).Concat(RemoteTC280Drone.Buffs).Concat(new[] { RemoteLeech.Buff }).ToArray());
                RemoteGunnerDrone.RegisterProjectiles(content);
                RemoteHealingDrone.RegisterProjectiles(content);
                RemoteJunkDrone.RegisterProjectiles(content);
                RemoteCleanupDrone.RegisterProjectiles(content);
                RemoteJailerDrone.RegisterProjectiles(content);
                RemoteBarrierDrone.RegisterPrefabs(content);
                RemoteBombardmentDrone.RegisterPrefabs(content);
                RemoteFreezeDrone.RegisterPrefabs(content);
                args.ReportProgress(1f); yield break;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
            { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }
    }
}
