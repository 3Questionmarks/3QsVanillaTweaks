using RoR2;
namespace Tweaks.Items.Equipment

{
    internal static class VanillaEquipmentDefaults
    {
        internal static EquipmentConfig Create(string id)
        {
            EquipmentConfig.MarkLoaded();
            var def=EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(id));
            if(!def||!def.canDrop||def.passiveBuffDef)return null;
            return new EquipmentConfig(Language.GetString(def.nameToken),id,false);
        }
        internal static void PreserveText(EquipmentConfig c, Tweaks.Items.Equipment.EquipmentNativeConfig values)
        {
            var def=c.Def;string old=Language.GetString(def.descriptionToken),pickup=Language.GetString(def.pickupToken);
            c.Text(()=>values.Rewrite(old),()=>pickup);
        }
        internal static void Number(EquipmentConfig c,string key,float native,string description,string method,float coefficient,float unit=1)
        {
            c.N(key,native,description+" Vanilla: "+native+".");c.Literal(method,coefficient,key,unit);
        }
    }
}
