using System;
using HarmonyLib;
using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexMechanics
    {
        internal static R Create(string name,string id)
        { U.Ensure(); return new R(name,id,true,"Hex"); }
        internal static void Patch(Type target,string method,Type patch,string prefix=null,string postfix=null,string finalizer=null)
        {
            var original=target==null?null:AccessTools.Method(target,method);
            if(original==null)throw new MissingMethodException(target?.FullName,method);
            U.Harmony.Patch(original,prefix:prefix==null?null:new HarmonyMethod(patch,prefix),postfix:postfix==null?null:new HarmonyMethod(patch,postfix),finalizer:finalizer==null?null:new HarmonyMethod(patch,finalizer));
        }
        internal static void Text(R c,Func<string> longText,Func<string> shortText)
        { c.ShowStat=key=>!c.Flags.ContainsKey("Rework") || (key.StartsWith("Vanilla ") != c.B("Rework")); c.Text(longText,shortText); }
    }
}
