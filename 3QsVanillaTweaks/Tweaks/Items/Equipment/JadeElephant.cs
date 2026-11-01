using RoR2;
namespace Tweaks.Items.Equipment
{
    internal static class JadeElephant
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("Jade Elephant","GainArmor",true,30);
            c.N("Armour",500,"Armour granted while active. Vanilla: 500.");
            c.N("Buff Duration",5,"Buff duration in seconds. Vanilla: 5.");
            c.Literal("FireGainArmor",5,"Buff Duration");
            c.Values.Literal("RoR2.CharacterBody","RecalculateStats",500,()=>c.F("Armour"),"ElephantArmorBoost");
            c.Text(()=>"Gain <style=cIsHealing>"+c.F("Armour")+" armor</style> for "+c.V("Buff Duration"," seconds","cIsUtility")+".",()=>"Gain massive armor for a short time.");
        }
    }
}
