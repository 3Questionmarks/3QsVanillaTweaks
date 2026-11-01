using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal sealed class EquipmentConfig
    {
        static bool loaded;
        static EquipmentConfig(){RoR2Application.onLoad+=()=>loaded=true;}
        internal static void MarkLoaded(){loaded=true;}
        internal static void Defer(Action action){if(loaded)action();else RoR2Application.onLoad+=action;}
        internal readonly R Values;
        internal readonly string Id;
        internal bool Active=>Values.Active;
        internal EquipmentDef Def=>EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex(Id));
        internal EquipmentConfig(string name,string id,bool enabled=true,float? cooldown=null)
        {
            Id=id;Values=new R(name,"",enabled,"Equipment");
            Defer(()=>{
                var d=Def;if(!d)return;
                float old=d.cooldown;N("Cooldown",cooldown??old,"Base cooldown in seconds. Vanilla: "+old.ToString("0.##",CultureInfo.InvariantCulture)+".");
                Action apply=()=>{d.cooldown=Active?F("Cooldown"):old;};apply();C.EffectiveSettingsChanged+=apply;
            });
        }
        internal float F(string k)=>Values.F(k);
        internal bool B(string k)=>Values.B(k);
        internal void N(string k,float v,string desc,float min=0,float max=100000)=>Values.N(k,v,desc,min,max);
        internal void Flag(string k,bool v,string desc)=>Values.B(k,v,desc);
        internal string V(string k,string unit="",string style="cIsDamage")=>Values.V(k,unit,style);
        internal void Literal(string method,float native,string key,float unit=1,int occurrence=0)=>Values.Literal("RoR2.EquipmentSlot",method,native,()=>F(key)/unit,occurrence:occurrence);
        internal void Text(Func<string> description,Func<string> pickup)
        {
            if(!Values.Enabled.Value)return;
            Defer(()=>{
                var d=Def;if(!d)return;string desc=d.descriptionToken,pick=d.pickupToken,oldDesc=Language.GetString(desc),oldPick=Language.GetString(pick);
                string newDesc="3QS_EQUIPMENT_"+Id.ToUpperInvariant()+"_DESC",newPick="3QS_EQUIPMENT_"+Id.ToUpperInvariant()+"_PICKUP";
                S.Text(desc,()=>Active?description():oldDesc);S.Text(pick,()=>Active?pickup():oldPick);
                S.Text(newDesc,()=>Active?description():oldDesc);S.Text(newPick,()=>Active?pickup():oldPick);d.descriptionToken=newDesc;d.pickupToken=newPick;
            });
        }
        internal void Patch(Type type,string method,Type owner,string prefix=null,string postfix=null)
        {if(Values.Enabled.Value)U.Patch(type,method,owner,prefix,postfix);}
    }
}
