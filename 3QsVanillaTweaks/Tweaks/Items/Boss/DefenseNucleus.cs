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
    internal static class DefenseNucleus
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Defense Nucleus", "MinorConstructOnKill");
            c.Pair("Damage Bonus (%)", 100f, 150f, "Construct bonus damage. Vanilla: 100% +100% per stack.");
            c.Pair("Health Bonus (%)", 100f, 150f, "Construct bonus health. Vanilla: 100% +100% per stack.");
            c.Pair("Maximum Constructs", 4f, 0f, "Maximum active constructs. Vanilla: 4 +4 per stack.");
            c.B("Mechanical", true, "Constructs count as mechanical allies. Vanilla: false.");
            c.B("Replace Oldest", true, "Replace the oldest construct when summoning at the limit. Vanilla: false.");
            c.Pair("Summon Chance (%)", 100, 0, "Chance to summon on an elite kill. Vanilla: 100%.");
            BossItemSupport.Summons();
            c.Text(() => "Killing an elite summons an <style=cIsUtility>Alpha Construct</style>. Maximum " + c.V("Maximum Constructs", "", "cIsUtility") + ". Constructs gain " + c.V("Damage Bonus (%)", "%") + " damage and " + c.V("Health Bonus (%)", "%", "cIsHealing") + " health.", () => "Killing elites summons powerful allied constructs.");
        }
    }
}
