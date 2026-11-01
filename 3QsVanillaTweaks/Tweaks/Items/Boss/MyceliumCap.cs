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
    internal static class MyceliumCap
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Mycelium Cap", "MushShot");
            c.Pair("Landing Damage (%)", 1000f, 200f, "Landing projectile damage. Vanilla: 1000% +200% per stack.");
            c.Pair("Spore Projectiles", 3, 0, "Projectiles launched when landing. Vanilla: 3.");
            c.Pair("Cloud Proc Coefficient", 0, 0, "Outgoing cloud proc coefficient. Vanilla value is read from the loaded projectile.");
            c.Pair("Spore Proc Coefficient", 0, 0, "Outgoing spore projectile proc coefficient. Vanilla value is read from the loaded projectile.");
            BossItemSupport.Mycelium();
            c.Text(() => "Become immune to <style=cIsUtility>fall damage</style>. Landing from a damaging fall launches " + c.V("Spore Projectiles", "", "cIsUtility") + " spores for " + c.V("Landing Damage (%)", "%") + " damage, leaving damaging clouds.", () => "Negate fall damage and launch spores when landing from a height.");
        }
    }
}
