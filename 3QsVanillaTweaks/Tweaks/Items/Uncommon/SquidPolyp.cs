using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using RoR2;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Uncommon
{
    internal static class SquidPolyp
    {
        const string HoldoutSpawns = "Spawn During Holdout Zones";
        const string SpawnInterval = "Holdout Spawn Interval";
        const string IntervalReduction = "Holdout Spawn Interval Reduction Per Stack (%)";
        const float MinimumInterval = 1f;
        static UncommonConfig c;
        static CharacterSpawnCard squidSpawnCard;
        static readonly ConditionalWeakTable<HoldoutZoneController, ConditionalWeakTable<CharacterMaster, HoldoutTimer>> holdoutTimers =
            new ConditionalWeakTable<HoldoutZoneController, ConditionalWeakTable<CharacterMaster, HoldoutTimer>>();

        sealed class HoldoutTimer
        {
            internal float Elapsed;
        }

        sealed class Lifetime : MonoBehaviour
        {
            internal float Remaining;
            internal CharacterMaster Master;
            void FixedUpdate()
            {
                if (!NetworkServer.active)
                    return;
                Remaining -= Time.fixedDeltaTime;
                if (Remaining <= 0)
                {
                    if (Master)
                        Master.TrueKill();
                    UnityEngine.Object.Destroy(this);
                }
            }
        }

        internal static void Init()
        {
            c = new UncommonConfig("Squid Polyp", "Squid", true);
            c.Pair("Duration", 30, 5, "Turret lifetime in seconds. Vanilla: 30, no duration stacking.");
            c.B("Strict Lifetime", true, "Use a strict lifetime instead of continuously draining health.");
            c.B("Apply Tar On Hit", true, "Squid attacks apply tar.");
            c.Pair("Tar Duration", 2, 0, "Tar duration in seconds. Vanilla: no tar.");
            c.Pair("Health Bonus (%)", 0, 20, "Extra health for the squid. Vanilla: 0%.");
            c.Pair("Attack Speed Bonus (%)", 100, 100, "Extra attack speed. Vanilla: 100% per additional item stack.");
            c.B("Inherit Items", false, "Copy the summoner's inventory to the squid, excluding deployable-generating items to prevent recursion.");
            c.Pair("Damage (%)", 100, 0, "Multiplier for the squid's attack damage. Vanilla: 100%.");
            c.Pair("Proc Coefficient", 1, 0, "Squid attack proc coefficient. Vanilla: 1.");
            c.B(HoldoutSpawns, true, "Periodically summon Squid Turrets near the centre of a charging holdout zone for each living player holding this item. Vanilla: false.");
            c.N(SpawnInterval, 15, "Seconds between Squid Turret spawns during a holdout. Minimum: 1 second. Vanilla: no timed spawns.", MinimumInterval);
            c.N(IntervalReduction, 0, "Percentage reduction to the remaining spawn interval for each additional stack. Vanilla: no timed spawns.", 0, 100);
            if (c.Enabled.Value)
            {
                MasterSummon.onServerMasterSummonGlobal += Spawn;
                U.Patch(typeof(HoldoutZoneController), "Update", typeof(SquidPolyp), postfix: nameof(TickHoldout));
                U.Patch(typeof(HoldoutZoneController), "OnDisable", typeof(SquidPolyp), postfix: nameof(ResetHoldout));
            }
            U.Ensure();
            U.Hit += Hit;
            U.Tick += Tick;
            U.BeforeDamage += Damage;
            c.ShowStat = key => key != IntervalReduction && (key != SpawnInterval || c.B(HoldoutSpawns));
            c.StatValue = (key, n, value) => key == SpawnInterval ? HoldoutInterval(n) : value;
            c.Text(() => "Activating an interactable summons a <style=cIsDamage>Squid Turret</style> for " + c.V("Duration", "s", "cIsUtility") + "." +
                (c.B(HoldoutSpawns) ? HoldoutDescription() : "") +
                (c.B("Apply Tar On Hit") ? " Its attacks <style=cIsUtility>tar</style> enemies." : ""),
                () => "Summon <style=cIsDamage>Squid Turrets</style> by activating interactables" +
                    (c.B(HoldoutSpawns) ? " or during holdout events." : "."));
        }

        static float HoldoutInterval(int stacks) => Mathf.Max(MinimumInterval,
            UncommonCalculations.StackCooldown(c.F(SpawnInterval), c.F(IntervalReduction), stacks));

        static string HoldoutDescription()
        {
            float reduction = c.F(IntervalReduction);
            return " During a charging holdout event, summon a turret near the centre every " + c.V(SpawnInterval, "s", "cIsUtility") +
                (reduction > 0 ? " <style=cStack>(-" + reduction.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "% per stack)</style>" : "") + ".";
        }

        static void ResetHoldout(HoldoutZoneController __instance) => holdoutTimers.Remove(__instance);

        static void TickHoldout(HoldoutZoneController __instance)
        {
            var zone = __instance;
            if (!NetworkServer.active)
                return;
            if (!c.Active || !c.B(HoldoutSpawns) || zone.isFullyCharged)
            {
                holdoutTimers.Remove(zone);
                return;
            }
            if (zone.currentChargeRateServer <= 0f || zone.currentRadius <= 0f)
                return;

            var timers = holdoutTimers.GetOrCreateValue(zone);
            foreach (var player in PlayerCharacterMasterController.instances)
            {
                var master = player ? player.master : null;
                if (!master)
                    continue;
                var body = master.GetBody();
                int stacks = c.Count(body);
                if (stacks <= 0 || master.teamIndex != zone.chargingTeam || !body.healthComponent || !body.healthComponent.alive)
                {
                    timers.Remove(master);
                    continue;
                }

                var timer = timers.GetOrCreateValue(master);
                timer.Elapsed += Time.deltaTime;
                float interval = HoldoutInterval(stacks);
                if (timer.Elapsed >= interval)
                {
                    timer.Elapsed = 0f;
                    SummonAtHoldout(zone, body);
                }
            }
        }

        static void SummonAtHoldout(HoldoutZoneController zone, CharacterBody owner)
        {
            var director = DirectorCore.instance;
            if (!director)
                return;
            if (!squidSpawnCard)
                squidSpawnCard = LegacyResourcesAPI.Load<CharacterSpawnCard>("SpawnCards/CharacterSpawnCards/cscSquidTurret");
            if (!squidSpawnCard)
                return;

            float radius = Mathf.Min(12f, zone.currentRadius);
            var placement = new DirectorPlacementRule
            {
                placementMode = DirectorPlacementRule.PlacementMode.Approximate,
                position = zone.transform.position,
                minDistance = Mathf.Min(3f, radius * 0.5f),
                maxDistance = radius
            };
            var request = new DirectorSpawnRequest(squidSpawnCard, placement, RoR2Application.rng)
            {
                teamIndexOverride = zone.chargingTeam,
                summonerBodyObject = owner.gameObject
            };
            if (!director.TrySpawnObject(request))
            {
                placement.minDistance = 0f;
                director.TrySpawnObject(request);
            }
        }

        static readonly string[] excluded =
        {
            "Squid",
            "DronesDropDynamite",
            "PhysicsProjectile",
            "MinorConstructOnKill",
            "BeetleGland",
            "RoboBallBuddy",
            "DroneWeapons"
        };
        static void Spawn(MasterSummon.MasterSummonReport r)
        {
            var master = r.summonMasterInstance;
            if (!master || !master.name.StartsWith("SquidTurret", StringComparison.Ordinal) || !r.leaderMasterInstance)
                return;
            var owner = r.leaderMasterInstance.GetBody();
            int n = c.Count(owner);
            if (n == 0)
                return;
            var body = master.GetBody();
            if (!body)
                return;
            if (configured.TryGetValue(master, out var existing))
                return;
            configured.Add(master, new Setup { Stacks = n });
            var life = master.gameObject.AddComponent<Lifetime>();
            life.Master = master;
            life.Remaining = c.Total("Duration", n);
            if (c.B("Inherit Items"))
                foreach (var index in ItemCatalog.allItems)
                {
                    var def = ItemCatalog.GetItemDef(index);
                    if (Array.IndexOf(excluded, def.name) >= 0)
                        continue;
                    int count = owner.inventory.GetItemCountEffective(index);
                    if (count > 0)
                        master.inventory.GiveItemPermanent(index, count);
                }

            if (!c.B("Strict Lifetime"))
            {
                UnityEngine.Object.Destroy(life);
                master.inventory.ResetItemPermanent(RoR2Content.Items.HealthDecay);
                master.inventory.GiveItemPermanent(RoR2Content.Items.HealthDecay, Mathf.Max(1, Mathf.RoundToInt(c.Total("Duration", n))));
            }
            else
                master.inventory.ResetItemPermanent(RoR2Content.Items.HealthDecay);
            master.inventory.ResetItemPermanent(RoR2Content.Items.BoostAttackSpeed);
            body.baseMaxHealth *= 1 + c.Total("Health Bonus (%)", n) / 100;
            body.levelMaxHealth *= 1 + c.Total("Health Bonus (%)", n) / 100;
            body.baseAttackSpeed *= 1 + c.Total("Attack Speed Bonus (%)", n) / 100;
            body.levelAttackSpeed *= 1 + c.Total("Attack Speed Bonus (%)", n) / 100;
            body.baseDamage *= c.Total("Damage (%)", n) / 100;
            body.levelDamage *= c.Total("Damage (%)", n) / 100;
            body.MarkAllStatsDirty();
        }

        sealed class Setup
        {
            internal int Stacks;
        }

        static readonly ConditionalWeakTable<CharacterMaster, Setup> configured = new ConditionalWeakTable<CharacterMaster, Setup>();
        static void Tick(CharacterBody b)
        {
            // Runs for every body each physics tick: check ownership before reading the
            // GameObject name, which allocates a new string on every access.
            if (!c.Active || !b.master || !b.master.minionOwnership || !b.master.minionOwnership.ownerMaster || !b.name.StartsWith("SquidTurret", StringComparison.Ordinal))
                return;
            if (!configured.TryGetValue(b.master, out var marker))
            {
                Spawn(new MasterSummon.MasterSummonReport { summonMasterInstance = b.master, leaderMasterInstance = b.master.minionOwnership.ownerMaster });
            }

            if (configured.TryGetValue(b.master, out var setup))
            {
                // Only touch the networked inventory when there is something to remove.
                if (b.inventory.GetItemCountPermanent(RoR2Content.Items.BoostAttackSpeed) > 0)
                    b.inventory.ResetItemPermanent(RoR2Content.Items.BoostAttackSpeed);
                int desired = c.B("Strict Lifetime") ? 0 : Mathf.Max(1, Mathf.RoundToInt(c.Total("Duration", setup.Stacks)));
                int actual = b.inventory.GetItemCountPermanent(RoR2Content.Items.HealthDecay);
                if (actual != desired)
                {
                    b.inventory.ResetItemPermanent(RoR2Content.Items.HealthDecay);
                    if (desired > 0)
                        b.inventory.GiveItemPermanent(RoR2Content.Items.HealthDecay, desired);
                }
            }
        }

        static void Damage(HealthComponent victim, DamageInfo info)
        {
            var b = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null;
            if (!c.Active || !b || !b.name.StartsWith("SquidTurret", StringComparison.Ordinal))
                return;
            var owner = b.master?.minionOwnership?.ownerMaster;
            int n = owner ? c.Count(owner.GetBody()) : 0;
            if (n > 0 && info.dotIndex == DotController.DotIndex.None)
                info.procCoefficient = c.Total("Proc Coefficient", n);
        }

        static void Hit(DamageReport r)
        {
            if (!c.Active || !c.B("Apply Tar On Hit") || !r.attackerBody || !r.attackerBody.name.StartsWith("SquidTurret", StringComparison.Ordinal) || !r.victimBody)
                return;
            var owner = r.attackerBody.master?.minionOwnership?.ownerMaster;
            int n = owner ? c.Count(owner.GetBody()) : 0;
            if (n > 0 && r.damageInfo.procCoefficient > 0)
                r.victimBody.AddTimedBuff(RoR2Content.Buffs.ClayGoo, c.Total("Tar Duration", n));
        }
    }
}
