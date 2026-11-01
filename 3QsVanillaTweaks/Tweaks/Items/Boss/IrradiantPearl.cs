using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class IrradiantPearl
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Irradiant Pearl", "ShinyPearl");
            c.Pair("Stat Increase (%)", 10f, 10f, "Health, damage, speed, attack speed, critical chance, armour and regeneration increase. Vanilla: 10% per stack except flat regeneration and critical chance.");
            c.B("Multiply Actual Armour", true, "Multiply total armour, including buffs and items. Vanilla: only the early base armour calculation.");
            c.B("Percentage Regeneration", true, "Increase actual regeneration by the configured percentage instead of adding flat regeneration. Vanilla: false.");
            c.B("Multiply Negative Regeneration", false, "Allow the regeneration multiplier to increase negative regeneration.");
            BossItemSupport.Pearl();
            c.Text(() => "Increase <style=cIsUtility>all stats</style> by " + c.V("Stat Increase (%)", "%", "cIsUtility") + ".", () => "Increase all stats.");
        }
    }
}
