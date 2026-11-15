using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using RoR2;
using RoR2.UI;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using ModConfig = Config.ConfigManager;
using Path = System.IO.Path;

namespace Tweaks.DebugStuff
{
    internal static class InteractablePlacementEditor
    {
        private const short PlacementMessageId = 187;
        private static readonly CultureInfo Format = CultureInfo.InvariantCulture;
        private static readonly List<Placement> Placements = new List<Placement>();
        private static EditorWindow window;
        private static ConfigEntry<bool> editorEnabled;
        private static ConfigEntry<KeyboardShortcut> shortcut;
        private static ConfigFile savedPlacements;

        internal static void Init()
        {
            const string section = "Debug - Interactable Placement";
            editorEnabled = ModConfig.BindBool(section, "Enable Placement Editor", true,
                "Open the placement editor in a solo game or as host. Saved placements also apply with the editor off.", clientSide: true);
            shortcut = ModConfig.Bind(section, "Placement Editor Shortcut", new KeyboardShortcut(KeyCode.F6),
                "Open or close the placement editor during a run.", clientSide: true);
            string mainConfig = ModConfig.File.ConfigFilePath;
            string path = Path.Combine(Path.GetDirectoryName(mainConfig),
                Path.GetFileNameWithoutExtension(mainConfig) + ".Placements.cfg");
            savedPlacements = new ConfigFile(path, false) { SaveOnConfigSet = false };
            SceneManager.sceneUnloaded += scene => Placements.RemoveAll(item => !item.Target || item.Target.scene == scene);
            Run.onRunDestroyGlobal += _ => Placements.Clear();
            var root = new GameObject("3Qs Interactable Placement Editor");
            UnityEngine.Object.DontDestroyOnLoad(root);
            window = root.AddComponent<EditorWindow>();
        }

        // Call before network spawn for fixed placements. Slot names must identify a placement,
        // including its normal/alternate layout, independent of the rolled prefab.
        internal static void Register(GameObject target, string label, string slot, bool alreadySpawned = false)
        {
            if (!target || savedPlacements == null || !NetworkServer.active || !target.scene.IsValid()) return;
            var fallback = new Pose(target.transform.position, target.transform.rotation);
            var entry = savedPlacements.Bind(target.scene.name, slot, "",
                "Exact world position X, Y, Z and rotation X, Y, Z in degrees. Blank uses the normal placement.");
            var placement = new Placement(target, label, fallback, entry);
            if (TryReadPose(entry.Value.Split(','), out var saved))
                placement.Apply(saved, alreadySpawned);
            Placements.Add(placement);
        }

        internal static void Link(GameObject target, GameObject companion)
        {
            if (!companion) return;
            var placement = Placements.Find(item => item.Target == target);
            if (placement != null) placement.Link(companion);
        }

        private sealed class Placement
        {
            internal readonly GameObject Target;
            internal readonly string Label;
            internal readonly Pose Fallback;
            internal readonly ConfigEntry<string> Entry;
            private GameObject companion;
            private Vector3 companionOffset;
            private Quaternion companionRotation;

            internal Placement(GameObject target, string label, Pose fallback, ConfigEntry<string> entry)
            {
                Target = target;
                Label = label;
                Fallback = fallback;
                Entry = entry;
            }

            internal void Link(GameObject linked)
            {
                companion = linked;
                companionOffset = Target.transform.InverseTransformPoint(linked.transform.position);
                companionRotation = Quaternion.Inverse(Target.transform.rotation) * linked.transform.rotation;
            }

            internal void Apply(Pose pose, bool sync)
            {
                Move(Target, pose);
                if (companion)
                {
                    var linkedPose = new Pose(Target.transform.TransformPoint(companionOffset), pose.rotation * companionRotation);
                    Move(companion, linkedPose);
                    if (sync) SendPose(companion, linkedPose);
                }
                if (sync) SendPose(Target, pose);
                Physics.SyncTransforms();
            }

            internal void Save(string value)
            {
                string previous = Entry.Value;
                Entry.Value = value;
                try { Entry.ConfigFile.Save(); }
                catch
                {
                    Entry.Value = previous;
                    throw;
                }
            }
        }

        private static void Move(GameObject target, Pose pose)
        {
            target.transform.SetPositionAndRotation(pose.position, pose.rotation);
            var body = target.GetComponent<Rigidbody>();
            if (!body) return;
            body.position = pose.position;
            body.rotation = pose.rotation;
        }

