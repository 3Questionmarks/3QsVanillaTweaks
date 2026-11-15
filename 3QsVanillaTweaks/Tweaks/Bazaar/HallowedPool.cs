using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using R2API;
using RoR2;
using RoR2.Hologram;
using Tweaks.Stages;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Bazaar
{
    internal static class HallowedPool
    {
        private const string Section = "Bazaar - 05 Extra Interactables - Hallowed Pool";
        private const string CauldronPath = "RoR2/Base/LunarCauldrons/LunarCauldron, RedToWhite Variant.prefab";
        private const string PoolPath = "RoR2/Base/ShrineCleanse/ShrineCleanse.prefab";
        private const string NameToken = "3QS_HALLOWED_POOL_NAME";
        private const string ContextToken = "3QS_HALLOWED_POOL_CONTEXT";
        private const string TerminalShutterSound = "Play_UI_tripleChestShutter";
        private const string HallowingAcceptSound = "Play_DLC4_Inter_CurseShrine_Accept";
        private const float SinkDuration = .55f;
        private const float EmptyDuration = .4f;
        private const float RiseDuration = .6f;
        private const float DisplayTravel = .85f;
        private static readonly ConditionalWeakTable<Run, BanState> bans = new ConditionalWeakTable<Run, BanState>();
        private static readonly List<PoolWeight> weights = new List<PoolWeight>();
        private static BazaarSpawnHelper.Settings first, second, third;
        private static ConfigEntry<int> hexCost;
        private static GameObject prefab, useEffect;
        private static Stage lastStage;
        private static FieldInfo availablePickupsChanged;
        private static FieldInfo basicSelector;
        private static readonly Type eggController = typeof(Run).Assembly.GetType("RoR2.LemurianEggController");
        private static bool logged;
        [ThreadStatic] private static int fixedSpawnDepth, fixedGrantDepth, rolling;

        private sealed class BanState
        {
            internal readonly HashSet<ItemIndex> Items = new HashSet<ItemIndex>();
            internal readonly HashSet<List<PickupIndex>> Depleted = new HashSet<List<PickupIndex>>();
        }

        private sealed class PoolWeight
        {
            internal Func<Run, List<PickupIndex>> Pool;
            internal ConfigEntry<float> Weight;
        }

        private sealed class PoolOffer : MonoBehaviour
        {
            private ShopTerminalBehavior terminal;
            private PickupDisplay display;
            private Vector3 restPosition, restScale;
            private PickupIndex previousPickup;
            private bool swapping;
            private float animationTime = 1f;

            private void Start()
            {
                terminal = GetComponent<ShopTerminalBehavior>();
                display = terminal.pickupDisplay;
                if (display)
                {
                    restPosition = display.transform.localPosition;
                    restScale = display.transform.localScale;
                }
                previousPickup = terminal.CurrentPickup().pickupIndex;
                var tint = new Color(.16f, .40f, .24f, 1f);
                var emission = new Color(.004f, .025f, .010f, 1f);
                var block = new MaterialPropertyBlock();
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                {
                    if (display && renderer.transform.IsChildOf(display.transform)) continue;
                    renderer.GetPropertyBlock(block);
                    block.SetColor("_Color", tint);
                    block.SetColor("_TintColor", tint);
                    block.SetColor("_EmColor", emission);
                    block.SetColor("_EmissionColor", emission);
                    renderer.SetPropertyBlock(block);
                }
                foreach (Light light in GetComponentsInChildren<Light>(true))
                    if (!display || !light.transform.IsChildOf(display.transform))
                        light.intensity *= .2f;

                foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (display && particles.transform.IsChildOf(display.transform)) continue;
                    var main = particles.main;
                    main.startColor = new Color(.10f, .28f, .15f, .5f);
                }
            }

            private void LateUpdate()
            {
                if (!NetworkClient.active) return;

                // The terminal already syncs its purchase flag and current offer to every client.
                bool isBanishing = terminal.NetworkhasBeenPurchased;
                var pickup = terminal.CurrentPickup().pickupIndex;
                if (isBanishing && !swapping)
                {
                    animationTime = 0f;
                    Util.PlaySound(HallowingAcceptSound, gameObject);
                }
                if (!isBanishing && pickup != PickupIndex.none &&
                    (previousPickup == PickupIndex.none || (swapping && pickup != previousPickup)))
                    animationTime = 0f;
                swapping = isBanishing;
                previousPickup = pickup;

                if (!display || pickup == PickupIndex.none) return;
                float duration = isBanishing ? SinkDuration : RiseDuration;
                float progress = Mathf.SmoothStep(0f, 1f, animationTime / duration);
                float lowered = isBanishing ? progress : 1f - progress;
                display.transform.localPosition = restPosition - Vector3.up * (DisplayTravel * lowered);
                display.transform.localScale = restScale * Mathf.Lerp(1f, .15f, lowered);
                animationTime += Time.deltaTime;
            }
        }

        private sealed class FixedPickup : MonoBehaviour { }

        internal static void Init()
        {
            hexCost = C.Bind(Section, "Hexes Required To Banish", 1,
                new ConfigDescription("Number of Hexes a Hallowed Pool consumes to banish its displayed item.",
                    new AcceptableValueRange<int>(1, 999)));
            first = BazaarSpawnHelper.Bind(Section + " - First", "Hallowed Pool 1", null,
                100f, -134.04f, -25.68f, -28.17f, .57f, 0f, .82f,
                snapToGround: false, alignToGround: false,
                exactPose: "-146.643326, -24.5425, -22.1489, 0, 33.44354, 0");
            second = BazaarSpawnHelper.Bind(Section + " - Second", "Hallowed Pool 2", null,
                50f, -142.77f, -24.82f, -22.68f, .47f, 0f, .88f,
                snapToGround: false, alignToGround: false,
                exactPose: "-143.291489, -25.4653568, -14.9951906, 0, 108.443542, 0");
            C.ReadRetiredValue(Section + " - First", "Align To Wall", true);
            C.ReadRetiredValue(Section + " - Second", "Align To Wall", true);
            third = BazaarSpawnHelper.BindFormerUnlockOnly("Hallowed Pool 3", null,
                -144.67f, -24.26f, -24.72f, .95f, 0f, -.31f,
                exactPose: "-146.1788, -25.0921783, -18.019186, 0, 73.07233, 0");
            Weight("Common", 50f, run => run.availableTier1DropList);
            Weight("Uncommon", 35f, run => run.availableTier2DropList);
            Weight("Legendary", 15f, run => run.availableTier3DropList);
            Weight("Boss", 0f, run => run.availableBossDropList);
            Weight("Lunar", 0f, run => run.availableLunarItemDropList);
            Weight("Void Common", 0f, run => run.availableVoidTier1DropList);
            Weight("Void Uncommon", 0f, run => run.availableVoidTier2DropList);
            Weight("Void Legendary", 0f, run => run.availableVoidTier3DropList);
            Weight("Void Boss", 0f, run => run.availableVoidBossDropList);
            Weight("Meal", 0f, run => run.availableFoodTierDropList);
            Weight("Hallowed", 0f, run => run.availableCursedTierDropList);
            Weight("Hex", 0f, run => run.availableHexTierDropList);
            Weight("Page", 0f, run => run.availablePageTierDropList);
            Weight("Power Shape", 0f, run => run.availablePowerShapeItemsDropList);
            LanguageAPI.Add(NameToken, "Hallowed Pool");
            LanguageAPI.Add(ContextToken, "Banish the displayed item");
            availablePickupsChanged = A.Field(typeof(Run), nameof(Run.onAvailablePickupsModified))
                ?? A.Field(typeof(Run), "m_onAvailablePickupsModified");
            basicSelector = A.Field(typeof(BasicPickupDropTable), "selector");
            RoR2Application.onLoad += PreparePrefab;
            BazaarSpawnHelper.Register(typeof(HallowedPool), "HallowedPool", nameof(AfterStageStart));

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.HallowedPool");
            Patch(harmony, typeof(CostTypeDef), nameof(CostTypeDef.BuildCostString),
                prefix: nameof(BeforeHexCostText), postfix: nameof(AfterHexCostText),
                args: new[] { typeof(int), typeof(StringBuilder) });
            Patch(harmony, typeof(CostTypeDef), nameof(CostTypeDef.BuildCostStringStyled),
                prefix: nameof(BeforeHexCostText), postfix: nameof(AfterHexCostText),
                args: new[] { typeof(int), typeof(StringBuilder), typeof(bool), typeof(bool) });
            Patch(harmony, typeof(PurchaseInteraction), "OnInteractionBegin", prefix: nameof(BeforePurchase));
            harmony.Patch(A.Method(typeof(ShopTerminalBehavior), "UpdatePickupDisplayAndAnimations"),
                transpiler: new HarmonyMethod(typeof(HallowedPool), nameof(ReplaceShutterSound)));
            Patch(harmony, typeof(Run), "BuildDropTable", postfix: nameof(AfterBuildDropTable));
            Patch(harmony, typeof(Run), "EnableItemDrop", prefix: nameof(BeforeEnableDrop));
            Patch(harmony, typeof(CraftableCatalog), "FilterByEntitlement", postfix: nameof(FilterCraftingRecipes));
            Patch(harmony, typeof(CraftingController), "OnConfirmServer", prefix: nameof(BeforeCraftingConfirm));
            var mealType = typeof(Run).Assembly.GetType("RoR2.MealPrepController");
            if (mealType != null)
                harmony.Patch(A.Method(mealType, "BeginCookingServer"),
                    prefix: new HarmonyMethod(typeof(HallowedPool), nameof(BeforeMealOrder)) { priority = Priority.First });
            Patch(harmony, typeof(PickupDropTable), "GeneratePickup", postfix: nameof(FilterSingleDrop));
            Patch(harmony, typeof(PickupDropTable), "GenerateDistinctPickups", prefix: nameof(BeforeDistinct), postfix: nameof(FilterDistinct));
            Patch(harmony, typeof(PickupDropTable), "GenerateDrop", postfix: nameof(FilterLegacyDrop));
            Patch(harmony, typeof(PickupDropTable), "GenerateUniqueDrops", postfix: nameof(FilterLegacyDrops));
            Patch(harmony, typeof(BasicPickupDropTable), "Add", prefix: nameof(BeforeBasicAdd), postfix: nameof(AfterBasicAdd));
            Patch(harmony, typeof(ShopTerminalBehavior), "SetPickup", prefix: nameof(FilterTerminal));
            Patch(harmony, typeof(ShopTerminalBehavior), "DropPickup", prefix: nameof(BeforeTerminalDrop), args: new[] { typeof(bool) });
            Patch(harmony, typeof(PickupDistributorBehavior), "SetPickup", prefix: nameof(FilterTerminal));
            Patch(harmony, typeof(PickupPickerController), "SetOptionsServer", prefix: nameof(FilterPicker));
            var earlyDrop = new HarmonyMethod(typeof(HallowedPool), nameof(FilterEarlyDroplet)) { priority = Priority.First };
            var droplet = A.Method(typeof(PickupDropletController), "CreatePickupDroplet",
                new[] { typeof(UniquePickup), typeof(Vector3), typeof(Vector3) });
            if (droplet != null) harmony.Patch(droplet, prefix: earlyDrop);
            Patch(harmony, typeof(PickupDropletController), "CreatePickupDroplet", prefix: nameof(FilterDroplet),
                args: new[] { typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3) });
            Patch(harmony, typeof(PickupDropletController), "OnEnable", postfix: nameof(MarkFixedDroplet));
            Patch(harmony, typeof(PickupDropletController), "InitializePickup", prefix: nameof(BeforeFixedDroplet), finalizer: nameof(EndFixedSpawner));
            Patch(harmony, typeof(GenericPickupController), "CreatePickup", prefix: nameof(FilterCreatedPickup), postfix: nameof(MarkCreatedPickup),
                args: new[] { typeof(GenericPickupController.CreatePickupInfo).MakeByRefType() });
            Patch(harmony, typeof(GenericPickupController), "AttemptGrant", prefix: nameof(BeforeGrant), finalizer: nameof(FinishGrant));
            Patch(harmony, typeof(Inventory), "GiveItemPermanent", prefix: nameof(FilterGrant), args: new[] { typeof(ItemIndex), typeof(int) });
            Patch(harmony, typeof(Inventory), "GiveItemRented", prefix: nameof(FilterGrant), args: new[] { typeof(ItemIndex), typeof(int) });
            Patch(harmony, typeof(Inventory), "GiveItemTemp", prefix: nameof(FilterTempGrant), args: new[] { typeof(ItemIndex), typeof(float) });
            Patch(harmony, typeof(Inventory.ItemTransformation), "TryTransform", prefix: nameof(FilterTransformation));

            // Authored stage pickups can be emitted by a coroutine. Keep their explicit tables and grant intact.
            var spawner = typeof(Run).Assembly.GetType("RoR2.PickupSpawner");
            var coroutine = spawner == null ? null : A.Method(spawner, "SpawnPickupsCoroutine");
            var iterator = coroutine?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
            if (iterator != null)
                Patch(harmony, iterator, "MoveNext", prefix: nameof(BeforeFixedSpawner), finalizer: nameof(EndFixedSpawner));
            else
                ThreeQsVanillaTweaks.Log.Warning("Hallowed Pool could not hook the authored pickup spawner.");
        }

        private static void Patch(Harmony harmony, Type type, string name, string prefix = null, string postfix = null,
            string finalizer = null, Type[] args = null)
        {
            var target = args == null ? A.Method(type, name) : A.Method(type, name, args);
            if (target == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Hallowed Pool could not hook " + type.Name + "." + name + ".");
                return;
            }
            harmony.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(HallowedPool), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(HallowedPool), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(HallowedPool), finalizer));
        }

        private static void Weight(string name, float value, Func<Run, List<PickupIndex>> pool)
        {
            weights.Add(new PoolWeight { Pool = pool, Weight = StageFeatureSpawnHelper.Percent(Section,
                name + " Pool Weight (%)", value, "Relative chance to select this item pool when it contains a ban target.") });
        }

        private static void BeforeHexCostText(CostTypeDef __instance, int __0, StringBuilder __1, out int? __state)
        {
            __state = __0 == 1 && __instance.costTypeIndex == CostTypeIndex.Hex ? __1?.Length : null;
        }

        private static void AfterHexCostText(StringBuilder __1, int? __state)
        {
            // Only correct the English plural in this cost's appended text, preserving the game's styling.
            if (__state is int start && __1.Length > start)
                __1.Replace("Hexes", "Hex", start, __1.Length - start);
        }

        private static IEnumerable<CodeInstruction> ReplaceShutterSound(IEnumerable<CodeInstruction> instructions)
        {
            var selectSound = A.Method(typeof(HallowedPool), nameof(SelectTerminalSound));
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, TerminalShutterSound))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, selectSound);
                }
            }
        }

        private static string SelectTerminalSound(string original, ShopTerminalBehavior terminal)
        {
            if (!terminal.GetComponent<PoolOffer>()) return original;
            return null;
        }

        private static void PreparePrefab()
        {
            try
            {
                var cleansingPool = StageFeatureSpawnHelper.LoadPrefab(PoolPath);
                useEffect = cleansingPool.GetComponentInChildren<ShrineCleanseBehavior>(true)?.activationEffectPrefab;
                var terminalPrefab = StageFeatureSpawnHelper.LoadPrefab(CauldronPath);
                prefab = PrefabAPI.InstantiateClone(terminalPrefab, "3Qs_HallowedPool", true);
                prefab.AddComponent<PoolOffer>();
                var projector = prefab.GetComponent<HologramProjector>();
                if (projector) projector.disableHologramRotation = false;
            }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Hallowed Pool prefab could not be prepared: " + e.Message);
            }
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || lastStage == __instance) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, () =>
            {
                if (!prefab || !SpawnRequirements.ExpansionEnabled("DLC4")) return;
                Spawn(first);
                Spawn(second);
                Spawn(third);
            }));
        }

        private static void Spawn(BazaarSpawnHelper.Settings settings)
        {
            if (!BazaarSpawnHelper.Roll(settings)) return;
            BazaarSpawnHelper.Spawn(settings, prefab, settings.Name, settings.Position, settings.Forward, instance =>
            {
                var purchase = instance.GetComponent<PurchaseInteraction>();
                var terminal = instance.GetComponent<ShopTerminalBehavior>();
                if (!purchase || !terminal) throw new InvalidOperationException("The pool has no purchase or display component.");
                purchase.automaticallyScaleCostWithDifficulty = false;
                purchase.NetworkcostType = CostTypeIndex.Hex;
                purchase.Networkcost = C.Get(hexCost);
                purchase.saleStarCompatible = false;
                purchase.NetworkdisplayNameToken = NameToken;
                purchase.NetworkcontextToken = ContextToken;
                purchase.onSetAvailable = new PurchaseAvailabilityEvent();
                purchase.onSetUnavailable = new PurchaseAvailabilityEvent();
                terminal.selfGeneratePickup = false;
                terminal.dropTable = null;
                SetOffer(purchase, terminal);
            });
        }

        private static bool SetOffer(PurchaseInteraction purchase, ShopTerminalBehavior terminal, bool enablePurchase = true)
        {
            var run = Run.instance;
            var selection = new WeightedSelection<List<PickupIndex>>();
            foreach (var entry in weights)
            {
                float weight = C.Get(entry.Weight);
                if (weight <= 0f) continue;
                var candidates = entry.Pool(run).FindAll(index =>
                {
                    var item = Item(index);
                    return item && run.IsItemAvailable(item.itemIndex) && !IsBanned(item.itemIndex) && !IsScrap(item);
                });
                if (candidates.Count > 0) selection.AddChoice(candidates, weight);
            }
            if (selection.Count == 0)
            {
                terminal.SetPickup(UniquePickup.none);
                purchase.SetAvailable(false);
                return false;
            }
            var pool = selection.Evaluate(run.treasureRng.nextNormalizedFloat);
            terminal.SetPickup(new UniquePickup(pool[run.treasureRng.RangeInt(0, pool.Count)]));
            purchase.SetAvailable(enablePurchase);
            return true;
        }

        private static bool BeforePurchase(PurchaseInteraction __instance, Interactor activator)
        {
            if (!NetworkServer.active || !__instance.GetComponent<PoolOffer>()) return true;
            if (!activator || __instance.GetInteractability(activator) != Interactability.Available) return false;
            var terminal = __instance.GetComponent<ShopTerminalBehavior>();
            var item = Item(terminal.CurrentPickup().pickupIndex);
            if (!item || IsBanned(item.itemIndex)) { SetOffer(__instance, terminal); return false; }
            var body = activator.GetComponent<CharacterBody>();
            var inventory = body ? body.inventory : null;
            if (!inventory) return false;

            int requiredHexes = __instance.cost;
            var payment = new List<ItemIndex>();
            var hex = CostTypeCatalog.GetCostTypeDef(CostTypeIndex.Hex).itemTier;
            foreach (var index in inventory.itemAcquisitionOrder)
            {
                var candidate = ItemCatalog.GetItemDef(index);
                if (!candidate || candidate.tier != hex || !candidate.canRemove || candidate.ContainsTag(ItemTag.ObjectiveRelated)) continue;
                int count = Math.Min(inventory.GetItemCountPermanent(index), inventory.GetItemCountEffective(index));
                for (int i = 0; i < Math.Min(count, requiredHexes); i++) payment.Add(index);
            }
            if (payment.Count < requiredHexes || !BazaarUseCosts.TryPayPurchaseFee(__instance, activator)) return false;

            __instance.SetAvailable(false);
            var rng = Run.instance.treasureRng;
            for (int i = 0; i < requiredHexes; i++)
            {
                int slot = rng.RangeInt(0, payment.Count);
                var index = payment[slot];
                payment.RemoveAt(slot);
                inventory.RemoveItemPermanent(index, 1);
                PurchaseInteraction.CreateItemTakenOrb(body.corePosition, __instance.gameObject, index);
            }
            Ban(Run.instance, item);
            __instance.StartCoroutine(AnimateBanish(__instance, terminal));
            return false;
        }

        private static IEnumerator AnimateBanish(PurchaseInteraction purchase, ShopTerminalBehavior terminal)
        {
            terminal.SetHasBeenPurchased(true);
            yield return new WaitForSeconds(SinkDuration);
            if (!purchase || !terminal) yield break;

            // Let the terminal close with no offer, then return it to idle before revealing the next item.
            terminal.SetPickup(UniquePickup.none);
            if (useEffect)
                EffectManager.SimpleEffect(useEffect, terminal.transform.position, terminal.transform.rotation, true);
            yield return new WaitForSeconds(EmptyDuration * .5f);
            if (!purchase || !terminal) yield break;

            terminal.SetHasBeenPurchased(false);
            yield return new WaitForSeconds(EmptyDuration * .5f);
            if (!purchase || !terminal || !Run.instance || !SetOffer(purchase, terminal, enablePurchase: false))
                yield break;

            yield return new WaitForSeconds(RiseDuration);
            if (purchase && terminal) purchase.SetAvailable(true);
        }

        internal static bool HasBans => Run.instance && bans.TryGetValue(Run.instance, out var state) && state.Items.Count > 0;
        internal static bool IsBanned(ItemIndex index) => Run.instance && bans.TryGetValue(Run.instance, out var state) && state.Items.Contains(index);
        internal static bool IsBanned(PickupIndex index) => HasBans && IsBanned(Item(index)?.itemIndex ?? ItemIndex.None);

        private static void FilterCraftingRecipes(ref CraftableCatalog.RecipeEntry[] source)
        {
            if (HasBans)
                source = Array.FindAll(source, recipe => !IsBanned(recipe.result));
        }

        private static bool BeforeCraftingConfirm(CraftingController __instance)
        {
            if (!HasBans) return true;
            var recipe = __instance.bestFitRecipe;
            return recipe != null && __instance.AllSlotsFilled() && !IsBanned(recipe.result);
        }

        private static bool BeforeMealOrder(PickupIndex reward) => !IsBanned(reward);

        private static ItemDef Item(PickupIndex pickup) => ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(pickup)?.itemIndex ?? ItemIndex.None);
        private static bool IsScrap(ItemDef item) => item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap);
        private static PickupIndex Scrap(ItemTier tier)
        {
            ItemDef scrap = tier == ItemTier.Tier1 ? RoR2Content.Items.ScrapWhite :
                tier == ItemTier.Tier2 ? RoR2Content.Items.ScrapGreen :
                tier == ItemTier.Tier3 ? RoR2Content.Items.ScrapRed :
                tier == ItemTier.Boss ? RoR2Content.Items.ScrapYellow : null;
            return PickupCatalog.FindPickupIndex((scrap ? scrap : RoR2Content.Items.ScrapWhite).itemIndex);
        }

        private static void Ban(Run run, ItemDef item)
        {
            var state = bans.GetOrCreateValue(run);
            state.Items.Add(item.itemIndex);
            FilterRunPools(run, state);
            if (availablePickupsChanged != null)
                (availablePickupsChanged.GetValue(null) as Action<Run>)?.Invoke(run);
            else if (!logged)
            {
                logged = true;
                ThreeQsVanillaTweaks.Log.Warning("Hallowed Pool could not refresh cached drop tables. Runtime filtering is still active.");
            }

            foreach (var terminal in UnityEngine.Object.FindObjectsOfType<ShopTerminalBehavior>())
            {
                var previous = terminal.CurrentPickup();
                if (!IsBanned(previous.pickupIndex)) continue;
                var purchase = terminal.GetComponent<PurchaseInteraction>();
                if (purchase && purchase.GetComponent<PoolOffer>())
                {
                    if (purchase.available) SetOffer(purchase, terminal);
                }
                else
                    terminal.SetPickup(Replace(previous, run.treasureRng), terminal.pickupIndexIsHidden);
            }
            foreach (var distributor in UnityEngine.Object.FindObjectsOfType<PickupDistributorBehavior>())
            {
                var previous = distributor.GetPickup();
                if (IsBanned(previous.pickupIndex))
                    distributor.SetPickup(Replace(previous, run.treasureRng), distributor.pickupIndexIsHidden);
            }
            foreach (var picker in UnityEngine.Object.FindObjectsOfType<PickupPickerController>())
                if (picker.options != null && Array.Exists(picker.options, option => IsBanned(option.pickup.pickupIndex)))
                    picker.SetOptionsServer(picker.options);
        }

        private static void FilterRunPools(Run run, BanState state)
        {
            foreach (var entry in weights)
            {
                var list = entry.Pool(run);
                int removed = list.RemoveAll(IsBanned);
                if (removed > 0 && list.Count == 0)
                    state.Depleted.Add(list);
                if (list.Count == 0 && state.Depleted.Contains(list))
                    list.Add(Scrap(TierForPool(run, list)));
            }
            foreach (var pair in run.allAvailableTierDroplists)
            {
                int removed = pair.Value.RemoveAll(IsBanned);
                if (removed > 0 && pair.Value.Count == 0) state.Depleted.Add(pair.Value);
                if (pair.Value.Count == 0 && state.Depleted.Contains(pair.Value)) pair.Value.Add(Scrap(pair.Key));
            }
            foreach (var index in state.Items)
            {
                var item = ItemCatalog.GetItemDef(index);
                if (!item || run.allAvailableTierDroplists.ContainsKey(item.tier)) continue;
                var list = new List<PickupIndex> { Scrap(item.tier) };
                run.allAvailableTierDroplists.Add(item.tier, list);
                state.Depleted.Add(list);
            }
            run.availableLunarCombinedDropList.RemoveAll(IsBanned);
            foreach (var index in run.availableLunarItemDropList)
                if (!run.availableLunarCombinedDropList.Contains(index)) run.availableLunarCombinedDropList.Add(index);
        }

        private static ItemTier TierForPool(Run run, List<PickupIndex> pool) =>
            ReferenceEquals(pool, run.availableTier2DropList) ? ItemTier.Tier2 :
            ReferenceEquals(pool, run.availableTier3DropList) ? ItemTier.Tier3 :
            ReferenceEquals(pool, run.availableBossDropList) ? ItemTier.Boss : ItemTier.Tier1;

        private static void AfterBuildDropTable(Run __instance)
        {
            if (!NetworkServer.active || !bans.TryGetValue(__instance, out var state)) return;
            FilterRunPools(__instance, state);
        }

        private static bool BeforeEnableDrop(ItemIndex itemIndex) => !IsBanned(itemIndex);

        private static PickupIndex Replacement(PickupIndex pickup, Xoroshiro128Plus rng)
        {
            var item = Item(pickup);
            if (!item || !IsBanned(item.itemIndex)) return pickup;
            if (Run.instance.allAvailableTierDroplists.TryGetValue(item.tier, out var candidates))
            {
                var available = candidates.FindAll(candidate => !IsBanned(candidate));
                if (available.Count > 0) return available[rng.RangeInt(0, available.Count)];
            }
            return Scrap(item.tier);
        }

        internal static bool IsFallback(PickupIndex pickup, ItemTier tier) =>
            HasBans && pickup == Scrap(tier) &&
            (!Run.instance.allAvailableTierDroplists.TryGetValue(tier, out var pool) || pool.Count == 0 ||
                pool.Count == 1 && pool[0] == pickup);

        private static UniquePickup Replace(UniquePickup pickup, Xoroshiro128Plus rng) =>
            IsBanned(pickup.pickupIndex) ? pickup.WithPickupIndex(Replacement(pickup.pickupIndex, rng)) : pickup;

        private static bool FilterLoot => NetworkServer.active && HasBans && fixedSpawnDepth == 0;

        private static void BeforeBasicAdd(BasicPickupDropTable __instance, out int __state) =>
            __state = FilterLoot && basicSelector?.GetValue(__instance) is WeightedSelection<UniquePickup> selector ? selector.Count : -1;

        private static void AfterBasicAdd(BasicPickupDropTable __instance, List<PickupIndex> sourceDropList, float chance, int __state)
        {
            if (__state < 0 || chance <= 0f || sourceDropList.Count != 1 ||
                !bans.GetOrCreateValue(Run.instance).Depleted.Contains(sourceDropList)) return;
            if (basicSelector.GetValue(__instance) is WeightedSelection<UniquePickup> selector && selector.Count == __state)
                selector.AddChoice(new UniquePickup(sourceDropList[0]), chance);
        }
        private static void FilterSingleDrop(PickupDropTable __instance, Xoroshiro128Plus rng, ref UniquePickup __result)
        {
            if (!FilterLoot || rolling != 0 || !IsBanned(__result.pickupIndex)) return;
            rolling++;
            try
            {
                var tier = Item(__result.pickupIndex).tier;
                if (__instance.GetPickupCount() > 1)
                    for (int i = 0; i < 8; i++)
                    {
                        var next = __instance.GeneratePickup(rng);
                        var nextItem = Item(next.pickupIndex);
                        if (!nextItem || nextItem.tier != tier || IsBanned(nextItem.itemIndex)) continue;
                        __result = next;
                        return;
                    }
                __result = Replace(__result, rng);
            }
            finally { rolling--; }
        }

        private static void FilterLegacyDrop(Xoroshiro128Plus rng, ref PickupIndex __result)
        { if (FilterLoot) __result = Replacement(__result, rng); }

        private static void FilterLegacyDrops(Xoroshiro128Plus rng, PickupIndex[] __result)
        {
            if (!FilterLoot || __result == null) return;
            for (int i = 0; i < __result.Length; i++) __result[i] = Replacement(__result[i], rng);
        }

        private static void BeforeDistinct(List<UniquePickup> dest, out int __state) => __state = dest.Count;
        private static void FilterDistinct(List<UniquePickup> dest, Xoroshiro128Plus rng, int __state)
        {
            if (!FilterLoot) return;
            for (int i = __state; i < dest.Count; i++) dest[i] = Replace(dest[i], rng);
        }

        private static void FilterTerminal(Component __instance, ref UniquePickup newPickup)
        {
            if (FilterLoot && !__instance.GetComponent<PoolOffer>())
                newPickup = Replace(newPickup, Run.instance.treasureRng);
        }

        private static bool BeforeTerminalDrop(ShopTerminalBehavior __instance) => !__instance.GetComponent<PoolOffer>();

        private static void FilterPicker(PickupPickerController __instance, ref PickupPickerController.Option[] newOptions)
        {
            if (!FilterLoot || newOptions == null || __instance is CraftingController ||
                __instance.GetComponent<ScrapperController>() || (eggController != null && __instance.GetComponent(eggController))) return;
            // Inventory-based pickers take items rather than generate them.
            if (__instance.isRebirthChoice) return;
            PickupPickerController.Option[] changed = null;
            for (int i = 0; i < newOptions.Length; i++)
            {
                if (!IsBanned(newOptions[i].pickup.pickupIndex)) continue;
                if (changed == null) changed = (PickupPickerController.Option[])newOptions.Clone();
                changed[i].pickup = Replace(newOptions[i].pickup, Run.instance.treasureRng);
            }
            if (changed != null) newOptions = changed;
        }

        private static void FilterDroplet(ref GenericPickupController.CreatePickupInfo pickupInfo)
        {
            if (FilterLoot) pickupInfo.pickup = Replace(pickupInfo.pickup, Run.instance.treasureRng);
        }

        private static void FilterEarlyDroplet(ref UniquePickup pickup)
        { if (FilterLoot) pickup = Replace(pickup, Run.instance.treasureRng); }

        private static void MarkFixedDroplet(PickupDropletController __instance)
        {
            if (fixedSpawnDepth > 0) __instance.gameObject.AddComponent<FixedPickup>();
        }

        private static void BeforeFixedDroplet(PickupDropletController __instance, out bool __state)
        {
            __state = __instance.GetComponent<FixedPickup>();
            if (__state) fixedSpawnDepth++;
        }

        private static void FilterCreatedPickup(ref GenericPickupController.CreatePickupInfo createPickupInfo, out bool __state)
        {
            var pickup = createPickupInfo.pickup;
            __state = fixedSpawnDepth > 0;
            if (FilterLoot && !__state) createPickupInfo.pickup = Replace(pickup, Run.instance.treasureRng);
        }

        private static void MarkCreatedPickup(GenericPickupController __result, bool __state)
        { if (__state && __result) __result.gameObject.AddComponent<FixedPickup>(); }

        private static void BeforeGrant(GenericPickupController __instance, out bool __state)
        {
            __state = NetworkServer.active && (__instance.GetComponent<FixedPickup>() ||
                (__instance.GetComponent<NetworkIdentity>() is NetworkIdentity identity && !identity.sceneId.IsEmpty()));
            if (__state) fixedGrantDepth++;
            else if (FilterLoot && IsBanned(__instance.pickup.pickupIndex))
                __instance.pickup = Replace(__instance.pickup, Run.instance.treasureRng);
        }

        private static void FinishGrant(bool __state) { if (__state) fixedGrantDepth--; }

        private static void FilterGrant(ref ItemIndex itemIndex, int countToAdd)
        {
            if (countToAdd > 0 && fixedGrantDepth == 0 && FilterLoot && IsBanned(itemIndex))
                itemIndex = Item(Replacement(PickupCatalog.FindPickupIndex(itemIndex), Run.instance.treasureRng))?.itemIndex ?? ItemIndex.None;
        }

        private static void FilterTempGrant(ref ItemIndex itemIndex, float countToAdd)
        { if (countToAdd > 0) FilterGrant(ref itemIndex, 1); }

        private static void FilterTransformation(ref Inventory.ItemTransformation __instance)
        {
            if (!FilterLoot || fixedGrantDepth > 0 || !IsBanned(__instance.newItemIndex)) return;
            var replacement = Replacement(PickupCatalog.FindPickupIndex(__instance.newItemIndex), Run.instance.treasureRng);
            __instance.newItemIndex = Item(replacement)?.itemIndex ?? ItemIndex.None;
        }

        private static void BeforeFixedSpawner(object __instance, out bool __state)
        {
            var spawner = A.Field(__instance.GetType(), "<>4__this")?.GetValue(__instance) as PickupSpawner;
            __state = spawner && spawner.pickupDropTable is ExplicitPickupDropTable && spawner.ignoreArtifactPickupReplacements;
            if (__state) fixedSpawnDepth++;
        }

        private static void EndFixedSpawner(bool __state) { if (__state) fixedSpawnDepth--; }
    }
}
