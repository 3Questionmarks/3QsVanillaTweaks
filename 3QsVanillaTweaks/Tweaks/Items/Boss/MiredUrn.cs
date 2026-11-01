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
    internal static class MiredUrn
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Mired Urn", "SiphonOnLowHealth");
            c.Pair("Maximum Targets", 3f, 1f, "Enemies siphoned at once. Vanilla: 1 +1 per stack.");
            c.Pair("Damage Per Tick (%)", 25f, 10f, "Damage per tick per target. Vanilla: 25%, no item damage scaling.");
            c.Pair("Damage Interval", 0.25f, 0f, "Seconds between damage ticks. Vanilla: 0.25.");
            c.Pair("Radius", 13f, 0f, "Siphon range in metres. Vanilla: 13.");
            c.Pair("Proc Coefficient", 0f, 0f, "Outgoing proc coefficient. Vanilla: 0.");
            c.Pair("Healing From Damage (%)", 100f, 0f, "Healing as a percentage of actual damage dealt. Vanilla: 100%.");
            BossItemSupport.Urn();
            c.Text(() => "While in combat, siphon up to " + c.V("Maximum Targets", "", "cIsUtility") + " nearby enemies within " + c.V("Radius", "m", "cIsUtility") + ", dealing " + c.V("Damage Per Tick (%)", "%") + " damage every " + c.V("Damage Interval", "s", "cIsUtility") + ". Heal for " + c.V("Healing From Damage (%)", "%", "cIsHealing") + " of damage dealt.", () => "Siphon health from nearby enemies while in combat.");
        }
    }
}
