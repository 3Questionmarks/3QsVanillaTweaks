using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class CollectorsCompulsion
    {
        internal enum Reward
        {
            False,
            Minor,
            True
        }

        static UncommonConfig c;
        static ConfigEntry<Reward> collectibles, barrels, temporary;
        static BuffDef full, minor;
        sealed class Totals
        {
            internal int Full, Minor;
        }

        static ConditionalWeakTable<CharacterMaster, Totals> totals = new ConditionalWeakTable<CharacterMaster, Totals>();
        [ThreadStatic]
        static string category;
        [ThreadStatic]
        static bool printerCreation;
        internal sealed class PrinterOrigin : MonoBehaviour
        {
        }

        internal static void Init()
        {
            c = new UncommonConfig("Collector's Compulsion", "SpeedOnPickup", true);
            c.Pair("Stat Increase (%)", .5f, .25f, "Stat bonus per full pickup. Vanilla: 3% per pickup, with no increase to this amount from item stacks.");
            c.N("Duration", -1, "Buff duration in seconds. -1 lasts for the entire stage. Vanilla: 20 seconds.", -1);
            c.N("Maximum Stacks", -1, "Maximum combined full and minor pickup stacks. -1 is unlimited. Vanilla: 3.", -1);
            c.N("Maximum Stacks Per Item Stack", 0, "Extra maximum stacks per additional item, if a cap is enabled. Vanilla: 2.");
            c.N("Minor Reduction (%)", 50, "Percentage removed from the stat bonus for minor awards. Vanilla minor pickups grant the full stat bonus.", 0, 100);
            collectibles = C.Bind(c.Section, "Allow Item And Skill Collectibles", Reward.False, new ConfigDescription("Allow item/skill-generated collectibles such as ammo packs, treasure, junk, antler orbs and food. Minor grants the reduced bonus."));
            barrels = C.Bind(c.Section, "Opening Barrels", Reward.Minor, new ConfigDescription("Bonus when opening a barrel. Minor grants the reduced bonus."));
            temporary = C.Bind(c.Section, "Temporary Items", Reward.Minor, new ConfigDescription("Bonus when collecting a temporary item. Minor grants the reduced bonus."));
            c.B("Allow Equipment", false, "Grant a full bonus for equipment pickups.");
            c.B("Allow Printer Items", false, "Allow items produced by 3D printers. Other category restrictions still apply.");
            c.B("Lunar Coins", true, "Grant a full bonus for physically collected Lunar Coins.");
            c.B("Scrap", false, "Grant a bonus for scrap pickups.");
            c.B("Also Increase Acceleration", true, "Include acceleration among the increased stats.");
            full = U.Buff("Collector's Compulsion", false, () => DLC3Content.Buffs.SpeedOnPickup, () => "Each stack increases all stats by " + c.V("Stat Increase (%)", "%", "cIsUtility") + ".");
            minor = U.Buff("Collector's Compulsion (Minor)", false, () => DLC3Content.Buffs.SpeedOnPickup, () => "Each stack grants " + (100 - c.F("Minor Reduction (%)")) + "% of a full pickup's stat bonus.");
            c.Suppress("RoR2.CharacterBody", "OnPickup");
            c.Suppress("RoR2.GlobalEventManager", "OnInteractionBegin");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(CharacterBody), "OnPickup", typeof(CollectorsCompulsion), postfix: nameof(Pickup));
                U.Patch(typeof(GenericPickupController), "AttemptGrant", typeof(CollectorsCompulsion), prefix: nameof(Grant));
                Finalize(typeof(GenericPickupController), "AttemptGrant", nameof(EndGrant));
                U.Patch(typeof(BarrelInteraction), "OnInteractionBegin", typeof(CollectorsCompulsion), prefix: nameof(BarrelBefore), postfix: nameof(BarrelAfter));
                U.Patch(typeof(BarrelInteraction), "DropPickupIfSpeedOnPickup", typeof(CollectorsCompulsion), prefix: nameof(NativeBarrel));
                S.Patch(U.Harmony, typeof(PickupDropletController), "CreatePickupDroplet", typeof(CollectorsCompulsion), prefix: nameof(DropBefore), args: new[] { typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3) });
                var drop = A.Method(typeof(PickupDropletController), "CreatePickupDroplet", new[] { typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3) });
                U.Harmony.Patch(drop, finalizer: new HarmonyMethod(typeof(CollectorsCompulsion), nameof(DropAfter)));
                U.Patch(typeof(PickupDropletController), "CreatePickup", typeof(CollectorsCompulsion), prefix: nameof(TransferBefore));
                Finalize(typeof(PickupDropletController), "CreatePickup", nameof(TransferAfter));
                S.Patch(U.Harmony, typeof(GenericPickupController), "CreatePickup", typeof(CollectorsCompulsion), postfix: nameof(Created), args: new[] { typeof(GenericPickupController.CreatePickupInfo).MakeByRefType() });
            }

            if (c.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(CollectorsCompulsion), nameof(DurabilityIL)));
            U.Ensure();
            U.Stats += Stats;
            U.Tick += Sync;
            Stage.onStageStartGlobal += stage =>
            {
                if (!NetworkServer.active)
                    return;
                totals = new ConditionalWeakTable<CharacterMaster, Totals>();
                foreach (var b in CharacterBody.readOnlyInstancesList)
                    if (b)
                    {
                        b.ClearAllBuffs(full);
                        b.ClearAllBuffs(minor);
                    }
            };
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => c.Item ? c.Item.itemIndex : ItemIndex.None, (master, n, text) =>
            {
                var b = master ? master.GetBody() : null;
                float value = b ? Bonus(b, n) : 0;
                return Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue("", "Current Stat Bonus", value, "%", "#E5C962");
            }, () => c.Active);
            c.Text(() => "Collecting items grants " + c.V("Stat Increase (%)", "%", "cIsUtility") + " to <style=cIsUtility>all stats</style>" + (c.F("Duration") < 0 ? " for the rest of the stage" : " for " + c.V("Duration", "s", "cIsUtility")) + "." + (c.F("Maximum Stacks") >= 0 ? " Stacks up to " + c.F("Maximum Stacks") + " times." : "") + " Minor pickups grant " + (100 - c.F("Minor Reduction (%)")) + "% of this bonus.", () => "Collecting items grants stacking bonuses to <style=cIsUtility>all stats</style>.");
        }

        static void Finalize(Type type, string method, string finalizer) => U.Harmony.Patch(A.Method(type, method), finalizer: new HarmonyMethod(typeof(CollectorsCompulsion), finalizer));
        static void Grant(GenericPickupController __instance, out string __state)
        {
            __state = category;
            category = "collectible";
            var def = PickupCatalog.GetPickupDef(__instance.pickup.pickupIndex);
            if (def == null)
                return;
            if (def.coinValue > 0)
                category = "coin";
            else if (def.equipmentIndex != EquipmentIndex.None)
                category = "equipment";
            else if (def.itemIndex != ItemIndex.None)
            {
                var item = ItemCatalog.GetItemDef(def.itemIndex);
                category = item && item.name == "Junk" ? "collectible" : item && (item.ContainsTag(ItemTag.Scrap) || item.ContainsTag(ItemTag.PriorityScrap)) ? "scrap" : __instance.pickup.isTempItem ? "temporary" : "item";
            }

            if (__instance.GetComponent<PrinterOrigin>() && !c.B("Allow Printer Items"))
                category = "blocked";
        }

        static Exception EndGrant(string __state, Exception __exception)
        {
            category = __state;
            return __exception;
        }

        static void Pickup(CharacterBody __instance, CharacterBody.PickupClass pickupClass)
        {
            if (!NetworkServer.active || c.Count(__instance) == 0)
                return;
            string kind = category ?? (pickupClass == CharacterBody.PickupClass.Minor ? "collectible" : pickupClass == CharacterBody.PickupClass.TempItem ? "temporary" : "collectible");
            Reward reward = kind == "item" ? Reward.True : kind == "coin" ? (c.B("Lunar Coins") ? Reward.True : Reward.False) : kind == "equipment" ? (c.B("Allow Equipment") ? Reward.True : Reward.False) : kind == "scrap" ? (c.B("Scrap") ? Reward.True : Reward.False) : kind == "temporary" ? C.Get(temporary) : kind == "collectible" ? C.Get(collectibles) : Reward.False;
            Award(__instance, reward);
        }

        static void Award(CharacterBody b, Reward reward)
        {
            int n = c.Count(b);
            if (!NetworkServer.active || n == 0 || reward == Reward.False)
                return;
            if (c.F("Duration") > 0)
            {
                b.SetTimedBuffDurationIfPresent(full, c.F("Duration"), true);
                b.SetTimedBuffDurationIfPresent(minor, c.F("Duration"), true);
            }

            int count = b.GetBuffCount(full) + b.GetBuffCount(minor);
            float cap = c.F("Maximum Stacks");
            if (cap >= 0 && count >= cap + (n - 1) * c.F("Maximum Stacks Per Item Stack"))
                return;
            var buff = reward == Reward.Minor ? minor : full;
            if (c.F("Duration") < 0)
            {
                b.AddBuff(buff);
                if (b.master)
                {
                    var state = totals.GetOrCreateValue(b.master);
                    state.Full = b.GetBuffCount(full);
                    state.Minor = b.GetBuffCount(minor);
                }
            }
            else if (c.F("Duration") > 0)
                b.AddTimedBuff(buff, c.F("Duration"));
        }

        static void Sync(CharacterBody b)
        {
            if (c.Count(b) == 0 || c.F("Duration") >= 0 || !b.master || !totals.TryGetValue(b.master, out var state))
                return;
            if (b.GetBuffCount(full) != state.Full)
                b.SetBuffCount(full.buffIndex, state.Full);
            if (b.GetBuffCount(minor) != state.Minor)
                b.SetBuffCount(minor.buffIndex, state.Minor);
        }

        static float Bonus(CharacterBody b, int n) => c.Total("Stat Increase (%)", n) * (b.GetBuffCount(full) + b.GetBuffCount(minor) * (1 - c.F("Minor Reduction (%)") / 100));
        // Apply capacity bonuses before vanilla computes barrier/hull capacity
        // and reconciles current health/shield against the previous maxima.
        // Applying these in the shared postfix makes vanilla see smaller maxima
        // on the next recalculation and can repeatedly clamp or refill the pools.
        static void Durability(CharacterBody body)
        {
            int n = c.Count(body);
            if (n == 0) return;
            float bonus = Bonus(body, n);
            U.Scale(body, "maxHealth", bonus);
            U.Scale(body, "maxShield", bonus);
        }
        static IEnumerable<CodeInstruction> DurabilityIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int found = 0;
            for (int i = 6; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo setter) || setter.DeclaringType != typeof(CharacterBody) || setter.Name != "set_maxBarrier") continue;
                if (code[i - 1].opcode != OpCodes.Add || !(code[i - 2].operand is MethodInfo shield) || shield.Name != "get_maxShield" || !(code[i - 4].operand is MethodInfo health) || health.Name != "get_maxHealth" || code[i - 3].opcode != OpCodes.Ldarg_0 || code[i - 5].opcode != OpCodes.Ldarg_0 || code[i - 6].opcode != OpCodes.Ldarg_0) continue;
                int start = i - 6;
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[start].labels); code[start].labels.Clear();
                load.blocks.AddRange(code[start].blocks); code[start].blocks.Clear();
                code.InsertRange(start, new[] { load, new CodeInstruction(OpCodes.Call, A.Method(typeof(CollectorsCompulsion), nameof(Durability))) });
                found++; i += 2;
            }
            if (found != 1) throw new InvalidOperationException("Collector's Compulsion capacity anchor changed: " + found);
            return code;
        }

        static void Stats(CharacterBody b)
        {
            int n = c.Count(b);
            if (n == 0)
                return;
            float bonus = Bonus(b, n);
            foreach (string stat in new[]
            {
                "regen",
                "damage",
                "crit",
                "attackSpeed",
                "moveSpeed",
                "armor"
            }

            )
                U.Scale(b, stat, bonus);
            if (c.B("Also Increase Acceleration"))
                U.Scale(b, "acceleration", bonus);
        }

        static bool NativeBarrel() => !c.Active;
        static void BarrelBefore(BarrelInteraction __instance, out bool __state) => __state = __instance.Networkopened;
        static void BarrelAfter(BarrelInteraction __instance, Interactor activator, bool __state)
        {
            if (c.Active && !__state && __instance.Networkopened && activator)
                Award(activator.GetComponent<CharacterBody>(), C.Get(barrels));
        }

        sealed class DropState
        {
            internal bool Previous;
            internal HashSet<int> Existing;
        }

        static void DropBefore(out DropState __state)
        {
            __state = new DropState
            {
                Previous = printerCreation
            };
            if (!c.Active)
                return;
            printerCreation = printerCreation || new StackTrace().GetFrames().Any(f => (f.GetMethod()?.DeclaringType?.FullName ?? "").IndexOf("Duplicator", StringComparison.OrdinalIgnoreCase) >= 0);
            if (printerCreation)
                __state.Existing = new HashSet<int>(UnityEngine.Object.FindObjectsOfType<PickupDropletController>().Select(p => p.GetInstanceID()));
        }

        static Exception DropAfter(DropState __state, Exception __exception)
        {
            if (__state.Existing != null)
                foreach (var p in UnityEngine.Object.FindObjectsOfType<PickupDropletController>())
                    if (!__state.Existing.Contains(p.GetInstanceID()) && !p.GetComponent<PrinterOrigin>())
                        p.gameObject.AddComponent<PrinterOrigin>();
            printerCreation = __state.Previous;
            return __exception;
        }

        static void TransferBefore(PickupDropletController __instance, out bool __state)
        {
            __state = printerCreation;
            printerCreation = __instance.GetComponent<PrinterOrigin>();
        }

        static Exception TransferAfter(bool __state, Exception __exception)
        {
            printerCreation = __state;
            return __exception;
        }

        static void Created(GenericPickupController __result)
        {
            if (printerCreation && __result && !__result.GetComponent<PrinterOrigin>())
                __result.gameObject.AddComponent<PrinterOrigin>();
        }
    }
}
