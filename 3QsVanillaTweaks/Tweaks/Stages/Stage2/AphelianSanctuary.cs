using BepInEx.Configuration;
using RoR2;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages.Stage2
{
    internal static class AphelianSanctuary
    {
        private static ConfigEntry<int> lunarBudCost;
        internal static void Init()
        {
            var pool = StageFeatureSpawnHelper.Bind("Stages - Stage 2 - Aphelian Sanctuary - Cleansing Pool",
                "ancientloft", "Aphelian Sanctuary Cleansing Pool", -65.98f, 60f, -217.34f);
            StageFeatureSpawnHelper.Register(pool, (settings, rng) =>
                StageFeatureSpawnHelper.Spawn(settings, StageFeatureSpawnHelper.LoadPrefab("RoR2/Base/ShrineCleanse/ShrineCleanse.prefab")));
            const string section = "Stages - Stage 2 - Aphelian Sanctuary - Lunar Bud";
            var bud = StageFeatureSpawnHelper.Bind(section, "ancientloft", "Aphelian Sanctuary Lunar Bud", -69.58f, 80.17f, 221.61f);
            lunarBudCost = ModConfig.Bind(section, "Lunar Coin Cost", 0,
                new ConfigDescription("Lunar coin cost for this fixed Lunar Bud; 0 makes it free.", new AcceptableValueRange<int>(0, 1000)));
            StageFeatureSpawnHelper.Register(bud, (settings, rng) =>
                StageFeatureSpawnHelper.Spawn(settings, StageFeatureSpawnHelper.LoadPrefab("RoR2/Base/LunarChest/LunarChest.prefab"), instance =>
                {
                    var purchase = instance.GetComponent<PurchaseInteraction>();
                    if (!purchase) throw new System.InvalidOperationException("Lunar Bud has no purchase component.");
                    purchase.costType = CostTypeIndex.LunarCoin;
                    purchase.Networkcost = ModConfig.Get(lunarBudCost);
                }));
        }
    }
}
