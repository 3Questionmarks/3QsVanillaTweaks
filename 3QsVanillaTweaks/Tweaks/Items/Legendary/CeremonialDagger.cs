using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class CeremonialDagger
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Ceremonial Dagger", "Dagger");
            c.Pair("Damage (%)", 200, 200, "Damage per dagger. Vanilla: 150% per stack.");
            c.Pair("Launch Force", 200, 0, "Dagger launch force. Vanilla: 200.");
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 200, () => c.Total("Launch Force", c.Count(N.Source)), "Dagger", 0, 100);
            c.Pair("Daggers", 3, 0, "Daggers launched on kill. Vanilla: 3.");
            c.Pair("Proc Coefficient", 0, 0, "Dagger proc coefficient. Vanilla: 1.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance to launch daggers on a kill. Vanilla: 100%.");
            c.NativeCount("RoR2.GlobalEventManager", "OnCharacterDeath", 1.5f, "Damage (%)", 100);
            RareItemSupport.KillBudget(c, "RoR2.GlobalEventManager", "OnCharacterDeath", "Dagger", 3, "Daggers");
            RoR2Application.onLoad += () => Tweaks.Items.Uncommon.UncommonProjectiles.RegisterLoaded(c, UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Dagger/DaggerProjectile.prefab").WaitForCompletion());
            c.Text(() => "Killing an enemy releases " + c.V("Daggers", "", "cIsUtility") + " homing daggers that deal " + c.V("Damage (%)", "%") + " damage each.", () => "Kills release homing daggers.");
        }
    }
}
