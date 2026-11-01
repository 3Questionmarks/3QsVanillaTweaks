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
    internal static class SaleStarConsumed
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Sale Star (Consumed)", "LowerPricedChestsConsumed", true);
            c.B("Regenerate At Start Of Stage", true, "Restore the usable item at the next stage. Vanilla: true.");
            c.B("Can Scrap", true, "Allow this spent item to be converted into uncommon scrap.");
            if (c.Enabled.Value)
            {
                S.Ensure();
                S.ScrapAllowed["LowerPricedChestsConsumed"] = () => c.Active && c.B("Can Scrap");
                S.ScrapTiers["LowerPricedChestsConsumed"] = ItemTier.Tier2;
                U.Patch(typeof(CharacterMaster), "TrySaleStar", typeof(SaleStarConsumed), prefix: nameof(Regenerate));
            }

            c.Text(() => c.B("Regenerate At Start Of Stage") ? "<style=cIsUtility>Regenerates</style> at the start of the next stage." : "A spent item.", () => c.B("Regenerate At Start Of Stage") ? "Regenerates next stage." : "A spent item.");
        }

        static bool Regenerate() => !c.Active || c.B("Regenerate At Start Of Stage");
    }
}
