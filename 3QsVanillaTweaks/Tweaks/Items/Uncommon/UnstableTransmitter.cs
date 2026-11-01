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
    internal static class UnstableTransmitter
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Unstable Transmitter", "TeleportOnLowHealth", false);
            c.N("Health Threshold (%)", 25, "Remaining combined health required to activate. Vanilla: 25%.", 0, 100);
            c.Pair("Barrier (%)", 75, 0, "Barrier granted as a percentage of maximum health. Vanilla: 75%.");
            c.N("Invulnerability Duration", .2f, "Seconds of damage immunity on activation. Vanilla: 0.2.");
            c.N("Active Duration", 8, "Base active duration. Vanilla: 8 seconds.");
            c.N("Maximum Active Duration", 10, "Maximum active duration after kills. Vanilla: 10 seconds.");
            c.N("Duration Per Kill", 1, "Active time added by killing an affected enemy. Vanilla: 1 second.");
            c.N("Cooldown", 1.1f, "Seconds after the effect ends before becoming available. Vanilla: 1.1.");
            c.N("Cooldown Reduction Per Stack (%)", 10, "Cooldown reduction amplification per extra item. Vanilla: 10%.");
            c.Pair("Bleed Stacks", 3, 0, "Bleed stacks applied to enemies that enter the transmitter. Vanilla: 3.");
            c.Pair("Bleed Duration", 5, 0, "Bleed duration in seconds. Vanilla: 5.");
            c.N("Stun Duration", 1, "Enemy stun duration. Vanilla: 1 second.");
            c.N("Warp Distance", 35, "Distance enemies are teleported away. Vanilla: 35 metres.");
            c.N("Warp Padding", 10, "Additional placement padding. Vanilla: 10 metres.");
            c.N("Victim Mark Duration", 8, "How long affected enemies can extend the transmitter on kill. Vanilla: 8 seconds.");
            c.Literal("RoR2.HealthComponent", "DoWarp", 35, () => c.F("Warp Distance"));
            c.Literal("RoR2.HealthComponent", "DoWarp", 10, () => c.F("Warp Padding"));
            c.Literal("RoR2.HealthComponent", "DoWarp", 3, () => c.Total("Bleed Stacks", c.Count(UncommonNative.Source)));
            c.Literal("RoR2.HealthComponent", "DoWarp", 5, () => c.Total("Bleed Duration", c.Count(UncommonNative.Source)));
            c.Literal("RoR2.HealthComponent", "DoWarp", 1, () => c.F("Stun Duration"));
            c.Literal("RoR2.HealthComponent", "DoWarp", 8, () => c.F("Victim Mark Duration"));
            c.Literal("RoR2.HealthComponent", "TakeDamageProcess", .25f, () => c.F("Health Threshold (%)") / 100, "unstableTransmitter", 0, 30);
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "TryProc", .75f, () => c.F("Barrier (%)") / 100);
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "TryProc", 0, () => c.F("Barrier (%) Per Stack") / 100);
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "TryProc", .2f, () => c.F("Invulnerability Duration"));
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "TryProc", 8, () => c.F("Active Duration"));
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "RefreshCooldown", 1.1f, () => c.F("Cooldown"));
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "RefreshCooldown", .1f, () => c.F("Cooldown Reduction Per Stack (%)") / 100);
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "OnCharacterDeathGlobal", 1, () => c.F("Duration Per Kill"));
            c.Literal("RoR2.TeleportOnLowHealthBehavior", "OnCharacterDeathGlobal", 10, () => c.F("Maximum Active Duration"));
            c.Text(() => "Falling below " + c.V("Health Threshold (%)", "%", "cIsHealth") + " health activates a <style=cIsUtility>transmitter</style> for " + c.V("Active Duration", "s", "cIsUtility") + " and grants " + c.V("Barrier (%)", "%", "cIsHealing") + " barrier. Kills extend its duration. Regenerates next stage.", () => "At low health, activate a <style=cIsUtility>transmitter</style> and gain barrier. Regenerates next stage.");
        }
    }
}
