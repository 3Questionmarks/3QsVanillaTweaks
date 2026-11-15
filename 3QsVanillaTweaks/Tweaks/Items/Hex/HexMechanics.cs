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
        // Convert each configured percentage to odds before stacking, preserving
        // the first item's exact effect while keeping later penalties below 100%.
        internal static float DiminishingPercent(float first, float extra, int count)
        {
            if (count <= 0) return 0f;
            double a = Math.Max(0d, Math.Min(99.99d, first));
            double b = Math.Max(0d, Math.Min(99.99d, extra));
            double odds = a / (100d - a) + (count - 1d) * b / (100d - b);
            return (float)(100d * odds / (1d + odds));
        }
        internal static float Penalty(R config, string key, int count) =>
            DiminishingPercent(config.F(key), config.F(key + " Per Stack"), count);
        internal static string PenaltyText(R config, string key) =>
            "<style=cDeath>" + config.F(key).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%</style>" +
            (config.F(key + " Per Stack") > 0 ? " <style=cStack>(diminishing with additional stacks)</style>" : "");
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
