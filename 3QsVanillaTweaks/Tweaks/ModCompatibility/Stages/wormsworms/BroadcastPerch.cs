using UnityEngine;
using Tweaks.Stages.Stage3;

namespace Tweaks.ModCompatibility.Stages.wormsworms
{
    internal static class BroadcastPerch
    {
        internal const string SceneName = "broadcastperch_wormsworms";
        private const string Section = "Mod Compatibility - Stages - Broadcast Perch";

        internal static void Init()
        {
            ModdedStageFeatures.RegisterNewt(Section, SceneName, "S2 (Rescue Newt)",
                new Vector3(26.774750f, 198.502300f, 78.456540f), new Vector3(349.022000f, 223.456100f, 348.955900f));
            Vector3 forward = Quaternion.Euler(0f, 41.95f, 0f) * Vector3.forward;
            IronAlluvium.RegisterChest(Section + " - Command Chest", SceneName, "Broadcast Perch",
                new Vector3(-122.06f, 132.49f, -92.78f), forward.x, forward.z, requiresDlc3: false, exactPose: true);
        }
    }
}
