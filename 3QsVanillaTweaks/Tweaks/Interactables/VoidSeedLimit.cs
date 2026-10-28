using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Interactables
{
    internal static class VoidSeedLimit
    {
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<int> baseLimit, perLoop, itemsPerSeed, maximum;
        private static readonly Dictionary<int, int> Counts = new Dictionary<int, int>();
        [ThreadStatic] private static Population active;

        internal sealed class Population
        {
            internal Population Previous;
            internal int Scene, Limit;
            internal readonly Dictionary<InteractableSpawnCard, int> Originals = new Dictionary<InteractableSpawnCard, int>();
        }

        internal static void Init()
        {
            const string section = "Interactables - Void Seeds";
            enabled = ModConfig.BindBool(section, "Enabled", true, "Enable Void Seed changes.");
            baseLimit = Number(section, "Base Limit", 3, "Maximum void seeds before loop and item bonuses.");
            perLoop = Number(section, "Additional Seeds Per Loop", 1, "Increase the limit by this amount for each completed loop.");
            itemsPerSeed = Number(section, "Void Items Per Additional Seed", 5, "Increase the limit by one for every this many void item stacks held by all players combined. Set to 0 to disable this bonus.");
            maximum = Number(section, "Absolute Maximum", 10, "Highest possible void seed limit, including all bonuses.");
            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Interactables.VoidSeedLimit");
            harmony.Patch(AccessTools.Method(typeof(SceneDirector), "PopulateScene"),
                prefix: new HarmonyMethod(typeof(VoidSeedLimit), nameof(BeforePopulate)),
                finalizer: new HarmonyMethod(typeof(VoidSeedLimit), nameof(AfterPopulate)));
            SceneDirector.onGenerateInteractableCardSelection += ConfigureCards;
            harmony.Patch(AccessTools.Method(typeof(DirectorCore), "TrySpawnObject"),
                prefix: new HarmonyMethod(typeof(VoidSeedLimit), nameof(BeforeSpawn)),
                postfix: new HarmonyMethod(typeof(VoidSeedLimit), nameof(AfterSpawn)));
            Run.onRunDestroyGlobal += run => Counts.Clear();
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += scene => Counts.Remove(scene.handle);
        }

        private static ConfigEntry<int> Number(string section, string key, int value, string description)
        {
            return ModConfig.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<int>(0, 1000)));
        }

        private static bool IsSeed(SpawnCard card)
        {
            return card && (card.name.StartsWith("iscVoidCamp", StringComparison.OrdinalIgnoreCase) ||
                card.prefab && card.prefab.name.StartsWith("VoidCamp", StringComparison.OrdinalIgnoreCase));
        }

        internal static int CalculateLimit(int basis, int loops, int loopBonus, long voidItems, int perItemBonus, int cap)
        {
            cap = Math.Max(0, cap);
            long limit = Math.Max(0L, (long)basis + (long)loops * loopBonus);
            if (limit >= cap) return cap;
            long itemBonus = perItemBonus > 0 ? Math.Max(0L, voidItems) / perItemBonus : 0L;
            return itemBonus >= cap - limit ? cap : (int)(limit + itemBonus);
        }

        private static long TeamVoidItems()
        {
            long total = 0;
            var counted = new HashSet<Inventory>();
            foreach (PlayerCharacterMasterController player in PlayerCharacterMasterController.instances)
            {
                Inventory inventory = player && player.master ? player.master.inventory : null;
                if (!inventory || !counted.Add(inventory)) continue;
                foreach (ItemIndex index in ItemCatalog.allItems)
                {
                    ItemDef item = ItemCatalog.GetItemDef(index);
                    if (!item || !SpawnRequirements.ItemAllowed(item)) continue;
                    ItemTier tier = item.tier;
                    if (tier == ItemTier.VoidTier1 || tier == ItemTier.VoidTier2 || tier == ItemTier.VoidTier3 || tier == ItemTier.VoidBoss)
                        total += inventory.GetItemCountEffective(index);
                }
            }
            return total;
        }

        private static void BeforePopulate(SceneDirector __instance, out Population __state)
        {
            __state = null;
            if (!NetworkServer.active || !Run.instance || !ModConfig.Get(enabled) || !SpawnRequirements.ExpansionEnabled("DLC1")) return;
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            if (!scene || scene.sceneType != SceneType.Stage) return;
            int handle = __instance.gameObject.scene.handle;
            if (!Counts.ContainsKey(handle))
            {
                int existing = 0;
                foreach (CampDirector camp in UnityEngine.Object.FindObjectsOfType<CampDirector>())
                    if (camp.gameObject.scene.handle == handle && camp.gameObject.name.StartsWith("VoidCamp", StringComparison.OrdinalIgnoreCase)) ++existing;
                Counts.Add(handle, existing);
            }
            __state = new Population
            {
                Previous = active, Scene = handle,
                Limit = CalculateLimit(ModConfig.Get(baseLimit), Run.instance.loopClearCount, ModConfig.Get(perLoop), TeamVoidItems(), ModConfig.Get(itemsPerSeed), ModConfig.Get(maximum))
            };
            active = __state;
        }

        private static void ConfigureCards(SceneDirector director, DirectorCardCategorySelection selection)
        {
            if (active == null || !selection) return;
            foreach (var category in selection.categories)
                foreach (DirectorCard directorCard in category.cards ?? Array.Empty<DirectorCard>())
                {
                    var card = directorCard?.GetSpawnCard() as InteractableSpawnCard;
                    if (!IsSeed(card)) continue;
                    if (!active.Originals.ContainsKey(card)) active.Originals.Add(card, card.maxSpawnsPerStage);
                    card.maxSpawnsPerStage = Math.Max(0, active.Limit - Counts[active.Scene]);
                }
        }

        private static void AfterPopulate(Population __state)
        {
            if (__state == null) return;
            foreach (var pair in __state.Originals) if (pair.Key) pair.Key.maxSpawnsPerStage = pair.Value;
            active = __state.Previous;
        }

        private static bool BeforeSpawn(DirectorSpawnRequest directorSpawnRequest, out Population __state)
        {
            __state = null;
            if (active == null || !IsSeed(directorSpawnRequest.spawnCard)) return true;
            __state = active;
            return Counts[active.Scene] < active.Limit;
        }

        private static void AfterSpawn(GameObject __result, Population __state)
        {
            if (__state != null && __result) ++Counts[__state.Scene];
        }
    }
}
