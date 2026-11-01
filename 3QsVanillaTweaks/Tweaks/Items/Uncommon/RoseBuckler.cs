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
    internal static class RoseBuckler
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Rose Buckler", "SprintArmor", false);
            c.Pair("Armor", 30, 30, "Armor while sprinting. Vanilla: 30 per stack.");
            c.Suppress("RoR2.CharacterBody", "RecalculateStats");
            U.Ensure();
            U.Stats += b =>
            {
                if (b.isSprinting)
                    U.Add(b, "armor", c.Total("Armor", c.Count(b)));
            };
            c.Text(() => "Gain " + c.V("Armor", "", "cIsHealing") + " <style=cIsHealing>armor</style> while sprinting.", () => "Gain <style=cIsHealing>armor</style> while sprinting.");
        }
    }
}
