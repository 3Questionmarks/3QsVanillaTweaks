using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class RunicLens
    {
        static R c;
        internal static void Init()
        {
            c = RareItemSupport.Config("Runic Lens", "MeteorAttackOnHighDamage");
            c.Pair("Proc Coefficient", 0, 0, "Meteor proc coefficient. Vanilla: 0.5.");
            c.N("Base Meteor Damage (%)", 2000, "Base meteor damage. Vanilla: 2000%.");
            c.Pair("Meteor Damage Per 100% Attack Damage (%)", 150, 150, "Additional meteor damage per 100% triggering attack damage. Vanilla: 150% +50% per stack.");
            c.N("Maximum Meteor Damage (%)", 7500, "Maximum meteor damage. Vanilla: 7500%.");
            c.N("Base Trigger Chance (%)", 3, "Base meteor chance. Vanilla: 3%.");
            c.Pair("Additional Chance Per 100% Attack Damage (%)", 3, 3, "Extra trigger chance per 100% attack damage. Vanilla: 3% per stack.");
            c.N("Maximum Trigger Chance (%)", 75, "Maximum meteor chance. Vanilla: 75%.");
            c.Pair("Radius", 10, 0, "Meteor explosion radius in metres. Vanilla: 10.");
            c.Pair("Impact Delay", 1, 0, "Seconds until impact. Vanilla: 1.");
            c.Pair("Blast Force", 5000, 0, "Meteor blast force. Vanilla: 5000.");
            c.Literal("RoR2.MeteorAttackOnHighDamageBodyBehavior", "DetonateRunicLensMeteor", .5f, () => c.Total("Proc Coefficient", c.Count(N.Body)));
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "CalculateDamage", "baseDamage", () => c.F("Base Meteor Damage (%)") / 100f);
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "CalculateDamage", "bonusDamageFromOverspill", () => c.F("Meteor Damage Per 100% Attack Damage (%)") / 100f);
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "CalculateDamage", "stackBonusDamageFromOverspill", () => c.F("Meteor Damage Per 100% Attack Damage (%) Per Stack") / 100f);
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "CalculateDamage", "meteorDamageThreshold", () => c.F("Maximum Meteor Damage (%)") / 100f);
            c.Field("RoR2.GlobalEventManager", "ProcessHitEnemy", "baseChanceToActivate", () => c.F("Base Trigger Chance (%)"));
            c.Field("RoR2.GlobalEventManager", "ProcessHitEnemy", "bonusChanceToActivateFromOverspill", () => c.F("Additional Chance Per 100% Attack Damage (%)"));
            c.Field("RoR2.GlobalEventManager", "ProcessHitEnemy", "stackBonusChanceToActivateFromOverspill", () => c.F("Additional Chance Per 100% Attack Damage (%) Per Stack"));
            c.Field("RoR2.GlobalEventManager", "ProcessHitEnemy", "meteorChanceThreshold", () => c.F("Maximum Trigger Chance (%)"));
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "DetonateRunicLensMeteor", "meteorRadius", () => c.Total("Radius", c.Count(N.Body)));
            c.Field("RoR2.MeteorAttackOnHighDamageBodyBehavior", "DetonateRunicLensMeteor", "meteorBlastForce", () => c.Total("Blast Force", c.Count(N.Body)));
            c.Field("RoR2.CharacterBody", "RunicLensUpdateVariables", "meteorBlastAttackDelay", () => c.Total("Impact Delay", c.Count(N.Body)));
            c.Text(() => "Hits have a " + c.V("Base Trigger Chance (%)", "%", "cIsUtility") + " chance to call down a meteor, plus " + c.V("Additional Chance Per 100% Attack Damage (%)", "%", "cIsUtility") + " chance per 100% damage of the triggering attack, up to " + c.V("Maximum Trigger Chance (%)", "%", "cIsUtility") + ". Meteors deal " + RareItemSupport.Damage(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Base Meteor Damage (%)")) + "%") + " damage, plus " + c.V("Meteor Damage Per 100% Attack Damage (%)", "%") + " per 100% damage of the triggering attack. Maximum " + RareItemSupport.Damage(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Maximum Meteor Damage (%)")) + "%") + " damage.", () => "Powerful hits are more likely to call down a damaging meteor.");
        }
    }
}
