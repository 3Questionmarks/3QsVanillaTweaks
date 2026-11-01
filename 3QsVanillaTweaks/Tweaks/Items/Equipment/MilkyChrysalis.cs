using RoR2;
namespace Tweaks.Items.Equipment
{
    internal static class MilkyChrysalis
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("Milky Chrysalis","Jetpack",true,45);
            c.N("Movement Speed (%)",20,"Movement speed increase while flying. Vanilla: 20%.");
            c.Values.Literal("RoR2.CharacterBody","RecalculateStats",.2f,()=>c.F("Movement Speed (%)")/100,"BugWings",window:15);
            RoR2Application.onLoad+=()=>{var p=LegacyResourcesAPI.Load<UnityEngine.GameObject>("Prefabs/NetworkedObjects/BodyAttachments/JetpackController");if(!p)return;var j=p.GetComponent<JetpackController>();if(!j)return;
                c.N("Flight Duration",j.duration,"Flight duration in seconds. Default read from the native prefab.");c.N("Flight Acceleration",j.acceleration,"Flight acceleration. Default read from the native prefab.");c.N("Boost Speed Multiplier",j.boostSpeedMultiplier,"Flight boost speed multiplier. Default read from the native prefab.");c.N("Boost Cooldown",j.boostCooldown,"Seconds between flight boosts. Default read from the native prefab.",.01f);
            };
            c.Patch(typeof(JetpackController),"Start",typeof(MilkyChrysalis),prefix:nameof(Start));
            c.Text(()=>"Sprout wings and <style=cIsUtility>fly</style> for "+c.V("Flight Duration"," seconds","cIsUtility")+". Gain "+c.V("Movement Speed (%)","%","cIsUtility")+" <style=cIsUtility>movement speed</style> for the duration.",()=>"Gain temporary flight.");
        }
        static void Start(JetpackController __instance){if(!c.Active||!c.Values.Numbers.ContainsKey("Flight Duration"))return;__instance.duration=c.F("Flight Duration");__instance.acceleration=c.F("Flight Acceleration");__instance.boostSpeedMultiplier=c.F("Boost Speed Multiplier");__instance.boostCooldown=c.F("Boost Cooldown");}
    }
}
