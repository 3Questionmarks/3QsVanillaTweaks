using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C=Config.ConfigManager;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Stages
{
    internal static class HoldoutKillCharge
    {
        static ConfigEntry<bool> signals, pillars;
        static ConfigEntry<float> signalCharge, pillarCharge;
        static readonly HashSet<Tracker> zones = new HashSet<Tracker>();
        internal static void Init()
        {
            signals=C.BindBool("Stages - Void Locus", "Enable Charge On Kill", true, "Enemy kills by the player team add charge to actively charging Deep Void Signals.");
            pillars=C.BindBool("Stages - Commencement", "Enable Charge On Kill", true, "Enemy kills by the player team add charge to actively charging pillars. Does not affect the escape ship.");
            signalCharge=C.Bind("Stages - Void Locus", "Charge Per Kill (%)", 1f, new ConfigDescription("Charge added to each active signal per player-team enemy kill.",new AcceptableValueRange<float>(0,100)));
            pillarCharge=C.Bind("Stages - Commencement", "Charge Per Kill (%)", 1f,new ConfigDescription("Charge added to each active pillar per player-team enemy kill.",new AcceptableValueRange<float>(0,100)));
            new Harmony("3Questionmarks.3QsVanillaTweaks.HoldoutKills").Patch(A.Method(typeof(HoldoutZoneController),"Start"),postfix:new HarmonyMethod(typeof(HoldoutKillCharge),nameof(Attach)));
            var deathEvent = typeof(GlobalEventManager).GetEvent(
                "onCharacterDeathServer",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            if (deathEvent != null)
                deathEvent.AddEventHandler(null, (Action<DamageReport>)Killed);
            else
                new Harmony("3Questionmarks.3QsVanillaTweaks.HoldoutKills.DeathFallback").Patch(
                    A.Method(typeof(GlobalEventManager), "HandleCharacterDeath")
                        ?? A.Method(typeof(GlobalEventManager), "OnCharacterDeath"),
                    postfix: new HarmonyMethod(typeof(HoldoutKillCharge), nameof(AfterDeath)));
        }
        static void Attach(HoldoutZoneController __instance)
        {
            string scene=__instance.gameObject.scene.name;
            if(scene!="voidstage" && scene!="moon2" || __instance.GetComponent<Tracker>())return;
            var tracker=__instance.gameObject.AddComponent<Tracker>();tracker.Zone=__instance;tracker.Signal=scene=="voidstage";
            __instance.calcAccumulatedCharge+=tracker.Apply;zones.Add(tracker);
        }
        static void AfterDeath(DamageReport __0) => Killed(__0);
        static void Killed(DamageReport report)
        {
            if(!NetworkServer.active || report==null || report.attackerTeamIndex!=TeamIndex.Player || (report.victimTeamIndex!=TeamIndex.Monster && report.victimTeamIndex!=TeamIndex.Void))return;
            foreach(var tracker in zones)if(tracker && tracker.Charging)tracker.Pending+=C.Get(tracker.Signal?signalCharge:pillarCharge)/100;
        }
        internal sealed class Tracker:MonoBehaviour
        {
            internal HoldoutZoneController Zone;internal bool Signal;internal float Pending;
            internal bool Charging
            {
                get
                {
                    if(!Zone || !Zone.enabled || !gameObject.activeInHierarchy || Zone.charge>=1 || !C.Get(Signal?signals:pillars))return false;
                    foreach(var machine in GetComponentsInParent<EntityStateMachine>(true))
                        if(machine.state?.GetType().Name==(Signal?"Charging":"MoonBatteryActive") && (!Signal || machine.state.GetType().Namespace=="EntityStates.DeepVoidPortalBattery"))return true;
                    return false;
                }
            }
            internal void Apply(ref float charge){if(Charging)charge=Mathf.Clamp01(charge+Pending);Pending=0;}
            void OnDestroy(){zones.Remove(this);if(Zone)Zone.calcAccumulatedCharge-=Apply;}
        }
    }
}
