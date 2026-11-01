namespace Tweaks.Items.Hex
{
    internal static class HexOfFamine
    {
        internal static void Init()=>HexConfigSupport.Register("LowerHealing",Configure);
        static void Configure(HexConfig c)
        {
            c.Pair("Healing Reduction (%)",40,40,"Healing and regeneration penalty; retains vanilla stacking.");
            c.Values.Literal("RoR2.Items.LowerHealingUtils","GetLowerHealingCoefficient",40,()=>c.Values.F("Healing Reduction (%)"),occurrence:0);
            c.Values.Literal("RoR2.Items.LowerHealingUtils","GetLowerHealingCoefficient",40,()=>c.Values.F("Healing Reduction (%) Per Stack"),occurrence:1);
        }
    }
}
