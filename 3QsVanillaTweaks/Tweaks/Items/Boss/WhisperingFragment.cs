using RoR2;
using RoR2.Items;
using RoR2.Projectile;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
namespace Tweaks.Items.Boss
{
    internal static class WhisperingFragment
    {
        static R c;
        internal static void Init()
        {
            c = BossItemSupport.Config("Whispering Fragment", "WispOnKill", false);
            c.N("Kills Required", 5, "Kills required to create a wisp. Vanilla: 5.", 1);
            c.Pair("Wisp Damage (%)", 360, 0, "Damage dealt by each wisp projectile as a percentage of base damage. Vanilla: 360%.");
            c.Pair("Wisp Charges", 2, 1, "Projectile charges for each wisp. Vanilla: 2 +1 per stack.", 0);
            c.N("Wisp Spawn Interval (Seconds)", .1f, "Time between spawning wisps. Vanilla: 0.1 seconds.", .01f);
            c.N("Orbit Radius (Metres)", 2, "Base orbit radius. Vanilla: 2 metres.");
            c.N("Orbit Radius Per Stack (Metres)", .25f, "Orbit radius added per stack. Vanilla: 0.25 metres.");
            c.Field("RoR2.Items.WispOnKillBehavior", "FixedUpdate", "wispSpawnInterval", () => c.F("Wisp Spawn Interval (Seconds)"));
            c.Field("RoR2.Items.WispOnKillBehavior", "InitializeOrbiter", "baseOrbitRadius", () => c.F("Orbit Radius (Metres)"));
            c.Field("RoR2.Items.WispOnKillBehavior", "InitializeOrbiter", "orbitRadiusPerStack", () => c.F("Orbit Radius Per Stack (Metres)"));
            c.N("Wisp Charges Per Stack", 1, "Additional projectile charges per item stack. Vanilla: +1.", 0);
            c.Field("RoR2.Projectile.WispOnKillProjectileController", "InternalFireWisp", "baseDamageCoefficient", () => c.F("Wisp Damage (%)") / 100f);
            if (c.Enabled.Value)
            {
                var behavior = typeof(CharacterBody).Assembly.GetType("RoR2.Items.WispOnKillBehavior", true);
                U.Harmony.Patch(A.Method(behavior, "OnCharacterDeath"), prefix: new HarmonyMethod(typeof(WhisperingFragment), nameof(Kill)));
                U.Harmony.Patch(A.Method(typeof(WispOnKillProjectileController), "OnEnable"), postfix: new HarmonyMethod(typeof(WhisperingFragment), nameof(ChargesBase)));
                U.Harmony.Patch(A.Method(typeof(WispOnKillProjectileController), "AcquireOwner"), postfix: new HarmonyMethod(typeof(WhisperingFragment), nameof(ChargesStack)));
            }
            c.Text(() => "Killing " + c.F("Kills Required").ToString("0") + " enemies creates a wisp. Wisps deal " + c.V("Wisp Damage (%)", "%") + " damage and have " + c.V("Wisp Charges", " charges") + ".", () => "Killing enemies creates wisps.");
        }

        static bool Kill(WispOnKillBehavior __instance, DamageReport damageReport)
        {
            CharacterBody body = A.Property(typeof(BaseItemBodyBehavior), "body")?.GetValue(__instance, null) as CharacterBody;
            if (!body || damageReport.attackerBody != body) return true;
            var type = __instance.GetType();
            var killsField = AccessTools.Field(type, "killCount");
            var spawnField = AccessTools.Field(type, "wispsToSpawn");
            int kills = (int)killsField.GetValue(__instance) + 1;
            if (kills >= Mathf.Max(1, Mathf.RoundToInt(c.F("Kills Required"))))
            {
                spawnField.SetValue(__instance, (int)spawnField.GetValue(__instance) + 1);
                kills = 0;
            }
            killsField.SetValue(__instance, kills);
            body.SetBuffCount(DLC4Content.Buffs.WispOnKillCounter.buffIndex, kills);
            return false;
        }

        static void ChargesBase(WispOnKillProjectileController __instance) => __instance.charges = Mathf.Max(0, Mathf.RoundToInt(c.F("Wisp Charges")));
        static void ChargesStack(WispOnKillProjectileController __instance, ProjectileController controller)
        {
            CharacterBody body = controller && controller.owner ? controller.owner.GetComponent<CharacterBody>() : null;
            int count = c.Count(body);
            if (count > 0) __instance.charges = Mathf.Max(0, Mathf.RoundToInt(c.F("Wisp Charges") + c.F("Wisp Charges Per Stack") * count));
        }
    }
}
