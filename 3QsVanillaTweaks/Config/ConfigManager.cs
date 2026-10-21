
using BepInEx.Configuration;

namespace ThreeQsVanillaTweaks.Config
{
    internal static class ConfigManager
    {
        internal static ConfigFile File { get; private set; }

        internal static void Init(ConfigFile config)
        {
            File = config;
        }

        internal static ConfigEntry<bool> BindBool(
            string section,
            string key,
            bool defaultValue,
            string description)
        {
            return File.Bind(
                section,
                key,
                defaultValue,
                description);
        }
    }
}
