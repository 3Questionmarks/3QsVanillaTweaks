using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class NewtAltar
    {
        private const string Section = "Stages - Newt Altars";
        private const string HarmonyId = "3Questionmarks.3QsVanillaTweaks.Stages.NewtAltar";

        // A single setting controls every stage's behaviour.
        private enum SpawnMode { Vanilla, AtLeast1, OneSpecific }
        private static ConfigEntry<SpawnMode> mode;
        private static ConfigEntry<float> permanentChance;
        private static readonly Dictionary<string, ConfigEntry<string>> stages =
            new Dictionary<string, ConfigEntry<string>>(StringComparer.OrdinalIgnoreCase);

        internal sealed class AltarCandidate
        {
            internal GameObject gameObject;
            internal string letter;
            internal Vector3 position;
            internal string hierarchy;
        }

        internal static void Init()
        {
            mode = ModConfig.Bind(Section, "Spawn Mode", SpawnMode.OneSpecific,
                "Vanilla: never modify Newt Altar spawns. At Least 1: ensure one altar is active if none spawned. " +
                "1 Specific: ensure the stage's selected letter is active while leaving other altar locations to vanilla. " +
                "Dropdown values: Vanilla, AtLeast1, OneSpecific.");

            permanentChance = ModConfig.Bind(Section, "Extra Permanent Altar Chance", 0.25f,
                "Chance (0.0 to 1.0) to keep an additional, vanilla-permanent altar when OneSpecific guarantees a different position. Does not affect the preferred altar or ordinary vanilla-random altars.");

            Add("golemplains", "Titanic Plains", "B");
            Add("golemplains2", "Titanic Plains (Alternate)", "D");
            Add("blackbeach", "Distant Roost", "A");
            Add("blackbeach2", "Distant Roost (Alternate)", "A");
            Add("snowyforest", "Siphoned Forest", "B");
            Add("lakes", "Verdant Falls", "C");
            Add("lakesnight", "Viscous Falls", "B");
            Add("village", "Shattered Abode", "A");
            Add("villagenight", "Disturbed Impact", "D");
            Add("goolake", "Abandoned Aqueduct", "C");
            Add("foggyswamp", "Wetland Aspect", "B");
            Add("ancientloft", "Aphelian Sanctuary", "C");
            Add("pretendersprecipice", "Pretenders Precipice", "B");
            Add("frozenwall", "Rallypoint Delta", "C");
            Add("wispgraveyard", "Scorched Acres", "C");
            Add("sulfurpools", "Sulfur Pools", "C");
            Add("ironalluvium", "Iron Alluvium", "A");
            Add("ironauroras", "Iron Auroras", "A");
            Add("dampcaverns", "Damp Caverns", "B");
            Add("tenebrousquagmire", "Tenebrous Quagmire", "A");
            Add("dampcavesimple", "Abyssal Depths", "C");
            Add("shipgraveyard", "Sirens Call", "A");
            Add("rootjungle", "Sundered Grove", "B");
            Add("repurposedcrater", "Repurposed Crater", "C");
            Add("luridbasilica", "Lurid Basilica", "B");
            Add("skymeadow", "Sky Meadow", "B");
            Add("helminthroost", "Helminth Hatchery", "D");
            Add("lemuriantemple", "Reformed Altar", "D");
            Add("habitat", "Treeborn Colony", "A");
            Add("habitatfall", "Golden Dieback", "A");

            // Stage.Start can occur before NetworkServer.active becomes true (and
            // with some stage swappers it may not run for the expected scene).
            // A persistent, server-side poller works independently of stage start timing.
            var monitorObject = new GameObject("3Qs Newt Altar Scene Monitor");
            UnityEngine.Object.DontDestroyOnLoad(monitorObject);
            monitorObject.AddComponent<SceneMonitor>();
            ThreeQsVanillaTweaks.Log.Info("Newt Altar: persistent stage monitor installed.");
        }

        private sealed class SceneMonitor : MonoBehaviour
        {
            private float nextCheck;
            private readonly HashSet<int> completedHandles = new HashSet<int>();
            private readonly HashSet<int> reportedWaits = new HashSet<int>();

            private void Update()
            {
                if (Time.unscaledTime < nextCheck) return;
                nextCheck = Time.unscaledTime + 0.5f;

                // Reusing a scene handle after an unload must not block a new run.
                var loadedHandles = new HashSet<int>();
                for (int i = 0; i < SceneManager.sceneCount; ++i)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.IsValid() || !scene.isLoaded || !stages.ContainsKey(scene.name)) continue;
                    loadedHandles.Add(scene.handle);
                    if (completedHandles.Contains(scene.handle)) continue;

                    if (ModConfig.Get(mode) == SpawnMode.Vanilla)
                    {
                        if (reportedWaits.Add(scene.handle))
                            ThreeQsVanillaTweaks.Log.Info("Newt Altar: '" + scene.name + "' uses Vanilla mode; no changes.");
                        completedHandles.Add(scene.handle);
                        continue;
                    }
                    if (Tweaks.DebugStuff.NewtAltarLocations.DisableGuaranteesDuringMapping)
                    {
                        if (reportedWaits.Add(scene.handle))
                            ThreeQsVanillaTweaks.Log.Warning("Newt Altar: '" + scene.name + "' guarantee DISABLED by debug mapping setting.");
                        continue;
                    }
                    if (!NetworkServer.active)
                    {
                        if (reportedWaits.Add(scene.handle))
                            ThreeQsVanillaTweaks.Log.Info("Newt Altar: awaiting NetworkServer on '" + scene.name + "'.");
                        continue;
                    }
                    if (GetCandidates(scene).Count == 0) continue;

                    completedHandles.Add(scene.handle);
                    ThreeQsVanillaTweaks.Log.Info("Newt Altar: starting stage presence check on '" + scene.name + "'.");
                    StartCoroutine(CheckAltars(scene));
                }
                completedHandles.RemoveWhere(h => !loadedHandles.Contains(h));
                reportedWaits.RemoveWhere(h => !loadedHandles.Contains(h));
            }
        }

        private static IEnumerator CheckAltars(Scene scene)
        {
            // Do not alter preplaced spawn selection before the stage finishes setting up.
            yield return new WaitForSeconds(0.5f);
            if (!NetworkServer.active || !scene.IsValid() || !scene.isLoaded) yield break;
            if (ModConfig.Get(mode) == SpawnMode.Vanilla ||
                Tweaks.DebugStuff.NewtAltarLocations.DisableGuaranteesDuringMapping) yield break;

            List<AltarCandidate> candidates = GetCandidates(scene);
            for (int i = 0; candidates.Count == 0 && i < 12; ++i)
            {
                yield return new WaitForSeconds(0.5f);
                if (!scene.IsValid() || !scene.isLoaded) yield break;
                candidates = GetCandidates(scene);
            }
            if (candidates.Count == 0)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt Altar: no candidate positions in scene '" + scene.name + "'.");
                yield break;
            }

            SpawnMode currentMode = ModConfig.Get(mode);
            string wanted = (ModConfig.Get(stages[scene.name]) ?? "").Trim().ToUpperInvariant();
            AltarCandidate preferred = candidates.FirstOrDefault(c => c.letter == wanted);
            int present = candidates.Count(c => IsPresent(c));
            ThreeQsVanillaTweaks.Log.Info("Newt Altar: scene='" + scene.name + "', mode=" + currentMode +
                ", preferred=" + wanted + ", discovered=" + candidates.Count + ", present=" + present + ".");

            if (currentMode == SpawnMode.AtLeast1)
            {
                if (present == 0)
                {
                    AltarCandidate pick = candidates[UnityEngine.Random.Range(0, candidates.Count)];
                    Quaternion originalRotation = pick.gameObject.transform.localRotation;
                    EnsureSpawned(pick, scene);
                    yield return RestoreSceneRotation(pick, originalRotation, scene);
                }
                yield break;
            }

            if (currentMode != SpawnMode.OneSpecific || wanted.Length == 0) yield break;
            if (preferred == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt Altar: preferred '" + wanted +
                    "' not found on '" + scene.name + "'. Found: " +
                    string.Join(",", candidates.Select(c => c.letter).ToArray()));
                yield break;
            }

            if (!IsPresent(preferred))
            {
                Quaternion originalRotation = preferred.gameObject.transform.localRotation;
                EnsureSpawned(preferred, scene);
                yield return RestoreSceneRotation(preferred, originalRotation, scene);
            }
            else ThreeQsVanillaTweaks.Log.Info("Newt Altar: preferred '" + wanted + "' already present; no duplicate spawned.");

            // Ordinary randomly selected vanilla altars remain unchanged. A permanently
            // active altar outside the scene's toggle groups is special: if it is an
            // additional location, let it remain only after a separate chance roll.
            float chance = Mathf.Clamp01(ModConfig.Get(permanentChance));
            HashSet<GameObject> grouped = GetToggleGroupAltarObjects(scene);
            foreach (AltarCandidate other in candidates)
            {
                if (other == preferred || !IsPresent(other) || grouped.Contains(other.gameObject)) continue;
                bool keep = UnityEngine.Random.value < chance;
                ThreeQsVanillaTweaks.Log.Info("Newt Altar: extra permanent '" + other.letter +
                    "' on '" + scene.name + "' rolled " + (keep ? "keep" : "remove") +
                    " (chance " + chance.ToString("P0", CultureInfo.InvariantCulture) + ").");
                if (!keep) other.gameObject.SetActive(false);
            }
        }

        private static bool IsPresent(AltarCandidate candidate)
        {
            // This matches the debug scanner's active flag. Merely having an inactive
            // scene location object available is NOT considered an existing spawn.
            return candidate != null && candidate.gameObject != null && candidate.gameObject.activeInHierarchy;
        }

        private static void EnsureSpawned(AltarCandidate candidate, Scene scene)
        {
            GameObject altar = candidate.gameObject;
            if (altar == null) return;
            // Preplaced NewtStatue is itself the complete scene altar prefab. Activating
            // that exact position avoids moving or duplicating unrelated objects.
            altar.SetActive(true);
            if (!altar.activeInHierarchy)
            {
                ThreeQsVanillaTweaks.Log.Warning("Newt Altar: '" + candidate.letter +
                    "' cannot activate because its parent is inactive: " + candidate.hierarchy);
                return;
            }
            NetworkIdentity identity = altar.GetComponent<NetworkIdentity>();
            if (identity != null && identity.netId.Value == 0u)
            {
                try
                {
                    NetworkServer.Spawn(altar);
                    ThreeQsVanillaTweaks.Log.Info("Newt Altar: requested network spawn for '" + candidate.letter + "'.");
                }
                catch (Exception ex)
                {
                    ThreeQsVanillaTweaks.Log.Warning("Newt Altar: network spawn of '" +
                        candidate.letter + "' failed: " + ex);
                }
            }
            ThreeQsVanillaTweaks.Log.Info("Newt Altar: ensured '" + candidate.letter + "' at " +
                candidate.position.ToString("F2") + " on '" + scene.name +
                "', active=" + altar.activeInHierarchy +
                ", hasNetworkIdentity=" + (identity != null) + ".");
        }

        // Preserve the orientation authored into the stage scene. Enabling an inactive
        // NewtStatue can run OnEnable/Start code which may alter its transform; unlike
        // the stage director, our guarantee bypasses the normal placement process.
        // Restore only altars we activated, never a naturally spawned vanilla altar.
        private static IEnumerator RestoreSceneRotation(AltarCandidate candidate, Quaternion originalLocalRotation, Scene scene)
        {
            GameObject altar = candidate.gameObject;
            if (altar == null || !altar.activeInHierarchy) yield break;

            altar.transform.localRotation = originalLocalRotation;
            // Start() executes before the first Update following SetActive(true).
            yield return null;
            if (altar == null || !scene.IsValid() || !scene.isLoaded || !altar.activeInHierarchy) yield break;

            Quaternion beforeRestore = altar.transform.localRotation;
            float correction = Quaternion.Angle(beforeRestore, originalLocalRotation);
            altar.transform.localRotation = originalLocalRotation;
            ThreeQsVanillaTweaks.Log.Info("Newt Altar: rotation for '" + candidate.letter +
                "' on '" + scene.name + "' restored to scene-authored local rotation " +
                originalLocalRotation.eulerAngles.ToString("F1") +
                " (initialisation deviation " + correction.ToString("F1", CultureInfo.InvariantCulture) + " degrees).");
        }

        private static HashSet<GameObject> GetToggleGroupAltarObjects(Scene scene)
        {
            var output = new HashSet<GameObject>();
            foreach (SceneObjectToggleGroup toggle in Resources.FindObjectsOfTypeAll<SceneObjectToggleGroup>())
            {
                if (toggle == null || toggle.gameObject.scene != scene || toggle.toggleGroups == null) continue;
                foreach (var group in toggle.toggleGroups)
                {
                    if (group.objects == null) continue;
                    foreach (GameObject obj in group.objects)
                        if (IsAltar(obj)) output.Add(obj);
                }
            }
            return output;
        }

        private static void Add(string scene, string displayName, string letter)
        {
            string safe = displayName;
            foreach (char invalid in new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']' })
                safe = safe.Replace(invalid.ToString(), "");
            stages[scene] = ModConfig.Bind(Section, safe + " - Preferred Letter", letter,
                "Diagnostic letter of the Newt Altar to guarantee when Spawn Mode is 1 Specific. " +
                "Use Debug > Newt Altar - Show Location Markers to identify positions. " +
                "Letters can change if the game's stage objects change; blank disables the specific guarantee for this stage.");
        }

        private static bool IsAltar(GameObject obj)
        {
            if (obj == null) return false;
            string n = obj.name;
            return n.StartsWith("NewtStatue", StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith("NewtAltar", StringComparison.OrdinalIgnoreCase) ||
                   n.StartsWith("Newt Altar", StringComparison.OrdinalIgnoreCase);
        }

        private static string Hierarchy(Transform transform)
        {
            var list = new List<string>();
            for (Transform t = transform; t != null; t = t.parent) list.Add(t.name);
            list.Reverse();
            return String.Join("/", list.ToArray());
        }

        private static string Letter(int index)
        {
            string value = "";
            do
            {
                value = (char)('A' + index % 26) + value;
                index = index / 26 - 1;
            } while (index >= 0);
            return value;
        }

        // Single shared source of truth for ordering between the debug overlay and stage guarantees.
        internal static List<AltarCandidate> GetCandidates(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return new List<AltarCandidate>();
            var objects = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(obj => obj != null && obj.scene == scene && IsAltar(obj))
                .OrderBy(obj => Hierarchy(obj.transform), StringComparer.Ordinal)
                .ThenBy(obj => obj.transform.position.x)
                .ThenBy(obj => obj.transform.position.z)
                .ToArray();

            var output = new List<AltarCandidate>();
            var positions = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameObject obj in objects)
            {
                Vector3 p = obj.transform.position;
                string key = p.x.ToString("F2", CultureInfo.InvariantCulture) + "," +
                             p.y.ToString("F2", CultureInfo.InvariantCulture) + "," +
                             p.z.ToString("F2", CultureInfo.InvariantCulture);
                if (!positions.Add(key)) continue;
                output.Add(new AltarCandidate
                {
                    gameObject = obj,
                    letter = Letter(output.Count),
                    position = p,
                    hierarchy = Hierarchy(obj.transform)
                });
            }
            return output;
        }
    }
}
