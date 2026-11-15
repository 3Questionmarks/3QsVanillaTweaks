using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using Glass = Tweaks.ModCompatibility.LookingGlassStatRegistry;
using Bloom = Tweaks.Items.Void.BenthicBloom;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Lunar
{
    internal static class Purity
    {
        const string CooldownKey = "Cooldown Reduction (s)";
        const string LuckKey = "Luck";
        static R settings;
        internal static bool StatsPatched { get; private set; }
        static bool Active => StatsPatched && (Bloom.SwapActive || settings.Active);

        internal static void Init()
        {
            settings = new R("Purity", "LunarBadLuck", true, "Lunar");
            settings.Pair(CooldownKey, -2, -1, "Flat skill cooldown change in seconds. Negative values reduce cooldowns. Vanilla: -2s, -1s per stack.", -100000);
            settings.Pair(LuckKey, -1, -1, "Luck change. Negative luck favours unfavourable outcomes. Vanilla: -1, -1 per stack.", -100000);

            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"),
                transpiler: new HarmonyMethod(typeof(Purity), nameof(RecalculateStatsIL)));
            Bloom.RegisterText(settings, () => Active,
                () => (Bloom.SwapActive ? Bloom.UpgradeDescription() : CooldownDescription()) + " " + LuckDescription(),
                () => (Bloom.SwapActive ? Bloom.UpgradePickup() : CooldownPickup()) + " " + LuckPickup());
            Glass.RegisterItem(() => RoR2Content.Items.LunarBadLuck ? RoR2Content.Items.LunarBadLuck.itemIndex : ItemIndex.None, (_, stacks, text) =>
            {
                string result = Bloom.SwapActive ? Bloom.UpgradeStat(stacks) : CooldownStat(stacks);
                return Glass.AddValue(result, "Luck", settings.Total(LuckKey, stacks), "", "#E58262");
            }, () => Active);
        }

        internal static string CooldownDescription()
        {
            float first = settings.F(CooldownKey), extra = settings.F(CooldownKey + " Per Stack");
            if (first <= 0f && extra <= 0f)
                return "Reduce <style=cIsUtility>skill cooldowns</style> by <style=cIsUtility>" + Number(-first) + "s</style>" +
                    StackText(-extra, "s") + ".";
            return "Change <style=cIsUtility>skill cooldowns</style> by " + settings.V(CooldownKey, "s", "cIsUtility") + ".";
        }

        static string LuckDescription()
        {
            float first = settings.F(LuckKey), extra = settings.F(LuckKey + " Per Stack");
            if (first < 0f && extra <= 0f)
                return "All random effects are rolled <style=cDeath>+" + Number(-first) + "</style>" +
                    StackText(-extra, "") + " times for an <style=cDeath>unfavourable outcome</style>.";
            return "Change your luck by " + settings.V(LuckKey, "", first < 0f ? "cDeath" : "cIsUtility") + ".";
        }

        static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        static string StackText(float extra, string unit) =>
            extra > 0f ? " <style=cStack>(+" + Number(extra) + unit + " per stack)</style>" : "";

        internal static string CooldownPickup() => settings.F(CooldownKey) <= 0f && settings.F(CooldownKey + " Per Stack") <= 0f ?
            "Reduce skill cooldowns." : "Change skill cooldowns.";

        static string LuckPickup() => settings.F(LuckKey) < 0f && settings.F(LuckKey + " Per Stack") <= 0f ?
            "<style=cDeath>Become unlucky</style>." : "Change your luck.";

        internal static string CooldownStat(int stacks) =>
            Glass.AddValue("", "Flat Skill Cooldown Change", settings.Total(CooldownKey, stacks), "s", "#91C8DD");

        static float AdjustCooldown(float nativeReduction, CharacterBody body)
        {
            if (!Active || !body.inventory)
                return nativeReduction;
            int purity = body.inventory.GetItemCountEffective(RoR2Content.Items.LunarBadLuck);
            int cooldownStacks = Bloom.SwapActive ? body.inventory.GetItemCountEffective(DLC1Content.Items.CloverVoid) : purity;
            // The game stores a positive reduction. Remove vanilla Purity's contribution before applying the signed setting.
            return nativeReduction - (purity > 0 ? purity + 1f : 0f) - settings.Total(CooldownKey, cooldownStacks);
        }

        static float AdjustLuck(float nativeLuck, CharacterBody body)
        {
            if (!Active || !body.inventory)
                return nativeLuck;
            int purity = body.inventory.GetItemCountEffective(RoR2Content.Items.LunarBadLuck);
            return nativeLuck + purity + settings.Total(LuckKey, purity);
        }

        static IEnumerable<CodeInstruction> RecalculateStatsIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var cooldownSetter = AccessTools.PropertySetter(typeof(GenericSkill), nameof(GenericSkill.flatCooldownReduction));
            var luckSetter = AccessTools.PropertySetter(typeof(CharacterMaster), nameof(CharacterMaster.luck));
            StatsPatched = cooldownSetter != null && luckSetter != null && code.Count(i => i.Calls(cooldownSetter)) == 4 &&
                code.Count(i => i.Calls(luckSetter)) == 1;
            if (!StatsPatched)
            {
                ThreeQsVanillaTweaks.Log.Warning("Purity: native cooldown or luck assignments could not be verified. Purity settings and the Benthic Bloom swap are disabled.");
                return code;
            }

            var updated = new List<CodeInstruction>();
            foreach (var instruction in code)
            {
                bool cooldown = instruction.Calls(cooldownSetter);
                if (cooldown || instruction.Calls(luckSetter))
                {
                    var body = new CodeInstruction(OpCodes.Ldarg_0);
                    body.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    updated.Add(body);
                    updated.Add(new CodeInstruction(OpCodes.Call, A.Method(typeof(Purity), cooldown ? nameof(AdjustCooldown) : nameof(AdjustLuck))));
                }
                updated.Add(instruction);
            }
            return updated;
        }
    }
}
