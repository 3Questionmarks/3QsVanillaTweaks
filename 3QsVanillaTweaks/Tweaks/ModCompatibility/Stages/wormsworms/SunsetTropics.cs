using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class SunsetTropics
    {
        internal const string SceneName = "tropics_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Sunset Tropics";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S4 (Conch Newt)",
                new Vector3(116.736100f, 57.607800f, 104.147300f), new Vector3(22.431670f, 22.311580f, 359.356000f));
        }
    }
}
