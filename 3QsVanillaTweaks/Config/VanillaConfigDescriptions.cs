using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Config
{
    // Description metadata only. Never use these baselines as configured values.
    internal static class VanillaConfigDescriptions
    {
        private static readonly Dictionary<string, object> assets = new Dictionary<string, object>();

        internal static string Add(string section, string key, string description)
        {
            description = description ?? "";
            if (description.IndexOf("vanilla:", StringComparison.OrdinalIgnoreCase) >= 0) return description;
            string baseline;
            try { baseline = Baseline(section ?? "", key ?? ""); }
            catch (Exception e)
            {
                ThreeQsVanillaTweaks.Log.Warning("Could not add vanilla reference for " + section + " / " + key + ": " + e.Message);
                return description;
            }
            return baseline == null ? description : description.TrimEnd() + " Vanilla: " + baseline + ".";
        }

        internal static ConfigDescription Add(string section, string key, ConfigDescription description)
        {
            if (description == null) return new ConfigDescription(Add(section, key, ""));
            string text = Add(section, key, description.Description);
            return text == description.Description ? description :
                new ConfigDescription(text, description.AcceptableValues, description.Tags);
        }

        private static string Baseline(string section, string key)
        {
            // Debug controls, module switches and mod integrations have no vanilla equivalent.
            if (section.StartsWith("Debug", StringComparison.OrdinalIgnoreCase) ||
                section.StartsWith("Mod Compatibility", StringComparison.Ordinal) || section == "01 Modules" || section == "00 General") return null;

            if (section.EndsWith(" - Laser Scope", StringComparison.Ordinal))
            {
                switch (key)
                {
                    case "Enabled": return "False";
                    case "Base Crit Chance": case "Crit Chance Per Stack": return "0";
                    case "Base Crit Damage (%)": case "Crit Damage Per Stack (%)": return "100"; ;
                }
            }
            if (section.EndsWith(" - Egocentrism", StringComparison.Ordinal))
            {
                switch (key)
                {
                    case "Consumption Interval": return EgoInterval();
                    case "Interval Change Per Stack": return "0 seconds";
                    case "Orb Base Damage (%)": return "360";
                    case "Orb Damage Per Stack (%)": return "0";
                    case "Orb Spawn Time": return "3";
                    case "Pause Consumption When Run Timer Paused": return "False";
                    case "Consumption Priority": return "Other Items";
                }
                if (key.StartsWith("Allow ", StringComparison.Ordinal))
                    return "True";
            }
            if (section.EndsWith(" - Gesture of the Drowned", StringComparison.Ordinal))
            {
                switch (key)
                {
                    case "Automatically Use Equipment": return "true";
                    case "First Stack Cooldown Multiplier": return "0.5";
                    case "Additional Stack Cooldown Multiplier": return "0.85";
                    case "Pause Cooldowns in Bazaar Between Time": case "Pause Cooldowns in Computational Exchange": return "False";
                }
            }
            if (section.EndsWith(" - Regenerating Scrap", StringComparison.Ordinal))
            {
                if (key == "Uncommon Printers") return "true";
                if (key == "Common Printers" || key == "Legendary Printers" || key == "Boss Printers") return "false";
            }
            if ((section.EndsWith(" - Dios Best Friend", StringComparison.Ordinal) ||
                 section.EndsWith(" - Pluripotent Larva", StringComparison.Ordinal)) && key == "Enabled")
                return "False";
            if (section.EndsWith(" - Lost Seers Lenses", StringComparison.Ordinal))
            {
                switch (key)
                {
                    case "Enabled": return "False";
                    case "Affect Bosses": return "False";
                    case "Void Crit Affected By Luck": return "False";
                }
            }

            if (section == "Interactables - Shrine of Chance")
            {
                if (key == "Improve Rewards On Failure") return "False";
                if (key == "Reset Chances On Success") return "False";
                return ChanceValue(key);
            }
            if (section == "Interactables - Shrine of Blood")
            {
                if (key == "Gold Per Health Lost") return "0.5";
                if (key == "Small Chest Bonus") return "0";
            }
            if (section == "Interactables - Shrine of Combat")
            {
                if (key == "Enabled") return "False";
            }
            if (section == "Interactables - Void Seeds")
            {
                if (key == "Base Limit") return VoidSeedCap();
                if (key == "Additional Seeds Per Loop") return "0";
                if (key == "Void Items Per Additional Seed") return "0";
            }

            if (section.StartsWith("Other - Lunar Coins", StringComparison.Ordinal) || section == "Others - Lunar Coins")
            {
                if (key == "Initial Drop Chance (%)") return "0.5%";
                if (key == "Share Coin Pickups") return "False";
            }
            if (section == "Others - Pickup Recovery")
            {
                if (key == "Enabled" || key.StartsWith("Recover ", StringComparison.Ordinal)) return "False";
                return null;
            }
            if (section == "Artifacts - Fleeting Moon")
            {
                switch (key)
                {
                    case "Lunar Coin Drop Rate Multiplier": return "1x";
                }
            }

            if (section.StartsWith("Bazaar", StringComparison.Ordinal))
            {
                if (section.Contains("Newt"))
                {
                    if (key == "Newt Never Kicks Players Out") return "False";
                    if (key == "Award Bazaar Karma When Newt Dies" || key == "Karma Once Per Bazaar Visit") return "False";
                    if (key == "Karma Once Per Bazaar Visit") return "True";
                    return NewtValue(key);
                }
                if (key == "Accept One Legendary Item") return "False";
                if (key == "Refresh Purchased Lunar Slots") return "False";
                if (key == "Reroll Price Formula") return "x * 2; prices start at 1 and double after each reroll";
                if (key == "Spawn With Command") return "False";
                return null; // Coordinates, weights and replacement rolls for added objects have no vanilla value.
            }

            if (!section.StartsWith("Stages", StringComparison.Ordinal)) return null;
            if (section == "Stages - Previous Run" && key == "Avoid Previous Run Stages") return "False";
            if (section.Contains("Variant Availability")) return "False";
            if (section.Contains("Primordial Teleporter")) return "False";
            if (section.Contains("Abandoned Aqueduct") && section.Contains("Buttons"))
            {
                if (key == "Button Count" || key == "Buttons Required") return "2";
                if (key == "Buttons Stay Pressed") return "False";

            }
            if (section.Contains("Rallypoint Delta") && section.Contains("Timed Chest"))
            {
                if (key == "Stage Arrival Timer") return "False.";
                if (key == "Replace Reward After Challenge") return "False";
                if (key == "Additional Timed Chests") return "False";
            }
            if (section.Contains("Guaranteed Legendary Chests")) return "False";            
            if (key == "Spawn With Command") return "False";
            return null;
        }

        private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        // Asset-defined baselines come from the installed game, rather than copying a mod default.
        // Keep these readers separate so unrelated config entries never load game assets.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static T Asset<T>(string address) where T : UnityEngine.Object
        {
            if (assets.TryGetValue(address, out object cached)) return cached as T;
            T asset = null;
            try { asset = Addressables.LoadAssetAsync<T>(address).WaitForCompletion(); }
            catch (Exception e) { ThreeQsVanillaTweaks.Log.Warning("Could not read vanilla config reference " + address + ": " + e.Message); }
            assets[address] = asset;
            return asset;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string EgoInterval()
        {
            FieldInfo field = typeof(LunarSunBehavior).GetField("secondsPerTransform", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? "the game's fixed consumption interval" : Number(Convert.ToDouble(field.GetValue(null))) + " seconds";
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ChanceValue(string key)
        {
            if (key != "Maximum Payouts" && key != "Cost Multiplier Per Purchase" && !key.EndsWith(" Starting Chance (%)", StringComparison.Ordinal)) return null;
            SpawnCard card = Asset<SpawnCard>("RoR2/Base/ShrineChance/iscShrineChance.asset");
            ShrineChanceBehavior shrine = card && card.prefab ? card.prefab.GetComponent<ShrineChanceBehavior>() : null;
            if (!shrine) return "the original Shrine of Chance prefab value";
            if (key == "Maximum Payouts") return Number(shrine.maxPurchaseCount);
            if (key == "Cost Multiplier Per Purchase") return Number(shrine.costMultiplierPerPurchase) + "x";
            double failure = shrine.failureChance;
            if (key == "Nothing Starting Chance (%)") return Number(failure * 100d) + "%";
            string[] fields = { "equipmentWeight", "tier1Weight", "tier2Weight", "tier3Weight" };
            string[] names = { "Equipment", "Common", "Uncommon", "Legendary" };
            double[] weights = new double[4];
            double total = 0d;
            for (int i = 0; i < fields.Length; ++i)
            {
                object source = shrine.dropTable ? (object)shrine.dropTable : shrine;
                FieldInfo field = source.GetType().GetField(fields[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null) return "the original Shrine of Chance reward table";
                total += weights[i] = Math.Max(0d, Convert.ToDouble(field.GetValue(source)));
            }
            if (total <= 0d) return "the original Shrine of Chance reward table";
            for (int i = 0; i < names.Length; ++i)
                if (key == names[i] + " Starting Chance (%)") return Number(100d * (1d - failure) * weights[i] / total) + "%";
            return null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string VoidSeedCap()
        {
            var card = Asset<InteractableSpawnCard>("RoR2/DLC1/VoidCamp/iscVoidCamp.asset");
            return !card ? "the original Void Seed spawn-card limit" : card.maxSpawnsPerStage < 0 ? "no spawn-card cap" : Number(card.maxSpawnsPerStage);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string NewtValue(string key)
        {
            if (key != "Base Health" && key != "Health Per Level" && key != "Armor" && key != "Max Health Regeneration Per Second (%)") return null;
            if (!BodyCatalog.availability.available) return null;
            GameObject prefab = BodyCatalog.FindBodyPrefab("ShopkeeperBody");
            CharacterBody body = prefab ? prefab.GetComponent<CharacterBody>() : null;
            if (!body) return null;
            if (key == "Base Health") return Number(body.baseMaxHealth);
            if (key == "Health Per Level") return Number(body.levelMaxHealth);
            if (key == "Armor") return Number(body.baseArmor);
            return Number(body.baseMaxHealth > 0f ? body.baseRegen / body.baseMaxHealth * 100d : 0d) + "% at level 1 (" + Number(body.baseRegen) + " health per second)";
        }
    }
}
