using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class Medkit
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        static ConfigEntry<float> v3;
        static ConfigEntry<float> v4;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Medkit");
            v0 = settings.Number("Flat Healing", 20f, "Flat healing after taking damage. Vanilla: 20.");
            v1 = settings.Number("Flat Healing Per Stack", 0f, "Additional flat healing per stack. Vanilla: 0.");
            v2 = settings.Number("Healing (%)", 5f, "First-stack maximum-health healing. Vanilla: 5%.");
            v3 = settings.Number("Healing Per Stack (%)", 5f, "Additional percentage healing per stack. Vanilla: 5%.");
            v4 = settings.Number("Delay", 2f, "Seconds without another hit before healing. Vanilla: 2.");
            settings.Literal("RoR2.CharacterBody", "RemoveBuffInternal", 20f, () => S.Linear(S.Count(VanillaCommonSupport.Body, RoR2Content.Items.Medkit), C.Get(v0), C.Get(v1)), "Medkit", window: 30);
            settings.Count("RoR2.CharacterBody", "RemoveBuffInternal", "Medkit", .05f, v2, v3, 100);
            settings.Literal("RoR2.HealthComponent", "UpdateLastHitTime", 2f, () => C.Get(v4), "MedkitHeal", window: 10);
            settings.Describe("ITEM_MEDKIT_DESC", () => "After <style=cIsUtility>" + C.Get(v4) + " seconds</style> without another damaging hit, <style=cIsHealing>heal</style> for <style=cIsHealing>" + C.Get(v0) + "</style> <style=cStack>(+" + C.Get(v1) + " per stack)</style> plus <style=cIsHealing>" + C.Get(v2) + "%</style> <style=cStack>(+" + C.Get(v3) + "% per stack)</style> maximum health.");
        }
    }
}
