using A = ThreeQsVanillaTweaks.ReflectionTools;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class TopazBrooch
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> flat, flatStack, percent, percentStack, chance, chanceStack;
        internal static void Init()
        {
            var s = S.Section("Topaz Brooch");
            enabled = S.Enable(s);
            flat = S.Num(s, "Flat Barrier", 10, "Flat barrier on kill. Vanilla: 15.");
            flatStack = S.Num(s, "Flat Barrier Per Stack", 0, "Extra flat barrier per additional stack. Vanilla: 15.");
            percent = S.Num(s, "Barrier (%)", 1, "Barrier based on maximum combined health on kill. Vanilla: 0%.");
            percentStack = S.Num(s, "Barrier Per Stack (%)", 1, "Additional percentage per extra stack.");
            chance = S.Num(s, "Trigger Chance (%)", 100, "Chance on each kill. Vanilla: 100%.", 0, 100);
            chanceStack = S.Num(s, "Trigger Chance Per Stack (%)", 0, "Extra chance per additional stack.", 0, 100);
            if (!enabled.Value)
                return;
            var h = S.Harmony("Topaz");
            S.Patch(h, typeof(GlobalEventManager), "OnCharacterDeath", typeof(TopazBrooch), postfix: nameof(After), transpiler: nameof(IL));
            S.Text("ITEM_BARRIERONKILL_DESC", () => "Killing an enemy grants <style=cIsHealing>" + C.Get(flat) + "</style> <style=cStack>(+" + C.Get(flatStack) + " per stack)</style> <style=cIsHealing>barrier</style> plus <style=cIsHealing>" + C.Get(percent) + "%</style> <style=cStack>(+" + C.Get(percentStack) + "% per stack)</style> maximum combined health.");
        }

        static int Suppress(int n) => S.Active(enabled) ? 0 : n;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            bool pending = false;
            foreach (var i in code)
            {
                yield return i;
                if (i.operand is FieldInfo f && f.Name == "BarrierOnKill")
                    pending = true;
                if (pending && i.operand is MethodInfo m && m.Name == "GetItemCountEffective")
                {
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(TopazBrooch), nameof(Suppress)));
                    pending = false;
                }
            }
        }

        static void After(DamageReport damageReport)
        {
            if (!NetworkServer.active || !S.Active(enabled) || damageReport == null)
                return;
            var b = damageReport.attackerBody;
            int n = S.Count(b, RoR2Content.Items.BarrierOnKill);
            if (n > 0 && b.healthComponent.alive && Util.CheckRoll(S.Total(chance, chanceStack, n), 0, b.master))
                b.healthComponent.AddBarrier(S.Total(flat, flatStack, n) + b.healthComponent.fullCombinedHealth * S.Total(percent, percentStack, n) / 100);
        }
    }
}
