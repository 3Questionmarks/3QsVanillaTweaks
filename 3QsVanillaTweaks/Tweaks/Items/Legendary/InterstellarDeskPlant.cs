using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A=ThreeQsVanillaTweaks.ReflectionTools;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using N=Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    // Native item ID: Plant. Configuration and hooks for this item only.
    internal static class InterstellarDeskPlant
    {
        const string Body="RoR2.CharacterBody",Health="RoR2.HealthComponent",Events="RoR2.GlobalEventManager";
        internal static void Init()=>LegendaryConfigSupport.Register("Plant",Configure);
        static void Configure(LegendaryConfig c)
        {
            var v=c.Values;
            c.StaticField("RoR2.DeskPlantController","seedDuration",new[]{"RoR2.DeskPlantController+SeedState|FixedUpdate"});
            c.StaticField("RoR2.DeskPlantController","mainDuration",new[]{"RoR2.DeskPlantController+MainState|FixedUpdate"});
            c.StaticField("RoR2.DeskPlantController","scaleDuration",new[]{"RoR2.DeskPlantController+SproutState|CalcScale","RoR2.DeskPlantController+SproutState|FixedUpdate","RoR2.DeskPlantController+DeathState|CalcScale","RoR2.DeskPlantController+DeathState|FixedUpdate"});
            c.Literal("RoR2.DeskPlantController","Awake",500f,"Awake - Value",occurrence:0);
            c.Literal("RoR2.DeskPlantController+MainState","OnEnter",0.05f,"On Enter - healFraction",occurrence:0);
            c.Assets("RoR2.DeskPlantController","RoR2.DeskPlantController+DeskPlantBaseState","RoR2.DeskPlantController+SeedState","RoR2.DeskPlantController+SproutState","RoR2.DeskPlantController+MainState","RoR2.DeskPlantController+DeathState");
            c.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/DeskplantWard"));
        }
    }
}
