using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class WarpedEcho
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> retained, interval, cooldown, initial;
        static ConfigEntry<int> hits, extra;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Warped Echo");
            retained = settings.Number("Damage Retained (%)", 90, "Damage retained after splitting; 90 means 10% damage reduction. Native default: 90%.", 0, 100);
            interval = settings.Number("Hit Interval", .75f, "Seconds between delayed hits. Native default: 0.75.");
            cooldown = settings.Number("Recharge Time", 10, "Seconds to recharge after consumption. Native default: 10.", .02f);
            initial = settings.Number("Initial Recharge Delay", 15, "Initial body recharge timer. Native default: 15.");
            hits = settings.Integer("Delayed Hits", 3, "Delayed hits from one stack. Native default: 3.", 1);
            extra = settings.Integer("Delayed Hits Per Stack", 1, "Additional delayed hits per stack. Native default: 1.");
            settings.Literal("RoR2.HealthComponent", "TakeDamageProcess", .9f, () => C.Get(retained) / 100, "DelayedDamageBuff", window: 100);
            settings.Literal("RoR2.HealthComponent", "TakeDamageProcess", .75f, () => C.Get(interval), "DelayedDamageBuff", window: 300);
            if (!settings.Enabled.Value)
                return;
            var h = S.Harmony("WarpedEcho");
            S.Patch(h, typeof(HealthComponent), "TakeDamageProcess", typeof(WarpedEcho), transpiler: nameof(IL));
            S.Patch(h, typeof(CharacterBody), "UpdateDelayedDamage", typeof(WarpedEcho), prefix: nameof(Update));
            CharacterBody.onBodyStartGlobal += b =>
            {
                if (settings.Active)
                    A.Field(typeof(CharacterBody), "delayedDamageRefreshTimer").SetValue(b, C.Get(initial));
            };
            settings.Describe("ITEM_DELAYEDDAMAGE_DESC", () => "Every <style=cIsUtility>" + C.Get(cooldown) + " seconds</style>, split an incoming hit into <style=cIsUtility>" + C.Get(hits) + "</style> <style=cStack>(+" + C.Get(extra) + " per stack)</style> delayed hits spaced <style=cIsUtility>" + C.Get(interval) + " seconds</style> apart, retaining <style=cIsHealth>" + C.Get(retained) + "%</style> of its damage.");
        }

        static void Update(CharacterBody __instance)
        {
            if (settings.Active)
                A.Field(typeof(CharacterBody), "delayedDamageRefreshTime").SetValue(__instance, C.Get(cooldown));
        }

        static int Count(int n) => settings.Active ? (int)S.Linear(n, C.Get(hits), C.Get(extra)) - 2 : n;
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> source)
        {
            bool pending = false;
            bool patched = false;
            foreach (var i in source)
            {
                yield return i;
                if (i.operand is FieldInfo f && f.Name == "DelayedDamage")
                    pending = true;
                if (pending && i.operand is MethodInfo m && m.Name == "GetItemCountEffective")
                {
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(WarpedEcho), nameof(Count)));
                    pending = false;
                    patched = true;
                }
            }

            if (!patched)
                throw new InvalidOperationException("Warped Echo hit count missing");
        }
    }
}
