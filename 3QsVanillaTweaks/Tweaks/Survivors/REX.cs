using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using RoR2;
using RoR2.ContentManagement;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;

namespace Tweaks.Survivors
{
    internal static class REX
    {
        private const string Root = "Survivors - 08 REX", StatsRoot = "Survivor-Stats - 08 REX";
        private static readonly string[] Names = { "Directive Inject", "Directive Drill", "Seed Barrage", "Directive Disperse", "Bramble Volley", "Proliferation", "Directive Harvest", "Tangling Growth" };
        private static readonly string[] Attacks = { "Weapon.FireSyringe", "Weapon.AimMortarRain", "Weapon.AimMortar2", "Weapon.FireSonicBoom", "Weapon.FirePlantSonicBoom", "Proliferation", "TreebotFireFruitSeed", "FireFlower2" };
        internal enum PassiveMode { Choice, Extra }
        private sealed class Profile
        {
            internal int Index;
            internal S.Skill Skill;
            internal GameObject Projectile, FinalProjectile;
            internal ConfigEntry<float> Cost;
            internal float NativeRadius;
            internal ConfigEntry<int> Limit;
            internal float NativeRootInterval;
        }
        private static readonly Dictionary<string, ConfigEntry<bool>> switches = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly Dictionary<int, Profile> profiles = new Dictionary<int, Profile>();
        private static readonly Dictionary<SkillDef, Profile> definitions = new Dictionary<SkillDef, Profile>();
        private static readonly Dictionary<GameObject, Profile> projectiles = new Dictionary<GameObject, Profile>();
        private static readonly HashSet<string> patches = new HashSet<string>();
        private static ConfigEntry<bool> enabled, statsEnabled, flowerStatsEnabled, toxinsEnabled, platingEnabled, noPermanentDamage, allShots, injectWeaken, inheritItems, alliedFlowers, healDead;
        private static ConfigEntry<PassiveMode> passiveMode;
        private static ConfigEntry<int> weakenArmour, weakenMovement, weakenDamage, toxinsChance, missingInterval, platingArmour, platingDamage, burst, baseFlowers, maxFlowers;
        private static ConfigEntry<float> weakenDuration, injectHeal, brambleHeal, flowerRange, teleportRange, teleportCooldown, teleportDamage, teleportHeal, fruitHeal, growthHeal, growthKill;
        private static CharacterBody prefabBody, flowerPrefab;
        private static S.Stats stats, flowerStats;
        private static Harmony harmony;
        private static SkillDef toxinsDefinition, platingDefinition;
        private static SkillFamily passiveFamily;
        private static bool choice;
        private static string nativePassiveDescription;
        private const string ToxinsToken = "3QS_REX_TOXINS_DESCRIPTION", PlatingToken = "3QS_REX_PLATING_DESCRIPTION", PassiveTitle = "3QS_REX_PASSIVE_SLOT";
        private sealed class PassiveRow { internal object Row; }
        private static readonly ConditionalWeakTable<object, PassiveRow> passiveRows = new ConditionalWeakTable<object, PassiveRow>();
        private static DeployableSlot flowerSlot;
        private static Type flowerSpawnerType;
        private static string flowerSpawnMethod;
        private static SkillDef teleportDefinition;
        private sealed class FlowerContext { internal SkillDef Original; internal bool Active; }
        private static readonly ConditionalWeakTable<GenericSkill, FlowerContext> flowerContexts = new ConditionalWeakTable<GenericSkill, FlowerContext>();
        [ThreadStatic] private static EntityState activeState;
        [ThreadStatic] private static CharacterBody payingBody;
        [ThreadStatic] private static CharacterMaster spawningFlower;
        [ThreadStatic] private static bool catchingUpSyringes;
        private sealed class Scope
        {
            internal EntityState Previous;
            internal readonly List<KeyValuePair<FieldInfo, object>> Fields = new List<KeyValuePair<FieldInfo, object>>();
        }
        private sealed class CostScope { internal CharacterBody Previous; }
        private sealed class TeleportClock { internal float ReadyAt; }
        private sealed class Marker : MonoBehaviour { internal Profile Profile; internal Vector3 Origin; }
        private sealed class GrowthData
        {
            internal CharacterBody Owner;
            internal readonly Dictionary<CharacterBody, float> Targets = new Dictionary<CharacterBody, float>();
            internal readonly HashSet<CharacterBody> Paid = new HashSet<CharacterBody>();
        }
        private static readonly ConditionalWeakTable<CharacterBody, TeleportClock> teleports = new ConditionalWeakTable<CharacterBody, TeleportClock>();
        private static readonly ConditionalWeakTable<EntityState, GrowthData> growths = new ConditionalWeakTable<EntityState, GrowthData>();
        private static readonly List<WeakReference<EntityState>> activeGrowths = new List<WeakReference<EntityState>>();
        private static readonly ConditionalWeakTable<GameObject, List<Marker>> areas = new ConditionalWeakTable<GameObject, List<Marker>>();
        private static Type State(string name) => typeof(EntityState).Assembly.GetType("EntityStates.Treebot." + name) ?? typeof(EntityState).Assembly.GetType("EntityStates." + name);
        private static Type Native(string name) => typeof(CharacterBody).Assembly.GetType("RoR2." + name);
        private static CharacterBody Body(EntityState state) => state?.outer ? state.outer.GetComponent<CharacterBody>() : null;
        private static bool IsRex(CharacterBody body) => body && prefabBody && body.bodyIndex == prefabBody.bodyIndex;
        private static bool IsFlower(CharacterBody body) => body && flowerPrefab && body.bodyIndex == flowerPrefab.bodyIndex;
        private static bool HasFlag(DamageInfo info, DamageType flag) => (info.damageType & flag) != DamageType.Generic;
        private static Profile ForState(EntityState state)
        {
            Type type = state?.GetType();
            if (type == null || !(type.Namespace ?? "").StartsWith("EntityStates.Treebot", StringComparison.Ordinal) &&
                !(type.Namespace == "EntityStates" && (type.Name == "PrepFlower2" || type.Name == "FireFlower2"))) return null;
            string name = type.Name;
            int i = name == "FireSyringe" ? 0 : name == "AimMortarRain" ? 1 : name == "AimMortar2" || name == "FireMortar2" ? 2 :
                name == "ChargeSonicBoom" || name == "FireSonicBoom" ? 3 : name == "ChargePlantSonicBoom" || name == "FirePlantSonicBoom" ? 4 :
                name == "Proliferation" || name == "EnterProliferationTeleport" || name == "ProliferationTeleport" ? 5 :
                name == "TreebotPrepFruitSeed" || name == "TreebotFireFruitSeed" ? 6 : name == "AimFlower" || name == "CreatePounder" || name == "PrepFlower2" || name == "FireFlower2" ? 7 : -1;
            return profiles.TryGetValue(i, out Profile p) ? p : null;
        }
        internal static void Init()
        {
            S.BindGeneral();
            enabled = C.BindStartupBool(Root, "Changes Enabled", true, "Enable REX changes. Restart to apply. Vanilla: False.");
            statsEnabled = C.BindStartupBool(StatsRoot, "Changes Enabled", true, "Enable REX stat changes. Restart to apply. Vanilla: False.");
            flowerStatsEnabled = C.BindStartupBool(StatsRoot + " Flower", "Changes Enabled", true, "Enable flower stat changes. Restart to apply. Vanilla: False.");
            toxinsEnabled = C.BindStartupBool(Root + " - Natural Toxins", "Changes Enabled", true, "Enable Natural Toxins changes. Restart to apply. Vanilla: False.");
            platingEnabled = C.BindStartupBool(Root + " - Adaptive Plating", "Changes Enabled", true, "Add Adaptive Plating. Restart to apply. Vanilla: False.");
            foreach (string name in Names) switches[name] = C.BindStartupBool(Root + " - " + name, "Changes Enabled", true, "Enable " + name + " changes. Restart to apply. Vanilla: False.");
            if (!enabled.Value) return;
            if (switches[Names[5]].Value) ContentManager.collectContentPackProviders += add => add(new TeleportContent());
            if (toxinsEnabled.Value) toxinsChance = Percent(Root + " - Natural Toxins", "Weaken Chance (%)", 50, 0, "Chance for damage dealt to inflict Weaken. Self damage cannot inflict Weaken.");
            if (platingEnabled.Value)
            {
                string section = Root + " - Adaptive Plating";
                passiveMode = C.Bind(section, "Choice Or Extra", PassiveMode.Choice, "Choose Adaptive Plating instead of Natural Toxins, or always gain it as an extra passive. Restart to apply. Vanilla: Not available.");
                missingInterval = S.Int(section, "Health Interval (%)", 1, 0, "Missing health needed for each increase.", 1);
                platingArmour = S.Int(section, "Armour Gained Per Interval", 1, 0, "Armour gained for each missing health interval.");
                platingDamage = S.Int(section, "Damage Increase Per Interval (%)", 1, 0, "Extra damage dealt for each missing health interval. Does not increase self damage.");
                choice = passiveMode.Value == PassiveMode.Choice;
                if (choice) ContentManager.collectContentPackProviders += add => add(new PassiveContent());
            }
            RoR2Application.onLoad += Load;
        }
        private sealed class TeleportContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.REXTeleport";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                CharacterBody body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Treebot/TreebotBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                SkillDef native = body.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).FirstOrDefault(d => d && d.activationState.stateType == State("Proliferation"));
                if (!native)
                {
                    ThreeQsVanillaTweaks.Log.Warning("REX teleport could not be registered: Proliferation is unavailable.");
                    args.ReportProgress(1f); yield break;
                }
                teleportDefinition = ScriptableObject.CreateInstance<SkillDef>();
                ((ScriptableObject)teleportDefinition).name = teleportDefinition.skillName = "3QSRexFlowerTeleport";
                teleportDefinition.skillNameToken = S.Read(native, "targetNameToken") as string ?? "3QS_REX_TELEPORT_NAME";
                teleportDefinition.skillDescriptionToken = "3QS_REX_TELEPORT_DESCRIPTION";
                teleportDefinition.icon = S.Read(native, "targetIcon") as Sprite ?? native.icon;
                teleportDefinition.activationState = new SerializableEntityStateType(State("EnterProliferationTeleport"));
                teleportDefinition.activationStateMachineName = native.activationStateMachineName;
                teleportDefinition.interruptPriority = native.interruptPriority;
                teleportDefinition.baseRechargeInterval = 5.5f;
                teleportDefinition.baseMaxStock = teleportDefinition.rechargeStock = teleportDefinition.requiredStock = teleportDefinition.stockToConsume = 1;
                teleportDefinition.fullRestockOnAssign = false; teleportDefinition.dontAllowPastMaxStocks = true;
                teleportDefinition.cancelSprintingOnActivation = teleportDefinition.canceledFromSprinting = false;
                teleportDefinition.isCombatSkill = false; teleportDefinition.keywordTokens = Array.Empty<string>();
                R2API.LanguageAPI.Add("3QS_REX_TELEPORT_NAME", "Teleport");
                content.skillDefs.Add(new[] { teleportDefinition });
                args.ReportProgress(1f); yield break;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args) { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }
        private sealed class PassiveContent : IContentPackProvider
        {
            private readonly ContentPack content = new ContentPack();
            public string identifier => "3Questionmarks.3QsVanillaTweaks.REXPassives";
            public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
            {
                CharacterBody body = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Treebot/TreebotBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                // Prefab assets have not run CharacterBody.Awake, which fills its cached references.
                SkillLocator locator = body.GetComponent<SkillLocator>();
                var passive = locator.passiveSkill;
                toxinsDefinition = Passive("3QSRexNaturalToxins", passive.skillNameToken, toxinsEnabled.Value ? ToxinsToken : passive.skillDescriptionToken, passive.icon);
                platingDefinition = Passive("3QSRexAdaptivePlating", "3QS_REX_PLATING_NAME", PlatingToken, AdaptivePlatingIcon(passive.icon));
                R2API.LanguageAPI.Add("3QS_REX_PLATING_NAME", "Adaptive Plating");
                R2API.LanguageAPI.Add(PassiveTitle, "Passive");
                // These tooltips must exist even if an unrelated skill fails to load later.
                if (toxinsEnabled.Value) S.SetText(ToxinsToken, ToxinsDescription);
                S.SetText(PlatingToken, PlatingDescription);
                passiveFamily = ScriptableObject.CreateInstance<SkillFamily>();
                ((ScriptableObject)passiveFamily).name = "3QSRexPassiveFamily";
                passiveFamily.variants = new[] { new SkillFamily.Variant { skillDef = toxinsDefinition }, new SkillFamily.Variant { skillDef = platingDefinition } };
                GenericSkill slot = body.gameObject.AddComponent<GenericSkill>();
                slot.skillName = "Passive"; slot.loadoutTitleToken = PassiveTitle; slot.hideInCharacterSelect = false;
                AccessTools.Field(typeof(GenericSkill), "_skillFamily").SetValue(slot, passiveFamily);
                AccessTools.Field(typeof(SkillLocator), "allSkills").SetValue(locator, body.GetComponents<GenericSkill>());
                locator.passiveSkill.enabled = false;
                content.skillDefs.Add(new[] { toxinsDefinition, platingDefinition });
                content.skillFamilies.Add(new[] { passiveFamily });
                InstallPassiveUI();
                args.ReportProgress(1f); yield break;
            }
            private static SkillDef Passive(string name, string title, string description, Sprite icon)
            {
                SkillDef def = ScriptableObject.CreateInstance<SkillDef>();
                ((ScriptableObject)def).name = def.skillName = name;
                def.skillNameToken = title; def.skillDescriptionToken = description; def.icon = icon;
                def.activationState = new SerializableEntityStateType(typeof(Idle));
                def.activationStateMachineName = ""; def.baseMaxStock = 0; def.requiredStock = 1;
                def.stockToConsume = def.rechargeStock = 0; def.isCombatSkill = false;
                def.keywordTokens = name.EndsWith("NaturalToxins", StringComparison.Ordinal) ? new[] { "KEYWORD_WEAK" } : Array.Empty<string>();
                return def;
            }
            public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args) { ContentPack.Copy(content, args.output); args.ReportProgress(1f); yield break; }
            public IEnumerator FinalizeAsync(FinalizeAsyncArgs args) { args.ReportProgress(1f); yield break; }
        }
        private static void InstallPassiveUI()
        {
            if (harmony == null) harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.REX");
            Type panel = Native("UI.LoadoutPanelController"), row = panel.GetNestedType("Row", BindingFlags.NonPublic);
            Patch(row, "FromSkillSlot", postfix: nameof(AfterPassiveRow));
            Patch(panel, "DestroyRows", nameof(BeforeDestroyRows));
            harmony.Patch(AccessTools.Method(panel, "Rebuild"), transpiler: new HarmonyMethod(typeof(REX), nameof(PassiveRowsIL)));
            Patch(Native("UI.CharacterSelectController"), "BuildSkillStripDisplayData", postfix: nameof(AfterSkillStrip));
        }
        private static void AfterPassiveRow(object owner, GenericSkill skillSlot, object __result)
        {
            if (!skillSlot || skillSlot.skillFamily != passiveFamily || __result == null) return;
            passiveRows.Remove(owner); passiveRows.Add(owner, new PassiveRow { Row = __result });
        }
        private static void BeforeDestroyRows(object __instance) => passiveRows.Remove(__instance);
        private static void OrderPassiveRows(object panel)
        {
            if (!passiveRows.TryGetValue(panel, out PassiveRow tracked) || !(S.Read(panel, "rows") is IList rows)) return;
            int index = rows.IndexOf(tracked.Row);
            if (index < 0) return;
            rows.RemoveAt(index); rows.Insert(0, tracked.Row);
            (S.Read(tracked.Row, "rowPanelTransform") as Transform)?.SetAsFirstSibling();
        }
        private static IEnumerable<CodeInstruction> PassiveRowsIL(IEnumerable<CodeInstruction> instructions)
        {
            bool skinRow = false;
            foreach (CodeInstruction code in instructions)
            {
                yield return code;
                if (code.operand is MethodInfo method)
                {
                    if (method.Name == "FromSkin") skinRow = true;
                    else if (skinRow && method.Name == "Add")
                    {
                        // Reorder before the native navigation links are constructed.
                        yield return new CodeInstruction(OpCodes.Ldarg_0);
                        yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(REX), nameof(OrderPassiveRows)));
                        skinRow = false;
                    }
                }
            }
        }
        private static void AfterSkillStrip(object[] __args)
        {
            if (__args.Length < 3 || !(S.Read(__args[1], "bodyPrefabBodyComponent") is CharacterBody body) || !IsRex(body) || !(__args[2] is IList rows)) return;
            string toxins = Language.GetString(toxinsDefinition.skillNameToken), plating = Language.GetString(platingDefinition.skillNameToken);
            for (int i = 0; i < rows.Count; i++)
            {
                string title = S.Read(rows[i], "titleString") as string;
                if (title != toxins && title != plating) continue;
                object passive = rows[i]; rows.RemoveAt(i); rows.Insert(0, passive); break;
            }
        }
        private static void Patch(Type type, string name, string prefix = null, string postfix = null, string finalizer = null, Type[] args = null)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, name, args);
            if (target == null) throw new MissingMethodException(type?.FullName, name);
            if (!patches.Add(target.MethodHandle.Value + ":" + prefix + ":" + postfix + ":" + finalizer)) return;
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(REX), prefix), postfix == null ? null : new HarmonyMethod(typeof(REX), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(REX), finalizer));
        }
        private static void Hook(Type type)
        {
            foreach (string name in new[] { "OnEnter", "FixedUpdate", "OnExit", "OnProjectileFiredLocal" })
            {
                MethodInfo method = type == null ? null : AccessTools.Method(type, name);
                if (method == null || method.DeclaringType == typeof(EntityState) || method.DeclaringType == typeof(BaseState)) continue;
                Patch(type, name, nameof(BeforeState), finalizer: nameof(EndScope));
            }
        }
        private static void Load()
        {
            try
            {
                prefabBody = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Treebot/TreebotBody.prefab").WaitForCompletion().GetComponent<CharacterBody>();
                nativePassiveDescription = Language.GetString(prefabBody.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken);
                harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Survivors.REX");
                if (statsEnabled.Value) stats = new S.Stats(StatsRoot, prefabBody);
                string section = Root + " - General";
                noPermanentDamage = S.Bool(section, "Self Damage Ignores Eclipse 8", true, false, "Health costs do not cause permanent damage in Eclipse 8.");
                weakenArmour = S.Int(section, "Weaken Armour Penalty", 30, 30, "Armour removed by Weaken.");
                weakenMovement = Percent(section, "Weaken Movement Penalty (%)", 40, 40, "Movement speed removed by Weaken.");
                weakenDamage = Percent(section, "Weaken Damage Penalty (%)", 25, 40, "Damage dealt removed by Weaken.");
                weakenDuration = S.Float(section, "Weaken Duration", 3f, 6f, "Weaken duration in seconds.");
                if (toxinsEnabled.Value) S.SetText(ToxinsToken, ToxinsDescription);
                if (platingEnabled.Value) S.SetText(PlatingToken, PlatingDescription);
                SkillDef[] defs = prefabBody.GetComponents<GenericSkill>().Where(s => s.skillFamily).SelectMany(s => s.skillFamily.variants.Select(v => v.skillDef)).Where(d => d).Distinct().ToArray();
                for (int i = 0; i < Names.Length; i++)
                {
                    if (!switches[Names[i]].Value) continue;
                    try
                    {
                    Type type = State(Attacks[i]);
                    SkillDef def = defs.FirstOrDefault(d => Matches(i, d.activationState.stateType));
                    if (i == 7 && def && (def.activationState.stateType?.Name == "AimFlower" || def.activationState.stateType?.Name == "CreatePounder")) type = def.activationState.stateType;
                    if (type == null || !def) throw new MissingMemberException("REX skill unavailable: " + Names[i]);
                    var p = new Profile { Index = i, Skill = new S.Skill(Root + " - " + Names[i], def, type, switches[Names[i]]) };
                    Bind(p); p.Skill.Loaded = true; profiles[i] = p; definitions[def] = p;
                    if (p.Projectile) RegisterProjectile(p.Projectile, p, new HashSet<GameObject>());
                    if (p.FinalProjectile) RegisterProjectile(p.FinalProjectile, p, new HashSet<GameObject>());
                    Hook(type); Hook(def.activationState.stateType);
                    S.SetDescription(p.Skill, "3QS_REX_SKILL_" + i, () => Description(p));
                    Tweaks.ModCompatibility.LookingGlassSkillStats.Register(def, () => Extra(p), p.Skill.Proc == null ? (Func<float>)null : () => C.Get(p.Skill.Proc));
                    }
                    catch (Exception error)
                    {
                        ThreeQsVanillaTweaks.Log.Warning("REX skill could not be loaded: " + Names[i] + ": " + error);
                    }
                }
                Patch(typeof(CharacterBody), "Awake", postfix: nameof(AfterBody));
                Patch(typeof(CharacterBody), "RecalculateStats", postfix: nameof(AfterStats));
                harmony.Patch(AccessTools.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(REX), nameof(WeakenStatsIL)));
                Patch(typeof(HealthComponent), "TakeDamage", nameof(BeforeDamage), nameof(AfterDamageHit), nameof(AfterDamage));
                MethodInfo addBuff = typeof(CharacterBody).GetMethods().FirstOrDefault(m => m.Name == "AddUnmanagedBuff" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(BuffDef));
                if (addBuff == null) throw new MissingMethodException("CharacterBody.AddUnmanagedBuff(BuffDef)");
                Patch(typeof(CharacterBody), "AddUnmanagedBuff", nameof(BeforeUnmanagedBuff), args: addBuff.GetParameters().Select(p => p.ParameterType).ToArray());
                Patch(typeof(GlobalEventManager), "OnHitEnemy", nameof(BeforeHit), finalizer: nameof(AfterHit));
                Patch(typeof(CharacterBody), "AddTimedBuff", nameof(BeforeTimedBuff), args: new[] { typeof(BuffDef), typeof(float) });
                Patch(typeof(GlobalEventManager), "OnCharacterDeath", postfix: nameof(OnDeath));
                if (profiles.Count > 0)
                {
                    Patch(typeof(SkillDef), "OnExecute", nameof(BeforePayment), finalizer: nameof(AfterPayment));
                    Patch(typeof(SkillDef), "HasRequiredStockAndDelay", postfix: nameof(AfterRequired));
                    Patch(typeof(SkillDef), "GetRechargeStock", postfix: nameof(AfterRecharge));
                    Patch(typeof(SkillDef), "OnFixedUpdate", nameof(BeforeSkillUpdate));
                    Patch(typeof(ProjectileManager), "FireProjectile", nameof(BeforeProjectile), args: new[] { typeof(FireProjectileInfo) });
                    Patch(typeof(ProjectileManager), "InitializeProjectile", postfix: nameof(AfterProjectileInitialized));
                    Patch(typeof(ProjectileController), "Start", nameof(BeforeProjectileStart));
                }
                if (profiles.ContainsKey(0))
                {
                    Patch(typeof(ProjectileInflictTimedBuff), "OnDamageInflictedServer", nameof(BeforeProjectileBuff));
                    harmony.Patch(AccessTools.Method(State("Weapon.FireSyringe"), "FixedUpdate"), transpiler: new HarmonyMethod(typeof(REX), nameof(SyringeSpreadIL)));
                    Patch(State("Weapon.FireSyringe"), "FixedUpdate", postfix: nameof(AfterSyringeUpdate));
                }
                if (profiles.ContainsKey(4))
                {
                    Patch(State("Weapon.FirePlantSonicBoom"), "AddDebuff", nameof(BeforeBrambleHeal));
                    Patch(typeof(OrbManager), "AddOrb", nameof(BeforeHealOrb));
                }
                if (profiles.ContainsKey(5)) InstallFlowers();
                if (profiles.ContainsKey(6)) Patch(typeof(HealthPickup), "OnTriggerStay", nameof(BeforeFruitPickup));
                if (profiles.ContainsKey(7))
                {
                    Type flower = State("TreebotFlower.TreebotFlower2Projectile");
                    Patch(flower, "OnEnter", nameof(BeforeGrowth), nameof(AfterGrowth), nameof(EndScope));
                    Patch(flower, "FixedUpdate", nameof(BeforeGrowth), finalizer: nameof(EndScope));
                    Patch(flower, "RootPulse", postfix: nameof(AfterRoot));
                    Patch(flower, "HealPulse", nameof(BeforeGrowthHeal));
                    harmony.Patch(AccessTools.Method(flower, "FixedUpdate"), transpiler: new HarmonyMethod(typeof(REX), nameof(GrowthIntervalsIL)));
                }
                S.SetText("KEYWORD_WEAK", () => "<style=cKeywordName>Weak</style><style=cSub>Reduces armour by <style=cIsDamage>" + C.Get(weakenArmour) + "</style>, movement speed by <style=cIsUtility>" + C.Get(weakenMovement) + "%</style>, and damage dealt by <style=cIsDamage>" + C.Get(weakenDamage) + "%</style>.</style>");
                if (toxinsEnabled.Value || platingEnabled.Value && !choice) S.SetText(prefabBody.GetComponent<SkillLocator>().passiveSkill.skillDescriptionToken,
                    () => (toxinsEnabled.Value ? ToxinsDescription() : nativePassiveDescription) + (platingEnabled.Value && !choice ? "\n\n" + PlatingDescription() : ""));
                if (platingEnabled.Value) S.SetText("3QS_REX_PLATING_DESCRIPTION", PlatingDescription);
                C.EffectiveSettingsChanged += Apply; Apply();
            }
            catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("REX changes could not be loaded: " + error); }
        }
        private static bool Matches(int i, Type type)
        {
            string name = type?.Name;
            return name == State(Attacks[i])?.Name || i == 2 && (name == "AimMortar" || name == "FireMortar2") || i == 3 && name == "ChargeSonicBoom" ||
                i == 4 && name == "ChargePlantSonicBoom" || i == 6 && name == "TreebotPrepFruitSeed" ||
                i == 7 && (name == "PrepFlower2" || name == "FireFlower2" || name == "AimFlower" || name == "CreatePounder");
        }
        private static ConfigEntry<int> Percent(string section, string key, int value, int native, string description) =>
            C.Bind(section, key, value, new ConfigDescription(description + " Vanilla: " + native + ".", new AcceptableValueRange<int>(0, 100)));
        private static void Bind(Profile p)
        {
            S.Skill s = p.Skill; int i = p.Index; object sample = s.Sample;
            s.Bind(changedCooldown: i == 3 ? 4.5f : i == 5 ? 30f : (float?)null, damage: false);
            p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
            float nativeDamage = S.Value(sample, "damageCoefficient", 0f) * 100f;
            float nativeProc = S.Value(sample, "procCoefficient", p.Projectile && p.Projectile.GetComponent<ProjectileController>() ? p.Projectile.GetComponent<ProjectileController>().procCoefficient : 1f);
            if ((i == 2 || i == 6 || i == 7) && p.Projectile && p.Projectile.GetComponent<ProjectileExplosion>() is ProjectileExplosion nativeBlast)
            { nativeDamage *= nativeBlast.blastDamageCoefficient; nativeProc *= nativeBlast.blastProcCoefficient; }
            if (i == 0)
            {
                p.FinalProjectile = S.Read(sample, "finalProjectilePrefab") as GameObject;
                burst = S.Int(s.Section, "Burst Size", 3, (int)S.Value(sample, "projectileCount", 3), "Shots fired per burst.", 1);
                allShots = S.Bool(s.Section, "Apply Healing And Weaken To All Shots", true, false, "Allow every shot to heal and apply the skill's Weaken effect.");
                injectWeaken = S.Bool(s.Section, "Weaken On Hit", false, true, "Make healing shots inflict Weaken independently of Natural Toxins.");
                var nativeHeal = p.FinalProjectile ? p.FinalProjectile.GetComponent<ProjectileHealOwnerOnDamageInflicted>() : null;
                injectHeal = S.Float(s.Section, "Heal Amount (%)", 40f, nativeHeal ? nativeHeal.fractionOfDamage * 100f : 60f, "Healing as a percentage of damage dealt by each healing shot.");
            }
            if (i == 1)
            {
                var zone = p.Projectile.GetComponent<ProjectileDotZone>();
                nativeDamage *= zone.damageCoefficient;
                nativeProc *= zone.overlapProcCoefficient;
                s.Interval = S.Float(s.Section, "Damage Interval", 1f / zone.resetFrequency, 1f / zone.resetFrequency, "Seconds between damage ticks.", 0.001f);
                s.Duration = S.Float(s.Section, "AOE Duration", 3.2f, zone.lifetime, "Drill duration in seconds. -1 lasts until its normal end condition.", -1f);
                p.NativeRadius = RadiusOf(p.Projectile);
                s.Radius = S.Float(s.Section, "AOE Radius", p.NativeRadius * 1.5f, p.NativeRadius, "Drill radius in metres.");
                p.Limit = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum active drills per player. Recasting removes the oldest drill. -1 is unlimited.", -1);
            }
            if (i == 3 || i == 4)
            {
                s.Radius = S.Float(s.Section, "AOE Radius", S.Value(sample, "maxDistance"), S.Value(sample, "maxDistance"), "Maximum distance reached by the shockwave, in metres.");
            }
            if (i == 4) brambleHeal = S.Float(s.Section, "Health Healed On Hit (%)", 10f, 10f, "Health restored per enemy hit, as a percentage of that enemy's maximum health. Vanilla uses REX's maximum health.");
            if (i == 5) BindFlowers(p);
            if (i == 6)
            {
                GameObject fruit = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Treebot/TreebotFruitPack.prefab").WaitForCompletion();
                fruitHeal = S.Float(s.Section, "Healing Fruits Recovery (%)", 25f, fruit.GetComponentInChildren<HealthPickup>(true).fractionalHealing * 100f, "Maximum health restored by each healing fruit.");
            }
            if (i == 7)
            {
                object flower = EntityStateCatalog.InstantiateState(State("TreebotFlower.TreebotFlower2Projectile"));
                s.Radius = S.Float(s.Section, "AOE Radius", S.Value(flower, "radius"), S.Value(flower, "radius"), "Rooting radius in metres.");
                s.Duration = S.Float(s.Section, "AOE Duration", S.Value(flower, "duration"), S.Value(flower, "duration"), "Root duration in seconds. -1 lasts until the flower is removed.", -1f);
                float interval = S.Value(flower, "duration") / Math.Max(1f, S.Value(flower, "healPulseCount"));
                s.Interval = S.Float(s.Section, "Healing Interval", interval, interval, "Seconds between healing pulses.", 0.001f);
                p.NativeRootInterval = S.Value(flower, "duration") / Math.Max(1f, S.Value(flower, "rootPulseCount"));
                growthHeal = S.Float(s.Section, "Health Per Target (% Per Second)", 2f, S.Value(flower, "healthFractionYieldPerHit") * 100f / Math.Max(0.001f, S.Value(flower, "duration")), "Health restored each second as a percentage of each rooted target's maximum health. Vanilla uses REX's maximum health.");
                healDead = S.Bool(s.Section, "Continue Healing After Target Dies", false, true, "Continue receiving healing from targets that die while rooted.");
                growthKill = S.Float(s.Section, "Health Gained On Kill (%)", 20f, 0f, "Health restored when a rooted target dies, as a percentage of its maximum health.");
                p.Limit = S.Int(s.Section, "Max Instances Of AOE", -1, -1, "Maximum active growths per player. Recasting removes the oldest growth. -1 is unlimited.", -1);
            }
            if (i != 3 && i != 5) s.BindDamage(nativeDamage, i == 0 || i == 1 ? 100f : i == 6 ? 600f : i == 7 ? 330f : (float?)null, i == 0 || i == 4 ? 1f : nativeProc, i == 1);
            if (p.Projectile && i != 1 && (p.Projectile.GetComponents<Component>().Any(c => c && S.Read(c, "desiredForwardSpeed") is float speed && speed > 0f) || S.Value(sample, "projectileMaxSpeed", -1f) > 0f))
                s.BindProjectileSpeed(p.Projectile);
            if (i == 2 || i == 4 || i == 5 || i == 7)
            {
                float listed = i == 2 ? 15f : i == 4 ? 20f : i == 5 ? 30f : 25f;
                p.Cost = C.Bind(s.Section, "Health Cost (%)", listed, new ConfigDescription("Current health spent to use the skill, before damage reduction. Vanilla description: " + S.Number(listed) + "%.", new AcceptableValueRange<float>(0f, 100f)));
            }
            if (i == 2 || i == 6)
            {
                var explosion = p.Projectile ? p.Projectile.GetComponent<ProjectileExplosion>() : null;
                if (explosion)
                {
                    s.Radius = S.Float(s.Section, "AOE Radius", explosion.blastRadius, explosion.blastRadius, "Explosion radius in metres.");
                    s.Area = C.Bind(s.Section, "AOE Damage Falloff", S.AreaFalloff.None, "Damage falloff from the centre. Vanilla: None.");
                }
            }
        }
        private static void BindFlowers(Profile p)
        {
            string section = p.Skill.Section;
            Component spawn = FindFlowerSpawner(p.Projectile, new HashSet<GameObject>());
            if (!spawn) throw new MissingMemberException("Proliferation projectile has no supported flower spawner.");
            flowerSpawnerType = spawn.GetType();
            flowerSpawnMethod = spawn is ProjectileSpawnMaster ? "SpawnMaster" : "SpawnDeployableMaster";
            object slot = S.Read(spawn, "deployableSlot");
            if (!(slot is DeployableSlot)) throw new MissingMemberException("Proliferation deployable slot is unavailable.");
            flowerSlot = (DeployableSlot)slot;
            GameObject masterPrefab = S.Read(spawn, "characterMasterPrefabToSpawn") as GameObject;
            if (!masterPrefab && S.Read(spawn, "characterMasterPrefabToSpawnAddress") is object address)
            {
                object key = S.Read(address, "RuntimeKey");
                if (key != null && !string.IsNullOrEmpty(key.ToString())) masterPrefab = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
            }
            if (!masterPrefab)
            {
                SpawnCard card = (S.Read(spawn, "spawnCard") ?? S.Read(spawn, "deployableSpawnCard")) as SpawnCard;
                if (card) masterPrefab = card.prefab;
            }
            CharacterMaster master = masterPrefab ? masterPrefab.GetComponent<CharacterMaster>() : null;
            if (!master || !master.bodyPrefab) throw new MissingMemberException("Proliferation flower master or body prefab is unavailable.");
            flowerPrefab = master.bodyPrefab.GetComponent<CharacterBody>();
            if (!flowerPrefab) throw new MissingMemberException("Proliferation flower body has no CharacterBody.");
            object shot = EntityStateCatalog.InstantiateState(typeof(EntityState).Assembly.GetType("EntityStates.ProliferationFlowerTurret.FireShot"));
            p.Skill.BindDamage(S.Value(shot, "damageCoefficient") * 100f, 200f, 1f);
            if (flowerStatsEnabled.Value)
            {
                // Offshoot damage starts from REX's base stats, and has its own editable page.
                S.Float(StatsRoot + " Flower", "Damage", prefabBody.baseDamage, flowerPrefab.baseDamage, "Base damage.");
                S.Float(StatsRoot + " Flower", "Damage Per Level", prefabBody.levelDamage, flowerPrefab.levelDamage, "Damage gained per level.");
                flowerStats = new S.Stats(StatsRoot + " Flower", flowerPrefab);
            }
            inheritItems = S.Bool(section, "Inherits Items", true, false, "Flowers inherit the items you hold when planted.");
            baseFlowers = S.Int(section, "Base Flowers", 2, 2, "Flowers that can be active at base. Each extra utility stock adds one flower.");
            maxFlowers = C.Bind(section, "Maximum Flowers", 3, new ConfigDescription("Absolute maximum active flowers per player. Vanilla: No absolute maximum.", new AcceptableValueRange<int>(0, int.MaxValue)));
            float nativeRange = master.GetComponents<RoR2.CharacterAI.AISkillDriver>().Where(d => d.skillSlot != SkillSlot.None && !float.IsInfinity(d.maxDistance)).Select(d => d.maxDistance).DefaultIfEmpty(60f).Max();
            flowerRange = S.Float(section, "Flower Range", 90f, nativeRange, "Maximum range at which flowers attack, in metres.");
            Component tracker = prefabBody.GetComponent(Native("TurretTracking"));
            teleportRange = S.Float(section, "Teleport Range", 80f, S.Value(tracker, "maxTrackingDisance", 20f), "Maximum distance to target a flower for teleportation, in metres.");
            teleportCooldown = S.Float(section, "Teleport Cooldown", 5.5f, 0f, "Seconds between flower teleports. Separate from Proliferation's cooldown.");
            teleportDamage = C.Bind(section, "Damage On Teleport (%)", 30f, new ConfigDescription("Flower's maximum health lost when used for teleportation. 100 destroys it. Vanilla: 100.", new AcceptableValueRange<float>(0f, 100f)));
            object sample = EntityStateCatalog.InstantiateState(State("ProliferationTeleport"));
            teleportHeal = S.Float(section, "Heal On Teleport (%)", 10f, S.Value(sample, "percentToHeal") * 100f, "Maximum health restored when teleporting. Negative values deal damage instead.", -100f);
            alliedFlowers = S.Bool(section, "Can Teleport To Allied Flowers", true, true, "Allow teleportation to flowers planted by other allied REX players.");
            S.SetText("3QS_REX_TELEPORT_DESCRIPTION", TeleportDescription);
            S.SetText(S.Read(p.Skill.Definition, "targetDescription") as string ?? "3QS_REX_TELEPORT_DESCRIPTION", TeleportDescription);
            // LookingGlass reads the contextual SkillDef's own cooldown for its normal stats line.
            if (teleportDefinition) Tweaks.ModCompatibility.LookingGlassSkillStats.Register(teleportDefinition, () => "\nTeleport Range: <style=cIsUtility>" + S.Number(C.Get(teleportRange)) + "m</style>");
        }
        private static Component FindFlowerSpawner(GameObject prefab, HashSet<GameObject> seen)
        {
            if (!prefab || !seen.Add(prefab)) return null;
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
                if (component && (component is ProjectileSpawnMaster || component.GetType().Name == "ProjectileSpawnDeployableImpact")) return component;
            foreach (Component component in components)
                if (component)
                    foreach (string field in new[] { "childrenProjectilePrefab", "childProjectilePrefab" })
                        if (S.Read(component, field) is GameObject child)
                        {
                            Component found = FindFlowerSpawner(child, seen);
                            if (found) return found;
                        }
            return null;
        }
        private static void RegisterProjectile(GameObject prefab, Profile p, HashSet<GameObject> seen)
        {
            if (!prefab || !seen.Add(prefab)) return;
            projectiles[prefab] = p;
            foreach (Component component in prefab.GetComponents<Component>())
            {
                if (!component) continue;
                foreach (string field in new[] { "childrenProjectilePrefab", "childProjectilePrefab" })
                    if (S.Read(component, field) is GameObject child) RegisterProjectile(child, p, seen);
            }
        }
        private static void Apply()
        {
            if (!prefabBody) return;
            if (teleportDefinition && teleportCooldown != null) teleportDefinition.baseRechargeInterval = C.Get(teleportCooldown);
            stats?.Apply(prefabBody); flowerStats?.Apply(flowerPrefab);
            foreach (Profile p in profiles.Values)
            {
                p.Skill.ApplyDefinition();
                bool weak = p.Index == 0 ? C.Get(injectWeaken) : p.Index == 3;
                p.Skill.SetKeyword("KEYWORD_WEAK", weak);
                // Some versions use WEAKEN for the same keyword.
                p.Skill.SetKeyword("KEYWORD_WEAKEN", false);
            }
            foreach (CharacterBody body in CharacterBody.readOnlyInstancesList) { ApplyBody(body); body.MarkAllStatsDirty(); }
            if (platingDefinition) R2API.LanguageAPI.Add("3QS_REX_PLATING_DESCRIPTION", PlatingDescription());
        }
        private static void AfterBody(CharacterBody __instance) => ApplyBody(__instance);
        private static void ApplyBody(CharacterBody body)
        {
            if (IsRex(body))
            {
                stats?.Apply(body);
                if (platingEnabled.Value && !body.GetComponent<PlatingMonitor>()) body.gameObject.AddComponent<PlatingMonitor>();
            }
            if (IsFlower(body)) { flowerStats?.Apply(body); body.baseVisionDistance = C.Get(flowerRange); }
        }
        private static bool UsesPlating(CharacterBody body) => IsRex(body) && platingEnabled.Value && (!choice || body.GetComponents<GenericSkill>().Any(s => s.skillDef == platingDefinition));
        private static bool UsesToxins(CharacterBody body) => IsRex(body) && toxinsEnabled.Value && (!choice || !platingDefinition || body.GetComponents<GenericSkill>().Any(s => s.skillDef == toxinsDefinition));
        private static int PlatingSteps(CharacterBody body) => body && body.healthComponent && body.healthComponent.alive ? MissingSteps(body.healthComponent.health, body.healthComponent.fullHealth, C.Get(missingInterval)) : 0;
        internal static int MissingSteps(float health, float maximum, int interval) => maximum <= 0f ? 0 : (int)Math.Floor(Math.Max(0f, Math.Min(100f, 100f * (1f - health / maximum))) / Math.Max(1, interval) + 0.0001f);
        private sealed class PlatingMonitor : MonoBehaviour
        {
            private CharacterBody body; private int previous = -1;
            private void Awake() { body = GetComponent<CharacterBody>(); }
            private void FixedUpdate()
            {
                int current = UsesPlating(body) ? PlatingSteps(body) : 0;
                if (current != previous) { previous = current; body.MarkAllStatsDirty(); }
            }
        }
        private static void AfterStats(CharacterBody __instance)
        {
            if (!UsesPlating(__instance)) return;
            int steps = PlatingSteps(__instance);
            AccessTools.Property(typeof(CharacterBody), "armor").SetValue(__instance, __instance.armor + steps * C.Get(platingArmour));
            AccessTools.Property(typeof(CharacterBody), "damage").SetValue(__instance, __instance.damage * (1f + steps * C.Get(platingDamage) / 100f));
        }
        private static float WeakArmour(float original, CharacterBody body) => body.HasBuff(RoR2Content.Buffs.Weak) ? C.Get(weakenArmour) : original;
        private static float WeakDamage(float original, CharacterBody body) => body.HasBuff(RoR2Content.Buffs.Weak) ? 1f - C.Get(weakenDamage) / 100f : original;
        private static float WeakMovement(float original, CharacterBody body) => body.HasBuff(RoR2Content.Buffs.Weak) ? 1f - C.Get(weakenMovement) / 100f : original;
        private static IEnumerable<CodeInstruction> WeakenStatsIL(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList(); int end = -1, kind = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction code = codes[i];
                if (code.opcode == OpCodes.Ldsfld && code.operand is FieldInfo buff && buff.Name == "Weak") { end = i + 50; kind = 0; }
                yield return code;
                if (i <= end && code.opcode == OpCodes.Ldc_R4 && code.operand is float value && (kind == 0 && value == 30f || kind > 0 && kind < 3 && value == 0.6f))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(REX), kind == 0 ? nameof(WeakArmour) : kind == 1 ? nameof(WeakDamage) : nameof(WeakMovement)));
                    kind++;
                }
            }
        }
        private static void Set(Scope scope, object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(instance.GetType(), name); if (field == null) return;
            if (field.IsStatic) scope.Fields.Add(new KeyValuePair<FieldInfo, object>(field, field.GetValue(null)));
            field.SetValue(field.IsStatic ? null : instance, Convert.ChangeType(value, field.FieldType));
        }
        private static void BeforeState(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState }; Profile p = ForState(__instance);
            if (p == null || !IsRex(Body(__instance))) return;
            activeState = __instance; S.Skill skill = p.Skill;
            if (skill.Damage != null && p.Index != 5) Set(__state, __instance, "damageCoefficient", C.Get(skill.Damage) / 100f);
            if (skill.Proc != null) Set(__state, __instance, "procCoefficient", C.Get(skill.Proc));
            if (p.Cost != null) Set(__state, __instance, "healthCostFraction", C.Get(p.Cost) / 100f);
            if (p.Index == 0)
            {
                Set(__state, __instance, "projectileCount", C.Get(burst));
                if (C.Get(burst) == 1) Set(__state, __instance, "totalYawSpread", 0f);
            }
            if (p.Index == 3 || p.Index == 4) { Set(__state, __instance, "maxDistance", C.Get(skill.Radius)); Set(__state, __instance, "slowDuration", C.Get(weakenDuration)); }
        }
        private static Exception EndScope(Scope __state, Exception __exception)
        {
            if (__state != null) { for (int i = __state.Fields.Count - 1; i >= 0; i--) __state.Fields[i].Key.SetValue(null, __state.Fields[i].Value); activeState = __state.Previous; }
            return __exception;
        }
        private static float SpreadDivisor(float value) => Math.Max(1f, value);
        private static void AfterSyringeUpdate(EntityState __instance)
        {
            if (catchingUpSyringes || !IsRex(Body(__instance)) || ForState(__instance)?.Index != 0 || __instance.outer.state != __instance) return;
            float duration = S.Value(__instance, "fireDuration"); if (duration <= 0f) return;
            int due = Math.Min(C.Get(burst), 1 + (int)Math.Floor(S.Value(__instance, "fixedAge") / duration * C.Get(burst)));
            try
            {
                catchingUpSyringes = true;
                // The native state emits at most one shot per physics update.
                // Catch up overdue shots so larger bursts are not cut off by the animation.
                while (S.Value(__instance, "projectilesFired") < due)
                {
                    float before = S.Value(__instance, "projectilesFired");
                    AccessTools.Method(__instance.GetType(), "FixedUpdate").Invoke(__instance, null);
                    if (S.Value(__instance, "projectilesFired") <= before) break;
                }
            }
            finally { catchingUpSyringes = false; }
        }
        private static IEnumerable<CodeInstruction> SyringeSpreadIL(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            for (int i = 0; i < codes.Count; i++)
            {
                yield return codes[i];
                if (i > 0 && i + 1 < codes.Count && codes[i].opcode == OpCodes.Conv_R4 && codes[i - 1].opcode == OpCodes.Sub && codes[i + 1].opcode == OpCodes.Div)
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(REX), nameof(SpreadDivisor)));
            }
        }
        private static void BeforeDamage(HealthComponent __instance, DamageInfo damageInfo, out CostScope __state)
        {
            __state = new CostScope { Previous = payingBody };
            Profile p = ForState(activeState);
            bool cost = p?.Cost != null && IsRex(__instance.body) && Body(activeState) == __instance.body && !damageInfo.attacker && !damageInfo.inflictor && HasFlag(damageInfo, DamageType.NonLethal);
            if (cost)
            {
                payingBody = __instance.body;
                damageInfo.damage = __instance.combinedHealth * C.Get(p.Cost) / 100f;
                damageInfo.damageType &= ~DamageType.BypassArmor;
            }
            Marker marker = damageInfo.inflictor ? damageInfo.inflictor.GetComponent<Marker>() : null;
            Profile damageProfile = marker ? marker.Profile : ForState(activeState)?.Index == 4 && damageInfo.attacker && damageInfo.attacker != __instance.gameObject && Body(activeState)?.gameObject == damageInfo.attacker ? ForState(activeState) : null;
            if (damageProfile?.Skill.Falloff != null)
            {
                float distance = Vector3.Distance(marker ? marker.Origin : Body(activeState).corePosition, damageInfo.position);
                S.BulletFalloff falloff = C.Get(damageProfile.Skill.Falloff);
                if (falloff == S.BulletFalloff.Default) damageInfo.damage *= Mathf.Lerp(1f, 0.5f, Mathf.InverseLerp(25f, 60f, distance));
                if (falloff == S.BulletFalloff.Buckshot) damageInfo.damage *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(7f, 25f, distance));
            }
        }
        private static Exception AfterDamage(CostScope __state, Exception __exception) { payingBody = __state.Previous; return __exception; }
        private static void AfterDamageHit(HealthComponent __instance, DamageInfo damageInfo)
        {
            if (!NetworkServer.active || damageInfo.rejected || damageInfo.damage <= 0f || !damageInfo.attacker || damageInfo.attacker == __instance.gameObject || !__instance.body || !__instance.alive) return;
            CharacterBody attacker = damageInfo.attacker.GetComponent<CharacterBody>();
            if (UsesToxins(attacker) && Util.CheckRoll(C.Get(toxinsChance), attacker.master)) __instance.body.AddTimedBuff(RoR2Content.Buffs.Weak, C.Get(weakenDuration));
        }
        private static bool BeforeUnmanagedBuff(CharacterBody __instance, BuffDef buffDef) => !(C.Get(noPermanentDamage) && payingBody == __instance && buffDef == RoR2Content.Buffs.PermanentCurse);
        [ThreadStatic] private static bool weakHit;
        private static void BeforeHit(DamageInfo damageInfo, GameObject victim, out bool __state)
        {
            __state = weakHit; weakHit = false;
            if (!NetworkServer.active || damageInfo.rejected || damageInfo.damage <= 0f || !victim || damageInfo.attacker == victim || !damageInfo.attacker) return;
            CharacterBody attacker = damageInfo.attacker.GetComponent<CharacterBody>();
            weakHit = IsRex(attacker) || HasFlag(damageInfo, DamageType.WeakOnHit);
        }
        private static Exception AfterHit(bool __state, Exception __exception) { weakHit = __state; return __exception; }
        private static void BeforeTimedBuff(BuffDef buffDef, ref float duration) { if (weakHit && buffDef == RoR2Content.Buffs.Weak) duration = C.Get(weakenDuration); }
        private static void BeforeProjectile(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!fireProjectileInfo.owner || !IsRex(fireProjectileInfo.owner.GetComponent<CharacterBody>()) || !projectiles.TryGetValue(fireProjectileInfo.projectilePrefab, out Profile p)) return;
            if (p.Skill.ProjectileSpeed != null && !(p.Index == 2 && fireProjectileInfo.speedOverride == 0f)) fireProjectileInfo.speedOverride = p.Skill.LaunchSpeed(fireProjectileInfo.speedOverride);
        }
        private static void AfterProjectileInitialized(ProjectileController projectileController) => ConfigureProjectile(projectileController);
        private static void BeforeProjectileStart(ProjectileController __instance) => ConfigureProjectile(__instance);
        private static Profile ProjectileProfile(ProjectileController pc)
        {
            if (!pc || !pc.owner || !IsRex(pc.owner.GetComponent<CharacterBody>())) return null;
            return projectiles.FirstOrDefault(pair => pc.catalogIndex >= 0 && pair.Key.GetComponent<ProjectileController>() && pair.Key.GetComponent<ProjectileController>().catalogIndex == pc.catalogIndex || pc.gameObject.name == pair.Key.name + "(Clone)").Value;
        }
        private static void ConfigureProjectile(ProjectileController pc)
        {
            if (!pc || pc.GetComponent<Marker>()) return;
            Profile p = ProjectileProfile(pc); if (p == null) return;
            Marker marker = pc.gameObject.AddComponent<Marker>(); marker.Profile = p; marker.Origin = pc.transform.position;
            if (p.Skill.Proc != null) pc.procCoefficient = C.Get(p.Skill.Proc);
            if (p.Index == 0)
            {
                bool final = pc.gameObject.name == p.FinalProjectile.name + "(Clone)" || pc.catalogIndex >= 0 && pc.catalogIndex == p.FinalProjectile.GetComponent<ProjectileController>().catalogIndex;
                bool healing = C.Get(allShots) || final;
                var heal = pc.GetComponent<ProjectileHealOwnerOnDamageInflicted>();
                if (healing && !heal) heal = pc.gameObject.AddComponent<ProjectileHealOwnerOnDamageInflicted>();
                if (heal) heal.fractionOfDamage = healing ? C.Get(injectHeal) / 100f : 0f;
                var weak = pc.GetComponent<ProjectileInflictTimedBuff>();
                if (weak && weak.buffDef == RoR2Content.Buffs.Weak) weak.enabled = false;
                var pd = pc.GetComponent<ProjectileDamage>();
                if (pd)
                {
                    pd.damageType &= ~DamageType.WeakOnHit;
                    if (healing && C.Get(injectWeaken)) pd.damageType |= DamageType.WeakOnHit;
                }
            }
            if (p.Index == 1)
            {
                var zone = pc.GetComponent<ProjectileDotZone>();
                if (zone) { zone.damageCoefficient = zone.overlapProcCoefficient = 1f; zone.resetFrequency = 1f / C.Get(p.Skill.Interval); zone.fireFrequency = Math.Max(zone.fireFrequency, zone.resetFrequency); zone.lifetime = C.Get(p.Skill.Duration) < 0f ? 0f : C.Get(p.Skill.Duration); }
                ScaleRadius(pc.gameObject, C.Get(p.Skill.Radius) / Math.Max(0.001f, p.NativeRadius));
            }
            if (p.Skill.Area != null && pc.GetComponent<ProjectileExplosion>() is ProjectileExplosion explosion)
            { explosion.blastRadius = C.Get(p.Skill.Radius); explosion.falloffModel = Area(C.Get(p.Skill.Area)); }
            if (pc.GetComponent<ProjectileExplosion>() is ProjectileExplosion blast)
            {
                blast.blastProcCoefficient = 1f;
                if (p.Index == 2 || p.Index == 6 || p.Index == 7) blast.blastDamageCoefficient = 1f;
                if (p.Index == 7) blast.blastDamageCoefficient = blast.childrenDamageCoefficient = 1f;
            }
            if (p.Limit != null && NetworkServer.active)
            {
                List<Marker> list = areas.GetOrCreateValue(pc.owner); list.RemoveAll(m => !m); list.Add(marker);
                int limit = C.Get(p.Limit);
                if (limit >= 0)
                {
                    Marker[] same = list.Where(m => m && m.Profile == p).ToArray();
                    for (int i = 0; i < same.Length - limit; i++) same[i].gameObject.AddComponent<RemoveArea>();
                }
            }
        }
        private sealed class RemoveArea : MonoBehaviour { private void Start() { if (NetworkServer.active) NetworkServer.Destroy(gameObject); } }
        private static void ScaleRadius(GameObject obj, float scale)
        { HitBoxGroup group = obj.GetComponent<HitBoxGroup>(); if (group) foreach (HitBox box in group.hitBoxes) if (box) box.transform.localScale *= scale; }
        private static float RadiusOf(GameObject obj)
        {
            HitBoxGroup group = obj.GetComponent<HitBoxGroup>(); if (!group || group.hitBoxes.Length == 0) throw new MissingMemberException("REX area hitbox unavailable.");
            HitBox box = group.hitBoxes[0]; SphereCollider sphere = box.GetComponent<SphereCollider>(); if (sphere) return sphere.radius * box.transform.lossyScale.x;
            BoxCollider cube = box.GetComponent<BoxCollider>(); if (cube) return cube.size.x * box.transform.lossyScale.x * 0.5f;
            CapsuleCollider capsule = box.GetComponent<CapsuleCollider>(); return capsule ? capsule.radius * box.transform.lossyScale.x : box.transform.lossyScale.x * 0.5f;
        }
        private static BlastAttack.FalloffModel Area(S.AreaFalloff value) => value == S.AreaFalloff.Linear ? BlastAttack.FalloffModel.Linear : value == S.AreaFalloff.HalfLinear ? BlastAttack.FalloffModel.HalfLinear : value == S.AreaFalloff.QuarterLinear ? BlastAttack.FalloffModel.QuarterLinear : value == S.AreaFalloff.SweetSpot ? BlastAttack.FalloffModel.SweetSpot : BlastAttack.FalloffModel.None;
        private static bool BeforeBrambleHeal(EntityState __instance, CharacterBody body)
        {
            CharacterBody owner = Body(__instance); if (!profiles.ContainsKey(4) || !IsRex(owner)) return true;
            // Retain the native stun, impact effect and sound; replace just its heal value.
            brambleTarget = body; return true;
        }
        [ThreadStatic] private static CharacterBody brambleTarget;
        private static void BeforeHealOrb(Orb orb)
        {
            if (orb is HealOrb heal && activeState != null && ForState(activeState)?.Index == 4 && brambleTarget)
            { heal.healValue = brambleTarget.healthComponent.fullHealth * C.Get(brambleHeal) / 100f; brambleTarget = null; }
        }
        private static void BeforeFruitPickup(HealthPickup __instance)
        {
            if (__instance.baseObject && __instance.baseObject.name.StartsWith("TreebotFruitPack", StringComparison.Ordinal)) __instance.fractionalHealing = C.Get(fruitHeal) / 100f;
        }
        private static bool BeforeProjectileBuff(ProjectileInflictTimedBuff __instance)
        {
            Marker marker = __instance.GetComponent<Marker>();
            return !(marker && marker.Profile.Index == 0 && __instance.buffDef == RoR2Content.Buffs.Weak);
        }
        private static void InstallFlowers()
        {
            Patch(typeof(GenericSkill), "ManagedFixedUpdate", nameof(BeforeUtilityUpdate));
            Patch(typeof(SkillDef), "OnExecute", postfix: nameof(AfterContextTeleport));
            Patch(typeof(SkillDef), "CanExecute", postfix: nameof(AfterContextReady));
            Patch(typeof(EntityState).Assembly.GetType("EntityStates.ProliferationFlowerTurret.FireShot"), "OnEnter", nameof(BeforeFlowerShot), finalizer: nameof(EndScope));
            Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", postfix: nameof(AfterFlowerLimit));
            Patch(typeof(CharacterMaster), "IsDeployableLimited", postfix: nameof(AfterLimited));
            Patch(typeof(CharacterMaster), "AddDeployable", postfix: nameof(AfterDeployable));
            Patch(flowerSpawnerType, flowerSpawnMethod, nameof(BeforeFlowerSpawn), finalizer: nameof(AfterFlowerSpawn));
            Type tracker = Native("TurretTracking");
            Patch(tracker, "SearchForTarget", nameof(BeforeFlowerSearch));
            Type def = Native("Skills.TreebotProliferateSkillDef");
            Patch(def, "CanExecute", postfix: nameof(AfterTeleportReady));
            Patch(def, "IsReady", postfix: nameof(AfterTeleportReady));
            Patch(def, "OnExecute", nameof(BeforeTeleportExecute), nameof(AfterTeleportExecute));
            Patch(State("ProliferationTeleport"), "DestroyTurretServer", nameof(BeforeTeleportDamage));
            Patch(State("ProliferationTeleport"), "HandleHealingServer", nameof(BeforeTeleportHeal));
            Patch(typeof(BulletAttack), "Fire", nameof(BeforeFlowerBullet));
        }
        private static void AfterFlowerLimit(CharacterMaster __instance, DeployableSlot slot, ref int __result)
        {
            CharacterBody body = __instance.GetBody(); if (slot != flowerSlot || !IsRex(body) || !body.skillLocator.utility) return;
            GenericSkill utility = body.skillLocator.utility;
            SkillDef original = utility.skillDef == teleportDefinition && flowerContexts.TryGetValue(utility, out FlowerContext context) ? context.Original : utility.skillDef;
            if (!original || !definitions.TryGetValue(original, out Profile p) || p.Index != 5) return;
            int stocks = utility.skillDef == teleportDefinition ? original.GetMaxStock(utility) + (original.dontAllowPastMaxStocks ? 0 : utility.bonusStockFromBody) : utility.maxStock;
            __result = Math.Min(C.Get(maxFlowers), C.Get(baseFlowers) + Math.Max(0, stocks - 1));
        }
        private static void AfterLimited(CharacterMaster __instance, DeployableSlot slot, ref bool __result)
        { if (spawningFlower == __instance && slot == flowerSlot && __instance.GetDeployableSameSlotLimit(slot) > 0) __result = false; }
        private static void BeforeFlowerSpawn(Component __instance, out CharacterMaster __state)
        {
            __state = spawningFlower; ProjectileController pc = __instance.GetComponent<ProjectileController>() ?? __instance.GetComponentInParent<ProjectileController>();
            if (pc && pc.GetComponent<Marker>()?.Profile.Index == 5) spawningFlower = pc.owner.GetComponent<CharacterBody>().master;
        }
        private static Exception AfterFlowerSpawn(CharacterMaster __state, Exception __exception) { spawningFlower = __state; return __exception; }
        private static void AfterDeployable(CharacterMaster __instance, Deployable deployable, DeployableSlot slot)
        {
            if (!NetworkServer.active || slot != flowerSlot || !IsRex(__instance.GetBody())) return;
            // Modern flower spawning adds the deployable during MasterSummon's
            // pre-spawn callback, before the flower has a body instance.
            CharacterMaster flower = deployable.GetComponent<CharacterMaster>();
            if (!flower || !flower.bodyPrefab || !IsFlower(flower.bodyPrefab.GetComponent<CharacterBody>())) return;
            if (C.Get(inheritItems) && __instance.inventory) flower.inventory.CopyItemsFrom(__instance.inventory);
            if (deployable.onUndeploy == null) deployable.onUndeploy = new UnityEngine.Events.UnityEvent();
            deployable.onUndeploy.AddListener(() => { if (flower && flower.GetBody() && flower.GetBody().healthComponent.alive) flower.TrueKill(); });
            CharacterBody body = flower.GetBody();
            if (body) { ApplyBody(body); body.MarkAllStatsDirty(); }
            foreach (var driver in flower.GetComponents<RoR2.CharacterAI.AISkillDriver>()) if (driver.skillSlot != SkillSlot.None) driver.maxDistance = C.Get(flowerRange);
        }
        private static void BeforeFlowerShot(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            if (IsFlower(Body(__instance))) Set(__state, __instance, "damageCoefficient", C.Get(profiles[5].Skill.Damage) / 100f);
        }
        private static void BeforeFlowerBullet(BulletAttack __instance)
        {
            if (!__instance.owner || !IsFlower(__instance.owner.GetComponent<CharacterBody>())) return;
            __instance.maxDistance = C.Get(flowerRange); __instance.procCoefficient = C.Get(profiles[5].Skill.Proc);
            var falloff = C.Get(profiles[5].Skill.Falloff);
            __instance.falloffModel = falloff == S.BulletFalloff.Buckshot ? BulletAttack.FalloffModel.Buckshot : falloff == S.BulletFalloff.Default ? BulletAttack.FalloffModel.DefaultBullet : BulletAttack.FalloffModel.None;
        }
        private static void BeforeUtilityUpdate(GenericSkill __instance, float deltaTime)
        {
            CharacterBody body = __instance.characterBody;
            if (!teleportDefinition || !IsRex(body) || !body.skillLocator || body.skillLocator.utility != __instance) return;
            FlowerContext context = flowerContexts.GetOrCreateValue(__instance);
            bool plant = definitions.TryGetValue(__instance.skillDef, out Profile p) && p.Index == 5;
            if (plant) context.Original = __instance.skillDef;
            bool target = (plant || context.Active) && HasFlowerTarget(__instance);
            if (target && !context.Active)
            {
                __instance.SetSkillOverride(context, teleportDefinition, GenericSkill.SkillOverridePriority.Contextual);
                context.Active = true;
                if (__instance.GetComponent(Native("TurretTracking")) is Behaviour tracker) tracker.enabled = true;
            }
            else if (!target && context.Active)
            {
                bool restock = context.Original.fullRestockOnAssign;
                try { context.Original.fullRestockOnAssign = false; __instance.UnsetSkillOverride(context, teleportDefinition, GenericSkill.SkillOverridePriority.Contextual); }
                finally { context.Original.fullRestockOnAssign = restock; }
                context.Active = false;
            }
            if (context.Active && __instance.skillDef == teleportDefinition)
            {
                if (!profiles[5].Skill.CooldownAfterDuration || !body.GetComponents<EntityStateMachine>().Any(m => m.state?.GetType() == State("Proliferation")))
                    __instance.RechargeBaseSkill(deltaTime);
                float remaining = Math.Max(0f, teleports.GetOrCreateValue(body).ReadyAt - Time.fixedTime);
                __instance.stock = remaining > 0f ? 0 : 1;
                __instance.rechargeStopwatch = remaining > 0f ? Math.Max(0f, __instance.CalculateFinalRechargeInterval() - remaining) : 0f;
            }
        }
        private static void AfterContextReady(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        { if (__instance == teleportDefinition) __result &= HasFlowerTarget(skillSlot) && Time.fixedTime >= teleports.GetOrCreateValue(skillSlot.characterBody).ReadyAt; }
        private static void AfterContextTeleport(SkillDef __instance, GenericSkill skillSlot)
        { if (__instance == teleportDefinition) teleports.GetOrCreateValue(skillSlot.characterBody).ReadyAt = Time.fixedTime + skillSlot.CalculateFinalRechargeInterval(); }
        private static bool BeforeFlowerSearch(Component __instance, Ray aimRay)
        {
            CharacterBody body = __instance.GetComponent<CharacterBody>(); if (!IsRex(body)) return true;
            var search = new BullseyeSearch { searchOrigin = aimRay.origin, searchDirection = aimRay.direction, maxDistanceFilter = C.Get(teleportRange), maxAngleFilter = S.Value(__instance, "maxTrackingAngle", 20f), sortMode = BullseyeSearch.SortMode.Distance, filterByLoS = true };
            TeamMask mask = TeamMask.none; mask.AddTeam(body.teamComponent.teamIndex); search.teamMaskFilter = mask;
            search.RefreshCandidates(); search.FilterOutGameObject(body.gameObject);
            HurtBox target = search.GetResults().FirstOrDefault(h => h && h.healthComponent && h.healthComponent.alive && IsFlower(h.healthComponent.body) &&
                (C.Get(alliedFlowers) || h.healthComponent.body.master.GetComponent<Deployable>()?.ownerMaster == body.master));
            AccessTools.Field(__instance.GetType(), "_trackedTarget").SetValue(__instance, target);
            return false;
        }
        private static bool HasFlowerTarget(GenericSkill slot) => slot && slot.GetComponent(Native("TurretTracking")) is Component tracker && S.Read(tracker, "_trackedTarget") is HurtBox target && target && target.healthComponent.alive;
        private static void AfterTeleportReady(SkillDef __instance, GenericSkill skillSlot, ref bool __result)
        {
            if (definitions.TryGetValue(__instance, out Profile p) && p.Index == 5 && IsRex(skillSlot.characterBody) && HasFlowerTarget(skillSlot))
                __result &= Time.fixedTime >= teleports.GetOrCreateValue(skillSlot.characterBody).ReadyAt;
        }
        private static bool BeforeTeleportExecute(SkillDef __instance, GenericSkill skillSlot, out bool __state)
        {
            __state = definitions.TryGetValue(__instance, out Profile p) && p.Index == 5 && IsRex(skillSlot.characterBody) && HasFlowerTarget(skillSlot);
            if (__state && Time.fixedTime < teleports.GetOrCreateValue(skillSlot.characterBody).ReadyAt) { __state = false; return false; }
            return true;
        }
        private static void AfterTeleportExecute(GenericSkill skillSlot, bool __state)
        { if (__state) teleports.GetOrCreateValue(skillSlot.characterBody).ReadyAt = Time.fixedTime + C.Get(teleportCooldown); }
        private static bool BeforeTeleportDamage(EntityState __instance)
        {
            if (!IsRex(Body(__instance))) return true;
            CharacterBody target = S.Read(__instance, "trackedTarget") as CharacterBody;
            if (target && target.healthComponent.alive && !target.currentVehicle)
            {
                if (C.Get(teleportDamage) >= 100f) return true;
                DamageInfo damage = new DamageInfo { damage = target.healthComponent.fullHealth * C.Get(teleportDamage) / 100f, damageType = DamageType.BypassArmor, procCoefficient = 0f, position = target.corePosition };
                target.healthComponent.TakeDamage(damage);
            }
            return false;
        }
        private static bool BeforeTeleportHeal(EntityState __instance)
        {
            CharacterBody body = Body(__instance); if (!IsRex(body)) return true;
            float heal = C.Get(teleportHeal);
            if (heal >= 0f) { S.Write(__instance, "percentToHeal", heal / 100f); return true; }
            CharacterBody previous = payingBody;
            try
            {
                payingBody = body;
                body.healthComponent.TakeDamage(new DamageInfo { damage = body.healthComponent.fullHealth * -heal / 100f, damageType = DamageType.NonLethal, procCoefficient = 0f, position = body.corePosition });
            }
            finally { payingBody = previous; }
            return false;
        }
        private static void BeforeGrowth(EntityState __instance, out Scope __state)
        {
            __state = new Scope { Previous = activeState };
            ProjectileController pc = __instance.outer ? __instance.outer.GetComponent<ProjectileController>() : null;
            if (!pc || !pc.GetComponent<Marker>() || pc.GetComponent<Marker>().Profile.Index != 7) return;
            S.Skill skill = profiles[7].Skill;
            float duration = C.Get(skill.Duration), interval = C.Get(skill.Interval);
            Set(__state, __instance, "duration", duration < 0f ? float.MaxValue : Math.Max(0.001f, duration));
            Set(__state, __instance, "radius", C.Get(skill.Radius));
            Set(__state, __instance, "healPulseCount", (duration < 0f ? float.MaxValue : Math.Max(0.001f, duration)) / interval);
        }
        private static float GrowthHealInterval(float original, EntityState state) => growths.TryGetValue(state, out _) ? C.Get(profiles[7].Skill.Interval) : original;
        private static float GrowthRootInterval(float original, EntityState state) => growths.TryGetValue(state, out _) ? profiles[7].NativeRootInterval : original;
        private static IEnumerable<CodeInstruction> GrowthIntervalsIL(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();
            for (int i = 0; i < codes.Count; i++)
            {
                yield return codes[i];
                if (i > 0 && codes[i].opcode == OpCodes.Div && codes[i - 1].opcode == OpCodes.Ldsfld && codes[i - 1].operand is FieldInfo field && (field.Name == "healPulseCount" || field.Name == "rootPulseCount"))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(REX), field.Name == "healPulseCount" ? nameof(GrowthHealInterval) : nameof(GrowthRootInterval)));
                }
            }
        }
        private static void AfterGrowth(EntityState __instance)
        {
            ProjectileController pc = __instance.outer ? __instance.outer.GetComponent<ProjectileController>() : null;
            if (!pc || !pc.GetComponent<Marker>() || pc.GetComponent<Marker>().Profile.Index != 7) return;
            growths.Remove(__instance); growths.Add(__instance, new GrowthData { Owner = pc.owner.GetComponent<CharacterBody>() });
            activeGrowths.RemoveAll(w => !w.TryGetTarget(out EntityState state) || state.outer == null);
            activeGrowths.Add(new WeakReference<EntityState>(__instance));
        }
        private static void AfterRoot(EntityState __instance)
        {
            if (!growths.TryGetValue(__instance, out GrowthData data)) return;
            if (S.Read(__instance, "rootedBodies") is IEnumerable<CharacterBody> bodies)
                foreach (CharacterBody body in bodies) if (body && !data.Targets.ContainsKey(body)) data.Targets[body] = body.healthComponent.fullHealth;
        }
        private static bool BeforeGrowthHeal(EntityState __instance)
        {
            if (!growths.TryGetValue(__instance, out GrowthData data)) return true;
            AfterRoot(__instance);
            if (NetworkServer.active && data.Owner && data.Owner.healthComponent.alive)
            {
                float sum = data.Targets.Where(pair => C.Get(healDead) || pair.Key && pair.Key.healthComponent.alive).Sum(pair => pair.Value);
                Heal(data.Owner, __instance.outer.transform.position, sum * C.Get(growthHeal) / 100f * C.Get(profiles[7].Skill.Interval));
            }
            return false;
        }
        private static void OnDeath([HarmonyArgument(0)] DamageReport report)
        {
            if (!NetworkServer.active || !profiles.ContainsKey(7) || !report.victimBody) return;
            for (int i = activeGrowths.Count - 1; i >= 0; i--)
            {
                if (!activeGrowths[i].TryGetTarget(out EntityState state) || !state.outer || state.outer.state != state || !growths.TryGetValue(state, out GrowthData data)) { activeGrowths.RemoveAt(i); continue; }
                AfterRoot(state);
                if (data.Owner && data.Targets.TryGetValue(report.victimBody, out float maximum) && data.Paid.Add(report.victimBody)) Heal(data.Owner, report.victimBody.corePosition, maximum * C.Get(growthKill) / 100f);
            }
        }
        private static void Heal(CharacterBody owner, Vector3 origin, float amount)
        { if (owner && owner.healthComponent.alive && amount > 0f) OrbManager.instance.AddOrb(new HealOrb { origin = origin, target = owner.mainHurtBox, healValue = amount, overrideDuration = 0.3f }); }
        private sealed class Payment { internal int Cost; }
        private static void BeforePayment(SkillDef __instance, GenericSkill skillSlot, out Payment __state)
        { __state = null; if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.ConsumeAll) { __state = new Payment { Cost = __instance.stockToConsume }; __instance.stockToConsume = skillSlot.stock; } }
        private static Exception AfterPayment(SkillDef __instance, Payment __state, Exception __exception) { if (__state != null) __instance.stockToConsume = __state.Cost; return __exception; }
        private static void AfterRequired(SkillDef __instance, GenericSkill skillSlot, ref bool __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RequireAll && skillSlot.stock < skillSlot.maxStock) __result = false; }
        private static void AfterRecharge(SkillDef __instance, GenericSkill skillSlot, ref int __result) { if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.RechargeAll) __result = skillSlot.maxStock; }
        private static void BeforeSkillUpdate(SkillDef __instance, GenericSkill skillSlot, ref float deltaTime)
        {
            if (definitions.TryGetValue(__instance, out Profile p) && p.Skill.CooldownAfterDuration && skillSlot.characterBody &&
                (skillSlot.characterBody.GetComponents<EntityStateMachine>().Any(m => ForState(m.state) == p) || areas.TryGetValue(skillSlot.characterBody.gameObject, out List<Marker> list) && list.Any(m => m && m.Profile == p))) deltaTime = 0f;
        }
        private static string ToxinsDescription() => "Damage dealt has a <style=cIsDamage>" + C.Get(toxinsChance) + "%</style> chance to <style=cIsUtility>Weaken</style> enemies for <style=cIsUtility>" + S.Number(weakenDuration == null ? 3f : C.Get(weakenDuration)) + " seconds</style>.";
        private static string PlatingDescription() => "Gain <style=cIsHealing>" + C.Get(platingArmour) + " armour</style> and <style=cIsDamage>" + C.Get(platingDamage) + "% damage</style> for every <style=cIsHealth>" + C.Get(missingInterval) + "% missing health</style>.";
        private static string TeleportDescription() => "Teleport to a flower, restoring <style=cIsHealing>" + S.Number(C.Get(teleportHeal)) + "% health</style> and costing the flower <style=cIsHealth>" + S.Number(C.Get(teleportDamage)) + "% health</style>.";
        private static string Description(Profile p)
        {
            S.Skill s = p.Skill; string damage = s.Damage == null ? "" : S.DamageText(C.Get(s.Damage), s.Interval == null || p.Index != 1 ? 0f : C.Get(s.Interval), p.Index == 1);
            string cost = p.Cost == null ? "" : "<style=cIsHealth>" + S.Number(C.Get(p.Cost)) + "% HP.</style> ";
            string text = p.Index == 0 ? "Fire " + C.Get(burst) + " syringes for " + damage + " each. " + (C.Get(allShots) ? "Each syringe" : "The final syringe") + " heals for <style=cIsHealing>" + S.Number(C.Get(injectHeal)) + "% of damage dealt</style>" + (C.Get(injectWeaken) ? " and <style=cIsUtility>Weakens</style> enemies" : "") + "." :
                p.Index == 1 ? "Launch a series of seed bullets, dealing " + damage + " for <style=cIsUtility>" + Duration(s) + "</style>." :
                p.Index == 2 ? "Launch a mortar into the sky, dealing " + damage + "." :
                p.Index == 3 ? "Fire a sonic boom that pushes and <style=cIsUtility>Weakens</style> enemies, pushing you backwards if airborne." :
                p.Index == 4 ? "Fire a sonic boom that damages enemies for " + damage + ". Heal for <style=cIsHealing>" + S.Number(C.Get(brambleHeal)) + "% of each enemy's maximum health</style>." :
                p.Index == 5 ? "Plant an offshoot that " + (C.Get(inheritItems) ? "<style=cIsUtility>inherits all your items</style> and " : "") + "attacks nearby enemies for " + damage + ". Can place up to <style=cIsUtility>" + C.Get(baseFlowers) + "</style>. Aim at a flower to <style=cIsUtility>teleport</style> to it." :
                p.Index == 6 ? "Fire a seed for " + damage + " that marks an enemy. Killing the marked enemy drops healing fruits that restore <style=cIsHealing>" + S.Number(C.Get(fruitHeal)) + "% health</style> each." :
                "Fire a flower that <style=cIsUtility>roots</style> enemies, dealing " + damage + " on impact and again when pulling them in. Heal for <style=cIsHealing>" + S.Number(C.Get(growthHeal)) + "% of each target's maximum health per second</style>" + (C.Get(growthKill) > 0f ? ", plus <style=cIsHealing>" + S.Number(C.Get(growthKill)) + "%</style> when a rooted enemy dies" : "") + ".";
            return s.AgileText + cost + text;
        }
        private static string Duration(S.Skill skill) => C.Get(skill.Duration) < 0f ? "as long as it remains active" : S.Number(C.Get(skill.Duration)) + " seconds";
        private static string Extra(Profile p)
        {
            var text = new StringBuilder(); Action<string, string> add = (label, value) => text.Append("\n").Append(label).Append(": <style=cIsUtility>").Append(value).Append("</style>");
            if (p.Skill.Radius != null) add("Radius", S.Number(C.Get(p.Skill.Radius)) + "m");
            if (p.Skill.ProjectileSpeed != null) add("Projectile Speed", S.Number(C.Get(p.Skill.ProjectileSpeed)) + "m/s");
            if (p.Index == 5) { add("Flower Range", S.Number(C.Get(flowerRange)) + "m"); add("Teleport Range", S.Number(C.Get(teleportRange)) + "m"); add("Teleport Cooldown", S.Number(C.Get(teleportCooldown)) + "s"); add("Maximum Flowers", C.Get(maxFlowers).ToString()); }
            if (p.Index == 7) { add("Duration", Duration(p.Skill)); add("Healing Interval", S.Number(C.Get(p.Skill.Interval)) + "s"); }
            return text.ToString();
        }
        // A separate sprite keeps the original skill artwork untouched.
        private static Sprite addedSkillIcon;
        private static Sprite AdaptivePlatingIcon(Sprite original)
        {
            if (addedSkillIcon) return addedSkillIcon;
            if (!original) return original;
            try { return addedSkillIcon = BuildAdaptivePlatingIcon(original); }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("3QSAdaptivePlatingIcon: retaining original icon because conversion failed: " + e.Message);
                return original;
            }
        }

        static Sprite BuildAdaptivePlatingIcon(Sprite original)
        {
            if (original.packed && original.packingRotation != SpritePackingRotation.None)
                throw new InvalidOperationException("Unsupported rotated sprite atlas entry.");
            const string name = "3QSAdaptivePlatingIcon";
            // GPU readback also works for game textures imported without Read/Write.
            Rect region = original.textureRect;
            Vector2 offset = original.textureRectOffset;
            int width = Mathf.RoundToInt(original.rect.width);
            int height = Mathf.RoundToInt(original.rect.height);
            int rw = Mathf.RoundToInt(region.width), rh = Mathf.RoundToInt(region.height);
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            Texture2D readable = null, texture = null;
            Sprite sprite = null;
            try
            {
                temporary = RenderTexture.GetTemporary(original.texture.width, original.texture.height,
                    0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(original.texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(rw, rh, TextureFormat.RGBA32, false, false);
                readable.ReadPixels(region, 0, 0, false);
                readable.Apply(false, false);
                Color[] source = readable.GetPixels();
                Color[] pixels = new Color[width * height];
                int ox = Mathf.RoundToInt(offset.x), oy = Mathf.RoundToInt(offset.y);
                for (int y = 0; y < rh; y++)
                for (int x = 0; x < rw; x++)
                {
                    Color pixel = source[y * rw + x];
                    Color.RGBToHSV(pixel, out float hue, out float saturation, out float value);
                    if (pixel.a > 0 && saturation > .2f && value > .12f)
                    {
                        if (hue >= .12f && hue <= .40f)
                        {
                            hue = .23f;
                            value = Mathf.Min(1f, value * 1.12f);
                        }
                        Color changed = Color.HSVToRGB(hue, saturation, value);
                        changed.a = pixel.a;
                        pixel = changed;
                    }
                    int dx = x + ox, dy = y + oy;
                    if (dx < 0 || dx >= width || dy < 0 || dy >= height) continue;
                    dy = height - 1 - dy;
                    pixels[dy * width + dx] = pixel;
                }
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                texture.name = name;
                texture.filterMode = original.texture.filterMode;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.SetPixels(pixels);
                texture.Apply(false, true);
                Vector2 pivot = new Vector2(original.pivot.x / width, original.pivot.y / height);
                pivot.y = 1f - pivot.y;
                sprite = Sprite.Create(texture, new Rect(0, 0, width, height), pivot,
                    original.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                return sprite;
            }
            catch
            {
                if (sprite) UnityEngine.Object.Destroy(sprite);
                if (texture) UnityEngine.Object.Destroy(texture);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary) RenderTexture.ReleaseTemporary(temporary);
                if (readable) UnityEngine.Object.Destroy(readable);
            }
        }
    }
}
