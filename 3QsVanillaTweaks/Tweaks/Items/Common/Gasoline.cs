using BepInEx.Configuration;
using RoR2;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;

namespace Tweaks.Items.Common
{
    internal static class Gasoline
    {
        static VanillaCommonConfig settings;
        static ConfigEntry<float> v0;
        static ConfigEntry<float> v1;
        static ConfigEntry<float> v2;
        static ConfigEntry<float> v3;
        static ConfigEntry<float> v4;
        static ConfigEntry<float> v5;
        static ConfigEntry<float> v6;
        internal static void Init()
        {
            settings = new VanillaCommonConfig("Gasoline");
            v0 = settings.Number("Explosion Damage (%)", 150f, "Initial explosion damage. Vanilla: 150%.");
            v1 = settings.Number("Explosion Damage Per Stack (%)", 0f, "Additional initial damage per stack. Vanilla: 0%.");
            v2 = settings.Number("Burn Damage (%)", 150f, "Total burn damage from one stack. Vanilla: 150%.");
            v3 = settings.Number("Burn Damage Per Stack (%)", 75f, "Additional total burn damage per stack. Vanilla: 75%.");
            v4 = settings.Number("Radius", 12f, "Base explosion radius in metres, before adding the victim radius. Vanilla: 12.");
            v5 = settings.Number("Radius Per Stack", 4f, "Additional explosion radius per stack. Vanilla: 4.");
            v6 = settings.Number("Proc Coefficient", 0f, "Initial explosion proc coefficient. Vanilla: 0.");
            settings.Literal("RoR2.GlobalEventManager", "ProcIgniteOnKill", 8f, () => C.Get(v4) - C.Get(v5));
            settings.Literal("RoR2.GlobalEventManager", "ProcIgniteOnKill", 4f, () => C.Get(v5));
            settings.Literal("RoR2.GlobalEventManager", "ProcIgniteOnKill", 1.5f, () => S.Linear(S.Count(VanillaCommonSupport.Body, RoR2Content.Items.IgniteOnKill), C.Get(v0), C.Get(v1)) / 100);
            settings.Literal("RoR2.GlobalEventManager", "ProcIgniteOnKill", .75f, () =>
            {
                int n = S.Count(VanillaCommonSupport.Body, RoR2Content.Items.IgniteOnKill);
                return S.Linear(n, C.Get(v2), C.Get(v3)) / 100 / (1 + n);
            });
            settings.Literal("RoR2.GlobalEventManager", "ProcIgniteOnKill", 0f, () => C.Get(v6));
            settings.Describe("ITEM_IGNITEONKILL_DESC", () => "Killing an enemy <style=cIsDamage>ignites</style> enemies within <style=cIsUtility>" + C.Get(v4) + "m</style> <style=cStack>(+" + C.Get(v5) + "m per stack)</style>, plus the victim radius. Deals <style=cIsDamage>" + C.Get(v0) + "%</style> <style=cStack>(+" + C.Get(v1) + "% per stack)</style> initial damage and <style=cIsDamage>" + C.Get(v2) + "%</style> <style=cStack>(+" + C.Get(v3) + "% per stack)</style> total burn damage.");
        }
    }
}
