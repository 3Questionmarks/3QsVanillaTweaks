using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentLifestealOnHit
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("LifestealOnHit");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.Number(c,"Buff Duration",8,"Lifesteal duration, in seconds.","FireLifeStealOnHit",8);
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
