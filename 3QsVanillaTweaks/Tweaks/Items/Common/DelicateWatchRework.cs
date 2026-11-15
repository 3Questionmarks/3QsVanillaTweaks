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
        static ConfigEntry<bool> enabled, repair, ignoreDamageOverTime, ignoreFallDamage;
        internal static bool RepairOnStage => Active && C.Get(repair);

        static ConfigEntry<float> damage, extra, loss, lossStack, threshold, thresholdStack, wearGracePeriod;
        static BuffDef wear;
        sealed class State
        {
            internal int Hits;
            internal double LastWearTime = double.NegativeInfinity;

            internal void ClearWear()
            {
                Hits = 0;
                LastWearTime = double.NegativeInfinity;
            }
        }
        internal static float BreakThreshold(int stacks) => S.Total(threshold, thresholdStack, stacks);

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
            ignoreDamageOverTime = C.BindBool(s, "Ignore Damage Over Time", true, "Prevent damage-over-time ticks such as bleed and burn, as well as void fog, from wearing down or breaking watches.");
            ignoreFallDamage = C.BindBool(s, "Ignore Fall Damage", true, "Prevent fall damage from wearing down or breaking watches.");
            C.ReadRetiredValue(s, "Ignore Void Fog", true);
            wearGracePeriod = S.Num(s, "Wear Grace Period (s)", 0, "Seconds after a hit counts toward watch wear during which further hits cannot wear down or break watches. Set to 0 to count every qualifying hit.");
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
                Text = icon => icon.buffCount + " hits have worn down your Delicate Watches. " + Description()
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
        internal static void DamageTaken(CharacterBody b, DamageInfo info)
        {
            var type = info.damageType;
            if (C.Get(ignoreFallDamage) && (type.damageType & DamageType.FallDamage) != 0)
                return;

            bool isDot = info.dotIndex != DotController.DotIndex.None || (type.damageType & DamageType.DoT) != 0;
            bool isVoidFog = type.damageSource == DamageSource.Hazard &&
                (type.damageTypeExtended & DamageTypeExtended.DamageField) != 0;
            if (C.Get(ignoreDamageOverTime) && (isDot || isVoidFog))
                return;

            int n = S.Count(b, DLC1Content.Items.FragileDamageBonus);
            if (n <= 0 || !b.master)
                return;
            var state = states.GetValue(b.master, k => new State());
            float grace = C.Get(wearGracePeriod);
            double now = Time.fixedTimeAsDouble;
            if (grace > 0f && now - state.LastWearTime < grace)
                return;

            if (Bonus(b, n) <= BreakThreshold(n))
            {
                S.Consume(b, DLC1Content.Items.FragileDamageBonus, DLC1Content.Items.FragileDamageBonusConsumed, int.MaxValue);
                state.ClearWear();
            }
            else
            {
                state.Hits = Math.Min(100000, state.Hits + 1);
                state.LastWearTime = now;
            }
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
                state.ClearWear();
            if (b.GetBuffCount(wear) != state.Hits)
                b.SetBuffCount(wear.buffIndex, state.Hits);
        }

        internal static string Description()
        {
            string text = "";
            if (C.Get(damage) != 0 || C.Get(extra) != 0)
                text = "Gain <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> <style=cIsDamage>damage</style>. ";
            if (C.Get(loss) != 0 || C.Get(lossStack) != 0)
                text += "Each qualifying hit removes <style=cIsHealth>" + C.Get(loss) + " percentage points</style> <style=cStack>(+" + C.Get(lossStack) + " per stack)</style> of bonus damage. ";
            text += "A qualifying hit with at most <style=cIsHealth>" + C.Get(threshold) + "%</style> <style=cStack>(+" + C.Get(thresholdStack) + "% per stack)</style> bonus remaining <style=cIsHealth>breaks</style> the watches.";
            bool ignoreDots = C.Get(ignoreDamageOverTime);
            bool ignoreFalls = C.Get(ignoreFallDamage);
            if (ignoreDots && ignoreFalls)
                text += " Ignores damage over time, void fog, and fall damage for watch wear.";
            else if (ignoreDots)
                text += " Ignores damage over time and void fog for watch wear.";
            else if (ignoreFalls)
                text += " Ignores fall damage for watch wear.";
            float grace = C.Get(wearGracePeriod);
            if (grace > 0f)
                text += " At most one hit counts every <style=cIsUtility>" + grace + "s</style>.";
            return text + (RepairOnStage ? " <style=cIsUtility>Fully repaired</style> at the start of the next stage." : "");
        }
    }
}
