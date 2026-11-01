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
    internal static class IgnitionTank
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Ignition Tank", "StrengthenBurn", true);
            c.Pair("Ignite Chance (%)", 10, 0, "Chance on hit to apply a burn. Vanilla: 0%.");
            c.Pair("Ignite Damage (%)", 50, 0, "Total burn damage as a percentage of the triggering hit. Vanilla: does not apply its own burn.");
            c.Pair("Ignite Duration", 4, 0, "Duration of the on-hit burn in seconds.");
            c.Pair("Stronger Burn Bonus (%)", 300, 300, "Extra burn damage when broad fire-damage mode is off. Vanilla: 300% per stack.");
            c.B("Increase All Fire Damage", true, "Replace the stronger-burn damage bonus with a bonus to fire damage, retaining stronger-burn upgrades and visuals.");
            c.Pair("Fire Damage Bonus (%)", 100, 100, "Extra fire damage in broad fire-damage mode. Vanilla: no broad bonus.");
            if (c.Enabled.Value)
                U.Patch(typeof(StrengthenBurnUtils), "CheckDotForUpgrade", typeof(IgnitionTank), prefix: nameof(BeforeUpgrade), postfix: nameof(AfterUpgrade));
            U.Ensure();
            U.Hit += Hit;
            U.BeforeDamage += Damage;
            c.ShowStat = key => c.B("Increase All Fire Damage") ? !key.StartsWith("Stronger Burn Bonus") : !key.StartsWith("Fire Damage Bonus");
            c.Text(() => "Hits have a " + c.V("Ignite Chance (%)", "%") + " chance to <style=cIsDamage>ignite</style> for " + c.V("Ignite Damage (%)", "%") + " total damage. " + (c.B("Increase All Fire Damage") ? "Deal " + c.V("Fire Damage Bonus (%)", "%") + " more <style=cIsDamage>fire damage</style>." : "Burns deal " + c.V("Stronger Burn Bonus (%)", "%") + " more damage."), () => c.B("Increase All Fire Damage") ? "Deal more <style=cIsDamage>fire damage</style>. Hits can ignite enemies." : "Strengthen <style=cIsDamage>burns</style>. Hits can ignite enemies.");
            FireDamageSupport.Enable(() => c.Active);
        }

        static void BeforeUpgrade(Inventory inventory, ref InflictDotInfo dotInfo, out InflictDotInfo __state)
        {
            __state = dotInfo;
        }

        static void AfterUpgrade(Inventory inventory, ref InflictDotInfo dotInfo, InflictDotInfo __state)
        {
            if (!c.Active || !inventory)
                return;
            int n = inventory.GetItemCountEffective(c.Item);
            if (n == 0 || (__state.dotIndex != DotController.DotIndex.Burn && __state.dotIndex != DotController.DotIndex.Helfire))
                return;
            var victim = __state.victimObject ? __state.victimObject.GetComponent<CharacterBody>() : null;
            bool oil = victim && victim.HasBuff(DLC2Content.Buffs.Oiled);
            float factor = 1 + (oil ? 3 : 0) + (c.B("Increase All Fire Damage") ? 0 : c.Total("Stronger Burn Bonus (%)", n) / 100);
            dotInfo.damageMultiplier = __state.damageMultiplier * factor;
            dotInfo.totalDamage = __state.totalDamage.HasValue ? __state.totalDamage.Value * factor : (float? )null;
        }

        static void Damage(HealthComponent target, DamageInfo info)
        {
            var b = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(b);
            if (n > 0 && c.B("Increase All Fire Damage") && FireDamageSupport.IsFire(info))
                info.damage *= 1 + c.Total("Fire Damage Bonus (%)", n) / 100;
        }

        static void Hit(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0 || !r.victimBody || r.damageInfo.procCoefficient <= 0 || !U.Roll(c.Total("Ignite Chance (%)", n) * r.damageInfo.procCoefficient, b))
                return;
            var info = new InflictDotInfo
            {
                attackerObject = b.gameObject,
                victimObject = r.victimBody.gameObject,
                dotIndex = DotController.DotIndex.Burn,
                duration = c.Total("Ignite Duration", n),
                damageMultiplier = 1,
                totalDamage = r.damageDealt * c.Total("Ignite Damage (%)", n) / 100
            };
            // Keep the triggering proc chain when this runtime exposes it.
            var chainField = A.Field(typeof(InflictDotInfo), "procChainMask");
            if (chainField != null && chainField.FieldType == typeof(ProcChainMask))
            {
                object boxed = info;
                chainField.SetValue(boxed, r.damageInfo.procChainMask);
                info = (InflictDotInfo)boxed;
            }
            StrengthenBurnUtils.CheckDotForUpgrade(b.inventory, ref info);
            DotController.InflictDot(ref info);
        }
    }
}
