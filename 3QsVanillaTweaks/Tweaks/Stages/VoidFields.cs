using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ActiveCell = EntityStates.Missions.Arena.NullWard.Active;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class VoidFields
    {
        private static ConfigEntry<bool> enabled, loot, stopTime, fullSpeed;
        private static ConfigEntry<int> rounds;
        private static ConfigEntry<float> healing, credits, killCharge, radius, duration;
        [ThreadStatic] private static bool grantingItems;

        internal static void Init()
        {
            const string section = "Stages - Void Fields";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable Void Fields changes.");
            rounds = ModConfig.Bind(section, "Void Cells", 5, new ConfigDescription(
                "Number of cells to complete. 3: one common, one uncommon and one legendary reward. 5: two common, two uncommon and one legendary reward. In these modes, enemies gain 5 common stacks in common rounds, 3 uncommon stacks in uncommon rounds and 1 legendary stack in the final round. 9: vanilla progression.",
                new AcceptableValueList<int>(3, 5, 9)));
            healing = Number(section, "Health On Activation (%)", 25f, 0f, 100f,
                "Restore this percentage of each living player's maximum health when a cell is activated. Vanilla: 0.");
            loot = ModConfig.BindBool(section, "Spawn Loot", true, "Allow loot to spawn in Void Fields. Vanilla: True.");
            credits = Number(section, "Loot Credits Multiplier", 0.5f, 0f, 100f,
                "Multiply the credits available to spawn loot. Vanilla: 1.");
            stopTime = ModConfig.BindBool(section, "Stop Time", true,
                "Stop the run timer throughout Void Fields. Vanilla: True");
            killCharge = Number(section, "Charge Per Kill (%)", 1f, 0f, 100f,
                "Charge added to the active cell when the player team kills an enemy. Vanilla: 0.");
            radius = Number(section, "Cell Radius", 20f, 1f, 1000f, "Radius of an active cell in metres. Vanilla: 20.");
            duration = Number(section, "Cell Charge Time", 60f, 1f, 3600f,
                "Seconds required to fully charge a cell at full speed. Vanilla: 60.");
            fullSpeed = ModConfig.BindBool(section, "Full Speed With One Player", true,
                "Charge at full speed with at least one player inside the cell. Vanilla: False.");

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.VoidFields");
            Patch(harmony, typeof(ArenaMissionController), "Awake", null, nameof(PrepareMission));
            Patch(harmony, typeof(ArenaMissionController), "BeginRound", nameof(BeforeRound));
            Patch(harmony, typeof(ArenaMissionController), "AddItemStack", nameof(BeforeAddItems));
            Patch(harmony, typeof(ActiveCell), "OnEnter", null, nameof(AfterCellActivated));
            Patch(harmony, typeof(ActiveCell), "OnExit", nameof(BeforeCellClosed));
            Patch(harmony, typeof(Run), "ShouldUpdateRunStopwatch", nameof(UpdateStopwatch));
            SceneDirector.onPrePopulateSceneServer += ConfigureLoot;
            // Newer builds expose a death event independent of item proc rolls.
            // Older builds route every death directly through OnCharacterDeath.
            var deathEvent = typeof(GlobalEventManager).GetEvent("onCharacterDeathServer");
            if (deathEvent != null) deathEvent.AddEventHandler(null, (Action<DamageReport>)EnemyKilled);
            else Patch(harmony, typeof(GlobalEventManager), "OnCharacterDeath", null, nameof(EnemyKilled));
        }

        private static ConfigEntry<float> Number(string section, string key, float value, float minimum, float maximum, string description)
        {
            return ModConfig.Bind(section, key, value, new ConfigDescription(description,
                new AcceptableValueRange<float>(minimum, maximum)));
        }

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null)
        {
            harmony.Patch(AccessTools.Method(type, method),
                prefix: prefix == null ? null : new HarmonyMethod(typeof(VoidFields), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(VoidFields), postfix));
        }

        private static bool Active => ModConfig.Get(enabled) && Run.instance &&
            SceneCatalog.mostRecentSceneDef && SceneCatalog.mostRecentSceneDef.baseSceneName == "arena";

        internal sealed class MissionSettings : MonoBehaviour
        {
            internal bool Short;
        }

        private static void PrepareMission(ArenaMissionController __instance)
        {
            if (!Active) return;
            int count = ModConfig.Get(rounds);
            var settings = __instance.gameObject.AddComponent<MissionSettings>();
            settings.Short = count == 3 || count == 5;
            // Leave the entire native reward and enemy progression intact in nine-round mode.
            if (!settings.Short) return;
            if (__instance.nullWards.Length < count || __instance.playerRewardOrder.Length < 3 ||
                __instance.monsterItemStackOrder.Length < 3)
            {
                settings.Short = false;
                ThreeQsVanillaTweaks.Log.Warning("Could not configure Void Fields rounds; using vanilla progression.");
                return;
            }
            if (NetworkServer.active && !EnsureDirectors(__instance, count))
            {
                settings.Short = false;
                return;
            }

            // The native controller shuffles all cells before activating the first one.
            // Keeping its full cell array preserves random locations and the native fog setup.
            // Only the configured number are activated; the rest remain dormant.
            __instance.totalRoundsMax = count;
            PickupDropTable[] rewards = __instance.playerRewardOrder;
            ArenaMissionController.MonsterItemStackData[] items = __instance.monsterItemStackOrder;
            var newRewards = new PickupDropTable[count];
            var newItems = new ArenaMissionController.MonsterItemStackData[count];
            for (int i = 0; i < count; i++)
            {
                int tier = TierForRound(count, i);
                int rewardIndex = tier == 0 ? 0 : tier == 1 ? rewards.Length - 2 : rewards.Length - 1;
                int itemIndex = tier == 0 ? 0 : tier == 1 ? items.Length - 2 : items.Length - 1;
                newRewards[i] = rewards[rewardIndex];
                newItems[i] = new ArenaMissionController.MonsterItemStackData
                {
                    dropTable = items[itemIndex].dropTable,
                    stacks = tier == 0 ? 5 : tier == 1 ? 3 : 1
                };
            }
            __instance.playerRewardOrder = newRewards;
            __instance.monsterItemStackOrder = newItems;
        }

        internal static int TierForRound(int count, int index)
        {
            return index == count - 1 ? 2 : index < (count - 1) / 2 ? 0 : 1;
        }

        private static bool EnsureDirectors(ArenaMissionController mission, int count)
        {
            CombatDirector[] directors = mission.combatDirectors;
            if (directors != null && directors.Length >= count) return true;
            CombatDirector template = null;
            if (directors != null)
                foreach (CombatDirector director in directors)
                    if (director) { template = director; break; }
            if (!template)
            {
                ThreeQsVanillaTweaks.Log.Warning("Could not configure Void Fields enemy types; using vanilla progression.");
                return false;
            }

            // Each active enemy type needs its own native director. Five-round mode
            // can need more than the scene supplies. Copy only the director settings,
            // rather than cloning its whole object (which may contain the mission).
            int originalCount = directors.Length;
            Array.Resize(ref directors, count);
            for (int i = originalCount; i < count; i++)
            {
                var holder = new GameObject("3Qs Void Fields Enemy Director " + (i + 1));
                holder.SetActive(false);
                holder.transform.SetParent(mission.transform, false);
                CombatDirector extra = holder.AddComponent<CombatDirector>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(template), extra);
                extra.enabled = false;
                extra.monsterCredit = 0f;
                directors[i] = extra;
                holder.SetActive(true);
            }
            // OnStartServer will initialise these alongside the existing directors,
            // including the callback which gives spawned enemies their item stacks.
            mission.combatDirectors = directors;
            return true;
        }

        private static void BeforeRound(ArenaMissionController __instance)
        {
            if (!NetworkServer.active) return;
            MissionSettings settings = __instance.GetComponent<MissionSettings>();
            if (!settings || !settings.Short) return;
            // Native BeginRound introduces enemies on rounds 1, 3 and 5. Its round
            // counter has not incremented yet, so fill the gaps before rounds 2 and 4.
            if ((__instance.currentRound & 1) != 0)
                AccessTools.Method(typeof(ArenaMissionController), "AddMonsterType").Invoke(__instance, null);
            // BeginRound normally grants an item on alternating rounds. Grant exactly once
            // here, before any enemies spawn, and suppress those native alternating grants.
            bool previous = grantingItems;
            try
            {
                grantingItems = true;
                AccessTools.Method(typeof(ArenaMissionController), "AddItemStack").Invoke(__instance, null);
            }
            finally { grantingItems = previous; }
        }

        private static bool BeforeAddItems(ArenaMissionController __instance)
        {
            MissionSettings settings = __instance.GetComponent<MissionSettings>();
            return !settings || !settings.Short || grantingItems;
        }

        private static void ConfigureLoot(SceneDirector director)
        {
            if (!NetworkServer.active || !Active) return;
            director.interactableCredit = ModConfig.Get(loot)
                ? Mathf.FloorToInt(director.interactableCredit * ModConfig.Get(credits)) : 0;
        }

        private static bool UpdateStopwatch(Run __instance, ref bool __result)
        {
            if (!Active) return true;
            // Override both the scene type and the cell's temporary force-pause requests.
            __result = !ModConfig.Get(stopTime) && __instance.livingPlayerCount > 0;
            return false;
        }

        internal sealed class CellSettings : MonoBehaviour
        {
            internal HoldoutZoneController Zone;
            internal bool Charging;
            internal float PendingCharge;
            internal void ApplyKills(ref float charge)
            {
                if (!Charging) { PendingCharge = 0f; return; }
                charge = Mathf.Clamp01(charge + PendingCharge);
                PendingCharge = 0f;
            }
            private void OnDestroy()
            {
                if (Zone) Zone.calcAccumulatedCharge -= ApplyKills;
            }
        }

        private static void AfterCellActivated(HoldoutZoneController ___holdoutZoneController)
        {
            if (!Active) return;
            HoldoutZoneController zone = ___holdoutZoneController;
            if (!zone) return;
            zone.baseRadius = ModConfig.Get(radius);
            zone.baseChargeDuration = ModConfig.Get(duration);
            if (ModConfig.Get(fullSpeed)) zone.playerCountScaling = 0f;
            if (!NetworkServer.active) return;
            CellSettings cell = zone.GetComponent<CellSettings>();
            if (!cell)
            {
                cell = zone.gameObject.AddComponent<CellSettings>();
                cell.Zone = zone;
                zone.calcAccumulatedCharge += cell.ApplyKills;
            }
            cell.Charging = true;
            cell.PendingCharge = 0f;
            float fraction = ModConfig.Get(healing) / 100f;
            if (fraction <= 0f) return;
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                CharacterBody body = player && player.master ? player.master.GetBody() : null;
                if (body && body.healthComponent && body.healthComponent.alive)
                    body.healthComponent.HealFraction(fraction, default(ProcChainMask));
            }
        }

        private static void BeforeCellClosed(HoldoutZoneController ___holdoutZoneController)
        {
            CellSettings cell = ___holdoutZoneController ? ___holdoutZoneController.GetComponent<CellSettings>() : null;
            if (cell) { cell.Charging = false; cell.PendingCharge = 0f; }
        }

        private static void EnemyKilled(DamageReport __0)
        {
            DamageReport report = __0;
            if (!NetworkServer.active || !Active || report == null ||
                report.attackerTeamIndex != TeamIndex.Player ||
                (report.victimTeamIndex != TeamIndex.Monster && report.victimTeamIndex != TeamIndex.Void)) return;
            ArenaMissionController mission = ArenaMissionController.instance;
            if (!mission) return;
            foreach (GameObject ward in mission.nullWards)
            {
                CellSettings cell = ward ? ward.GetComponent<CellSettings>() : null;
                if (cell && cell.Charging && cell.Zone && cell.Zone.enabled && cell.Zone.charge < 1f)
                {
                    cell.PendingCharge += ModConfig.Get(killCharge) / 100f;
                    break;
                }
            }
        }
    }
}
