using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class PowerElixirConsumed
    {
        static ConfigEntry<bool> enabled, scrap, regen, onHit;
        static ConfigEntry<float> heal, healStack, barrier, barrierStack, chance, chanceStack;
        internal static void Init()
        {
            var s = S.Section("Power Elixir (Consumed)");
            enabled = S.Enable(s);
            scrap = C.BindBool(s, "Can Scrap", true, "Allow consumed elixirs to become Common Scrap.");
            regen = C.BindBool(s, "Regenerate At Start Of Stage", true, "Restore permanent consumed elixirs at the start of each stage.");
            onHit = C.BindBool(s, "Health And Barrier On Taking Damage", true, "Grant the configured healing and barrier after each accepted damaging hit.");
            heal = S.Num(s, "Health Restored (%)", .5f, "Healing per hit from the first consumed elixir. Vanilla: 0%.");
            healStack = S.Num(s, "Health Restored Per Stack (%)", .5f, "Extra healing per additional consumed elixir.");
            barrier = S.Num(s, "Barrier Gained (%)", 1, "Combined-health barrier per hit from the first consumed elixir.");
            barrierStack = S.Num(s, "Barrier Gained Per Stack (%)", 1, "Extra combined-health barrier per additional consumed elixir.");
            chance = S.Num(s, "Trigger Chance (%)", 100, "Chance per accepted damaging hit.", 0, 100);
            chanceStack = S.Num(s, "Trigger Chance Per Stack (%)", 0, "Extra chance per additional consumed elixir.", 0, 100);
            if (!enabled.Value)
                return;
            S.Ensure();
            S.ScrapAllowed["HealingPotionConsumed"] = () => S.Active(enabled) && C.Get(scrap);
            Stage.onStageStartGlobal += stage =>
            {
                if (S.Active(enabled) && C.Get(regen))
                    S.Regenerate(DLC1Content.Items.HealingPotionConsumed, DLC1Content.Items.HealingPotion);
            };
            S.Hit += Hit;
            S.Text("ITEM_HEALINGPOTIONCONSUMED_DESC", () => "Taking damage <style=cIsHealing>restores</style> <style=cIsHealing>" + C.Get(heal) + "%</style> <style=cStack>(+" + C.Get(healStack) + "% per stack)</style> health and grants <style=cIsHealing>" + C.Get(barrier) + "%</style> <style=cStack>(+" + C.Get(barrierStack) + "% per stack)</style> maximum combined health as <style=cIsHealing>barrier</style>." + (C.Get(regen) ? " <style=cIsUtility>Regenerates</style> each stage." : ""));
        }

        static void Hit(DamageReport r)
        {
            var b = r.victimBody;
            if (!S.Active(enabled) || !C.Get(onHit) || !b || !b.healthComponent.alive)
                return;
            int n = S.Count(b, DLC1Content.Items.HealingPotionConsumed);
            if (n <= 0 || !Util.CheckRoll(S.Total(chance, chanceStack, n), 0, b.master))
                return;
            b.healthComponent.HealFraction(S.Total(heal, healStack, n) / 100, default(ProcChainMask));
            b.healthComponent.AddBarrier(b.healthComponent.fullCombinedHealth * S.Total(barrier, barrierStack, n) / 100);
        }
    }
}
