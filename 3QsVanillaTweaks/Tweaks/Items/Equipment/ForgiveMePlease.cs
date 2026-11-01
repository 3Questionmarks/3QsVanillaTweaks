using RoR2;
using UnityEngine;
namespace Tweaks.Items.Equipment
{
    internal static class VanillaEquipmentDeathProjectile
    {
        internal static void Init()=>RoR2Application.onLoad+=Load;
        static void Load()
        {
            var c=VanillaEquipmentDefaults.Create("DeathProjectile");if(c==null)return;
            var values=new Tweaks.Items.Equipment.EquipmentNativeConfig(c.Values,c.Id);
            values.Prefab(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/DeathProjectile"));
            VanillaEquipmentDefaults.PreserveText(c,values);
        }
    }
}
