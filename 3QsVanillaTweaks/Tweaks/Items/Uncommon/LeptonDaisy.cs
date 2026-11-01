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
    internal static class LeptonDaisy
    {
        static UncommonConfig c;
        sealed class State
        {
            internal readonly Dictionary<TeamIndex, float> Timers = new Dictionary<TeamIndex, float>();
            internal readonly Dictionary<TeamIndex, int> Pulses = new Dictionary<TeamIndex, int>();
        }

        static readonly ConditionalWeakTable<HoldoutZoneController, State> states = new ConditionalWeakTable<HoldoutZoneController, State>();
        internal static void Init()
        {
            c = new UncommonConfig("Lepton Daisy", "TPHealingNova", true);
            c.B("Rework", true, "Pulse periodically while charging rather than at fixed charge milestones.");
            c.Pair("Healing (%)", 50, 10, "Maximum health healed per pulse. Vanilla: 50%, no healing scaling.");
            c.Pair("Pulses Per Event", 1, 1, "Number of pulses in normal mode. Vanilla: one per stack.");
            c.Pair("Pulse Interval", 20, -1, "Seconds between pulses in rework mode. Minimum interval is 1 second. Vanilla: based on charge progress.", -100000);
            c.B("Excess Healing Becomes Barrier", true, "Convert the part of a pulse that exceeds missing health into barrier.");
            c.B("Mention Holdout Zones", true, "Use holdout-zone wording instead of teleporter-event wording.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(HoldoutZoneController), "UpdateHealingNovas", typeof(LeptonDaisy), prefix: nameof(Native));
                // Holdout zones advance charge in Update, not FixedUpdate.
                U.Patch(typeof(HoldoutZoneController), "Update", typeof(LeptonDaisy), postfix: nameof(Tick));
                var pulse = typeof(EntityStates.TeleporterHealNovaController.TeleporterHealNovaPulse).GetNestedType("HealPulse", System.Reflection.BindingFlags.NonPublic);
                U.Patch(pulse, "HealTarget", typeof(LeptonDaisy), prefix: nameof(HealTarget));
            }

            c.StatValue = (key, n, value) => key == "Pulse Interval" ? Mathf.Max(1, value) : value;
            c.ShowStat = key => c.B("Rework") ? !key.StartsWith("Pulses Per Event") : !key.StartsWith("Pulse Interval");
            c.Text(() => "During a <style=cIsUtility>" + (c.B("Mention Holdout Zones") ? "holdout zone" : "teleporter event") + "</style>, release a healing nova " + (c.B("Rework") ? "every " + c.V("Pulse Interval", "s", "cIsUtility") : c.V("Pulses Per Event", "", "cIsUtility") + " times") + ", healing allies inside for " + c.V("Healing (%)", "%", "cIsHealing") + " of maximum health." + (c.B("Excess Healing Becomes Barrier") ? " Excess healing grants <style=cIsHealing>barrier</style>." : ""), () => "Release <style=cIsHealing>healing novas</style> during " + (c.B("Mention Holdout Zones") ? "holdout zones" : "teleporter events") + ".");
        }

        static bool Native() => !c.Active;
        static void Tick(HoldoutZoneController __instance)
        {
            var zone = __instance;
            if (!c.Active || !NetworkServer.active || !zone.isActiveAndEnabled || zone.charge <= 0 || zone.charge >= 1)
                return;
            var s = states.GetOrCreateValue(zone);
            var teams = new Dictionary<TeamIndex, int>();
            foreach (var m in CharacterMaster.readOnlyInstancesList)
                if (m && m.inventory)
                {
                    int n = m.inventory.GetItemCountEffective(c.Item);
                    if (n > 0)
                    {
                        teams.TryGetValue(m.teamIndex, out int old);
                        teams[m.teamIndex] = old + n;
                    }
                }

            foreach (var pair in teams)
            {
                int n = pair.Value;
                var team = pair.Key;
                bool pulse = false;
                if (c.B("Rework"))
                {
                    s.Timers.TryGetValue(team, out float timer);
                    timer += Time.deltaTime;
                    float interval = Mathf.Max(1, c.Total("Pulse Interval", n));
                    if (timer >= interval)
                    {
                        timer -= interval;
                        pulse = true;
                    }

                    s.Timers[team] = timer;
                }
                else
                {
                    s.Pulses.TryGetValue(team, out int done);
                    int total = Mathf.Max(0, Mathf.FloorToInt(c.Total("Pulses Per Event", n)));
                    if (done < total && zone.charge >= (done + 1f) / (total + 1f))
                    {
                        s.Pulses[team] = done + 1;
                        pulse = true;
                    }
                }

                if (pulse)
                    Pulse(zone, team, n);
            }
        }

        static void Pulse(HoldoutZoneController zone, TeamIndex team, int count)
        {
            var wrapped = A.Field(typeof(EntityStates.TeleporterHealNovaController.TeleporterHealNovaGeneratorMain), "pulsePrefab").GetValue(null);
            var prefab = wrapped == null ? null : (A.Property(wrapped.GetType(), "gameObject")?.GetValue(wrapped, null) ?? A.Field(wrapped.GetType(), "gameObject")?.GetValue(wrapped)) as GameObject;
            if (prefab)
            {
                var obj = UnityEngine.Object.Instantiate(prefab, zone.transform.position, zone.transform.rotation, zone.transform);
                obj.GetComponent<TeamFilter>().teamIndex = team;
                NetworkServer.Spawn(obj);
                return;
            }

            foreach (var b in CharacterBody.readOnlyInstancesList)
                if (b && b.healthComponent && b.teamComponent.teamIndex == team && (b.corePosition - zone.transform.position).sqrMagnitude <= zone.currentRadius * zone.currentRadius)
                    Heal(b.healthComponent, count);
        }

        static bool HealTarget(HealthComponent target)
        {
            if (!c.Active)
                return true;
            int count = 0;
            foreach (var m in CharacterMaster.readOnlyInstancesList)
                if (m && m.inventory && m.teamIndex == target.body.teamComponent.teamIndex)
                    count += m.inventory.GetItemCountEffective(c.Item);
            Heal(target, count);
            return false;
        }

        static void Heal(HealthComponent target, int count)
        {
            if (count <= 0 || !target.alive)
                return;
            float fraction = c.Total("Healing (%)", count) / 100;
            float before = target.health;
            float healed = target.HealFraction(fraction, default(ProcChainMask));
            if (c.B("Excess Healing Becomes Barrier") && !target.body.HasBuff(RoR2Content.Buffs.HealingDisabled))
            {
                float maximum = (float)A.Method(typeof(Tweaks.Others.ShieldMechanics), "HealingHealth").Invoke(null, new object[] { target });
                float excess = Mathf.Max(0, maximum * fraction - Mathf.Max(0, target.fullHealth - before));
                target.AddBarrier(excess);
            }

            Util.PlaySound("Play_item_proc_TPhealingNova_hitPlayer", target.gameObject);
        }
    }
}
