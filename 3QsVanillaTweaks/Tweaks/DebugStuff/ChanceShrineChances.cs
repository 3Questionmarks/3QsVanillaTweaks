using System.Globalization;
using BepInEx.Configuration;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class ChanceShrineChances
    {
        private static ConfigEntry<bool> enabled;

        internal static void Init()
        {
            enabled = ModConfig.BindBool("Debug - Shrine of Chance", "Log Shrine Chances", true,
                "Log each Chance Shrine's reward chances before and after each purchase.", clientSide: true);
        }

        internal static void Record(GameObject shrine, int purchase, string phase, float[] weights)
        {
            if (!NetworkServer.active || !shrine || !ModConfig.Get(enabled)) return;
            Vector3 p = shrine.transform.position;
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "[Chance Shrine] scene={0} shrine={1} position=({2:0.##}, {3:0.##}, {4:0.##}) purchase={5} {6} | " +
                "Base chances: Nothing={7:0.###}%, Equipment={8:0.###}%, Common={9:0.###}%, Uncommon={10:0.###}%, Legendary={11:0.###}% (before Chance Doll or item replacements)",
                shrine.scene.name, shrine.GetInstanceID(), p.x, p.y, p.z, purchase, phase,
                weights[0], weights[1], weights[2], weights[3], weights[4]));
        }
    }
}
