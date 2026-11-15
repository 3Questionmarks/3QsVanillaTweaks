using System.Collections.ObjectModel;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Others.RemoteOperation
{
    internal static class RemoteDroneTeleporterCharge
    {
        private const string Section = "Remote Operation - General";
        private static ConfigEntry<bool> enabled;
        private static HoldoutZoneController updatingZone;

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Drones Can Charge Teleporter", false,
                "Allow player-controlled Remote Operation drones inside the Teleporter holdout zone to contribute to its charge. Drones outside the zone do not slow charging. Vanilla: False.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteDroneTeleporterCharge");
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "DoUpdate"),
                prefix: new HarmonyMethod(typeof(RemoteDroneTeleporterCharge), nameof(BeforeUpdate)),
                postfix: new HarmonyMethod(typeof(RemoteDroneTeleporterCharge), nameof(AfterUpdate)),
                finalizer: new HarmonyMethod(typeof(RemoteDroneTeleporterCharge), nameof(FinalizeUpdate)));
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "CountLivingPlayers"),
                postfix: new HarmonyMethod(typeof(RemoteDroneTeleporterCharge), nameof(AfterCountLivingPlayers)));
            harmony.Patch(AccessTools.Method(typeof(HoldoutZoneController), "CountPlayersInRadius"),
                postfix: new HarmonyMethod(typeof(RemoteDroneTeleporterCharge), nameof(AfterCountPlayersInRadius)));
        }

        private static bool ActiveZone(HoldoutZoneController zone)
        {
            if (enabled == null || !C.Get(enabled) || !zone) return false;
            TeleporterInteraction teleporter = TeleporterInteraction.instance;
            return teleporter && teleporter.holdoutZoneController == zone;
        }

        private static void BeforeUpdate(HoldoutZoneController __instance)
        {
            updatingZone = ActiveZone(__instance) ? __instance : null;
        }

        private static void AfterUpdate()
        {
            updatingZone = null;
        }

        private static System.Exception FinalizeUpdate(System.Exception __exception)
        {
            updatingZone = null;
            return __exception;
        }

        private static int CountDronesInside(TeamIndex team)
        {
            HoldoutZoneController zone = updatingZone;
            if (!ActiveZone(zone) || team != TeamIndex.Player) return 0;
            int count = 0;
            ReadOnlyCollection<TeamComponent> members = TeamComponent.GetTeamMembers(team);
            foreach (TeamComponent member in members)
            {
                CharacterBody body = member ? member.body : null;
                if (body && body.isRemoteOp && body.master &&
                    body.master.playerCharacterMasterController &&
                    body.healthComponent && body.healthComponent.alive &&
                    zone.IsBodyInChargingRadius(body)) count++;
            }
            return count;
        }

        private static void AfterCountLivingPlayers(TeamIndex teamIndex, ref int __result)
        {
            __result += CountDronesInside(teamIndex);
        }

        private static void AfterCountPlayersInRadius(HoldoutZoneController holdoutZoneController,
            TeamIndex teamIndex, ref int __result)
        {
            if (holdoutZoneController == updatingZone)
                __result += CountDronesInside(teamIndex);
        }
    }
}
