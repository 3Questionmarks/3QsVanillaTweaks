using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
namespace Tweaks.Stages
{
    internal static class EclipseVoidPortals
    {
        private static ConfigEntry<bool> enabled;
        internal static void Init()
        {
            enabled=C.BindStartupBool("Stages - Eclipse - Void Route","Enabled",true,"Allow Void Locus and Planetarium portal routes in Eclipse. Removes the Eclipse event-flag restriction from void portals and the Void Locus lunar-seer override; normal spawn chances, stage requirements and DLC checks remain. Restart to apply.");
            if(!enabled.Value)return;
            var h=new Harmony("3Questionmarks.3QsVanillaTweaks.EclipseVoidPortals");
            h.Patch(AccessTools.Method(typeof(PortalSpawner),"Start"),prefix:new HarmonyMethod(typeof(EclipseVoidPortals),nameof(BeforePortalStart)));
            h.Patch(AccessTools.Method(typeof(BazaarController),"SetUpSeerStations"),prefix:new HarmonyMethod(typeof(EclipseVoidPortals),nameof(BeforeSeers)));
        }
        private static bool Active=>enabled!=null&&C.Get(enabled)&&Run.instance&&Run.instance.gameModeIndex==GameModeCatalog.FindGameModeIndex("EclipseRun")&&Tweaks.SpawnRequirements.ExpansionEnabled("DLC1");
        private static void BeforePortalStart(PortalSpawner __instance)
        {
            if(!Active||!__instance.PortalSpawnCard)return;
            string name=__instance.PortalSpawnCard.name;
            if(name=="iscVoidPortal"||name=="iscDeepVoidPortal")
                AccessTools.Field(typeof(PortalSpawner),"bannedEventFlag").SetValue(__instance,string.Empty);
        }
        private static void BeforeSeers(BazaarController __instance)
        {
            if(!Active||__instance.seerSceneOverrides==null)return;
            for(int i=0;i<__instance.seerSceneOverrides.Length;i++)
            {
                var entry=__instance.seerSceneOverrides[i];
                if(entry.sceneDef&&(entry.sceneDef.baseSceneName=="voidstage"||entry.sceneDef.baseSceneName=="voidraid"))
                {entry.bannedEventFlag=string.Empty;__instance.seerSceneOverrides[i]=entry;}
            }
        }
    }
}
