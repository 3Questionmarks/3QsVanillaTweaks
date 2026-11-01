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
namespace Tweaks.Items.Hex
{
    internal static class HexConfigSupport
    {
        static readonly Dictionary<string,Action<HexConfig>> registrations = new Dictionary<string,Action<HexConfig>>();
        static bool subscribed;
        internal static void Register(string id, Action<HexConfig> configure)
        {
            registrations.Add(id, configure);
            if (subscribed) return;
            subscribed = true;
            RoR2Application.onLoad += Load;
        }
        static void Load()
        {
            U.Ensure();
            foreach (var entry in registrations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var item = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(entry.Key));
                if (!item || item.hidden) continue;
                var settings = new HexConfig(item);
                entry.Value(settings);
                settings.PreserveText();
            }
            HexFieldOptions.Install();
        }
    }
    // Native-only entries are opt-in. No item counts, descriptions or prefabs are changed while disabled.
    internal sealed class HexConfig
    {
        internal readonly R Values;
        internal readonly string Id;
        internal readonly List<Tuple<float,Func<float>,string>> DisplayValues=new List<Tuple<float,Func<float>,string>>();
        static readonly BindingFlags Members=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly;
        internal HexConfig(ItemDef item)
        {
            Id=item.name;Values=new R(Language.GetString(item.nameToken),Id,false,"Hex");
        }
        internal HexConfig(R values,string id){Values=values;Id=id;}
        internal static string Words(string text)=>Regex.Replace(Regex.Replace(text.TrimStart('_'),"([a-z0-9])([A-Z])","$1 $2"),"_+"," ");
        internal void Number(string key,float value,string description,float unit=1)
        {
            if(Values.Numbers.ContainsKey(key))return;
            Values.N(key,value*unit,description+" Vanilla: "+(value*unit).ToString("0.####",CultureInfo.InvariantCulture)+".",0);
            DisplayValues.Add(Tuple.Create(value*unit,(Func<float>)(()=>Values.F(key)),key));
        }
        internal void Pair(string key,float first,float extra,string description,float unit=1)
        {
            Values.Pair(key,first*unit,extra*unit,description+" Vanilla: "+first*unit+" +"+extra*unit+" per stack.");
            DisplayValues.Add(Tuple.Create(first*unit,(Func<float>)(()=>Values.F(key)),key));DisplayValues.Add(Tuple.Create(extra*unit,(Func<float>)(()=>Values.F(key+" Per Stack")),key+" Per Stack"));
        }
        internal void Literal(string type,string method,float native,string key,float unit=1,int occurrence=0,string anchor=null,int window=int.MaxValue)
        {
            Number(key,native,"Native balance value.",unit);Values.Literal(type,method,native,()=>Values.F(key)/unit,anchor,occurrence,window);
        }
        internal void Count(string type,string method,string field,float native,string key,float unit=1)
        {
            Pair(key,native,native,"Effect per item stack.",unit);Values.NativeCount(type,method,native,key,unit,field);
        }
        internal void StaticField(string declaring,string fieldName,string[] readers)
        {
            var type=typeof(CharacterBody).Assembly.GetType(declaring);var field=type==null?null:A.Field(type,fieldName);if(field==null)return;
            float native=Convert.ToSingle(field.GetValue(null),CultureInfo.InvariantCulture);if(float.IsNaN(native)||float.IsInfinity(native)||native<0)return;
            float unit=Unit(fieldName);string key=Words(fieldName)+(unit==100?" (%)":"");Number(key,native,"Native "+type.Name+"."+fieldName+".",unit);
            if(!Values.Enabled.Value)return;
            foreach(string reader in readers){int split=reader.LastIndexOf('|');string readerType=reader.Substring(0,split),method=reader.Substring(split+1);
                HexFieldOptions.Add(this,typeof(CharacterBody).Assembly.GetType(readerType),method,field,key,unit);
            }
        }
        internal void InstanceField(string declaring,string fieldName,float native,string[] readers)
        {
            var type=typeof(CharacterBody).Assembly.GetType(declaring);var field=type==null?null:A.Field(type,fieldName);if(field==null)return;
            float unit=Unit(fieldName);string key=Words(fieldName)+(unit==100?" (%)":"");Number(key,native,"Native "+type.Name+"."+fieldName+".",unit);
            if(!Values.Enabled.Value)return;
            foreach(string reader in readers){int split=reader.LastIndexOf('|');HexFieldOptions.Add(this,typeof(CharacterBody).Assembly.GetType(reader.Substring(0,split)),reader.Substring(split+1),field,key,unit);}
        }
        internal void FieldValue(string type, string fieldName, string key, float unit, params string[] methods)
        {
            var declaring = typeof(CharacterBody).Assembly.GetType(type);
            var field = declaring == null ? null : A.Field(declaring, fieldName);
            if (field == null || !Values.Enabled.Value) return;
            foreach (var method in methods) HexFieldOptions.Add(this, declaring, method, field, key, unit);
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
        bool described;
        internal void Describe(Func<string> description,Func<string> pickup=null)
        {
            if(!Values.Enabled.Value)return;var item=Values.Item;if(!item)return;described=true;
            string desc=item.descriptionToken,pick=item.pickupToken,old=Language.GetString(desc),oldPick=Language.GetString(pick);
            string newDesc="3QS_NATIVE_"+Id.ToUpperInvariant()+"_DESC",newPick="3QS_NATIVE_"+Id.ToUpperInvariant()+"_PICKUP";
            Func<string> full=()=>Values.Active?description():old;Func<string> shortText=()=>Values.Active&&pickup!=null?pickup():oldPick;
            S.Text(desc,full);S.Text(pick,shortText);S.Text(newDesc,full);S.Text(newPick,shortText);item.descriptionToken=newDesc;item.pickupToken=newPick;
        }
        internal void PreserveText()
        {
            if(described||!Values.Enabled.Value)return;var item=Values.Item;if(!item)return;
            string old=Language.GetString(item.descriptionToken),pickup=Language.GetString(item.pickupToken);
            // Keep the original sentences, tags and stacking notation. Only unambiguous configured values are substituted.
            string desc=item.descriptionToken,pick=item.pickupToken;
            S.Text(desc,()=>Values.Active?Rewrite(old):old);S.Text(pick,()=>pickup);
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
    internal static class HexFieldOptions
    {
        sealed class Rule{internal HexConfig Config;internal FieldInfo Field;internal string Key;internal float Unit;}
        static readonly List<Rule> rules=new List<Rule>();
        static readonly Dictionary<MethodBase,List<int>> methods=new Dictionary<MethodBase,List<int>>();
        internal static void Add(HexConfig c,Type type,string name,FieldInfo field,string key,float unit)
        {
            if(type==null)return;foreach(var method in type.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Where(m=>m.Name==name)){
                if(!methods.TryGetValue(method,out var list))methods[method]=list=new List<int>();
                if(list.Any(i=>rules[i].Field==field))continue;list.Add(rules.Count);rules.Add(new Rule{Config=c,Field=field,Key=key,Unit=unit});
            }
        }
        internal static void Install(){foreach(var target in methods.Keys)U.Harmony.Patch(target,transpiler:new HarmonyMethod(typeof(HexFieldOptions),nameof(IL)));}
        static float F(float native,int id){var r=rules[id];return r.Config.Values.Active?r.Config.Values.F(r.Key)/r.Unit:native;}
        static int I(int native,int id)=>Mathf.RoundToInt(F(native,id));
        static uint W(uint native,int id)=>(uint)Mathf.Max(0,Mathf.RoundToInt(F(native,id)));
        static IEnumerable<CodeInstruction> IL(IEnumerable<CodeInstruction> input,MethodBase __originalMethod)
        {
            foreach(var ins in input){yield return ins;if(ins.opcode!=OpCodes.Ldsfld&&ins.opcode!=OpCodes.Ldfld)continue;
                foreach(int id in methods[__originalMethod])if(Equals(ins.operand,rules[id].Field)){
                    yield return new CodeInstruction(OpCodes.Ldc_I4,id);yield return new CodeInstruction(OpCodes.Call,A.Method(typeof(HexFieldOptions),rules[id].Field.FieldType==typeof(float)?nameof(F):rules[id].Field.FieldType==typeof(uint)?nameof(W):nameof(I)));break;
                }
            }
        }
    }
}
