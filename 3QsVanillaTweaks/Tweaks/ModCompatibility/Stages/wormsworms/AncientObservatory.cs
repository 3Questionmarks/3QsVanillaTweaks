using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class AncientObservatory
    {
        internal const string SceneName = "observatory_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Ancient Observatory";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S2 (Small Ruins Newt)",
                new Vector3(232.279100f, 167.741200f, 283.356100f), new Vector3(0.000000f, 226.481000f, 0.000000f));
            ModdedStageFeatures.RegisterScrapper(Section + " - Scrapper", SceneName, "Ancient Observatory Scrapper", new Vector3(0f, 30f, 0f));
        }
    }
}
