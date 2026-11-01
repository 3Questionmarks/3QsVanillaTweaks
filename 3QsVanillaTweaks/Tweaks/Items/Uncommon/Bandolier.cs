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
    internal static class Bandolier
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Bandolier", "Bandolier", false);
            c.N("Chance Exponent", .33f, "Drop chance is 100 * (1 - 1 / (item count + 1)^exponent). Vanilla: 0.33.");
            c.N("Cooldown Restored (%)", 100, "Fraction of each skill's recharge restored by an ammo pack. Vanilla: 100%.", 0, 100);
            c.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", .33f, () => c.F("Chance Exponent"), "Bandolier", 0, 45);
            AmmoPackSupport.Register(c);
            c.Text(() => "Kills have a chance to drop an <style=cIsUtility>ammo pack</style> that restores " + c.V("Cooldown Restored (%)", "%", "cIsUtility") + " of skill cooldowns.", () => "Chance on kill to drop an <style=cIsUtility>ammo pack</style> that restores skill cooldowns.");
        }
    }
}
