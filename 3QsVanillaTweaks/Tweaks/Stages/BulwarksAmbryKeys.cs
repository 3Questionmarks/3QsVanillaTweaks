using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
namespace Tweaks.Stages
{
    internal static class BulwarksAmbryKeys
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<int> kills;
        sealed class Counter { internal int Kills; }
        static readonly ConditionalWeakTable<ArtifactTrialMissionController, Counter> counters = new ConditionalWeakTable<ArtifactTrialMissionController, Counter>();
        internal static void Init()
        {
            const string s="Stages - Bulwarks Ambry";
            enabled=C.BindBool(s,"Guaranteed Key Enabled",true,"Guarantee an Artifact Key after enough enemy kills without a key drop. Earlier random drops still work.");
            kills=C.Bind(s,"Kills Without A Key",25,new ConfigDescription("Maximum eligible enemy kills between key drops. Vanilla: random drops only.",new AcceptableValueRange<int>(1,10000)));
            var type=typeof(ArtifactTrialMissionController).GetNestedType("CombatState",System.Reflection.BindingFlags.NonPublic);
            var h=new Harmony("3Qs.AmbryKeys");
            h.Patch(AccessTools.Method(type,"OnCharacterDeathGlobal"),prefix:new HarmonyMethod(typeof(BulwarksAmbryKeys),nameof(Before)));
            h.Patch(AccessTools.Method(typeof(ArtifactTrialMissionController),"GenerateDrop"),postfix:new HarmonyMethod(typeof(BulwarksAmbryKeys),nameof(Dropped)));
        }
        static bool Before(object __instance, DamageReport damageReport)
        {
            if (!C.Get(enabled)) return true;
            if (!damageReport.victimBody || !damageReport.victimMaster || damageReport.victimTeamIndex == TeamIndex.Player || damageReport.victimTeamIndex == TeamIndex.Neutral) return false;
            var controller = (ArtifactTrialMissionController)AccessTools.Property(__instance.GetType(),"missionController").GetValue(__instance);
            if (!controller) return true;
            var counter=counters.GetOrCreateValue(controller);
            if (++counter.Kills < C.Get(kills)) return true;
            var pickup=(UniquePickup)AccessTools.Method(typeof(ArtifactTrialMissionController),"GenerateDrop").Invoke(controller,null);
            PickupDropletController.CreatePickupDroplet(new GenericPickupController.CreatePickupInfo { pickup = pickup, position = damageReport.victimBody.corePosition }, damageReport.victimBody.corePosition, Vector3.up*20f);
            return false;
        }
        static void Dropped(ArtifactTrialMissionController __instance) { counters.GetOrCreateValue(__instance).Kills=0; }
    }
}
