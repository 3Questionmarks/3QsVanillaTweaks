using System;
using System.Runtime.CompilerServices;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Lunar
{
    internal static class LongstandingSolitude
    {
        static R c;
        sealed class State { internal double Remainder, PendingGold; internal int Stage = -1, Cost; }
        static readonly ConditionalWeakTable<CharacterMaster, State> states = new ConditionalWeakTable<CharacterMaster, State>();
        internal static void Init()
        {
            U.Ensure(); c = new R("Longstanding Solitude", "OnLevelUpFreeUnlock", true, "Lunar");
            c.N("Maximum Free Purchases Per Level", -1, "Maximum free purchases granted per level, with one purchase per item stack. -1 removes the stacking cap; it does not grant unlimited purchases. Vanilla: capped at 3.", -1);
            c.N("Experience Per Chest Cost (%)", 12, "Gold equal to one difficulty-scaled small chest grants this percentage of the XP needed for the next level. This replaces normal gold income, not enemy XP. Vanilla: 12%.");
            c.N("Base Chest Gold Cost", 25, "Base gold cost used to scale the gold-to-XP conversion. Vanilla: 25.", 1);
            c.N("Extra Experience Per Stack (%)", 0, "Percentage bonus to gold-converted XP per additional item. Vanilla: 1.25 percentage points added to the base conversion rate.");
            c.N("Maximum Extra Experience (%)", 0, "Cap on the stacking XP bonus. 0 is unlimited. Vanilla caps the conversion calculation at 8 items.");
            c.N("Gold Cost Increase Per Stack (%)", 25, "Increase the gold needed for the same converted XP per extra item. Only affects this holder's conversion, not the team's XP curve. Vanilla: 0%.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(CharacterMaster), "GiveMoneyWithOnLevelUpFreeUnlock", typeof(LongstandingSolitude), prefix: nameof(Convert));
                U.Patch(typeof(CharacterBody), "AddFreeChestBuff", typeof(LongstandingSolitude), prefix: nameof(Grant));
            }
            c.ShowStat = key => false;
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => c.Item ? c.Item.itemIndex : ItemIndex.None,
                (master, n, text) => ConversionStats(n), () => c.Active);
            c.Text(Description, () => "Gain free purchases on level up... <style=cIsHealth>but convert all gold into experience.</style>");
        }
        static string ConversionStats(int count)
        {
            float extra = c.F("Extra Experience Per Stack (%)") * Math.Max(0, count - 1);
            float cap = c.F("Maximum Extra Experience (%)");
            if (cap > 0) extra = Mathf.Min(extra, cap);
            float cost = (1 + c.F("Gold Cost Increase Per Stack (%)") * Math.Max(0, count - 1) / 100) / (1 + extra / 100);
            return Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue("", "Gold Required For Experience", (cost - 1) * 100, "%", "#E5C962", cost >= 1 ? "+" : "");
        }
        static bool Convert(CharacterMaster __instance, uint amount)
        {
            // Guard this replacement too: another prefix may run before the native conversion method is skipped.
            if (Tweaks.Bazaar.BazaarGold.BlocksGold) return false;
            int n = c.Active && __instance.inventory ? __instance.inventory.GetItemCountEffective(c.Item) : 0;
            if (!NetworkServer.active || n == 0) return true;
            var body = __instance.GetBody(); var state = states.GetOrCreateValue(__instance);
            if (!body || !Run.instance || !TeamManager.instance) { state.PendingGold += amount; return false; }
            if (state.Stage != Run.instance.stageClearCount) { state.Stage = Run.instance.stageClearCount; state.Cost = Mathf.Max(1, Run.instance.GetDifficultyScaledCost(Mathf.RoundToInt(c.F("Base Chest Gold Cost")))); }
            uint level = (uint)Mathf.Max(1, Mathf.FloorToInt(body.level));
            ulong levelXP = TeamManager.GetExperienceForLevel(level + 1) - TeamManager.GetExperienceForLevel(level);
            float extra = c.F("Extra Experience Per Stack (%)") * (n - 1), cap = c.F("Maximum Extra Experience (%)"); if (cap > 0) extra = Mathf.Min(extra, cap);
            double rate = levelXP * (c.F("Experience Per Chest Cost (%)") / 100d) * (1 + extra / 100d) / (state.Cost * (1 + c.F("Gold Cost Increase Per Stack (%)") * (n - 1) / 100d));
            // Carry fractional XP between gold payments, instead of discarding it.
            double xp = (amount + state.PendingGold) * rate + state.Remainder; state.PendingGold = 0;
            ulong whole = (ulong)Math.Min(Math.Floor(Math.Max(0, xp)), (double)ulong.MaxValue);
            state.Remainder = xp - whole;
            if (whole > 0) TeamManager.instance.GiveTeamExperience(__instance.teamIndex, whole);
            return false;
        }
        internal static int PurchasesGranted(int stacks, int cap) => cap < 0 ? Math.Max(0, stacks) : Math.Min(Math.Max(0, stacks), cap);
        static string Description()
        {
            return "On level up gain <style=cIsUtility>a free unlock</style> for the next purchase <style=cStack>(+1 per stack)</style>, but <style=cIsHealth>all gold is converted</style> to <style=cIsUtility>experience</style>." + (c.F("Gold Cost Increase Per Stack (%)") == 0 ? "" : " <style=cIsHealth>Increases the gold required</style> to gain experience by <style=cIsHealth>0%</style> <style=cStack>(+" + c.F("Gold Cost Increase Per Stack (%)").ToString("0.##") + "% per stack)</style>.");
        }
        static bool Grant(CharacterBody __instance)
        {
            int stacks = c.Count(__instance);
            if (!NetworkServer.active || stacks == 0) return true;
            int count = PurchasesGranted(stacks, Mathf.RoundToInt(c.F("Maximum Free Purchases Per Level")));
            if (count > 0)
            {
                __instance.SetBuffCount(DLC2Content.Buffs.FreeUnlocks.buffIndex, __instance.GetBuffCount(DLC2Content.Buffs.FreeUnlocks) + count);
                if (__instance.master) __instance.master.trackedFreeUnlocks += (uint)count;
            }
            return false;
        }
    }
}
