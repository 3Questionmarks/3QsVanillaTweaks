using System;
using HarmonyLib;
using RoR2;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexOfAcceleration
    {
        static HexConfig c;
        internal static void Init()=>HexConfigSupport.Register("SlipperyGround",Configure);
        static void Configure(HexConfig settings)
        {
            c=settings;
            c.Pair("Acceleration Penalty (%)",40,30,"Vanilla acceleration reduction; retains vanilla stacking.");
            U.Stats+=Stats;
        }
        static float ConVar(string name)
        {var value=AccessTools.Field(typeof(CharacterBody),name).GetValue(null);return Convert.ToSingle(AccessTools.Property(value.GetType(),"value")?.GetValue(value)??AccessTools.Field(value.GetType(),"value").GetValue(value));}
        static void Stats(CharacterBody body)
        {
            int count=c.Values.Count(body);if(count<=0||body.moveSpeed==0)return;
            float native=Util.ConvertAmplificationPercentageIntoReductionNormalizedInverted(count*ConVar("slipperyHexMult")-ConVar("slipperyHexSub"));
            float desired=Util.ConvertAmplificationPercentageIntoReductionNormalizedInverted(c.Values.Total("Acceleration Penalty (%)",count)/100);
            if(native>0)body.acceleration*=desired/native;
        }
    }
}
