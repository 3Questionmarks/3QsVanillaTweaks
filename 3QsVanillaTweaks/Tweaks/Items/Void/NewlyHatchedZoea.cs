using System;
using System.Runtime.CompilerServices;
using RoR2;
using UnityEngine;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
namespace Tweaks.Items.Void
{
    internal static class NewlyHatchedZoea
    {
        static R c;
        sealed class Ally { internal CharacterMaster Owner; internal float Damage = float.NaN; }
        static readonly ConditionalWeakTable<CharacterMaster, Ally> allies = new ConditionalWeakTable<CharacterMaster, Ally>();
        internal static void Init()
        {
            U.Ensure(); c = new R("Newly Hatched Zoea", "VoidMegaCrabItem", true, "Void");
            c.N("Respawn Cooldown", 60, "Seconds between ally spawn attempts. Vanilla: 60 seconds divided by item count.", .1f);
            c.Pair("Cooldown Reduction (%)", 0, 0, "Multiplicative reduction to respawn cooldown; each extra stack contributes its configured reduction. Vanilla instead divides by item count.", 0);
            c.Pair("Ally Damage Bonus (%)", 0, 100, "Additional ally damage. Vanilla: no bonus from stacking.");
            c.Pair("Maximum Allies", 1, 0, "Maximum living allies. Vanilla: 1 +1 per stack.");
            c.N("Minimum Spawn Distance", 3, "Minimum distance in metres. Vanilla: 3.");
            c.N("Maximum Spawn Distance", 40, "Maximum distance in metres. Vanilla: 40.");
            c.N("Void Jailer Spawn Weight", 15, "Relative chance to summon a Void Jailer. Vanilla: 15.");
            c.N("Void Reaver Spawn Weight", 15, "Relative chance to summon a Void Reaver. Vanilla: 15.");
            c.N("Void Devastator Spawn Weight", 1, "Relative chance to summon a Void Devastator. Vanilla: 1.");
            c.Field("RoR2.VoidMegaCrabItemBehavior", "FixedUpdate", "baseSecondsPerSpawn", () => Duration(c.Count(N.Body)) * Mathf.Max(1, c.Count(N.Body)));
            c.Field("RoR2.VoidMegaCrabItemBehavior", "OnEnable", "minSpawnDist", () => c.F("Minimum Spawn Distance"));
            c.Field("RoR2.VoidMegaCrabItemBehavior", "OnEnable", "maxSpawnDist", () => Mathf.Max(c.F("Minimum Spawn Distance"), c.F("Maximum Spawn Distance")));
            c.Literal("RoR2.VoidMegaCrabItemBehavior", "OnEnable", 15, () => SpawnWeight("Void Jailer Spawn Weight", 15), null, 0);
            c.Literal("RoR2.VoidMegaCrabItemBehavior", "OnEnable", 15, () => SpawnWeight("Void Reaver Spawn Weight", 15), null, 1);
            c.Literal("RoR2.VoidMegaCrabItemBehavior", "OnEnable", 1, () => SpawnWeight("Void Devastator Spawn Weight", 1));
            if (c.Enabled.Value)
            {
                U.Patch(typeof(VoidMegaCrabItemBehavior), "GetMaxProjectiles", typeof(NewlyHatchedZoea), postfix: nameof(Maximum));
                U.Patch(typeof(VoidMegaCrabItemBehavior), "OnMasterSpawned", typeof(NewlyHatchedZoea), postfix: nameof(Spawned));
                U.Stats += Stats; U.Tick += Tick;
            }
            c.StatValue = (key, n, value) => key == "Respawn Cooldown" ? Duration(n) : value;
            c.Text(() => "Every <style=cIsUtility>" + c.F("Respawn Cooldown").ToString("0.##") + " seconds</style>, gain a random <style=cIsVoid>Void ally</style>. Can have up to " + c.V("Maximum Allies", "", "cIsUtility") + " allies at a time." + (c.F("Ally Damage Bonus (%)") == 0 && c.F("Ally Damage Bonus (%) Per Stack") == 0 ? "" : " Allies gain " + c.V("Ally Damage Bonus (%)", "%") + " bonus damage.") + (c.F("Cooldown Reduction (%)") == 0 && c.F("Cooldown Reduction (%) Per Stack") == 0 ? "" : " Reduce the summon cooldown by " + c.V("Cooldown Reduction (%)", "%", "cIsUtility") + ".") + " <style=cIsVoid>Corrupts all yellow items</style>.", () => "Periodically recruit a Void ally. <style=cIsVoid>Corrupts all yellow items</style>.");
        }
        static float SpawnWeight(string key, float vanilla) => c.F("Void Jailer Spawn Weight") + c.F("Void Reaver Spawn Weight") + c.F("Void Devastator Spawn Weight") > 0 ? c.F(key) : vanilla;
        internal static float Duration(int n) => Mathf.Max(.1f, c.F("Respawn Cooldown") * Mathf.Max(0, 1-c.F("Cooldown Reduction (%)")/100) * Mathf.Pow(Mathf.Max(0,1-c.F("Cooldown Reduction (%) Per Stack")/100), Mathf.Max(0,n-1)));
        static void Maximum(Inventory inventory, ref int __result)
        { if (c.Active && inventory) { int n = inventory.GetItemCountEffective(c.Item); __result = n > 0 ? Mathf.Max(0, Mathf.RoundToInt(c.Total("Maximum Allies", n))) : 0; } }
        static void Spawned(VoidMegaCrabItemBehavior __instance, SpawnCard.SpawnResult spawnResult)
        {
            if (!c.Active || !spawnResult.spawnedInstance) return;
            var master = spawnResult.spawnedInstance.GetComponent<CharacterMaster>(); if (!master) return;
            var ally = allies.GetOrCreateValue(master); ally.Owner = __instance.GetComponent<CharacterBody>()?.master;
            var body = master.GetBody(); if (body) body.RecalculateStats();
        }
        static int OwnerCount(CharacterMaster owner) => c.Active && owner && owner.inventory ? owner.inventory.GetItemCountEffective(c.Item) : 0;
        static void Stats(CharacterBody body)
        { if (body.master && allies.TryGetValue(body.master, out var a)) U.Scale(body, "damage", c.Total("Ally Damage Bonus (%)", OwnerCount(a.Owner))); }
        static void Tick(CharacterBody body)
        {
            if (!body.master || !allies.TryGetValue(body.master, out var a)) return;
            float damage = c.Total("Ally Damage Bonus (%)", OwnerCount(a.Owner));
            if (a.Damage != damage) { a.Damage = damage; body.RecalculateStats(); }
        }
    }
}
