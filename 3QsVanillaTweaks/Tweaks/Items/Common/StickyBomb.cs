using A = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class StickyBomb
    {
        static ConfigEntry<bool> enabled;
        static ConfigEntry<float> damage, extra, chance, chanceStack, proc, radius, fuse, procStack;
        static ConfigEntry<BlastAttack.FalloffModel> falloff;
        static GameObject prefab;
        [ThreadStatic]
        static DamageInfo active;
        sealed class Scope
        {
            internal DamageInfo Previous;
        }

        internal static void Init()
        {
            var s = S.Section("Sticky Bomb");
            enabled = S.Enable(s);
            damage = S.Num(s, "Damage (%)", 240, "Total damage coefficient per bomb. Vanilla: 180%.");
            extra = S.Num(s, "Damage Per Stack (%)", 0, "Additional damage per extra stack. Vanilla: 0%.");
            chance = S.Num(s, "Trigger Chance (%)", 5, "First-stack chance, multiplied by the triggering hit's proc coefficient. Vanilla: 5%.");
            chanceStack = S.Num(s, "Trigger Chance Per Stack (%)", 5, "Extra chance per additional stack. Vanilla: 5%.");
            procStack = S.Num(s, "Proc Coefficient Per Stack", 0, "Extra bomb proc coefficient per additional stack.");
            if (!enabled.Value)
                return;
            var h = S.Harmony("StickyBomb");
            h.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy") ?? A.Method(typeof(GlobalEventManager), "OnHitEnemy"), prefix: new HarmonyMethod(typeof(StickyBomb), nameof(BeforeHit)), transpiler: new HarmonyMethod(typeof(StickyBomb), nameof(IL)), finalizer: new HarmonyMethod(typeof(StickyBomb), nameof(Finish)));
            S.Patch(h, typeof(ProjectileManager), "FireProjectile", typeof(StickyBomb), prefix: nameof(BeforeFire), args: new[] { typeof(FireProjectileInfo) });
            S.Patch(h, typeof(ProjectileController), "Start", typeof(StickyBomb), prefix: nameof(BeforeProjectile));
            RoR2Application.onLoad += () =>
            {
                prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/StickyBomb");
                var pc = prefab.GetComponent<ProjectileController>();
                var blast = prefab.GetComponent<ProjectileExplosion>();
                proc = S.Num(s, "Proc Coefficient", pc.procCoefficient * (blast ? blast.blastProcCoefficient : 1), "Bomb proc coefficient; default from the vanilla prefab.");
                radius = S.Num(s, "Explosion Radius", blast ? blast.blastRadius : 10, "Explosion radius in metres; default from vanilla prefab.");
                falloff = C.Bind(s, "Explosion Falloff", blast ? blast.falloffModel : BlastAttack.FalloffModel.None, "Explosion damage falloff; default from vanilla prefab.");
                var field = A.Field(blast.GetType(), "lifetimeAfterImpact");
                fuse = S.Num(s, "Fuse Duration", field == null ? 2 : (float)field.GetValue(blast), "Seconds after impact before detonation; default from vanilla prefab.");
            };
            S.Text("ITEM_STICKYBOMB_DESC", () => "<style=cIsDamage>" + C.Get(chance) + "%</style> <style=cStack>(+" + C.Get(chanceStack) + "% per stack)</style> chance on hit to attach a <style=cIsDamage>bomb</style> for <style=cIsDamage>" + C.Get(damage) + "%</style> <style=cStack>(+" + C.Get(extra) + "% per stack)</style> TOTAL damage.");
        }

        static void BeforeHit(DamageInfo damageInfo, out Scope __state)
        {
            __state = new Scope
            {
                Previous = active
            };
            active = damageInfo;
        }

        static Exception Finish(Scope __state, Exception __exception)
        {
            active = __state.Previous;
            return __exception;
        }

        static float ChanceCoefficient(float native)
        {
            if (!S.Active(enabled) || active == null || !active.attacker)
                return native;
            int n = S.Count(active.attacker.GetComponent<CharacterBody>(), RoR2Content.Items.StickyBomb);
            return n > 0 ? S.Total(chance, chanceStack, n) / n : native;
        }

        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int at = list.FindIndex(i => i.operand is FieldInfo f && f.Name == "StickyBomb");
            if (at < 0)
                throw new InvalidOperationException("Sticky Bomb count missing");
            int value = list.FindIndex(at, Math.Min(20, list.Count - at), i => i.LoadsConstant(5f));
            if (value < 0)
                throw new InvalidOperationException("Sticky Bomb chance missing");
            list.Insert(value + 1, new CodeInstruction(OpCodes.Call, A.Method(typeof(StickyBomb), nameof(ChanceCoefficient))));
            return list;
        }

        static void BeforeFire(ref FireProjectileInfo fireProjectileInfo)
        {
            if (!S.Active(enabled) || !prefab || fireProjectileInfo.projectilePrefab != prefab || !fireProjectileInfo.owner)
                return;
            int n = S.Count(fireProjectileInfo.owner.GetComponent<CharacterBody>(), RoR2Content.Items.StickyBomb);
            if (n > 0)
                fireProjectileInfo.damage *= S.Total(damage, extra, n) / 180f;
        }

        static void BeforeProjectile(ProjectileController __instance)
        {
            if (!S.Active(enabled) || !prefab || __instance.catalogIndex != prefab.GetComponent<ProjectileController>().catalogIndex)
                return;
            if (proc != null)
            {
                int n = S.Count(__instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null, RoR2Content.Items.StickyBomb);
                __instance.procCoefficient = S.Total(proc, procStack, Math.Max(1, n));
            }

            foreach (var blast in __instance.GetComponentsInChildren<ProjectileExplosion>(true))
            {
                blast.blastProcCoefficient = 1;
                if (radius != null)
                    blast.blastRadius = C.Get(radius);
                if (falloff != null)
                    blast.falloffModel = C.Get(falloff);
                var f = A.Field(blast.GetType(), "lifetimeAfterImpact");
                if (f != null && fuse != null)
                    f.SetValue(blast, C.Get(fuse));
            }
        }
    }
}
