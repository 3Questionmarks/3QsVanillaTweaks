using RoR2;
using UnityEngine;
using Glass = Tweaks.ModCompatibility.LookingGlassStatRegistry;

namespace Tweaks.Items.Void
{
    // Native item ID: BearVoid. Configuration and hooks for this item only.
    internal static class SaferSpaces
    {
        const string Health = "RoR2.HealthComponent";
        const string RechargeTimeKey = "Recharge Time";
        const string RechargeReductionKey = "Recharge Reduction Per Stack (%)";
        const float NativeRechargeTime = 15f;
        const float NativeRechargeMultiplier = .9f;

        internal static void Init() => VoidConfigSupport.Register("BearVoid", Configure);

        static void Configure(VoidConfig c)
        {
            var values = c.Values;
            c.Number(RechargeTimeKey, NativeRechargeTime, "Base recharge time before the native per-stack reduction.");
            c.Number(RechargeReductionKey, 10, "Multiplicative recharge reduction per stack.");

            float RechargeTime() => values.Active ? values.F(RechargeTimeKey) : NativeRechargeTime;
            float RechargeMultiplier() => values.Active
                ? Mathf.Clamp01(1f - values.F(RechargeReductionKey) / 100f)
                : NativeRechargeMultiplier;

            for (int occurrence = 0; occurrence < 2; occurrence++)
            {
                values.Literal(Health, "TakeDamageProcess", NativeRechargeTime, RechargeTime, "BearVoidReady", occurrence, 140);
                values.Literal(Health, "TakeDamageProcess", NativeRechargeMultiplier, RechargeMultiplier, "BearVoidReady", occurrence, 140);
            }
            c.Assets("RoR2.BearVoidBehavior");

            Glass.RegisterItem(() => DLC1Content.Items.BearVoid ? DLC1Content.Items.BearVoid.itemIndex : ItemIndex.None,
                (_, stacks, text) => Glass.AddValue(text, "Total Cooldown",
                    RechargeTime() * Mathf.Pow(RechargeMultiplier(), Mathf.Max(1, stacks)), "s", "#91C8DD"));
        }
    }
}
