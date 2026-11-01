using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class RustedKey
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> common, uncommon, rare;
        static BasicPickupDropTable configured;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Rusted Key");
            RoR2Application.onLoad += () =>
            {
                var card = LegacyResourcesAPI.Load<InteractableSpawnCard>("SpawnCards/InteractableSpawnCard/iscLockbox");
                var table = card.prefab.GetComponent<ChestBehavior>().dropTable as BasicPickupDropTable;
                if (!table)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Rusted Key: native lockbox drop table was not BasicPickupDropTable.");
                    return;
                }

                common = settings.Number("Common Weight", table.tier1Weight, "Common reward weight. Default is read from the native lockbox.");
                uncommon = settings.Number("Uncommon Weight", table.tier2Weight, "Uncommon reward weight. Default is read from the native lockbox.");
                rare = settings.Number("Legendary Weight", table.tier3Weight, "Legendary reward weight. Default is read from the native lockbox.");
                if (settings.Enabled.Value)
                {
                    configured = Object.Instantiate(table);
                    S.Patch(S.Harmony("RustedKey"), typeof(ChestBehavior), "RollItem", typeof(RustedKey), prefix: nameof(BeforeRoll));
                }
            };
        }

        static void BeforeRoll(ChestBehavior __instance)
        {
            if (!settings.Active || !configured || !__instance.gameObject.name.StartsWith("Lockbox"))
                return;
            if (C.Get(common) + C.Get(uncommon) + C.Get(rare) <= 0)
                return;
            configured.tier1Weight = C.Get(common);
            configured.tier2Weight = C.Get(uncommon);
            configured.tier3Weight = C.Get(rare);
            configured.RegenerateDropTable(Run.instance);
            __instance.dropTable = configured;
        }
    }
}
