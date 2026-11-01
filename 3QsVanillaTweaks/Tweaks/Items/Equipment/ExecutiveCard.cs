using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentMultiShopCard
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("MultiShopCard");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
