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
    internal static class EmpathyCores
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Empathy Cores", "RoboBallBuddy", false);
            c.Pair("Damage Per Ally (%)", 100f, 100f, "Bonus damage per allied team member. Vanilla: 100% per item stack.");
            BossItemSupport.Cores();
            c.Text(() => "Recruit two <style=cIsUtility>Solus Probes</style> that gain " + c.V("Damage Per Ally (%)", "%") + " damage per ally on your team.", () => "Recruit two Solus Probes that grow stronger with allies.");
        }
    }
}
