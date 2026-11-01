using System;
using System.Reflection;
using RoR2;
using UnityEngine;
using R=Tweaks.Items.Uncommon.UncommonConfig;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Hallowed
{
    internal static class CorpseLily
    {
        static R c;
        static readonly Type controllerType=typeof(CharacterBody).Assembly.GetType("RoR2.BlightZoneController",true);
        static readonly FieldInfo dotField=A.Field(controllerType,"_blightDotInfo");
        static readonly MethodInfo teamCount=A.Method(controllerType,"GetTeamBlightZoneStackCount");
        internal static void Init()
        {
            U.Ensure();c=new R("Corpse Lily","BlightZone",true,"Hallowed");
            c.Pair("Damage Per Second (%)",180,90,"Maximum combined blight damage per second, shared among holders as in vanilla. Vanilla: 180% +60% per team stack.");
            c.B("Show Damage Per Tick",true,"Describe blight damage per tick and its interval instead of damage per second. Does not change global Blight tick timing.");
            c.Pair("Size Growth (%)",1,1,"Cloud-size growth for each 1% holdout charge; capped by the holdout radius. Default: 1% +1% per team stack. Changes growth speed, not maximum size.");
            c.N("Application Interval",5,"Seconds between blight applications. Vanilla: 5.",.01f);
            c.Pair("Blight Duration",3,0,"Seconds each blight application lasts. Vanilla: 3.",.01f);
            c.Field("RoR2.BlightZoneController","UpdateBlightTimer","blightCooldownTime",()=>c.F("Application Interval"));
            if(c.Enabled.Value)
            {
                U.Patch(controllerType,"BlightEnemiesInRadius",typeof(CorpseLily),prefix:nameof(BeforeBlight));
                U.Patch(controllerType,"GetAdjustedRadius",typeof(CorpseLily),prefix:nameof(BeforeRadius));
            }
            c.Text(()=>"Release an expanding cloud during the <style=cIsUtility>Teleporter event and holdout zones</style>, inflicting <style=cIsDamage>Blight</style> for up to "+DamageText()+". The cloud grows by "+c.V("Size Growth (%)","%","cIsUtility")+" per 1% event charge, up to the holdout's radius.",()=>"Release an expanding cloud of Blight during holdout events.");
        }
        static string DamageText()
        {
            if(!c.B("Show Damage Per Tick"))return c.V("Damage Per Second (%)","%")+" base damage per second";
            float tick=DotController.GetDotDef(DotController.DotIndex.Blight)?.interval??1;
            float first=c.F("Damage Per Second (%)")*tick,extra=c.F("Damage Per Second (%) Per Stack")*tick;
            return "<style=cIsDamage>"+first.ToString("0.##")+"%</style>"+(extra==0?"":" <style=cStack>(+"+extra.ToString("0.##")+"% per stack)</style>")+" base damage every <style=cIsUtility>"+tick.ToString("0.##")+" seconds</style>";
        }
        static int Count(object controller)=>Math.Max(1,(int)teamCount.Invoke(controller,null));
        static void BeforeBlight(object __instance)
        {
            if(!c.Active)return;int n=Count(__instance);
            var dot=(InflictDotInfo)dotField.GetValue(__instance);
            dot.damageMultiplier=c.Total("Damage Per Second (%)",n)/(180+60*(n-1));
            dot.duration=c.Total("Blight Duration",n);dotField.SetValue(__instance,dot);
        }
        static void BeforeRadius(object __instance)
        {
            if(!c.Active)return;float growth=c.Total("Size Growth (%)",Count(__instance));
            float minimum=(float)A.Field(controllerType,"minimumGrowthDuration").GetValue(__instance);
            A.Field(controllerType,"expectedTimeOfMaxSizeNormalized").SetValue(__instance,growth<=0?float.PositiveInfinity:Mathf.Max(minimum,1/growth));
        }
    }
}
