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
    internal static class GreenScrap
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Item Scrap, Green", "ScrapGreen", false);
            c.B("Prioritize At Printers", true, "Use green scrap before ordinary green items at printers. Vanilla: true.");
            RoR2Application.onLoad += () =>
            {
                if (!c.Enabled.Value || !c.Item)
                    return;
                var tags = new List<ItemTag>(c.Item.tags);
                if (c.B("Prioritize At Printers"))
                {
                    if (!tags.Contains(ItemTag.Scrap))
                        tags.Add(ItemTag.Scrap);
                }
                else
                    tags.Remove(ItemTag.Scrap);
                c.Item.tags = tags.ToArray();
            };
            c.Text(() => "Does nothing. " + (c.B("Prioritize At Printers") ? "<style=cIsUtility>Prioritized</style> when using uncommon 3D Printers." : "Can be exchanged at uncommon 3D Printers."), () => "Can be exchanged at <style=cIsUtility>uncommon 3D Printers</style>.");
        }
    }
}
