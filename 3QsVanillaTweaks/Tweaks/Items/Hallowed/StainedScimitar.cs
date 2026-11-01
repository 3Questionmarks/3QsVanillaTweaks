using RoR2;
using RoR2.Projectile;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using R=Tweaks.Items.Uncommon.UncommonConfig;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Hallowed
{
    internal static class StainedScimitar
    {
        static R c;
        static readonly System.Type behaviorType=typeof(CharacterBody).Assembly.GetType("RoR2.Items.GhostBladesBehavior",true);
        static readonly System.Type projectileType=typeof(CharacterBody).Assembly.GetType("RoR2.Projectile.GhostBladeProjectileController",true);
        internal static void Init()
        {
            U.Ensure();c=new R("Stained Scimitar","GhostBlades",true,"Hallowed");
            c.Pair("Lifesteal (%)",5,5,"Healing from damage dealt, scaled by the triggering hit's proc coefficient. Vanilla: 1% per stack.");
            c.Pair("Blade Damage (%)",100,0,"Orbiting blade base damage. Vanilla: 100%.");
            c.Pair("Maximum Blades",6,6,"Maximum stored blades. Vanilla: 6 +6 per stack.");
            c.N("Healing Per Blade (%)",10,"Combined maximum health that must be healed to produce a blade. Includes overhealing. Vanilla: 10%.",.01f);
            c.N("Blade Duration",5,"Seconds an orbiting blade lasts. Vanilla: 5.",.01f);
            c.NativeCount("RoR2.GlobalEventManager","ProcessHitEnemy",.01f,"Lifesteal (%)",100);
            c.Field("RoR2.Items.GhostBladesBehavior","SpawnGhostBlade","damageMult",()=>c.Total("Blade Damage (%)",c.Count(N.Body))/100);
            c.Field("RoR2.Items.GhostBladesBehavior","OnCharacterHealed","healthToFireMult",()=>c.F("Healing Per Blade (%)")/100);
            c.Field("RoR2.Items.GhostBladesBehavior","SpawnGhostBlade","bladeDuration",()=>c.F("Blade Duration"));
            c.Field("RoR2.Items.GhostBladesBehavior","RefreshOldestGhostBlade","bladeDuration",()=>c.F("Blade Duration"));
            if(c.Enabled.Value)U.Harmony.Patch(ThreeQsVanillaTweaks.ReflectionTools.Property(behaviorType,"MaxGhostBlades").GetGetMethod(true),postfix:new HarmonyLib.HarmonyMethod(typeof(StainedScimitar),nameof(Maximum)));
            if(c.Enabled.Value)U.Patch(typeof(CharacterMaster),"GetDeployableSameSlotLimit",typeof(StainedScimitar),postfix:nameof(DeployableMaximum));
            RoR2Application.onLoad += () => {
                var type=behaviorType;
                var prefab=A.Field(type,"ghostBladePrefab")?.GetValue(null) as GameObject;
                float native=prefab && prefab.GetComponent<ProjectileController>() ? prefab.GetComponent<ProjectileController>().procCoefficient : 1;
                c.Pair("Blade Proc Coefficient",native,0,"Proc coefficient of the damaging orbiting blades. Default matches the native projectile prefab.");
            };
            if(c.Enabled.Value)U.Patch(typeof(ProjectileController),"Start",typeof(StainedScimitar),prefix:nameof(Projectile));
            c.Text(()=>"Heal for "+c.V("Lifesteal (%)","%","cIsHealing")+" of damage dealt. Healing <style=cIsHealing>"+c.F("Healing Per Blade (%)").ToString("0.##")+"% of maximum health</style> creates an orbiting blade for <style=cIsUtility>"+c.F("Blade Duration").ToString("0.##")+" seconds</style>, dealing "+c.V("Blade Damage (%)","%")+" base damage. Hold up to "+c.V("Maximum Blades"," blades","cIsUtility")+". Using your secondary launches stored blades.",()=>"Gain lifesteal. Healing creates blades that launch when you use your secondary.");
        }
        static void Projectile(ProjectileController __instance)
        {
            if(!__instance.GetComponent(projectileType)||!c.Numbers.ContainsKey("Blade Proc Coefficient"))return;
            int n=c.Count(__instance.owner?__instance.owner.GetComponent<CharacterBody>():null);
            if(n>0)__instance.procCoefficient=c.Total("Blade Proc Coefficient",n);
        }
        static void DeployableMaximum(CharacterMaster __instance,DeployableSlot slot,ref int __result)
        {
            if(!c.Active||slot.ToString()!="GhostBlade"||!__instance.inventory)return;
            int n=__instance.inventory.GetItemCountEffective(c.Item);if(n>0)__result=Mathf.Max(0,Mathf.RoundToInt(c.Total("Maximum Blades",n)));
        }
        static void Maximum(object __instance,ref int __result){int n=c.Count((__instance as Component)?.GetComponent<CharacterBody>());if(n>0)__result=UnityEngine.Mathf.Max(0,UnityEngine.Mathf.RoundToInt(c.Total("Maximum Blades",n)));}
    }
}
