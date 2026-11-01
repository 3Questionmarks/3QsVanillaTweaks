namespace Tweaks.Items.Hex
{
    internal static class HexOfPain
    {
        internal static void Init()=>HexConfigSupport.Register("HexCurse",Configure);
        static void Configure(HexConfig c)
        {c.Count("RoR2.CharacterBody","RecalculateStats","HexCurse",.3f,"Health Curse (%)",100);}
    }
}
