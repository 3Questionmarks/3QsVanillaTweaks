using System;
using RoR2;
using RoR2.Items;
using R=Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Hex
{
    internal static class HexOfMimicry
    {
        static R c;
        [ThreadStatic] static CharacterMaster spawningOwner;
        static bool Restricted()=>c.Active&&c.B("Target Holder Only");
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Mimicry","Mimicry");
            c.B("Target Holder Only",true,"Mimics spawned by this Hex only target, devour or damage its holder. False restores vanilla targeting.");
            c.N("Spawn Cooldown",60,"Vanilla: 60 seconds.",.1f);
            c.N("Cooldown Reduction Per Stack (%)",10,"Vanilla: 10, using vanilla hyperbolic cooldown reduction.");
            c.N("Spawn Distance",40,"Vanilla: 40 metres behind the holder.");
            c.Field("RoR2.Items.MimicryBehaviour","GetCooldownValue","baseMimicCooldown",()=>c.F("Spawn Cooldown"));
            c.Field("RoR2.Items.MimicryBehaviour","GetCooldownValue","cooldownReductionPerStack",()=>c.F("Cooldown Reduction Per Stack (%)"));
            c.Field("RoR2.Items.MimicryBehaviour","TrySpawnMimic","distanceToSearch",()=>c.F("Spawn Distance"));
            HexOwnerTargets.Ensure();
            HexMechanics.Patch(typeof(MimicryBehaviour),"TrySpawnMimic",typeof(HexOfMimicry),prefix:nameof(Begin),finalizer:nameof(End));
            HexMechanics.Patch(typeof(MasterSummon),"Perform",typeof(HexOfMimicry),postfix:nameof(Spawned));
            HexMechanics.Text(c,()=>"Spawns a <style=cDeath>Mimic</style> every "+c.V("Spawn Cooldown","s","cDeath")+". Mimics appear "+c.V("Cooldown Reduction Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style> more frequently."+(Restricted()?" Mimics only attack you.":""),()=>"Periodically spawns Mimics."+(Restricted()?" They only attack you.":""));
        }
        static void Begin(MimicryBehaviour __instance,out CharacterMaster __state)
        {__state=spawningOwner;spawningOwner=__instance.body?__instance.body.master:null;}
        static void End(CharacterMaster __state){spawningOwner=__state;}
        static void Spawned(CharacterMaster __result)
        {if(spawningOwner)HexOwnerTargets.Mark(__result,spawningOwner,Restricted);}
    }
}
