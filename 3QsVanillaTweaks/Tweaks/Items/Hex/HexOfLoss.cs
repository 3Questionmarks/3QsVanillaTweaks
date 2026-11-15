using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using R2API;
using RoR2;
using RoR2.Items;
using UnityEngine;
using UnityEngine.Networking;
using R=Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Hex
{
    internal static class HexOfLoss
    {
        static R c;
        const string LostItemToken = "3QS_HEX_OF_LOSS_NOTIFICATION";
        sealed class ProcessedTeleporter { }
        static readonly ConditionalWeakTable<TeleporterInteraction, ProcessedTeleporter> processedTeleporters =
            new ConditionalWeakTable<TeleporterInteraction, ProcessedTeleporter>();
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Loss","RoamingItemDisable");
            c.B("Rework",true,"Permanently remove items when a teleporter event begins instead of temporarily disabling items. False restores configurable vanilla behaviour.");
            c.N("Items Removed Per Stack",1,"Permanent item copies removed per Hex stack when a teleporter event begins.");
            c.B("Prioritise Scrap",true,"Remove eligible scrap before other items, starting with the lowest tier. At the same tier, ordinary scrap is used before Regenerating Scrap.");
            c.B("Can Remove Lunar Items",false,"Allow lunar items to be selected.");
            c.B("Can Remove Important Items",false,"Allow removable objective/world-unique items and Prison Matrix/Cerebellums. Hidden, non-removable, NoTier, Hex and Page items remain protected.");
            c.N("Vanilla Disable Interval",30,"Vanilla: 30 seconds.",.1f);
            c.N("Vanilla Disabled Item Types Per Stack (%)",10,"Vanilla: 10% of item types disabled per stack.");
            const string native="RoR2.Items.RoamingItemDisableBehavior";
            c.Literal(native,"FixedUpdate",30,()=>c.F("Vanilla Disable Interval"));
            c.Literal(native,"ResetRoamingDisableTimer",27,()=>Mathf.Max(0,c.F("Vanilla Disable Interval")-3));
            c.Literal(native,"RoamItemDisable",.1f,()=>c.F("Vanilla Disabled Item Types Per Stack (%)")/100,null,1);
            HexMechanics.Patch(typeof(RoamingItemDisableBehavior),"FixedUpdate",typeof(HexOfLoss),prefix:nameof(Native));
            LanguageAPI.Add(LostItemToken, "Permanently lost to Hex of Loss.");
            TeleporterInteraction.onTeleporterBeginChargingGlobal += OnTeleporterBeginCharging;
            HexMechanics.Text(c,()=>c.B("Rework")?"<style=cDeath>Lose "+c.F("Items Removed Per Stack").ToString("0.##")+" <style=cStack>(per stack)</style> permanent items</style> when the Teleporter begins charging."+(c.B("Prioritise Scrap")?" Lower-tier scrap is consumed first.":""):"Every "+c.V("Vanilla Disable Interval","s","cDeath")+", <style=cDeath>disable</style> "+c.V("Vanilla Disabled Item Types Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style> of your item types.",()=>c.B("Rework")?"Lose permanent items when the Teleporter begins charging.":"Periodically disables some of your items.");
        }
        static bool Native(RoamingItemDisableBehavior __instance)
        {
            if(!c.Active||!c.B("Rework"))return true;
            // Clean up this component's own disablers if the mode was changed during a run.
            var disabled=(HashSet<ItemIndex>)AccessTools.Field(typeof(RoamingItemDisableBehavior),"_disabledItemIndices").GetValue(__instance);
            if(disabled.Count>0)AccessTools.Method(typeof(RoamingItemDisableBehavior),"OnDisable").Invoke(__instance,null);
            return false;
        }
        static void OnTeleporterBeginCharging(TeleporterInteraction teleporter)
        {
            var run = Run.instance;
            if (!NetworkServer.active || !run || !teleporter || !c.Active || !c.B("Rework") ||
                processedTeleporters.TryGetValue(teleporter, out _)) return;

            var hex = DLC4Content.Items.RoamingItemDisable;
            if (!hex) return;
            processedTeleporters.Add(teleporter, new ProcessedTeleporter());
            foreach (var player in PlayerCharacterMasterController.instances.ToArray())
            {
                var master = player ? player.master : null;
                if (!master || !master.inventory) continue;
                try
                {
                    RemoveItems(run, master, hex);
                }
                catch (Exception exception)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Hex of Loss could not finish removing items for a player: " + exception);
                }
            }
        }

        static void RemoveItems(Run run, CharacterMaster master, ItemDef hex)
        {
            var inventory = master.inventory;
            int count = Mathf.FloorToInt(inventory.GetItemCountEffective(hex) * c.F("Items Removed Per Stack"));
            bool prioritiseScrap = c.B("Prioritise Scrap");
            var candidates = new List<ItemIndex>();
            for (int i = 0; i < count; i++)
            {
                FindCandidates(inventory, prioritiseScrap, candidates);
                if (candidates.Count == 0) break;

                var selected = candidates[run.treasureRng.RangeInt(0, candidates.Count)];
                int previousCount = inventory.GetItemCountPermanent(selected);
                inventory.RemoveItemPermanent(selected, 1);
                if (inventory.GetItemCountPermanent(selected) >= previousCount) continue;

                CharacterMasterNotificationQueue.SendCustomNotification(master, selected,
                    new CharacterMasterNotificationQueue.CustomOverrideInfo
                    {
                        titleText = ItemCatalog.GetItemDef(selected).nameToken,
                        descriptionText = LostItemToken,
                        iconColor = Color.gray
                    });
            }
        }

        static void FindCandidates(Inventory inventory, bool prioritiseScrap, List<ItemIndex> candidates)
        {
            candidates.Clear();
            int bestPriority = int.MaxValue;
            foreach (ItemIndex index in ItemCatalog.allItems)
            {
                if (inventory.GetItemCountPermanent(index) <= 0) continue;
                var item = ItemCatalog.GetItemDef(index);
                if (!item) continue;

                int scrapPriority = GetScrapPriority(item);
                if (!Eligible(item, scrapPriority < int.MaxValue)) continue;

                int priority = prioritiseScrap ? scrapPriority : int.MaxValue;
                if (priority > bestPriority) continue;
                if (priority < bestPriority)
                {
                    candidates.Clear();
                    bestPriority = priority;
                }
                candidates.Add(index);
            }
        }

        static int GetScrapPriority(ItemDef item)
        {
            // Recognize the standard scrap directly even when printer or item mods change its tags.
            if (item == RoR2Content.Items.ScrapWhite) return 0;
            if (item == RoR2Content.Items.ScrapGreen) return 2;
            if (item == RoR2Content.Items.ScrapRed) return 4;
            if (item == RoR2Content.Items.ScrapYellow) return 6;
            if (item == DLC1Content.Items.RegeneratingScrap) return 3;
            if (!item.ContainsTag(ItemTag.Scrap) && !item.ContainsTag(ItemTag.PriorityScrap))
                return int.MaxValue;

            switch (item.tier)
            {
                case ItemTier.Tier1:
                case ItemTier.VoidTier1: return 0;
                case ItemTier.Tier2:
                case ItemTier.VoidTier2: return 2;
                case ItemTier.Tier3:
                case ItemTier.VoidTier3: return 4;
                case ItemTier.Boss:
                case ItemTier.VoidBoss: return 6;
                default: return 8;
            }
        }

        static bool Eligible(ItemDef item, bool isScrap)
        {
            if (item.hidden || !item.canRemove || item.tier == ItemTier.NoTier ||
                item.tier == ItemTier.Hex || item.tier == ItemTier.Page) return false;
            if (item.tier == ItemTier.Lunar && !c.B("Can Remove Lunar Items")) return false;
            if (c.B("Can Remove Important Items")) return true;

            if (item.ContainsTag(ItemTag.WorldUnique) || item.ContainsTag(ItemTag.ObjectiveRelated) ||
                item.name == "PowerCube" ||
                item.name.IndexOf("Cerebell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                Language.GetString(item.nameToken).IndexOf("Cerebell", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // Scrap can carry theft or transformation restrictions and still be expendable.
            return isScrap || (!item.ContainsTag(ItemTag.CannotSteal) && !item.ContainsTag(ItemTag.CannotTransform));
        }
    }
}
