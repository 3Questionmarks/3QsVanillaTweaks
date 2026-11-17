using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;

namespace Tweaks.Enemies
{
    // Extra run difficulty: faster time scaling, and enemy health and damage bonuses.
    internal static class EnemyDifficulty
    {
        private const string Section = "Enemies - Difficulty";
        private static ConfigEntry<bool> enabled, logBosses;
        private static ConfigEntry<float> timeScaling;
        private static ConfigEntry<int> healthBonus, damageBonus, bossHealth, bossDamage, majorHealth, majorDamage;
        private static ConfigEntry<string> majorBodies;
        private static string[] majorParsed = Array.Empty<string>();
        private static string majorSource;
        private static readonly List<ILHook> hooks = new List<ILHook>();
        private sealed class Boosted { }
        private static readonly ConditionalWeakTable<CharacterMaster, Boosted> boosted = new ConditionalWeakTable<CharacterMaster, Boosted>();

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enabled", true, "Enable the difficulty changes below. Vanilla: false.");
            timeScaling = C.Bind(Section, "Time Scaling Multiplier", 1.2f, new ConfigDescription(
                "Multiplies how quickly difficulty rises over time, on top of the selected difficulty (Drizzle 1, Rainstorm 2, Monsoon 3). Stage scaling is unchanged. Vanilla: 1.",
                new AcceptableValueRange<float>(0f, 10f)));
            healthBonus = Percent("Enemy Health Bonus (%)", 30, "Extra maximum health for regular enemies.");
            damageBonus = Percent("Enemy Damage Bonus (%)", 10, "Extra damage for regular enemies.");
            bossHealth = Percent("Boss Health Bonus (%)", 40, "Extra maximum health for bosses: teleporter bosses and other champion-class enemies such as Stone Titans.");
            bossDamage = Percent("Boss Damage Bonus (%)", 10, "Extra damage for bosses.");
            majorHealth = Percent("Major Boss Health Bonus (%)", 60, "Extra maximum health for the major bosses listed below.");
            majorDamage = Percent("Major Boss Damage Bonus (%)", 10, "Extra damage for the major bosses listed below.");
            majorBodies = C.Bind(Section, "Major Boss Bodies", "Brother, VoidRaidCrab, FalseSonBoss, SolusWing, SolusHeart, ScavLunar, ProvBody, ProvDesperationBody, ProvFinalBody, ReaperBoss",
                "Comma-separated parts of body names treated as major bosses (Mithrix, Voidling, False Son, Solus Wing, Solus Heart, Twisted Scavengers, Providence, Anima Harvester). Matching ignores case.");
            logBosses = C.BindBool("Debug - Enemy Difficulty", "Log Boss Bonuses", true,
                "Log each boss's body name, category and bonus when it spawns.");
            if (!enabled.Value) return;

            var method = A.Method(typeof(Run), "RecalculateDifficultyCoefficentInternal");
            if (method != null)
                hooks.Add(new ILHook(method, ScaleTimeIL));
            else ThreeQsVanillaTweaks.Log.Warning("Enemy difficulty: difficulty calculation not found; time scaling unchanged.");
            CharacterBody.onBodyStartGlobal += BoostEnemy;
        }

        private static ConfigEntry<int> Percent(string key, int value, string description) =>
            C.Bind(Section, key, value, new ConfigDescription(description + " Applied in steps of 10%. Vanilla: 0.",
                new AcceptableValueRange<int>(0, 10000)));

        private static bool IsMajor(string bodyName)
        {
            string list = C.Get(majorBodies) ?? "";
            if (!ReferenceEquals(list, majorSource))
            {
                majorSource = list;
                majorParsed = Array.FindAll(Array.ConvertAll(list.Split(','), part => part.Trim()), part => part.Length > 0);
            }
            foreach (string part in majorParsed)
                if (bodyName.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void ScaleTimeIL(ILContext il)
        {
            // The selected difficulty's scalingValue is the factor applied to elapsed time.
            var cursor = new ILCursor(il);
            int found = 0;
            while (cursor.TryGotoNext(MoveType.After, i => i.MatchLdfld<DifficultyDef>("scalingValue")))
            {
                cursor.EmitDelegate<Func<float, float>>(value => C.Get(enabled) ? value * Mathf.Max(0f, C.Get(timeScaling)) : value);
                found++;
            }
            if (found == 0) ThreeQsVanillaTweaks.Log.Warning("Enemy difficulty: time scaling value not found; time scaling unchanged.");
        }

        // Vanilla BoostHp and BoostDamage give +10% maximum health and +10% damage per stack.
        // As items they apply before stats are calculated, so enemies still spawn at full health.
        private static void BoostEnemy(CharacterBody body)
        {
            if (!NetworkServer.active || !C.Get(enabled) || !body || !body.teamComponent) return;
            var team = body.teamComponent.teamIndex;
            if (team == TeamIndex.Player || team == TeamIndex.Neutral || team == TeamIndex.None) return;
            var master = body.master;
            if (!master || !master.inventory || boosted.TryGetValue(master, out _)) return;
            // Once per master: respawned bodies keep the master's inventory.
            boosted.Add(master, new Boosted());
            string bodyName = BodyCatalog.GetBodyName(body.bodyIndex) ?? body.name;
            bool major = IsMajor(bodyName);
            bool boss = !major && (body.isChampion || body.isBoss);
            int healthPercent = C.Get(major ? majorHealth : boss ? bossHealth : healthBonus);
            int damagePercent = C.Get(major ? majorDamage : boss ? bossDamage : damageBonus);
            int health = Mathf.RoundToInt(healthPercent / 10f);
            int damage = Mathf.RoundToInt(damagePercent / 10f);
            if (health > 0) master.inventory.GiveItemPermanent(RoR2Content.Items.BoostHp, health);
            if (damage > 0) master.inventory.GiveItemPermanent(RoR2Content.Items.BoostDamage, damage);
            if ((major || boss) && C.Get(logBosses))
                ThreeQsVanillaTweaks.Log.Info("Enemy difficulty: " + bodyName + " is a " + (major ? "major boss" : "boss") +
                    "; +" + health * 10 + "% health, +" + damage * 10 + "% damage.");
        }
    }
}
