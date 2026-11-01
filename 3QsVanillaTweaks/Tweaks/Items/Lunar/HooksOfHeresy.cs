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
        private static void BindHooksOfHeresy(Profile p)
        {
            p.Number("Cooldown Per Stack", 5f, 5f, "Additional cooldown seconds for each item after the first. First-copy cooldown is configured under the skill.", false);

                p.Number("Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points per periodic hit per extra item.", false);
                p.Number("Explosion Damage Per Stack (%)", 0f, 0f, "Additional base-damage percentage points on explosion per extra item.", false);
                p.Number("Root Duration Per Stack", 3f, 3f, "Additional root seconds per extra item.", false);
                object sample = EntityStateCatalog.InstantiateState(typeof(ThrowLunarSecondary));
                p.Projectile = S.Read(sample, "projectilePrefab") as GameObject;
                RequireProjectile(p);
                p.Number("Damage (%)", 175f, 175f, "Damage per periodic hit as a percentage of base damage.");
                p.Number("Damage Interval", .2f, .2f, "Time between repeated hits in seconds.", min: .001f);
                p.Number("Explosion Damage (%)", 700f, 700f, "Explosion damage as a percentage of base damage.");
                p.Number("Proc Coefficient", NativeProc(p), NativeProc(p), "Proc coefficient per periodic hit.");
                p.Number("Explosion Proc Coefficient", NativeExplosionProc(p), NativeExplosionProc(p), "Proc coefficient of the final explosion.");
                p.Number("Root Duration", 3f, 3f, "First-copy root duration in seconds.");
                p.Number("Minimum Velocity", S.Value(sample, "minSpeed"), S.Value(sample, "minSpeed"), "Projectile launch speed at minimum charge in metres per second.");
                p.Number("Maximum Velocity", S.Value(sample, "maxSpeed"), S.Value(sample, "maxSpeed"), "Projectile launch speed at full charge in metres per second.");
                p.Number("Charge Duration", S.Value(p.Skill.Sample, "baseDuration", 1f), S.Value(p.Skill.Sample, "baseDuration", 1f), "Time required to fully charge before attack speed.", min: .001f);
                p.Number("Minimum Charge Duration", S.Value(p.Skill.Sample, "minChargeDuration", .5f), S.Value(p.Skill.Sample, "minChargeDuration", .5f), "Minimum time before releasing a charged shot.");
                p.Number("Throw Duration", S.Value(sample, "baseDuration", .2f), S.Value(sample, "baseDuration", .2f), "Throw animation time before attack speed.", min: .001f);
                p.Flag("Show Damage Per Tick", true, false, "Show damage per hit rather than damage per second.");
                BindProjectileBasics(p);
            
        }
    }
}
