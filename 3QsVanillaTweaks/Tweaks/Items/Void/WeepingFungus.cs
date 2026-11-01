using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Void
{
    // Native item ID: MushroomVoid. Configuration and hooks for this item only.
    internal static class WeepingFungus
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>VoidConfigSupport.Register("MushroomVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            c.Pair("Healing Per Tick (%)",1,1,"Maximum health healed each tick while sprinting.");c.Number("Healing Interval",.5f,"Seconds between healing ticks.");
            v.Field("RoR2.MushroomVoidBehavior","FixedUpdate","healPercentagePerStack",()=>{int n=v.Count(N.Body);return n>0?v.Total("Healing Per Tick (%)",n)/(100*n):.01f;});
            v.Field("RoR2.MushroomVoidBehavior","FixedUpdate","healPeriodSeconds",()=>Mathf.Max(.01f,v.F("Healing Interval")));
            c.Describe(()=>"Heal for <style=cIsHealing>"+(v.F("Healing Per Tick (%)")/Mathf.Max(.01f,v.F("Healing Interval"))).ToString("0.##")+"%</style>"+(v.F("Healing Per Tick (%) Per Stack")!=0?" <style=cStack>(+"+(v.F("Healing Per Tick (%) Per Stack")/Mathf.Max(.01f,v.F("Healing Interval"))).ToString("0.##")+"% per stack)</style>":"")+" of your maximum health every second <style=cIsUtility>while sprinting</style>. <style=cIsVoid>Corrupts all Bustling Fungus</style>.");
            c.Assets("RoR2.MushroomVoidBehavior");
        }
    }
}
