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
    internal static class ShippingRequestForm
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Shipping Request Form", "FreeChest", false);
            c.Pair("Deliveries", 1, 0, "Delivery chests per item holder each stage. Vanilla: 1 per holder.");
            c.N("Common Weight", 79, "Common-item reward weight. Vanilla: 79.");
            c.N("Uncommon Weight", 20, "Uncommon weight multiplied by team item count. Vanilla: 20.");
            c.N("Legendary Weight", 1, "Legendary weight multiplied by the square of team item count. Vanilla: 1.");
            c.Suppress("RoR2.SceneDirector", "PopulateScene");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(SceneDirector), "PopulateScene", typeof(ShippingRequestForm), postfix: nameof(Spawn));
                U.Patch(typeof(FreeChestDropTable), "RebuildSelection", typeof(ShippingRequestForm), prefix: nameof(Weights));
            }

            c.Text(() => "Receive " + c.V("Deliveries", "", "cIsUtility") + " <style=cIsUtility>delivery chests</style> each stage. More item stacks improve the contents.", () => "Receive a <style=cIsUtility>delivery</style> each stage.");
        }

        static void Spawn()
        {
            if (!c.Active || !NetworkServer.active || !Run.instance)
                return;
            int count = 0;
            foreach (var m in CharacterMaster.readOnlyInstancesList)
                if (m && m.inventory)
                {
                    int n = m.inventory.GetItemCountEffective(c.Item);
                    if (n > 0)
                        count += Mathf.Max(0, Mathf.FloorToInt(c.Total("Deliveries", n)));
                }

            var card = LegacyResourcesAPI.Load<SpawnCard>("SpawnCards/InteractableSpawnCard/iscFreeChest");
            for (int i = 0; i < count; i++)
                DirectorCore.instance.TrySpawnObject(new DirectorSpawnRequest(card, new DirectorPlacementRule { placementMode = DirectorPlacementRule.PlacementMode.Random }, Run.instance.stageRng));
        }

        static void Weights(FreeChestDropTable __instance)
        {
            if (!c.Active)
                return;
            A.Field(typeof(FreeChestDropTable), "tier1Weight").SetValue(__instance, c.F("Common Weight"));
            A.Field(typeof(FreeChestDropTable), "tier2Weight").SetValue(__instance, c.F("Uncommon Weight"));
            A.Field(typeof(FreeChestDropTable), "tier3Weight").SetValue(__instance, c.F("Legendary Weight"));
        }
    }
}
