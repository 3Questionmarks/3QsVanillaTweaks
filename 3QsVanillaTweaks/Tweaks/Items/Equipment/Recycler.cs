using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentRecycle
    {
        static EquipmentConfig c;
        static bool procGuardReady;

        internal static void Init() => RoR2Application.onLoad += Load;

        static void Load()
        {
            EquipmentConfig.MarkLoaded();
            c = new EquipmentConfig("Recycler", "Recycle", true);
            c.Flag("Short Cooldown While Time Is Paused", true,
                "Limit Recycler's cooldown while the run timer is paused. These uses do not trigger equipment-use effects. Vanilla: false.");
            c.N("Paused Timer Cooldown", 1f,
                "Maximum remaining cooldown in seconds while the run timer is paused. Shorter cooldowns are preserved. Vanilla: uses the normal equipment cooldown.", 0.01f);
            var values = new EquipmentNativeConfig(c.Values, c.Id);
            VanillaEquipmentDefaults.PreserveText(c, values);
            c.Patch(typeof(Inventory), "UpdateEquipment", typeof(VanillaEquipmentRecycle), postfix: nameof(ShortenPausedCooldown));
            if (c.Values.Enabled.Value)
                U.Harmony.Patch(A.Method(typeof(EquipmentSlot), "OnEquipmentExecuted",
                    new[] { typeof(EquipmentLocation).MakeByRefType(), typeof(EquipmentIndex) }),
                    transpiler: new HarmonyMethod(typeof(VanillaEquipmentRecycle), nameof(GuardEquipmentProcs)));
        }

        static bool PausedMode => c != null && c.Active && Run.instance
            && Run.instance.isRunStopwatchPaused && c.B("Short Cooldown While Time Is Paused");

        static bool SuppressProcs(EquipmentIndex index) => PausedMode
            && RoR2Content.Equipment.Recycle && index == RoR2Content.Equipment.Recycle.equipmentIndex;

        static T FilterActivationEvent<T>(T handler, EquipmentIndex index) where T : class
            => SuppressProcs(index) ? null : handler;

        static bool PreventUseEffects(bool original, EquipmentIndex index) => original || SuppressProcs(index);

        static IEnumerable<CodeInstruction> GuardEquipmentProcs(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>();
            int events = 0, flags = 0;
            foreach (var instruction in instructions)
            {
                result.Add(instruction);
                if (!(instruction.operand is FieldInfo field)) continue;
                if (instruction.opcode == OpCodes.Ldsfld && field.DeclaringType == typeof(EquipmentSlot)
                    && (field.Name == "onServerEquipmentActivated" || field.Name == "onServerEquipmentActivatedDetailed"
                        || field.Name == "m_onServerEquipmentActivated" || field.Name == "m_onServerEquipmentActivatedDetailed"))
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_2));
                    result.Add(new CodeInstruction(OpCodes.Call,
                        A.Method(typeof(VanillaEquipmentRecycle), nameof(FilterActivationEvent)).MakeGenericMethod(field.FieldType)));
                    events++;
                }
                else if (instruction.opcode == OpCodes.Ldfld && field.Name == "dontActivateOnEquipmentUseEffects")
                {
                    result.Add(new CodeInstruction(OpCodes.Ldarg_2));
                    result.Add(new CodeInstruction(OpCodes.Call, A.Method(typeof(VanillaEquipmentRecycle), nameof(PreventUseEffects))));
                    flags++;
                }
            }
            procGuardReady = events == 2 && flags == 1;
            if (!procGuardReady)
                throw new InvalidOperationException("Recycler: equipment-use proc guards were not found; paused-timer cooldown disabled.");
            return result;
        }

        static void ShortenPausedCooldown(Inventory __instance)
        {
            if (!NetworkServer.active || !procGuardReady || !PausedMode) return;

            var recycler = RoR2Content.Equipment.Recycle;
            if (!recycler) return;
            var latestFinish = Run.FixedTimeStamp.now + c.F("Paused Timer Cooldown");
            for (uint slot = 0; slot < __instance.equipmentSlotCount; slot++)
            for (uint set = 0; set < __instance.equipmentSetCount; set++)
            {
                var location = new EquipmentLocation { slot = slot, set = set };
                var state = __instance.GetEquipment(in location);
                // Infinity means no recharge is scheduled. Do not create extra charges.
                if (state.equipmentIndex != recycler.equipmentIndex || state.chargeFinishTime.isInfinity
                    || state.chargeFinishTime <= latestFinish) continue;
                var replacement = new EquipmentState(state.equipmentIndex, latestFinish, state.charges);
                __instance.SetEquipment(in replacement, in location);
            }
        }
    }
}
