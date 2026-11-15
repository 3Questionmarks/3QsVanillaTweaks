using RoR2;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexOfRain
    {
        static R c;
        internal static void Init()
        {
            c = HexMechanics.Create("Hex of Rain", "IncreasedDifficulty");
            c.B("Rework", true, "Replace team-wide difficulty scaling with damage penalties for the holder. False restores the configurable vanilla effect.");
            c.Pair("Damage Taken Increase (%)", 20, 20, "Rework: additional damage taken by the holder.");
            c.Pair("Damage Dealt Reduction (%)", 20, 20, "Rework: damage dealt reduction with diminishing stacking.");
            c.StatValue = (key, count, value) => c.B("Rework") &&
                (key == "Damage Taken Increase (%)" || key == "Damage Dealt Reduction (%)")
                ? HexMechanics.Penalty(c, key, count) : value;
            c.N("Vanilla Difficulty Increase Per Stack (%)",25,"Vanilla: 25%. Retains vanilla multiplayer scaling.");
            c.Literal("RoR2.Run","RecalculateDifficultyCoefficentInternal",.25f,()=>c.B("Rework")?0:c.F("Vanilla Difficulty Increase Per Stack (%)")/100f,"IncreasedDifficulty");
            U.BeforeDamage += Damage;
            HexMechanics.Text(c,()=>c.B("Rework") ? "Take "+HexMechanics.PenaltyText(c,"Damage Taken Increase (%)")+" more damage. Deal "+HexMechanics.PenaltyText(c,"Damage Dealt Reduction (%)")+" less damage." : "Increases <style=cDeath>difficulty</style> by "+c.V("Vanilla Difficulty Increase Per Stack (%)","%","cDeath")+" <style=cStack>(per stack)</style>.",()=>c.B("Rework")?"Take more damage and deal less damage.":"Increases difficulty.");
        }
        static void Damage(HealthComponent victim, DamageInfo info)
        {
            if (!c.Active || !c.B("Rework") || info.rejected) return;
            int incoming=c.Count(victim.body), outgoing=c.Count(info.attacker?info.attacker.GetComponent<CharacterBody>():null);
            info.damage *= (1+HexMechanics.Penalty(c,"Damage Taken Increase (%)",incoming)/100f)
                *(1-HexMechanics.Penalty(c,"Damage Dealt Reduction (%)",outgoing)/100f);
        }
    }
}
