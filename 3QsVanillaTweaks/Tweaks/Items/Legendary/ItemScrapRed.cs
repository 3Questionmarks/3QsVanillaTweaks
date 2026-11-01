using System;
using HarmonyLib;
using RoR2;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Legendary
{
    internal static class ItemScrapRed
    {
        internal static void Init() => LegendaryConfigSupport.Register("ScrapRed", c => { });
        // Scrap has no numerical gameplay effect; its standard item switches and vanilla text are retained.
    }
}
