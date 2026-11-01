using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class Infusion
    {
        static UncommonConfig c;
        sealed class State
        {
            internal float Kills;
            internal uint Bonus;
        }

        static readonly ConditionalWeakTable<CharacterMaster, State> states = new ConditionalWeakTable<CharacterMaster, State>();
        internal static void Init()
        {
            c = new UncommonConfig("Infusion", "Infusion", true);
            c.N("Maximum Health Increase", -1, "Maximum bonus health per item stack. -1 means unlimited. Vanilla: 100.", -1);
            c.Pair("Health Per Award", 1, 1, "Bonus health per completed kill requirement. Vanilla: 1 per stack.");
            c.N("Maximum Health Increase Per Stack", 100, "Additional maximum bonus health per extra item stack when the cap is enabled. Vanilla: 100.");
            c.N("Enemies Per Award", 1, "Initial kills required per award. Vanilla: 1.", 1);
            c.N("Extra Enemies Per Threshold", 1, "Additional kills required for each health threshold reached. Vanilla: 0.");
            c.N("Health Threshold", 100, "Each time accumulated bonus health reaches another multiple, increase the kill requirement. Vanilla: no increase.", 1);
            c.B("Share With Inheriting Allies", true, "Share permanent bonus health and kill progress with allies that inherit the owner's Infusion. Does not share with other players.");
            c.Suppress("RoR2.GlobalEventManager", "OnCharacterDeath");
            U.Ensure();
            U.Kill += Kill;
            U.Tick += Sync;
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(
                () => c.Item ? c.Item.itemIndex : ItemIndex.None,
                (master, stacks, text) =>
                {
                    uint health = master && master.inventory
                        ? master.inventory.infusionBonus
                        : 0u;

                    return Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue(
                        "", "Health Gained", health, "", "#9CE562", "+");
                },
                () => c.Active);
            c.Text(() => "Kills permanently increase <style=cIsHealth>health</style> by " + c.V("Health Per Award", "", "cIsHealth") + (c.F("Enemies Per Award") == 1 ? " per enemy" : " per " + c.V("Enemies Per Award", "", "cIsUtility") + " kills") + (c.F("Maximum Health Increase") >= 0 ? ", up to " + c.V("Maximum Health Increase", "", "cIsHealth") + " per item stack" : "") + "." + (c.F("Extra Enemies Per Threshold") > 0 ? " Every " + c.V("Health Threshold", "", "cIsHealth") + " bonus health increases the requirement by " + c.V("Extra Enemies Per Threshold", "", "cIsUtility") + " kills." : "") + (c.B("Share With Inheriting Allies") ? " Shared with allies that inherit your Infusion." : ""), () => "Kills permanently increase <style=cIsHealth>maximum health</style>.");
        }

        static CharacterMaster Owner(CharacterMaster master)
        {
            if (!c.B("Share With Inheriting Allies"))
                return master;
            var seen = new HashSet<CharacterMaster>();
            while (master && seen.Add(master) && master.minionOwnership && master.minionOwnership.ownerMaster)
            {
                var owner = master.minionOwnership.ownerMaster;
                if (!owner.inventory || owner.inventory.GetItemCountEffective(c.Item) <= 0)
                    break;
                master = owner;
            }

            return master;
        }

        static State StateFor(CharacterMaster master)
        {
            var s = states.GetOrCreateValue(master);
            s.Bonus = Math.Max(s.Bonus, master.inventory.infusionBonus);
            return s;
        }

        static void Kill(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0 || !b.master)
                return;
            var owner = Owner(b.master);
            var s = StateFor(owner);
            float needed = UncommonCalculations.InfusionRequirement(c.F("Enemies Per Award"), s.Bonus, c.F("Health Threshold"), c.F("Extra Enemies Per Threshold"));
            s.Kills++;
            if (s.Kills < needed)
                return;
            s.Kills -= needed;
            uint add = (uint)Mathf.Max(0, Mathf.RoundToInt(c.Total("Health Per Award", n)));
            uint cap = c.F("Maximum Health Increase") < 0 ? uint.MaxValue : (uint)c.Total("Maximum Health Increase", n);
            s.Bonus = (uint)Math.Min(cap, (ulong)s.Bonus + add);
            SyncAll(owner, s.Bonus);
        }

        static void SyncAll(CharacterMaster root, uint bonus)
        {
            foreach (var m in CharacterMaster.readOnlyInstancesList)
                if (m && m.inventory && m.inventory.GetItemCountEffective(c.Item) > 0 && Owner(m) == root && m.inventory.infusionBonus < bonus)
                {
                    m.inventory.AddInfusionBonus(bonus - m.inventory.infusionBonus);
                    var b = m.GetBody();
                    if (b)
                        b.MarkAllStatsDirty();
                }
        }

        static void Sync(CharacterBody b)
        {
            if (c.Count(b) == 0 || !b.master)
                return;
            var owner = Owner(b.master);
            var s = StateFor(owner);
            if (b.inventory.infusionBonus > s.Bonus)
                s.Bonus = b.inventory.infusionBonus;
            if (b.inventory.infusionBonus < s.Bonus)
                b.inventory.AddInfusionBonus(s.Bonus - b.inventory.infusionBonus);
        }
    }
}
