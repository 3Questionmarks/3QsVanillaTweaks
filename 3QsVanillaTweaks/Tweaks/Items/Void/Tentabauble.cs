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
    // Native item ID: SlowOnHitVoid. Configuration and hooks for this item only.
    internal static class Tentabauble
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>VoidConfigSupport.Register("SlowOnHitVoid",Configure);
        static void Configure(VoidConfig c)
        {
            var v=c.Values;
            c.Pair("Root Chance (%)", 5, 5, "Root chance before the native diminishing-returns calculation and triggering hit's proc coefficient.");
            c.Pair("Root Duration", 1, 1, "Duration of the root, in seconds.");
            v.NativeCount(Events, "ProcessHitEnemy", 5f, "Root Chance (%)", 1, "SlowOnHitVoid");
            v.NativeCount(Events, "ProcessHitEnemy", 1f, "Root Duration", 1, "SlowOnHitVoid");
            c.Describe(() => v.V("Root Chance (%)", "%", "cIsUtility") + " chance on hit to <style=cIsUtility>root</style> enemies for " + v.V("Root Duration", "s", "cIsUtility") + ". <style=cIsVoid>Corrupts all Chronobaubles</style>.", () => "Chance on hit to <style=cIsUtility>root enemies</style>. <style=cIsVoid>Corrupts all Chronobaubles</style>.");

        }
    }
}
