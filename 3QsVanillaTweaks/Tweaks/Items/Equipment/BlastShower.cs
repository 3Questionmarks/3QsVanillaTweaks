using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentCleanse
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("Cleanse");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
