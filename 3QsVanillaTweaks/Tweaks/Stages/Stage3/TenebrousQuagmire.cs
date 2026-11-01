using System;
using HarmonyLib;
using UnityEngine;

namespace Tweaks.Stages.Stage3
{
    internal static class TenebrousQuagmire
    {
        internal static void Init()
        {
            var settings = StageFeatureSpawnHelper.Bind("Stages - Stage 3 - Tenebrous Quagmire - Hallowed Shrine", "mire", "Hallowed Shrine", -209.77f, 29.86f, 210.25f, 0.73f, -0.68f);
            StageFeatureSpawnHelper.Register(settings, (spawn, rng) =>
            {
                if (!SpawnRequirements.ExpansionEnabled("DLC4")) return;
                // The base spawns and owns the networked hands in Start. A standalone
                // GrantHexesController hand has no owner and cannot finish its use state.
                var prefab = StageInteractableAssets.Interactable("Hallow", "HallowingShrineSplitBase");
                var baseType = typeof(RoR2.Run).Assembly.GetType("HallowingShrineSplitBase");
                var shrineBase = baseType == null ? null : prefab.GetComponent(baseType);
                var nodes = shrineBase == null ? null :
                    AccessTools.Field(baseType, "NodePrefabsToSpawn")?.GetValue(shrineBase) as GameObject[];
                if (nodes == null || nodes.Length == 0)
                    throw new InvalidOperationException("Hallowed Shrine base is missing its hand prefabs.");
                foreach (var node in nodes)
                    if (!node)
                        throw new InvalidOperationException("Hallowed Shrine base contains a missing hand prefab.");
                StageFeatureSpawnHelper.Spawn(spawn, prefab);
            });
        }
    }
}
