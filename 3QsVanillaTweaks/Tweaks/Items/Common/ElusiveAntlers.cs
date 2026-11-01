using System;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Common
{
    internal static class ElusiveAntlers
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> speed, duration, spawn, initial, reduction, scaling, barrier, barrierStack, age, distance;
        static ConfigEntry<int> cap, capStack;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Elusive Antlers");
            speed = settings.Number("Movement Speed Per Buff (%)", 12, "Movement-speed bonus per shard buff. Native default: 12%.");
            duration = settings.Number("Buff Duration", 12, "Shard buff duration in seconds. Native default: 12.");
            spawn = settings.Number("Spawn Interval", 10, "First-stack shard spawn interval in seconds. Native default: 10.", .02f);
            initial = settings.Number("First Spawn Delay", 3, "Delay before the first shard. Native default: 3.");
            reduction = settings.Number("Maximum Interval Reduction", 8, "Maximum reduction to spawn interval from stacking. Native default: 8.");
            scaling = settings.Number("Interval Scaling Constant", 3, "Hyperbolic interval constant: reduction times (1 - constant / (constant + stacks - 1)). Native default: 3.", .001f);
            cap = settings.Integer("Maximum Buffs", 3, "Maximum shard buffs at one item stack. Native default: 3.");
            capStack = settings.Integer("Maximum Buffs Per Stack", 3, "Additional maximum buffs per extra item. Native default: 3.");
            barrier = settings.Number("Flat Barrier Per Shard", 0, "Flat barrier from a shard. Native default: 0.");
            barrierStack = settings.Number("Flat Barrier Per Shard Per Stack", 0, "Additional flat barrier per extra item. Native default: 0.");
            age = settings.Number("Minimum Despawn Age", 60, "Seconds before out-of-range shards may despawn. Native default: 60.");
            distance = settings.Number("Maximum Shard Distance", 120, "Distance in metres beyond which old shards despawn. Native default: 120.");
            settings.Literal("RoR2.CharacterBody", "RecalculateStats", .12f, () => C.Get(speed) / 100);
            settings.Literal("RoR2.CharacterBody", "SpawnShard", 0, () => C.Get(barrier));
            settings.Literal("RoR2.CharacterBody", "SpawnShard", 0, () => C.Get(barrierStack), occurrence: 1);
            settings.Literal("RoR2.CharacterBody", "SpawnShard", 12, () => C.Get(duration));
            settings.Literal("RoR2.CharacterBody", "SpawnShard", 60, () => C.Get(age));
            settings.Literal("RoR2.CharacterBody", "SpawnShard", 120, () => C.Get(distance));
            if (!settings.Enabled.Value)
                return;
            var h = S.Harmony("ElusiveAntlers");
            S.Patch(h, typeof(CharacterBody), "GetElusiveAntlersCurrentMaxStack", typeof(ElusiveAntlers), postfix: nameof(Cap));
            S.Patch(h, typeof(ElusiveAntlersBehavior), "OnEnable", typeof(ElusiveAntlers), postfix: nameof(Enable));
            S.Patch(h, typeof(ElusiveAntlersBehavior), "FixedUpdate", typeof(ElusiveAntlers), prefix: nameof(Before), postfix: nameof(After));
            settings.Describe("ITEM_SPEEDBOOSTPICKUP_DESC", () => "Collect spawned shards to gain <style=cIsUtility>" + C.Get(speed) + "% movement speed</style> per buff for <style=cIsUtility>" + C.Get(duration) + " seconds</style>, up to <style=cIsUtility>" + C.Get(cap) + "</style> <style=cStack>(+" + C.Get(capStack) + " per stack)</style> buffs. Periodically spawns new shards.");
        }

        static void Cap(CharacterBody __instance, ref int __result)
        {
            if (settings.Active)
                __result = (int)S.Linear(S.Count(__instance, DLC2Content.Items.SpeedBoostPickup), C.Get(cap), C.Get(capStack));
        }

        static void Enable(ElusiveAntlersBehavior __instance)
        {
            if (settings.Active)
                A.Field(typeof(ElusiveAntlersBehavior), "spawnTimer").SetValue(__instance, C.Get(initial));
        }

        static void Before(ElusiveAntlersBehavior __instance, out float __state)
        {
            __state = (float)A.Field(typeof(ElusiveAntlersBehavior), "spawnTimer").GetValue(__instance);
        }

        static void After(ElusiveAntlersBehavior __instance, float __state)
        {
            if (!settings.Active || !NetworkServer.active || !__instance.body || __instance.body.isSpawnPickupsMaxed || __state > Time.fixedDeltaTime)
                return;
            int n = S.Count(__instance.body, DLC2Content.Items.SpeedBoostPickup);
            float k = C.Get(scaling);
            float seconds = C.Get(spawn) - (1 - k / (k + Math.Max(0, n - 1))) * C.Get(reduction);
            A.Field(typeof(ElusiveAntlersBehavior), "spawnTimer").SetValue(__instance, Mathf.Max(.02f, seconds));
        }
    }
}
