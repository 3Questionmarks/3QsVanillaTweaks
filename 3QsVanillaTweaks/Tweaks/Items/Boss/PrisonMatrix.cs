using System;
using RoR2;
using HarmonyLib;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class PrisonMatrix
    {
        internal static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Prison Matrix", "PowerCube", true);
            c.Pair("Armour Bonus (%)", 50, 0, "Armour increase while the item buff is active. Vanilla increases only base armour by 50%.");
            c.Literal("RoR2.CharacterBody", "RecalculateStats", 1.5f, () => 1f, "PowerCubeBuff");
            U.Ensure();
            U.Stats += Stats;
            c.Text(() => "Increase <style=cIsUtility>armour</style> by " + c.V("Armour Bonus (%)", "%", "cIsUtility") + ".", () => "Increase armour.");
        }
        static void Stats(CharacterBody body)
        {
            if (c.Active && body && body.HasBuff(DLC3Content.Buffs.PowerCubeBuff))
                U.Scale(body, "armor", c.F("Armour Bonus (%)"));
        }
    }
}
