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
    internal static class QueensGland
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Queen's Gland", "BeetleGland");
            c.Pair("Maximum Guards", 1f, 0f, "Maximum active guards. Vanilla: 1 +1 per stack.");
            c.Pair("Damage Bonus (%)", 300f, 300f, "Guard bonus damage. Vanilla: 300%, no stacking.");
            c.Pair("Health Bonus (%)", 100f, 100f, "Guard bonus health. Vanilla: 100%, no stacking.");
            c.N("Respawn Delay", 30, "Seconds before a replacement guard. Vanilla: 30.", 0.1f);
            BossItemSupport.Summons();
            c.Text(() => "Recruit a <style=cIsUtility>Beetle Guard</style> with " + c.V("Damage Bonus (%)", "%") + " bonus damage and " + c.V("Health Bonus (%)", "%", "cIsHealing") + " bonus health. Maximum " + c.V("Maximum Guards", "", "cIsUtility") + ".", () => "Recruit a Beetle Guard to fight alongside you.");
        }
    }
}
