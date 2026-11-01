using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class Warbanner
    {
        static ConfigEntry<bool> enabled, inherit, holdouts, bosses, onLevel;
        static ConfigEntry<float> damage, regen, armour, crit, move, attack, duration, durationStack, radius, radiusStack, wardLife, interval;
        static Dictionary<string, ConfigEntry<float>> stats = new Dictionary<string, ConfigEntry<float>>();
        static ConfigEntry<int> jumps, stocks, maximumBanners;
        sealed class BannerList { internal readonly List<GameObject> Banners = new List<GameObject>(); }
        static readonly ConditionalWeakTable<CharacterMaster, BannerList> ownedBanners = new ConditionalWeakTable<CharacterMaster, BannerList>();
        [ThreadStatic] static CharacterMaster spawnOwner;
        static GameObject prefab;
        sealed class WardSettings : MonoBehaviour
        {
            internal int Stacks = 1;
        }

        sealed class ZoneState
        {
            internal bool Raised;
        }

        static ConditionalWeakTable<HoldoutZoneController, ZoneState> zones = new ConditionalWeakTable<HoldoutZoneController, ZoneState>();
        static Stage bossStage;
        static HashSet<string> bossesSeen = new HashSet<string>();
        internal static bool Active => S.Active(enabled);

        internal static void Init()
        {
            var s = S.Section("Warbanner");
            enabled = S.Enable(s);
            damage = S.Num(s, "Damage Buff (%)", 10, "Damage increase while buffed. Vanilla: 0%.");
            regen = S.Num(s, "Health Regen Buff", 3, "Flat health regeneration per second while buffed. Vanilla: 0.");
            armour = S.Num(s, "Armour Buff", 10, "Flat armour while buffed. Vanilla: 0.");
            crit = S.Num(s, "Crit Buff (%)", 10, "Critical-strike chance in percentage points. Vanilla: 0%.");
            move = S.Num(s, "Movement Speed Buff (%)", 30, "Movement-speed bonus. Vanilla: 30%.");
            attack = S.Num(s, "Attack Speed Buff (%)", 30, "Attack-speed bonus. Vanilla: 30%.");
            duration = S.Num(s, "Buff Duration", 2, "Buff duration refreshed while inside, allowing it to linger after leaving.");
            durationStack = S.Num(s, "Buff Duration Per Stack", 0, "Extra linger duration per additional source stack.");
            radius = S.Num(s, "Radius", 16, "First-stack banner radius in metres. Vanilla: 16.");
            radiusStack = S.Num(s, "Radius Per Stack", 8, "Extra radius per additional stack. Vanilla: 8.");
            wardLife = S.Num(s, "Banner Lifetime", -1, "Seconds before the banner disappears. -1 lasts for the stage.", -1);
            maximumBanners = S.Int(s, "Maximum Banners Per Player", 3, "Maximum active banners per player, shared with their inheriting minions. Placing another removes the oldest. 0 disables the limit. Vanilla: unlimited.");
            interval = S.Num(s, "Buff Refresh Interval", .2f, "Seconds between buff refreshes.", .02f);
            inherit = C.BindBool(s, "Turrets Can Inherit", true, "Remove the turret blacklist so Engineer turrets and other inheriting minions, including compatible proliferation offshoots, can hold Warbanner.");
            holdouts = C.BindBool(s, "Allow All Holdout Zones", true, "Raise banners when any enabled holdout begins, including modded zones built on HoldoutZoneController and enabled Captain beacon zones.");
            bosses = C.BindBool(s, "Place On End Boss Spawn", true, "Raise banners when a final-boss encounter first spawns its boss. Repeated phases do not duplicate this trigger.");
            onLevel = C.BindBool(s, "Place On Level Up", true, "Raise a banner on level up. Vanilla: true.");
            foreach (var key in new[]
            {
                "Maximum Health Buff (%)",
                "Maximum Shield Buff (%)",
                "Flat Health Buff",
                "Flat Shield Buff",
                "Flat Damage Buff",
                "Flat Movement Speed Buff",
                "Flat Attack Speed Buff",
                "Regeneration Buff (%)",
                "Critical Damage Buff (%)",
                "Jump Power Buff (%)",
                "Acceleration Buff (%)",
                "Vision Range Buff (%)",
                "Sprint Speed Buff (%)",
                "Skill Cooldown Reduction (%)",
                "Flat Skill Cooldown Reduction"
            }

            )
                stats[key] = S.Num(s, key, 0, "Additional stat bonus while the banner buff is active. Vanilla: 0.", 0, key == "Skill Cooldown Reduction (%)" ? 99.9f : 100000f);
            jumps = S.Int(s, "Extra Jumps", 0, "Additional jumps while buffed. Vanilla: 0.");
            stocks = S.Int(s, "Extra Skill Stocks", 0, "Additional maximum stocks for skills while buffed. Vanilla: 0.");
            if (!enabled.Value)
                return;
            S.Ensure();
            S.Stats += Stats;
            var h = S.Harmony("Warbanner");
            // Hook whichever inventory-copy API this game version exposes.
            foreach (var method in typeof(Inventory).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var parameters = method.GetParameters();
                if (method.Name == "CloneInventory" && parameters.Length == 1 ||
                    method.Name == "CopyItemsFrom" && parameters.Length == 2 && parameters[1].ParameterType == typeof(Func<ItemIndex, bool>))
                    h.Patch(method, prefix: new HarmonyMethod(typeof(Warbanner), nameof(BeforeClone)));
            }
            S.Patch(h, typeof(CharacterBody), "RecalculateStats", typeof(Warbanner), transpiler: nameof(StatsIL));
            S.Patch(h, typeof(BuffWard), "FixedUpdate", typeof(Warbanner), prefix: nameof(WardUpdate));
            var zoneUpdate = A.Method(typeof(HoldoutZoneController), "DoUpdate")
                ?? A.Method(typeof(HoldoutZoneController), "FixedUpdate")
                ?? A.Method(typeof(HoldoutZoneController), "Update");
            if (zoneUpdate != null)
                h.Patch(zoneUpdate, prefix: new HarmonyMethod(typeof(Warbanner), nameof(ZoneUpdate)));
            var level = typeof(Run).Assembly.GetType("RoR2.Items.WardOnLevelManager");
            if (level != null)
                S.Patch(h, level, "OnCharacterLevelUp", typeof(Warbanner), prefix: nameof(BeforeLevel), transpiler: nameof(TrackSpawns));
            var charging = typeof(TeleporterInteraction).GetNestedType("ChargingState", BindingFlags.Public | BindingFlags.NonPublic);
            if (charging != null)
                S.Patch(h, charging, "OnEnter", typeof(Warbanner), transpiler: nameof(TrackSpawns));
            // Captain's controller type is not present in every game/reference version.
            foreach (string name in new[] { "RoR2.SupportBeaconController", "RoR2.CaptainSupplyDropController" })
            {
                var controller = A.TypeByName(name);
                var activate = controller == null ? null : A.Method(controller, "ActivateTeleporterItemsServer");
                if (activate != null)
                    h.Patch(activate, transpiler: new HarmonyMethod(typeof(Warbanner), nameof(TrackSpawns)));
            }
            CharacterBody.onBodyStartGlobal += BossStarted;
            RoR2Application.onLoad += () =>
            {
                prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/WarbannerWard");
                S.BuffTexts[RoR2Content.Buffs.Warbanner.name] = new S.BuffText
                {
                    Title = "Warbanner",
                    Text = icon => Description()
                };
            };
            S.Text("ITEM_WARDONLEVEL_DESC", Description);
        }

        static float Value(string key) => C.Get(stats[key]);
        static void BeforeClone(Inventory __instance, object[] __args)
        {
            if (!Active || __args == null || __args.Length == 0)
                return;
            var master = __instance.GetComponent<CharacterMaster>();
            string name = master && master.bodyPrefab ? master.bodyPrefab.name : __instance.gameObject.name;
            if (!(name.Contains("EngiTurret") || name.Contains("EngiWalkerTurret") || name.Contains("FlowerTurret") || name.Contains("TreebotFlower") || name.Contains("TreebotTree") || name.Contains("Proliferation")))
                return;
            if (__args.Length == 2)
            {
                __args[1] = InheritanceFilter(__args[1] as Func<ItemIndex, bool>);
                return;
            }
            // Harmony writes the boxed argument back to the original parameter.
            // No compile-time dependency on Inventory.CloneInventoryArgs is needed.
            object args = __args[0];
            if (args == null) return;
            var field = A.Field(args.GetType(), "canCloneFilter");
            if (field == null || field.FieldType != typeof(Func<ItemIndex, bool>)) return;
            field.SetValue(args, InheritanceFilter(field.GetValue(args) as Func<ItemIndex, bool>));
            __args[0] = args;
        }

        static Func<ItemIndex, bool> InheritanceFilter(Func<ItemIndex, bool> original)
        {
            if (original == null)
                original = A.Field(typeof(Inventory), "defaultItemCopyFilterDelegate")?.GetValue(null) as Func<ItemIndex, bool>;
            var fallback = original;
            return index => index == RoR2Content.Items.WardOnLevel.itemIndex
                ? C.Get(inherit) : fallback == null || fallback(index);
        }

        static bool BeforeLevel() => !Active || C.Get(onLevel);
        static float Move(float native) => Active ? C.Get(move) / 100 : native;
        static float Attack(float native) => Active ? C.Get(attack) / 100 : native;
        static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int n = 0;
            for (int i = 0; i < list.Count; i++)
                if (list[i].operand is FieldInfo f && f.Name == "Warbanner")
                    for (int j = i + 1; j < Math.Min(i + 8, list.Count); j++)
                        if (list[j].LoadsConstant(.3f))
                        {
                            list.Insert(j + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(Warbanner), n++ == 0 ? nameof(Move) : nameof(Attack))));
                            i = j + 1;
                            break;
                        }

            if (n != 2)
                throw new InvalidOperationException("Warbanner native speed anchors changed: " + n);
            return list;
        }

        static int SpawnCount(Inventory inventory, ItemDef item)
        {
            int count = inventory.GetItemCountEffective(item);
            spawnOwner = Active ? inventory.GetComponent<CharacterMaster>() : null;
            return count;
        }

        static int HoldoutSpawnCount(Inventory inventory, ItemDef item)
        {
            int count = SpawnCount(inventory, item);
            return Active && C.Get(holdouts) ? 0 : count;
        }

        static GameObject InstantiateBanner(GameObject original, Vector3 position, Quaternion rotation)
        {
            CharacterMaster owner = spawnOwner;
            var go = UnityEngine.Object.Instantiate(original, position, rotation);
            if (Active && NetworkServer.active && original && original.name == "WarbannerWard")
            {
                spawnOwner = null;
                TrackBanner(owner, go);
            }
            return go;
        }

        static void TrackBanner(CharacterMaster owner, GameObject banner)
        {
            if (!NetworkServer.active || !owner || !banner) return;
            // Turrets and other inheriting allies use their player's pool.
            var seen = new HashSet<CharacterMaster>();
            while (owner.minionOwnership && owner.minionOwnership.ownerMaster && seen.Add(owner))
                owner = owner.minionOwnership.ownerMaster;
            var list = ownedBanners.GetOrCreateValue(owner).Banners;
            list.RemoveAll(go => !go);
            list.Add(banner);
            int maximum = C.Get(maximumBanners);
            while (maximum > 0 && list.Count > maximum)
            {
                GameObject oldest = list[0];
                list.RemoveAt(0);
                if (!oldest) continue;
                // Stop refreshing buffs immediately, then remove it on every client.
                var ward = oldest.GetComponent<BuffWard>();
                if (ward) ward.enabled = false;
                NetworkServer.Destroy(oldest);
            }
        }

        static IEnumerable<CodeInstruction> TrackSpawns(IEnumerable<CodeInstruction> code, MethodBase __originalMethod)
        {
            bool level = __originalMethod.DeclaringType.Name == "WardOnLevelManager";
            bool pending = false;
            foreach (var instruction in code)
            {
                if (instruction.operand is FieldInfo field && field.Name == "WardOnLevel")
                    pending = true;
                if (pending && instruction.operand is MethodInfo count && count.Name == "GetItemCountEffective")
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = A.Method(typeof(Warbanner), level ? nameof(SpawnCount) : nameof(HoldoutSpawnCount));
                    pending = false;
                }
                if (instruction.operand is MethodInfo instantiate && instantiate.DeclaringType == typeof(UnityEngine.Object)
                    && instantiate.Name == "Instantiate" && instantiate.IsGenericMethod
                    && instantiate.GetGenericArguments()[0] == typeof(GameObject))
                {
                    var parameters = instantiate.GetParameters();
                    if (parameters.Length == 3 && parameters[1].ParameterType == typeof(Vector3) && parameters[2].ParameterType == typeof(Quaternion))
                    {
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = A.Method(typeof(Warbanner), nameof(InstantiateBanner));
                    }
                }
                yield return instruction;
            }
        }

        static void WardUpdate(BuffWard __instance)
        {
            if (!Active || __instance.buffDef != RoR2Content.Buffs.Warbanner)
                return;
            var marker = __instance.GetComponent<WardSettings>();
            if (!marker)
            {
                marker = __instance.gameObject.AddComponent<WardSettings>();
                marker.Stacks = Math.Max(1, Mathf.RoundToInt((__instance.radius - 8) / 8));
            }

            __instance.buffDuration = Mathf.Max(C.Get(interval), S.Linear(marker.Stacks, C.Get(duration), C.Get(durationStack)));
            __instance.interval = C.Get(interval);
            if (permanentBuff != null) permanentBuff.SetValue(__instance, false);
            __instance.Networkradius = S.Linear(marker.Stacks, C.Get(radius), C.Get(radiusStack));
            __instance.expires = C.Get(wardLife) >= 0;
            __instance.expireDuration = Mathf.Max(0, C.Get(wardLife));
        }

        static readonly FieldInfo permanentBuff = A.Field(typeof(BuffWard), "permanentBuff");
        static readonly PropertyInfo fullyCharged = A.Property(typeof(HoldoutZoneController), "isFullyCharged");
        static readonly FieldInfo maximumCharge = A.Field(typeof(HoldoutZoneController), "maxCharge");
        static bool FullyCharged(HoldoutZoneController zone)
        {
            if (fullyCharged != null && fullyCharged.PropertyType == typeof(bool))
                return (bool)fullyCharged.GetValue(zone, null);
            // Earlier holdouts always use normalised charge from zero to one.
            float maximum = maximumCharge == null ? 1f : Convert.ToSingle(maximumCharge.GetValue(zone));
            return zone.charge >= maximum;
        }

        static void ZoneUpdate(HoldoutZoneController __instance)
        {
            if (!Active || !C.Get(holdouts) || !NetworkServer.active || !__instance.enabled || FullyCharged(__instance))
                return;
            var state = zones.GetValue(__instance, z => new ZoneState());
            if (state.Raised)
                return;
            state.Raised = true;
            RaiseAll();
        }

        static void BossStarted(CharacterBody b)
        {
            if (!Active || !C.Get(bosses) || !NetworkServer.active || !b || !b.teamComponent || b.teamComponent.teamIndex == TeamIndex.Player)
                return;
            string name = b.gameObject.name;
            string family = name.StartsWith("Brother") ? "Mithrix" : name.StartsWith("VoidRaidCrab") ? "Voidling" : name.StartsWith("FalseSonBoss") ? "FalseSon" : name.StartsWith("Providence") && !name.Contains("Shadow") ? "Providence" : name.StartsWith("SolusWing") ? "SolusWing" : name.StartsWith("ScavLunar") ? "TwistedScavenger" : null;
            if (family == null)
                return;
            if (bossStage != Stage.instance)
            {
                bossStage = Stage.instance;
                bossesSeen.Clear();
            }

            if (bossesSeen.Add(family))
                RaiseAll();
        }

        static void RaiseAll()
        {
            if (!prefab)
                return;
            foreach (var b in CharacterBody.readOnlyInstancesList)
            {
                int n = S.Count(b, RoR2Content.Items.WardOnLevel);
                if (n <= 0 || !b.healthComponent.alive || !b.teamComponent)
                    continue;
                var go = UnityEngine.Object.Instantiate(prefab, b.footPosition, Quaternion.identity);
                go.GetComponent<TeamFilter>().teamIndex = b.teamComponent.teamIndex;
                go.AddComponent<WardSettings>().Stacks = n;
                go.GetComponent<BuffWard>().Networkradius = S.Linear(n, C.Get(radius), C.Get(radiusStack));
                TrackBanner(b.master, go);
                NetworkServer.Spawn(go);
            }
        }

        static void Stats(CharacterBody b)
        {
            if (!Active || !b.HasBuff(RoR2Content.Buffs.Warbanner))
                return;
            S.Set(b, "damage", b.damage * (1 + C.Get(damage) / 100) + Value("Flat Damage Buff"));
            S.Set(b, "armor", b.armor + C.Get(armour));
            S.Set(b, "crit", b.crit + C.Get(crit));
            if (!b.HasBuff(RoR2Content.Buffs.HealingDisabled))
                S.Set(b, "regen", b.regen * (1 + Value("Regeneration Buff (%)") / 100) + C.Get(regen));
            S.Set(b, "maxHealth", b.maxHealth * (1 + Value("Maximum Health Buff (%)") / 100) + Value("Flat Health Buff"));
            S.Set(b, "maxShield", b.maxShield * (1 + Value("Maximum Shield Buff (%)") / 100) + Value("Flat Shield Buff"));
            S.Set(b, "moveSpeed", b.moveSpeed + Value("Flat Movement Speed Buff"));
            S.Set(b, "attackSpeed", b.attackSpeed + Value("Flat Attack Speed Buff"));
            S.Set(b, "jumpPower", b.jumpPower * (1 + Value("Jump Power Buff (%)") / 100));
            S.Set(b, "acceleration", b.acceleration * (1 + Value("Acceleration Buff (%)") / 100));
            S.Set(b, "visionDistance", b.visionDistance * (1 + Value("Vision Range Buff (%)") / 100));
            S.Set(b, "critMultiplier", b.critMultiplier * (1 + Value("Critical Damage Buff (%)") / 100));
            if (b.isSprinting)
                S.Set(b, "moveSpeed", b.moveSpeed * (1 + Value("Sprint Speed Buff (%)") / 100));
            if (C.Get(jumps) != 0)
                A.Property(typeof(CharacterBody), "maxJumpCount").SetValue(b, b.maxJumpCount + C.Get(jumps), null);
            if (b.skillLocator)
                foreach (var skill in b.skillLocator.allSkills)
                {
                    if (!skill)
                        continue;
                    skill.cooldownScale *= 1 - Value("Skill Cooldown Reduction (%)") / 100;
                    skill.flatCooldownReduction += Value("Flat Skill Cooldown Reduction");
                    skill.bonusStockFromBody += C.Get(stocks);
                }
        }

        static string Description() => "On level up" + (C.Get(holdouts) ? ", when a holdout starts" : " or starting a Teleporter event") + (C.Get(bosses) ? ", or when a final boss spawns" : "") + ", raise a banner within <style=cIsUtility>" + C.Get(radius) + "m</style> <style=cStack>(+" + C.Get(radiusStack) + "m per stack)</style>. Grants <style=cIsUtility>" + C.Get(move) + "% movement speed</style>, <style=cIsDamage>" + C.Get(attack) + "% attack speed</style>, <style=cIsDamage>" + C.Get(damage) + "% damage</style>, <style=cIsHealing>" + C.Get(regen) + " health regeneration</style>, <style=cIsHealing>" + C.Get(armour) + " armour</style> and <style=cIsDamage>" + C.Get(crit) + "% critical chance</style>. Buffs linger for <style=cIsUtility>" + C.Get(duration) + " seconds</style> after leaving." + (C.Get(maximumBanners) > 0 ? " Maximum <style=cIsUtility>" + C.Get(maximumBanners) + "</style> banners per player. New banners replace the oldest." : "");
    }
}
