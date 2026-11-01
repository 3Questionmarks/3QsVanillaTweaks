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
    internal static class WarHorn
    {
        static UncommonConfig c;
        static BuffDef buff;
        internal static void Init()
        {
            c = new UncommonConfig("War Horn", "EnergizedOnEquipmentUse", true);
            c.Pair("Attack Speed (%)", 60, 15, "Attack speed while energized. Vanilla: 70%, no speed stacking.");
            c.Pair("Duration", 6, 3, "Seconds after equipment use. Vanilla: 8 +4 per stack.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on equipment activation. Vanilla: 100%.");
            buff = U.Buff("War Horn", false, () => RoR2Content.Buffs.Energized, () => "Increased <style=cIsDamage>attack speed</style>.");
            c.Suppress("RoR2.EquipmentSlot", "OnEquipmentExecuted");
            if (c.Enabled.Value)
                EquipmentSlot.onServerEquipmentActivated += (slot, index) => Equipment(slot);
            U.Ensure();
            U.Stats += b =>
            {
                int n = c.Count(b);
                if (n > 0 && b.HasBuff(buff))
                    U.Scale(b, "attackSpeed", c.Total("Attack Speed (%)", n));
            };
            c.Text(() => "Activating equipment grants " + c.V("Attack Speed (%)", "%") + " <style=cIsDamage>attack speed</style> for " + c.V("Duration", "s", "cIsUtility") + ".", () => "Activating equipment grants a burst of <style=cIsDamage>attack speed</style>.");
        }

        static void Equipment(EquipmentSlot __instance)
        {
            var b = __instance.characterBody;
            int n = c.Count(b);
            if (n > 0 && NetworkServer.active && U.Roll(c.Total("Trigger Chance (%)", n), b))
            {
                b.ClearTimedBuffs(buff);
                b.AddTimedBuff(buff, c.Total("Duration", n));
            }
        }
    }
}
