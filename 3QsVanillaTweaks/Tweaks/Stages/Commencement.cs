using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using EntityStates.Missions.Moon;
using EntityStates.MoonElevator;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Stages
{
    internal static class Commencement
    {
        private static ConfigEntry<bool> enabled, rewards, keepPillars, balanced, eachType, stopEnemies, frog;
        private static ConfigEntry<int> required, maximum;
        private static ConfigEntry<bool> cauldrons;
        private static ConfigEntry<int> minimumCauldrons, maximumCauldrons;
        private static readonly ConfigEntry<int>[] guaranteedCauldrons = new ConfigEntry<int>[3];
        private static readonly ConfigEntry<float>[] radius = new ConfigEntry<float>[5], duration = new ConfigEntry<float>[5];
        private static readonly ConfigEntry<bool>[] fullSpeed = new ConfigEntry<bool>[5];
        internal static readonly string[] Names = { "Mass", "Design", "Soul", "Blood" };

        internal static void Init()
        {
            const string section = "Stages - Commencement";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable Commencement changes.");
            rewards = ModConfig.BindBool(section, "Pillar Rewards", true, "Completed pillars drop a random item. Vanilla: False.");
            CommencementRewards.Bind(section);
            keepPillars = ModConfig.BindBool(section, "Pillars Remain Available", true,
                "Allow unfinished pillars to be activated after the jump pads unlock. Vanilla: False.");
            required = Count(section, "Pillars Required", 4,
                "Pillars needed to activate the jump pads. Also sets the minimum number spawned. Vanilla: 4.");
            maximum = Count(section, "Maximum Pillars", 8,
                "Maximum number of pillars to spawn, up to 16. Cannot be lower than the number required. Vanilla: 7.");
            balanced = ModConfig.BindBool(section, "Equally Distribute Pillars", true,
                "Spawn one of each pillar type before repeating types. Vanilla: False.");
            eachType = ModConfig.BindBool(section, "Require Each Pillar Type", true,
                "Complete at least one of each pillar type to activate the jump pads. Ensures at least four pillars spawn and are required. Vanilla: False.");
            stopEnemies = ModConfig.BindBool(section, "Stop Enemies After Pillars", true,
                "Stop ambient and pillar enemies from spawning once the jump pads unlock. Boss encounters and the escape sequence are unaffected. Vanilla: False.");
            frog = ModConfig.BindBool(section, "Frog Leads To Void Locus", true,
                "The Glass Frog's portal leads to Void Locus. Vanilla: False.");

            const string cauldronSection = section + " - Cauldrons";
            cauldrons = ModConfig.BindBool(cauldronSection, "Enabled", true, "Enable Commencement cauldron changes.");
            minimumCauldrons = CauldronCount(cauldronSection, "Minimum Cauldrons", 6, 24,
                "Minimum number of cauldrons to spawn. Raised to fit the guaranteed numbers of each type. Vanilla: 5.");
            maximumCauldrons = CauldronCount(cauldronSection, "Maximum Cauldrons", 12, 24,
                "Maximum number of cauldrons to spawn. Cannot be lower than the minimum or the guaranteed total. Vanilla: 5.");
            guaranteedCauldrons[0] = CauldronCount(cauldronSection, "Guaranteed Uncommon Cauldrons", 3, 12,
                "Minimum number of cauldrons that give uncommon items.");
            guaranteedCauldrons[1] = CauldronCount(cauldronSection, "Guaranteed Legendary Cauldrons", 2, 10,
                "Minimum number of cauldrons that give legendary items.");
            guaranteedCauldrons[2] = CauldronCount(cauldronSection, "Guaranteed Common Cauldrons", 1, 2,
                "Minimum number of cauldrons that give common items.");

            string[] labels = { "Pillar Of Mass", "Pillar Of Design", "Pillar Of Soul", "Pillar Of Blood", "Ship Holdout" };
            float[] sizes = { 30f, 20f, 20f, 20f, 60f }, times = { 60f, 30f, 30f, 10f, 60f };
            for (int i = 0; i < labels.Length; i++)
            {
                string holdout = section + " - " + labels[i];
                radius[i] = ModConfig.Bind(holdout, "Radius", sizes[i], new ConfigDescription(
                    "Holdout radius in metres. Vanilla: " + (i == 4 ? "40" : "20") + ".", new AcceptableValueRange<float>(1f, 1000f)));
                duration[i] = ModConfig.Bind(holdout, "Charge Time", times[i], new ConfigDescription(
                    "Seconds required to fully charge at full speed. Vanilla: " + times[i].ToString(System.Globalization.CultureInfo.InvariantCulture) + ".",
                    new AcceptableValueRange<float>(0.1f, 3600f)));
                fullSpeed[i] = ModConfig.BindBool(holdout, "Full Speed With One Player", i != 4,
                    "Charge at full speed with at least one player inside the holdout. Vanilla: False.");
            }

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Stages.Commencement");
            Patch(harmony, typeof(SceneObjectToggleGroup), "Generate", null, nameof(AfterGenerate));
            Patch(harmony, typeof(MoonBatteryMissionController), "Awake", null, nameof(AfterMissionAwake));
            Patch(harmony, typeof(MoonBatteryMissionController), "OnBatteryCharged", nameof(BeforePillarCharged));
            Patch(harmony, typeof(HoldoutZoneController), "Start", nameof(ConfigureHoldout));
            Patch(harmony, typeof(HoldoutZoneController), "DoUpdate", nameof(BeforeHoldoutUpdate));
            Patch(harmony, typeof(CombatDirector), "AttemptSpawnOnTarget", nameof(BeforeEnemySpawn));
            Patch(harmony, typeof(EscapeSequenceController.EscapeSequenceMainState), "OnEnter", null, nameof(EscapeStarted));
            Patch(harmony, typeof(FrogController), "Pet", nameof(BeforePet), nameof(AfterPet), nameof(EndPet));
            SceneDirector.onPostPopulateSceneServer += AfterPopulate;
        }

        private static ConfigEntry<int> Count(string section, string key, int value, string description) =>
            ModConfig.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<int>(0, 16)));

        private static ConfigEntry<int> CauldronCount(string section, string key, int value, int limit, string description) =>
            ModConfig.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<int>(0, limit)));

        private static void Patch(Harmony harmony, Type type, string method, string prefix = null, string postfix = null, string finalizer = null) =>
            harmony.Patch(AccessTools.Method(type, method),
                prefix: prefix == null ? null : new HarmonyMethod(typeof(Commencement), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Commencement), postfix),
                finalizer: finalizer == null ? null : new HarmonyMethod(typeof(Commencement), finalizer));

        private static T Read<T>(object obj, string field) => (T)AccessTools.Field(obj.GetType(), field).GetValue(obj);
        private static void Write(object obj, string field, object value) => AccessTools.Field(obj.GetType(), field).SetValue(obj, value);
        private static bool Apply(GameObject obj) => obj && obj.scene.name == "moon2" && Run.instance && ModConfig.Get(enabled);
        private static int Required => Mathf.Clamp(Math.Max(ModConfig.Get(required), ModConfig.Get(eachType) ? 4 : 0), 0, 16);

        internal static int Kind(GameObject obj)
        {
            for (Transform current = obj ? obj.transform : null; current; current = current.parent)
            {
                string name = current.name;
                if (name.IndexOf("MoonBattery", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("Pillar", StringComparison.OrdinalIgnoreCase) < 0) continue;
                for (int i = 0; i < Names.Length; i++)
                    if (name.IndexOf(Names[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }

        private static int CandidateKind(GameObject obj)
        {
            if (!obj) return -1;
            foreach (HoldoutZoneController zone in obj.GetComponentsInChildren<HoldoutZoneController>(true))
            {
                int kind = Kind(zone.gameObject);
                if (kind >= 0) return kind;
            }
            return -1;
        }

        private static void AfterGenerate(SceneObjectToggleGroup __instance)
        {
            if (!NetworkServer.active || !Apply(__instance.gameObject)) return;
            GameObject[] objects = Read<GameObject[]>(__instance, "allToggleableObjects");
            bool[] activations = Read<bool[]>(__instance, "activations");
            ConfigureCauldrons(objects, activations);
            var buckets = new List<int>[4];
            for (int i = 0; i < 4; i++) buckets[i] = new List<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                int kind = CandidateKind(objects[i]);
                if (kind < 0) continue;
                buckets[kind].Add(i);
            }
            int available = buckets.Sum(list => list.Count);
            if (available == 0) return;
            var rng = Run.instance.stageRng;
            int min = Math.Min(Required, available);
            int max = Math.Min(Math.Max(min, ModConfig.Get(maximum)), available);
            int count = rng.RangeInt(min, max + 1);
            HashSet<int> selected = SelectPillars(buckets, count, ModConfig.Get(balanced), ModConfig.Get(eachType), rng);
            foreach (List<int> bucket in buckets)
                foreach (int index in bucket) activations[index] = selected.Contains(index);
            // Generate has already marked this array dirty. Native scene activation and
            // replication apply the selection to all peers, including inactive objects.
        }

        internal static HashSet<int> SelectPillars(List<int>[] buckets, int count, bool distribute, bool requireEach, Xoroshiro128Plus rng)
        {
            var pools = buckets.Select(list => new List<int>(list)).ToArray();
            foreach (List<int> pool in pools) Stage3SpawnHelper.Shuffle(pool, rng);
            var selected = new HashSet<int>();
            while (selected.Count < count)
            {
                var types = Enumerable.Range(0, pools.Length).Where(i => pools[i].Count > 0).ToList();
                if (types.Count == 0) break;
                Stage3SpawnHelper.Shuffle(types, rng);
                if (!distribute && !(requireEach && selected.Count == 0))
                {
                    var remaining = pools.SelectMany(list => list).ToList();
                    Stage3SpawnHelper.Shuffle(remaining, rng);
                    foreach (int index in remaining.Take(count - selected.Count)) selected.Add(index);
                    break;
                }
                foreach (int type in types)
                {
                    if (selected.Count >= count) break;
                    int last = pools[type].Count - 1;
                    selected.Add(pools[type][last]);
                    pools[type].RemoveAt(last);
                }
            }
            return selected;
        }

        private static int CauldronKind(GameObject obj)
        {
            if (!obj) return -1;
            foreach (ShopTerminalBehavior terminal in obj.GetComponentsInChildren<ShopTerminalBehavior>(true))
                for (Transform current = terminal.transform; current; current = current.parent)
                {
                    string name = current.name;
                    if (name.IndexOf("Cauldron", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (name.IndexOf("ToGreen", StringComparison.OrdinalIgnoreCase) >= 0) return 0;
                    if (name.IndexOf("ToRed", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                    if (name.IndexOf("ToWhite", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
                    if (terminal.itemTier == ItemTier.Tier2) return 0;
                    if (terminal.itemTier == ItemTier.Tier3) return 1;
                    if (terminal.itemTier == ItemTier.Tier1) return 2;
                }
            return -1;
        }

        private static void ConfigureCauldrons(GameObject[] objects, bool[] activations)
        {
            if (!ModConfig.Get(cauldrons)) return;
            var buckets = new[] { new List<int>(), new List<int>(), new List<int>() };
            for (int i = 0; i < objects.Length; i++)
            {
                int kind = CauldronKind(objects[i]);
                if (kind >= 0) buckets[kind].Add(i);
            }
            if (buckets.All(list => list.Count == 0)) return;
            int[] guarantees = guaranteedCauldrons.Select(entry => ModConfig.Get(entry)).ToArray();
            if (Enumerable.Range(0, 3).Any(i => guarantees[i] > buckets[i].Count))
                ThreeQsVanillaTweaks.Log.Warning("Commencement: fewer cauldron locations are available than requested; using all available locations of the affected types.");
            HashSet<int> selected = SelectCauldrons(buckets, ModConfig.Get(minimumCauldrons),
                ModConfig.Get(maximumCauldrons), guarantees, Run.instance.stageRng);
            foreach (List<int> bucket in buckets)
                foreach (int index in bucket) activations[index] = selected.Contains(index);
        }

        internal static HashSet<int> SelectCauldrons(List<int>[] buckets, int minimum, int maximum, int[] guarantees, Xoroshiro128Plus rng)
        {
            var pools = buckets.Select(list => new List<int>(list)).ToArray();
            var selected = new HashSet<int>();
            for (int i = 0; i < pools.Length; i++)
            {
                Stage3SpawnHelper.Shuffle(pools[i], rng);
                int count = Math.Min(Math.Max(0, guarantees[i]), pools[i].Count);
                for (int j = 0; j < count; j++) selected.Add(pools[i][j]);
                pools[i].RemoveRange(0, count);
            }
            var remaining = pools.SelectMany(list => list).ToList();
            int available = selected.Count + remaining.Count;
            int min = Math.Min(available, Math.Max(selected.Count, minimum));
            int max = Math.Min(available, Math.Max(min, maximum));
            int total = rng.RangeInt(min, max + 1);
            Stage3SpawnHelper.Shuffle(remaining, rng);
            foreach (int index in remaining.Take(total - selected.Count)) selected.Add(index);
            return selected;
        }

        internal sealed class Mission : MonoBehaviour
        {
            internal MoonBatteryMissionController Owner;
            internal HoldoutZoneController[] Zones;
            internal EntityStateMachine[] Pillars, Elevators;
            internal readonly HashSet<HoldoutZoneController> Completed = new HashSet<HoldoutZoneController>();
            internal readonly List<CombatDirector> PausedAmbient = new List<CombatDirector>();
            internal int Types, Needed, RequiredTypes;
            internal bool Unlocked, Escaping;

            private void FixedUpdate()
            {
                if (NetworkServer.active || !Owner || !Apply(gameObject)) return;
                // Pillar states and the completed count already replicate natively.
                // Rebuild the same objective target locally when a type is still missing.
                int types = 0;
                foreach (EntityStateMachine pillar in Pillars)
                {
                    if (!pillar || !(pillar.state is MoonBatteryComplete)) continue;
                    int kind = Commencement.Kind(pillar.gameObject);
                    if (kind >= 0) types |= 1 << kind;
                }
                int missing = Enumerable.Range(0, 4).Count(i => (RequiredTypes & (1 << i)) != 0 && (types & (1 << i)) == 0);
                int count = Owner.numChargedBatteries;
                Write(Owner, "_numRequiredBatteries", Math.Max(Needed, count + missing));
            }
        }

        private static void AfterMissionAwake(MoonBatteryMissionController __instance)
        {
            if (!Apply(__instance.gameObject)) return;
            var state = __instance.gameObject.AddComponent<Mission>();
            state.Owner = __instance;
            state.Zones = Read<HoldoutZoneController[]>(__instance, "batteryHoldoutZones");
            state.Pillars = Read<EntityStateMachine[]>(__instance, "batteryStateMachines");
            state.Elevators = Read<EntityStateMachine[]>(__instance, "elevatorStateMachines");
            state.Needed = Required;
            state.RequiredTypes = ModConfig.Get(eachType) ? 15 : 0;
            Write(__instance, "_numRequiredBatteries", state.Needed);
        }

        private static void AfterPopulate(SceneDirector director)
        {
            if (!NetworkServer.active || !Apply(director.gameObject)) return;
            MoonBatteryMissionController owner = MoonBatteryMissionController.instance;
            Mission state = owner ? owner.GetComponent<Mission>() : null;
            if (!state) return;
            HoldoutZoneController[] active = state.Zones.Where(zone => zone && zone.gameObject.activeInHierarchy).ToArray();
            int availableTypes = 0;
            foreach (HoldoutZoneController zone in active)
            {
                int kind = Kind(zone.gameObject);
                if (kind >= 0) availableTypes |= 1 << kind;
            }
            if ((state.RequiredTypes & availableTypes) != state.RequiredTypes)
            {
                state.RequiredTypes &= availableTypes;
                ThreeQsVanillaTweaks.Log.Warning("Commencement: a required pillar type is unavailable; only the available types will be required.");
            }
            if (state.Needed > active.Length)
            {
                state.Needed = active.Length;
                Write(owner, "_numRequiredBatteries", state.Needed);
                ThreeQsVanillaTweaks.Log.Warning("Commencement: fewer pillar locations are available than requested; required count was reduced to avoid blocking progress.");
            }
            if (state.Needed == 0 && state.RequiredTypes == 0) Unlock(state);
            // All native non-pillar holdouts on Commencement belong to the rescue ship.
            foreach (HoldoutZoneController zone in UnityEngine.Object.FindObjectsOfType<HoldoutZoneController>(true))
                if (zone.gameObject.scene == director.gameObject.scene) ConfigureHoldout(zone);
        }

        private static bool BeforePillarCharged(MoonBatteryMissionController __instance, HoldoutZoneController __0)
        {
            Mission state = __instance.GetComponent<Mission>();
            if (!state || !Apply(__instance.gameObject)) return true;
            if (!NetworkServer.active || !__0 || !state.Completed.Add(__0)) return false;
            int kind = Kind(__0.gameObject);
            if (kind >= 0) state.Types |= 1 << kind;
            __instance.Network_numChargedBatteries = state.Completed.Count;
            if (ModConfig.Get(rewards))
                try { CommencementRewards.Drop(__0.transform.position); }
                catch (Exception error) { ThreeQsVanillaTweaks.Log.Warning("Commencement pillar reward failed: " + error.Message); }
            int missingTypes = Enumerable.Range(0, 4).Count(i => (state.RequiredTypes & (1 << i)) != 0 && (state.Types & (1 << i)) == 0);
            bool ready = state.Completed.Count >= state.Needed && missingTypes == 0;
            // Keep the objective visible when enough pillars are charged but a type is missing.
            Write(__instance, "_numRequiredBatteries", ready || state.Unlocked ? state.Needed : Math.Max(state.Needed, state.Completed.Count + missingTypes));
            if (ready && !state.Unlocked) Unlock(state);
            return false;
        }

        private static void Unlock(Mission state)
        {
            if (state.Unlocked) return;
            state.Unlocked = true;
            foreach (EntityStateMachine elevator in state.Elevators)
                if (elevator) elevator.SetNextState(new InactiveToReady());
            if (!ModConfig.Get(keepPillars))
                foreach (EntityStateMachine pillar in state.Pillars)
                    if (pillar && !state.Completed.Contains(pillar.GetComponent<HoldoutZoneController>()) &&
                        !(pillar.state is MoonBatteryComplete)) pillar.SetNextState(new MoonBatteryDisabled());
            if (ModConfig.Get(stopEnemies))
                foreach (CombatDirector director in UnityEngine.Object.FindObjectsOfType<CombatDirector>())
                    if (AmbientOrPillar(director) && !ActivePillarDirector(state, director))
                    {
                        if (director.enabled && Kind(director.gameObject) < 0) state.PausedAmbient.Add(director);
                        director.monsterCredit = 0f;
                        director.enabled = false;
                    }
        }

        private static void EscapeStarted()
        {
            if (!NetworkServer.active) return;
            MoonBatteryMissionController owner = MoonBatteryMissionController.instance;
            Mission state = owner ? owner.GetComponent<Mission>() : null;
            if (!state) return;
            state.Escaping = true;
            foreach (CombatDirector director in state.PausedAmbient)
                if (director) director.enabled = true;
            state.PausedAmbient.Clear();
        }

        private static bool AmbientOrPillar(CombatDirector director) => director && director.gameObject.scene.name == "moon2" &&
            (Kind(director.gameObject) >= 0 || director.GetComponent<SceneDirector>() || director.GetComponent<MoonMissionController>());

        // An optional pillar keeps its own encounter while ambient spawning stays paused.
        private static bool ActivePillarDirector(Mission mission, CombatDirector director) =>
            mission.Pillars.Any(pillar => pillar && pillar.gameObject == director.gameObject &&
                pillar.state is MoonBatteryActive && !mission.Completed.Contains(pillar.GetComponent<HoldoutZoneController>()));

        private static bool BeforeEnemySpawn(CombatDirector __instance, ref bool __result)
        {
            if (!NetworkServer.active || !Apply(__instance.gameObject) || !ModConfig.Get(stopEnemies) || !AmbientOrPillar(__instance)) return true;
            MoonBatteryMissionController owner = MoonBatteryMissionController.instance;
            Mission state = owner ? owner.GetComponent<Mission>() : null;
            if (!state || !state.Unlocked || state.Escaping || ActivePillarDirector(state, __instance)) return true;
            __result = false;
            return false;
        }

        internal sealed class Holdout : MonoBehaviour
        {
            internal int Kind;
            internal HoldoutZoneController Zone;
            internal void Configure()
            {
                if (!Apply(gameObject)) return;
                Zone.baseRadius = ModConfig.Get(radius[Kind]);
                Zone.baseChargeDuration = ModConfig.Get(duration[Kind]);
                Zone.playerCountScaling = ModConfig.Get(fullSpeed[Kind]) ? 0f : 1f;
                AccessTools.Field(typeof(HoldoutZoneController), "ignorePlayersInRadiusForCharging")?.SetValue(Zone, false);
            }
        }

        private static void ConfigureHoldout(HoldoutZoneController __instance)
        {
            if (!Apply(__instance.gameObject)) return;
            Holdout settings = __instance.GetComponent<Holdout>();
            if (!settings)
            {
                settings = __instance.gameObject.AddComponent<Holdout>();
                settings.Zone = __instance;
                int kind = Kind(__instance.gameObject);
                settings.Kind = kind >= 0 ? kind : 4;
            }
            settings.Configure();
        }

        private static void BeforeHoldoutUpdate(HoldoutZoneController __instance)
        {
            Holdout settings = __instance.GetComponent<Holdout>();
            if (settings) settings.Configure();
        }

        internal sealed class FrogVisit
        {
            internal PortalSpawner Spawner;
            internal Action<GameObject> Handler;
            internal void Close() { if (Spawner && Handler != null) typeof(PortalSpawner).GetEvent("onPortalSpawnedServer")?.RemoveEventHandler(Spawner, Handler); Handler = null; }
        }

        private static void BeforePet(FrogController __instance, PortalSpawner ___portalSpawner, out FrogVisit __state)
        {
            __state = null;
            if (!NetworkServer.active || !Apply(__instance.gameObject) || !ModConfig.Get(frog) ||
                !___portalSpawner || !SpawnRequirements.ExpansionEnabled("DLC1")) return;
            SceneDef destination = SceneCatalog.FindSceneDef("voidstage");
            if (!SpawnRequirements.SceneAllowed(destination)) return;
            __state = new FrogVisit { Spawner = ___portalSpawner };
            __state.Handler = portal =>
            {
                SceneExitController exit = portal ? portal.GetComponent<SceneExitController>() : null;
                if (!exit) return;
                exit.useRunNextStageScene = false;
                exit.destinationScene = destination;
            };
            typeof(PortalSpawner).GetEvent("onPortalSpawnedServer")?.AddEventHandler(__state.Spawner, __state.Handler);
        }

        private static void AfterPet(FrogVisit __state) => __state?.Close();
        private static Exception EndPet(Exception __exception, FrogVisit __state) { __state?.Close(); return __exception; }
    }
}
