using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RoR2;
using R2API;
using UnityEngine;
using UnityEngine.Networking;

namespace Tweaks.Others.RemoteOperation
{
    // Shared by Healing Drone's fully charged shot and Emergency Drone's slowed-target shot.
    // Each application adds one independently ticking damage stack, refreshes all existing
    // stacks' remaining durations, and increases damage-to-attacker healing additively.
    internal static class RemoteLeech
    {
        internal static BuffDef Buff { get; private set; }
        private static DamageColorIndex leechDamageColor;
        private static bool damageColorRegistered;
        private static bool patched;

        // Distinctive dark forest-green ticks. Registered once for both healing drones.
        private static readonly Color LeechGreen = new Color(0.14f, 0.49f, 0.27f, 1f);

        // Reuse the game's Life Steal symbol rather than the unrelated Weak icon.
        // Reflection keeps the fallbacks compatible with variations in RoR2Content.
        private static Sprite GetLeechIcon()
        {
            foreach (string name in new[] { "LifeSteal", "Poisoned", "Bleeding", "Weak" })
            {
                FieldInfo field = typeof(RoR2Content.Buffs).GetField(name,
                    BindingFlags.Public | BindingFlags.Static);
                BuffDef vanilla = field?.GetValue(null) as BuffDef;
                if (vanilla && vanilla.iconSprite)
                    return vanilla.iconSprite;
            }
            return null;
        }

        internal static void Register()
        {
            if (!damageColorRegistered)
            {
                leechDamageColor = ColorsAPI.RegisterDamageColor(LeechGreen);
                damageColorRegistered = true;
            }
            if (Buff)
            {
                if (!Buff.iconSprite) Buff.iconSprite = GetLeechIcon();
                return;
            }
            Buff = ScriptableObject.CreateInstance<BuffDef>();
            Buff.name = "3QSRemoteLeech";
            Buff.buffColor = LeechGreen;
            Buff.isDebuff = true;
            Buff.canStack = true;
            Buff.iconSprite = GetLeechIcon();
        }

        internal static void Install()
        {
            // Native content is populated by this point even on builds where it was
            // unavailable during the content-pack registration phase.
            if (Buff && !Buff.iconSprite) Buff.iconSprite = GetLeechIcon();
            if (patched) return;
            patched = true;
            var method = AccessTools.Method(typeof(HealthComponent), nameof(HealthComponent.TakeDamage));
            new Harmony("3Questionmarks.3QsVanillaTweaks.RemoteLeech").Patch(method,
                prefix: new HarmonyMethod(typeof(RemoteLeech), nameof(BeforeDamage)),
                postfix: new HarmonyMethod(typeof(RemoteLeech), nameof(AfterDamage)));
        }

        internal static void Apply(CharacterBody victim, CharacterBody source, float duration,
            float tickInterval, float tickDamagePercent, float healingPercent)
        {
            if (!NetworkServer.active || !Buff || !victim || !victim.healthComponent || !source) return;
            var status = victim.GetComponent<LeechStatus>() ?? victim.gameObject.AddComponent<LeechStatus>();
            status.Add(source, duration, tickInterval, tickDamagePercent, healingPercent);
        }

        private static void BeforeDamage(HealthComponent __instance, out float __state)
        {
            __state = __instance ? __instance.combinedHealth : 0f;
        }

        private static void AfterDamage(HealthComponent __instance, DamageInfo damageInfo, float __state)
        {
            if (!NetworkServer.active || !__instance || !__instance.body || damageInfo == null ||
                !damageInfo.attacker) return;
            var status = __instance.GetComponent<LeechStatus>();
            if (!status) return;
            float amount = Mathf.Max(0f, __state - __instance.combinedHealth);
            if (amount <= 0f) return;
            var attacker = damageInfo.attacker.GetComponent<HealthComponent>();
            if (attacker && attacker != __instance && attacker.alive)
            {
                float healing = amount * status.TotalHealingPercent / 100f;
                if (healing > 0f) attacker.Heal(healing, default(ProcChainMask));
            }
        }

        public sealed class LeechStatus : MonoBehaviour
        {
            private sealed class Stack
            {
                internal CharacterBody Source;
                internal float Remaining, Interval, NextTick, DamagePercent, HealingPercent;
            }
            private readonly List<Stack> stacks = new List<Stack>();
            private CharacterBody victim;
            private int appliedCount = -1;
            internal float TotalHealingPercent
            {
                get
                {
                    float total = 0f;
                    for (int i = 0; i < stacks.Count; i++) total += stacks[i].HealingPercent;
                    return total;
                }
            }

            private void Awake() { victim = GetComponent<CharacterBody>(); }

            internal void Add(CharacterBody source, float duration, float interval, float damage, float healing)
            {
                float length = Mathf.Max(.05f, duration);
                // Reapplying adds another stack and refreshes the existing stacks' durations.
                for (int i = 0; i < stacks.Count; i++) stacks[i].Remaining = length;
                stacks.Add(new Stack {
                    Source = source, Remaining = length, Interval = Mathf.Max(.05f, interval),
                    NextTick = Mathf.Max(.05f, interval), DamagePercent = Mathf.Max(0f, damage),
                    HealingPercent = Mathf.Max(0f, healing)
                });
                RefreshBuff();
            }

            private void RefreshBuff()
            {
                if (NetworkServer.active && victim && Buff && Buff.buffIndex != BuffIndex.None &&
                    appliedCount != stacks.Count)
                {
                    appliedCount = stacks.Count;
                    victim.SetBuffCount(Buff.buffIndex, stacks.Count);
                }
            }

            private void FixedUpdate()
            {
                if (!NetworkServer.active) return;
                if (!victim || !victim.healthComponent || !victim.healthComponent.alive)
                {
                    stacks.Clear(); RefreshBuff(); Destroy(this); return;
                }
                float dt = Time.fixedDeltaTime;
                for (int i = stacks.Count - 1; i >= 0; i--)
                {
                    Stack stack = stacks[i];
                    stack.Remaining -= dt;
                    if (stack.Remaining <= 0f) { stacks.RemoveAt(i); continue; }
                    stack.NextTick -= dt;
                    if (stack.NextTick <= 0f)
                    {
                        stack.NextTick += stack.Interval;
                        if (stack.Source && stack.DamagePercent > 0f)
                        {
                            var hit = new DamageInfo {
                                attacker = stack.Source.gameObject, inflictor = gameObject,
                                damage = stack.Source.damage * stack.DamagePercent / 100f,
                                procCoefficient = 0f, position = victim.corePosition,
                                damageColorIndex = leechDamageColor
                            };
                            victim.healthComponent.TakeDamage(hit);
                        }
                    }
                }
                RefreshBuff();
                if (stacks.Count == 0) Destroy(this);
            }
            private void OnDestroy()
            {
                if (NetworkServer.active && victim && Buff && Buff.buffIndex != BuffIndex.None)
                    victim.SetBuffCount(Buff.buffIndex, 0);
            }
        }
    }
}
