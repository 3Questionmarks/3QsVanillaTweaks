using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EntityStates;
using HarmonyLib;
using R2API;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class WanderingChef
    {
        private static ConfigEntry<bool> multiplePlayers, mealsToInventory;
        private static Type mealType, waitType, idleType;
        private static MethodInfo isAffordable;
        internal static GameObject MenuPrefab { get; private set; }
        internal static GameObject ChefPrefab { get; private set; }
        private static bool hooksInstalled;
        [ThreadStatic] private static Meal activeDrop;

        internal static void Init()
        {
            const string section = "Interactables - Wandering Chef";
            multiplePlayers = ModConfig.BindBool(section, "Multiple Players Can Cook", true,
                "Allow multiple players to use the Chef's menu at the same time. Vanilla: False.");
            mealsToInventory = ModConfig.BindBool(section, "Meals Go To Inventory", true,
                "Send cooked meals to the cooking player's inventory. Equipment still drops normally. Vanilla: False.");
            InstallHooks();
        }

        internal static void InstallHooks()
        {
            if (hooksInstalled) return;
            hooksInstalled = true;
            mealType = typeof(Run).Assembly.GetType("RoR2.MealPrepController");
            waitType = typeof(Run).Assembly.GetType("EntityStates.MealPrep.WaitToBeginCooking");
            Type finishType = typeof(Run).Assembly.GetType("EntityStates.MealPrep.CookingToIdle");
            idleType = typeof(Run).Assembly.GetType("EntityStates.MealPrep.Idle");
            if (mealType == null || waitType == null || finishType == null || idleType == null)
            {
                Log.Warning("Wandering Chef changes: the cooking controller is unavailable.");
                return;
            }
            isAffordable = AccessTools.Method(mealType, "IsAffordable");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.WanderingChef");
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "GetInteractability"),
                prefix: new HarmonyMethod(typeof(WanderingChef), nameof(BeforeInteractability)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "OnInteractionBegin"),
                prefix: new HarmonyMethod(typeof(WanderingChef), nameof(BeforeInteraction)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "ShouldShowOnScanner"),
                postfix: new HarmonyMethod(typeof(WanderingChef), nameof(HideMenuProxy)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "ShouldProximityHighlight"),
                postfix: new HarmonyMethod(typeof(WanderingChef), nameof(HideMenuProxy)));
            harmony.Patch(AccessTools.Method(typeof(PickupPickerController), "ShouldIgnoreSpherecastForInteractibility"),
                postfix: new HarmonyMethod(typeof(WanderingChef), nameof(IgnoreMenuProxy)));
            harmony.Patch(AccessTools.Method(mealType, "BeginCookingServer"),
                prefix: new HarmonyMethod(typeof(WanderingChef), nameof(BeforeCooking)),
                postfix: new HarmonyMethod(typeof(WanderingChef), nameof(AfterCooking)),
                finalizer: new HarmonyMethod(typeof(WanderingChef), nameof(FinishCookingCall)));
            harmony.Patch(AccessTools.Method(finishType, "DropItem"),
                prefix: new HarmonyMethod(typeof(WanderingChef), nameof(BeforeDrop)),
                finalizer: new HarmonyMethod(typeof(WanderingChef), nameof(AfterDrop)));
            harmony.Patch(AccessTools.Method(idleType, "OnEnter"),
                postfix: new HarmonyMethod(typeof(WanderingChef), nameof(AfterIdle)));
            // Override this precise cooking droplet call, leaving every other pickup source alone.
            harmony.Patch(AccessTools.Method(typeof(PickupDropletController), "CreatePickupDroplet",
                    new[] { typeof(PickupIndex), typeof(Vector3), typeof(Vector3) }),
                prefix: new HarmonyMethod(typeof(WanderingChef), nameof(DeliverMeal)));
            RoR2Application.onLoad += PrepareMenus;
        }

        private static bool Active => Run.instance && SpawnRequirements.ExpansionEnabled("DLC3");
        private static bool MultiplePlayers => multiplePlayers != null && ModConfig.Get(multiplePlayers);
        private static bool MealsToInventory => mealsToInventory != null && ModConfig.Get(mealsToInventory);

        private static void PrepareMenus()
        {
            if (MenuPrefab) return;
            try
            {
                GameObject original;
                try { original = Stages.StageInteractableAssets.Interactable("MealPrep", mealType.FullName); }
                catch (InvalidOperationException) { original = Stages.StageInteractableAssets.Interactable("Chef", mealType.FullName); }
                ChefPrefab = original;
                MenuPrefab = PrefabAPI.InstantiateClone(original, "3Qs_WanderingChefMenu", true);
                MenuPrefab.AddComponent<MenuProxy>();
                // Each networked proxy contains only an invisible, non-interactable menu.
                // The real Chef alone runs the cooking animation and produces the rewards.
                foreach (Renderer renderer in MenuPrefab.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                foreach (Collider collider in MenuPrefab.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Animator animator in MenuPrefab.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                Type speech = typeof(Run).Assembly.GetType("RoR2.CharacterSpeech.MealPrepSpeechDriver");
                if (speech != null)
                    foreach (Component component in MenuPrefab.GetComponentsInChildren(speech, true))
                        if (component is Behaviour behaviour) behaviour.enabled = false;
            }
            catch (Exception e) { Log.Warning("Wandering Chef menus could not be prepared: " + e); }
        }

        internal sealed class MenuProxy : MonoBehaviour
        {
            internal CharacterMaster Owner;
            internal ChefQueue Chef;
            internal bool Pending;
        }

        internal sealed class Meal
        {
            internal CharacterMaster Cook;
            internal PickupIndex Reward;
            internal int Count, Sent;
            internal List<PickupIndex> Ingredients;
            internal MenuProxy Menu;
        }

        internal sealed class ChefQueue : MonoBehaviour
        {
            internal Component Controller;
            internal GameObject MenuPrefab;
            internal Action<MenuProxy> PrepareMenu;
            internal Meal Current;
            internal readonly Queue<Meal> Waiting = new Queue<Meal>();
            internal readonly Dictionary<CharacterMaster, MenuProxy> Menus = new Dictionary<CharacterMaster, MenuProxy>();

            private void FixedUpdate()
            {
                if (!NetworkServer.active || Current != null || Waiting.Count == 0 || !Controller) return;
                var machine = Read(Controller, "esm") as EntityStateMachine;
                if (!machine || machine.state == null || machine.state.GetType().FullName != "EntityStates.MealPrep.Idle") return;
                Current = Waiting.Dequeue();
                var cooking = (EntityState)Activator.CreateInstance(waitType);
                Write(cooking, "pickupsToTake", Current.Ingredients);
                Write(cooking, "pickupToDrop", Current.Reward);
                Write(cooking, "amountToDrop", Current.Count);
                machine.SetNextState(cooking);
            }

            private void OnDestroy()
            {
                if (!NetworkServer.active) return;
                if (Current != null) CompleteRemaining(Current, transform.position);
                while (Waiting.Count > 0) CompleteRemaining(Waiting.Dequeue(), transform.position);
                foreach (MenuProxy menu in Menus.Values) if (menu) NetworkServer.Destroy(menu.gameObject);
            }
        }

        internal static Component Controller(Component component) => component ? component.GetComponent(mealType) : null;

        internal static ChefQueue QueueFor(Component controller)
        {
            ChefQueue queue = controller.GetComponent<ChefQueue>();
            if (!queue) queue = controller.gameObject.AddComponent<ChefQueue>();
            queue.Controller = controller;
            return queue;
        }

        private static CharacterMaster CookFor(Interactor activator)
        {
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            return body ? body.master : null;
        }

        private static bool BeforeInteractability(PickupPickerController __instance, Interactor activator, ref Interactability __result)
        {
            if (__instance.GetComponentInParent<MenuProxy>()) { __result = Interactability.Disabled; return false; }
            if (!Active || !MenuPrefab || !Controller(__instance)) return true;
            ChefQueue chef = __instance.GetComponent<ChefQueue>();
            if (!MultiplePlayers && (!chef || !chef.MenuPrefab)) return true;
            CharacterMaster cook = CookFor(activator);
            if (!cook || (activator.isRemoteOp && !__instance.allowRemoteOpInteraction)) return true;
            __result = chef && chef.Menus.TryGetValue(cook, out MenuProxy menu) && menu &&
                (menu.Pending || menu.GetComponentInChildren<NetworkUIPromptController>(true).inUse)
                ? Interactability.ConditionsNotMet : Interactability.Available;
            return false;
        }

        private static bool BeforeInteraction(PickupPickerController __instance, Interactor activator)
        {
            if (__instance.GetComponentInParent<MenuProxy>()) return true;
            Component controller = Controller(__instance);
            if (!NetworkServer.active || !Active || !MenuPrefab || !controller) return true;
            var existing = controller.GetComponent<ChefQueue>();
            if (!MultiplePlayers && (!existing || !existing.MenuPrefab)) return true;
            CharacterMaster cook = CookFor(activator);
            if (!cook || !cook.inventory) return false;
            ChefQueue chef = QueueFor(controller);
            if (!chef.Menus.TryGetValue(cook, out MenuProxy menu) || !menu)
            {
                GameObject instance = UnityEngine.Object.Instantiate(chef.MenuPrefab ? chef.MenuPrefab : MenuPrefab,
                    controller.transform.position, controller.transform.rotation);
                menu = instance.GetComponent<MenuProxy>();
                menu.Owner = cook;
                menu.Chef = chef;
                chef.PrepareMenu?.Invoke(menu);
                instance.SetActive(true);
                chef.Menus[cook] = menu;
                NetworkServer.Spawn(instance);
            }
            else
            {
                var prompt = menu.GetComponentInChildren<NetworkUIPromptController>(true);
                if (menu.Pending || prompt.inUse) return false;
                chef.PrepareMenu?.Invoke(menu);
            }

            var picker = menu.GetComponentInChildren<PickupPickerController>(true);
            picker.SetAvailable(true);
            picker.OnInteractionBegin(activator);
            return false;
        }

        private static void HideMenuProxy(PickupPickerController __instance, ref bool __result)
        { if (__instance.GetComponentInParent<MenuProxy>()) __result = false; }

        private static void IgnoreMenuProxy(PickupPickerController __instance, ref bool __result)
        { if (__instance.GetComponentInParent<MenuProxy>()) __result = true; }

        internal sealed class CookingCall
        {
            internal ChefQueue Chef;
            internal MenuProxy Menu;
            internal object Machine;
            internal Meal Meal;
            internal bool Committed;
        }

        private static bool BeforeCooking(Component __instance, Interactor activator, PickupIndex[] itemsToTake,
            PickupIndex reward, int count, out CookingCall __state)
        {
            __state = null;
            if (!NetworkServer.active || !Active) return true;
            MenuProxy menu = __instance.GetComponentInParent<MenuProxy>();
            if (!menu && !MultiplePlayers && !MealsToInventory) return true;
            CharacterMaster cook = CookFor(activator);
            CharacterBody body = activator ? activator.GetComponent<CharacterBody>() : null;
            if (!cook || !cook.inventory || !body || count <= 0 || itemsToTake == null || PickupCatalog.GetPickupDef(reward) == null) return false;
            if (menu && (menu.Owner != cook || menu.Pending || !menu.Chef || (!menu.Chef.MenuPrefab && !MultiplePlayers))) return false;
            ChefQueue chef = menu ? menu.Chef : QueueFor(__instance);
            if (!menu && chef.Current != null) return false;
            if (!(bool)isAffordable.Invoke(__instance, new object[] { itemsToTake, body })) return false;
            var meal = new Meal { Cook = cook, Reward = reward, Count = count, Ingredients = new List<PickupIndex>(itemsToTake), Menu = menu };
            __state = new CookingCall { Chef = chef, Menu = menu, Meal = meal };
            if (menu)
            {
                menu.Pending = true;
                __state.Machine = Read(__instance, "esm");
                // Let the native controller charge the ingredients and create their orbs.
                // Its menu proxy must not start another cooking animation of its own.
                Write(__instance, "esm", null);
            }
            else chef.Current = meal;
            return true;
        }

        private static void AfterCooking(Component __instance, CookingCall __state)
        {
            if (__state == null) return;
            __state.Committed = true;
            if (!__state.Menu) return;
            Write(__instance, "esm", __state.Machine);
            __state.Chef.Waiting.Enqueue(__state.Meal);
            var picker = __instance.GetComponent<PickupPickerController>();
            picker.SetAvailable(false);
            try { __instance.GetComponent<NetworkUIPromptController>().SetParticipantMaster(null); }
            catch (Exception e) { Log.Warning("Could not close the Chef menu: " + e.Message); }
        }

        private static void FinishCookingCall(Component __instance, CookingCall __state)
        {
            if (__state == null) return;
            if (__state.Menu) Write(__instance, "esm", __state.Machine);
            if (__state.Committed) return;
            if (__state.Menu) __state.Menu.Pending = false;
            else if (__state.Chef.Current == __state.Meal) __state.Chef.Current = null;
        }

        internal sealed class DropScope
        {
            internal Meal Previous, Meal;
            internal int NativeDrops;
        }

        private static void BeforeDrop(EntityState __instance, out DropScope __state)
        {
            __state = null;
            if (!NetworkServer.active || !__instance.outer) return;
            ChefQueue chef = __instance.outer.GetComponent<ChefQueue>();
            if (!chef || chef.Current == null) return;
            __state = new DropScope { Previous = activeDrop, Meal = chef.Current, NativeDrops = (int)Read(__instance, "itemsDropped") };
            activeDrop = chef.Current;
        }

        private static void AfterDrop(EntityState __instance, DropScope __state)
        {
            if (__state == null) return;
            if ((int)Read(__instance, "itemsDropped") > __state.NativeDrops) ++__state.Meal.Sent;
            activeDrop = __state.Previous;
        }

        private static bool DeliverMeal(PickupIndex pickupIndex)
        {
            if (activeDrop == null || activeDrop.Reward != pickupIndex || !MealsToInventory) return true;
            Meal meal = activeDrop;
            activeDrop = null;
            try { return !GrantMeal(meal.Cook, pickupIndex); }
            finally { activeDrop = meal; }
        }

        internal static bool GrantMeal(CharacterMaster cook, PickupIndex pickup)
        {
            PickupDef definition = PickupCatalog.GetPickupDef(pickup);
            if (!NetworkServer.active || !cook || !cook.inventory || definition == null || definition.itemIndex == ItemIndex.None || definition.equipmentIndex != EquipmentIndex.None) return false;
            cook.inventory.GiveItemPermanent(definition.itemIndex, 1);
            try { GenericPickupController.SendPickupMessage(cook, new UniquePickup(pickup)); }
            catch (Exception e) { Log.Warning("Could not show a cooked meal pickup notification: " + e.Message); }
            return true;
        }

        private static void AfterIdle(EntityState __instance)
        {
            if (!NetworkServer.active || __instance.GetType() != idleType || !__instance.outer) return;
            ChefQueue chef = __instance.outer.GetComponent<ChefQueue>();
            if (!chef || chef.Current == null) return;
            if (chef.Current.Menu)
            {
                chef.Current.Menu.Pending = false;
                chef.Current.Menu.GetComponentInChildren<PickupPickerController>(true).SetAvailable(true);
            }
            chef.Current = null;
        }

        private static void CompleteRemaining(Meal meal, Vector3 position)
        {
            for (int i = meal.Sent; i < meal.Count; ++i)
                if (!MealsToInventory || !GrantMeal(meal.Cook, meal.Reward))
                {
                    var info = new GenericPickupController.CreatePickupInfo
                    {
                        pickup = new UniquePickup(meal.Reward), position = position + Vector3.up * 2f,
                        rotation = Quaternion.identity
                    };
                    PickupDropletController.CreatePickupDroplet(info, info.position,
                        Quaternion.AngleAxis(i * 137.5f, Vector3.up) * new Vector3(3f, 12f, 0f));
                }
        }

        private static object Read(object target, string field)
        { return AccessTools.Field(target.GetType(), field).GetValue(target); }

        private static void Write(object target, string field, object value)
        { AccessTools.Field(target.GetType(), field).SetValue(target, value); }
    }
}
