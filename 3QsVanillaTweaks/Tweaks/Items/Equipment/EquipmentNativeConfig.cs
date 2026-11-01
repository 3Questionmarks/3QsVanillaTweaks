using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using C=Config.ConfigManager;
using R=Tweaks.Items.Uncommon.UncommonConfig;
using S=Tweaks.Items.Common.CommonItemSupport;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
using A=ThreeQsVanillaTweaks.ReflectionTools;
namespace Tweaks.Items.Equipment
{
    // Equipment-specific native values; independent of the removed Defaults folder.
    internal sealed class EquipmentNativeConfig
    {
        internal readonly R Values;
        internal readonly string Id;
        internal readonly List<Tuple<float,Func<float>,string>> DisplayValues=new List<Tuple<float,Func<float>,string>>();
        static readonly BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
        internal EquipmentNativeConfig(R values,string id){Values=values;Id=id;}
        internal static string Words(string text)=>Regex.Replace(Regex.Replace(text.TrimStart('_'),"([a-z0-9])([A-Z])","$1 $2"),"_+"," ");
        internal void Number(string key,float value,string description,float unit=1)
        {
            if(Values.Numbers.ContainsKey(key))return;
            Values.N(key,value*unit,description+" Vanilla: "+(value*unit).ToString("0.####",CultureInfo.InvariantCulture)+".",0);
            DisplayValues.Add(Tuple.Create(value*unit,(Func<float>)(()=>Values.F(key)),key));
        }
        static float Unit(string name)=>name.IndexOf("proc",StringComparison.OrdinalIgnoreCase)>=0?1:name.IndexOf("Coefficient",StringComparison.OrdinalIgnoreCase)>=0||name.EndsWith("Normalized",StringComparison.OrdinalIgnoreCase)||name.IndexOf("Percentage",StringComparison.OrdinalIgnoreCase)>=0||name.EndsWith("Fraction",StringComparison.OrdinalIgnoreCase)||name.StartsWith("regainPercent",StringComparison.OrdinalIgnoreCase)?100:1;
        readonly HashSet<GameObject> scanned=new HashSet<GameObject>();
        internal void Assets(params string[] types)
        {
            foreach(string name in types){var type=typeof(CharacterBody).Assembly.GetType(name);if(type==null)continue;
                foreach(var field in type.GetFields(Members).Where(f=>f.IsStatic&&typeof(GameObject).IsAssignableFrom(f.FieldType))){var prefab=field.GetValue(null) as GameObject;if(prefab)Prefab(prefab);}
            }
        }
        internal void Prefab(GameObject prefab)
        {
            if(!prefab||scanned.Contains(prefab))return;
            var file=Values.Enabled.ConfigFile;bool save=file.SaveOnConfigSet;file.SaveOnConfigSet=false;
            try{BindPrefab(prefab);}finally{file.SaveOnConfigSet=save;if(save)file.Save();}
        }
        void BindPrefab(GameObject prefab)
        {
            if(!prefab||!scanned.Add(prefab))return;
            foreach(var component in prefab.GetComponentsInChildren<MonoBehaviour>(true)){
                if(!component||component is RoR2.Projectile.ProjectileDamage||component.GetType().Assembly!=typeof(CharacterBody).Assembly||Regex.IsMatch(component.GetType().Name,"(?i)(effect|visual|vfx|sound|camera|animation|audio)"))continue;
                foreach(var field in component.GetType().GetFields(Members)){
                    if(field.IsStatic||field.IsInitOnly||field.IsLiteral||field.IsNotSerialized||(!field.IsPublic&&!field.IsDefined(typeof(SerializeField),true)))continue;
                    if(field.IsDefined(typeof(UnityEngine.Networking.SyncVarAttribute),true))continue;
                    if(typeof(PickupDropTable).IsAssignableFrom(field.FieldType)){DropTable(field.GetValue(component) as PickupDropTable);continue;}
                    if(field.FieldType==typeof(GameObject)){var child=field.GetValue(component) as GameObject;if(child&&child.GetComponent<RoR2.Projectile.ProjectileController>())Prefab(child);continue;}
                    if(field.FieldType!=typeof(float)&&field.FieldType!=typeof(int)&&field.FieldType!=typeof(uint))continue;
                    // Never expose runtime counters, network timings, layer masks or animation IDs as item balance settings.
                    string f=field.Name;if(!Regex.IsMatch(f,"(?i)(damage|proc|duration|lifetime|radius|range|interval|frequency|heal|health|regen|barrier|shield|armor|armour|speed|force|chance|count|maximum|minimum|cooldown|recharge|targets|distance|cost|crit|stock|jump|acceleration)"))continue;if(Regex.IsMatch(f,"(?i)(stopwatch|stopWatch|timer$|netId|hash$|networkInterval|counter$|current|spawned|index$|seed|step|sound|rtpc|visual|effectScale|angleOffset)"))continue;
                    float native=Convert.ToSingle(field.GetValue(component),CultureInfo.InvariantCulture);if(native<0||float.IsNaN(native)||float.IsInfinity(native)||native>100000)continue;
                    float unit=Unit(f);string key=Words(prefab.name)+" - "+Words(component.GetType().Name)+" - "+Words(f)+(unit==100?" (%)":"");
                    Number(key,native,"Native "+prefab.name+"."+f+".",unit);
                    if(!Values.Enabled.Value)continue;
                    Action apply=()=>field.SetValue(component,Convert.ChangeType(Values.Active?Values.F(key)/unit:native,field.FieldType,CultureInfo.InvariantCulture));apply();C.EffectiveSettingsChanged+=apply;
                }
            }
        }
        readonly HashSet<PickupDropTable> tables=new HashSet<PickupDropTable>();
        internal void DropTable(PickupDropTable table)
        {
            if(!table||!tables.Add(table))return;
            foreach(var field in table.GetType().GetFields(Members)){
                if(field.IsStatic||field.IsInitOnly||field.IsLiteral||field.IsNotSerialized||(!field.IsPublic&&!field.IsDefined(typeof(SerializeField),true)))continue;
                if(field.FieldType!=typeof(float)&&field.FieldType!=typeof(int))continue;
                float native=Convert.ToSingle(field.GetValue(table));if(native<0)continue;
                string key=Words(table.name)+" - "+Words(field.Name);Number(key,native,"Native drop table value.");
                if(!Values.Enabled.Value)continue;
                Action apply=()=>field.SetValue(table,Convert.ChangeType(Values.Active?Values.F(key):native,field.FieldType));apply();C.EffectiveSettingsChanged+=apply;
            }
        }
        internal string Rewrite(string text)
        {
            return Regex.Replace(text,@"(?<![\w#])\d+(?:\.\d+)?",m=>{
                if(!float.TryParse(m.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out float native))return m.Value;
                var matches=DisplayValues.Where(v=>Mathf.Abs(v.Item1-native)<.00001f).ToList();
                if(matches.Count==0)return m.Value;
                var values=matches.Select(v=>v.Item2()).Distinct().ToArray();return values.Length==1?values[0].ToString("0.####",CultureInfo.InvariantCulture):m.Value;
            });
        }
    }
}
