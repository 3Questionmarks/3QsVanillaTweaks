using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using ThreeQsVanillaTweaks.Config;

namespace ThreeQsVanillaTweaks
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(R2API.LanguageAPI.PluginGUID)]
    public class VanillaTweaksPlugin : BaseUnityPlugin
    {
        public const string PluginAuthor = "3Questionmarks";
        public const string PluginName = "3QsVanillaTweaks";
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginVersion = "0.0.1";

        private void Awake()
        {
            Log.Init(Logger);
            ConfigManager.Init(Config);

            InitializeTweaks();

            Log.Info("3QsVanillaTweaks initialized.");
        }

        private void InitializeTweaks()
        {
            const string tweakNamespace = "ThreeQsVanillaTweaks.Tweaks";

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

                try
                {
                    initMethod.Invoke(null, null);

                    initialized++;

                    Log.Info("Initialized tweak: " + type.Name);
                }
                catch (Exception exception)
                {
                    Log.Warning(
                        "Failed to initialize tweak " +
                        type.FullName + ": " + exception);
                }
            }

            Log.Info("Initialized " + initialized + " tweak(s).");
        }
    }
}
