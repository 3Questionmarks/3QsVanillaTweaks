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
    internal static class HalcyonSeed
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Halcyon Seed", "TitanGoldDuringTP");
            c.Pair("Damage Bonus (%)", 0f, 100f, "Bonus Aurelionite damage relative to one seed. Vanilla: square-root scaling with total seeds.");
            c.Pair("Health Bonus (%)", 0f, 100f, "Bonus Aurelionite health relative to one seed. Vanilla: 100% per extra seed.");
            BossItemSupport.Titan();
            c.Text(() => "Summon <style=cIsUtility>Aurelionite</style> during the teleporter event. Additional seeds grant " + "<style=cIsDamage>" + c.F("Damage Bonus (%) Per Stack").ToString("0.##") + "%</style> damage and <style=cIsHealing>" + c.F("Health Bonus (%) Per Stack").ToString("0.##") + "%</style> health each.", () => "Summon Aurelionite during teleporter events.");
        }
    }
}
