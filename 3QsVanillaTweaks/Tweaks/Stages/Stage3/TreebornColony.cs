using UnityEngine;
namespace Tweaks.Stages.Stage3
{
    internal static class TreebornColony
    {
        internal static void Init()
        {
            CloakedChestSpawnHelper.Register("habitat", "Treeborn Colony",
                new Vector3(-5.6f, 84.32f, -90.28f), new Vector3(71.06f, 51.93f, -142.09f),
                new Vector3(116.19f, 99.96f, 26.33f), new Vector3(-43.60f, 34.35f, 129.27f));
        }
    }
}
