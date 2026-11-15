using UnityEngine;
using Tweaks.Stages.Stage3;

namespace Tweaks.ModCompatibility.Stages.JaceDaDorito
{
    internal static class FogboundLagoon
    {
        internal const string SceneName = "FBLScene";
        private const string Section = "Mod Compatibility - Stages - Fogbound Lagoon";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S4 (NewtStatue4)",
                new Vector3(211.233900f, 268.295700f, 411.020900f), new Vector3(3.479926f, 9.146982f, 2.849785f));
            RallypointDelta.RegisterScene(SceneName);
        }
    }
}
