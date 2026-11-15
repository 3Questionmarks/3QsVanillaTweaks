using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using R2API;
using RoR2;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class ShrineOfTheWoods
    {
        private const string Section = "Interactables - Shrine of the Woods";
        private const string Token = "3QS_WOODS_LOADOUT_";
        private enum Kind { Passive, Primary, Secondary, Utility, Special }

        private static ConfigEntry<bool> enabled;
        internal static bool LoadoutVariantsEnabled => enabled != null && C.Get(enabled);
        internal static int VariantCount => Names.Length;
        private static ConfigEntry<string> acolyteSpecial, multSecondary, captainSpecial;
        private static readonly Dictionary<NetworkUser, Dictionary<string, SkillDef>> Choices =
            new Dictionary<NetworkUser, Dictionary<string, SkillDef>>();
        private static readonly System.Reflection.FieldInfo AuthorityField =
            AccessTools.Field(typeof(SkillLocator), "hasEffectiveAuthority");
        private static readonly string[] Names = { "Passive", "Primary", "Secondary", "Utility", "Special" };
        private static readonly string[] ShrineNames =
        {
            "Shrine of Instinct", "Shrine of the First Strike", "Shrine of the Echo",
            "Shrine of Passage", "Shrine of Fate"
        };
        private static readonly Color[] Colors =
        {
            new Color(.65f, .43f, .9f), new Color(.25f, .65f, 1f), new Color(1f, .66f, .24f),
            new Color(.39f, .88f, .55f), new Color(1f, .37f, .5f)
        };

        private sealed class Marker : MonoBehaviour
        {
            internal Kind Type;
            internal Renderer[] Renderers;
            internal MaterialPropertyBlock[] Original;
            internal float ThirdHealPoints, ThirdHealFraction;
            internal bool HasThirdHeal;

            private void OnDestroy()
            {
                if (Renderers == null) return;
                for (int i = 0; i < Renderers.Length; ++i)
                    if (Renderers[i]) Renderers[i].SetPropertyBlock(Original[i]);
            }
        }

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enable Loadout Shrines", true,
                "Shrine of the Woods also changes one loadout slot. Each shrine colour identifies the slot; healing remains available.");
            acolyteSpecial = C.Bind(Section, "Acolyte Special", "Litany",
                "Litany, Special, or Both. Both changes both slots on one purchase.");
            multSecondary = C.Bind(Section, "MUL-T Secondary", "Both",
                "Second Primary, Both, or Secondary. Both changes the second primary and a separately added secondary, if present.");
            captainSpecial = C.Bind(Section, "Captain Special", "Both",
                "Both, First, or Second. Each selected beacon slot changes independently.");

            for (int i = 0; i < Names.Length; ++i)
            {
                LanguageAPI.Add(Token + Names[i].ToUpperInvariant() + "_NAME", ShrineNames[i]);
                LanguageAPI.Add(Token + Names[i].ToUpperInvariant() + "_CONTEXT", "Change your " + Names[i].ToLowerInvariant() + " skill");
            }

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.WoodsLoadout");
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "Start"),
                postfix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(AssignKind)));
            harmony.Patch(AccessTools.Method(typeof(ShrineHealingBehavior), "AddShrineStack"),
                postfix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(AfterPurchase)));
            harmony.Patch(AccessTools.Method(typeof(SkillLocator), "ApplyLoadoutServer"),
                postfix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(Restore)));
            harmony.Patch(AccessTools.Method(typeof(CharacterBody), "Start"),
                postfix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(RestoreOnSpawn)));
            harmony.Patch(AccessTools.Method(typeof(SkillLocator), "OnDeserialize"),
                prefix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(BeforeDeserialize)),
                postfix: new HarmonyMethod(typeof(ShrineOfTheWoods), nameof(AfterDeserialize)));
            Run.onRunDestroyGlobal += _ => Choices.Clear();
        }

        private static void BeforeDeserialize(SkillLocator __instance, out bool? __state)
        {
            __state = null;
            if (!C.Get(enabled) || NetworkServer.active || AuthorityField == null) return;
            __state = (bool)AuthorityField.GetValue(__instance);
            // Server-selected skill variants must reach the player's own authoritative body too.
            AuthorityField.SetValue(__instance, false);
        }

        private static void AfterDeserialize(SkillLocator __instance, bool? __state)
        {
            if (__state.HasValue) AuthorityField.SetValue(__instance, __state.Value);
        }

        // The scene and quantized world position are shared by all clients, without a new network component.
        private static Kind Choose(Transform transform)
        {
            Vector3 p = transform.position;
            int hash = 17;
            unchecked
            {
                hash = hash * 31 + Mathf.RoundToInt(p.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(p.y * 10f);
                hash = hash * 31 + Mathf.RoundToInt(p.z * 10f);
            }
            return (Kind)((hash & int.MaxValue) % Names.Length);
        }

        private static void AssignKind(PurchaseInteraction __instance)
        {
            if (!C.Get(enabled) || !__instance) return;
            ShrineHealingBehavior shrine = __instance.GetComponent<ShrineHealingBehavior>();
            if (!shrine || shrine.GetComponent<Marker>()) return;
            Marker marker = shrine.gameObject.AddComponent<Marker>();
            marker.Type = Choose(__instance.transform);
            for (int i = 0; i < Names.Length; i++)
                if (__instance.displayNameToken == Token + Names[i].ToUpperInvariant() + "_NAME")
                {
                    marker.Type = (Kind)i;
                    break;
                }
            shrine.costMultiplierPerPurchase = 1f;
            shrine.maxPurchaseCount = int.MaxValue;
            marker.Renderers = __instance.GetComponentsInChildren<Renderer>(true);
            marker.Original = new MaterialPropertyBlock[marker.Renderers.Length];
            for (int i = 0; i < marker.Renderers.Length; ++i)
            {
                Renderer renderer = marker.Renderers[i];
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original);
                marker.Original[i] = original;
                var tint = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(tint);
                tint.SetColor("_Color", Colors[(int)marker.Type]);
                tint.SetColor("_TintColor", Colors[(int)marker.Type]);
                renderer.SetPropertyBlock(tint);
            }
            if (NetworkServer.active)
                SetVariantTokens(__instance, (int)marker.Type);
        }

        internal static void SetVariantTokens(PurchaseInteraction purchase, int variant)
        {
            string key = Names[variant].ToUpperInvariant();
            purchase.NetworkdisplayNameToken = Token + key + "_NAME";
            purchase.NetworkcontextToken = Token + key + "_CONTEXT";
        }

        private static void AfterPurchase(ShrineHealingBehavior __instance, Interactor activator)
        {
            if (!NetworkServer.active || !C.Get(enabled) || !__instance || !activator) return;
            Marker marker = __instance.GetComponent<Marker>();
            if (!marker) return;
            // AddShrineStack has already enabled the healing ward; keep it active.
            if (__instance.healingWard && __instance.purchaseCount >= 3)
            {
                HealingWard ward = __instance.healingWard;
                if (!marker.HasThirdHeal)
                {
                    marker.ThirdHealPoints = ward.healPoints;
                    marker.ThirdHealFraction = ward.healFraction;
                    marker.HasThirdHeal = true;
                }
                if (__instance.purchaseCount > 3)
                {
                    ward.Networkradius = __instance.baseRadius + 2f * __instance.radiusBonusPerPurchase;
                    ward.healPoints = Mathf.Min(ward.healPoints, marker.ThirdHealPoints);
                    ward.healFraction = Mathf.Min(ward.healFraction, marker.ThirdHealFraction);
                }
            }
            CharacterBody body = activator.GetComponent<CharacterBody>();
            SkillLocator locator = body ? body.skillLocator : null;
            if (!locator) return;
            AcolyteFormController form = body.GetComponent<AcolyteFormController>();
            if (form && form.inTransformation) return;
            NetworkUser user = User(body);
            if (!user) return;
            List<GenericSkill> slots = Resolve(locator, marker.Type, body);
            foreach (GenericSkill slot in slots)
            {
                SkillDef next = Next(slot, user);
                if (!next) continue;
                Apply(locator, slot, next);
                if (!Choices.TryGetValue(user, out var saved)) Choices[user] = saved = new Dictionary<string, SkillDef>();
                saved[Key(body, locator, slot)] = next;
            }
        }

        private static NetworkUser User(CharacterBody body)
        {
            var player = body && body.master ? body.master.playerCharacterMasterController : null;
            return player ? player.networkUser : null;
        }

        private static string Key(CharacterBody body, SkillLocator locator, GenericSkill slot) =>
            body.bodyIndex + ":" + locator.GetSkillSlotIndex(slot);

        private static void Restore(SkillLocator __instance)
        {
            if (!NetworkServer.active || !C.Get(enabled) || !__instance) return;
            CharacterBody body = __instance.GetComponent<CharacterBody>();
            NetworkUser user = User(body);
            if (!user || !Choices.TryGetValue(user, out var saved)) return;
            foreach (GenericSkill slot in __instance.AllSkills)
                if (slot && saved.TryGetValue(Key(body, __instance, slot), out SkillDef chosen) &&
                    Contains(slot, chosen)) Apply(__instance, slot, chosen);
        }

        private static void RestoreOnSpawn(CharacterBody __instance)
        {
            if (__instance && __instance.skillLocator) Restore(__instance.skillLocator);
        }

        private static bool Contains(GenericSkill slot, SkillDef skill)
        {
            if (!slot.skillFamily || slot.skillFamily.variants == null) return false;
            foreach (var variant in slot.skillFamily.variants) if (variant.skillDef == skill) return true;
            return false;
        }

        private static void Apply(SkillLocator locator, GenericSkill slot, SkillDef next)
        {
            int oldStock = slot.stock;
            float oldRecharge = slot.rechargeStopwatch;
            slot.SetBaseSkill(next);
            slot.stock = Mathf.Min(oldStock, slot.maxStock);
            slot.rechargeStopwatch = oldRecharge;
            int index = locator.GetSkillSlotIndex(slot);
            if (index >= 0 && index < 32) locator.SetDirtyBit(1u << index);
        }

        private static SkillDef Next(GenericSkill slot, NetworkUser user)
        {
            if (!slot || !slot.skillFamily || slot.skillFamily.variants == null) return null;
            var variants = slot.skillFamily.variants;
            if (variants.Length < 2) return null;
            int current = Array.FindIndex(variants, v => v.skillDef == slot.baseSkill);
            for (int offset = 1; offset < variants.Length; ++offset)
            {
                var variant = variants[(current + offset + variants.Length) % variants.Length];
                if (variant.skillDef && (variant.unlockableDef == null || user.unlockables.Contains(variant.unlockableDef)))
                    return variant.skillDef;
            }
            return null;
        }

        private static bool Has(GenericSkill skill, string word) => skill &&
            ((skill.skillName ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 ||
             (skill.loadoutTitleToken ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 ||
             (skill.skillFamily && (SkillCatalog.GetSkillFamilyName(skill.skillFamily.catalogIndex) ?? "")
                 .IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0));

        private static List<GenericSkill> Resolve(SkillLocator locator, Kind type, CharacterBody body)
        {
            var result = new List<GenericSkill>();
            GenericSkill[] all = locator.AllSkills;
            if (all == null) return result;
            void Add(GenericSkill skill)
            {
                if (skill && skill.skillFamily && skill.skillFamily.variants != null &&
                    skill.skillFamily.variants.Length > 1 && !result.Contains(skill)) result.Add(skill);
            }

            switch (type)
            {
                case Kind.Passive:
                    foreach (GenericSkill slot in all)
                        if (slot != locator.primary && slot != locator.secondary && slot != locator.utility &&
                            slot != locator.special && Has(slot, "Passive")) Add(slot);
                    break;
                case Kind.Primary: Add(locator.primary); break;
                case Kind.Secondary:
                    bool mult = body.baseNameToken != null && body.baseNameToken.IndexOf("TOOLBOT", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (mult)
                    {
                        string choice = C.Get(multSecondary);
                        if (!choice.Equals("Secondary", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (GenericSkill slot in all)
                                if (slot != locator.primary && Has(slot, "Primary")) { Add(slot); break; }
                        }
                        if (!choice.Equals("Second Primary", StringComparison.OrdinalIgnoreCase)) Add(locator.secondary);
                    }
                    else Add(locator.secondary);
                    break;
                case Kind.Utility: Add(locator.utility); break;
                case Kind.Special:
                    bool acolyte = body.baseNameToken != null && body.baseNameToken.IndexOf("ACOLYTE", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool captain = body.baseNameToken != null && body.baseNameToken.IndexOf("CAPTAIN", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (acolyte)
                    {
                        string choice = C.Get(acolyteSpecial);
                        if (!choice.Equals("Special", StringComparison.OrdinalIgnoreCase))
                            foreach (GenericSkill slot in all) if (Has(slot, "Litany")) Add(slot);
                        if (!choice.Equals("Litany", StringComparison.OrdinalIgnoreCase)) Add(locator.special);
                    }
                    else if (captain)
                    {
                        string choice = C.Get(captainSpecial);
                        if (!choice.Equals("Second", StringComparison.OrdinalIgnoreCase)) Add(locator.special);
                        if (!choice.Equals("First", StringComparison.OrdinalIgnoreCase))
                            foreach (GenericSkill slot in all)
                                if (slot != locator.special && (Has(slot, "Beacon") || Has(slot, "Special"))) { Add(slot); break; }
                    }
                    else Add(locator.special);
                    break;
            }
            return result;
        }
    }
}
