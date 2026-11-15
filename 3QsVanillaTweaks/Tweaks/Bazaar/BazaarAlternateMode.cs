using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using C = Config.ConfigManager;
using Log = ThreeQsVanillaTweaks.Log;

namespace Tweaks.Bazaar
{
    internal static partial class BazaarAlternateMode
    {
        private const string LegacySection = "Bazaar - 06 Alternate Mode";
        private const string Section = "Bazaar - 06 Unlock Mode";
        private const string ModeSettings = Section + " - 01 Mode Settings";
        private const string ContextToken = "3QS_BAZAAR_UNLOCK_CONTEXT";
        private const string LockedContextToken = "3QS_BAZAAR_LOCKED_CONTEXT";
        private const string CrystalNamePrefix = "3QS_BAZAAR_CRYSTAL_NAME_";
        private const string LunarShopSlotNamePrefix = "Lunar Shop Slot ";
        private const string LunarShopCrystalTokenPrefix = CrystalNamePrefix + "LUNAR_SHOP_SLOT_";
        private const string CauldronGroup = "02 Cauldrons";
        private const string LockedMagePrefabPath = "RoR2/Base/Mage/LockedMage.prefab";
        private const string CrystalChargeSound = "Play_mage_m2_charge";
        private const string CrystalBreakSound = "Play_item_proc_iceRingSpear";
        private const float UnlockRangeMultiplier = .8f;
        private enum Activation { FleetingMoonOnly, AlwaysOff, AlwaysOn }

        internal sealed class Slot
        {
            internal string Name, ProgressKey;
            internal ConfigEntry<bool> Spawns, Locked;
            internal ConfigEntry<int> Cost;
        }

        private sealed class Gate
        {
            internal Slot Slot;
            internal GameObject Target;
            internal PurchaseInteraction Crystal;
            internal Vector3 CrystalSize;
        }

        private static ConfigEntry<Activation> mode;
        private static GameObject crystalPrefab;
        private static Mesh crystalMesh;
        private static Material[] crystalMaterials;
        private static Stage currentStage;
        private static bool findingNearbyInteractable;
        private static readonly Dictionary<GameObject, Gate> targets = new Dictionary<GameObject, Gate>();
        private static readonly Dictionary<PurchaseInteraction, Gate> crystals = new Dictionary<PurchaseInteraction, Gate>();
        private static readonly HashSet<string> singleCrystalTokens = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, (string Group, int Cost)> slotDefaults =
            new Dictionary<string, (string Group, int Cost)>(StringComparer.Ordinal)
        {
            ["Lunar Shop Slot 1"] = ("01 Lunar Shop", 0),
            ["Lunar Shop Slot 2"] = ("01 Lunar Shop", 0),
            ["Lunar Shop Slot 3"] = ("01 Lunar Shop", 1),
            ["Lunar Shop Slot 4"] = ("01 Lunar Shop", 2),
            ["Lunar Shop Slot 5"] = ("01 Lunar Shop", 3),
            ["Common Cauldron"] = (CauldronGroup, 1),
            ["Common Cauldron 2"] = (CauldronGroup, 2),
            ["Uncommon Cauldron 1"] = (CauldronGroup, 2),
            ["Uncommon Cauldron 2"] = (CauldronGroup, 3),
            ["Legendary Cauldron 1"] = (CauldronGroup, 3),
            ["Legendary Cauldron 2"] = (CauldronGroup, 5),
            ["3D Printer"] = ("03 Printers and Scrapper", 2),
            ["3D Printer 2"] = ("03 Printers and Scrapper", 3),
            ["Scrapper"] = ("03 Printers and Scrapper", 3),
            ["Shrine of Order"] = ("04 Shrines", 3),
            ["Shrine of the Woods"] = ("04 Shrines", 2),
            ["Shrine of the Woods 2"] = ("04 Shrines", 3),
            ["Cleansing Pool"] = ("05 Pools", 5),
            ["Hallowed Pool 1"] = ("05 Pools", 2),
            ["Hallowed Pool 2"] = ("05 Pools", 4),
            ["Hallowed Pool 3"] = ("05 Pools", 6),
            ["Lockbox 1"] = ("06 Lockboxes", 1),
            ["Lockbox 2"] = ("06 Lockboxes", 1),
            ["Lockbox 3"] = ("06 Lockboxes", 2),
            ["Lunar Pod"] = ("07 Lunar Pods and Void Cradle", 1),
            ["Lunar Pod 2"] = ("07 Lunar Pods and Void Cradle", 2),
            ["Lunar Pod 3"] = ("07 Lunar Pods and Void Cradle", 3),
            ["Void Cradle"] = ("07 Lunar Pods and Void Cradle", 4),
            ["Lunar Seer 1"] = ("08 Lunar Seers", 2),
            ["Lunar Seer 2"] = ("08 Lunar Seers", 2),
            ["Lunar Seer 3"] = ("08 Lunar Seers", 2),
            ["Wandering Chef"] = ("09 Wandering Chef", 3),
            ["Exit Portal"] = ("10 Exit Portal", 0)
        };

