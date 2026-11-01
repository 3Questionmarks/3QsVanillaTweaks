using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class TriTipDagger
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> chance, chanceStack, duration, damage, damageStack;
        [ThreadStatic]
        static bool ownBleed;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Tri-Tip Dagger");
            chance = settings.Number("Bleed Chance (%)", 10, "First-stack bleed chance, multiplied by the triggering proc coefficient. Vanilla: 10%.");
            chanceStack = settings.Number("Bleed Chance Per Stack (%)", 10, "Additional bleed chance per item stack. Vanilla: 10%.");
            duration = settings.Number("Bleed Duration", 3, "Bleed duration before triggering proc-coefficient scaling. Vanilla: 3.");
            damage = settings.Number("Bleed Damage Per Second (%)", 80, "Base damage per second before bleed modifiers. Vanilla: 80%.");
            damageStack = settings.Number("Bleed Damage Per Second Per Stack (%)", 0, "Additional bleed damage per second per extra item. Vanilla: 0%.");
            settings.Count("RoR2.CharacterBody", "RecalculateStats", "BleedOnHit", 10, chance, chanceStack);
            if (!settings.Enabled.Value)
                return;
            var h = S.Harmony("TriTipDagger");
            h.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy"), prefix: new HarmonyMethod(typeof(TriTipDagger), nameof(Before)), finalizer: new HarmonyMethod(typeof(TriTipDagger), nameof(Finish)), ilmanipulator: null);
            S.Patch(h, typeof(DotController), "InflictDot", typeof(TriTipDagger), prefix: nameof(Dot), args: new[] { typeof(InflictDotInfo).MakeByRefType() });
            settings.Describe("ITEM_BLEEDONHIT_DESC", () => "Gain a <style=cIsDamage>" + C.Get(chance) + "%</style> <style=cStack>(+" + C.Get(chanceStack) + "% per stack)</style> chance on hit to <style=cIsDamage>bleed</style> for <style=cIsUtility>" + C.Get(duration) + " seconds</style> at <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(damageStack) + "% per stack)</style> base damage per second.");
        }

        static void Before(DamageInfo damageInfo, out bool __state)
        {
            __state = ownBleed;
            var b = damageInfo.attacker ? damageInfo.attacker.GetComponent<CharacterBody>() : null;
            bool forced = (damageInfo.damageType & DamageType.BleedOnHit) != 0 || (b && S.Count(b, RoR2Content.Items.BleedOnHitAndExplode) > 0 && damageInfo.crit);
            ownBleed = settings.Active && b && S.Count(b, RoR2Content.Items.BleedOnHit) > 0 && !forced;
        }

        static Exception Finish(bool __state, Exception __exception)
        {
            ownBleed = __state;
            return __exception;
        }

        static void Dot(ref InflictDotInfo inflictDotInfo)
        {
            if (!ownBleed || inflictDotInfo.dotIndex != DotController.DotIndex.Bleed || !inflictDotInfo.attackerObject)
                return;
            int n = S.Count(inflictDotInfo.attackerObject.GetComponent<CharacterBody>(), RoR2Content.Items.BleedOnHit);
            inflictDotInfo.duration *= C.Get(duration) / 3;
            inflictDotInfo.damageMultiplier *= S.Linear(n, C.Get(damage), C.Get(damageStack)) / 80;
        }
    }
}
