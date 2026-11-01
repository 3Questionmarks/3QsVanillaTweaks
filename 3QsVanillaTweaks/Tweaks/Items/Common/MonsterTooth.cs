using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class MonsterTooth
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        static ConfigEntry<float> v3;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Monster Tooth");
            v0 = settings.Number("Flat Healing", 8f, "Flat health restored by the first-stack orb. Vanilla: 8.");
            v1 = settings.Number("Flat Healing Per Stack", 0f, "Additional flat health per stack. Vanilla: 0.");
            v2 = settings.Number("Healing (%)", 2f, "Maximum health restored by the first-stack orb. Vanilla: 2%.");
            v3 = settings.Number("Healing Per Stack (%)", 2f, "Additional percentage healing per stack. Vanilla: 2%.");
            settings.Literal("RoR2.GlobalEventManager", "OnCharacterDeath", 8f, () => S.Linear(S.Count(VanillaCommonSupport.Body, RoR2Content.Items.Tooth), C.Get(v0), C.Get(v1)), "Tooth", window: 110);
            settings.Count("RoR2.GlobalEventManager", "OnCharacterDeath", "Tooth", .02f, v2, v3, 100);
            settings.Describe("ITEM_TOOTH_DESC", () => "Killing an enemy drops a <style=cIsHealing>healing orb</style> that restores <style=cIsHealing>" + C.Get(v0) + "</style> <style=cStack>(+" + C.Get(v1) + " per stack)</style> health plus <style=cIsHealing>" + C.Get(v2) + "%</style> <style=cStack>(+" + C.Get(v3) + "% per stack)</style> maximum health.");
        }
    }
}
