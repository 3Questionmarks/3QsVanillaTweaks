using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.ExpansionManagement;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    // Shared stage-feature utilities. No Init(): the existing loader initialises each stage module.
    internal static class StageFeatureSpawnHelper
    {
        internal sealed class Settings
        {
            internal string SceneName, Name;
            internal ConfigEntry<bool> Enabled, SnapToGround, AlignToGround, SpawnWithCommand;
            internal ConfigEntry<float> Chance, X, Y, Z, ForwardX, ForwardZ, HeightOffset, GroundSearchDistance;
            internal Vector3 Position => new Vector3(ModConfig.Get(X), ModConfig.Get(Y), ModConfig.Get(Z));
        }

        private sealed class Feature
        {
            internal Settings Settings;
            internal Action<Settings, Xoroshiro128Plus> Spawn;
        }

        private static readonly List<Feature> Features = new List<Feature>();
        private static readonly HashSet<int> CompletedScenes = new HashSet<int>();
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, SpawnCard> SpawnCards = new Dictionary<string, SpawnCard>();
        private static readonly Dictionary<string, ExpansionDef> Expansions = new Dictionary<string, ExpansionDef>();
        private static bool registered;

        internal static Settings BindRandom(string section, string sceneName, string name)
        {
            return new Settings
            {
                SceneName = sceneName,
                Name = name,
                Enabled = ModConfig.BindBool(section, "Enabled", true, "Enable " + name + " changes."),
                Chance = Percent(section, "Spawn Chance (%)", 100f, "Chance to guarantee at least one " + name + ".")
            };
        }

        internal static Settings Bind(string section, string sceneName, string name,
            float x, float y, float z, float forwardX = 0f, float forwardZ = 1f, float heightOffset = 0f)
        {
            return new Settings
            {
                SceneName = sceneName,
                Name = name,
                Enabled = ModConfig.BindBool(section, "Enabled", true, "Enable this fixed stage feature on every visit to this map."),
                Chance = Percent(section, "Spawn Chance (%)", 100f, "Chance per visit: 0 never spawns; 100 always spawns."),
                X = ModConfig.Bind(section, "Position X", x, "World-space X position."),
                Y = ModConfig.Bind(section, "Position Y", y, "World-space Y position, also used to choose the nearest ground surface."),
                Z = ModConfig.Bind(section, "Position Z", z, "World-space Z position."),
                ForwardX = ModConfig.Bind(section, "Forward X", forwardX, "Horizontal facing vector X. Terrain alignment supplies the slope rotation."),
                ForwardZ = ModConfig.Bind(section, "Forward Z", forwardZ, "Horizontal facing vector Z. Both zero uses world forward."),
                SnapToGround = ModConfig.BindBool(section, "Snap To Ground", true, "Move the feature to the ground nearest the configured height."),
                AlignToGround = ModConfig.BindBool(section, "Align To Ground", true, "Tilt the feature to match the ground. Disable to keep it upright."),
                HeightOffset = ModConfig.Bind(section, "Height Offset", heightOffset, "World-space vertical offset added after ground snapping (or to Position Y when snapping is disabled)."),
                GroundSearchDistance = ModConfig.Bind(section, "Ground Search Distance", 12f,
                    new ConfigDescription("Search this far above and below Position Y for ground. The surface closest to Position Y is used to avoid snapping to overhead floors.",
                        new AcceptableValueRange<float>(0.1f, 100f)))
            };
        }

        internal static Settings BindPrinter(string section, string sceneName, string name,
            float x, float y, float z, float forwardX = 0f, float forwardZ = 1f, float heightOffset = 0f)
        {
            Settings settings = Bind(section, sceneName, name, x, y, z, forwardX, forwardZ, heightOffset);
            settings.SpawnWithCommand = ModConfig.BindBool(section, "Spawn With Command", false,
                "Allow this fixed printer to spawn while the Artifact of Command is enabled.");
            return settings;
        }

        internal static ConfigEntry<float> Percent(string section, string key, float value, string description)
        {
            return ModConfig.Bind(section, key, value,
                new ConfigDescription(description, new AcceptableValueRange<float>(0f, 100f)));
        }

        internal static bool Roll(float chance, Xoroshiro128Plus rng)
        {
            chance = Mathf.Clamp(chance, 0f, 100f);
            if (chance <= 0f || float.IsNaN(chance)) return false;
            return chance >= 100f || rng.nextNormalizedFloat < chance / 100f;
        }

        internal static void RegisterScene(string sceneName, string name, Action<Xoroshiro128Plus> spawn)
        {
            Register(new Settings { SceneName = sceneName, Name = name }, (settings, rng) => spawn(rng));
        }

        internal static void Register(Settings settings, Action<Settings, Xoroshiro128Plus> spawn)
        {
            Features.Add(new Feature { Settings = settings, Spawn = spawn });
            if (registered) return;
            registered = true;
            SceneDirector.onPostPopulateSceneServer += AfterPopulate;
            SceneManager.sceneUnloaded += scene => CompletedScenes.Remove(scene.handle);
        }

        private static void AfterPopulate(SceneDirector director)
        {
            if (!NetworkServer.active || !Run.instance || !director) return;
            Scene scene = director.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || CompletedScenes.Contains(scene.handle)) return;
            if (!Features.Exists(feature => feature.Settings.SceneName == scene.name)) return;
            if (!SpawnRequirements.SceneAllowed(SceneCatalog.FindSceneDef(scene.name))) return;
            CompletedScenes.Add(scene.handle);
            var rng = new Xoroshiro128Plus(Run.instance.stageRng.nextUlong);
            Physics.SyncTransforms();
            foreach (Feature feature in Features)
            {
                Settings settings = feature.Settings;
                if (settings.SceneName != scene.name || (settings.Enabled != null && !ModConfig.Get(settings.Enabled)) || (settings.Chance != null && !Roll(ModConfig.Get(settings.Chance), rng))) continue;
                try { feature.Spawn(settings, rng); }
                catch (Exception e) { Log.Warning("Stage feature failed: " + settings.Name + " on " + scene.name + ": " + e); }
            }
        }

        internal static bool IsStageOneMap()
        {
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            if (!scene) return false;
            switch (scene.baseSceneName)
            {
                case "blackbeach": case "blackbeach2": case "golemplains": case "golemplains2":
                case "snowyforest": case "lakes": case "lakesnight": case "village": case "villagenight": return true;
                default: return false;
            }
        }

        internal static bool ExpansionEnabled(ExpansionDef expansion)
        {
            return Run.instance && expansion && Run.instance.IsExpansionEnabled(expansion);
        }

        internal static bool ExpansionEnabled(string address)
        {
            if (!Run.instance) return false;
            if (!Expansions.TryGetValue(address, out ExpansionDef expansion))
            {
                try
                {
                    expansion = Addressables.LoadAssetAsync<ExpansionDef>(address).WaitForCompletion();
                    Expansions[address] = expansion;
                }
                catch (Exception e)
                {
                    Log.Warning("Stage feature could not load expansion " + address + ": " + e.Message);
                    return false;
                }
            }
            return ExpansionEnabled(expansion);
        }

        internal static bool PrefabAllowed(GameObject prefab, string address = null)
        {
            if (!NetworkServer.active || !Run.instance || !prefab) return false;
            if (address != null && !SpawnRequirements.AddressAllowed(address)) return false;
            foreach (ExpansionRequirementComponent requirement in prefab.GetComponentsInChildren<ExpansionRequirementComponent>(true))
                if (requirement.requiredExpansion && !ExpansionEnabled(requirement.requiredExpansion)) return false;
            return true;
        }

        internal static GameObject LoadPrefab(string address)
        {
            if (Prefabs.TryGetValue(address, out GameObject cached) && cached) return cached;
            GameObject prefab = Addressables.LoadAssetAsync<GameObject>(address).WaitForCompletion();
            if (!prefab) throw new InvalidOperationException("Prefab not found: " + address);
            Prefabs[address] = prefab;
            return prefab;
        }

        // Resolve the exact asset basename through the game's address catalogue instead of guessing DLC directories.
        internal static SpawnCard LoadSpawnCard(string assetName)
        {
            if (SpawnCards.TryGetValue(assetName, out SpawnCard cached) && cached) return cached;
            string suffix = "/" + assetName + ".asset";
            foreach (var locator in Addressables.ResourceLocators)
            {
                foreach (object key in locator.Keys)
                {
                    string address = key as string;
                    if (address == null || !(address.Equals(assetName, StringComparison.OrdinalIgnoreCase) ||
                        address.Equals(assetName + ".asset", StringComparison.OrdinalIgnoreCase) ||
                        address.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))) continue;
                    SpawnCard card = Addressables.LoadAssetAsync<SpawnCard>(key).WaitForCompletion();
                    if (!card || !card.prefab) continue;
                    SpawnCards[assetName] = card;
                    return card;
                }
            }
            throw new InvalidOperationException("Could not resolve " + assetName + ".asset in the address catalogue.");
        }

        internal static void GetPlacement(Settings settings, out Vector3 position, out Quaternion rotation)
        {
            GetPlacement(settings.Name, settings.Position,
                new Vector3(ModConfig.Get(settings.ForwardX), 0f, ModConfig.Get(settings.ForwardZ)),
                ModConfig.Get(settings.SnapToGround), ModConfig.Get(settings.AlignToGround),
                ModConfig.Get(settings.HeightOffset), ModConfig.Get(settings.GroundSearchDistance), out position, out rotation);
        }

        // Shared by fixed stage features and Bazaar additions, including offset lockboxes.
        internal static void GetPlacement(string name, Vector3 configuredPosition, Vector3 facing,
            bool snapToGround, bool alignToGround, float heightOffset, float groundSearchDistance,
            out Vector3 position, out Quaternion rotation)
        {
            Physics.SyncTransforms();
            position = configuredPosition;
            Vector3 up = Vector3.up;
            if (snapToGround || alignToGround)
            {
                float distance = Mathf.Clamp(groundSearchDistance, 0.1f, 100f);
                RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * distance, Vector3.down,
                    distance * 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
                int best = -1;
                float bestDistance = float.PositiveInfinity;
                for (int i = 0; i < hits.Length; ++i)
                {
                    // Ignore undersides and near-vertical walls; raycast ordering is unspecified.
                    if (hits[i].normal.y < 0.2f) continue;
                    float difference = Mathf.Abs(hits[i].point.y - position.y);
                    if (difference < bestDistance) { best = i; bestDistance = difference; }
                }
                if (best >= 0)
                {
                    if (snapToGround) position = hits[best].point;
                    if (alignToGround) up = hits[best].normal.normalized;
                }
                else Log.Warning("Interactable " + name + ": no ground near " + position + "; using the configured position and upright rotation.");
            }
            position += Vector3.up * heightOffset;
            Vector3 forward = new Vector3(facing.x, 0f, facing.z);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward = Vector3.ProjectOnPlane(forward, up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.ProjectOnPlane(Vector3.right, up);
            rotation = Quaternion.LookRotation(forward.normalized, up);
        }

        internal static GameObject Spawn(Settings settings, GameObject prefab, Action<GameObject> configure = null)
        {
            if (!PrefabAllowed(prefab)) return null;
            GetPlacement(settings, out Vector3 position, out Quaternion rotation);
            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                instance.name = "3Qs_StageFeature_" + settings.Name;
                configure?.Invoke(instance);
                NetworkServer.Spawn(instance);
                Log.Info("Stage feature spawned " + settings.Name + " on " + settings.SceneName + " at " + position);
                return instance;
            }
            catch
            {
                if (instance) UnityEngine.Object.Destroy(instance);
                throw;
            }
        }

        internal static void SpawnPrinter(Settings settings, string address, ItemTier tier)
        {
            if (SpawnRequirements.CommandActive && (settings.SpawnWithCommand == null || !ModConfig.Get(settings.SpawnWithCommand))) return;
            if (!SpawnRequirements.AddressAllowed(address) || !SpawnRequirements.PrinterTierAvailable(tier)) return;
            Spawn(settings, LoadPrefab(address), instance =>
            {
                ShopTerminalBehavior terminal = instance.GetComponent<ShopTerminalBehavior>();
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                if (!terminal || !purchase) throw new InvalidOperationException("Printer lacks its shop or purchase component.");
                terminal.dropTable = null;
                terminal.itemTier = tier;
                purchase.cost = 1;
                switch (tier)
                {
                    case ItemTier.Tier2: purchase.costType = CostTypeIndex.GreenItem; break;
                    case ItemTier.Tier3: purchase.costType = CostTypeIndex.RedItem; break;
                    case ItemTier.Boss: purchase.costType = CostTypeIndex.BossItem; break;
                    default: purchase.costType = CostTypeIndex.WhiteItem; break;
                }
            });
        }

        internal static void SpawnCompulsionShrine(Settings settings)
        {
            // Resolve the newer DLC types at runtime so this addition does not force
            // a new GameLibs package merely to reference its expansion/shrine classes.
            Type contentType = typeof(Run).Assembly.GetType("RoR2.DLC4Content");
            Type shrineType = typeof(Run).Assembly.GetType("RoR2.ShrineCompulsionBehaviour");
            if (contentType == null || shrineType == null)
                throw new InvalidOperationException("This game build does not contain the Compulsion shrine.");
            ExpansionDef expansion = AccessTools.Field(contentType, "DLC4")?.GetValue(null) as ExpansionDef;
            if (!ExpansionEnabled(expansion)) return;
            GameObject prefab = LoadSpawnCard("iscShrineCompulsion").prefab;
            if (!prefab.GetComponent(shrineType))
                throw new InvalidOperationException("Compulsion shrine spawn card has an unexpected prefab.");
            Spawn(settings, prefab, instance =>
            {
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                if (purchase && purchase.costType == CostTypeIndex.Money)
                    purchase.Networkcost = Run.instance.GetDifficultyScaledCost(purchase.cost);
            });
        }
    }
}
