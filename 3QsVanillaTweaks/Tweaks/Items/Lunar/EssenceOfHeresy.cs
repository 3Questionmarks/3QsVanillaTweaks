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
        private static void BindEssenceOfHeresy(Profile p)
        {
            p.Number("Cooldown Per Stack", 8f, 8f, "Additional cooldown seconds for each item after the first. First-copy cooldown is configured under the skill.", false);

                p.Number("Ruin Duration Per Stack", 20f, 0f, "Additional Ruin debuff seconds per extra item.", false);
                p.Number("Ruin Damage Per Stack (%)", 0f, 0f, "Additional base explosion damage percentage points per extra item.", false);
                p.Number("Ruin Damage Per Debuff Stack Per Stack (%)", 0f, 0f, "Additional damage percentage points per Ruin debuff stack per extra item.", false);
                p.Number("Damage (%)", 300f, Detonate.baseDamageCoefficient * 100f, "Base detonation damage percentage, before Ruin debuff stacks.");
                p.Number("Ruin Debuff Per Stack Extra Damage (%)", 120f, Detonate.damageCoefficientPerStack * 100f, "Additional damage percentage per Ruin debuff stack.");
                p.Number("Ruin Debuff Duration", 20f, 10f, "First-copy Ruin debuff duration in seconds.");
                p.Number("Proc Coefficient", 1f, 1f, "Proc coefficient of the detonation.");
                p.Number("Orb Speed", 120f, 120f, "Detonation orb travel speed in metres per second.");
                p.Flag("Can Apply Ruin On Cooldown", false, false, "Apply Ruin when the skill has no stocks ready.");
                p.Flag("Reapplying Resets Duration", true, false, "A new Ruin stack refreshes the timers of existing Ruin stacks.");
            
        }
    }
}
