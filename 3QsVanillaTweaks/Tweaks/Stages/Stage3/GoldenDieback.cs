using UnityEngine;
namespace Tweaks.Stages.Stage3
{
    internal static class GoldenDieback
    {
        internal static void Init()
        {
            CloakedChestSpawnHelper.Register("habitatfall", "Golden Dieback",
                new Vector3(-66.06f, 106.01f, 1.82f), new Vector3(-13.66f, 73.96f, 145.97f),
                new Vector3(131.97f, 112.49f, -90.41f), new Vector3(164.8f, 72.97f, 59.32f),
                new Vector3(34.28f, 73.76f, -162.41f));
        }
    }
}