        private static void SendPose(GameObject target, Pose pose)
        {
            var identity = target.GetComponent<NetworkIdentity>();
            if (identity && identity.netId.Value != 0u)
                NetworkServer.SendToAll(PlacementMessageId, new PlacementMessage { Id = identity.netId, Pose = pose });
        }

        private static void ReceivePose(NetworkMessage message)
        {
            if (!NetworkServer.active && window) window.QueuePose(message.ReadMessage<PlacementMessage>());
        }

        private sealed class PlacementMessage : MessageBase
        {
            internal NetworkInstanceId Id;
            internal Pose Pose;

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(Id);
                writer.Write(Pose.position);
                writer.Write(Pose.rotation);
            }

            public override void Deserialize(NetworkReader reader)
            {
                Id = reader.ReadNetworkId();
                Pose = new Pose(reader.ReadVector3(), reader.ReadQuaternion());
            }
        }

        private static string[] PoseFields(Pose pose)
        {
            Vector3 position = pose.position;
            Vector3 rotation = pose.rotation.eulerAngles;
            return new[] { position.x.ToString("R", Format), position.y.ToString("R", Format), position.z.ToString("R", Format),
                rotation.x.ToString("R", Format), rotation.y.ToString("R", Format), rotation.z.ToString("R", Format) };
        }

        private static bool TryNumber(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, Format, out value) && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool ShortcutPressed(KeyboardShortcut binding)
        {
            if (binding.MainKey == KeyCode.None || !Input.GetKeyDown(binding.MainKey)) return false;
            // A movement key or mouse button can be held while opening the editor.
            foreach (var modifier in binding.Modifiers)
                if (!Input.GetKey(modifier)) return false;
            return true;
        }

        private static bool TryReadPose(string[] fields, out Pose pose)
        {
            pose = default;
            if (fields.Length != 6 || !TryNumber(fields[0], out float x) || !TryNumber(fields[1], out float y) ||
                !TryNumber(fields[2], out float z) || !TryNumber(fields[3], out float pitch) ||
                !TryNumber(fields[4], out float yaw) || !TryNumber(fields[5], out float roll)) return false;
            pose = new Pose(new Vector3(x, y, z),
                Quaternion.Euler(pitch % 360f, yaw % 360f, roll % 360f));
            return true;
        }

        private sealed class EditorWindow : MonoBehaviour
        {
            private static readonly string[] AxisLabels = { "X", "Y", "Z", "Pitch (X)", "Yaw (Y)", "Roll (Z)" };
            private readonly List<Placement> visible = new List<Placement>();
            private readonly Dictionary<NetworkInstanceId, (Pose Pose, float Expiry)> pendingPoses =
                new Dictionary<NetworkInstanceId, (Pose, float)>();
            private readonly List<NetworkInstanceId> processedPoses = new List<NetworkInstanceId>();
            private NetworkClient client;
            private MPEventSystem cursorOwner;
            private bool open, previewing;
            private Stage stage;
            private Scene scene;
            private CharacterBody player;
            private Placement selected;
            private Pose beforePreview;
            private string[] fields;
            private Vector2 listScroll, windowScroll;
            private string filter = "", status = "";
            private string positionStep = "0.25", rotationStep = "5";
            private string shortcutHint;
            private float shortcutHintUntil;

            // Director-spawned objects can send a placement while the client is still loading them.
            internal void QueuePose(PlacementMessage update) =>
                pendingPoses[update.Id] = (update.Pose, Time.unscaledTime + 30f);

            private void ApplyPendingPoses()
            {
                if (pendingPoses.Count == 0) return;
                bool moved = false;
                foreach (var pair in pendingPoses)
                {
                    var target = ClientScene.FindLocalObject(pair.Key);
                    if (target)
                    {
                        Move(target, pair.Value.Pose);
                        moved = true;
                    }
                    if (target || Time.unscaledTime >= pair.Value.Expiry) processedPoses.Add(pair.Key);
                }
                foreach (var id in processedPoses) pendingPoses.Remove(id);
                processedPoses.Clear();
                if (moved) Physics.SyncTransforms();
            }

