using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.Winslow
{
    internal static class StagnantPipeworks
    {
        internal const string SceneName = "swampybog_winslow";
        private const string Section = "Mod Compatibility - Stages - Stagnant Pipeworks";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S3 (Newt (3))",
                new Vector3(307.246200f, 53.875720f, -21.535790f), new Vector3(351.111800f, 73.570960f, 355.446300f));
        }
    }
}
