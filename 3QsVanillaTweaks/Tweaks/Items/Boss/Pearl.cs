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
    internal static class Pearl
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Pearl", "Pearl", false);
            c.Pair("Maximum Health (%)", 10f, 10f, "Maximum health increase. Vanilla: 10% per stack.");
            BossItemSupport.OptionalPearl();
            c.Text(() => "Increase <style=cIsHealing>maximum health</style> by " + c.V("Maximum Health (%)", "%", "cIsHealing") + ".", () => "Increase maximum health.");
        }
    }
}
