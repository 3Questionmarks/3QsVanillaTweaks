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
        private static void BindVisionsOfHeresy(Profile p)
        {
            p.Number("Cooldown Per Stack", 2f, 2f, "Additional cooldown seconds for each item after the first. First-copy cooldown is configured under the skill.", false);

                p.Flag("Rapid", false, false, "Cooldown is reduced by attack speed. Overrides Moffein Heretic's Visions attack-speed setting.", false);
                p.Number("Initial Hit Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on impact per extra item.", false);
                p.Number("Detonation Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on detonation per extra item.", false);
                p.Number("Charges Per Stack", 12f, 12f, "Additional charges per extra item, rounded down.", false);
                p.Projectile = FireLunarNeedle.projectilePrefab;
                RequireProjectile(p);
                p.Number("Initial Hit Damage (%)", 5f, 5f, "Impact damage as a percentage of base damage.");
                p.Number("Initial Hit Proc Coefficient", .1f, .1f, "Proc coefficient of the initial impact.");
                p.Number("Detonation Damage (%)", 120f, 120f, "Detonation damage as a percentage of base damage.");
                p.Number("Proc Coefficient", NativeExplosionProc(p), NativeExplosionProc(p), "Detonation proc coefficient.");
                p.Number("Damage Interval", FireLunarNeedle.baseDuration, FireLunarNeedle.baseDuration, "Minimum time between shots before attack speed.", min: .001f);
                var steering = p.Projectile.GetComponent<ProjectileSteerTowardTarget>();
                if (!steering) throw new InvalidOperationException("Hungering Gaze steering component was not found.");
                p.Number("Tracking Aggression", steering.rotationSpeed * 2f, steering.rotationSpeed, "Homing turn rate in degrees per second; default is twice vanilla.");
                BindProjectileBasics(p);
            
        }
    }
}
