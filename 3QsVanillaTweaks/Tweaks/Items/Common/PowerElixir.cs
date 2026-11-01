using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class PowerElixir
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> heal, healStack, barrier, barrierStack, threshold;
        static ConfigEntry<int> consume;
        internal static void Init()
        {
            var s = S.Section("Power Elixir");
            enabled = S.Enable(s);
            heal = S.Num(s, "Health Restored (%)", 25, "Health restored when consumed. Vanilla: 75%.");
            healStack = S.Num(s, "Health Restored Per Stack (%)", 0, "Extra healing for each additional held elixir.");
            barrier = S.Num(s, "Barrier Gained (%)", 50, "Barrier based on maximum combined health. Vanilla: 0%.");
            barrierStack = S.Num(s, "Barrier Gained Per Stack (%)", 0, "Extra barrier per additional held elixir.");
            threshold = S.Num(s, "Consume Threshold (%)", 25, "Consume after taking damage while at or below this combined health and shield percentage. Vanilla: 25%.", 0, 100);
            consume = S.Int(s, "Items Consumed Per Trigger", 1, "Elixirs consumed by one trigger. Vanilla: 1.", 1);
            if (!enabled.Value)
                return;
            S.Ensure();
            S.Suppressed["healingPotion"] = () => S.Active(enabled);
            S.Hit += Hit;
            S.Text("ITEM_HEALINGPOTION_DESC", () => "When damaged below <style=cIsHealth>" + C.Get(threshold) + "% health</style>, <style=cIsUtility>consume</style> an elixir to restore <style=cIsHealing>" + C.Get(heal) + "% health</style> and gain <style=cIsHealing>" + C.Get(barrier) + "%</style> maximum combined health as <style=cIsHealing>barrier</style>.");
        }

        static void Hit(DamageReport r)
        {
            var b = r.victimBody;
            if (!S.Active(enabled) || !b)
                return;
            int n = S.Count(b, DLC1Content.Items.HealingPotion);
            if (n <= 0 || b.healthComponent.combinedHealth > b.healthComponent.fullCombinedHealth * C.Get(threshold) / 100)
                return;
            S.Consume(b, DLC1Content.Items.HealingPotion, DLC1Content.Items.HealingPotionConsumed, C.Get(consume));
            b.healthComponent.HealFraction(S.Total(heal, healStack, n) / 100, default(ProcChainMask));
            b.healthComponent.AddBarrier(b.healthComponent.fullCombinedHealth * S.Total(barrier, barrierStack, n) / 100);
        }
    }
}
