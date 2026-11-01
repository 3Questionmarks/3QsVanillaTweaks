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
    internal static class RedWhip
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Red Whip", "SprintOutOfCombat", true);
            c.Pair("Movement Speed (%)", 10, 0, "Unconditional movement speed. Vanilla: 0%.");
            c.Pair("Out Of Combat Speed (%)", 30, 30, "Additional movement speed out of combat. Vanilla: 30% per stack.");
            c.N("Out Of Combat Delay", 5, "Seconds without using a combat skill. Vanilla: 5.");
            c.NativeCount("RoR2.CharacterBody", "RecalculateStats", .3f, "Out Of Combat Speed (%)", 100);
            c.Literal("RoR2.CharacterBody", "UpdateOutOfCombatAndDanger", 5, () => c.Count(UncommonNative.Body) > 0 ? c.F("Out Of Combat Delay") : 5);
            U.Ensure();
            U.Stats += b => U.Scale(b, "moveSpeed", c.Total("Movement Speed (%)", c.Count(b)));
            c.Text(() => "Increase <style=cIsUtility>movement speed</style> by " + c.V("Movement Speed (%)", "%", "cIsUtility") + ". After " + c.V("Out Of Combat Delay", "s", "cIsUtility") + " out of combat, gain an additional " + c.V("Out Of Combat Speed (%)", "%", "cIsUtility") + " movement speed.", () => "Move faster, with <style=cIsUtility>additional speed out of combat</style>.");
        }
    }
}
