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
    internal static class DeathMark
    {
        static UncommonConfig c;
        sealed class State
        {
            internal int Stacks;
        }

        static readonly ConditionalWeakTable<CharacterBody, State> states = new ConditionalWeakTable<CharacterBody, State>();
        internal static void Init()
        {
            c = new UncommonConfig("Death Mark", "DeathMark", true);
            c.Pair("Damage Bonus (%)", 10, 0, "Bonus damage taken per qualifying debuff group. Vanilla: 50%, independent of item count.");
            c.N("Required Debuffs", 1, "Distinct debuffs required to mark. Vanilla: 4.", 1);
            c.B("Damage Per Required Debuffs", true, "Each full group of the required number of debuffs grants another damage bonus.");
            c.Pair("Duration", 7, 7, "Mark duration in seconds. Vanilla: 7 per stack.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on hit to mark a qualifying target. Vanilla: 100%.");
            c.Suppress("RoR2.GlobalEventManager", "ProcDeathMark");
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", 1.5f, () => 1, "DeathMark", 0, 40);
            U.Ensure();
            U.Hit += Hit;
            U.BeforeDamage += Damage;
            c.Text(() => "Enemies with " + c.V("Required Debuffs", "", "cIsUtility") + " or more debuffs are <style=cIsDamage>marked for death</style> for " + c.V("Duration", "s", "cIsUtility") + ", taking " + c.V("Damage Bonus (%)", "%") + " bonus damage" + (c.B("Damage Per Required Debuffs") ? " for each full group of " + c.F("Required Debuffs") + " debuffs" : "") + ".", () => "Debuffed enemies are <style=cIsDamage>marked for death</style> and take more damage.");
        }

        internal static int Debuffs(CharacterBody b)
        {
            var indices = new HashSet<BuffIndex>();
            foreach (var index in BuffCatalog.debuffBuffIndices)
                if (index != RoR2Content.Buffs.DeathMark.buffIndex && b.HasBuff(index))
                    indices.Add(index);
            int dots = 0;
            var controller = DotController.FindDotController(b.gameObject);
            if (controller)
                for (DotController.DotIndex i = 0; i < DotController.DotIndex.Count; i++)
                    if (controller.HasDotActive(i))
                    {
                        var def = DotController.GetDotDef(i);
                        if (def.associatedBuff)
                            indices.Add(def.associatedBuff.buffIndex);
                        else
                            dots++;
                    }

            return indices.Count + dots;
        }

        static void Hit(DamageReport r)
        {
            int n = c.Count(r.attackerBody);
            var b = r.victimBody;
            if (n == 0 || !b || r.damageInfo.procCoefficient <= 0)
                return;
            int count = Debuffs(b), required = Mathf.Max(1, Mathf.CeilToInt(c.F("Required Debuffs")));
            if (count < required || !U.Roll(c.Total("Trigger Chance (%)", n), r.attackerBody))
                return;
            states.GetOrCreateValue(b).Stacks = n;
            b.ClearTimedBuffs(RoR2Content.Buffs.DeathMark);
            b.AddTimedBuff(RoR2Content.Buffs.DeathMark, c.Total("Duration", n));
        }

        static void Damage(HealthComponent target, DamageInfo info)
        {
            if (!c.Active || !target.body.HasBuff(RoR2Content.Buffs.DeathMark))
                return;
            int stacks = states.TryGetValue(target.body, out var s) ? s.Stacks : 1;
            int groups = c.B("Damage Per Required Debuffs") ? Debuffs(target.body) / Mathf.Max(1, Mathf.CeilToInt(c.F("Required Debuffs"))) : 1;
            info.damage *= 1 + c.Total("Damage Bonus (%)", stacks) * groups / 100;
        }
    }
}
