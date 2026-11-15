using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class DelicateWatch
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> damage, extra, threshold;
        static bool installed, contentLoaded;
        const string DescriptionToken = "3QS_ITEM_FRAGILEDAMAGEBONUS_DESC";
        static string nativeDescriptionToken;
        static bool descriptionLogged;
        internal static bool Active => DelicateWatchRework.Active || S.Active(enabled);

        internal static void Init()
        {
            var s = S.Section("Delicate Watch");
            enabled = S.Enable(s);
            damage = S.Num(s, "Damage (%)", 15, "First-stack damage bonus. Vanilla: 20%.");
            extra = S.Num(s, "Damage Per Stack (%)", 15, "Extra damage per additional stack. Vanilla: 20%.");
            threshold = S.Num(s, "Break Threshold (%)", 25, "Break all watches after taking damage while at or below this combined health and shield percentage. Ignored by the optional wear-down rework. Vanilla: 25%.", 0, 100);
            if (!enabled.Value)
                return;
            Install();
        }

        internal static void Install()
        {
            // The rework is a complete alternative, including when normal Watch
            // changes (or their separate module toggle) are disabled.
            if (installed)
                return;
            installed = true;
            S.Ensure();
            S.Suppressed["FragileDamageBonus"] = () => Active;
            S.Suppressed["fragileDamageBonus"] = () => Active;
            S.Stats += Stats;
            S.Hit += Hit;
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => DLC1Content.Items.FragileDamageBonus ? DLC1Content.Items.FragileDamageBonus.itemIndex : ItemIndex.None, (master, stacks, text) =>
            {
                if (!Active)
                    return text;
                float bonus = CurrentBonus(master ? master.GetBody() : null, stacks);
                if (text.IndexOf("Bonus Damage:", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return Tweaks.ModCompatibility.LookingGlassStatRegistry.ReplaceValue(text, "Bonus Damage:", bonus, "#E5C962");
                return Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue(text, "Current Damage Bonus", bonus, "%", "#E5C962");
            }, () => Active);
            // Bind only after all item modules have selected their modes. The
            // original token may already have been cached by language extensions.
            RoR2Application.onLoad += () =>
            {
                contentLoaded = true;
                RefreshDescription();
            };
        }

        internal static void RefreshDescription()
        {
            if (!contentLoaded)
                return;
            var item = DLC1Content.Items.FragileDamageBonus;
            if (!item)
                return;
            if (nativeDescriptionToken == null)
                nativeDescriptionToken = item.descriptionToken;
            // This token is never initially populated with normal Watch wording.
            // Logbook entries and item tooltips resolve it from the ItemDef.
            S.Text(DescriptionToken, Description);
            if (!string.IsNullOrEmpty(nativeDescriptionToken))
                S.Text(nativeDescriptionToken, Description);
            S.Text("ITEM_FRAGILEDAMAGEBONUS_DESC", Description);
            item.descriptionToken = DescriptionToken;
            if (!descriptionLogged)
            {
                descriptionLogged = true;
                ThreeQsVanillaTweaks.Log.Info("Delicate Watch description bound to " + DescriptionToken + (DelicateWatchRework.Active ? " (wear-down rework)." : " (normal Watch)."));
            }
        }

        static string Description()
        {
            if (DelicateWatchRework.Active)
                return DelicateWatchRework.Description();
            if (!S.Active(enabled))
                return "Increase damage by <style=cIsDamage>20%</style> <style=cStack>(+20% per stack)</style>. Breaks below <style=cIsHealth>25% health</style> after taking damage.";
            return "Increase damage by <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style>. Breaks below <style=cIsHealth>" + C.Get(threshold) + "% health</style> after taking damage.";
        }

        internal static float CurrentBonus(CharacterBody body, int stacks) => DelicateWatchRework.Active ? DelicateWatchRework.Bonus(body, stacks) : S.Active(enabled) ? S.Total(damage, extra, stacks) : 20 * stacks;
        static void Stats(CharacterBody b)
        {
            if (!Active)
                return;
            if (DelicateWatchRework.Active)
                DelicateWatchRework.Synchronize(b);
            int n = S.Count(b, DLC1Content.Items.FragileDamageBonus);
            float bonus = CurrentBonus(b, n);
            if (bonus != 0)
                S.Set(b, "damage", b.damage * (1 + bonus / 100));
        }

        static void Hit(DamageReport r)
        {
            var b = r.victimBody;
            if (!Active || !b || S.Count(b, DLC1Content.Items.FragileDamageBonus) <= 0)
                return;
            if (DelicateWatchRework.Active)
            {
                DelicateWatchRework.DamageTaken(b, r.damageInfo);
                return;
            }

            if (b.healthComponent.combinedHealth <= b.healthComponent.fullCombinedHealth * C.Get(threshold) / 100)
                S.Consume(b, DLC1Content.Items.FragileDamageBonus, DLC1Content.Items.FragileDamageBonusConsumed, int.MaxValue);
        }
    }
}
