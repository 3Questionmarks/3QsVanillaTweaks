using System.Globalization;
using BepInEx.Configuration;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using ModConfig = Config.ConfigManager;

namespace Tweaks.DebugStuff
{
    internal static class PlayerCoordinates
    {
        private static ConfigEntry<bool> showOverlay, logOnKey;
        private static ConfigEntry<KeyboardShortcut> logKey;
        private static ConfigEntry<int> precision;
        private static ConfigEntry<float> overlayX, overlayY;
        private static CoordinateOverlay overlay;

        internal static void Init()
        {
            const string section = "Debug - Player Coordinates";
            showOverlay = ModConfig.BindBool(section, "Show Player Coordinates", true,
                "Show your position, rotation and facing direction on screen.", clientSide: true);
            logOnKey = ModConfig.BindBool(section, "Log Coordinates On Key Press", true,
                "Print your current position, rotation and facing direction to the log when the shortcut is pressed.", clientSide: true);
            logKey = ModConfig.Bind(section, "Log Coordinates Shortcut", new KeyboardShortcut(KeyCode.F8),
                "Shortcut used to print your current coordinates to the log.", clientSide: true);
            precision = ModConfig.Bind(section, "Decimal Places", 2,
                new ConfigDescription("Number of decimal places shown and logged.", new AcceptableValueRange<int>(0, 6)), clientSide: true);
            overlayX = ModConfig.Bind(section, "Overlay X", 16f,
                new ConfigDescription("Distance from the left edge of the screen in pixels.", new AcceptableValueRange<float>(0f, 10000f)), clientSide: true);
            overlayY = ModConfig.Bind(section, "Overlay Y", 16f,
                new ConfigDescription("Distance from the top edge of the screen in pixels.", new AcceptableValueRange<float>(0f, 10000f)), clientSide: true);
            if (overlay) return;
            var root = new GameObject("3Qs Player Coordinate Display");
            UnityEngine.Object.DontDestroyOnLoad(root);
            overlay = root.AddComponent<CoordinateOverlay>();
        }

        private static CharacterBody LocalBody()
        {
            LocalUser local = LocalUserManager.GetFirstLocalUser();
            return local == null ? null : local.cachedBody;
        }

        internal static string FormatVector(Vector3 value, int decimals)
        {
            string format = "F" + decimals;
            return value.x.ToString(format, CultureInfo.InvariantCulture) + ", " +
                value.y.ToString(format, CultureInfo.InvariantCulture) + ", " +
                value.z.ToString(format, CultureInfo.InvariantCulture);
        }

        internal static void Capture(CharacterBody body, out Vector3 position, out Vector3 feet,
            out Vector3 rotation, out Vector3 forward)
        {
            position = body.transform.position;
            feet = body.footPosition;
            // CharacterDirection may rotate a separate transform rather than the body root.
            Transform facing = body.characterDirection ? body.characterDirection.targetTransform : null;
            if (!facing) facing = body.transform;
            rotation = facing.eulerAngles;
            forward = facing.forward;
        }

        private static void Print(CharacterBody body)
        {
            Capture(body, out Vector3 position, out Vector3 feet, out Vector3 rotation, out Vector3 forward);
            int decimals = ModConfig.Get(precision);
            string scene = body.gameObject.scene.name;
            Log.Info("[Player Coordinates] scene=" + scene +
                " | Position: " + FormatVector(position, decimals) +
                " | Feet: " + FormatVector(feet, decimals) +
                " | Rotation (degrees): " + FormatVector(rotation, decimals) +
                " | Forward: " + FormatVector(forward, decimals));
            // Matches the fixed-spawn coordinate/facing format used while mapping stages.
            Log.Info("[Player Placement] scene=" + scene + " | " + FormatVector(feet, decimals) +
                " - " + forward.x.ToString("F" + decimals, CultureInfo.InvariantCulture) + ", " +
                forward.z.ToString("F" + decimals, CultureInfo.InvariantCulture));
        }

        private sealed class CoordinateOverlay : MonoBehaviour
        {
            private GUIStyle style;
            private string text;
            private float nextRefresh;

            private void Update()
            {
                bool show = ModConfig.Get(showOverlay);
                bool log = ModConfig.Get(logOnKey);
                if (!show && !log) { text = null; return; }
                CharacterBody body = LocalBody();
                if (!body) { text = null; return; }
                if (log && ModConfig.Get(logKey).IsDown()) Print(body);
                if (!show) { text = null; return; }
                if (text != null && Time.unscaledTime < nextRefresh) return;
                nextRefresh = Time.unscaledTime + 0.05f;
                Capture(body, out Vector3 position, out Vector3 feet, out Vector3 rotation, out Vector3 forward);
                int decimals = ModConfig.Get(precision);
                text = "Player coordinates | " + body.gameObject.scene.name +
                    "\nPosition: " + FormatVector(position, decimals) +
                    "\nFeet: " + FormatVector(feet, decimals) +
                    "\nRotation (degrees): " + FormatVector(rotation, decimals) +
                    "\nForward: " + FormatVector(forward, decimals) +
                    "\nLog: " + ModConfig.Get(logKey);
            }

            private void OnGUI()
            {
                if (!ModConfig.Get(showOverlay) || string.IsNullOrEmpty(text)) return;
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 16,
                        alignment = TextAnchor.UpperLeft,
                        padding = new RectOffset(10, 10, 8, 8),
                        wordWrap = false,
                        richText = false
                    };
                    style.normal.textColor = Color.white;
                }
                Vector2 size = style.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(ModConfig.Get(overlayX), ModConfig.Get(overlayY), size.x, size.y), text, style);
            }
        }
    }
}
