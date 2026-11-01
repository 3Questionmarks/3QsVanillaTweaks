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
    internal static class Shatterspleen
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Shatterspleen", "BleedOnHitAndExplode");
            c.Pair("Critical Chance (%)", 5f, 0f, "Critical strike chance. Vanilla: 5%.");
            c.Pair("Explosion Damage (%)", 400f, 400f, "Base damage on killing a bleeding enemy. Vanilla: 400% +400% per stack.");
            c.Pair("Victim Health Damage (%)", 15f, 15f, "Explosion damage based on victim maximum health. Vanilla: 15% +15% per stack.");
            c.Pair("Explosion Radius", 16f, 0f, "Explosion radius in metres. Vanilla: 16.");
            c.Pair("Proc Coefficient", 0f, 0f, "Explosion proc coefficient. Vanilla: 1.");
            c.Pair("Bleed Damage Per Second (%)", 80, 0, "Bleed damage per second. Vanilla: 80%.");
            c.Pair("Bleed Duration", 3, 0, "Bleed duration before incoming proc scaling. Vanilla: 3 seconds.");
            c.Pair("Bleed Chance On Critical Hit (%)", 100, 0, "Chance for a critical hit to apply bleed. Vanilla: 100%.");
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 4, () => c.Total("Explosion Damage (%)", c.Count(N.Body)) / (100 * Mathf.Max(1, c.Count(N.Body))), "BleedOnHitAndExplode", 0, 120);
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", .15f, () => c.Total("Victim Health Damage (%)", c.Count(N.Body)) / (100 * Mathf.Max(1, c.Count(N.Body))), "BleedOnHitAndExplode", 0, 120);
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 16, () => c.Total("Explosion Radius", c.Count(N.Body)), "BleedOnHitAndExplode", 0, 180);
            BossItemSupport.Spleen();
            c.Text(() => "Gain " + c.V("Critical Chance (%)", "%") + " critical strike chance. Critical strikes <style=cIsDamage>bleed</style> enemies for " + c.V("Bleed Damage Per Second (%)", "%") + " damage per second for " + c.V("Bleed Duration", "s", "cIsUtility") + ". Bleeding enemies explode on death for " + c.V("Explosion Damage (%)", "%") + " damage plus " + c.V("Victim Health Damage (%)", "%") + " of their maximum health.", () => "Critical strikes bleed enemies. Bleeding enemies explode on death.");
        }
    }
}
