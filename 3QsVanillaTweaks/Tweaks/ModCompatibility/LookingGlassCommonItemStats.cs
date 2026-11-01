using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.ModCompatibility
{
    // These values are evaluated when LookingGlass builds the inspected item's
    // stats, using its supplied stack count and CharacterMaster, not the local user.
    internal static class LookingGlassCommonItemStats
    {
        sealed class Stats
        {
            internal readonly Type Type;
            internal readonly int Stacks;
            internal readonly CharacterBody Body;
            internal string Text;
            internal Stats(Type type, CharacterMaster master, int stacks, string text)
            {
                Type = type;
                Stacks = stacks;
                Body = master ? master.GetBody() : null;
                Text = text ?? "";
            }

            internal float F(string name)
            {
                var value = A.Field(Type, name)?.GetValue(null);
                if (value is ConfigEntry<float> f)
                    return C.Get(f);
                if (value is ConfigEntry<int> i)
                    return C.Get(i);
                return 0;
            }

            internal bool B(string name) => A.Field(Type, name)?.GetValue(null)is ConfigEntry<bool> b && C.Get(b);
            internal float Total(string first, string additional) => S.Linear(Stacks, F(first), F(additional));
            internal void Add(string name, float value, string suffix = "", string color = "#E5C962")
            {
                if (value != 0)
                    Text = LookingGlassStatRegistry.AddValue(Text, name, value, suffix, color);
            }
        }

        static bool Active(Type type)
        {
            var settings = A.Field(type, "settings")?.GetValue(null) as Tweaks.Items.Common.VanillaCommonConfig;
            if (settings != null)
                return settings.Active;
            return A.Field(type, "enabled")?.GetValue(null)is ConfigEntry<bool> enabled && C.Get(enabled);
        }

        static void Register(string typeName, string itemName, Action<Stats> formatter)
        {
            var type = typeof(LookingGlassCommonItemStats).Assembly.GetType("Tweaks.Items.Common." + typeName, true);
            LookingGlassStatRegistry.RegisterItem(() => ItemCatalog.FindItemIndex(itemName), (master, stacks, text) =>
            {
                var stats = new Stats(type, master, stacks, text);
                formatter(stats);
                return stats.Text;
            }, () => Active(type));
        }

        internal static void Init()
        {
            Register("ArmorPiercingRounds", "BossDamageBonus", s =>
            {
                s.Add("Boss Damage Bonus", s.Total("v0", "v1"), "%");
            });
            Register("BackupMagazine", "SecondarySkillMagazine", s =>
            {
                s.Add("Secondary Stocks", s.Total("stocks", "extra"), "");
            });
            Register("BisonSteak", "FlatHealth", s =>
            {
                s.Add("Maximum Health", s.Total("v0", "v1"), "");
            });
            Register("EnergyDrink", "SprintBonus", s =>
            {
                s.Add("Sprint Speed", s.Total("v0", "v1"), "%");
            });
            Register("PaulsGoatHoof", "Hoof", s =>
            {
                s.Add("Movement Speed", s.Total("v0", "v1"), "%");
            });
            Register("SoldiersSyringe", "Syringe", s =>
            {
                s.Add("Attack Speed", s.Total("v0", "v1"), "%");
            });
            Register("PersonalShieldGenerator", "PersonalShield", s =>
            {
                s.Add("Shield", s.Total("v0", "v1"), "%");
            });
            Register("LensMakersGlasses", "CritGlasses", s =>
            {
                s.Add("Critical Chance", s.Total("v0", "v1"), "%");
            });
            Register("CautiousSlug", "HealWhileSafe", s =>
            {
                s.Add("Regeneration", s.Total("v0", "v1"), " HP/s");
            });
            Register("Mocha", "AttackSpeedAndMoveSpeed", s =>
            {
                s.Add("Attack Speed", s.Total("v0", "v1"), "%");
                s.Add("Movement Speed", s.Total("v2", "v3"), "%");
            });
            Register("FocusCrystal", "NearbyDamageBonus", s =>
            {
                s.Add("Bonus Damage", s.Total("v0", "v1"), "%");
            });
            Register("RepulsionArmorPlate", "ArmorPlate", s =>
            {
                s.Add("Damage Reduction", s.Total("v0", "v1"), "");
            });
            Register("MonsterTooth", "Tooth", s =>
            {
                s.Add("Flat Healing", s.Total("v0", "v1"), "");
                s.Add("Percent Healing", s.Total("v2", "v3"), "%");
            });
            Register("Medkit", "Medkit", s =>
            {
                s.Add("Flat Healing", s.Total("v0", "v1"), "");
                s.Add("Percent Healing", s.Total("v2", "v3"), "%");
            });
            Register("Gasoline", "IgniteOnKill", s =>
            {
                s.Add("Explosion Damage", s.Total("v0", "v1"), "%");
                s.Add("Burn Damage", s.Total("v2", "v3"), "%");
                s.Add("Radius", s.Total("v4", "v5"), "m");
            });
            Register("TriTipDagger", "BleedOnHit", s =>
            {
                s.Add("Bleed Chance", s.Total("chance", "chanceStack"), "%");
                s.Add("Bleed Damage Per Second", s.Total("damage", "damageStack"), "%");
            });
            Register("HikersBoots", "CritAtLowerElevation", s =>
            {
                s.Add("Maximum Buffs", s.Total("cap", "capStack"), "");
                s.Add("Buffs Per Hit", s.Total("gain", "gainStack"), "");
            });
            Register("ElusiveAntlers", "SpeedBoostPickup", s =>
            {
                s.Add("Maximum Buffs", s.Total("cap", "capStack"), "");
            });
            Register("RollOfPennies", "GoldOnHurt", s =>
            {
                s.Add("Base Gold Per Hit", s.Total("gold", "extra"), "");
            });
            Register("WarpedEcho", "DelayedDamage", s =>
            {
                s.Add("Delayed Hits", s.Total("hits", "extra"), "");
            });
            Register("BundleOfFireworks", "Firework", s =>
            {
                s.Add("Fireworks", s.Total("count", "countStack"));
                s.Add("Damage Per Firework", s.Total("damage", "extra"), "%");
            });
            Register("BustlingFungus", "Mushroom", s =>
            {
                s.Add("Healing Per Second", s.Total("heal", "extra"), "%", "#A5EE77");
                s.Add("Radius", s.Total("radius", "radiusStack"), "m", "#95D5E8");
                s.Add("Activation Delay", s.F("delay"), "s", "#95D5E8");
            });
            Register("Crowbar", "Crowbar", s => s.Add("Bonus Damage", s.Total("damage", "extra"), "%"));
            Register("EclipseLite", "BarrierOnCooldown", s => s.Add("Barrier Per Cooldown Second", s.Total("barrier", "extra"), "%", "#A5EE77"));
            Register("OddlyShapedOpal", "OutOfCombatArmor", s =>
            {
                s.Add("Armour", s.Total("armour", "extra"), "", "#A5EE77");
                s.Add("Activation Delay", s.F("delay"), "s", "#95D5E8");
            });
            Register("StickyBomb", "StickyBomb", s =>
            {
                s.Add("Chance", s.Total("chance", "chanceStack"), "%");
                s.Add("Bomb Damage", s.Total("damage", "extra"), "%");
            });
            Register("StunGrenade", "StunChanceOnHit", s =>
            {
                float raw = s.Total("chance", "extra");
                s.Add("Chance", s.B("linear") ? raw : Util.ConvertAmplificationPercentageIntoReductionPercentage(raw), "%");
                s.Add("Stun Duration", s.Total("duration", "durationStack"), "s", "#95D5E8");
                s.Add("Damage", s.Total("damage", "damageStack"), "%");
            });
            Register("TopazBrooch", "BarrierOnKill", s =>
            {
                float flat = s.Total("flat", "flatStack"), fraction = s.Total("percent", "percentStack");
                if (s.Body)
                    s.Add("Barrier On Kill", flat + s.Body.healthComponent.fullCombinedHealth * fraction / 100, "", "#A5EE77");
                else
                {
                    s.Add("Flat Barrier", flat, "", "#A5EE77");
                    s.Add("Maximum Health Barrier", fraction, "%", "#A5EE77");
                }
            });
            Register("TougherTimes", "Bear", s =>
            {
                float raw = s.Total("chance", "extra");
                s.Add("Block Chance", s.B("linear") ? raw : Util.ConvertAmplificationPercentageIntoReductionPercentage(raw), "%", "#95D5E8");
            });
            Register("Warbanner", "WardOnLevel", s =>
            {
                s.Add("Radius", s.Total("radius", "radiusStack"), "m", "#95D5E8");
                s.Add("Movement Speed", s.F("move"), "%", "#95D5E8");
                s.Add("Attack Speed", s.F("attack"), "%");
                s.Add("Damage", s.F("damage"), "%");
                s.Add("Regeneration", s.F("regen"), " HP/s", "#A5EE77");
                s.Add("Armour", s.F("armour"), "", "#A5EE77");
                s.Add("Critical Chance", s.F("crit"), "%");
                s.Add("Buff Duration", s.Total("duration", "durationStack"), "s", "#95D5E8");
            });
            Register("PowerElixir", "HealingPotion", s =>
            {
                s.Add("Healing", s.Total("heal", "healStack"), "%", "#A5EE77");
                s.Add("Barrier", s.Total("barrier", "barrierStack"), "%", "#A5EE77");
                s.Add("Activation Health", s.F("threshold"), "%", "#E58282");
            });
            Register("DelicateWatchBroken", "FragileDamageBonusConsumed", s => s.Add("Bonus Damage", s.Total("damage", "extra"), "%"));
            Register("PowerElixirConsumed", "HealingPotionConsumed", s =>
            {
                s.Add("Healing On Hit Taken", s.Total("heal", "healStack"), "%", "#A5EE77");
                s.Add("Barrier On Hit Taken", s.Total("barrier", "barrierStack"), "%", "#A5EE77");
            });
            Register("BolsteringLantern", "AttackSpeedPerNearbyAllyOrEnemy", s =>
            {
                float perBody = s.Total("attack", "extra"), cap = s.Total("cap", "capStack");
                s.Add("Attack Speed Per Nearby Body", perBody, "%");
                s.Add("Maximum Attack Speed", perBody * cap, "%");
                s.Add("Radius", s.F("radius"), "m", "#95D5E8");
                if (s.Body)
                    s.Add("Current Attack Speed Bonus", perBody * s.Body.GetBuffCount(DLC2Content.Buffs.AttackSpeedPerNearbyAllyOrEnemyBuff), "%");
            });
            Register("ChronicExpansion", "IncreaseDamageOnMultiKill", s =>
            {
                float each = s.Total("damage", "extra");
                s.Add("Damage Per Kill Buff", each, "%");
                s.Add("Maximum Buffs", s.Total("cap", "capStack"));
                s.Add("Maximum Damage Bonus", each * s.Total("cap", "capStack"), "%");
                if (s.Body)
                    s.Add("Current Damage Bonus", each * s.Body.GetBuffCount(DLC2Content.Buffs.IncreaseDamageBuff), "%");
            });
        }
    }
}
