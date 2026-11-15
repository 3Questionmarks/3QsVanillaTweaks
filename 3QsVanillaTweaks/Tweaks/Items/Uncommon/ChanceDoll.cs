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
    internal static class ChanceDoll
    {
        static UncommonConfig c;
        sealed class Purchase
        {
            internal Purchase Previous;
            internal ShrineChanceBehavior Shrine;
            internal CharacterBody Body;
            internal UniquePickup Reward;
            internal PickupDropTable Table;
            internal bool HasReward;
        }

        [ThreadStatic]
        static Purchase current;
        internal static void Init()
        {
            c = new UncommonConfig("Chance Doll", "ExtraShrineItem", true);
            c.B("Rework", true, "Successful Chance Shrines may drop additional items instead of upgrading the reward.");
            c.Pair("Trigger Chance (%)", 20, 20, "Chance per successful purchase. Vanilla: 40 +10 per extra stack.");
            c.Pair("Extra Items", 1, 0, "Extra rewards per successful roll. Vanilla: no extra rewards.");
            c.B("Convert Excess Chance To Extra Rewards", true, "Every full 100% guarantees an extra reward roll, with the remainder rolled normally.");
            c.B("Extra Items Are Copies", true, "Copy the shrine's original reward. If false, roll each additional reward independently using the shrine's current item and equipment odds.");
            c.Suppress("RoR2.ShrineChanceBehavior", "AddShrineStack", () => c.B("Rework"));
            // The native upgrade roll is 30 + 10 * count. Only change that roll.
            c.Literal("RoR2.ShrineChanceBehavior", "AddShrineStack", 30, () => c.F("Trigger Chance (%)") - c.F("Trigger Chance (%) Per Stack"), "ExtraShrineItem", 0, 45);
            c.Literal("RoR2.ShrineChanceBehavior", "AddShrineStack", 10, () => c.F("Trigger Chance (%) Per Stack"), "ExtraShrineItem", 0, 45);
            if (c.Enabled.Value)
            {
                U.Patch(typeof(ShrineChanceBehavior), "AddShrineStack", typeof(ChanceDoll), prefix: nameof(Before));
                var method = A.Method(typeof(ShrineChanceBehavior), "AddShrineStack");
                U.Harmony.Patch(method, finalizer: new HarmonyLib.HarmonyMethod(typeof(ChanceDoll), nameof(Finish)));
                S.Patch(U.Harmony, typeof(PickupDropletController), "CreatePickupDroplet", typeof(ChanceDoll), prefix: nameof(Droplet), args: new[] { typeof(GenericPickupController.CreatePickupInfo), typeof(Vector3), typeof(Vector3) });
            }

            c.ShowStat = key => c.B("Rework") || !key.StartsWith("Extra Items");
            c.Text(() => c.B("Rework") ? "Successful <style=cIsUtility>Shrines of Chance</style> have a " + c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance to drop " + c.V("Extra Items", "", "cIsUtility") + " additional " + (c.B("Extra Items Are Copies") ? "copies of the reward" : "rewards") + "." + (c.B("Convert Excess Chance To Extra Rewards") ? " Excess chance grants additional rewards." : "") : "Successful <style=cIsUtility>Shrines of Chance</style> have a " + c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance to upgrade the reward.", () => c.B("Rework") ? "Chance Shrines can drop <style=cIsUtility>extra rewards</style>." : "Chance Shrines can grant <style=cIsUtility>better rewards</style>.");
        }

        static void Before(ShrineChanceBehavior __instance, Interactor activator, out Purchase __state)
        {
            __state = current;
            var b = activator ? activator.GetComponent<CharacterBody>() : null;
            if (c.Count(b) > 0 && c.B("Rework") && NetworkServer.active)
                current = new Purchase
                {
                    Previous = current,
                    Shrine = __instance,
                    Body = b
                };
        }

        static void Droplet(GenericPickupController.CreatePickupInfo pickupInfo)
        {
            var pickup = pickupInfo.pickup;
            if (current != null && !current.HasReward)
            {
                current.Table = current.Shrine.dropTable;
                current.Reward = pickup;
                current.HasReward = pickup.isValid;
            }
        }

        static Exception Finish(Purchase __state, Exception __exception)
        {
            var p = current;
            current = __state;
            if (__exception != null || p == null || p == __state || !p.HasReward)
                return __exception;
            int n = c.Count(p.Body);
            float chance = c.Total("Trigger Chance (%)", n);
            float remainder = c.B("Convert Excess Chance To Extra Rewards") ? chance % 100 : Mathf.Min(chance, 100);
            int rolls = UncommonCalculations.ExtraRewardRolls(chance, c.B("Convert Excess Chance To Extra Rewards"), U.Roll(remainder, p.Body));
            int count = Mathf.Max(0, Mathf.FloorToInt(c.Total("Extra Items", n))) * rolls;
            var rng = new Xoroshiro128Plus(Run.instance.treasureRng.nextUlong);
            for (int i = 0; i < count; i++)
            {
                var pickup = p.Reward;
                if (!c.B("Extra Items Are Copies") && p.Table)
                    pickup = p.Table.GeneratePickup(rng);
                if (pickup.isValid)
                {
                    // Use the shrine's own launch direction and speed. Spread multiple
                    // extras slightly while keeping them clear of its top surface.
                    float spread = (i - (count - 1) * .5f) * 12f;
                    Vector3 velocity = Quaternion.AngleAxis(spread, Vector3.up) *
                        p.Shrine.dropletOrigin.forward * 20f;
                    PickupDropletController.CreatePickupDroplet(pickup, p.Shrine.dropletOrigin.position,
                        velocity, c.B("Extra Items Are Copies"));
                }
            }

            return __exception;
        }
    }
}
