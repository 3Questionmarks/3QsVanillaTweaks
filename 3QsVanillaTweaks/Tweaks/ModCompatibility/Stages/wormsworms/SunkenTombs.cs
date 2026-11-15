using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class SunkenTombs
    {
        internal const string SceneName = "sunkentombs_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Sunken Tombs";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S1 (Kelp Newt)",
                new Vector3(-45.010130f, 11.580540f, -5.904318f), new Vector3(0.000000f, 53.109180f, 0.000000f));
        }
    }
}
