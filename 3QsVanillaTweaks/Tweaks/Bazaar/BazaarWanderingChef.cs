using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using R2API;
using RoR2;
using RoR2.Networking;
using ThreeQsVanillaTweaks;
using UnityEngine;
using Tweaks.Interactables;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Bazaar
{
    internal static class BazaarWanderingChef
    {
        private const string ExchangeScene = "computationalexchange";
        private const string MealPrepGroup = "GROUP: Meal Prep";
        private const string CounterMesh = "Bazaar_LunarTable (1)";
        private static BazaarSpawnHelper.Settings settings;
        private static ConfigEntry<int> mealFee;
        private static Stage lastStage;
        private static GameObject menuPrefab, chefPrefab, tablePrefab;

        internal static void Init()
        {
            var position = new Vector3(-117.09f, -26.24f, -17.86f);
            var facing = Quaternion.Euler(0f, 30.33f, 0f) * Vector3.forward;
            const string section = "Bazaar - 05 Extra Interactables - Wandering Chef";
            settings = BazaarSpawnHelper.Bind(section,
                "Wandering Chef", null, 40f, position.x, position.y, position.z, facing.x, facing.y, facing.z,
                heightOffset: .65f,
                exactPose: "-128.0188, -24.4426785, -34.0821953, -0.000103950515, 300.33, -2.28881836E-05");
            mealFee = BazaarUseCosts.BindCoinPrice(section, 0, "Lunar coins per meal in addition to the recipe ingredients. Vanilla: no coin fee.");
            WanderingChef.InstallHooks();
            RequestTable();
            RoR2Application.onLoad += PrepareMenuPrefab;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.WanderingChef");
            harmony.Patch(A.Method(typeof(CraftingController), "GetGeneratedOptionsFromInteractor"),
                postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(RestrictIngredients)));
            harmony.Patch(A.Method(typeof(CraftingController), "AttemptFindPossibleRecipes"),
                transpiler: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(FilterRecipesIL)));
            harmony.Patch(A.Method(typeof(CraftingController), "OnConfirmServer"),
                prefix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(BeforeConfirm)));
            harmony.Patch(A.Method(typeof(PickupPickerController), "OnInteractionBegin"),
                postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(RefreshMenu)));
            harmony.Patch(A.Method(typeof(PickupPickerController), "GetInteractability"),
                postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(AfterInteractability)));
            harmony.Patch(A.Method(typeof(PickupPickerController), "GetContextString"),
                postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(AfterContext)) { priority = Priority.First });
            harmony.Patch(A.Method(typeof(EntityStates.MealPrep.MealPrepBaseState), "OnEnter"),
                postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(CaptureTableDuringVisit)));
            var mealType = typeof(Run).Assembly.GetType("RoR2.MealPrepController");
            if (mealType != null)
                harmony.Patch(A.Method(mealType, "BeginCookingServer"),
                    prefix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(ValidateOrder)) { priority = Priority.First },
                    postfix: new HarmonyMethod(typeof(BazaarWanderingChef), nameof(ChargeMealFee))
                    {
                        after = new[] { "3Questionmarks.3QsVanillaTweaks.Interactables.WanderingChef" }
                    });
            BazaarSpawnHelper.Register(typeof(BazaarWanderingChef), "Chef", nameof(AfterStageStart));
        }

        private static void AfterStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || lastStage == __instance) return;
            lastStage = __instance;
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, () =>
            {
                if (!BazaarSpawnHelper.Roll(settings) || !SpawnRequirements.ExpansionEnabled("DLC3")) return;
                var prefab = chefPrefab ? chefPrefab : WanderingChef.ChefPrefab;
                if (menuPrefab && prefab)
                    BazaarSpawnHelper.Spawn(settings, prefab, settings.Name, settings.Position, settings.Forward, ConfigureChef);
            }));
        }

        private static void PrepareMenuPrefab()
        {
            if (menuPrefab || !WanderingChef.MenuPrefab) return;
            try
            {
                var prefab = PrefabAPI.InstantiateClone(WanderingChef.MenuPrefab, "3Qs_BazaarChefMenu", true);
                // The menu syncs its recipe itself. A PickupIndexNetworker here makes LookingGlass
                // treat the menu as a potential, skip sorting it and remap clicks with a stale map.
                prefab.AddComponent<RestrictedMenu>();
                menuPrefab = prefab;

                if (WanderingChef.ChefPrefab)
                {
                    chefPrefab = PrefabAPI.InstantiateClone(WanderingChef.ChefPrefab, "3Qs_BazaarWanderingChef", true);
                    chefPrefab.AddComponent<ChefTable>();
                }
            }
            catch (Exception e) { Log.Warning("The Bazaar Chef menu could not be prepared: " + e); }
        }

        private static event Action TablePrepared;

        private static void RequestTable()
        {
            // The optional SceneAsset module supplies scene-only props without a build-time reference.
            var api = Type.GetType("R2API.SceneAssetAPI, R2API.SceneAsset");
            var request = api?.GetMethod("AddAssetRequest", new[] { typeof(string), typeof(Action<GameObject[]>) });
            if (request == null)
            {
                Log.Warning("Bazaar Wandering CHEF's table requires R2API.SceneAsset.");
                return;
            }

            try
            {
                request.Invoke(null, new object[] { ExchangeScene, (Action<GameObject[]>)PrepareTable });
            }
            catch (Exception e) { Log.Warning("Could not request Wandering CHEF's table: " + e); }
        }

        private static void CaptureTableDuringVisit(EntityStates.MealPrep.MealPrepBaseState __instance)
        {
            if (tablePrefab) return;
            var scene = __instance.gameObject.scene;
            if (scene.name == ExchangeScene) PrepareTable(scene.GetRootGameObjects());
        }

        private static void PrepareTable(GameObject[] sceneRoots)
        {
            if (tablePrefab) return;
            Transform table = sceneRoots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == "Table" && transform.parent &&
                    transform.parent.name == MealPrepGroup);
            if (!table)
            {
                Log.Warning("Bazaar Wandering CHEF: Computational Exchange did not contain " + MealPrepGroup + "/Table.");
                return;
            }

            // The counter is scene geometry. The cooking interactable can live in a separate scene root.
            var chef = sceneRoots.SelectMany(root => root.GetComponentsInChildren<MealPrepController>(true))
                .Where(controller => controller.craftingController)
                .OrderBy(controller => (controller.craftingController.transform.position - table.position).sqrMagnitude)
                .FirstOrDefault();
            Matrix4x4 anchorMatrix;
            Quaternion anchorRotation;
            Vector3 anchorScale;
            if (chef)
            {
                var anchor = chef.craftingController.transform;
                anchorMatrix = anchor.localToWorldMatrix;
                anchorRotation = anchor.rotation;
                anchorScale = anchor.lossyScale;
            }
            else if (!TryGetAuthoredChefAnchor(sceneRoots, table.position, out anchorMatrix, out anchorRotation, out anchorScale))
            {
                Log.Warning("Bazaar Wandering CHEF: found the counter, but couldn't resolve the cooking anchor during preload. Will retry when the Exchange chef spawns.");
                return;
            }

            var clone = PrefabAPI.InstantiateClone(table.gameObject, "3Qs_WanderingChefTable", false);
            var cyberification = clone.transform.Find("Cyberification");
            if (cyberification) UnityEngine.Object.DestroyImmediate(cyberification.gameObject);
            var counter = clone.transform.Find(CounterMesh);
            var components = clone.GetComponentsInChildren<Component>(true);
            for (int i = components.Length - 1; i >= 0; i--)
            {
                var component = components[i];
                if (!component || component is Transform || component is Renderer || component is MeshFilter ||
                    component is ParticleSystem || component is Light ||
                    (component is MeshCollider && component.transform == counter)) continue;
                UnityEngine.Object.DestroyImmediate(component);
            }

            var tableScale = table.lossyScale;
            clone.transform.SetLocalPositionAndRotation(anchorMatrix.inverse.MultiplyPoint3x4(table.position),
                Quaternion.Inverse(anchorRotation) * table.rotation);
            clone.transform.localScale = new Vector3(tableScale.x / anchorScale.x,
                tableScale.y / anchorScale.y, tableScale.z / anchorScale.z);
            tablePrefab = clone;
            Log.Info("Bazaar Wandering CHEF: prepared the Computational Exchange counter.");
            TablePrepared?.Invoke();
        }

        private static bool TryGetAuthoredChefAnchor(GameObject[] roots, Vector3 tablePosition,
            out Matrix4x4 anchorMatrix, out Quaternion anchorRotation, out Vector3 anchorScale)
        {
            // During preload the scene can contain an authored spawn point in place of the spawned chef.
            var spawnPoints = roots.SelectMany(root => root.GetComponentsInChildren<InstantiatePrefabBehavior>(true))
                .Where(point => point.targetTransform)
                .Select(point => (point.prefab, point.targetTransform.position,
                    point.copyTargetRotation ? point.targetTransform.rotation : Quaternion.identity))
                .Concat(roots.SelectMany(root => root.GetComponentsInChildren<GenericSceneSpawnPoint>(true))
                    .Select(point => (point.networkedObjectPrefab, point.transform.position, point.transform.rotation)));
            foreach (var (prefab, position, rotation) in spawnPoints.OrderBy(point => (point.Item2 - tablePosition).sqrMagnitude))
            {
                if (!prefab) continue;
                var chef = prefab.GetComponentInChildren<MealPrepController>(true);
                if (!chef || !chef.craftingController) continue;
                var root = prefab.transform;
                var anchor = chef.craftingController.transform;
                anchorMatrix = Matrix4x4.TRS(position, rotation, root.localScale) * root.worldToLocalMatrix * anchor.localToWorldMatrix;
                anchorRotation = rotation * Quaternion.Inverse(root.rotation) * anchor.rotation;
                anchorScale = anchorMatrix.lossyScale;
                return true;
            }
            anchorMatrix = default;
            anchorRotation = default;
            anchorScale = default;
            return false;
        }

        private sealed class ChefTable : MonoBehaviour
        {
            private void Start()
            {
                if (tablePrefab) Attach();
                else TablePrepared += Attach;
            }

            private void OnDestroy() => TablePrepared -= Attach;

            private void Attach()
            {
                TablePrepared -= Attach;
                var chef = GetComponentInChildren<MealPrepController>(true);
                if (chef && chef.craftingController)
                {
                    UnityEngine.Object.Instantiate(tablePrefab, chef.craftingController.transform, false).SetActive(true);
                    Log.Info("Bazaar Wandering CHEF: attached the Computational Exchange counter.");
                }
            }
        }

        private static void ConfigureChef(GameObject instance)
        {
            var crafter = instance.GetComponentInChildren<CraftingController>(true);
            var controller = WanderingChef.Controller(crafter);
            if (!controller) throw new InvalidOperationException("The Bazaar Chef has no cooking controller.");
            var offer = new RecipeOffer { IngredientCount = crafter.ingredientCount };
            var queue = WanderingChef.QueueFor(controller);
            queue.MenuPrefab = menuPrefab;
            queue.PrepareMenu = menu => menu.GetComponent<RestrictedMenu>().Recipe = ChooseRecipe(offer, menu.Owner);
        }

        private sealed class RecipeOffer
        {
            internal int IngredientCount;
            internal readonly Dictionary<CharacterMaster, PickupIndex> Recipes = new Dictionary<CharacterMaster, PickupIndex>();
        }

        private sealed class RestrictedMenu : NetworkBehaviour
        {
            private PickupIndex recipe = PickupIndex.none;

            internal PickupIndex Recipe
            {
                get => recipe;
                set
                {
                    if (recipe == value) return;
                    recipe = value;
                    if (NetworkServer.active) SetDirtyBit(1u);
                }
            }

            public override bool OnSerialize(NetworkWriter writer, bool initialState)
            {
                if (!initialState) writer.WritePackedUInt32(syncVarDirtyBits);
                if (!initialState && syncVarDirtyBits == 0u) return false;
                writer.Write(recipe);
                return true;
            }

            public override void OnDeserialize(NetworkReader reader, bool initialState)
            {
                if (initialState || reader.ReadPackedUInt32() != 0u) recipe = reader.ReadPickupIndex();
            }
        }

        private static void RefreshMenu(PickupPickerController __instance, Interactor activator)
        {
            if (!NetworkServer.active || !(__instance is CraftingController crafter) ||
                !crafter.GetComponentInParent<RestrictedMenu>()) return;
            crafter.SetOptionsFromInteractor(activator);
            crafter.ClearAllSlots();
            crafter.SetDirtyBit(1u); // CraftingController's native options bit.
        }

        private static void AfterInteractability(PickupPickerController __instance, Interactor activator, ref Interactability __result)
        {
            if (__result == Interactability.Available && __instance.GetComponentInParent<ChefTable>() &&
                !BazaarUseCosts.CanPay(Config.ConfigManager.Get(mealFee), activator))
                __result = Interactability.ConditionsNotMet;
        }

        private static void AfterContext(PickupPickerController __instance, ref string __result)
        {
            int price = Config.ConfigManager.Get(mealFee);
            if (price > 0 && __instance.GetComponentInParent<ChefTable>()) __result += BazaarUseCosts.FormatFee(price);
        }

        private static bool ValidateOrder(Component __instance, Interactor activator, PickupIndex[] itemsToTake,
            PickupIndex reward, int count, out int __state)
        {
            __state = 0;
            var menu = __instance.GetComponentInParent<RestrictedMenu>();
            if (!menu) return true;

            int price = Config.ConfigManager.Get(mealFee);
            if (!BazaarUseCosts.CanPay(price, activator)) return false;
            if (itemsToTake == null || reward != menu.Recipe || HallowedPool.IsBanned(reward) ||
                !CraftableCatalog.resultToRecipeSearchTable.TryGetValue(reward, out var recipes) ||
                !recipes.Any(recipe => recipe.amountToDrop == count && recipe.possibleIngredients.Length == itemsToTake.Length &&
                    CraftableCatalog.ValidateRecipe(recipe, itemsToTake))) return false;

            // A pending order has already paid. The shared Chef hook owns the queue transition.
            var proxy = menu.GetComponent<WanderingChef.MenuProxy>();
            if (proxy && !proxy.Pending) __state = price;
            return true;
        }

        private static void ChargeMealFee(Component __instance, Interactor activator, int __state, bool __runOriginal)
        {
            if (!__runOriginal || __state == 0 || !NetworkServer.active) return;
            var proxy = __instance.GetComponentInParent<WanderingChef.MenuProxy>();
            if (proxy && proxy.Pending) BazaarUseCosts.Pay(__state, activator);
        }

        private static PickupIndex ChooseRecipe(RecipeOffer offer, CharacterMaster cook)
        {
            if (offer.Recipes.TryGetValue(cook, out var previous)) return previous;
            var recipes = CraftableCatalog.GetAllRecipes();
            CraftableCatalog.FilterByEntitlement(ref recipes);
            var outputs = recipes.Where(recipe => !HallowedPool.IsBanned(recipe.result) &&
                recipe.possibleIngredients.Length == offer.IngredientCount && CanMake(recipe, cook.inventory, 0, new Dictionary<PickupIndex, int>()))
                .Select(recipe => recipe.result).Distinct().ToList();
            if (outputs.Count == 0) return PickupIndex.none;
            var unassigned = outputs.FindAll(output => !offer.Recipes.ContainsValue(output));
            if (unassigned.Count > 0) outputs = unassigned;
            var result = outputs[Run.instance.stageRng.RangeInt(0, outputs.Count)];
            offer.Recipes.Add(cook, result);
            return result;
        }

        private static bool CanMake(CraftableCatalog.RecipeEntry recipe, Inventory inventory, int slot, Dictionary<PickupIndex, int> used)
        {
            if (slot == recipe.possibleIngredients.Length) return true;
            foreach (var pickup in recipe.possibleIngredients[slot].pickups ?? Array.Empty<PickupIndex>())
            {
                var def = PickupCatalog.GetPickupDef(pickup);
                if (def == null) continue;
                int count = 0;
                if (def.itemIndex != ItemIndex.None)
                {
                    var item = ItemCatalog.GetItemDef(def.itemIndex);
                    if (item && ((item.canRemove && !item.hidden) || item.ContainsTag(ItemTag.AllowedForUseAsCraftingIngredient)))
                        count = inventory.GetItemCountPermanent(def.itemIndex);
                }
                else if (def.equipmentIndex != EquipmentIndex.None && inventory.GetEquipmentIndex() == def.equipmentIndex)
                    count = 1;
                used.TryGetValue(pickup, out int taken);
                if (taken >= count) continue;
                used[pickup] = taken + 1;
                if (CanMake(recipe, inventory, slot + 1, used)) return true;
                used[pickup] = taken;
            }
            return false;
        }

        private static void RestrictIngredients(CraftingController __instance, List<PickupPickerController.Option> __result)
        {
            var menu = __instance.GetComponentInParent<RestrictedMenu>();
            if (!menu || __result == null) return;
            var allowed = new HashSet<PickupIndex>();
            var output = menu.Recipe;
            if (!HallowedPool.IsBanned(output) && CraftableCatalog.resultToRecipeSearchTable.TryGetValue(output, out var recipes))
                foreach (var recipe in recipes)
                    if (recipe.possibleIngredients.Length == __instance.ingredientCount)
                        foreach (var slot in recipe.possibleIngredients)
                            if (slot.pickups != null) allowed.UnionWith(slot.pickups);
            __result.RemoveAll(option => !allowed.Contains(option.pickup.pickupIndex));
        }

        private static IEnumerable<CodeInstruction> FilterRecipesIL(IEnumerable<CodeInstruction> instructions)
        {
            var field = A.Field(typeof(CraftingController), "_possibleRecipes");
            var filter = A.Method(typeof(BazaarWanderingChef), nameof(FilterRecipes));
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, field))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, filter);
                }
                yield return instruction;
            }
        }

        private static CraftableCatalog.RecipeEntry[] FilterRecipes(CraftableCatalog.RecipeEntry[] recipes, CraftingController controller)
        {
            var menu = controller.GetComponentInParent<RestrictedMenu>();
            if (!menu) return recipes;
            var output = menu.Recipe;
            return Array.FindAll(recipes, recipe => recipe.result == output &&
                recipe.possibleIngredients.Length == controller.ingredientCount && !HallowedPool.IsBanned(recipe.result));
        }

        private static bool BeforeConfirm(CraftingController __instance)
        {
            var menu = __instance.GetComponentInParent<RestrictedMenu>();
            if (!menu) return true;
            var recipe = __instance.bestFitRecipe;
            return recipe != null && __instance.AllSlotsFilled() && recipe.result == menu.Recipe && !HallowedPool.IsBanned(recipe.result);
        }
    }
}
