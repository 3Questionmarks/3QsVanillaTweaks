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
    internal static class WaxQuail
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Wax Quail", "JumpBoost", false);
            c.Pair("Jump Distance", 10, 10, "Extra sprint-jump distance in metres. Vanilla: 10 per stack.");
            c.NativeCount("EntityStates.GenericCharacterMain", "ProcessJump", 10, "Jump Distance");
            c.Text(() => "Jumping while sprinting boosts you forward by " + c.V("Jump Distance", "m", "cIsUtility") + ".", () => "Jumping while sprinting <style=cIsUtility>boosts you forward</style>.");
        }
    }
}
