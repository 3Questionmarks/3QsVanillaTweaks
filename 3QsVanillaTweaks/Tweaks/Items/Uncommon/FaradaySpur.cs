using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class FaradaySpur
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Faraday Spur", "JumpDamageStrike", false);
            c.Pair("Damage (%)", 400, 280, "Discharge base damage. Vanilla: 400 +280 per stack.");
            c.Pair("Proc Coefficient", 1, 0, "Discharge proc coefficient. Vanilla: 1.");
            c.N("Minimum Charge", 25, "Minimum charge required to discharge. Vanilla: 25.", 1, 100);
            c.N("Maximum Charge", 100, "Maximum stored charge. Vanilla: 100.", 1, 100);
            c.Pair("Radius", 12.5f, 7.5f, "Base discharge radius. Vanilla: 12.5 +7.5 per stack, plus charge growth.");
            c.N("Radius Per Charge", .2f, "Radius added per charge beyond the first. Vanilla: 0.2 metres.");
            c.Literal("RoR2.Items.JumpDamageStrikeBodyBehavior", "DischargeEffects", 4, () => c.F("Damage (%)") / 100);
            c.Literal("RoR2.Items.JumpDamageStrikeBodyBehavior", "DischargeEffects", 2.8f, () => c.F("Damage (%) Per Stack") / 100);
            c.Assign("RoR2.Items.JumpDamageStrikeBodyBehavior", "DischargeEffects", "procCoefficient", () => c.Total("Proc Coefficient", U.Count(UncommonNative.Body, "JumpDamageStrike")));
            FaradaySupport.Register(c);
            c.Text(() => "Sprinting builds charge. Jumping discharges it, striking nearby enemies for " + c.V("Damage (%)", "%") + " base damage.", () => "Sprint to charge a <style=cIsDamage>lightning strike</style>, then jump to release it.");
        }
    }
}
