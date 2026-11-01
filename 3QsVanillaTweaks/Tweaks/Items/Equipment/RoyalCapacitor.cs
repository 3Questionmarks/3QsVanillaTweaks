using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentLightning
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("Lightning");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.Number(c,"Damage (%)",3000,"Damage per lightning strike.","FireLightning",30,100);VanillaEquipmentDefaults.Number(c,"Proc Coefficient",1,"Proc coefficient per lightning strike.","FireLightning",1);
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
