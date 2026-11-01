using RoR2;
using RoR2.Items;
using UnityEngine;
using R=Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Hex
{
    internal static class HexOfHaunting
    {
        static R c;
        static bool Restricted()=>c.Active&&c.B("Target Holder Only");
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Haunting","HexHaunting");
            c.B("Target Holder Only",true,"Poltergeists can target and damage only the Hex holder. False restores vanilla targeting.");
            c.N("Haunt Cooldown",60,"Vanilla: 60 seconds.",.1f);
            c.N("Cooldown Reduction Per Stack (%)",25,"Vanilla: 25, using vanilla hyperbolic cooldown reduction.");
            c.Field("RoR2.Items.HexHauntingBehaviour","GetCooldownValue","baseHauntCooldown",()=>c.F("Haunt Cooldown"));
            c.Field("RoR2.Items.HexHauntingBehaviour","GetCooldownValue","cooldownReductionPerStack",()=>c.F("Cooldown Reduction Per Stack (%)"));
            HexOwnerTargets.Ensure();
            HexMechanics.Patch(typeof(HauntedInteractable),"OnTriggerStay",typeof(HexOfHaunting),prefix:nameof(Trigger),postfix:nameof(Spawned));
            HexMechanics.Text(c,()=>"Haunts nearby interactables every "+c.V("Haunt Cooldown","s","cDeath")+". Haunts appear "+c.V("Cooldown Reduction Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style> more frequently."+(Restricted()?" Poltergeists only attack you.":""),()=>"Nearby interactables become haunted."+(Restricted()?" Poltergeists only attack you.":""));
        }
        static bool Trigger(HauntedInteractable __instance,Collider other)
        { return !Restricted()||(__instance.ownerBody&&other.GetComponent<CharacterBody>()==__instance.ownerBody); }
        static void Spawned(HauntedInteractable __instance)
        {if(__instance.ownerBody&&__instance.poltergeistMasterInstance)HexOwnerTargets.Mark(__instance.poltergeistMasterInstance,__instance.ownerBody.master,Restricted);}
    }
}
