using HarmonyLib;
using RoR2;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Bazaar
{
    // Using any Lunar Seer closes the others, including the extra seer this mod adds.
    // The native seers are linked in the scene; the added seer is not part of that link.
    internal static class BazaarSeerExclusive
    {
        internal static void Init()
        {
            var target = A.Method(typeof(SeerStationController), "SetRunNextStageToTarget");
            if (target == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Bazaar seers: SeerStationController.SetRunNextStageToTarget not found; seers are not linked.");
                return;
            }
            new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.SeerExclusive").Patch(target,
                postfix: new HarmonyMethod(typeof(BazaarSeerExclusive), nameof(AfterSeerUsed)));
        }

        private static void AfterSeerUsed(SeerStationController __instance)
        {
            if (!NetworkServer.active || !__instance) return;
            foreach (var seer in UnityEngine.Object.FindObjectsOfType<SeerStationController>())
            {
                if (!seer || seer == __instance) continue;
                var purchase = seer.GetComponent<PurchaseInteraction>();
                if (purchase && purchase.available) purchase.SetAvailable(false);
            }
        }
    }
}
