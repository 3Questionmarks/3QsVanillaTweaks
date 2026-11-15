using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class DesolateCoast
    {
        internal const string SceneName = "coast_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Desolate Coast";
        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S1 (Cave Pillar Newt)",
                new Vector3(5.870251f, -1.815239f, -230.282700f), new Vector3(0.000000f, 167.137700f, 0.000000f));
            ModConfig.ReadRetiredValue(Section, "Extra Interactable Credits", 0);
            ModConfig.ReadRetiredValue(Section, "Scale Bonus With Stage Budget", false);
        }
    }
}
