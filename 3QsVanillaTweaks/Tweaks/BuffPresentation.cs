using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using RoR2;
using RoR2.UI;
using UnityEngine;
using C = Config.ConfigManager;
using Huntress = Tweaks.Survivors.Huntress;
using Captain = Tweaks.Survivors.Captain;
using Engineer = Tweaks.Survivors.Engineer;
using OSP = Tweaks.Others.OneShotProtection;

namespace Tweaks
{
    // Independent of module/compatibility switches: registered buffs retain their UI.
    internal static class BuffPresentation
    {
        private sealed class Entry
        {
            internal Func<BuffIcon, string> Title, Description;
            internal Func<Sprite> Icon;
        }
        private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, FieldInfo> fields = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
        private static bool installed;
        private static T Setting<T>(Type type, string name, T fallback)
        {
            string key = type.FullName + "." + name;
            if (!fields.TryGetValue(key, out FieldInfo field))
            {
                field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                fields[key] = field;
            }
            return field?.GetValue(null) is ConfigEntry<T> entry ? C.Get(entry) : fallback;
        }
        private static string N(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Utility(string text) => "<style=cIsUtility>" + text + "</style>";
        private static string Healing(string text) => "<style=cIsHealing>" + text + "</style>";

        internal static void Init() { RoR2Application.onLoad += Install; }
        private static void Install()
        {
            if (installed) return;
            installed = true;
            entries.Add("3QS_HUNTRESS_MOMENTUM", new Entry
            {
                Title = icon => "Momentum", Description = Momentum,
                Icon = () => RoR2Content.Buffs.FullCrit ? RoR2Content.Buffs.FullCrit.iconSprite : null
            });
            entries.Add("3QSCaptainSupport", new Entry
            {
                Title = icon => Setting(typeof(Captain), "renameSupport", true) ? "Support Beacon" : "Healing Beacon",
                Description = Support,
                Icon = () => RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null
            });
            entries.Add("3QS_CARBONIZER_SLOW", new Entry
            {
                Title = icon => "Carbonizer Slow",
                Description = icon =>
                {
                    float coefficient = Setting(typeof(Engineer), "walkerSlow", 50);
                    float reduction = 100f * coefficient / Math.Max(1f, 100f + coefficient);
                    return "Slowed by a Carbonizer Turret. Reduces movement speed by " + Utility(N(reduction) + "%") + " before other slowing effects.";
                },
                Icon = () => RoR2Content.Buffs.Slow50 ? RoR2Content.Buffs.Slow50.iconSprite : null
            });
            entries.Add("3QS_ONE_SHOT_PROTECTION_COOLDOWN", new Entry
            {
                Title = icon => "One-Shot Protection Cooldown",
                Description = icon => "One-shot protection cannot trigger again until this cooldown ends.\nThe cooldown lasts " +
                    Utility(N(Setting(typeof(OSP), "cooldown", 5f)) + " seconds") + " from the trigger; healing does not reset it.",
                Icon = () => RoR2Content.Buffs.SmallArmorBoost ? RoR2Content.Buffs.SmallArmorBoost.iconSprite : null
            });
            entries.Add("3QSRemoteEmergencyNova", new Entry
            {
                Title = icon => "Healing Nova",
                Description = icon => "Increases movement speed and armour based on Healing Nova charge. Further healing pulses refresh the duration.",
                Icon = () => RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null
            });
            entries.Add("3QSRemoteEmergencyNovaRegen", new Entry
            {
                Title = icon => "Nova Regeneration",
                Description = icon => "Regenerates " + Healing(N(Setting(typeof(Tweaks.Others.RemoteOperation.RemoteEmergencyDrone), "novaRegeneration", 5f)) + "% of maximum health per second") +
                    " after a fully charged Healing Nova.",
                Icon = () => RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null
            });
            entries.Add("3QSRemoteEmergencyOverdrive", new Entry
            {
                Title = icon => "Overdrive",
                Description = icon => "Enhances Healing Beams, Siphoning Salvo and Healing Nova. Enhanced skills have distinct icons while Overdrive is active.",
                Icon = () => RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null
            });
            entries.Add("3QSRemoteEmergencyOverdriveAlly", new Entry
            {
                Title = icon => "Overdrive Support",
                Description = icon => "Increases attack speed by " + Utility(N(Setting(typeof(Tweaks.Others.RemoteOperation.RemoteEmergencyDrone), "overdriveAttackBuff", 30f)) + "%") +
                    ", movement speed by " + Utility(N(Setting(typeof(Tweaks.Others.RemoteOperation.RemoteEmergencyDrone), "overdriveSpeedBuff", 30f)) + "%") +
                    " and reduces incoming damage by " + Utility(N(Setting(typeof(Tweaks.Others.RemoteOperation.RemoteEmergencyDrone), "overdriveDamageReduction", 30f)) + "%") + ".",
                Icon = () => RoR2Content.Buffs.Warbanner ? RoR2Content.Buffs.Warbanner.iconSprite : null
            });
            entries.Add("3QSRemoteLeech", new Entry
            {
                Title = icon => "Leech",
                Description = icon => "Each stack deals periodic damage and restores health to attackers that damage the afflicted target. Additional stacks increase both effects and refresh their durations.",
                Icon = () => RoR2Content.Buffs.Bleeding ? RoR2Content.Buffs.Bleeding.iconSprite : null
            });
            // Bubble Shield already has its own dynamic tooltip in Engineer.cs.
            foreach (BuffDef buff in BuffCatalog.buffDefs)
                if (buff && !buff.iconSprite && entries.TryGetValue(buff.name, out Entry entry)) buff.iconSprite = entry.Icon();
            On.RoR2.UI.BuffIcon.UpdateIcon += UpdateIcon;
        }
        private static string Momentum(BuffIcon icon)
        {
            int perStack = Setting(typeof(Huntress), "critPerStack", 1);
            int maximum = Setting(typeof(Huntress), "maxStacks", 20);
            string text = "Gain " + Utility(perStack + "% critical strike chance") + " per stack (" +
                Utility(N(perStack * Math.Max(0, icon.buffCount)) + "% currently") + "). Build stacks while sprinting.";
            if (maximum >= 0) text += " Maximum: " + Utility(maximum + " stacks") + ".";
            if (Setting(typeof(Huntress), "resetHit", true))
            {
                text += "\nTaking damage removes Momentum.";
                if (Setting(typeof(Huntress), "barrierGate", true)) text += " Hits fully absorbed by barrier preserve it.";
                if (Setting(typeof(Huntress), "shieldGate", false)) text += " Hits fully absorbed by shield preserve it.";
            }
            if (Setting(typeof(Huntress), "resetCrit", false)) text += "\nDealing critical damage removes Momentum.";
            float grace = Setting(typeof(Huntress), "grace", 3f);
            if (grace >= 0f) text += "\nStacks begin decaying " + Utility(N(grace) + " seconds") + " after you stop sprinting.";
            return text;
        }
        private static string Support(BuffIcon icon)
        {
            string text = "Heals for " + Healing(N(Setting(typeof(Captain), "supportHeal", 10f)) + "% maximum health each second") +
                " and grants " + Utility(N(Setting(typeof(Captain), "supportAttack", 30f)) + "% attack speed") +
                " and " + Utility(N(Setting(typeof(Captain), "supportArmour", 25f)) + " armour") + ".";
            float damage = Setting(typeof(Captain), "supportDamage", 0f);
            if (damage != 0f) text += " Also grants " + Utility(N(damage) + "% damage") + ".";
            float duration = Setting(typeof(Captain), "supportDecay", 1.5f);
            if (duration > 0f) text += "\nPersists for " + Utility(N(duration) + " seconds") + " after leaving the beacon.";
            text += Setting(typeof(Captain), "stackSupport", false) ? "\nBonuses from overlapping support beacons can stack." : "\nOverlapping support beacons do not stack.";
            return text;
        }

        private sealed class TooltipState : MonoBehaviour
        {
            internal BuffDef Buff;
            internal TooltipProvider Provider;
            internal bool Applied, Created, WasEnabled, Raycast;
            internal string TitleToken, BodyToken, Title, Body;
            internal UnityEngine.UI.Image Image;
            internal void Restore()
            {
                if (!Applied) return;
                if (Provider)
                {
                    Provider.titleToken = TitleToken; Provider.bodyToken = BodyToken;
                    Provider.overrideTitleText = Title; Provider.overrideBodyText = Body;
                    Provider.enabled = Created ? false : WasEnabled;
                }
                if (Image) Image.raycastTarget = Raycast;
                Applied = false; Buff = null;
            }
            private void OnDisable() => Restore();
        }
        private static void UpdateIcon(On.RoR2.UI.BuffIcon.orig_UpdateIcon orig, BuffIcon icon)
        {
            TooltipState state = icon.GetComponent<TooltipState>();
            if (state && state.Applied && state.Buff != icon.buffDef) state.Restore();
            orig(icon);
            if (!icon.buffDef || !entries.TryGetValue(icon.buffDef.name, out Entry entry)) return;
            if (!icon.buffDef.iconSprite)
            {
                icon.buffDef.iconSprite = entry.Icon();
                if (icon.iconImage) icon.iconImage.sprite = icon.buffDef.iconSprite;
            }
            if (!state) state = icon.gameObject.AddComponent<TooltipState>();
            TooltipProvider tooltip = icon.GetComponent<TooltipProvider>();
            if (!tooltip) { tooltip = icon.gameObject.AddComponent<TooltipProvider>(); state.Created = true; }
            if (!state.Applied)
            {
                state.Provider = tooltip; state.Buff = icon.buffDef;
                state.TitleToken = tooltip.titleToken; state.BodyToken = tooltip.bodyToken;
                state.Title = tooltip.overrideTitleText; state.Body = tooltip.overrideBodyText; state.WasEnabled = tooltip.enabled;
                state.Image = icon.iconImage; state.Raycast = icon.iconImage && icon.iconImage.raycastTarget; state.Applied = true;
            }
            tooltip.titleToken = ""; tooltip.bodyToken = "";
            tooltip.overrideTitleText = entry.Title(icon); tooltip.overrideBodyText = entry.Description(icon);
            tooltip.enabled = true;
            if (icon.iconImage) icon.iconImage.raycastTarget = true;
        }
    }
}
