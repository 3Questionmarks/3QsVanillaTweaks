namespace Tweaks.Items.Hex
{
    internal static class HexOfLeaping
    {
        internal static void Init()=>HexConfigSupport.Register("WeakAssHamstring",Configure);
        static void Configure(HexConfig c)
        {
            c.Literal("RoR2.Items.WeakAssHamstringBehaviour","HandleJump",25,"Jump Power Lost Per Jump (%)");
            c.Literal("RoR2.Items.WeakAssHamstringBehaviour","GetTimeUntilJumpsAreRestored",6,"Recovery Time Per Stack");
        }
    }
}
