using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class BolsteringLantern
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> attack, extra, radius, refresh;
        static ConfigEntry<int> cap, capStack;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Bolstering Lantern", true);
            attack = settings.Number("Attack Speed Per Nearby Body (%)", 10, "Total attack-speed bonus per nearby body at one item stack. Intended vanilla: 10%; the native duplicate contribution is removed.");
            extra = settings.Number("Attack Speed Per Stack (%)", 5, "Additional attack speed per nearby body per extra item stack. Vanilla: 3.5%.");
            radius = settings.Number("Radius", 20, "Detection radius in metres. Vanilla: 20.");
            refresh = settings.Number("Refresh Interval", 3, "Periodic nearby-body refresh interval in seconds. Vanilla: 3.", .02f);
            cap = settings.Integer("Maximum Nearby Bodies", 3, "Maximum nearby bodies counted at one stack. Vanilla: 3.");
            capStack = settings.Integer("Maximum Nearby Bodies Per Stack", 1, "Additional counted bodies per extra stack. Vanilla: 1.");
            if (!settings.Enabled.Value)
                return;
            var h = S.Harmony("BolsteringLantern");
            S.Patch(h, typeof(CharacterBody), "RecalculateStats", typeof(BolsteringLantern), transpiler: nameof(StatsIL));
            S.Patch(h, typeof(AttackSpeedPerNearbyCollider), "UpdateValues", typeof(BolsteringLantern), postfix: nameof(Values));
            S.Patch(h, typeof(AttackSpeedPerNearbyCollider), "TryUpdateCharactersInRange", typeof(BolsteringLantern), prefix: nameof(Radius));
            settings.Describe("ITEM_ATTACKSPEEDPERNEARBYALLYORENEMY_DESC", () => "Gain <style=cIsDamage>" + C.Get(attack) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> <style=cIsDamage>attack speed</style> for each enemy or ally within <style=cIsUtility>" + C.Get(radius) + "m</style>, up to <style=cIsUtility>" + C.Get(cap) + "</style> <style=cStack>(+" + C.Get(capStack) + " per stack)</style> nearby bodies.");
        }

        static float Extra(float native) => settings.Active ? C.Get(extra) / 100 : native;
        static float Base(float native) => settings.Active ? (C.Get(attack) - C.Get(extra)) / 100 : native;
        static float Duplicate(float native) => settings.Active ? 0 : native;

        static IEnumerable<CodeInstruction> StatsIL(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            int first = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "AttackSpeedPerNearbyAllyOrEnemyBuff");
            if (first < 0) throw new InvalidOperationException("Bolstering Lantern buff anchor missing.");
            foreach (var pair in new[] { new { Value = .035f, Method = nameof(Extra) }, new { Value = .065f, Method = nameof(Base) } })
            {
                int at = code.FindIndex(first, Math.Min(40, code.Count - first), i => i.LoadsConstant(pair.Value));
                if (at < 0) throw new InvalidOperationException("Bolstering Lantern stacking coefficient missing: " + pair.Value);
                code.Insert(at + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(BolsteringLantern), pair.Method)));
            }
            // The first two references belong to the stacking block. The third is
            // a separate, unconditional 10% addition for the same buff count.
            int reference = 0, duplicate = -1;
            for (int i = 0; i < code.Count; i++)
                if (code[i].operand is FieldInfo f && f.Name == "AttackSpeedPerNearbyAllyOrEnemyBuff" && ++reference == 3)
                {
                    duplicate = code.FindIndex(i, Math.Min(10, code.Count - i), instruction => instruction.LoadsConstant(.1f));
                    break;
                }
            if (duplicate < 0) throw new InvalidOperationException("Bolstering Lantern duplicate attack-speed contribution missing.");
            code.Insert(duplicate + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(BolsteringLantern), nameof(Duplicate))));
            return code;
        }

        static void Values(AttackSpeedPerNearbyCollider __instance, int itemCount, ref float diameter)
        {
            if (!settings.Active)
                return;
            diameter = 2 * C.Get(radius);
            A.Field(typeof(AttackSpeedPerNearbyCollider), "maxCharacterCount").SetValue(__instance, (int)S.Linear(itemCount, C.Get(cap), C.Get(capStack)));
            A.Field(typeof(AttackSpeedPerNearbyCollider), "syncInterval").SetValue(__instance, C.Get(refresh));
        }

        static void Radius(ref float radius)
        {
            if (settings.Active)
                radius = C.Get(BolsteringLantern.radius);
        }
    }
}
