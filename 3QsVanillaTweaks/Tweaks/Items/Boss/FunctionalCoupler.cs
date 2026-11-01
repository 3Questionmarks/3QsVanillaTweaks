using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class FunctionalCoupler
    {
        static R c;
        sealed class PassiveState { internal readonly Dictionary<BuffDef, int> Counts = new Dictionary<BuffDef, int>(); }
        static readonly ConditionalWeakTable<CharacterBody, PassiveState> states = new ConditionalWeakTable<CharacterBody, PassiveState>();

        internal static void Init()
        {
            c = BossItemSupport.Config("Functional Coupler", "ExtraEquipment", true);
            c.B("Extra Slot Equipment Passives", true, "Equipment passive buffs also apply while that equipment is in an inactive equipment set.");
            U.Ensure();
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "OnInventoryChanged"), postfix: new HarmonyMethod(typeof(FunctionalCoupler), nameof(RefreshPassives)));
            U.Harmony.Patch(A.Method(typeof(CharacterMaster), "TryReviveOnBodyDeath"), prefix: new HarmonyMethod(typeof(FunctionalCoupler), nameof(SelectSeed)), postfix: new HarmonyMethod(typeof(FunctionalCoupler), nameof(RestoreSeedSlot)));
            U.Harmony.Patch(A.Method(typeof(RoR2.Items.MultiShopCardUtils), "OnPurchase"), postfix: new HarmonyMethod(typeof(FunctionalCoupler), nameof(ExtraExecutiveCard)));
            c.Text(() => "Gain an additional equipment slot. Passive equipment effects apply from inactive slots.", () => "Gain an additional equipment slot.");
        }

        struct SeedSlotState
        {
            internal Inventory Inventory;
            internal EquipmentLocation Previous;
            internal bool Changed;
        }

        static void SelectSeed(CharacterMaster __instance, out SeedSlotState __state)
        {
            __state = default(SeedSlotState);
            if (!c.Active || !c.B("Extra Slot Equipment Passives") || !__instance || !__instance.inventory) return;
            var inventory = __instance.inventory;
            if (inventory.currentEquipmentIndex == DLC2Content.Equipment.HealAndRevive.equipmentIndex) return;
            for (uint slot = 0; slot < inventory.equipmentSlotCount; slot++)
            for (uint set = 0; set < inventory.accessibleEquipmentSetCount; set++)
            {
                EquipmentLocation location = new EquipmentLocation { slot = slot, set = set };
                if (location.slot == inventory.activeEquipmentLocation.slot && location.set == inventory.activeEquipmentLocation.set) continue;
                if (inventory.GetEquipment(in location).equipmentIndex != DLC2Content.Equipment.HealAndRevive.equipmentIndex) continue;
                var previous = inventory.activeEquipmentLocation;
                if (!inventory.TrySetActiveEquipmentLocation(in location)) return;
                __state = new SeedSlotState { Inventory = inventory, Previous = previous, Changed = true };
                return;
            }
        }

        static void RestoreSeedSlot(SeedSlotState __state)
        {
            if (__state.Changed && __state.Inventory)
                __state.Inventory.TrySetActiveEquipmentLocation(in __state.Previous);
        }

        static void ExtraExecutiveCard(CostTypeDef.PayCostContext context, int moneyCost)
        {
            if (!NetworkServer.active || !c.Active || !c.B("Extra Slot Equipment Passives") || !context.activatorMaster || !context.activatorMaster.hasBody) return;
            var inventory = context.activatorMaster.inventory;
            var body = context.activatorMaster.GetBody();
            if (!inventory || !body || inventory.currentEquipmentIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex) return;
            EquipmentLocation cardLocation = default(EquipmentLocation);
            bool found = false;
            for (uint slot = 0; slot < inventory.equipmentSlotCount && !found; slot++)
            for (uint set = 0; set < inventory.accessibleEquipmentSetCount; set++)
            {
                EquipmentLocation location = new EquipmentLocation { slot = slot, set = set };
                if (location.slot == inventory.activeEquipmentLocation.slot && location.set == inventory.activeEquipmentLocation.set) continue;
                var state = inventory.GetEquipment(in location);
                if (state.equipmentIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex && state.charges > 0)
                { cardLocation = location; found = true; break; }
            }
            if (!found || inventory.GetEquipmentDisabled()) return;

            Util.NullifyIfInvalid<GameObject>(ref context.purchasedObject);
            bool spent = false;
            if (moneyCost > 0)
            {
                var orb = new GoldOrb
                {
                    origin = context.purchasedObject ? context.purchasedObject.transform.position : body.corePosition,
                    target = body.mainHurtBox,
                    goldAmount = (uint)(0.1f * moneyCost)
                };
                OrbManager.instance.AddOrb(orb);
                spent = true;
            }
            var purchased = context.purchasedObject;
            var terminal = purchased ? purchased.GetComponent<ShopTerminalBehavior>() : null;
            if (terminal && terminal.serverMultiShopController)
            {
                terminal.serverMultiShopController.SetCloseOnTerminalPurchase(purchased.GetComponent<PurchaseInteraction>(), false);
                spent = true;
            }
            var drone = purchased ? purchased.GetComponent<DroneVendorTerminalBehavior>() : null;
            if (drone && drone.serverMultiShopController)
            {
                drone.serverMultiShopController.SetCloseOnTerminalPurchase(purchased.GetComponent<PurchaseInteraction>(), false);
                spent = true;
            }
            if (spent)
            {
                var old = inventory.activeEquipmentLocation;
                if (inventory.TrySetActiveEquipmentLocation(in cardLocation))
                {
                    body.equipmentSlot.OnEquipmentExecuted();
                    inventory.TrySetActiveEquipmentLocation(in old);
                }
            }
        }

        static void RefreshPassives(CharacterBody __instance)
        {
            CharacterBody body = __instance;
            if (!NetworkServer.active || !body || !body.inventory) return;
            var state = states.GetOrCreateValue(body);
            var wanted = new Dictionary<BuffDef, int>();
            if (c.Active && c.B("Extra Slot Equipment Passives"))
            {
                var inv = body.inventory;
                for (uint slot = 0; slot < inv.equipmentSlotCount; slot++)
                for (uint set = 0; set < inv.accessibleEquipmentSetCount; set++)
                {
                    if (slot == inv.activeEquipmentSlot && set == inv.activeEquipmentLocation.set) continue;
                    EquipmentLocation location = new EquipmentLocation { slot = slot, set = set };
                    var equipment = inv.GetEquipment(in location);
                    var def = EquipmentCatalog.GetEquipmentDef(equipment.equipmentIndex);
                    if (!def || def.passiveBuffDef == null) continue;
                    wanted.TryGetValue(def.passiveBuffDef, out int count);
                    wanted[def.passiveBuffDef] = count + 1;
                }
            }
            foreach (var pair in state.Counts)
            {
                wanted.TryGetValue(pair.Key, out int target);
                for (int i = target; i < pair.Value; i++) body.RemoveBuff(pair.Key);
            }
            foreach (var pair in wanted)
            {
                state.Counts.TryGetValue(pair.Key, out int old);
                for (int i = old; i < pair.Value; i++) body.AddBuff(pair.Key);
            }
            state.Counts.Clear();
            foreach (var pair in wanted) state.Counts[pair.Key] = pair.Value;
        }
    }
}
