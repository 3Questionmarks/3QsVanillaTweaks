using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Boss
{
    internal static class FaultyConductor
    {
        internal static void Init()
        {
            var c = BossItemSupport.Config("Faulty Conductor", "ShockDamageAura", false);
            c.N("Pulse Interval (Seconds)", 15, "Seconds between pulses. Vanilla: 15, with 20% shorter intervals per stack.", .01f);
            c.N("Pulse Interval Reduction Per Stack (%)", 20, "Interval reduction for each additional stack. Vanilla: 20%.");
            c.N("Pulse Radius (Metres)", 45, "Radius of the energizing pulse. Vanilla: 45 metres.");
            c.N("Buff Duration (Seconds)", 7, "Duration of the energized buff. Vanilla: 7 seconds.");
            c.N("Attack Speed Bonus (%)", 30, "Attack speed granted by the energized buff. Vanilla: 30%.");
            c.N("Movement Speed Bonus (%)", 30, "Movement speed granted by the energized buff. Vanilla: 30%.");
            c.Field("RoR2.Items.DroneShockDamageBodyBehavior", "get_duration", "durationBase", () => c.F("Pulse Interval (Seconds)"));
            c.Field("RoR2.Items.DroneShockDamageBodyBehavior", "get_duration", "durationStack", () => 1f - c.F("Pulse Interval Reduction Per Stack (%)") / 100f);
            c.Field("RoR2.Items.DroneShockDamageBodyBehavior", "TriggerEnergize", "radius", () => c.F("Pulse Radius (Metres)"));
            c.Field("RoR2.Items.DroneShockDamageBodyBehavior", "TriggerEnergize", "buffDuration", () => c.F("Buff Duration (Seconds)"));
            c.Field("RoR2.Orbs.ShockDamageAuraOrb", "OnArrival", "buffDuration", () => c.F("Buff Duration (Seconds)"));
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .3f, () => c.F("Movement Speed Bonus (%)") / 100f, "ShockDamageEnergized", 0, 100);
            c.Literal("RoR2.CharacterBody", "RecalculateStats", .3f, () => c.F("Attack Speed Bonus (%)") / 100f, "ShockDamageEnergized", 1, 130);
            c.Text(() => "Periodically energize nearby allies, increasing their <style=cIsUtility>attack speed</style> by " + c.V("Attack Speed Bonus (%)", "%", "cIsUtility") + " and <style=cIsUtility>movement speed</style> by " + c.V("Movement Speed Bonus (%)", "%", "cIsUtility") + ".", () => "Periodically energize nearby allies.");
        }
    }
}