        internal static bool EnabledByFleetingMoon => mode != null && C.Get(mode) == Activation.FleetingMoonOnly;

        internal static bool Active
        {
            get
            {
                if (mode == null || !Run.instance) return false;
                switch (C.Get(mode))
                {
                    case Activation.AlwaysOn: return true;
                    case Activation.FleetingMoonOnly:
                        var manager = RunArtifactManager.instance;
                        var artifact = ArtifactCatalog.FindArtifactDef("3QS_ARTIFACT_FLEETING_MOON");
                        return manager && artifact && manager.IsArtifactEnabled(artifact);
                    default: return false;
                }
            }
        }

        internal static void Init()
        {
            C.RetireUnboundSectionTree(LegacySection);
            C.RetireUnboundSectionTree("Bazaar - Artificer");
            mode = C.Bind(ModeSettings, "Activation", Activation.FleetingMoonOnly,
                "Use the unlock layout with the Artifact of the Fleeting Moon, every run, or never.");
            BindProgression();
            BazaarAlternateLayout.Bind();

            LanguageAPI.Add(ContextToken, "Unlock {0}");
            LanguageAPI.Add(LockedContextToken, "Locked");
            PrepareCrystalPrefab();
            RoR2Application.onLoad += LoadCrystalMesh;
            Run.onRunStartGlobal += _ => ClearVisit(null);
            Run.onRunDestroyGlobal += _ => ClearVisit(null);
            RoR2Application.onUpdate += UpdateNetwork;
            NetworkUser.onNetworkUserLoadedSceneServer += SendToLoadedPlayer;
            Stage.onStageStartGlobal += ClearVisit;

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.AlternateMode");
            harmony.Patch(A.Method(typeof(Interactor), "FindBestInteractableObject",
                    new[] { typeof(Ray), typeof(float), typeof(Vector3), typeof(float) }),
                prefix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(BeginInteractionSearch)),
                postfix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(FindCrystalTarget)),
                finalizer: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(EndInteractionSearch)));
            harmony.Patch(A.Method(typeof(Interactor), "PerformInteraction", new[] { typeof(GameObject) }),
                prefix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(BeforePerformInteraction)));
            foreach (var type in new[] { typeof(PurchaseInteraction), typeof(PickupPickerController),
                typeof(GenericInteraction), typeof(BazaarUpgradeInteraction) })
            {
                var get = A.Method(type, "GetInteractability") ?? A.Method(type, "RoR2.IInteractable.GetInteractability");
                var begin = A.Method(type, "OnInteractionBegin") ?? A.Method(type, "RoR2.IInteractable.OnInteractionBegin");
                var context = A.Method(type, "GetContextString") ?? A.Method(type, "RoR2.IInteractable.GetContextString");
                if (get != null) harmony.Patch(get,
                    postfix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(AfterInteractability)) { priority = Priority.Last });
                if (begin != null) harmony.Patch(begin,
                    prefix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(BeforeInteraction)) { priority = Priority.First });
                if (context != null) harmony.Patch(context,
                    postfix: new HarmonyMethod(typeof(BazaarAlternateMode), nameof(AfterContext)));
            }
            Log.Info("Bazaar unlock crystals: solid collision, lunar coin interaction and synchronized shatter installed.");
        }

        internal static Slot BindSlot(string progressSuffix, string name)
        {
            var defaults = slotDefaults[name];
            bool locked = name != "Exit Portal" && name != "Lunar Shop Slot 1" && name != "Lunar Shop Slot 2";
            if (name == "Lockbox 3") name = "Void Lockbox";
            string section = Section + " - 02 Interactables - " + defaults.Group + " - " + name;
            C.ReadRetiredValue(section, "Spawn Pose", "");
            var slot = new Slot
            {
                Name = name,
                ProgressKey = LegacySection + " - " + progressSuffix,
                Spawns = C.BindBool(section, "Spawns", true, "Include this interactable in the Bazaar unlock layout."),
                Locked = C.BindBool(section, "Locked By Default", locked,
                    "Require an unlock before using this interactable."),
                Cost = C.Bind(section, "Lunar Coin Unlock Cost", defaults.Cost,
                    new ConfigDescription("Lunar coins to unlock this interactable. 0 makes the unlock free.",
                        new AcceptableValueRange<int>(0, 999)))
            };
            string crystalToken = CrystalNamePrefix + name.Replace(' ', '_').ToUpperInvariant();
            if (defaults.Group == CauldronGroup ||
                name.StartsWith("Lunar Seer ", StringComparison.Ordinal) ||
                name.StartsWith("Hallowed Pool ", StringComparison.Ordinal) ||
                name.StartsWith("Shrine of the Woods", StringComparison.Ordinal))
                singleCrystalTokens.Add(crystalToken);
            LanguageAPI.Add(crystalToken, name);
            return slot;
        }

        private static void PrepareCrystalPrefab()
        {
            var template = new GameObject("Bazaar unlock crystal template");
            template.SetActive(false);
            template.AddComponent<NetworkIdentity>();
            var interactionHitbox = new GameObject("Interaction hitbox");
            interactionHitbox.transform.SetParent(template.transform, false);
            interactionHitbox.layer = LayerIndex.entityPrecise.intVal;
            var hitbox = interactionHitbox.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.enabled = false;
            interactionHitbox.AddComponent<EntityLocator>().entity = template;
            var purchase = template.AddComponent<PurchaseInteraction>();
            purchase.purchaseStatNames = Array.Empty<string>();
            purchase.onSetAvailable = new PurchaseAvailabilityEvent();
            purchase.onSetUnavailable = new PurchaseAvailabilityEvent();
            purchase.onDetailedPurchaseServer = new DetailedPurchaseEvent();
#pragma warning disable CS0618
            purchase.onPurchase = new PurchaseEvent();
#pragma warning restore CS0618
            purchase.costType = CostTypeIndex.LunarCoin;
            purchase.contextToken = ContextToken;
            purchase.shouldProximityHighlight = true;
            purchase.saleStarCompatible = false;
            purchase.automaticallyScaleCostWithDifficulty = false;
            template.AddComponent<CrystalVisual>();
            crystalPrefab = PrefabAPI.InstantiateClone(template, "3Qs_BazaarUnlockCrystal", true);
            UnityEngine.Object.Destroy(template);
        }

        private sealed class CrystalVisual : MonoBehaviour
        {
            private GameObject model;
            private BoxCollider hitbox;
            private PurchaseInteraction purchase;
            private Coroutine breaking;
            private uint chargeSoundId;
            internal Vector3 Size;
            private bool IsShopCrystal => purchase.displayNameToken?.StartsWith(LunarShopCrystalTokenPrefix, StringComparison.Ordinal) == true;

            internal bool IsShopHitbox(Collider collider) => collider == hitbox && IsShopCrystal;

            private void Awake()
            {
                hitbox = GetComponentInChildren<BoxCollider>();
                purchase = GetComponent<PurchaseInteraction>();
            }

            private void CreateCluster()
            {
                model = new GameObject("Unlock crystal cluster");
                model.transform.SetParent(transform, false);
                hitbox.size = IsShopCrystal ? Size * 1.25f : Size;
                hitbox.center = Vector3.up * ((hitbox.size.y - Size.y) * .5f + Size.y * .48f);
                hitbox.enabled = true;

                // Use the network ID so every player sees the same cluster without advancing the run RNG.
                var random = new System.Random(unchecked((int)GetComponent<NetworkIdentity>().netId.Value));
                float clusterYaw = Range(random, 0f, 360f);
                float width = Mathf.Min(Size.x, Size.z);
                AddShard(Vector3.zero, Quaternion.Euler(Range(random, -9f, 9f), clusterYaw, Range(random, -9f, 9f)),
                    Size.y, width * .9f);

                // The purchase name token is synchronized when the crystal spawns.
                if (singleCrystalTokens.Contains(purchase.displayNameToken)) return;

                for (int i = 0; i < 3; i++)
                {
                    float yaw = clusterYaw + i * 120f + Range(random, -22f, 22f);
                    var direction = Quaternion.Euler(0f, yaw, 0f);
                    var origin = direction * Vector3.forward * Range(random, .26f, .38f);
                    origin = Vector3.Scale(origin, new Vector3(Size.x, 1f, Size.z));
                    origin.y = GroundPosition(transform.position + origin, 1.5f).y - transform.position.y;
                    var rotation = direction * Quaternion.AngleAxis(Range(random, 14f, 32f), Vector3.right) *
                        Quaternion.AngleAxis(Range(random, 0f, 360f), Vector3.up);
                    AddShard(origin, rotation, Size.y * Range(random, .63f - i * .08f, .82f - i * .08f),
                        width * Range(random, .45f, .68f));
                }
            }

            private static float Range(System.Random random, float min, float max) =>
                Mathf.Lerp(min, max, (float)random.NextDouble());

            private void AddShard(Vector3 origin, Quaternion orientation, float height, float width)
            {
                var shard = crystalMesh ? new GameObject("IceMesh") : GameObject.CreatePrimitive(PrimitiveType.Cube);
                var node = shard.transform;
                node.SetParent(model.transform, false);
                node.localPosition = origin + orientation * Vector3.up * (height * .5f) - Vector3.up * (width * .14f);
                node.localRotation = orientation;
                if (crystalMesh)
                {
                    // The original mesh grows along local Z. Rotate it upright and center its bounds.
                    var bounds = crystalMesh.bounds;
                    var scale = new Vector3(width / bounds.size.x, width / bounds.size.y, height / bounds.size.z);
                    node.localRotation *= Quaternion.Euler(-90f, 0f, 0f);
                    node.localScale = scale;
                    node.localPosition -= node.localRotation * Vector3.Scale(bounds.center, scale);
                    shard.AddComponent<MeshFilter>().sharedMesh = crystalMesh;
                    shard.AddComponent<MeshRenderer>().sharedMaterials = crystalMaterials;
                    var collider = shard.AddComponent<MeshCollider>();
                    collider.convex = true;
                    collider.sharedMesh = crystalMesh;
                }
                else
                {
                    node.localScale = new Vector3(width, height, width);
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_Color", new Color(.48f, .72f, 1f));
                    shard.GetComponent<Renderer>().SetPropertyBlock(block);
                }

                shard.layer = LayerIndex.world.intVal;
                shard.AddComponent<EntityLocator>().entity = gameObject;

                var highlight = GetComponent<Highlight>();
                if (!highlight.targetRenderer)
                {
                    highlight.targetRenderer = shard.GetComponent<Renderer>();
                    highlight.highlightColor = Highlight.HighlightColor.interactive;
                }
            }

            private void Update()
            {
                if (!EnsureCluster()) return;

                var local = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                if (!local) return;
                bool visible = CrystalIsVisible(purchase, local);
                if (hitbox.enabled != visible) hitbox.enabled = visible;
                if (breaking == null && model.activeSelf != visible) model.SetActive(visible);
            }

            private bool EnsureCluster()
            {
                if (!model)
                {
                    if (!NetworkServer.active && TryGetState(purchase, out var state)) Size = state.CrystalSize;
                    if (Size.y <= 0f) return false;
                    CreateCluster();
                }
                return true;
            }

            internal void PlayBreak()
            {
                if (breaking != null || !EnsureCluster()) return;
                model.SetActive(true);
                hitbox.enabled = false;
                breaking = StartCoroutine(BreakCluster());
            }

            private IEnumerator BreakCluster()
            {
                var shards = new List<(Transform Shard, Vector3 Position, Vector3 Scale, Quaternion Rotation)>();
                foreach (Transform shard in model.transform)
                {
                    shard.GetComponent<Collider>().enabled = false;
                    shards.Add((shard, shard.localPosition, shard.localScale, shard.localRotation));
                }

                const float chargeTime = .15f;
                const float breakTime = .55f;
                chargeSoundId = Util.PlaySound(CrystalChargeSound, gameObject);
                for (float elapsed = 0f; elapsed < chargeTime; elapsed += Time.deltaTime)
                {
                    model.transform.localScale = Vector3.one * (1f + .08f * Mathf.Sin(Mathf.PI * elapsed / chargeTime));
                    yield return null;
                }
                model.transform.localScale = Vector3.one;
                StopChargeSound();
                Util.PlaySound(CrystalBreakSound, gameObject);
                for (float elapsed = 0f; elapsed < breakTime; elapsed += Time.deltaTime)
                {
                    float t = elapsed / breakTime;
                    for (int i = 0; i < shards.Count; ++i)
                    {
                        var (shard, position, scale, rotation) = shards[i];
                        var direction = Quaternion.Euler(0f, 360f * i / shards.Count, 0f) * Vector3.forward;
                        shard.localPosition = position + direction * (Size.x * .35f * t) + Vector3.up * (Size.y * (.4f * t - .45f * t * t));
                        shard.localRotation = Quaternion.AngleAxis(85f * t, Vector3.Cross(Vector3.up, direction)) * rotation;
                        shard.localScale = scale * (1f - t * t);
                    }
                    yield return null;
                }

                var local = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
                model.SetActive(local && CrystalIsVisible(purchase, local));
                foreach (var (shard, position, scale, rotation) in shards)
                {
                    shard.SetLocalPositionAndRotation(position, rotation);
                    shard.localScale = scale;
                    shard.GetComponent<Collider>().enabled = true;
                }
                breaking = null;
            }

            private void OnDestroy() => StopChargeSound();

            private void StopChargeSound()
            {
                if (chargeSoundId == 0u) return;
                AkSoundEngine.StopPlayingID(chargeSoundId);
                chargeSoundId = 0u;
            }
        }

        private static Vector3 GroundPosition(Vector3 point, float searchDistance)
        {
            var position = point;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * searchDistance, Vector3.down,
                searchDistance * 2f, LayerIndex.world.mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < .2f || hit.collider.GetComponentInParent<CrystalVisual>()) continue;
                float distance = Mathf.Abs(hit.point.y - point.y);
                if (distance >= nearest) continue;
                nearest = distance;
                position = hit.point;
            }
            return position;
        }

        private static Bounds ModelBounds(GameObject instance, Component anchor, bool shopSlot, bool lockbox)
        {
            var bounds = new Bounds(shopSlot ? anchor.transform.position : instance.transform.position, Vector3.zero);
            bool foundModel = false;
            var model = shopSlot ? anchor.gameObject : instance;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer) ||
                    renderer.GetComponent<TMPro.TMP_Text>() ||
                    (!lockbox && renderer.GetComponentInParent<PickupDisplay>())) continue;
                if (foundModel) bounds.Encapsulate(renderer.bounds);
                else bounds = renderer.bounds;
                foundModel = true;
            }
            return bounds;
        }

        private static Vector3 CrystalCenter(GameObject instance, Component anchor, Slot slot, Bounds bounds, bool shopSlot)
        {
            // The vanilla highlights identify the bud, seer pedestal and scrapper body separately from their displays.
            if (shopSlot || slot.Name == "Scrapper" || slot.Name.StartsWith("Lunar Seer ", StringComparison.Ordinal))
            {
                var highlight = anchor.GetComponent<Highlight>() ?? instance.GetComponent<Highlight>();
                if (highlight && highlight.targetRenderer) return highlight.targetRenderer.bounds.center;
            }
            return bounds.center;
        }

        private static Vector3 CrystalDimensions(Slot slot, Bounds bounds, Vector3 origin, bool shopSlot, bool lockbox)
        {
            float modelHeight = bounds.max.y - origin.y;
            if (shopSlot)
                return new Vector3(1.75f, Mathf.Clamp(modelHeight + .4f, 1.8f, 2.9f), 1.75f);

            var previousSize = new Vector3(Mathf.Clamp(bounds.size.x * 1.15f, 3.2f, 12f),
                Mathf.Clamp(modelHeight + .7f, 4.6f, 16f), Mathf.Clamp(bounds.size.z * 1.15f, 3.2f, 12f));
            if (lockbox) return previousSize;
            if (slot.Name == "Scrapper") return previousSize * 1.2f;
            var size = new Vector3(Mathf.Clamp(bounds.size.x * 1.65f, 5.6f, 16f),
                Mathf.Clamp(modelHeight * 1.35f + 1f, 7f, 22f), Mathf.Clamp(bounds.size.z * 1.65f, 5.6f, 16f));
            if (slot.Name.StartsWith("Shrine of the Woods", StringComparison.Ordinal)) return size * .75f;
            return slot.Name.StartsWith("Lunar Pod", StringComparison.Ordinal) ? size * .85f : size;
        }

        private static void LoadCrystalMesh()
        {
            try
            {
                var lockedMage = Stages.StageFeatureSpawnHelper.LoadPrefab(LockedMagePrefabPath);
                var renderer = lockedMage.GetComponent<Highlight>()?.targetRenderer as MeshRenderer;
                var filter = renderer ? renderer.GetComponent<MeshFilter>() : null;
                if (!filter || !filter.sharedMesh)
                    throw new InvalidOperationException("The LockedMage highlight has no ice mesh.");

                crystalMaterials = renderer.sharedMaterials;
                crystalMesh = filter.sharedMesh;
            }
            catch (Exception e)
            {
                Log.Warning("Bazaar unlock crystal could not load the Artificer's ice mesh: " + e.Message);
            }
        }

        private static void ClearVisit(Stage stage)
        {
            if (stage && ReferenceEquals(currentStage, stage)) return;
            currentStage = stage;
            targets.Clear();
            crystals.Clear();
            movedObjects.Clear();
            visitUnlocks.Clear();
            // Network state may already have arrived for a client loading this visit.
            if (NetworkServer.active || !stage || !BazaarSpawnHelper.InBazaar())
            {
                clientStates.Clear();
                pendingPoses.Clear();
            }
        }

        internal static void LockSpawn(GameObject instance, Slot slot)
        {
            if (slot == null || !Active || !C.Get(slot.Locked) || !crystalPrefab) return;
            if (Stage.instance && currentStage != Stage.instance) ClearVisit(Stage.instance);
            if (targets.ContainsKey(instance)) return;

            Component anchor = instance.GetComponentInChildren<PurchaseInteraction>() as Component ??
                instance.GetComponentInChildren<PickupPickerController>() as Component ??
                instance.GetComponentInChildren<GenericInteraction>() as Component ??
                instance.GetComponentInChildren<BazaarUpgradeInteraction>();
            if (!anchor || (anchor is PurchaseInteraction nativePurchase && nativePurchase.lockGameObject)) return;

            bool shopSlot = slot.Name.StartsWith(LunarShopSlotNamePrefix, StringComparison.Ordinal);
            bool lockbox = slot.Name.StartsWith("Lockbox ", StringComparison.Ordinal) || slot.Name == "Void Lockbox";
            var bounds = ModelBounds(instance, anchor, shopSlot, lockbox);
            var center = CrystalCenter(instance, anchor, slot, bounds, shopSlot);
            float baseY = shopSlot ? anchor.transform.position.y : Mathf.Min(instance.transform.position.y, bounds.min.y);
            var origin = GroundPosition(new Vector3(center.x, baseY, center.z), 4f);
            var size = CrystalDimensions(slot, bounds, origin, shopSlot, lockbox);
            var crystal = UnityEngine.Object.Instantiate(crystalPrefab, origin, Quaternion.identity);
            crystal.GetComponent<CrystalVisual>().Size = size;
            var purchase = crystal.GetComponent<PurchaseInteraction>();
            int price = C.Get(slot.Cost);
            purchase.Networkcost = price;
            purchase.NetworkcostType = price == 0 ? CostTypeIndex.None : CostTypeIndex.LunarCoin;
            purchase.NetworkdisplayNameToken = CrystalNamePrefix + slot.Name.Replace(' ', '_').ToUpperInvariant();

            var gate = new Gate { Slot = slot, Target = instance, Crystal = purchase, CrystalSize = size };
            targets.Add(instance, gate);
            crystals.Add(purchase, gate);
            crystal.SetActive(true);
            purchase.SetAvailable(true);
            NetworkServer.Spawn(crystal);
            DebugStuff.InteractablePlacementEditor.Link(instance, crystal);
            PublishGate(gate);
        }

        private static Gate FindGate(Component interaction)
        {
            for (var node = interaction.transform; node; node = node.parent)
                if (targets.TryGetValue(node.gameObject, out var gate)) return gate;
            return null;
        }

        private static NetworkUser GetUser(Interactor activator) =>
            activator ? Util.LookUpBodyNetworkUser(activator.gameObject) : null;

        private static bool CrystalIsVisible(PurchaseInteraction purchase, NetworkUser user)
        {
            if (NetworkServer.active)
                return !crystals.TryGetValue(purchase, out var gate) || CanBuy(gate, user);
            return !TryGetState(purchase, out var state) || state.Allows(user);
        }

        private static bool CanBuy(Gate gate, NetworkUser buyer) =>
            buyer && (C.Get(recipients) == Recipients.Host ? Host && !IsUnlocked(gate.Slot, Host) : !IsUnlocked(gate.Slot, buyer));

        private static bool IsCrystal(Component interaction) =>
            interaction is PurchaseInteraction && interaction.GetComponent<CrystalVisual>();

        private static bool CanInteract(Component interaction, Interactor activator)
        {
            if (!BazaarSpawnHelper.InBazaar()) return true;
            var user = GetUser(activator);
            if (NetworkServer.active)
            {
                if (interaction is PurchaseInteraction purchase && crystals.TryGetValue(purchase, out var crystal))
                    return CanBuy(crystal, user);
                var gate = FindGate(interaction);
                if (gate == null) return true;
                // Preserve any native lock. Purchasing the Bazaar crystal only grants access to the slot.
                return IsUnlocked(gate.Slot, user);
            }
            return !TryGetState(interaction, out var state) || state.Allows(user);
        }

        private static PurchaseInteraction UnlockPurchase(Component interaction, NetworkUser user)
        {
            if (NetworkServer.active)
            {
                var gate = interaction is PurchaseInteraction purchase && crystals.TryGetValue(purchase, out var crystalGate)
                    ? crystalGate : FindGate(interaction);
                return gate != null && !IsUnlocked(gate.Slot, user) && CanBuy(gate, user) ? gate.Crystal : null;
            }
            if (!TryGetState(interaction, out var state)) return null;
            if (IsCrystal(interaction)) return state.Allows(user) ? (PurchaseInteraction)interaction : null;
            if (state.Allows(user) || state.CrystalId.IsEmpty() ||
                !clientStates.TryGetValue(state.CrystalId, out var crystalState) || !crystalState.Allows(user)) return null;
            var crystalObject = ClientScene.FindLocalObject(state.CrystalId);
            return crystalObject ? crystalObject.GetComponent<PurchaseInteraction>() : null;
        }

        private static bool BeforeInteraction(Component __instance, Interactor __0)
        {
            if (!BazaarSpawnHelper.InBazaar()) return true;
            var user = GetUser(__0);
            var purchase = UnlockPurchase(__instance, user);
            if (!purchase) return !IsCrystal(__instance) && CanInteract(__instance, __0);
            TryPayForUnlock(purchase, __0, user);
            return false;
        }

        private static void BeginInteractionSearch(out bool __state)
        {
            __state = findingNearbyInteractable;
            findingNearbyInteractable = BazaarSpawnHelper.InBazaar();
        }

        private static void EndInteractionSearch(bool __state) => findingNearbyInteractable = __state;

        private static void FindCrystalTarget(Interactor __instance, Ray __0, float __1, ref GameObject __result)
        {
            if (!Active || !BazaarSpawnHelper.InBazaar() || __instance.isRemoteOp) return;
            var user = GetUser(__instance);
            // Unlocks require a direct hit. Vanilla proximity selection handles ordinary interactables.
            if (!user || !TryFindUnlockHit(__0, __1 * UnlockRangeMultiplier, user, __result, out var hit)) return;

            var entity = EntityLocator.GetEntity(hit.collider.gameObject);
            for (var node = entity ? entity.transform : hit.collider.transform; node; node = node.parent)
            {
                foreach (var interaction in node.GetComponents<IInteractable>())
                {
                    if (!(interaction is Component component)) continue;
                    var purchase = UnlockPurchase(component, user);
                    if (!purchase) continue;
                    __result = purchase.gameObject;
                    return;
                }
            }
        }

        private static bool TryFindUnlockHit(Ray ray, float range, NetworkUser user, GameObject vanillaTarget, out RaycastHit hit)
        {
            // Shop triggers extend the aim area while solid geometry still occludes it.
            bool found = Physics.Raycast(ray, out hit, range,
                LayerIndex.CommonMasks.interactable | LayerIndex.world.mask, QueryTriggerInteraction.Ignore);
            if (found && vanillaTarget && (hit.collider.transform.IsChildOf(vanillaTarget.transform) ||
                EntityLocator.GetEntity(hit.collider.gameObject) == vanillaTarget)) return true;

            float closestDistance = found ? hit.distance : range;
            foreach (var candidate in Physics.RaycastAll(ray, closestDistance, LayerIndex.entityPrecise.mask, QueryTriggerInteraction.Collide))
            {
                if (!candidate.collider.isTrigger || candidate.distance >= closestDistance) continue;
                var crystal = candidate.collider.GetComponentInParent<CrystalVisual>();
                if (!crystal || !crystal.IsShopHitbox(candidate.collider) ||
                    !UnlockPurchase(crystal.GetComponent<PurchaseInteraction>(), user)) continue;
                hit = candidate;
                closestDistance = candidate.distance;
                found = true;
            }
            return found;
        }

        private static bool BeforePerformInteraction(Interactor __instance, GameObject __0)
        {
            if (!NetworkServer.active || !__0 || !Active || !BazaarSpawnHelper.InBazaar()) return true;
            var user = GetUser(__instance);
            foreach (var interaction in __0.GetComponents<IInteractable>())
            {
                if (!(interaction is Component component)) continue;
                var purchase = UnlockPurchase(component, user);
                if (!purchase) continue;
                TryPayForUnlock(purchase, __instance, user);
                return false;
            }
            return true;
        }

        private static void TryPayForUnlock(PurchaseInteraction purchase, Interactor activator, NetworkUser buyer)
        {
            if (!NetworkServer.active || activator.isRemoteOp || !buyer || !crystals.TryGetValue(purchase, out var gate) ||
                !CanBuy(gate, buyer) || !CanUnlock(purchase, activator)) return;
            var hitbox = purchase.GetComponentInChildren<BoxCollider>();
            if (!hitbox) return;
            var position = hitbox.transform.InverseTransformPoint(activator.transform.position);
            var bounds = new Bounds(hitbox.center, hitbox.size);
            float range = activator.maxInteractionDistance * UnlockRangeMultiplier;
            if (bounds.SqrDistance(position) > range * range) return;
            var input = activator.GetComponent<InputBankTest>();
            var viewPoint = input ? input.aimOrigin : activator.transform.position;
            var crystalPoint = hitbox.transform.TransformPoint(bounds.ClosestPoint(hitbox.transform.InverseTransformPoint(viewPoint)));
            if (Physics.Linecast(viewPoint, crystalPoint, out var obstruction, LayerIndex.world.mask, QueryTriggerInteraction.Ignore) &&
                obstruction.collider.GetComponentInParent<CrystalVisual>() != purchase.GetComponent<CrystalVisual>() &&
                !obstruction.collider.transform.IsChildOf(gate.Target.transform)) return;
            var viewers = new List<NetworkUser>();
            foreach (var user in NetworkUser.readOnlyInstancesList)
                if (CanBuy(gate, user)) viewers.Add(user);
            if (purchase.cost > 0) buyer.DeductLunarCoins((uint)purchase.cost);
            GrantUnlock(gate.Slot, buyer);
            PublishGate(gate);
            PublishCrystalBreak(gate, viewers);
            Newt.CommentOnUnlock();
            Log.Info($"Bazaar unlocked {gate.Slot.Name} for {purchase.cost} lunar coins.");
        }

        private static bool CanUnlock(PurchaseInteraction purchase, Interactor activator) =>
            purchase.cost == 0 || CostTypeCatalog.GetCostTypeDef(CostTypeIndex.LunarCoin).IsAffordable(purchase.cost, activator);

        private static void AfterInteractability(Component __instance, Interactor __0, ref Interactability __result)
        {
            if (!BazaarSpawnHelper.InBazaar()) return;
            var purchase = UnlockPurchase(__instance, GetUser(__0));
            // Aimed unlocks still show the price before the server checks affordability.
            if (purchase) __result = findingNearbyInteractable ? Interactability.Disabled : Interactability.Available;
            else if (IsCrystal(__instance) || !CanInteract(__instance, __0)) __result = Interactability.Disabled;
        }

        private static void AfterContext(Component __instance, Interactor __0, ref string __result)
        {
            if (!BazaarSpawnHelper.InBazaar()) return;
            var purchase = UnlockPurchase(__instance, GetUser(__0));
            if (purchase)
            {
                __result = Language.GetStringFormatted(ContextToken, Language.GetString(purchase.displayNameToken));
                if (purchase.cost > 0) __result += BazaarUseCosts.FormatFee(purchase.cost);
                if (!CanUnlock(purchase, __0)) __result += " <style=cDeath>Not enough lunar coins</style>";
                return;
            }
            if (IsCrystal(__instance)) __result = "";
            else if (!CanInteract(__instance, __0)) __result = Language.GetString(LockedContextToken);
        }
    }
}
