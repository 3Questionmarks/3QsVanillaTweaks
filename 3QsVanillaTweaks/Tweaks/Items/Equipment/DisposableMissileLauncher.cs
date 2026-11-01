using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentCommandMissile
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("CommandMissile");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            
            VanillaEquipmentDefaults.Number(c,"Missiles",12,"Missiles launched per use.","FireCommandMissile",12);VanillaEquipmentDefaults.Number(c,"Damage (%)",300,"Damage per missile.","FireMissile",3,100);
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
