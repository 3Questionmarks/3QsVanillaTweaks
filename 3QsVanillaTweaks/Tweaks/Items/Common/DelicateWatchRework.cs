using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using R2API;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class DelicateWatchRework
    {
        static ConfigEntry<bool> enabled, repair;
        internal static bool RepairOnStage => Active && C.Get(repair);

        static ConfigEntry<float> damage, extra, loss, lossStack, threshold, thresholdStack;
        static BuffDef wear;
        sealed class State
        {
            internal int Hits;
        }
        internal static float BreakThreshold(int stacks) =>
            S.Total(threshold, thresholdStack, stacks);

        static ConditionalWeakTable<CharacterMaster, State> states = new ConditionalWeakTable<CharacterMaster, State>();
        internal static bool Active => S.Active(enabled);

        internal static void Init()
        {
            var s = S.Section("Delicate Watch - Rework");
            enabled = C.BindStartupBool(s, "Enabled", true, "Use the wear-down rework instead of all normal Delicate Watch settings. Restart to apply.");
            repair = C.BindBool(s, "Fully Repair At Start Of Stage", true, "Reset all wear and restore broken permanent watches at the start of each stage. Overrides broken-watch regeneration while the rework is active.");
            damage = S.Num(s, "Damage (%)", 15, "Initial damage bonus from one watch.");
            extra = S.Num(s, "Damage Per Stack (%)", 15, "Initial bonus added by each additional watch.");
            loss = S.Num(s, "Damage Lost Per Hit (%)", 1, "Percentage points of bonus damage lost per accepted damaging hit.");
            lossStack = S.Num(s, "Damage Lost Per Hit Per Stack (%)", 0, "Extra loss per hit for each additional watch.");
            threshold = S.Num(s, "Break Threshold (%)", 5, "If already at or below this remaining bonus when hit, break all watches.");
            thresholdStack = S.Num(s, "Break Threshold Per Stack (%)", 5, "Additional break threshold per extra watch.");
            if (!enabled.Value)
            {
                ThreeQsVanillaTweaks.Log.Info("Delicate Watch: rework disabled; normal settings apply when enabled.");
                return;
            }

            ThreeQsVanillaTweaks.Log.Info("Delicate Watch: wear-down rework enabled; normal Watch settings are ignored.");
            Stage.onStageStartGlobal += stage =>
            {
                if (!RepairOnStage || !NetworkServer.active)
                    return;
                foreach (var master in CharacterMaster.readOnlyInstancesList)
                    if (master)
                        Reset(master);
                S.Regenerate(DLC1Content.Items.FragileDamageBonusConsumed, DLC1Content.Items.FragileDamageBonus);
            };
            wear = ScriptableObject.CreateInstance<BuffDef>();
            wear.name = "3QS_WATCH_WEAR";
            wear.canStack = true;
            wear.isDebuff = false;
            wear.buffColor = Color.yellow;
            ContentAddition.AddBuffDef(wear);
            RoR2Application.onLoad += () =>
            {
                wear.iconSprite = DLC1Content.Buffs.OutOfCombatArmorBuff.iconSprite;
            };
            RoR2Application.onLoad += DelicateWatch.RefreshDescription;
            CharacterBody.onBodyStartGlobal += b =>
            {
                if (NetworkServer.active && b.master && wear)
                    b.SetBuffCount(wear.buffIndex, states.GetValue(b.master, k => new State()).Hits);
            };
            DelicateWatch.Install();
            DelicateWatch.RefreshDescription();
            S.BuffTexts[wear.name] = new S.BuffText
            {
                Title = "Watch Wear",
                Text = icon => icon.buffCount + " damaging hits have worn down your Delicate Watches. Each hit removes " + C.Get(loss) + " percentage points of bonus damage before additional-stack scaling. " + Description()
            };
        }

        internal static float Bonus(CharacterBody b, int n)
        {
            if (n <= 0)
                return 0;
            int hits = wear && b ? b.GetBuffCount(wear) : 0;
            return CalculateBonus(n, hits, C.Get(damage), C.Get(extra), C.Get(loss), C.Get(lossStack));
        }

        internal static float CalculateBonus(int stacks, int hits, float first, float additional, float lossPerHit, float additionalLoss) => Math.Max(0, S.Linear(stacks, first, additional) - hits * S.Linear(stacks, lossPerHit, additionalLoss));
        internal static void DamageTaken(CharacterBody b)
        {
            int n = S.Count(b, DLC1Content.Items.FragileDamageBonus);
            if (n <= 0 || !b.master)
                return;
            var state = states.GetValue(b.master, k => new State());
            if (Bonus(b, n) <= S.Total(threshold, thresholdStack, n))
            {
                S.Consume(b, DLC1Content.Items.FragileDamageBonus, DLC1Content.Items.FragileDamageBonusConsumed, int.MaxValue);
                state.Hits = 0;
            }
            else
                state.Hits = Math.Min(100000, state.Hits + 1);
            if (wear)
                b.SetBuffCount(wear.buffIndex, state.Hits);
            b.RecalculateStats();
        }

        internal static void Reset(CharacterMaster master)
        {
            if (!NetworkServer.active || !master)
                return;
            states.Remove(master);
            var b = master.GetBody();
            if (b && wear)
            {
                b.SetBuffCount(wear.buffIndex, 0);
                b.RecalculateStats();
            }
        }

        internal static void Synchronize(CharacterBody b)
        {
            if (!NetworkServer.active || !wear || !b.master)
                return;
            var state = states.GetValue(b.master, k => new State());
            var inventory = b.inventory;
            var item = DLC1Content.Items.FragileDamageBonus;
            if (inventory && S.TotalHeld(inventory, item.itemIndex) == 0)
                state.Hits = 0;
            if (b.GetBuffCount(wear) != state.Hits)
                b.SetBuffCount(wear.buffIndex, state.Hits);
        }

        internal static string Description()
        {
            string text = "";
            if (C.Get(damage) != 0 || C.Get(extra) != 0)
                text = "Gain <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> <style=cIsDamage>damage</style>. ";
            if (C.Get(loss) != 0 || C.Get(lossStack) != 0)
                text += "Each damaging hit removes <style=cIsHealth>" + C.Get(loss) + " percentage points</style> <style=cStack>(+" + C.Get(lossStack) + " per stack)</style> of bonus damage. ";
            return text + "Being hit with at most <style=cIsHealth>" + C.Get(threshold) + "%</style> <style=cStack>(+" + C.Get(thresholdStack) + "% per stack)</style> bonus remaining <style=cIsHealth>breaks</style> the watches." + (RepairOnStage ? " <style=cIsUtility>Fully repaired</style> at the start of the next stage." : "");
        }
    }
}
