using UnityEngine;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class MidnightTropics
    {
        internal const string SceneName = "tropicsnight_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Midnight Tropics";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S3 (Ramp Newt)",
                new Vector3(6.765884f, 1.297802f, 175.687300f), new Vector3(0.000000f, 284.130700f, 0.000000f));
        }
    }
}
