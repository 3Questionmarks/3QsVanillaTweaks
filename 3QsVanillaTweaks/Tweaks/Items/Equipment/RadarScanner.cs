using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
namespace Tweaks.Items.Equipment
{
    internal static class RadarScanner
    {
        private const string Section="Items - Equipment - Radar Scanner";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> cooldown,radius,speed,reveal,interval,lifetime,visual;
        private static float originalCooldown;
        private static GameObject prefab;
        internal static void Init()
        {
            enabled=C.BindStartupBool(Section,"Enabled",true,"Enable configurable Radar Scanner. Restart to apply.");
            cooldown=Number("Cooldown",20f,"Base equipment cooldown in seconds. Vanilla: 45.",0.01f);
            if(!enabled.Value)return;
            RoR2Application.onLoad+=Load;
        }
        private static ConfigEntry<float> Number(string key,float value,string description,float min=0f)=>C.Bind(Section,key,value,new ConfigDescription(description,new AcceptableValueRange<float>(min,100000f)));
        private static void Load()
        {
            prefab=LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/ChestScanner");
            if(!prefab)throw new InvalidOperationException("Radar Scanner prefab unavailable.");
            var scanner=prefab.GetComponent<ChestRevealer>();if(!scanner)throw new InvalidOperationException("Radar Scanner has no ChestRevealer.");
            radius=Number("Scan Radius",scanner.radius,"Maximum reveal radius in metres. Default read from the vanilla scanner prefab.");
            speed=Number("Pulse Travel Speed",scanner.pulseTravelSpeed,"Reveal wave travel speed in metres per second. Vanilla: 10.",0.01f);
            reveal=Number("Reveal Duration",scanner.revealDuration,"How long each revealed marker remains visible, in seconds. Vanilla: 10.");
            interval=Number("Pulse Interval",scanner.pulseInterval,"Seconds between repeated reveal pulses. Vanilla: 1.",0.02f);
            visual=Number("Pulse Effect Scale",scanner.pulseEffectScale,"Visual pulse scale multiplier. Vanilla: 1.");
            var destroy=prefab.GetComponent<DestroyOnTimer>();
            if(destroy)lifetime=Number("Scanner Duration",destroy.duration,"Lifetime of the scanner in seconds. Default read from the vanilla prefab.",0.02f);
            originalCooldown=RoR2Content.Equipment.Scanner.cooldown;ApplyCooldown();C.EffectiveSettingsChanged+=ApplyCooldown;
            Tweaks.Survivors.SurvivorSettings.SetText("EQUIPMENT_SCANNER_DESC",()=>"<style=cIsUtility>Reveal</style> all interactables within <style=cIsUtility>"+C.Get(radius)+"m</style> for <style=cIsUtility>"+C.Get(reveal)+" seconds</style>.");
            var h=new Harmony("3Questionmarks.3QsVanillaTweaks.RadarScanner");
            h.Patch(AccessTools.Method(typeof(ChestRevealer),"FixedUpdate"),prefix:new HarmonyMethod(typeof(RadarScanner),nameof(BeforeStart)));
        }
        private static void ApplyCooldown(){RoR2Content.Equipment.Scanner.cooldown=C.Get(enabled)?C.Get(cooldown):originalCooldown;}
        private static void BeforeStart(ChestRevealer __instance)
        {
            if(!C.Get(enabled)||!prefab||__instance.gameObject.name!=prefab.name+"(Clone)")return;
            __instance.Networkradius=C.Get(radius);__instance.pulseTravelSpeed=C.Get(speed);__instance.revealDuration=C.Get(reveal);__instance.pulseInterval=C.Get(interval);__instance.pulseEffectScale=C.Get(visual);
            var destroy=__instance.GetComponent<DestroyOnTimer>();if(destroy&&lifetime!=null)destroy.duration=C.Get(lifetime);
        }
    }
}
