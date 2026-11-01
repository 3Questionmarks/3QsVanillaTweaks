using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
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
        sealed class Visit { internal Run Run; internal int Stage=-1; }
        static readonly ConditionalWeakTable<CharacterMaster,Visit> visits=new ConditionalWeakTable<CharacterMaster,Visit>();
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Loss","RoamingItemDisable");
            c.B("Rework",true,"Permanently remove items at the start of real stages instead of temporarily disabling items. False restores configurable vanilla behaviour.");
            c.N("Items Removed Per Stack",1,"Permanent item copies removed per Hex stack on entering a new stage.");
            c.B("Prioritise Scrap",true,"Remove eligible scrap before other items.");
            c.B("Can Remove Lunar Items",false,"Allow lunar items to be selected.");
            c.B("Can Remove Important Items",false,"Allow removable objective/world-unique items and Prison Matrix/Cerebellums. Hidden, non-removable, NoTier, Hex and Page items remain protected.");
            c.N("Vanilla Disable Interval",30,"Vanilla: 30 seconds.",.1f);
            c.N("Vanilla Disabled Item Types Per Stack (%)",10,"Vanilla: 10% of item types disabled per stack.");
            const string native="RoR2.Items.RoamingItemDisableBehavior";
            c.Literal(native,"FixedUpdate",30,()=>c.F("Vanilla Disable Interval"));
            c.Literal(native,"ResetRoamingDisableTimer",27,()=>Mathf.Max(0,c.F("Vanilla Disable Interval")-3));
            c.Literal(native,"RoamItemDisable",.1f,()=>c.F("Vanilla Disabled Item Types Per Stack (%)")/100,null,1);
            HexMechanics.Patch(typeof(RoamingItemDisableBehavior),"FixedUpdate",typeof(HexOfLoss),prefix:nameof(Native));
            Stage.onStageStartGlobal+=StartStage;
            HexMechanics.Text(c,()=>c.B("Rework")?"<style=cDeath>Lose "+c.F("Items Removed Per Stack").ToString("0.##")+" <style=cStack>(per stack)</style> permanent items</style> at the start of each stage."+(c.B("Prioritise Scrap")?" Scrap is consumed first.":""):"Every "+c.V("Vanilla Disable Interval","s","cDeath")+", <style=cDeath>disable</style> "+c.V("Vanilla Disabled Item Types Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style> of your item types.",()=>c.B("Rework")?"Lose permanent items at the start of each stage.":"Periodically disables some of your items.");
        }
        static bool Native(RoamingItemDisableBehavior __instance)
        {
            if(!c.Active||!c.B("Rework"))return true;
            // Clean up this component's own disablers if the mode was changed during a run.
            var disabled=(HashSet<ItemIndex>)AccessTools.Field(typeof(RoamingItemDisableBehavior),"_disabledItemIndices").GetValue(__instance);
            if(disabled.Count>0)AccessTools.Method(typeof(RoamingItemDisableBehavior),"OnDisable").Invoke(__instance,null);
            return false;
        }
        static void StartStage(Stage stage)
        {
            if(!NetworkServer.active||!Run.instance||!c.Active||!c.B("Rework"))return;
            var scene=SceneCatalog.GetSceneDefForCurrentScene();
            if(!scene||scene.preventStageAdvanceCounter||(scene.sceneType!=SceneType.Stage&&scene.sceneType!=SceneType.UntimedStage))return;
            foreach(var master in CharacterMaster.readOnlyInstancesList.ToArray())
            {
                if(!master||!master.inventory||master.teamIndex!=TeamIndex.Player)continue;
                var visit=visits.GetOrCreateValue(master);
                if(visit.Run==Run.instance&&visit.Stage==Run.instance.stageClearCount)continue;
                visit.Run=Run.instance;visit.Stage=Run.instance.stageClearCount;
                var inv=master.inventory;
                int count=inv.GetItemCountEffective(c.Item),remove=Mathf.FloorToInt(count*c.F("Items Removed Per Stack"));
                for(int k=0;k<remove;k++)
                {
                    var candidates=inv.itemAcquisitionOrder.Where(i=>inv.GetItemCountPermanent(i)>0&&Eligible(ItemCatalog.GetItemDef(i))).ToList();
                    if(candidates.Count==0)break;
                    if(c.B("Prioritise Scrap")){var scrap=candidates.Where(i=>ItemCatalog.GetItemDef(i).ContainsTag(ItemTag.Scrap)).ToList();if(scrap.Count>0)candidates=scrap;}
                    var selected=candidates[Run.instance.treasureRng.RangeInt(0,candidates.Count)];
                    inv.RemoveItemPermanent(selected,1);
                }
            }
        }
        static bool Eligible(ItemDef item)
        {
            if(!item||item.hidden||!item.canRemove||item.tier==ItemTier.NoTier)return false;
            string tier=item.tier.ToString();
            if(tier=="Hex"||tier=="Page")return false;
            if(item.tier==ItemTier.Lunar&&!c.B("Can Remove Lunar Items"))return false;
            if(!c.B("Can Remove Important Items")&&(item.ContainsTag(ItemTag.CannotSteal)||item.ContainsTag(ItemTag.WorldUnique)||item.ContainsTag(ItemTag.ObjectiveRelated)||item.ContainsTag(ItemTag.CannotTransform)||item.name=="PowerCube"||item.name.IndexOf("Cerebell",StringComparison.OrdinalIgnoreCase)>=0||Language.GetString(item.nameToken).IndexOf("Cerebell",StringComparison.OrdinalIgnoreCase)>=0))return false;
            return true;
        }
    }
}
