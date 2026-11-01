using RoR2;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexOfStrife
    {
        static R c;
        internal static void Init()
        {
            c=HexMechanics.Create("Hex of Strife","HexMountain");
            c.B("Rework",true,"Replace harder teleporter bosses with holder-only penalties against bosses. False restores the configurable vanilla effect.");
            c.Pair("Damage Taken From Bosses Increase (%)",30,30,"Rework: additional incoming boss damage.");
            c.Pair("Damage Dealt To Bosses Reduction (%)",30,30,"Rework: outgoing boss damage reduction, capped at 100%.");
            c.N("Vanilla Mountain Stacks Per Item",1,"Vanilla: 1 extra boss difficulty stack per item.");
            HexMechanics.Patch(typeof(TeleporterInteraction),"RecalculateHexMountainCount",typeof(HexOfStrife),postfix:nameof(Mountain));
            U.BeforeDamage+=Damage;
            HexMechanics.Text(c,()=>c.B("Rework")?"Take "+c.V("Damage Taken From Bosses Increase (%)","%","cDeath")+" more damage from bosses. Deal "+c.V("Damage Dealt To Bosses Reduction (%)","%","cDeath")+" less damage to bosses.":"Increases teleporter boss difficulty by "+c.V("Vanilla Mountain Stacks Per Item","","cDeath")+" <style=cStack>(per stack)</style>.",()=>c.B("Rework")?"Take more damage from bosses and deal less damage to them.":"Increases teleporter boss difficulty.");
        }
        static void Mountain(TeleporterInteraction __instance)
        { if(c.Active) __instance.hexBonusStacks=c.B("Rework")?0:Mathf.RoundToInt(__instance.hexBonusStacks*c.F("Vanilla Mountain Stacks Per Item")); }
        static bool Boss(CharacterBody b)=>b&&(b.isBoss||b.isChampion);
        static void Damage(HealthComponent victim, DamageInfo info)
        {
            if(!c.Active||!c.B("Rework")||info.rejected)return;
            var attacker=info.attacker?info.attacker.GetComponent<CharacterBody>():null;
            if(Boss(attacker))info.damage*=1+c.Total("Damage Taken From Bosses Increase (%)",c.Count(victim.body))/100f;
            if(Boss(victim.body))info.damage*=Mathf.Max(0,1-c.Total("Damage Dealt To Bosses Reduction (%)",c.Count(attacker))/100f);
        }
    }
}