            private void Update()
            {
                var currentClient = NetworkClient.active && !NetworkServer.active && NetworkManager.singleton
                    ? NetworkManager.singleton.client : null;
                if (client != currentClient)
                {
                    client = currentClient;
                    pendingPoses.Clear();
                    client?.RegisterHandler(PlacementMessageId, ReceivePose);
                }
                if (client != null) ApplyPendingPoses();

                var local = LocalUserManager.GetFirstLocalUser();
                CharacterBody currentPlayer = local?.cachedBody;
                Stage currentStage = Stage.instance;
                bool canEdit = ModConfig.Get(editorEnabled) && NetworkServer.active && Run.instance && currentStage && currentPlayer;
                if (open && (!canEdit || stage != currentStage || player != currentPlayer || !scene.isLoaded))
                {
                    Close();
                    return;
                }
                if (!Run.instance || (!ModConfig.DebugOptionsEnabled && !editorEnabled.Value) ||
                    !ShortcutPressed(ModConfig.Get(shortcut))) return;
                if (open)
                {
                    Close();
                    return;
                }
                if (!canEdit)
                {
                    if (!ModConfig.Get(editorEnabled))
                        shortcutHint = ModConfig.DebugOptionsEnabled
                            ? "Enable Placement Editor in Debug - Interactable Placement."
                            : "Turn on Use Debug Options in the main mod config.";
                    else if (!NetworkServer.active)
                        shortcutHint = "Open the placement editor in a solo run or as the host.";
                    else
                        shortcutHint = "Wait until the stage and your character are loaded.";
                    shortcutHintUntil = Time.unscaledTime + 5f;
                    Log.Info("Placement editor: " + shortcutHint);
                    return;
                }

                stage = currentStage;
                player = currentPlayer;
                scene = SceneInfo.instance ? SceneInfo.instance.gameObject.scene : SceneManager.GetActiveScene();
                cursorOwner = local.eventSystem;
                if (cursorOwner) cursorOwner.cursorOpenerCount++;
                filter = "";
                listScroll = windowScroll = Vector2.zero;
                RefreshList();
                Select(visible.Count == 0 ? null : visible[0]);
                open = true;
            }

            private void OnDisable() => Close();

            private void Close()
            {
                if (!open) return;
                RevertPreview();
                open = false;
                selected = null;
                if (cursorOwner) cursorOwner.cursorOpenerCount--;
                cursorOwner = null;
                player = null;
                stage = null;
            }

            private void RevertPreview()
            {
                if (previewing && NetworkServer.active && selected != null && selected.Target && selected.Target.scene.isLoaded)
                    selected.Apply(beforePreview, true);
                previewing = false;
            }

            private void Select(Placement placement)
            {
                RevertPreview();
                selected = placement;
                status = "";
                if (placement == null) return;
                var transform = placement.Target.transform;
                beforePreview = new Pose(transform.position, transform.rotation);
                fields = PoseFields(beforePreview);
            }

            private void RefreshList()
            {
                visible.Clear();
                Placements.RemoveAll(item => !item.Target);
                Vector3 origin = player.transform.position;
                foreach (var item in Placements)
                    if (item.Target.scene == scene && item.Target.activeInHierarchy &&
                        item.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        visible.Add(item);
                visible.Sort((a, b) => (a.Target.transform.position - origin).sqrMagnitude.CompareTo(
                    (b.Target.transform.position - origin).sqrMagnitude));
            }

            private void OnGUI()
            {
                if (!open)
                {
                    if (shortcutHintUntil > Time.unscaledTime)
                    {
                        GUILayout.BeginArea(new Rect(16f, 22f, Mathf.Min(440f, Screen.width - 32f), 70f), GUI.skin.box);
                        GUILayout.Label(shortcutHint);
                        GUILayout.EndArea();
                    }
                    return;
                }
                if (Event.current.type == EventType.Layout) RefreshList();
                float width = Mathf.Min(440f, Screen.width - 32f);
                float height = Mathf.Min(710f, Screen.height - 42f);
                GUILayout.BeginArea(new Rect(16f, 22f, width, height), GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Interactable Placement Editor");
                if (GUILayout.Button("Close", GUILayout.Width(58f)))
                {
                    GUILayout.EndHorizontal();
                    GUILayout.EndArea();
                    Close();
                    return;
                }
                GUILayout.EndHorizontal();
                windowScroll = GUILayout.BeginScrollView(windowScroll);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Filter", GUILayout.Width(40f));
                filter = GUILayout.TextField(filter);
                GUILayout.EndHorizontal();

                if (visible.Count == 0) GUILayout.Label("No tracked interactables match on this map.");
                listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(Mathf.Min(160f, height * .23f)));
                foreach (var item in visible)
                {
                    if (!item.Target) continue;
                    string distance = Vector3.Distance(item.Target.transform.position, player.transform.position).ToString("0.0", Format);
                    var label = (item == selected ? "● " : "") + item.Label + "  (" + distance + " m)";
                    if (GUILayout.Button(label) && item != selected)
                    {
                        GUI.FocusControl(null);
                        Select(item);
                    }
                }
                GUILayout.EndScrollView();

                if (selected != null && selected.Target)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(selected.Label + (previewing ? "  •  Preview" : ""));
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Move step (m)", GUILayout.Width(85f));
                    positionStep = GUILayout.TextField(positionStep, GUILayout.Width(54f));
                    GUILayout.Label("Turn step (°)", GUILayout.Width(85f));
                    rotationStep = GUILayout.TextField(rotationStep, GUILayout.Width(54f));
                    GUILayout.EndHorizontal();
                    for (int i = 0; i < fields.Length; i++) DrawAxis(i);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Preview")) Preview();
                    if (GUILayout.Button("Place at my feet")) PlaceAtFeet();
                    if (GUILayout.Button("Copy pose")) CopyPose();
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Save exact pose")) SavePose();
                    if (GUILayout.Button("Revert preview"))
                    {
                        RevertPreview();
                        fields = PoseFields(beforePreview);
                        status = "";
                    }
                    bool wasEnabled = GUI.enabled;
                    GUI.enabled = wasEnabled && !string.IsNullOrEmpty(selected.Entry.Value);
                    if (GUILayout.Button("Clear saved pose")) ClearSavedPose();
                    GUI.enabled = wasEnabled;
                    GUILayout.EndHorizontal();
                }
                if (status.Length > 0) GUILayout.Label(status);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
            }

