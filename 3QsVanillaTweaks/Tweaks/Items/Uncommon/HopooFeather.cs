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
    internal static class HopooFeather
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Hopoo Feather", "Feather", false);
            c.Pair("Extra Jumps", 1, 1, "Additional airborne jumps. Vanilla: 1 per stack.");
            c.N("Air Jump Horizontal Multiplier", 1.5f, "Horizontal speed multiplier for additional jumps. Vanilla: 1.5.");
            c.N("Air Jump Vertical Multiplier", 1.5f, "Vertical jump multiplier for additional jumps. Vanilla: 1.5.");
            c.Suppress("RoR2.CharacterBody", "RecalculateStats");
            U.Ensure();
            U.Stats += b =>
            {
                int n = c.Count(b);
                if (n > 0)
                    A.Property(typeof(CharacterBody), "maxJumpCount").SetValue(b, b.maxJumpCount + Mathf.RoundToInt(c.Total("Extra Jumps", n)), null);
            };
            c.Literal("EntityStates.GenericCharacterMain", "ProcessJump", 1.5f, () => c.F("Air Jump Horizontal Multiplier"), "JumpBoost", 0, 90);
            c.Literal("EntityStates.GenericCharacterMain", "ProcessJump", 1.5f, () => c.F("Air Jump Vertical Multiplier"), "JumpBoost", 1, 90);
            c.Text(() => "Gain " + c.V("Extra Jumps", "", "cIsUtility") + " additional jumps in the air.", () => "Gain an additional <style=cIsUtility>jump</style>.");
        }
    }
}
