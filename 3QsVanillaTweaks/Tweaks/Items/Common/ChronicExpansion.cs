using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class ChronicExpansion
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> damage, extra, delay;
        static ConfigEntry<int> cap, capStack;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Chronic Expansion", true);
            damage = settings.Number("Damage Per Buff (%)", 4, "Base-damage bonus per kill buff at one item stack. Vanilla: 3.5%.");
            extra = settings.Number("Damage Per Buff Per Stack (%)", 2, "Extra bonus per kill buff per additional item stack. Vanilla: 1%.");
            cap = settings.Integer("Maximum Buffs", 10, "Maximum kill buffs at one item stack. Vanilla: 10.");
            capStack = settings.Integer("Maximum Buffs Per Stack", 5, "Extra maximum kill buffs per additional item. Vanilla: 5.");
            delay = settings.Number("Buff Reset Delay", 7, "Seconds without another kill before the kill buffs reset. Vanilla: 7.");
            settings.Literal("RoR2.CharacterBody", "AddIncreasedDamageMultiKillTime", 7, () => C.Get(delay));
            if (!settings.Enabled.Value)
                return;
            var h = S.Harmony("ChronicExpansion");
            S.Patch(h, typeof(CharacterBody), "GetMaxIncreasedDamageMultiKillBuffsForCharacter", typeof(ChronicExpansion), postfix: nameof(Cap));
            S.Patch(h, typeof(CharacterBody), "GetIncreasedDamageFromMultiKillBuffsForCharacter", typeof(ChronicExpansion), postfix: nameof(Damage));
            settings.Describe("ITEM_INCREASEDAMAGEONMULTIKILL_DESC", () => "Each kill increases <style=cIsDamage>damage</style> by <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style>, up to <style=cIsUtility>" + C.Get(cap) + "</style> <style=cStack>(+" + C.Get(capStack) + " per stack)</style> times. Resets after <style=cIsUtility>" + C.Get(delay) + " seconds</style> without another kill.");
        }

        static void Cap(CharacterBody __instance, ref int __result)
        {
            if (settings.Active)
                __result = (int)S.Linear(S.Count(__instance, DLC2Content.Items.IncreaseDamageOnMultiKill), C.Get(cap), C.Get(capStack));
        }

        static void Damage(int itemCount, int buffCount, ref float __result)
        {
            if (settings.Active)
                __result = buffCount * S.Linear(itemCount, C.Get(damage), C.Get(extra)) / 100;
        }
    }
}
