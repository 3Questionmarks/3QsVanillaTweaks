using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using RoR2.UI;

namespace Tweaks.Items.Common
{
    internal static class CommonItemSupport
    {
        internal static event Action<CharacterBody> Stats;
        internal static event Action<DamageReport> Hit;
        private static bool ready;
        internal static readonly Dictionary<string, Func<bool>> Suppressed = new Dictionary<string, Func<bool>>();
        internal static readonly Dictionary<string, Func<bool>> ScrapAllowed = new Dictionary<string, Func<bool>>();
        internal static readonly Dictionary<string, ItemTier> ScrapTiers = new Dictionary<string, ItemTier>();
        internal static string Section(string name) => "Items - 01 Common - " + name;
        internal static ConfigEntry<bool> Enable(string section) => C.BindStartupBool(section, "Enabled", true, "Enable this item's configurable changes. Restart to apply.");
        internal static ConfigEntry<float> Num(string section, string key, float value, string description, float min = 0f, float max = 100000f) => C.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));
        internal static ConfigEntry<int> Int(string section, string key, int value, string desc, int min = 0, int max = 10000) => C.Bind(section, key, value, new ConfigDescription(desc, new AcceptableValueRange<int>(min, max)));
        internal static bool Active(ConfigEntry<bool> e) => e != null && C.Get(e);
        internal static float Linear(int n, float first, float extra) => n > 0 ? first + (n - 1) * extra : 0f;
        internal static int Count(CharacterBody body, ItemDef item) => body && body.inventory && item ? body.inventory.GetItemCountEffective(item) : 0;
        // Rental APIs differ between RoR2 reference versions. Resolve them once,
        // without requiring a particular member in the compile-time assembly.
        static readonly MethodInfo rentalCount = new[]
        {
            "GetItemCountRented",
            "GetItemCountRental",
            "GetItemCountRent"
        }.Select(name => A.Method(typeof(Inventory), name, new[] { typeof(ItemIndex) })).FirstOrDefault(method => method != null && method.ReturnType == typeof(int));
        static readonly MethodInfo totalCount = A.Method(typeof(Inventory), "GetItemCountTotal", new[] { typeof(ItemIndex) });
        internal static int RentalCount(Inventory inventory, ItemIndex item) => inventory && rentalCount != null ? (int)rentalCount.Invoke(inventory, new object[] { item }) : 0;
        internal static int TotalHeld(Inventory inventory, ItemIndex item)
        {
            if (!inventory)
                return 0;
            if (totalCount != null && totalCount.ReturnType == typeof(int))
                return (int)totalCount.Invoke(inventory, new object[] { item });
            return inventory.GetItemCountPermanent(item) + inventory.GetItemCountTemp(item) + RentalCount(inventory, item);
        }

        internal static float Total(ConfigEntry<float> first, ConfigEntry<float> extra, int n) => Linear(n, C.Get(first), C.Get(extra));
        // Stat properties are read and written from RecalculateStats for every body.
        // Cache compiled accessors instead of resolving the property on each call.
        sealed class StatAccess
        {
            internal Func<CharacterBody, float> Get;
            internal Action<CharacterBody, float> Set;
        }
        static readonly Dictionary<string, StatAccess> statAccess = new Dictionary<string, StatAccess>();
        static StatAccess Stat(string property)
        {
            if (statAccess.TryGetValue(property, out StatAccess access))
                return access;
            PropertyInfo info = A.Property(typeof(CharacterBody), property);
            if (info == null)
                throw new MissingMemberException(typeof(CharacterBody).FullName, property);
            access = new StatAccess
            {
                Get = b => (float)info.GetValue(b, null),
                Set = (b, value) => info.SetValue(b, value, null)
            };
            if (info.PropertyType == typeof(float))
            {
                try
                {
                    MethodInfo getter = info.GetGetMethod(true), setter = info.GetSetMethod(true);
                    if (getter != null && !getter.IsStatic)
                        access.Get = (Func<CharacterBody, float>)Delegate.CreateDelegate(typeof(Func<CharacterBody, float>), getter);
                    if (setter != null && !setter.IsStatic)
                        access.Set = (Action<CharacterBody, float>)Delegate.CreateDelegate(typeof(Action<CharacterBody, float>), setter);
                }
                catch (ArgumentException)
                {
                    // Keep the reflection accessors if a delegate cannot bind to this property.
                }
            }
            statAccess[property] = access;
            return access;
        }
        internal static float GetStat(CharacterBody b, string property) => Stat(property).Get(b);
        internal static void Set(CharacterBody b, string property, float value) => Stat(property).Set(b, value);
        // Only remove zero bonuses. Zero thresholds/durations can carry gameplay
        // meaning and must not be silently removed from their conditions.
        static readonly Regex zeroStack = new Regex(@"\s*<style=cStack>\(\+?0(?:[.,]0+)?(?:%|m|s)? per stack\)</style>", RegexOptions.CultureInvariant);
        internal static string CleanDescription(string text) => zeroStack.Replace(text ?? "", "");
        internal static void Text(string token, Func<string> text) => Tweaks.Survivors.SurvivorSettings.SetText(token, () => CleanDescription(text()));
        internal static Harmony Harmony(string name) => new Harmony("3Questionmarks.3QsVanillaTweaks.Common." + name);
        internal static void Patch(Harmony h, Type type, string name, Type owner, string prefix = null, string postfix = null, string transpiler = null, Type[] args = null)
        {
            var method = args == null ? A.Method(type, name) : A.Method(type, name, args);
            if (method == null)
                throw new MissingMethodException(type.FullName, name);
            h.Patch(method, prefix == null ? null : new HarmonyMethod(owner, prefix), postfix == null ? null : new HarmonyMethod(owner, postfix), transpiler == null ? null : new HarmonyMethod(owner, transpiler));
        }

        internal static void Ensure()
        {
            if (ready)
                return;
            ready = true;
            var h = Harmony("Support");
            Patch(h, typeof(CharacterBody), "RecalculateStats", typeof(CommonItemSupport), postfix: nameof(AfterStats));
            Patch(h, typeof(HealthComponent), "TakeDamageProcess", typeof(CommonItemSupport), transpiler: nameof(SuppressIL));
            Patch(h, typeof(HealthComponent), "UpdateLastHitTime", typeof(CommonItemSupport), transpiler: nameof(SuppressIL));
            Patch(h, typeof(PickupPickerController), "GetGeneratedOptionsFromInteractor", typeof(CommonItemSupport), postfix: nameof(ScrapMenu));
            Patch(h, typeof(ScrapperController), "BeginScrapping", typeof(CommonItemSupport), transpiler: nameof(ScrapIL), args: new[] { typeof(UniquePickup) });
            GlobalEventManager.onServerDamageDealt += report =>
            {
                if (report != null && report.damageDealt > 0f && !report.damageInfo.rejected)
                    Hit?.Invoke(report);
            };
            RoR2Application.onLoad += () => On.RoR2.UI.BuffIcon.UpdateIcon += UpdateIcon;
            C.EffectiveSettingsChanged += () =>
            {
                foreach (var b in CharacterBody.readOnlyInstancesList)
                    if (b)
                        b.RecalculateStats();
            };
        }

        private static void AfterStats(CharacterBody __instance) => Stats?.Invoke(__instance);
        private static int Filter(int count, string name) => Suppressed.TryGetValue(name, out var active) && active() ? 0 : count;
        private static IEnumerable<CodeInstruction> SuppressIL(IEnumerable<CodeInstruction> code)
        {
            var result = new List<CodeInstruction>();
            string pending = null;
            foreach (var i in code)
            {
                result.Add(i);
                if (i.operand is FieldInfo f)
                {
                    if (f.Name == "FragileDamageBonus")
                        pending = f.Name;
                    else if (f.Name == "fragileDamageBonus" || f.Name == "healingPotion")
                    {
                        result.Add(new CodeInstruction(OpCodes.Ldstr, f.Name));
                        result.Add(new CodeInstruction(OpCodes.Call, A.Method(typeof(CommonItemSupport), nameof(Filter))));
                    }
                }

                if (pending != null && i.operand is MethodInfo m && m.Name == "GetItemCountEffective")
                {
                    result.Add(new CodeInstruction(OpCodes.Ldstr, pending));
                    result.Add(new CodeInstruction(OpCodes.Call, A.Method(typeof(CommonItemSupport), nameof(Filter))));
                    pending = null;
                }
            }

            return result;
        }

        private static bool CanScrap(ItemDef item) => item && ScrapAllowed.TryGetValue(item.name, out var f) && f();
        private static ItemTier ScrapTier(ItemDef item) => CanScrap(item) ? (ScrapTiers.TryGetValue(item.name, out var tier) ? tier : ItemTier.Tier1) : item.tier;
        private static IEnumerable<CodeInstruction> ScrapIL(IEnumerable<CodeInstruction> code)
        {
            var getter = A.Property(typeof(ItemDef), "tier").GetGetMethod();
            foreach (var i in code)
            {
                if (i.Calls(getter))
                {
                    i.opcode = OpCodes.Call;
                    i.operand = A.Method(typeof(CommonItemSupport), nameof(ScrapTier));
                }

                yield return i;
            }
        }

        static readonly MethodInfo itemDisabled = A.Method(typeof(Inventory), "IsItemDisabled", new[] { typeof(ItemIndex) });
        static readonly MemberInfo rentalFlag = (MemberInfo)A.Field(typeof(UniquePickup), "isRental") ?? A.Property(typeof(UniquePickup), "isRental");
        static readonly MemberInfo rentalPermission = (MemberInfo)A.Field(typeof(PickupPickerController), "canUseRentalItems") ?? A.Property(typeof(PickupPickerController), "canUseRentalItems");
        static bool Flag(object instance, MemberInfo member)
        {
            if (member is FieldInfo field && field.FieldType == typeof(bool))
                return (bool)field.GetValue(instance);
            if (member is PropertyInfo property && property.PropertyType == typeof(bool) && property.CanRead)
                return (bool)property.GetValue(instance, null);
            return false;
        }

        static bool IsRental(UniquePickup pickup) => Flag(pickup, rentalFlag);
        static bool CanMarkRental => rentalFlag is FieldInfo rentalField && rentalField.FieldType == typeof(bool) && !rentalField.IsInitOnly || rentalFlag is PropertyInfo property && property.PropertyType == typeof(bool) && property.GetSetMethod(true) != null;

        static UniquePickup RentalPickup(PickupIndex index)
        {
            object pickup = new UniquePickup(index);
            if (rentalFlag is FieldInfo field)
                field.SetValue(pickup, true);
            else if (rentalFlag is PropertyInfo property)
                property.SetValue(pickup, true, null);
            return (UniquePickup)pickup;
        }

        static bool Disabled(Inventory inventory, ItemIndex item)
        {
            if (itemDisabled != null && itemDisabled.ReturnType == typeof(bool))
                return (bool)itemDisabled.Invoke(inventory, new object[] { item });
            // Effective stacks already exclude disabled items in versions without
            // an explicit per-item disabler query.
            return TotalHeld(inventory, item) > 0 && inventory.GetItemCountEffective(item) <= 0;
        }

        private static void ScrapMenu(PickupPickerController __instance, Interactor activator, ref List<PickupPickerController.Option> __result)
        {
            if (__result == null || !__instance.GetComponent<ScrapperController>() || !activator)
                return;
            var b = activator.GetComponent<CharacterBody>();
            if (!b || !b.inventory || b.inventory.inventoryDisabled)
                return;
            foreach (var pair in ScrapAllowed)
            {
                if (!pair.Value())
                    continue;
                var item = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(pair.Key));
                if (!item || Disabled(b.inventory, item.itemIndex))
                    continue;
                var index = PickupCatalog.FindPickupIndex(item.itemIndex);
                if (b.inventory.GetItemCountPermanent(item.itemIndex) > 0 && !__result.Any(o => o.pickup.pickupIndex == index && !o.pickup.isTempItem && !IsRental(o.pickup)))
                    __result.Add(new PickupPickerController.Option { available = true, pickup = new UniquePickup(index) });
                if (__instance.canUseTemporaryItems && b.inventory.GetItemCountTemp(item.itemIndex) > 0 && !__result.Any(o => o.pickup.pickupIndex == index && o.pickup.isTempItem))
                    __result.Add(new PickupPickerController.Option { available = true, pickup = new UniquePickup(index) { decayValue = 1f } });
                if (CanMarkRental && Flag(__instance, rentalPermission) && RentalCount(b.inventory, item.itemIndex) > 0 && !__result.Any(o => o.pickup.pickupIndex == index && IsRental(o.pickup)))
                    __result.Add(new PickupPickerController.Option { available = true, pickup = RentalPickup(index) });
            }
        }

        internal sealed class BuffText
        {
            internal string Title;
            internal Func<BuffIcon, string> Text;
        }

        internal static readonly Dictionary<string, BuffText> BuffTexts = new Dictionary<string, BuffText>();
        sealed class TooltipState : MonoBehaviour
        {
            internal BuffDef Buff;
            internal TooltipProvider Provider;
            internal string TitleToken, BodyToken, Title, Body;
            internal bool Applied, Created, Enabled, Raycast;
            internal UnityEngine.UI.Image Image;
            internal void Restore()
            {
                if (!Applied)
                    return;
                if (Provider)
                {
                    Provider.titleToken = TitleToken;
                    Provider.bodyToken = BodyToken;
                    Provider.overrideTitleText = Title;
                    Provider.overrideBodyText = Body;
                    Provider.enabled = Created ? false : Enabled;
                }

                if (Image)
                    Image.raycastTarget = Raycast;
                Applied = false;
            }

            void OnDisable() => Restore();
        }

        static void UpdateIcon(On.RoR2.UI.BuffIcon.orig_UpdateIcon orig, BuffIcon icon)
        {
            var state = icon.GetComponent<TooltipState>();
            if (state && state.Applied && state.Buff != icon.buffDef)
                state.Restore();
            orig(icon);
            if (!icon.buffDef || !BuffTexts.TryGetValue(icon.buffDef.name, out var entry))
                return;
            if (!state)
                state = icon.gameObject.AddComponent<TooltipState>();
            var tooltip = icon.GetComponent<TooltipProvider>();
            if (!tooltip)
            {
                tooltip = icon.gameObject.AddComponent<TooltipProvider>();
                state.Created = true;
            }

            if (!state.Applied)
            {
                state.Buff = icon.buffDef;
                state.Provider = tooltip;
                state.TitleToken = tooltip.titleToken;
                state.BodyToken = tooltip.bodyToken;
                state.Title = tooltip.overrideTitleText;
                state.Body = tooltip.overrideBodyText;
                state.Enabled = tooltip.enabled;
                state.Image = icon.iconImage;
                state.Raycast = icon.iconImage && icon.iconImage.raycastTarget;
                state.Applied = true;
            }

            tooltip.titleToken = "";
            tooltip.bodyToken = "";
            tooltip.overrideTitleText = entry.Title;
            tooltip.overrideBodyText = CleanDescription(entry.Text(icon));
            tooltip.enabled = true;
            if (icon.iconImage)
                icon.iconImage.raycastTarget = true;
        }

        internal static void Regenerate(ItemDef consumed, ItemDef usable)
        {
            if (!NetworkServer.active || !consumed || !usable)
                return;
            foreach (var master in CharacterMaster.readOnlyInstancesList)
            {
                if (!master || !master.inventory)
                    continue;
                int n = master.inventory.GetItemCountPermanent(consumed.itemIndex);
                if (n <= 0)
                    continue;
                master.inventory.RemoveItemPermanent(consumed.itemIndex, n);
                master.inventory.GiveItemPermanent(usable.itemIndex, n);
                CharacterMasterNotificationQueue.SendTransformNotification(master, consumed.itemIndex, usable.itemIndex, CharacterMasterNotificationQueue.TransformationType.RegeneratingScrapRegen);
            }
        }

        internal static void Consume(CharacterBody body, ItemDef usable, ItemDef consumed, int max)
        {
            new Inventory.ItemTransformation
            {
                originalItemIndex = usable.itemIndex,
                newItemIndex = consumed.itemIndex,
                maxToTransform = max,
                minToTransform = 1,
                transformationType = (ItemTransformationTypeIndex)9
            }.TryTransform(body.inventory, out var result);
        }
    }
}
