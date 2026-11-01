using RoR2;
using UnityEngine;
using U=Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Hex
{
    internal static class HexOfBlindness
    {
        static HexConfig c;
        internal static void Init()=>HexConfigSupport.Register("Blindness",Configure);
        static void Configure(HexConfig settings)
        {
            c=settings;
            c.Number("Maximum Vision Distance",100,"Vision radius at one stack, in metres.");
            c.Number("Vision Divisor Per Additional Stack",2,"Each additional stack divides vision range by this value.");
            U.Stats+=Stats;
        }
        static void Stats(CharacterBody body)
        {
            int count=c.Values.Count(body);if(count<=0)return;
            float native=Mathf.Min(100,body.baseVisionDistance)/Mathf.Pow(2,count-1);
            float desired=Mathf.Min(c.Values.F("Maximum Vision Distance"),body.baseVisionDistance)/Mathf.Pow(Mathf.Max(.01f,c.Values.F("Vision Divisor Per Additional Stack")),count-1);
            if(body.HasBuff(DLC1Content.Buffs.Blinded)){native=Mathf.Min(native,15);desired=Mathf.Min(desired,15);}
            body.visionDistance+=desired-native;
        }
    }
}
