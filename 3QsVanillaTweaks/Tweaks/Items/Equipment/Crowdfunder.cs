using EntityStates.GoldGat;
using RoR2;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Equipment
{
    internal static class Crowdfunder
    {
        static EquipmentConfig c;
        internal static void Init()
        {
            c=new EquipmentConfig("The Crowdfunder","GoldGat");
            c.N("Damage (%)",200,"Damage per bullet. Vanilla: 100%.");
            RoR2Application.onLoad+=()=>{
                foreach(var field in new[]{"procCoefficient","minFireFrequency","maxFireFrequency","windUpDuration","baseMoneyCostPerBullet","minSpread","maxSpread","force"}){
                    var f=A.Field(typeof(GoldGatFire),field);if(f==null)continue;float native=System.Convert.ToSingle(f.GetValue(null));string key=Key(field);c.N(key,native,"Native "+field+". Default read after skill state configuration has loaded.");

                }
            };
            if(c.Values.Enabled.Value)Tweaks.Items.Uncommon.UncommonItemSupport.Harmony.Patch(A.Method(typeof(GoldGatFire),"FireBullet"),prefix:new HarmonyLib.HarmonyMethod(typeof(Crowdfunder),nameof(Begin)),finalizer:new HarmonyLib.HarmonyMethod(typeof(Crowdfunder),nameof(End)));
            c.Text(()=>"Fires a continuous barrage that deals "+c.V("Damage (%)","%")+" damage per bullet. <style=cIsUtility>Costs gold per bullet</style>. Cost increases over time.",()=>"Toggle to fire. Costs gold per bullet.");
        }
        static readonly string[] fields={"damageCoefficient","procCoefficient","minFireFrequency","maxFireFrequency","windUpDuration","baseMoneyCostPerBullet","minSpread","maxSpread","force"};
        static void Begin(out object[] __state)
        {
            __state=null;if(!c.Active||!c.Values.Numbers.ContainsKey("Proc Coefficient"))return;
            __state=new object[fields.Length];for(int i=0;i<fields.Length;i++){
                var f=A.Field(typeof(GoldGatFire),fields[i]);__state[i]=f.GetValue(null);float v=fields[i]=="damageCoefficient"?c.F("Damage (%)")/100:c.F(Key(fields[i]));
                f.SetValue(null,System.Convert.ChangeType(v,f.FieldType));
            }
        }
        static System.Exception End(object[] __state,System.Exception __exception){if(__state!=null)for(int i=0;i<fields.Length;i++)A.Field(typeof(GoldGatFire),fields[i]).SetValue(null,__state[i]);return __exception;}
        static string Key(string field)=>field=="procCoefficient"?"Proc Coefficient":field=="minFireFrequency"?"Minimum Fire Rate":field=="maxFireFrequency"?"Maximum Fire Rate":field=="windUpDuration"?"Wind Up Duration":field=="baseMoneyCostPerBullet"?"Base Gold Per Bullet":field=="minSpread"?"Minimum Spread":field=="maxSpread"?"Maximum Spread":"Force";
    }
}
