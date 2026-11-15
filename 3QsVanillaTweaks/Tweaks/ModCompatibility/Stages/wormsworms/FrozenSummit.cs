using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class FrozenSummit
    {
        internal const string SceneName = "hollowsummitnight_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Frozen Summit";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S2 (Cave Newt)",
                new Vector3(40.222930f, 44.725320f, -101.983300f), new Vector3(322.771500f, 101.092400f, 353.235700f));
            ModdedStageFeatures.RegisterScrapper(Section + " - Scrapper", SceneName, "Frozen Summit Scrapper", new Vector3(-82.21f, 193.51f, -3.02f));
        }
    }
}
