using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentPassiveHealing
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("PassiveHealing");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.Number(c,"Activation Healing (%)",10,"Healing on activation.","FirePassiveHealing",.1f,100);
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
