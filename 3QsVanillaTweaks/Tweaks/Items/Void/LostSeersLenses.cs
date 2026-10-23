using System;
using BepInEx.Configuration;
using R2API;
using Tweaks.ModCompatibility;
using System.Globalization;
using UnityEngine.Networking;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using RoR2;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Items.Void
{
    internal static class LostSeersLenses
    {
        private const string Section = "Items - Void - Lost Seers Lenses";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> chancePerStack;
        private static ConfigEntry<float> damagePercent;
        private static ConfigEntry<bool> affectBosses;
        private static ConfigEntry<bool> useCriticalDamageMultiplier;
        private static ConfigEntry<bool> triggerOnCritEffects;
        private static ConfigEntry<bool> affectedByLuck;
        private static ConfigEntry<float> damagePerAdditionalStack;
        private static GameObject voidCritImpactPrefab;
        private static bool searchedVoidCritImpact;

        internal static void Init()
        {
            enabled = ModConfig.Bind(Section, "Enabled", true, "Replace Lost Seer's Lenses' execution with a Void Crit.");
            chancePerStack = ModConfig.Bind(Section, "Void Crit Chance Per Stack (%)", 1f,
                "Chance per lens to trigger the Void Crit, before proc coefficient scaling.");
            damagePercent = ModConfig.Bind(Section, "Void Crit Damage (%)", 1000f,
                "Total damage of a Void Crit, relative to the triggering hit (1000 = 10 times damage).");
            damagePerAdditionalStack = ModConfig.Bind(Section, "Additional Void Crit Damage Per Stack (%)", 0f,
                "Added percentage points of Void Crit damage per additional Lens (default: 0%).");
            affectBosses = ModConfig.BindBool(Section, "Affect Bosses", true,
                "Allow Void Crits against boss enemies.");
            useCriticalDamageMultiplier = ModConfig.BindBool(Section, "Void Crit Uses Critical Damage Multiplier", true,
                "Apply the attacker's critical damage multiplier to a Void Crit, even if the triggering hit was not a normal critical strike.");
            triggerOnCritEffects = ModConfig.BindBool(Section, "Void Crit Triggers On Crit Effects", true,
                "Treat a Void Crit as a critical strike for on-crit effects, even if the triggering hit was not a normal critical strike.");
            affectedByLuck = ModConfig.BindBool(Section, "Void Crit Affected By Luck", true,
                "Use the attacker's luck for Void Crit chance rolls (including positive and negative luck).");
            IL.RoR2.HealthComponent.TakeDamageProcess += PatchDamage;
            IL.EntityStates.Huntress.HuntressWeapon.FireFlurrySeekingArrow.OnEnter += PatchFlurry;
            Debug.Log("[3QsVanillaTweaks] Lost Seer's Lenses: registered damage IL hook.");
            UpdateDescriptions();
            ModConfig.EffectiveSettingsChanged += UpdateDescriptions;
            LookingGlassStatRegistry.Register(
                () => DLC1Content.Items.CritGlassesVoid != null ? DLC1Content.Items.CritGlassesVoid.itemIndex : ItemIndex.None,
                "Kill chance", FormatLookingGlass);
        }


        private static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static float VoidCritDamage(int stacks)
        {
            return Mathf.Max(0f, ModConfig.Get(damagePercent) +
                Mathf.Max(0, stacks - 1) * ModConfig.Get(damagePerAdditionalStack));
        }

        private static string FormatLookingGlass(int stacks, string text)
        {
            if (!ModConfig.Get(enabled)) return text;

            // LookingGlass uses "Kill chance" for Lost Seer's Lenses, not "Execute".
            // Strip its entire original stat line before displaying the new values.
            text = Regex.Replace(text,
                @"(?im)^.*(?:kill\s*chance|instant[ -]?kill|execution|execute).*?(?:\r?\n|$)", "");
            float chance = Mathf.Clamp(ModConfig.Get(chancePerStack) * stacks, 0f, 100f);
            text = LookingGlassStatRegistry.AddValue(text.TrimEnd() + "\n\n", "Void Crit Chance", chance, "%", "#C968FF");
            text = LookingGlassStatRegistry.AddValue(text, "Void Crit Damage", VoidCritDamage(stacks), "%", "#E5C962");
            return text;
        }

        private static void UpdateDescriptions()
        {
            if (!ModConfig.Get(enabled)) return;

            string chance = Number(Mathf.Max(0f, ModConfig.Get(chancePerStack)));
            string damage = Number(Mathf.Max(0f, ModConfig.Get(damagePercent)));
            string additional = Number(ModConfig.Get(damagePerAdditionalStack));
            string bossRestriction = ModConfig.Get(affectBosses) ? "" : " (Doesn't affect bosses.)";
            string critInteraction = ModConfig.Get(useCriticalDamageMultiplier) && ModConfig.Get(triggerOnCritEffects)
                ? " Void Crits scale with critical damage and trigger on-crit effects."
                : ModConfig.Get(useCriticalDamageMultiplier)
                    ? " Void Crits scale with critical damage."
                    : ModConfig.Get(triggerOnCritEffects)
                        ? " Void Crits trigger on-crit effects."
                        : "";

            string luckInteraction = ModConfig.Get(affectedByLuck) ? " Void Crit chance is affected by luck." : "";

            LanguageAPI.Add("ITEM_CRITGLASSESVOID_PICKUP", "Gain a small chance to deal massive damage. <style=cIsVoid>Corrupts all Lens-Maker's Glasses.</style>");
            LanguageAPI.Add("ITEM_CRITGLASSESVOID_DESC",
                "<style=cIsDamage>" + chance + "% chance</style> " +
                "<style=cStack>(+" + chance + "% per stack)</style> to deal a " +
                "<style=cIsDamage>Void Crit</style> on hit for " +
                "<style=cIsDamage>" + damage + "% damage</style>" +
                (ModConfig.Get(damagePerAdditionalStack) == 0f ? "" :
                    " <style=cStack>(" + (ModConfig.Get(damagePerAdditionalStack) > 0f ? "+" : "") + additional + "% per stack)</style>") +
                "." + bossRestriction + " <style=cIsVoid>Corrupts all Lens-Maker's Glasses.</style>");
        }

        // Flurry chooses its three/six-arrow mode before projectiles hit anything.
        // A separate pre-fire roll is therefore necessary: an on-hit Void Crit
        // cannot retroactively change the number of arrows already launched.
        private static void PatchFlurry(ILContext il)
        {
            var cursor = new ILCursor(il);
            bool found = cursor.TryGotoNext(MoveType.After,
                instruction => instruction.OpCode == OpCodes.Ldfld &&
                    instruction.Operand is FieldReference field &&
                    field.Name == "isCrit" &&
                    field.DeclaringType.FullName == "EntityStates.Huntress.HuntressWeapon.FireSeekingArrow");

            if (!found)
            {
                Debug.LogError("[3QsVanillaTweaks] Lost Seer's Lenses: could not locate Flurry's critical arrow-count check.");
                return;
            }

            // Stack at this point: vanilla isCrit boolean. OR in the extra
            // pre-fire roll without altering isCrit or normal critical damage.
            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<bool, EntityStates.Huntress.HuntressWeapon.FireFlurrySeekingArrow, bool>>(ShouldFireCritFlurry);
            Debug.Log("[3QsVanillaTweaks] Lost Seer's Lenses: Flurry Void Crit arrow-count hook installed.");
        }

        private static bool ShouldFireCritFlurry(bool normalCrit,
            EntityStates.Huntress.HuntressWeapon.FireFlurrySeekingArrow state)
        {
            if (normalCrit || !ModConfig.Get(enabled) || !ModConfig.Get(triggerOnCritEffects) || state == null)
                return normalCrit;

            CharacterBody body = state.characterBody;
            if (!body || !body.inventory || DLC1Content.Items.CritGlassesVoid == null)
                return normalCrit;

            int stacks = body.inventory.GetItemCountEffective(DLC1Content.Items.CritGlassesVoid);
            if (stacks <= 0) return normalCrit;

            float chance = Mathf.Clamp(ModConfig.Get(chancePerStack) * stacks, 0f, 100f);
            return Util.CheckRoll(chance, ModConfig.Get(affectedByLuck) ? body.master : null);
        }

        private static void PatchDamage(ILContext il)
        {
            // Find the exact vanilla Lost Seer's Lenses count read. Other item
            // GetItemCountEffective calls occur throughout this very large method.
            var cursor = new ILCursor(il);
            bool found = cursor.TryGotoNext(MoveType.After,
                instruction => instruction.OpCode == OpCodes.Ldsfld &&
                    instruction.Operand is FieldReference field &&
                    field.Name == "CritGlassesVoid" &&
                    field.DeclaringType.FullName.Contains("DLC1Content/Items"),
                instruction => instruction.OpCode == OpCodes.Callvirt &&
                    instruction.Operand is MethodReference method &&
                    method.Name == "GetItemCountEffective" &&
                    method.DeclaringType.FullName == "RoR2.Inventory");

            if (!found)
            {
                Debug.LogError("[3QsVanillaTweaks] Lost Seer's Lenses: could not locate vanilla execution roll. No damage patch installed.");
                return;
            }

            // The stack contains the vanilla lens count here. Replace with zero
            // while enabled so the vanilla instant-execute check cannot fire.
            cursor.EmitDelegate<Func<int, int>>(count => ModConfig.Get(enabled) ? 0 : count);

            // Only apply Void Crits when the original execute has been suppressed.
            // Apply to DamageInfo before the game's damage calculations begin.
            var entry = new ILCursor(il);
            entry.Emit(OpCodes.Ldarg_0);
            entry.Emit(OpCodes.Ldarg_1);
            entry.EmitDelegate<Action<HealthComponent, DamageInfo>>(ApplyVoidCrit);
            Debug.Log("[3QsVanillaTweaks] Lost Seer's Lenses: vanilla execution disabled; Void Crit damage hook installed.");
        }

        private static void ApplyVoidCrit(HealthComponent victim, DamageInfo damage)
        {
            if (!NetworkServer.active || !ModConfig.Get(enabled) || victim == null || damage == null || damage.rejected || !damage.attacker)
                return;

            CharacterBody attackerBody = damage.attacker.GetComponent<CharacterBody>();
            if (!attackerBody || !attackerBody.inventory || !victim.body) return;
            if (!ModConfig.Get(affectBosses) && victim.body.isBoss) return;

            int stacks = attackerBody.inventory.GetItemCountEffective(DLC1Content.Items.CritGlassesVoid);
            if (stacks <= 0 || damage.procCoefficient <= 0f) return;

            float chance = Mathf.Clamp(ModConfig.Get(chancePerStack) * stacks * damage.procCoefficient, 0f, 100f);
            // Passing a null master uses a luck-neutral roll; providing the
            // attacker master preserves RoR2's normal positive/negative luck rules.
            if (Util.CheckRoll(chance, ModConfig.Get(affectedByLuck) ? attackerBody.master : null))
            {
                damage.damage *= VoidCritDamage(stacks) / 100f;

                // HealthComponent.TakeDamageProcess applies the ordinary critical
                // multiplier later when DamageInfo.crit is true. Set that flag to
                // reuse the game's on-crit behaviour. The two toggles can be
                // independent without double-applying the crit multiplier.
                bool alreadyCrit = damage.crit;
                bool critDamageEnabled = ModConfig.Get(useCriticalDamageMultiplier);
                bool critEffectsEnabled = ModConfig.Get(triggerOnCritEffects);
                if (!alreadyCrit)
                {
                    float critMultiplier = Mathf.Max(0.01f, attackerBody.critMultiplier);
                    if (critEffectsEnabled)
                    {
                        damage.crit = true;
                        // On-crit effects only: cancel the multiplier that the
                        // game will apply later because the crit flag is set.
                        if (!critDamageEnabled)
                            damage.damage /= critMultiplier;
                    }
                    else if (critDamageEnabled)
                    {
                        // Critical damage only: leave the crit flag untouched so
                        // this Void Crit does not activate critical-hit effects.
                        damage.damage *= critMultiplier;
                    }
                }
                // An already-critical attack has been flagged by the game;
                // never suppress its normal critical effects or damage.
                SpawnVoidCritImpact(damage.position);
            }
        }

        // Use an ordinary Void impact visual, NOT the Lost Seer's Lenses
        // execution prefab. The latter generates misleading execute numbers.
        // EffectManager transmits this catalogued effect from the server to
        // clients, while the real damage remains displayed by the game.
        private static void SpawnVoidCritImpact(Vector3 position)
        {
            if (!searchedVoidCritImpact)
            {
                searchedVoidCritImpact = true;
                for (int i = 0; i < EffectCatalog.effectCount; i++)
                {
                    EffectDef effect = EffectCatalog.GetEffectDef((EffectIndex)i);
                    if (effect != null && effect.prefab != null &&
                        effect.prefab.name == "VoidSurvivorBeamImpact")
                    {
                        voidCritImpactPrefab = effect.prefab;
                        break;
                    }
                }

                if (voidCritImpactPrefab == null)
                    Debug.LogWarning("[3QsVanillaTweaks] Lost Seer's Lenses: VoidSurvivorBeamImpact effect not found; Void Crit damage will still work.");
            }

            if (voidCritImpactPrefab == null) return;

            EffectManager.SpawnEffect(voidCritImpactPrefab,
                new EffectData { origin = position, scale = 0.75f }, true);
        }
    }
}
