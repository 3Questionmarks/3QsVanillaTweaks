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
    internal static class NoxiousThorn
    {
        static UncommonConfig c;
        internal static void Init()
        {
            c = new UncommonConfig("Noxious Thorn", "TriggerEnemyDebuffs", false);
            c.Pair("Targets", 1, 1, "Nearby enemies that receive debuffs from a defeated enemy. Vanilla: 1 per stack.");
            c.Pair("Range", 20, 5, "Debuff spread range. Vanilla: 20 +5 metres per stack.");
            c.N("Debuff Stacks Copied (%)", 33, "Percentage of each debuff's stacks copied, rounded up. Vanilla: 33%.");
            c.N("Trigger Chance (%)", 100, "Chance on kill to spread debuffs. Vanilla: 100%.");
            c.Literal("RoR2.CharacterBody", "TriggerEnemyDebuffs", .33f, () => c.F("Debuff Stacks Copied (%)") / 100);
            c.Literal("RoR2.CharacterBody", "TriggerEnemyDebuffs", 20, () => c.F("Range"));
            c.Literal("RoR2.CharacterBody", "TriggerEnemyDebuffs", 5, () => c.F("Range Per Stack"));
            if (c.Enabled.Value)
                U.Patch(typeof(CharacterBody), "TriggerEnemyDebuffs", typeof(NoxiousThorn), prefix: nameof(Trigger));
            NoxiousSupport.Register(c);
            c.Text(() => "Killing an enemy spreads " + c.V("Debuff Stacks Copied (%)", "%", "cIsUtility") + " of its debuff stacks to " + c.V("Targets", "", "cIsUtility") + " enemies within " + c.V("Range", "m", "cIsUtility") + ".", () => "Killing enemies <style=cIsUtility>spreads their debuffs</style> to nearby enemies.");
        }

        static bool Trigger(CharacterBody __instance) => !c.Active || (c.Total("Targets", c.Count(__instance)) > 0 && U.Roll(c.F("Trigger Chance (%)"), __instance));
    }
}
