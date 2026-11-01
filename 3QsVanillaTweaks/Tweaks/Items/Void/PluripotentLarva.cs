using RoR2;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Void
{
    internal static class PluripotentLarva
    {
        static R c;
        internal static bool Enabled => c != null && c.Active;
        internal static void Init()
        {
            U.Ensure();
            // Retain the existing section so saved Enabled values are preserved.
            c = new R("Pluripotent Larva", "ExtraLifeVoid", true, "06 Void");
            c.N("Invulnerability Duration", 3, "Seconds of invulnerability after revival. Vanilla: 3.");
            c.Literal("RoR2.CharacterMaster", "RespawnExtraLifeVoid", 3f, () => c.F("Invulnerability Duration"), "Immune", 0, 12);
            c.Text(() => "Upon death, this item will be <style=cIsUtility>consumed</style> and you will return to life with <style=cIsUtility>" + c.F("Invulnerability Duration").ToString("0.##") + " seconds of invulnerability</style>, and all of your items that can be corrupted will be." + (PluripotentLarvaConsumed.RestoreEnabled ? " <style=cIsHealing>Regenerates at the start of each stage.</style>" : ""),
                () => "Revive on death and <style=cIsVoid>corrupt items</style>." + (PluripotentLarvaConsumed.RestoreEnabled ? " <style=cIsHealing>Regenerates each stage.</style>" : ""));
        }
    }
}
