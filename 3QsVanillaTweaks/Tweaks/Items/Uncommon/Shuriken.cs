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
    internal static class Shuriken
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Shuriken", "PrimarySkillShuriken", false);
            c.Pair("Damage (%)", 400, 100, "Shuriken base damage. Vanilla: 400 +100 per stack.");
            c.Pair("Maximum Stocks", 3, 1, "Shuriken charges. Vanilla: 3 +1 per stack.");
            c.Pair("Proc Coefficient", 1, 0, "Shuriken proc coefficient. Vanilla: 1.");
            c.N("Full Reload Time", 10, "Time to reload all shurikens. Vanilla: 10 seconds.");
            c.Field("RoR2.PrimarySkillShurikenBehavior", "FireShuriken", "damageCoefficientBase", () => (c.F("Damage (%)") - c.F("Damage (%) Per Stack")) / 100);
            c.Field("RoR2.PrimarySkillShurikenBehavior", "FireShuriken", "damageCoefficientPerStack", () => c.F("Damage (%) Per Stack") / 100);
            c.Field("RoR2.PrimarySkillShurikenBehavior", "FixedUpdate", "totalReloadTime", () => c.F("Full Reload Time"));
            ShurikenStockSupport.Register(c);
            UncommonProjectiles.Register(c, "Prefabs/Projectiles/ShurikenProjectile");
            c.Text(() => "Using your primary skill throws a <style=cIsDamage>shuriken</style> for " + c.V("Damage (%)", "%") + " base damage. Hold up to " + c.V("Maximum Stocks", "", "cIsUtility") + ", reloading over " + c.V("Full Reload Time", "s", "cIsUtility") + ".", () => "Using your primary skill throws a <style=cIsDamage>shuriken</style>.");
        }
    }
}
