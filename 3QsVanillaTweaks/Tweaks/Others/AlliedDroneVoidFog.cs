using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Others
{
    internal static class AlliedDroneVoidFog
    {
        static ConfigEntry<bool> enabled;
        internal static void Init()
        {
            enabled = C.BindBool("Other - Allied Drones", "Immune To Void Fog", true, "Player-team drones take no damage from native void fog. Other damage and other teams are unchanged. Vanilla: drones take half fog damage.");
            new Harmony("3Questionmarks.3QsVanillaTweaks.DroneVoidFog").Patch(A.Method(typeof(FogDamageController), "MyFixedUpdate"), transpiler: new HarmonyMethod(typeof(AlliedDroneVoidFog), nameof(FogIL)));
        }
        static void ApplyFogDamage(HealthComponent health, DamageInfo info)
        {
            if (C.Get(enabled) && health && health.body && health.body.IsDrone && health.body.teamComponent && health.body.teamComponent.teamIndex == TeamIndex.Player) return;
            health.TakeDamage(info);
        }
        static IEnumerable<CodeInstruction> FogIL(IEnumerable<CodeInstruction> input)
        {
            int found=0;
            foreach(var x in input)
            {
                if (x.operand is MethodInfo m && m.DeclaringType == typeof(HealthComponent) && m.Name == "TakeDamage") { x.opcode=OpCodes.Call; x.operand=A.Method(typeof(AlliedDroneVoidFog), nameof(ApplyFogDamage)); found++; }
                yield return x;
            }
            if(found!=1)throw new InvalidOperationException("Void fog damage anchor changed: "+found);
        }
    }
}
