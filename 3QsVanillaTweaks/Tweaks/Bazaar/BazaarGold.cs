using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using A = ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Bazaar
{
    internal static class BazaarGold
    {
        static ConfigEntry<bool> enabled;
        internal static bool BlocksGold
        {
            get
            {
                if (!NetworkServer.active || enabled == null || !C.Get(enabled)) return false;
                var scene = SceneCatalog.mostRecentSceneDef;
                return scene && string.Equals(scene.baseSceneName, "bazaar", System.StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static void Init()
        {
            enabled = C.BindBool("Bazaar - Gold Generation", "Prevent Gold Generation", true,
                "Prevents you from earning gold in the Bazaar. Vanilla: false.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.Gold");
            var denyReward = new HarmonyMethod(typeof(BazaarGold), nameof(BeforeReward)) { priority = Priority.First };
            foreach (var method in typeof(CharacterMaster).GetMethods(BindingFlags.Instance | BindingFlags.Public))
                if (method.Name == "GiveMoney") harmony.Patch(method, prefix: denyReward);
            var conversion = A.Method(typeof(CharacterMaster), "GiveMoneyWithOnLevelUpFreeUnlock");
            if (conversion != null) harmony.Patch(conversion, prefix: denyReward);
            harmony.Patch(A.Property(typeof(CharacterMaster), "money").GetSetMethod(true),
                prefix: new HarmonyMethod(typeof(BazaarGold), nameof(BeforeBalance)) { priority = Priority.First });
        }
        static bool BeforeReward() => !BlocksGold;
        internal static bool AllowsBalanceChange(bool blocked, uint current, uint requested) => !blocked || requested <= current;
        static bool BeforeBalance(CharacterMaster __instance, uint value) => AllowsBalanceChange(BlocksGold, __instance.money, value);
    }
}
