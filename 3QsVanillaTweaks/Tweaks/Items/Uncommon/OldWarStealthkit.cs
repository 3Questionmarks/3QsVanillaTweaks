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
    internal static class OldWarStealthkit
    {
        static UncommonConfig c;
        static BuffDef haste, cooldown;
        internal static void Init()
        {
            c = new UncommonConfig("Old War Stealthkit", "Phasing", true);
            c.B("Rework", true, "Trigger on taking damage with a chance that grows with the hit size, instead of automatically at low health.");
            c.Pair("Trigger Chance (%)", 5, 0, "Base chance after taking damage in rework mode. Vanilla: automatic at low health.");
            c.N("Guaranteed Hit Size (%)", 70, "A hit dealing this percentage of maximum combined health guarantees activation. Chance scales linearly up to this size.", 0, 100);
            c.N("Guaranteed Hit Size (%) Per Stack", 0, "Change to the guaranteed-hit threshold per additional item. Negative values lower the threshold.", -100, 100);
            c.N("Cooldown", 10, "Recharge seconds after activation. Vanilla: 30.");
            c.N("Cooldown Reduction Per Stack (%)", 50, "Multiplicative cooldown reduction per extra item. Vanilla: 50%.", 0, 100);
            c.Pair("Duration", 5, 0, "Invisibility and speed duration. Vanilla: 5 seconds.");
            c.Pair("Movement Speed (%)", 40, 0, "Speed during invisibility. Vanilla: 40%.");
            c.N("Low Health Threshold (%)", 25, "Normal-mode activation threshold. Vanilla: 25%.", 0, 100);
            if (c.Enabled.Value)
                U.Patch(typeof(CharacterBody).Assembly.GetType("RoR2.Items.PhasingBodyBehavior", true), "FixedUpdate", typeof(OldWarStealthkit), prefix: nameof(Native));
            haste = U.Buff("Stealthkit Haste", false, () => RoR2Content.Buffs.CloakSpeed, () => "Increased <style=cIsUtility>movement speed</style> while cloaked.");
            cooldown = U.Buff("Stealthkit Cooldown", false, () => RoR2Content.Buffs.CloakSpeed,
                () => "Old War Stealthkit is recharging.");
            cooldown.canStack = false;
            U.Ensure();
            U.Hit += Hit;
            U.Tick += Tick;
            U.Stats += b =>
            {
                int n = c.Count(b);
                if (n > 0 && b.HasBuff(haste))
                    U.Scale(b, "moveSpeed", c.Total("Movement Speed (%)", n));
            };
            c.StatValue = (key, n, value) => key == "Cooldown" ? UncommonCalculations.StackCooldown(c.F("Cooldown"), c.F("Cooldown Reduction Per Stack (%)"), n) : key == "Guaranteed Hit Size (%)" ? Mathf.Max(0, value) : value;
            c.ShowStat = key => c.B("Rework") ? !key.StartsWith("Low Health Threshold") : !key.StartsWith("Trigger Chance") && !key.StartsWith("Guaranteed Hit Size");
            c.Text(() => c.B("Rework") ? "Taking damage has a " + c.V("Trigger Chance (%)", "%", "cIsUtility") + " chance to <style=cIsUtility>cloak</style> you for " + c.V("Duration", "s", "cIsUtility") + " and increase movement speed by " + c.V("Movement Speed (%)", "%", "cIsUtility") + ". Larger hits increase the chance, guaranteed at " + c.V("Guaranteed Hit Size (%)", "%", "cIsHealth") + " of maximum health. Recharges in " + c.V("Cooldown", "s", "cIsUtility") + " <style=cStack>(-" + c.F("Cooldown Reduction Per Stack (%)") + "% per stack)</style>." : "Falling below " + c.V("Low Health Threshold (%)", "%", "cIsHealth") + " health cloaks you and increases movement speed for " + c.V("Duration", "s", "cIsUtility") + ". Recharges in " + c.V("Cooldown", "s", "cIsUtility") + ".", () => c.B("Rework") ? "Taking damage can <style=cIsUtility>cloak</style> you and increase movement speed. Larger hits are more likely to trigger it." : "Cloak and move faster at <style=cIsHealth>low health</style>.");
        }

        static bool Native() => !c.Active;
        static void Activate(CharacterBody b, int n)
        {
            if (b.HasBuff(cooldown))
                return;
            b.AddTimedBuff(RoR2Content.Buffs.Cloak, c.Total("Duration", n));
            b.AddTimedBuff(haste, c.Total("Duration", n));
            b.AddTimedBuff(cooldown, Mathf.Max(.01f, UncommonCalculations.StackCooldown(c.F("Cooldown"), c.F("Cooldown Reduction Per Stack (%)"), n)));
        }

        static void Hit(DamageReport r)
        {
            var b = r.victimBody;
            int n = c.Count(b);
            if (n <= 0 || !c.B("Rework") || b.HasBuff(cooldown) || !b.healthComponent.alive)
                return;
            float fraction = r.damageDealt / Mathf.Max(1, b.healthComponent.fullCombinedHealth), limit = Mathf.Max(.0001f, c.Total("Guaranteed Hit Size (%)", n) / 100);
            float chance = UncommonCalculations.StealthChance(c.Total("Trigger Chance (%)", n), fraction, limit);
            if (U.Roll(chance, b))
                Activate(b, n);
        }

        static void Tick(CharacterBody b)
        {
            int n = c.Count(b);
            if (n > 0 && !c.B("Rework") && b.healthComponent.alive && b.healthComponent.combinedHealth / b.healthComponent.fullCombinedHealth <= c.F("Low Health Threshold (%)") / 100)
                Activate(b, n);
        }
    }
}
