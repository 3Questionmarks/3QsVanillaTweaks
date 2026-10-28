using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Tweaks.Stages
{
    // Catalogue lookups avoid hard-coding DLC asset directories or newer controller types.
    internal static class StageInteractableAssets
    {
        private static readonly Dictionary<string, SpawnCard> Cards = new Dictionary<string, SpawnCard>();
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();

        internal static SpawnCard Card(string assetFragment, string componentName = null)
        {
            string cacheKey = assetFragment + "|" + componentName;
            if (Cards.TryGetValue(cacheKey, out SpawnCard cached) && cached) return cached;
            Type componentType = componentName == null ? null : typeof(Run).Assembly.GetType(componentName);
            foreach (var locator in Addressables.ResourceLocators)
                foreach (object key in locator.Keys)
                {
                    string address = key as string;
                    if (address == null || !address.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) continue;
                    string basename = address.Substring(address.LastIndexOf('/') + 1);
                    if (!basename.StartsWith("isc", StringComparison.OrdinalIgnoreCase) ||
                        basename.IndexOf(assetFragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    SpawnCard card = Addressables.LoadAssetAsync<SpawnCard>(key).WaitForCompletion();
                    if (!card || !card.prefab || (componentName != null &&
                        (componentType == null || !card.prefab.GetComponentInChildren(componentType, true)))) continue;
                    Cards[cacheKey] = card;
                    return card;
                }
            throw new InvalidOperationException("Could not find an interactable spawn card matching " + assetFragment + ".");
        }

        internal static GameObject Interactable(string fragment, string componentName)
        {
            try { return Card(fragment, componentName).prefab; }
            catch (InvalidOperationException) { /* Some fixed interactables have no director spawn card. */ }
            Type component = typeof(Run).Assembly.GetType(componentName);
            if (component == null) throw new InvalidOperationException("Controller unavailable: " + componentName);
            foreach (var locator in Addressables.ResourceLocators)
                foreach (object key in locator.Keys)
                {
                    string address = key as string;
                    if (address == null || !address.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                    string basename = address.Substring(address.LastIndexOf('/') + 1);
                    if (basename.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    GameObject prefab = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
                    if (prefab && prefab.GetComponentInChildren(component, true)) return prefab;
                }
            throw new InvalidOperationException("Could not find interactable " + fragment + ".");
        }

        internal static GameObject Prefab(string basename)
        {
            if (Prefabs.TryGetValue(basename, out GameObject cached) && cached) return cached;
            string suffix = "/" + basename + ".prefab";
            foreach (var locator in Addressables.ResourceLocators)
                foreach (object key in locator.Keys)
                {
                    string address = key as string;
                    if (address == null || !address.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                    GameObject prefab = Addressables.LoadAssetAsync<GameObject>(key).WaitForCompletion();
                    if (prefab) { Prefabs[basename] = prefab; return prefab; }
                }
            throw new InvalidOperationException("Could not find " + basename + ".prefab in the address catalogue.");
        }
    }
}
