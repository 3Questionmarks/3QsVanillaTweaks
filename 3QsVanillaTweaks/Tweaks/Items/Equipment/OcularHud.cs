using RoR2;
using UnityEngine;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Equipment
{
    internal static class OcularHud
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("Ocular HUD","CritOnUse");
            c.N("Critical Strike Chance (%)",100,"Critical chance granted while active. Vanilla: 100%.");c.N("Buff Duration",8,"Buff duration in seconds. Vanilla: 8.");
            c.Flag("Convert Excess Critical Chance",true,"Convert the critical chance granted by this buff which exceeds 100% into critical strike damage.");c.N("Conversion Ratio",1,"Critical damage percentage points per excess critical chance percentage point. Default: 1:1.");
            c.Literal("FireCritOnUse",8,"Buff Duration");
            c.Values.Literal("RoR2.CharacterBody","RecalculateStats",100,()=>c.F("Critical Strike Chance (%)"),"FullCrit");
            U.Ensure();U.Stats+=Stats;
            c.Text(()=>"Gain "+c.V("Critical Strike Chance (%)","%")+" <style=cIsDamage>Critical Strike Chance</style> for "+c.V("Buff Duration"," seconds","cIsUtility")+"."+(c.B("Convert Excess Critical Chance")?" Excess critical strike chance from this effect increases <style=cIsDamage>critical strike damage</style>"+(c.F("Conversion Ratio")==1?" at a <style=cIsDamage>1:1</style> ratio.":" by <style=cIsDamage>"+c.F("Conversion Ratio")+"%</style> per <style=cIsDamage>1%</style> excess chance."):""),()=>"Gain Critical Strike Chance."+(c.B("Convert Excess Critical Chance")?" Excess chance increases critical strike damage.":""));
        }
        static void Stats(CharacterBody b)
        {
            if(!c.Active||!c.B("Convert Excess Critical Chance")||!b.HasBuff(RoR2Content.Buffs.FullCrit))return;
            // Railgunner's conversion item already handles all critical chance; do not convert it twice.
            if(b.inventory&&b.inventory.GetItemCountPermanent(DLC1Content.Items.ConvertCritChanceToCritDamage.itemIndex)>0)return;
            float granted=c.F("Critical Strike Chance (%)");float excess=Mathf.Min(granted,Mathf.Max(0,b.crit-100));
            U.Add(b,"critMultiplier",excess*c.F("Conversion Ratio")/100);U.Add(b,"crit",-excess);
        }
    }
}
