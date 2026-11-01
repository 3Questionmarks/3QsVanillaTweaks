using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ThreeQsVanillaTweaks;

namespace Tweaks.ModCompatibility
{
    // Optional LookingGlass integration. No reference to LookingGlass.dll.
    // Any item can register its own stat formatting in LookingGlassStatRegistry.
    internal static class LookingGlass
    {
        private static bool warned;
        internal static BepInEx.Configuration.ConfigEntry<bool> BindEnabled()
        {
            var entry = global::Config.ConfigManager.BindBool("Mod Compatibility - General", "LookingGlass Enabled", true, "Show item and skill stats that match this mod's changes in LookingGlass.");
            // BepInEx retains renamed keys as unbound entries unless explicitly removed.
            var property = typeof(BepInEx.Configuration.ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.NonPublic);
            var orphaned = property?.GetValue(entry.ConfigFile, null) as System.Collections.Generic.Dictionary<BepInEx.Configuration.ConfigDefinition, string>;
            orphaned?.Remove(new BepInEx.Configuration.ConfigDefinition("Mod Compatibility - LookingGlass", "Enabled"));
            return entry;
        }

        internal static void Init()
        {
            var enabled = BindEnabled();
            if (!global::Config.ConfigManager.Get(enabled))
                return;
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => string.Equals(a.GetName().Name, "LookingGlass", StringComparison.OrdinalIgnoreCase));
            if (assembly == null)
            {
                Log.Info("LookingGlass compatibility: not installed; skipping.");
                return;
            }

            Type type = assembly.GetType("LookingGlass.ItemStatsNameSpace.ItemStats", false);
            MethodInfo method = type?.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(m => m.Name == "GetItemStatsFormatted" && m.ReturnType == typeof(void) && m.GetParameters().Length == 4 && m.GetParameters()[2].ParameterType == typeof(string).MakeByRefType());
            if (method == null)
            {
                Log.Warning("LookingGlass compatibility: unsupported GetItemStatsFormatted signature.");
                return;
            }

            try
            {
                LookingGlassStatRegistry.ConfigureContext(assembly);
                new Harmony("3Questionmarks.3QsVanillaTweaks.LookingGlass.Tooltips").Patch(AccessTools.PropertyGetter(typeof(RoR2.UI.TooltipProvider), "bodyText"), postfix: new HarmonyMethod(typeof(LookingGlass), nameof(FullItemTooltip)));
                MethodInfo postfix = typeof(LookingGlass).GetMethod("AfterFormatting", BindingFlags.Static | BindingFlags.NonPublic);
                new Harmony("3Questionmarks.3QsVanillaTweaks.LookingGlass").Patch(method, prefix: new HarmonyMethod(typeof(LookingGlass), nameof(BeforeFormatting)), postfix: new HarmonyMethod(postfix));
                Log.Info("LookingGlass compatibility: installed generic registered-item stat formatter.");
            }
            catch (Exception e)
            {
                Log.Warning("LookingGlass compatibility: failed to install hook: " + e);
            }
        }

        // Resolve when displayed: LookingGlass can otherwise leave the pickup text
        // cached on icons, and Watch wear can change without the item count changing.
        private static void FullItemTooltip(RoR2.UI.TooltipProvider __instance, ref string __result)
        {
            try
            {
                var equipmentIcon = __instance.GetComponent<RoR2.UI.EquipmentIcon>();
                if (equipmentIcon)
                {
                    object display = AccessTools.Field(
                        typeof(RoR2.UI.EquipmentIcon), "currentDisplayData")
                        ?.GetValue(equipmentIcon);

                    var equipment = display == null ? null :
                        AccessTools.Field(display.GetType(), "equipmentDef")
                            ?.GetValue(display) as RoR2.EquipmentDef;

                    if (equipment)
                    {
                        string pickup = RoR2.Language.GetString(equipment.pickupToken);
                        string description = RoR2.Language.GetString(equipment.descriptionToken);

                        if (!string.IsNullOrEmpty(pickup) &&
                            __result != null &&
                            __result.StartsWith(pickup, StringComparison.Ordinal))
                        {
                            __result = description + __result.Substring(pickup.Length);
                        }
                    }
                    return;
                }
                var icon = __instance.GetComponent<RoR2.UI.ItemIcon>();
                if (!icon) return;
                var item = RoR2.ItemCatalog.GetItemDef(icon.itemIndex);
                if (!item) return;
                var master = RoR2.LocalUserManager.GetFirstLocalUser()?.cachedMaster;
                // Scoreboard icons may represent another player.
                foreach (var parent in icon.GetComponentsInParent<UnityEngine.MonoBehaviour>())
                {
                    if (!parent || parent.GetType().Name != "ScoreboardStrip") continue;
                    foreach (var field in parent.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        if (typeof(RoR2.CharacterMaster).IsAssignableFrom(field.FieldType))
                        {
                            var owner = field.GetValue(parent) as RoR2.CharacterMaster;
                            if (owner) master = owner;
                        }
                }
                int count = (int)(AccessTools.Field(typeof(RoR2.UI.ItemIcon), "itemCount")?.GetValue(icon) ?? 1);
                if (LookingGlassStatRegistry.TryFormatKnownItem(item.itemIndex, master, count, out string stats))
                {
                    string description = RoR2.Language.GetString(item.descriptionToken);
                    __result = description + (string.IsNullOrEmpty(stats) ? "" : "\n\n" + stats.TrimStart('\n'));
                }
            }
            catch (Exception error)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("LookingGlass full item tooltip failed: " + error);
                }
            }
        }

        private static bool BeforeFormatting(ref string __2, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                if (!LookingGlassStatRegistry.TryFormatItem(__2, __args, __originalMethod, out string replacement))
                    return true;
                __2 = replacement;
                return false;
            }
            catch (Exception error)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("LookingGlass item override failed; retaining its native formatter: " + error);
                }

                return true;
            }
        }

        private static void AfterFormatting(ref string __2, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                __2 = LookingGlassStatRegistry.Format(__2);
            }
            catch (Exception e)
            {
                if (warned)
                    return;
                warned = true;
                Log.Warning("LookingGlass compatibility: stat formatting failed: " + e);
            }
        }
    }
}
