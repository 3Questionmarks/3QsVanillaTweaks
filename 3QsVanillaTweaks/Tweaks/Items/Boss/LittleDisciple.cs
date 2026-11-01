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
    internal static class LittleDisciple
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Little Disciple", "SprintWisp");
            c.Pair("Damage (%)", 300f, 300f, "Damage per wisp. Vanilla: 300% +300% per stack.");
            c.Pair("Proc Coefficient", 0f, 0f, "Outgoing proc coefficient. Vanilla: 1.");
            c.N("Search Radius", 40, "Targeting range in metres. Vanilla: 40.");
            c.N("Fire Rate At 7 Movement Speed", .6f, "Wisps per second at 7 movement speed. Scales with current speed. Vanilla: 0.6.");
            c.Field("RoR2.Items.SprintWispBodyBehavior", "Fire", "damageCoefficient", () => c.Total("Damage (%)", c.Count(N.Body)) / (100f * Mathf.Max(1, c.Count(N.Body))));
            c.Field("RoR2.Items.SprintWispBodyBehavior", "Fire", "searchRadius", () => c.F("Search Radius"));
            c.Field("RoR2.Items.SprintWispBodyBehavior", "FixedUpdate", "fireRate", () => c.F("Fire Rate At 7 Movement Speed") / 7f);
            c.Assign("RoR2.Items.SprintWispBodyBehavior", "Fire", "procCoefficient", () => c.Total("Proc Coefficient", c.Count(N.Body)));
            c.Text(() => "Fire <style=cIsDamage>tracking wisps</style> while sprinting, dealing " + c.V("Damage (%)", "%") + " damage. Fire rate increases with movement speed.", () => "Fire tracking wisps while sprinting.");
        }
    }
}
