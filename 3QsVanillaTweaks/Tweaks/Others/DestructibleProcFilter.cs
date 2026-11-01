using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Others
{
    internal static class DestructibleProcFilter
    {
        static ConfigEntry<bool> enabled;
        static bool Active => enabled != null && C.Get(enabled);

        internal static void Init()
        {
            enabled = C.BindStartupBool("Other - Destructible Props", "Prevent Item Procs", true,
                "Destructible props cannot trigger on-hit or on-kill item effects. Normal damage, destruction and objective death notifications are preserved. Restart to apply. Vanilla: false.");
            if (!enabled.Value) return;
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.DestructibleProcFilter");
            Patch(harmony, "OnHitEnemy", nameof(HitEnemy));
            Patch(harmony, "ProcessHitEnemy", nameof(HitEnemy));
            Patch(harmony, "OnHitAll", nameof(HitAll));
            Patch(harmony, "OnHitAllProcess", nameof(HitAll));
            Patch(harmony, "TriggerCharacterDeathProc", nameof(DeathProc));
            Patch(harmony, "OnCharacterDeath", nameof(DeathProc));
            Patch(harmony, "ServerDamageDealt", nameof(DamageEvent));
            // Reworked items can also act before the normal hit-proc stage.
            harmony.Patch(A.Method(typeof(Tweaks.Items.Uncommon.UncommonItemSupport), "Damage"),
                prefix: new HarmonyMethod(typeof(DestructibleProcFilter), nameof(ItemDamage)) { priority = Priority.First });
        }

        static void Patch(Harmony harmony, string method, string prefix)
        {
            MethodInfo target = A.Method(typeof(GlobalEventManager), method);
            if (target != null)
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(DestructibleProcFilter), prefix) { priority = Priority.First });
        }

        static bool IsCharacter(CharacterBody body, CharacterMaster master)
        {
            // Neutral scenery sometimes has a CharacterBody for damage handling.
            // A real character also has a master and belongs to a combat team.
            if (!body || !master || !body.teamComponent) return false;
            TeamIndex team = body.teamComponent.teamIndex;
            return team != TeamIndex.Neutral && team != TeamIndex.None;
        }

        static HealthComponent Health(GameObject target)
        {
            if (!target) return null;
            var hurtBox = target.GetComponent<HurtBox>();
            return hurtBox && hurtBox.healthComponent ? hurtBox.healthComponent
                : target.GetComponent<HealthComponent>() ?? target.GetComponentInParent<HealthComponent>();
        }

        static bool IsProp(HealthComponent health)
        {
            return health && !IsCharacter(health.body, health.body ? health.body.master : null);
        }

        static bool HitEnemy(DamageInfo __0, GameObject __1)
        {
            if (!Active) return true;
            HealthComponent health = Health(__1);
            return health && !IsProp(health);
        }

        static bool HitAll(DamageInfo __0, GameObject __1)
        {
            // A terrain impact is not a destroyed prop: keep normal impact effects.
            return !Active || !IsProp(Health(__1));
        }

        static bool DeathProc(DamageReport __0)
        {
            return !Active || __0 == null || IsCharacter(__0.victimBody, __0.victimMaster);
        }

        static bool DamageEvent(DamageReport __0)
        {
            return !Active || __0 == null || IsCharacter(__0.victimBody, __0.victimMaster);
        }

        static bool ItemDamage(HealthComponent __0) => !Active || !IsProp(__0);
    }
}
