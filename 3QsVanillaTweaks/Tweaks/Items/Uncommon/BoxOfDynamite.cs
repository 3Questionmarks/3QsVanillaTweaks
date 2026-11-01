using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class BoxOfDynamite
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Box of Dynamite", "DronesDropDynamite", true);
            c.Pair("Damage (%)", 240, 120, "Dynamite base damage. Vanilla: 240 +85 per stack.");
            c.N("Cooldown", 10, "Seconds between dynamite drops. Vanilla: 10.");
            c.N("Search Interval", .25f, "Seconds between enemy searches. Vanilla: 0.25.", .02f);
            c.N("Search Radius", 2.5f, "Enemy-search capsule radius. Vanilla: 2.5 metres.");
            c.Pair("Proc Coefficient", 1, 0, "Dynamite proc coefficient. Vanilla: 1.");
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "FixedUpdate", 2.4f, () => c.F("Damage (%)") / 100);
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "FixedUpdate", .85f, () => c.F("Damage (%) Per Stack") / 100);
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "FixedUpdate", 10, () => c.F("Cooldown"));
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "FixedUpdate", .25f, () => c.F("Search Interval"));
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "FixedUpdate", .25f, () => c.F("Search Interval"), null, 1);
            c.Literal("RoR2.Items.DroneDynamiteBehaviour", "IsEnemyNearby", 2.5f, () => c.F("Search Radius"));
            RoR2Application.onLoad += () => UncommonProjectiles.RegisterLoaded(c, CharacterBody.CommonAssets.droneDynamiteProjectile, "DroneDynamiteDisplay");
            c.Text(() => "Gain <style=cIsUtility>Lt. Droneboy</style>. In combat, your drones drop dynamite for " + c.V("Damage (%)", "%") + " damage, <style=cIsDamage>stunning</style> enemies. Recharges after " + c.V("Cooldown", "s", "cIsUtility") + ".", () => "Gain Lt. Droneboy. Your drones periodically drop <style=cIsDamage>dynamite</style>.");
        }
    }
}
