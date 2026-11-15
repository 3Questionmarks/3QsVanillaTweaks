using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexOfGreed
    {
        static R c;
        sealed class Remainder { internal double Value; }
        static readonly ConditionalWeakTable<CharacterMaster,Remainder> fractions=new ConditionalWeakTable<CharacterMaster,Remainder>();
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Greed","ShareItemsWithEnemies");
            c.B("Rework",true,"Reduce the holder's gold income instead of sharing items with enemies. False restores the configurable vanilla effect.");
            c.Pair("Gold Reduction (%)",25,25,"Rework: gold income reduction with diminishing stacking. Applies before gold is converted to experience.");
            c.StatValue = (key, count, value) => c.B("Rework") && key == "Gold Reduction (%)"
                ? HexMechanics.Penalty(c, key, count) : value;
            c.N("Vanilla Share Interval",15,"Vanilla: 15 seconds.",.1f);
            c.N("Vanilla Targets",3,"Vanilla: 3 enemies.");
            c.N("Vanilla Items Per Stack",1,"Vanilla: 1 shared item per stack.");
            c.N("Vanilla Search Radius",65,"Vanilla: 65 metres.");
            const string type="RoR2.Items.ShareItemsWithEnemiesBehaviour";
            c.Literal(type,"OnEnable",15,()=>c.F("Vanilla Share Interval"));
            c.Literal(type,"FixedUpdate",15,()=>c.F("Vanilla Share Interval"));
            c.Literal(type,"FixedUpdate",7.5f,()=>c.F("Vanilla Share Interval")/2);
            c.Literal(type,"ShareItemsWithEnemies",65,()=>c.F("Vanilla Search Radius"));
            c.Literal(type,"ShareItemsWithEnemies",3,()=>c.F("Vanilla Targets"));
            HexMechanics.Patch(typeof(CharacterBody).Assembly.GetType(type),"FixedUpdate",typeof(HexOfGreed),prefix:nameof(Native));
            HexMechanics.Patch(typeof(CharacterBody).Assembly.GetType(type),"ShareItemsWithEnemies",typeof(HexOfGreed),prefix:nameof(Amount));
            if(c.Enabled.Value)U.Harmony.Patch(AccessTools.Method(typeof(CharacterMaster),"GiveMoney",new[]{typeof(uint),typeof(bool)}),prefix:new HarmonyMethod(typeof(HexOfGreed),nameof(Money)){priority=Priority.First});
            HexMechanics.Text(c,()=>c.B("Rework")?"Gain "+HexMechanics.PenaltyText(c,"Gold Reduction (%)")+" less gold.":"Every "+c.V("Vanilla Share Interval","s","cDeath")+", share "+c.V("Vanilla Items Per Stack","","cDeath")+" <style=cStack>(per stack)</style> items with "+c.V("Vanilla Targets","","cDeath")+" nearby enemies.",()=>c.B("Rework")?"Gain less gold.":"Periodically share your items with nearby enemies.");
        }
        static bool Native()=>!c.Active||!c.B("Rework");
        static void Amount(ref int amount){if(c.Active)amount=UnityEngine.Mathf.RoundToInt(amount*c.F("Vanilla Items Per Stack"));}
        static void Money(CharacterMaster __instance,ref uint amount)
        {
            if(!c.Active||!c.B("Rework")||!__instance.inventory)return;
            int n=__instance.inventory.GetItemCountEffective(c.Item);
            if(n<=0){fractions.Remove(__instance);return;}
            double factor=1-HexMechanics.Penalty(c,"Gold Reduction (%)",n)/100d;
            var remainder=fractions.GetOrCreateValue(__instance);
            double value=amount*factor+remainder.Value;
            amount=(uint)Math.Floor(value);remainder.Value=value-amount;
        }
    }
}
