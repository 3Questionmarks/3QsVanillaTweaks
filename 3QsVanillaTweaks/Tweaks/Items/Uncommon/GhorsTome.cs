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
    internal static class GhorsTome
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Ghor's Tome", "BonusGoldPackOnKill", false);
            c.Pair("Trigger Chance (%)", 4, 4, "Chance on kill to drop a gold treasure. Vanilla: 4% per stack.");
            c.N("Gold", 25, "Base gold per treasure, scaled by difficulty. Vanilla: 25.");
            c.NativeCount("RoR2.GlobalEventManager", "OnCharacterDeath", 4, "Trigger Chance (%)");
            MoneyPackSupport.Register(c);
            c.Text(() => c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance on kill to drop a treasure worth <style=cIsUtility>$" + c.F("Gold") + "</style>. Value increases over time.", () => "Chance on kill to drop a <style=cIsUtility>gold treasure</style>.");
        }
    }
}
