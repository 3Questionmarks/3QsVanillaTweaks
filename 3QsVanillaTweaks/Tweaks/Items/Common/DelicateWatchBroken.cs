using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class DelicateWatchBroken
    {
        static ConfigEntry<bool> enabled, scrap, regen;
        static ConfigEntry<float> damage, extra;
        internal static float CurrentBonus(int stacks) => S.Active(enabled) ? S.Total(damage, extra, stacks) : 0;
        internal static void Init()
        {
            var s = S.Section("Delicate Watch (Broken)");
            enabled = S.Enable(s);
            scrap = C.BindBool(s, "Can Scrap", true, "Allow broken watches to be scrapped into Common Scrap without adding them to normal loot pools.");
            regen = C.BindBool(s, "Regenerate At Start Of Stage", true, "Restore all permanent broken watches at the start of each stage.");
            damage = S.Num(s, "Damage (%)", 2.5f, "First broken watch damage bonus. Vanilla: 0%.");
            extra = S.Num(s, "Damage Per Stack (%)", 2.5f, "Bonus per additional broken watch. Vanilla: 0%.");
            if (!enabled.Value)
                return;
            S.Ensure();
            S.ScrapAllowed["FragileDamageBonusConsumed"] = () => S.Active(enabled) && C.Get(scrap);
            S.Stats += b =>
            {
                if (!S.Active(enabled))
                    return;
                int n = S.Count(b, DLC1Content.Items.FragileDamageBonusConsumed);
                if (n > 0)
                    S.Set(b, "damage", b.damage * (1 + S.Total(damage, extra, n) / 100));
            };
            Stage.onStageStartGlobal += stage =>
            {
                if (S.Active(enabled) && C.Get(regen) && !DelicateWatchRework.Active)
                {
                    foreach (var master in CharacterMaster.readOnlyInstancesList)
                        if (master && master.inventory && master.inventory.GetItemCountPermanent(DLC1Content.Items.FragileDamageBonusConsumed.itemIndex) > 0)
                            DelicateWatchRework.Reset(master);
                    S.Regenerate(DLC1Content.Items.FragileDamageBonusConsumed, DLC1Content.Items.FragileDamageBonus);
                }
            };
            S.Text("ITEM_FRAGILEDAMAGEBONUSCONSUMED_DESC", () => "Gain <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> <style=cIsDamage>damage</style>." + ((DelicateWatchRework.Active ? DelicateWatchRework.RepairOnStage : C.Get(regen)) ? " <style=cIsUtility>Restores</style> at the start of the next stage." : ""));
        }
    }
}
