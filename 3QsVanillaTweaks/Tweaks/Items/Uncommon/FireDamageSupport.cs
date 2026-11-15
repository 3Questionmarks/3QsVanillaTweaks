using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;

namespace Tweaks.Items.Uncommon
{
    internal static class FireDamageSupport
    {
        [ThreadStatic]
        internal static int Depth;
        static bool installed;
        static Func<bool> active;
        // Names cover vanilla projectiles and optional Artificer Extended states.
        static readonly string[] names =
        {
            "flamebolt",
            "firebolt",
            "flamethrower",
            "fireball",
            "magmaball",
            "firetornado",
            "firemeatball",
            "molten",
            "molotov",
            "hellfire",
            "helfire",
            "igniteonkill",
            "gasoline",
            "willowisp",
            "willo'wisp",
            "explodeondeathvoid",
            "voidsent",
            "wyrm",
            "chefsear",
            "incinerator",
            "lavabolt",
            "nanoeruption",
            "solarflare",
            "convectioncurrent"
        };
        internal sealed class FireTag : MonoBehaviour
        {
        }

        static bool Named(string value)
        {
            value = (value ?? "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            return names.Any(value.Contains);
        }

        internal static bool IsFire(DamageInfo info) => NativeFire(info) || Depth > 0 || info.dotIndex == DotController.DotIndex.Burn || info.dotIndex == DotController.DotIndex.StrongerBurn || info.dotIndex == DotController.DotIndex.Helfire || (info.damageType & DamageType.IgniteOnHit) != 0 || (info.inflictor && (info.inflictor.GetComponent<FireTag>() || Named(info.inflictor.name)));
        static readonly MethodInfo nativeFire = A.Method(typeof(DamageInfo), "IsConsideredFire", new[] { typeof(CharacterBody) });
        static bool NativeFire(DamageInfo info) => nativeFire != null && (bool)nativeFire.Invoke(info, new object[] { info.attacker ? info.attacker.GetComponent<CharacterBody>() : null });
        internal static void Enable(Func<bool> enabled)
        {
            active = enabled;
            if (installed)
                return;
            installed = true;
            var h = U.Harmony;
            h.Patch(A.Method(typeof(ProjectileManager), "FireProjectile", new[] { typeof(FireProjectileInfo) }), prefix: new HarmonyMethod(typeof(FireDamageSupport), nameof(Projectile)));
            RoR2Application.onLoad += () =>
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic)
                        continue;
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types.Where(t => t != null).ToArray();
                    }

                    foreach (var type in types)
                    {
                        if (type != null)
                            PatchFireType(h, type);
                    }
                }

                var gasoline = A.Method(typeof(GlobalEventManager), "ProcIgniteOnKill");
                if (gasoline != null)
                    h.Patch(gasoline, prefix: new HarmonyMethod(typeof(FireDamageSupport), nameof(Before)), finalizer: new HarmonyMethod(typeof(FireDamageSupport), nameof(Finish)));
            };
        }

        static void PatchFireType(Harmony harmony, Type type)
        {
            try
            {
                bool delayed = type == typeof(DelayBlast);
                // Avoid resolving inheritance for unrelated types, including Unity editor importers.
                if (!delayed && (!Named(type.FullName) || !typeof(EntityStates.EntityState).IsAssignableFrom(type)))
                    return;

                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                        continue;
                    if (!(method.Name == "OnEnter" || method.Name == "FixedUpdate" || method.Name.StartsWith("Fire", StringComparison.Ordinal) ||
                        (delayed && method.Name.IndexOf("Detonate", StringComparison.OrdinalIgnoreCase) >= 0)))
                        continue;
                    if (method.GetMethodBody() == null)
                        continue;

                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(FireDamageSupport), nameof(Before)),
                        finalizer: new HarmonyMethod(typeof(FireDamageSupport), nameof(Finish)));
                }
            }
            catch (Exception error) when (error is TypeLoadException || error is FileNotFoundException || error is FileLoadException)
            {
                ThreeQsVanillaTweaks.Log.Debug("Fire damage: skipped a type with an unavailable dependency: " + error.Message);
            }
        }

        static void Before(object __instance, out int __state)
        {
            __state = Depth;
            if (active() && (!(__instance is DelayBlast blast) || Named(blast.name)))
                Depth++;
        }

        static Exception Finish(int __state, Exception __exception)
        {
            Depth = __state;
            return __exception;
        }

        static void Projectile(ref FireProjectileInfo fireProjectileInfo)
        {
            var prefab = fireProjectileInfo.projectilePrefab;
            if (active() && prefab && (Depth > 0 || Named(prefab.name)) && !prefab.GetComponent<FireTag>())
                prefab.AddComponent<FireTag>();
        }
    }
}
