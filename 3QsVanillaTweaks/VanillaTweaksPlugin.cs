using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace ThreeQsVanillaTweaks
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    [BepInDependency(R2API.PrefabAPI.PluginGUID)]
    [BepInDependency(R2API.ColorsAPI.PluginGUID)]
    [BepInDependency("Viliger.RegisterCommandChest", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(RetroactiveMacroGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class VanillaTweaksPlugin : BaseUnityPlugin
    {
        public const string PluginAuthor = "3Questionmarks";
        public const string PluginName = "3QsVanillaTweaks";
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginVersion = "0.5.2";
        internal const string RetroactiveMacroGuid = "Onyx.RetroactiveMacro";

        private void Awake()
        {
            Log.Init(Logger);
            var startupTimer = System.Diagnostics.Stopwatch.StartNew();
            InitializeConfigManager();
            Log.Info("[Startup] Config initialisation: " + startupTimer.Elapsed.TotalSeconds.ToString("F2") + " s.");

            Tweaks.Items.Uncommon.UncommonNative.BeginRegistration();
            try { InitializeTweaks(false); }
            finally { Tweaks.Items.Uncommon.UncommonNative.FinishRegistration(); }
            Log.Info("[Startup] Awake total: " + startupTimer.Elapsed.TotalSeconds.ToString("F2") + " s.");

            Log.Info("3QsVanillaTweaks initialized (artifact registration scheduled for Start).");
        }

        private void Update()
        {
            global::Config.ConfigManager.Tick();
        }

        // BepInEx has not finished registering this plugin's metadata while Awake runs.
        // R2API's ContentAddition.AddArtifactDef must be called after that registration,
        // but before RoR2 builds its content catalogues. Unity's Start is the correct
        // point for the artifact registration while ordinary tweaks stay in Awake.
        private void Start()
        {
            InitializeTweaks(true);
        }

        // Resolve the project ConfigManager independently of its namespace.
        // This supports either Config.ConfigManager or the older
        // ThreeQsVanillaTweaks.Config.ConfigManager after namespace changes.
        private void InitializeConfigManager()
        {
            Type configType = Assembly.GetExecutingAssembly().GetTypes()
                .FirstOrDefault(type => type.IsClass && type.Name == "ConfigManager" &&
                    type.GetMethod("Init", BindingFlags.Static | BindingFlags.Public |
                        BindingFlags.NonPublic, null, new[] { typeof(ConfigFile) }, null) != null);

            if (configType == null)
            {
                Log.Warning("Could not find ConfigManager.Init(ConfigFile); tweak initialization skipped.");
                return;
            }

            MethodInfo init = configType.GetMethod("Init", BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic, null,
                new[] { typeof(ConfigFile) }, null);
            init.Invoke(null, new object[] { Config });
        }

        private void InitializeTweaks(bool artifactsOnly)
        {
            const string tweakNamespace = "Tweaks";

            Type[] tweakTypes = Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(type =>
                    type.IsClass &&
                    type.Namespace != null &&
                    (type.Namespace == tweakNamespace ||
                     type.Namespace.StartsWith(
                         tweakNamespace + ".",
                         StringComparison.Ordinal)))
                .OrderBy(type => type.FullName)
                .ToArray();

            int initialized = 0;

            foreach (Type type in tweakTypes)
            {
                bool isArtifact = type.Namespace == "Tweaks.RoRArtifacts" ||
                    type.Namespace.StartsWith("Tweaks.RoRArtifacts.", StringComparison.Ordinal);
                if (isArtifact != artifactsOnly) continue;

                MethodInfo initMethod = type.GetMethod(
                    "Init",
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                if (initMethod == null || initMethod.ReturnType != typeof(void))
                    continue;

                if (!global::Config.ConfigManager.ModuleEnabled(type))
                {
                    Log.Info("Module disabled; skipped tweak: " + type.Name);
                    continue;
                }

                var moduleTimer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    initMethod.Invoke(null, null);

                    initialized++;

                    Log.Info("Initialized tweak: " + type.Name + " [Startup: " + moduleTimer.Elapsed.TotalMilliseconds.ToString("F0") + " ms]");
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "Failed to initialize tweak " +
                        type.FullName + ": " + exception);
                }
            }

            Log.Info("Initialized " + initialized +
                (artifactsOnly ? " artifact module(s)." : " non-artifact tweak(s)."));
        }
    }
}
