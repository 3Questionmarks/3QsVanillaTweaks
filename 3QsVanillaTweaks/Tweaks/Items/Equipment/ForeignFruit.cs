using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentFruit
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("Fruit");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.Number(c,"Healing (%)",50,"Healing based on maximum health.","FireFruit",.5f,100);
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