            private void DrawAxis(int index)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(AxisLabels[index], GUILayout.Width(68f));
                fields[index] = GUILayout.TextField(fields[index]);
                if (GUILayout.Button("−", GUILayout.Width(34f))) Nudge(index, -1f);
                if (GUILayout.Button("+", GUILayout.Width(34f))) Nudge(index, 1f);
                GUILayout.EndHorizontal();
            }

            private void Nudge(int index, float direction)
            {
                if (!TryNumber(fields[index], out float value) ||
                    !TryNumber(index < 3 ? positionStep : rotationStep, out float step) || step <= 0f)
                {
                    status = "Enter a number and a positive step.";
                    return;
                }
                fields[index] = (value + step * direction).ToString("R", Format);
                GUI.FocusControl(null);
                Preview();
            }

            private void PlaceAtFeet()
            {
                Vector3 feet = player.footPosition;
                fields[0] = feet.x.ToString("R", Format);
                fields[1] = feet.y.ToString("R", Format);
                fields[2] = feet.z.ToString("R", Format);
                Preview();
            }

            private bool ReadFields(out Pose pose)
            {
                if (TryReadPose(fields, out pose)) return true;
                status = "Enter six finite numbers. Use a decimal point.";
                return false;
            }

            private void Preview()
            {
                if (!ReadFields(out var pose)) return;
                selected.Apply(pose, true);
                previewing = true;
                status = "";
            }

            private void CopyPose()
            {
                if (!ReadFields(out var pose)) return;
                var values = PoseFields(pose);
                Vector3 forward = pose.rotation * Vector3.forward;
                GUIUtility.systemCopyBuffer = $"Position: {values[0]}, {values[1]}, {values[2]}\n" +
                    $"Rotation: {values[3]}, {values[4]}, {values[5]}\n" +
                    $"Forward: {PlayerCoordinates.FormatVector(forward, 6)}";
                status = "Copied world position, rotation and forward vector.";
            }

            private void SavePose()
            {
                if (!ReadFields(out var pose)) return;
                try
                {
                    selected.Save(string.Join(", ", PoseFields(pose)));
                    selected.Apply(pose, true);
                    beforePreview = pose;
                    previewing = false;
                    status = "Saved for future visits.";
                }
                catch (Exception error)
                {
                    Log.Warning("Could not save placement for " + selected.Label + ": " + error);
                    status = "Could not save this placement. See the log.";
                }
            }

            private void ClearSavedPose()
            {
                try
                {
                    selected.Save("");
                    beforePreview = selected.Fallback;
                    selected.Apply(beforePreview, true);
                    fields = PoseFields(beforePreview);
                    previewing = false;
                    status = "Restored the normal placement.";
                }
                catch (Exception error)
                {
                    Log.Warning("Could not reset placement for " + selected.Label + ": " + error);
                    status = "Could not clear this placement. See the log.";
                }
            }
        }
    }
}
