using System;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine.Events;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    // Logs how Wandering Chef menu proxies handle ingredient selections, so the
    // Bazaar Chef's restricted menu can be compared with the normal Chef's menu.
    internal static class ChefMenuDiagnostics
    {
        private static ConfigEntry<bool> enabled;
        private static Type restrictedMenu;
        private static FieldInfo possibleRecipes;

        internal static void Init()
        {
            enabled = C.BindBool("Debug - Wandering Chef", "Log Menu Selections", true,
                "Log ingredient selections, slot changes and recipe filtering for Wandering Chef menus.");
            restrictedMenu = typeof(Bazaar.BazaarWanderingChef).GetNestedType("RestrictedMenu", BindingFlags.NonPublic);
            possibleRecipes = A.Field(typeof(CraftingController), "_possibleRecipes");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Debug.ChefMenuDiagnostics");
            Patch(harmony, "HandlePickupSelected", prefix: nameof(BeforeSelection));
            Patch(harmony, "UpdateSlot", postfix: nameof(AfterSlot));
            Patch(harmony, "ClearSlot", prefix: nameof(BeforeClear));
            Patch(harmony, "AttemptFindPossibleRecipes", postfix: nameof(AfterRecipes));
        }

        private static void Patch(Harmony harmony, string method, string prefix = null, string postfix = null)
        {
            MethodInfo target = A.Method(typeof(CraftingController), method);
            if (target == null)
            {
                Log.Warning("Chef menu diagnostics: CraftingController." + method + " not found.");
                return;
            }
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(ChefMenuDiagnostics), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(ChefMenuDiagnostics), postfix));
        }

        // Only menus created by the Wandering Chef changes are logged.
        private static string Label(CraftingController crafter)
        {
            if (!C.Get(enabled) || !crafter || !crafter.GetComponentInParent<Interactables.WanderingChef.MenuProxy>()) return null;
            bool bazaar = restrictedMenu != null && crafter.GetComponentInParent(restrictedMenu);
            return "Chef menu [" + (bazaar ? "Bazaar" : "Normal") + " #" + crafter.GetInstanceID() +
                (NetworkServer.active ? " server" : " client") + "]: ";
        }

        private static string Name(PickupIndex pickup)
        {
            PickupDef def = PickupCatalog.GetPickupDef(pickup);
            return def != null ? def.internalName : pickup.ToString();
        }

        private static string Listeners(UnityEventBase unityEvent)
        {
            if (unityEvent == null) return "null";
            var text = new StringBuilder();
            int count = unityEvent.GetPersistentEventCount();
            text.Append(count).Append(" persistent");
            for (int i = 0; i < count; i++)
            {
                UnityEngine.Object target = unityEvent.GetPersistentTarget(i);
                text.Append(i == 0 ? " (" : ", ")
                    .Append(target ? target.GetType().Name + " #" + target.GetInstanceID() : "missing target")
                    .Append('.').Append(unityEvent.GetPersistentMethodName(i));
            }
            if (count > 0) text.Append(')');
            return text.ToString();
        }

        private static void BeforeSelection(CraftingController __instance, int choiceIndex)
        {
            string label = Label(__instance);
            if (label == null) return;
            var options = __instance.options;
            string option = (uint)choiceIndex < options.Length
                ? Name(options[choiceIndex].pickup.pickupIndex) + ", available=" + options[choiceIndex].available
                : "out of range";
#pragma warning disable CS0618
            string legacy = Listeners(__instance.onPickupSelected);
#pragma warning restore CS0618
            Log.Info(label + "selected choice " + choiceIndex + " of " + options.Length + " (" + option +
                "). onPickupSelected: " + legacy + "; onUniquePickupSelected: " + Listeners(__instance.onUniquePickupSelected) + ".");
        }

        private static void AfterSlot(CraftingController __instance, PickupIndex pickupIndex, int slot)
        {
            string label = Label(__instance);
            if (label != null) Log.Info(label + "slot " + slot + " = " + Name(pickupIndex) + ".");
        }

        private static void BeforeClear(CraftingController __instance, int index)
        {
            string label = Label(__instance);
            if (label == null) return;
            // Name the callers so an unexpected reset can be traced.
            var callers = new StringBuilder();
            StackFrame[] frames = new StackTrace(1, false).GetFrames() ?? Array.Empty<StackFrame>();
            for (int i = 0; i < frames.Length && i < 5; i++)
            {
                MethodBase method = frames[i].GetMethod();
                if (method == null) continue;
                if (callers.Length > 0) callers.Append(" <- ");
                callers.Append(method.DeclaringType != null ? method.DeclaringType.Name + "." : "").Append(method.Name);
            }
            Log.Info(label + "clearing slot " + index + " (called from " + callers + ").");
        }

        private static void AfterRecipes(CraftingController __instance)
        {
            string label = Label(__instance);
            if (label == null) return;
            var recipes = possibleRecipes?.GetValue(__instance) as CraftableCatalog.RecipeEntry[];
            var ingredients = __instance.ingredients;
            var text = new StringBuilder();
            for (int i = 0; ingredients != null && i < ingredients.Length; i++)
                text.Append(i == 0 ? "" : ", ").Append(ingredients[i] == PickupIndex.none ? "empty" : Name(ingredients[i]));
            Log.Info(label + (recipes?.Length.ToString() ?? "?") + " possible recipe(s) for ingredients [" + text +
                "]; best fit " + (__instance.bestFitRecipe != null ? Name(__instance.bestFitRecipe.result) : "none") + ".");
        }
    }
}
