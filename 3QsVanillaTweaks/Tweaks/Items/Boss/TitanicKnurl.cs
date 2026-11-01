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
    internal static class TitanicKnurl
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Titanic Knurl", "Knurl");
            c.Pair("Maximum Health (%)", 10f, 10f, "Additional maximum health. Vanilla: 0%.");
            c.Pair("Flat Maximum Health", 40f, 40f, "Flat maximum health. Vanilla: 40 +40 per stack.");
            c.Pair("Health Regeneration", 1.6f, 1.6f, "Base regeneration; scales with level. Vanilla: 1.6 +1.6 per stack.");
            c.NativeCount("RoR2.CharacterBody", "RecalculateStats", 40, "Flat Maximum Health");
            c.NativeCount("RoR2.CharacterBody", "RecalculateStats", 1.6f, "Health Regeneration");
            U.Ensure();
            BossItemSupport.CapacityBonuses();
            c.Text(() => "Increase <style=cIsHealing>maximum health</style> by " + c.V("Flat Maximum Health", "", "cIsHealing") + " and " + c.V("Maximum Health (%)", "%", "cIsHealing") + ". Increase <style=cIsHealing>base health regeneration</style> by " + c.V("Health Regeneration", " hp/s", "cIsHealing") + ".", () => "Increase maximum health and regeneration.");
        }
    }
}
