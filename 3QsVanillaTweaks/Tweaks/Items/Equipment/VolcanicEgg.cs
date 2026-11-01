using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentFireBallDash
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("FireBallDash");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            values.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/FireballVehicle"));
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
