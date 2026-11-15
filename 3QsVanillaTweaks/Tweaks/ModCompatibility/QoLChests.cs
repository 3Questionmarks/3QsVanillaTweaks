using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C = Config.ConfigManager;

namespace Tweaks.ModCompatibility
{
    // QoLChests owns the category choices and colours. This only limits the
    // highlights that QoLChests would otherwise enable for this local player.
    internal static class QoLChests
    {
        private const string Section = "Mod Compatibility - Interactables - QoL Chests";
        private static ConfigEntry<float> range;
        private static ConfigEntry<bool> disableWhileBlind;
        private static PropertyInfo trackedProperty, configInstance;
        private static FieldInfo removeWhenUsed;
        private static MethodInfo enableMethod, disableMethod;
        private static Type usedType, hideType, fadeType;
        private static ItemIndex blindnessItem = ItemIndex.None;
        private static float nextUpdate;
        private static bool warned;

        internal static void Init()
        {
            range = C.Bind(Section, "Glow Range (m)", 100f,
                new ConfigDescription("Maximum distance from your character at which QoL Chests highlights interactables.",
                    new AcceptableValueRange<float>(0f, 1000f)), clientSide: true);
            disableWhileBlind = C.BindBool(Section, "Disable Glow While Blind", true,
                "Hide QoL Chests highlights while you have Hex of Blindness or the Blinded status.", clientSide: true);

            // Other plugins may not have run Awake yet when our Init is called.
            RoR2Application.onLoad += Install;
        }

        private static void Install()
        {
            RoR2Application.onLoad -= Install;
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("Faust.QoLChests", out var plugin)) return;
            try
            {
                Assembly assembly = plugin.Instance.GetType().Assembly;
                Type handler = assembly.GetType("Faust.QoLChests.Handlers.HighlightHandler", true);
                Type state = assembly.GetType("Faust.QoLChests.Handlers.InteractableStateHandler", true);
                Type settings = assembly.GetType("Faust.QoLChests.Configs.ModConfig", true);
                enableMethod = AccessTools.Method(handler, "Enable", new[] { typeof(GameObject), typeof(bool) });
                disableMethod = AccessTools.Method(handler, "Disable", new[] { typeof(GameObject) });
                trackedProperty = AccessTools.Property(state, "InteractablesToHighlight");
                configInstance = AccessTools.Property(settings, "Instance");
                removeWhenUsed = AccessTools.Field(settings, "RemoveHighlightFromUsed");
                usedType = assembly.GetType("Faust.QoLChests.Components.InteractableUsed", true);
                hideType = assembly.GetType("Faust.QoLChests.Components.HideWithDelay", true);
                fadeType = assembly.GetType("Faust.QoLChests.Components.FadeWithDelay", true);
                if (enableMethod == null || disableMethod == null || trackedProperty == null ||
                    configInstance == null || removeWhenUsed == null)
                    throw new MissingMemberException("QoLChests highlight API changed.");

                new Harmony("3Questionmarks.3QsVanillaTweaks.QoLChestsRange")
                    .Patch(enableMethod, prefix: new HarmonyMethod(typeof(QoLChests), nameof(BeforeEnable)));
                RoR2Application.onUpdate += Update;
            }
            catch (Exception error)
            {
                ThreeQsVanillaTweaks.Log.Warning("QoL Chests glow range could not be installed: " + error);
            }
        }

        private static bool BeforeEnable(GameObject interactable)
        {
            return interactable && !Blocked(interactable.transform.position);
        }

        private static bool Blocked(Vector3 position)
        {
            LocalUser local = LocalUserManager.GetFirstLocalUser();
            CharacterBody body = local != null ? local.cachedBody : null;
            if (!body) return true;

            if (C.Get(disableWhileBlind))
            {
                if (blindnessItem == ItemIndex.None)
                    blindnessItem = ItemCatalog.FindItemIndex("Blindness");
                if (body.HasBuff(DLC1Content.Buffs.Blinded) ||
                    (blindnessItem != ItemIndex.None && body.inventory && body.inventory.GetItemCountEffective(blindnessItem) > 0))
                    return true;
            }

            float distance = Mathf.Max(0f, C.Get(range));
            return (position - body.corePosition).sqrMagnitude > distance * distance;
        }

        private static void Update()
        {
            if (Time.unscaledTime < nextUpdate) return;
            nextUpdate = Time.unscaledTime + .2f;
            try
            {
                var tracked = trackedProperty.GetValue(null, null) as IEnumerable;
                if (tracked == null) return;
                var settings = configInstance.GetValue(null, null);
                var removeUsed = removeWhenUsed.GetValue(settings) as ConfigEntry<bool>;
                foreach (object candidate in tracked)
                {
                    GameObject interactable = candidate as GameObject;
                    if (!interactable) continue;
                    Highlight highlight = interactable.GetComponent<Highlight>();
                    if (!highlight) continue;

                    if (Blocked(interactable.transform.position))
                    {
                        if (highlight.enabled && highlight.isOn)
                            disableMethod.Invoke(null, new object[] { interactable });
                    }
                    else if (!highlight.enabled || !highlight.isOn)
                    {
                        // QoLChests skips used/hidden objects on each scene refresh.
                        // Respect its separate setting to retain highlights after use.
                        if (interactable.GetComponent(hideType) || interactable.GetComponent(fadeType) ||
                            (removeUsed != null && removeUsed.Value && interactable.GetComponent(usedType)))
                            continue;
                        enableMethod.Invoke(null, new object[] { interactable, false });
                    }
                }
            }
            catch (Exception error)
            {
                if (warned) return;
                warned = true;
                ThreeQsVanillaTweaks.Log.Warning("QoL Chests glow range update failed: " + error);
            }
        }
    }
}
