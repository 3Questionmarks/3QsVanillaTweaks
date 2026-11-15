using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class HollowSummit
    {
        internal const string SceneName = "hollowsummit_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Hollow Summit";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S3 (Spiky Newt)",
                new Vector3(68.362930f, 148.992300f, 89.076680f), new Vector3(6.474761f, 342.979000f, 341.173200f));
            ModdedStageFeatures.RegisterScrapper(Section + " - Scrapper", SceneName, "Hollow Summit Scrapper", new Vector3(-82.21f, 193.51f, -3.02f));
        }
    }
}
