using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using C = Config.ConfigManager;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Lunar
{
    internal static class GestureOfTheDrowned
    {
        static R c;
        static ConfigEntry<bool> pauseBazaar, pauseExchange;
        static readonly MethodInfo perform = A.Method(typeof(EquipmentSlot), "PerformEquipmentAction");
        internal static void Init()
        {
            U.Ensure(); c = new R("Gesture of the Drowned", "AutoCastEquipment", true, "Lunar");
            c.Pair("Equipment Cooldown Reduction (%)", 25, 25, "Multiplicative cooldown reduction per item. Vanilla: 50% first stack, 15% each extra.");
            c.B("Automatically Use Equipment", true, "Automatically activate ready equipment outside excluded stages. Vanilla: true.");
            c.Pair("Equipment Failure Chance (%)", 0, 10, "Chance to spend a ready charge without activating equipment or equipment-use effects. Vanilla: 0%.");
            pauseBazaar = C.BindBool(c.Section, "Pause Cooldowns in Bazaar Between Time", true, "Prevent automatic equipment use in the Bazaar; cooldowns still progress.");
            pauseExchange = C.BindBool(c.Section, "Pause Cooldowns in Computational Exchange", true, "Prevent automatic equipment use in Computational Exchange; cooldowns still progress.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(Inventory), "CalculateEquipmentCooldownScale", typeof(GestureOfTheDrowned), postfix: nameof(Cooldown));
                U.Harmony.Patch(A.Method(typeof(EquipmentSlot), "MyFixedUpdate"), transpiler: new HarmonyMethod(typeof(GestureOfTheDrowned), nameof(AutoIL)));
                U.Harmony.Patch(A.Method(typeof(EquipmentSlot), "Execute"), transpiler: new HarmonyMethod(typeof(GestureOfTheDrowned), nameof(FailureIL)));
                var all = A.Method(typeof(EquipmentSlot), "ExecuteAllIfReady");
                if (all != null) U.Harmony.Patch(all, transpiler: new HarmonyMethod(typeof(GestureOfTheDrowned), nameof(FailureIL)));
            }
            c.Text(Description, () => "Reduce equipment cooldown" + (c.B("Automatically Use Equipment") ? "... <style=cIsHealth>but automatically activate your equipment.</style>" : "."));
        }
        static string Description()
        {
            string text = "Reduce <style=cIsUtility>equipment cooldowns</style> by " + c.V("Equipment Cooldown Reduction (%)", "%", "cIsUtility") + ".";
            if (c.B("Automatically Use Equipment")) text += " <style=cIsHealth>Equipment activates automatically.</style>";
            if (c.F("Equipment Failure Chance (%)") != 0 || c.F("Equipment Failure Chance (%) Per Stack") != 0) text += " Equipment has a " + c.V("Equipment Failure Chance (%)", "%", "cIsHealth") + " chance to <style=cIsHealth>fail, consuming its charge</style>.";
            return text;
        }
        static void Cooldown(Inventory __instance, ref float __result)
        {
            int n = c.Active && __instance ? __instance.GetItemCountEffective(c.Item) : 0; if (n == 0) return;
            float wanted = Mathf.Max(.001f, 1 - c.F("Equipment Cooldown Reduction (%)") / 100) * Mathf.Pow(Mathf.Max(.001f, 1 - c.F("Equipment Cooldown Reduction (%) Per Stack") / 100), n - 1);
            __result *= wanted / (.5f * Mathf.Pow(.85f, n - 1));
        }
        static int AutoCount(int count)
        {
            string scene = SceneManager.GetActiveScene().name.ToLowerInvariant();
            bool blocked = C.Get(pauseBazaar) && (scene == "bazaar" || scene == "bazaarbetween") || C.Get(pauseExchange) && scene == "computationalexchange";
            return c.Active && (!c.B("Automatically Use Equipment") || blocked) ? 0 : count;
        }
        static IEnumerable<CodeInstruction> AutoIL(IEnumerable<CodeInstruction> input)
        {
            bool pending = false; int found = 0;
            foreach (var i in input)
            {
                yield return i;
                if (i.operand is FieldInfo f && f.Name == "AutoCastEquipment") pending = true;
                if (pending && i.operand is MethodInfo m && m.Name.StartsWith("GetItemCount"))
                { yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(GestureOfTheDrowned), nameof(AutoCount))); pending = false; found++; }
            }
            if (found != 2) throw new InvalidOperationException("Gesture auto-use anchors changed: " + found);
        }
        static IEnumerable<CodeInstruction> FailureIL(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            var code = input.ToList(); var location = __originalMethod.GetMethodBody().LocalVariables.FirstOrDefault(v => v.LocalType.Name == "EquipmentLocation");
            int found = 0;
            foreach (var i in code)
            {
                if (i.operand is MethodInfo m && m == perform)
                {
                    var extra = location == null ? new CodeInstruction(OpCodes.Ldnull) : new CodeInstruction(OpCodes.Ldloc, (short)location.LocalIndex);
                    extra.labels.AddRange(i.labels); i.labels.Clear(); yield return extra;
                    if (location != null) yield return new CodeInstruction(OpCodes.Box, location.LocalType);
                    i.opcode = OpCodes.Call; i.operand = A.Method(typeof(GestureOfTheDrowned), nameof(TryPerform)); found++;
                }
                yield return i;
            }
            if (found != 1) throw new InvalidOperationException("Gesture equipment activation anchor changed: " + __originalMethod.Name + " / " + found);
        }
        static object Read(object obj, string name) => A.Field(obj.GetType(), name)?.GetValue(obj) ?? A.Property(obj.GetType(), name)?.GetValue(obj, null);
        static bool TryPerform(EquipmentSlot slot, EquipmentDef equipment, object location)
        {
            int n = c.Count(slot.characterBody); float chance = n == 0 ? 0 : Mathf.Clamp(c.Total("Equipment Failure Chance (%)", n), 0, 100);
            if (NetworkServer.active && chance > 0 && U.Roll(chance, slot.characterBody))
            {
                location = location ?? Read(slot, "activeEquipmentLocation");
                var methods = slot.inventory.GetType().GetMethods().Where(m => m.Name == "DeductEquipmentCharges" && m.GetParameters().Length == 2).ToArray();
                var deduct = location == null ? methods.FirstOrDefault(m => m.GetParameters()[0].ParameterType == typeof(byte)) : methods.FirstOrDefault(m => m.GetParameters()[0].ParameterType.IsByRef && m.GetParameters()[0].ParameterType.GetElementType().IsInstanceOfType(location));
                if (deduct == null) throw new MissingMethodException("Gesture cannot deduct the failed equipment charge.");
                deduct.Invoke(slot.inventory, new object[] { location ?? Read(slot, "activeEquipmentSlot"), 1 });
                A.Method(typeof(EquipmentSlot), "UpdateInventory")?.Invoke(slot, null);
                return false; // Never call OnEquipmentExecuted, including global activation events.
            }
            return (bool)perform.Invoke(slot, new object[] { equipment });
        }
    }
}
