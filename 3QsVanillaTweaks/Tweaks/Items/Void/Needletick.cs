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
    // Native item ID: BleedOnHitVoid. Configuration and hooks for this item only.
    internal static class Needletick
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>VoidConfigSupport.Register("BleedOnHitVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            c.Pair("Trigger Chance (%)",10,10,"Chance to collapse on hit. Scales with the triggering proc coefficient.");
            v.NativeCount(Events,"ProcessHitEnemy",10,"Trigger Chance (%)",1,"BleedOnHitVoid");
            // Dot values are read from the already-loaded native definition.
            var dot=DotController.GetDotDef(DotController.DotIndex.Fracture);if(dot!=null){c.Number("Collapse Damage (%)",dot.damageCoefficient,"Collapse damage coefficient.",100);c.Number("Collapse Delay",dot.interval,"Seconds before collapse damage.",1);if(v.Enabled.Value){float damage=dot.damageCoefficient,delay=dot.interval;Action apply=()=>{dot.damageCoefficient=v.Active?v.F("Collapse Damage (%)")/100:damage;dot.interval=v.Active?Mathf.Max(.01f,v.F("Collapse Delay")):delay;};apply();Config.ConfigManager.EffectiveSettingsChanged+=apply;}}
        }
    }
}
