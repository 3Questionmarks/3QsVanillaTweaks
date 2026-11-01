using System;

namespace Tweaks.Items.Uncommon
{
    internal static class UncommonCalculations
    {
        internal static float StealthChance(float baseChance, float hitFraction, float guaranteedFraction) => Math.Min(100, Math.Max(0, baseChance + (100 - baseChance) * Math.Min(1, Math.Max(0, hitFraction / Math.Max(.0001f, guaranteedFraction)))));
        internal static float InfusionRequirement(float initial, float bonus, float threshold, float increment) => Math.Max(1, initial + (float)Math.Floor(bonus / Math.Max(1, threshold)) * increment);
        internal static int ExtraRewardRolls(float chance, bool overflow, bool remainderSucceeded) => (overflow ? Math.Max(0, (int)Math.Floor(chance / 100)) : 0) + (remainderSucceeded ? 1 : 0);
        internal static float StackCooldown(float cooldown, float reduction, int count) => cooldown * (float)Math.Pow(1 - Math.Min(1, Math.Max(0, reduction / 100)), Math.Max(0, count - 1));
    }
}
