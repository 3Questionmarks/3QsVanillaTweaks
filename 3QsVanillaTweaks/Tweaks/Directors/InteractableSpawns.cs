using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using Tweaks.Stages;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;

namespace Tweaks.Directors
{
    internal static class InteractableSpawns
    {
        private const string Section = "Directors - Interactables";
        private const string StageSection = "Directors - Stage Credits";
        private const string ModdedStageSection = "Directors - Stage Credits - Modded Stages";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<int> lunarPod, voidSeed, voidPotential, cloakedChest, woodsShrine, woodsMaximum;
        private static ConfigEntry<int> stalk, commonPrinter, uncommonPrinter, legendaryPrinter, bossPrinter;
        private static ConfigEntry<int> gunnerTurret, largeDrones, tc280, tcMaximum;
        private static readonly Dictionary<string, (ConfigEntry<int> Entry, int Vanilla)> StageCredits =
            new Dictionary<string, (ConfigEntry<int>, int)>(StringComparer.OrdinalIgnoreCase);
        private static ConfigEntry<int> disturbedImpactStageOne;
        private static readonly Dictionary<SceneDirector, List<UnityEngine.Object>> Clones =
            new Dictionary<SceneDirector, List<UnityEngine.Object>>();
        private static readonly MethodInfo ObjectCloneMethod = typeof(object).GetMethod(
            "MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void Init()
        {
            enabled = C.BindBool(Section, "Enable Changes", true,
                "Adjust interactable director costs and limits on regular stages. Hidden realms keep their own settings.");
            lunarPod = Cost("Lunar Pod Cost", 20, 25);
            voidSeed = Cost("Void Seed Cost", 30, 50);
            voidPotential = Cost("Void Potential Cost", 30, 40);
            cloakedChest = Cost("Cloaked Chest Cost", 5, 10);
            woodsShrine = Cost("Shrine of the Woods Cost", 20, 15);
            woodsMaximum = Maximum("Shrine of the Woods Maximum Per Stage", 1);
            stalk = Cost("Stalk Cost", 5, 15);
            commonPrinter = Cost("Common Printer Cost", 10, 5);
            uncommonPrinter = Cost("Uncommon Printer Cost", 10, 10);
            legendaryPrinter = Cost("Legendary Printer Cost", 10, 15);
            bossPrinter = Cost("Boss Printer Cost", 10, 10);
            gunnerTurret = Cost("Gunner Turret Cost", 5, 10);
            largeDrones = Cost("Large Drone Cost", 25, 30);
            tc280 = Cost("TC-280 Prototype Cost", 35, 40);
            tcMaximum = Maximum("TC-280 Prototype Maximum Per Stage", 1);

            // Keep the existing config keys for stages that already had overrides.
            RegisterStage("Distant Roost", 250, 220, "blackbeach", "blackbeach2");
            RegisterStage("Titanic Plains", 250, 220, "golemplains", "golemplains2");
            RegisterStage("Siphoned Forest", 300, 300, "snowyforest");
            RegisterStage("Verdant Falls", 280, 280, "lakes");
            RegisterStage("Viscous Falls", 280, 280, "lakesnight");
            RegisterStage("Shattered Abodes", 280, 280, "village");
            RegisterStage("Disturbed Impact", 310, 310, "villagenight");
            disturbedImpactStageOne = C.Bind(StageSection, "Disturbed Impact Stage One Credits", 280,
                "Base interactable credits when Disturbed Impact appears as stage one. Additional players increase this by 50% each; the stage-one rebalance then subtracts 10 flat. Vanilla: 310.");

            RegisterStage("Wetland Aspect", 280, 280, "foggyswamp");
            RegisterStage("Abandoned Aqueduct", 280, 220, "goolake");
            RegisterStage("Aphelian Sanctuary", 280, 280, "ancientloft");
            RegisterStage("Pretender's Precipice", 280, 280, "nest");
            RegisterStage("Reformed Altar", 300, 300, "lemuriantemple");

            RegisterStage("Rallypoint Delta", 280, 280, "frozenwall");
            RegisterStage("Scorched Acres", 280, 280, "wispgraveyard");
            RegisterStage("Sulfur Pools", 280, 280, "sulfurpools");
            RegisterStage("Damp Caverns", 310, 310, "fungal");
            RegisterStage("Tenebrous Quagmire", 340, 340, "mire");
            RegisterStage("Iron Alluvium", 280, 280, "ironalluvium");
            RegisterStage("Iron Auroras", 280, 280, "ironalluvium2");
            RegisterStage("Treeborn Colony", 280, 280, "habitat");
            RegisterStage("Golden Dieback", 280, 280, "habitatfall");

            RegisterStage("Abyssal Depths", 400, 400, "dampcavesimple");
            RegisterStage("Siren's Call", 400, 400, "shipgraveyard");
            RegisterStage("Sundered Grove", 400, 400, "rootjungle");
            RegisterStage("Repurposed Crater", 400, 400, "repurposedcrater");
            RegisterStage("Conduit Canyon", 400, 400, "conduitcanyon");
            RegisterStage("Lurid Basilica", 400, 400, "sanctum");

            RegisterStage("Sky Meadow", 550, 520, "skymeadow");
            RegisterStage("Helminth Hatchery", 570, 570, "helminthroost");

            if (C.ModuleEnabled(typeof(Tweaks.ModCompatibility.Stages.ModdedStageFeatures)))
                RegisterModdedStages();

            new Harmony("3Questionmarks.3QsVanillaTweaks.Directors.InteractableSpawns")
                .Patch(AccessTools.Method(typeof(SceneDirector), "Start"),
                    prefix: new HarmonyMethod(typeof(InteractableSpawns), nameof(BeforeDirectorStart)) { priority = Priority.Last });
            new Harmony("3Questionmarks.3QsVanillaTweaks.Directors.VoidCampInteractables")
                .Patch(AccessTools.PropertyGetter(typeof(DirectorCard), "cost"),
                    postfix: new HarmonyMethod(typeof(InteractableSpawns), nameof(VoidCampCost)));
            SceneDirector.onGenerateInteractableCardSelection += ChangeSelection;
            SceneDirector.onPostPopulateSceneServer += ReleaseClones;
            Run.onRunDestroyGlobal += _ => ReleaseAllClones();
        }

        private static ConfigEntry<int> Cost(string key, int value, int vanilla) =>
            C.Bind(Section, key, value, "Credits the director spends to spawn one. Vanilla: " + vanilla + ".");

        private static ConfigEntry<int> Maximum(string key, int value) =>
            C.Bind(Section, key, value, "Maximum natural spawns per regular stage. -1 removes the limit. Vanilla: unlimited.");

        private static void RegisterStage(string name, int value, int vanilla, params string[] sceneNames) =>
            RegisterStage(StageSection, name, value, vanilla, sceneNames);

        private static void RegisterStage(string section, string name, int value, int mapDefault, params string[] sceneNames)
        {
            string baselineLabel = section == StageSection ? "Vanilla" : "Map default";
            ConfigEntry<int> entry = C.Bind(section, name.Replace("'", "") + " Credits", value,
                "Base interactable credits for one player. Additional players increase this by 50% each. " +
                baselineLabel + ": " + mapDefault + ".");
            foreach (string sceneName in sceneNames) StageCredits.Add(sceneName, (entry, mapDefault));
        }

        private static void RegisterModdedStages()
        {
            RegisterStage(ModdedStageSection, "Desolate Coast", 300, 220, "coast_wormsworms");

            RegisterStage(ModdedStageSection, "Broadcast Perch", 280, 280, "broadcastperch_wormsworms");
            RegisterStage(ModdedStageSection, "Wetland Downpour", 280, 280, "foggyswampdownpour");

            RegisterStage(ModdedStageSection, "Fogbound Lagoon", 280, 280, "FBLScene");
            RegisterStage(ModdedStageSection, "Forlorn Wreckage", 280, 280, "forgottenwreckage_ws");

            RegisterStage(ModdedStageSection, "Sunset Tropics", 400, 400, "tropics_wormsworms");
            RegisterStage(ModdedStageSection, "Midnight Tropics", 400, 480, "tropicsnight_wormsworms");
            RegisterStage(ModdedStageSection, "Stagnant Pipeworks", 400, 400, "swampybog_winslow");
            RegisterStage(ModdedStageSection, "Sleeping Hollows", 400, 450, "swampybognight_winslow");
            RegisterStage(ModdedStageSection, "Sunken Tombs", 400, 400, "sunkentombs_wormsworms");

            RegisterStage(ModdedStageSection, "Ancient Observatory", 570, 570, "observatory_wormsworms");
            RegisterStage(ModdedStageSection, "Hollow Summit", 550, 520, "hollowsummit_wormsworms");
            RegisterStage(ModdedStageSection, "Frozen Summit", 550, 520, "hollowsummitnight_wormsworms");
        }

        private static bool NormalStage()
        {
            SceneDef scene = SceneInfo.instance ? SceneInfo.instance.sceneDef : null;
            return NetworkServer.active && Run.instance && scene && scene.sceneType == SceneType.Stage &&
                scene.baseSceneName != "meridian" && scene.baseSceneName != "moon2";
        }

        private static void BeforeDirectorStart()
        {
            if (!C.Get(enabled) || !NormalStage() || !ClassicStageInfo.instance) return;
            ClassicStageInfo stage = ClassicStageInfo.instance;
            string scene = SceneInfo.instance.sceneDef.baseSceneName;
            if (!StageCredits.TryGetValue(scene, out var setting)) return;
            // LoopVariantConfig owns the night variants' budget and enemy restrictions when installed.
            if ((scene == "lakesnight" || scene == "villagenight") && FirstStageVariantRebalance.ExternalOwnsVariants) return;
            // The stage-one rebalance sets Disturbed Impact to 280 before this prefix runs.
            // Its separate -10 flat bonus remains in place after player scaling.
            if (scene == "villagenight" && Run.instance.stageClearCount == 0 &&
                stage.sceneDirectorInteractibleCredits == 280)
            {
                stage.sceneDirectorInteractibleCredits = Mathf.Max(0, C.Get(disturbedImpactStageOne));
                return;
            }
            int configured = C.Get(setting.Entry);
            // Vanilla stages with unchanged defaults defer to other mods' budgets.
            if (setting.Entry.Definition.Section == StageSection &&
                setting.Entry.Value == setting.Vanilla && configured == setting.Vanilla) return;
            stage.sceneDirectorInteractibleCredits = Mathf.Max(0, configured);
        }

        private static void ChangeSelection(SceneDirector director, DirectorCardCategorySelection selection)
        {
            if (!C.Get(enabled) || !NormalStage() || !selection) return;
            var objects = new List<UnityEngine.Object>();
            var clonedCards = new Dictionary<DirectorCard, DirectorCard>();
            for (int i = 0; i < selection.categories.Length; i++)
            {
                var category = selection.categories[i];
                DirectorCard[] cards = category.cards;
                if (cards == null) continue;
                for (int j = 0; j < cards.Length; j++)
                {
                    DirectorCard original = cards[j];
                    if (original == null) continue;
                    if (clonedCards.TryGetValue(original, out DirectorCard previous))
                    {
                        cards[j] = previous;
                        continue;
                    }
                    InteractableSpawnCard spawn = original.GetSpawnCard() as InteractableSpawnCard;
                    if (!spawn) continue;
                    int cost = spawn.directorCreditCost;
                    int maximum = spawn.maxSpawnsPerStage;
                    string name = spawn.name ?? "";
                    string prefab = spawn.prefab ? spawn.prefab.name : "";
                    if (Has(name, prefab, "LunarChest", "LunarPod") && cost == 25) cost = C.Get(lunarPod);
                    else if (Has(name, prefab, "VoidCamp", "VoidSeed") && cost == 50) cost = C.Get(voidSeed);
                    else if (Has(name, prefab, "VoidPotential") && cost == 40) cost = C.Get(voidPotential);
                    else if (Has(name, prefab, "Stealthed", "CloakedChest") && cost == 10) cost = C.Get(cloakedChest);
                    else if (Has(name, prefab, "ShrineHealing", "ShrineWoods", "ShrineOfTheWoods"))
                    {
                        cost = C.Get(woodsShrine);
                        maximum = C.Get(woodsMaximum);
                    }
                    else if (Has(name, prefab, "VoidCoinBarrel", "VoidStalk") && cost == 15) cost = C.Get(stalk);
                    else if (Has(name, prefab, "DuplicatorMilitary")) cost = C.Get(legendaryPrinter);
                    else if (Has(name, prefab, "DuplicatorLarge")) cost = C.Get(uncommonPrinter);
                    else if (Has(name, prefab, "DuplicatorWild")) cost = C.Get(bossPrinter);
                    else if (Has(name, prefab, "Duplicator")) cost = C.Get(commonPrinter);
                    else if (Has(name, prefab, "MegaDrone", "TC280", "TC-280"))
                    {
                        cost = C.Get(tc280);
                        maximum = C.Get(tcMaximum);
                    }
                    else if (Has(name, prefab, "Turret1", "GunnerTurret") && cost == 10) cost = C.Get(gunnerTurret);
                    else if (string.Equals(category.name, "Drones", StringComparison.OrdinalIgnoreCase) &&
                             cost == 30) cost = C.Get(largeDrones);

                    cost = Mathf.Max(1, cost);
                    maximum = Mathf.Max(-1, maximum);
                    if (cost == spawn.directorCreditCost && maximum == spawn.maxSpawnsPerStage) continue;
                    // DirectorCard is a managed class in the game's assemblies, not a Unity object.
                    // Copy all its stage requirements and other fields before substituting the spawn card.
                    DirectorCard copy = (DirectorCard)ObjectCloneMethod.Invoke(original, null);
                    InteractableSpawnCard spawnCopy = UnityEngine.Object.Instantiate(spawn);
                    spawnCopy.directorCreditCost = cost;
                    spawnCopy.maxSpawnsPerStage = maximum;
                    copy.spawnCard = spawnCopy;
                    clonedCards.Add(original, copy);
                    objects.Add(spawnCopy);
                    cards[j] = copy;
                }
                category.cards = cards;
                selection.categories[i] = category;
            }
            if (objects.Count > 0) Clones[director] = objects;
        }

        private static bool Has(string name, string prefab, params string[] words)
        {
            foreach (string word in words)
                if (name.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prefab.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // The Void Seed's camp director buys Void Potentials and Stalks separately
        // from the stage director's purchase of the Seed itself.
        private static void VoidCampCost(DirectorCard __instance, ref int __result)
        {
            if ((__result != 50 && __result != 40 && __result != 15) || __instance == null) return;
            SpawnCard spawn = __instance.GetSpawnCard();
            if (!spawn) return;
            string name = spawn.name ?? "";
            string prefab = spawn.prefab ? spawn.prefab.name : "";
            if (!C.Get(enabled) || !NormalStage()) return;
            if (__result == 50 && Has(name, prefab, "VoidCamp", "VoidSeed"))
                __result = Mathf.Max(1, C.Get(voidSeed));
            else if (__result == 40 && Has(name, prefab, "VoidPotential"))
                __result = Mathf.Max(1, C.Get(voidPotential));
            else if (__result == 15 && Has(name, prefab, "VoidCoinBarrel", "VoidStalk"))
                __result = Mathf.Max(1, C.Get(stalk));
        }

        private static void ReleaseClones(SceneDirector director)
        {
            if (!Clones.TryGetValue(director, out List<UnityEngine.Object> objects)) return;
            Clones.Remove(director);
            foreach (UnityEngine.Object obj in objects) if (obj) UnityEngine.Object.Destroy(obj);
        }

        private static void ReleaseAllClones()
        {
            foreach (var objects in Clones.Values)
                foreach (UnityEngine.Object obj in objects) if (obj) UnityEngine.Object.Destroy(obj);
            Clones.Clear();
        }
    }
}
