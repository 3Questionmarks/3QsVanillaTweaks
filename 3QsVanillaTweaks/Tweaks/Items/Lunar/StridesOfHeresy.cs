using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Mage.Weapon;
using EntityStates.GlobalSkills.LunarNeedle;
using EntityStates.GlobalSkills.LunarDetonator;
using HarmonyLib;
using RoR2;
using RoR2.Orbs;
using RoR2.Projectile;
using RoR2.Skills;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Survivors.SurvivorSettings;
using LG = Tweaks.ModCompatibility.LookingGlassSkillStats;

namespace Tweaks.Items.Lunar
{
    internal static partial class HeresyItems
    {
        private static void BindStridesOfHeresy(Profile p)
        {
            p.Number("Cooldown Per Stack", 0f, 0f, "Additional cooldown seconds for each item after the first. First-copy cooldown is configured under the skill.", false);

                p.Number("Movement Speed Per Stack (%)", 0f, 0f, "Additional movement-speed percentage points per extra item.", false);
                p.Number("Heal Per Stack (%)", 1.5f, 0f, "Additional percentage of maximum health restored per interval per extra item.", false);
                p.Number("Duration Per Stack", 3f, 3f, "Additional duration seconds per extra item.", false);
                p.Number("Movement Speed Boost (%)", 30f, (GhostUtilitySkillState.moveSpeedCoefficient - 1f) * 100f, "Movement-speed boost while intangible.");
                p.Number("Heal Per Interval (%)", 1.5f, GhostUtilitySkillState.healFractionPerTick * 100f, "Percentage of maximum health healed each tick.");
                p.Number("Interval", .2f, 1f / Math.Max(.001f, GhostUtilitySkillState.healFrequency), "Seconds between healing ticks.", min: .001f);
                p.Number("Duration", 3f, GhostUtilitySkillState.baseDuration, "First-copy duration in seconds.", min: .001f);
                p.Flag("Can Cancel", false, false, "Press utility again after releasing it to cancel early. Heretic's passive can independently enable this.");
                p.Flag("Cleanses Debuffs", true, false, "Cleanse debuffs, damage over time and stun on activation. Keeps positive buffs and cooldown debuffs, including bands.");
            
        }
    }
}
