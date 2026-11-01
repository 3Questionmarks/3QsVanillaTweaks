using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class BensRaincoat
    {
        static R c;
        static GameObject effect;
        sealed class Protection
        {
            internal int Ready = -1, Capacity;
            internal float Recharge, Grace;
        }

        static readonly ConditionalWeakTable<CharacterBody, Protection> states = new ConditionalWeakTable<CharacterBody, Protection>();
        internal static void Init()
        {
            c = RareItemSupport.Config("Ben's Raincoat", "ImmuneToDebuff");
            c.Pair("Debuff Preventions", 2, 1, "Available debuff blocks. Vanilla: 1 per stack.");
            c.Pair("Barrier Gained (%)", 15, 0, "Barrier gained per block, as a percentage of maximum combined health. Vanilla: 10%.");
            c.Pair("Cooldown", 5, 0, "Seconds to refill all blocks. Vanilla: 5.");
            c.Pair("Debuff Grace Period", .25f, 0, "Seconds of debuff immunity after a block; grants no additional barrier. Vanilla: one physics tick.");
            c.B("Cooldown Starts Instantly", true, "Start recharging on every block, resetting the timer when another charge is spent. Remaining charges remain usable. When false, recharge starts after the last block. Vanilla: false.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance for a charge to prevent a debuff. Vanilla: 100%.");
            if (c.Enabled.Value)
            {
                U.Ensure();
                U.Tick += Tick;
                U.Patch(typeof(RoR2.Items.ImmuneToDebuffBehavior), "TryApplyOverride", typeof(BensRaincoat), prefix: nameof(Block));
                U.Patch(typeof(RoR2.Items.ImmuneToDebuffBehavior), "FixedUpdate", typeof(BensRaincoat), prefix: nameof(NativeTick));
                RoR2Application.onLoad += () => effect = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>("RoR2/DLC1/ImmuneToDebuff/ImmuneToDebuffEffect.prefab").WaitForCompletion();
            }

            c.Text(() => "Prevent " + c.V("Debuff Preventions", "", "cIsUtility") + " debuffs, gaining " + c.V("Barrier Gained (%)", "%", "cIsHealing") + " of maximum health as barrier. Recharge all blocks after " + c.V("Cooldown", "s", "cIsUtility") + ". Blocking a debuff grants " + c.V("Debuff Grace Period", "s", "cIsUtility") + " of debuff immunity.", () => "Prevent debuffs and gain barrier. Recharges over time.");
        }

        static bool NativeTick(RoR2.Items.ImmuneToDebuffBehavior __instance) => !c.Active;
        static Protection Get(CharacterBody b, int n)
        {
            var s = states.GetOrCreateValue(b);
            int cap = Mathf.Max(0, Mathf.RoundToInt(c.Total("Debuff Preventions", n)));
            if (s.Ready < 0)
                s.Ready = cap;
            else
                s.Ready = Mathf.Clamp(s.Ready + Mathf.Max(0, cap - s.Capacity), 0, cap);
            s.Capacity = cap;
            return s;
        }

        static void Tick(CharacterBody b)
        {
            int n = c.Count(b);
            if (n == 0)
                return;
            var s = Get(b, n);
            if (s.Recharge > 0 && Time.fixedTime >= s.Recharge)
            {
                s.Recharge = 0;
                s.Ready = s.Capacity;
                b.ClearTimedBuffs(DLC1Content.Buffs.ImmuneToDebuffCooldown);
            }

            b.SetBuffCount(DLC1Content.Buffs.ImmuneToDebuffReady.buffIndex, s.Ready);
        }

        static bool Block(CharacterBody body, ref bool __result)
        {
            if (!c.Active)
                return true;
            __result = false;
            if (!NetworkServer.active || !body || !body.healthComponent)
                return false;
            int n = c.Count(body);
            if (n == 0)
                return false;
            Tick(body);
            var s = Get(body, n);
            if (Time.fixedTime < s.Grace)
            {
                __result = true;
                return false;
            }

            if (s.Ready <= 0 || !U.Roll(c.Total("Trigger Chance (%)", n), body))
                return false;
            s.Ready--;
            s.Grace = Time.fixedTime + c.Total("Debuff Grace Period", n);
            body.healthComponent.AddBarrier(body.healthComponent.fullCombinedHealth * c.Total("Barrier Gained (%)", n) / 100f);
            if (c.B("Cooldown Starts Instantly") || s.Ready == 0)
            {
                float cd = c.Total("Cooldown", n);
                s.Recharge = Time.fixedTime + Mathf.Max(.001f, cd);
                body.ClearTimedBuffs(DLC1Content.Buffs.ImmuneToDebuffCooldown);
                body.AddTimedBuff(DLC1Content.Buffs.ImmuneToDebuffCooldown, Mathf.Max(.001f, cd));
            }

            body.SetBuffCount(DLC1Content.Buffs.ImmuneToDebuffReady.buffIndex, s.Ready);
            if (effect)
                EffectManager.SimpleImpactEffect(effect, body.corePosition, Vector3.up, true);
            __result = true;
            return false;
        }
    }
}
