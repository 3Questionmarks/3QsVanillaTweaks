using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// Adds a 2-uncommon -> 3-common cauldron at a configurable world-space
    /// position and facing direction in the Bazaar. Reuses the vanilla red-to-white cauldron network prefab.
    /// </summary>
    internal static class CommonCauldron
    {
        internal const string LegacySection = "Bazaar - 04 Common Cauldron";
        internal const string Section = "Bazaar - 05 Extra Interactables - 08 Common Cauldron";
        private const string PrefabPath = "RoR2/Base/LunarCauldrons/LunarCauldron, RedToWhite Variant.prefab";
        private static Stage spawnedStage;
        private static GameObject prefab;
        private static BazaarSpawnHelper.Settings first, second;
        private static readonly HashSet<int> PaidCauldrons = new HashSet<int>();

        internal static void Init()
        {
            first = BazaarSpawnHelper.Bind(Section, "Common Cauldron", PrefabPath,
                50f, -120.71f, -24.03f, -6.87f, -.258f, 0f, -.966f, alignToGround: false,
                exactPose: "-117.210014, -24.2996559, -6.869999, 0, 89.92999, 0",
                unlockSection: "Common Cauldron", editorSlot: LegacySection,
                enabledKey: "Add Green-to-White Cauldron",
                enabledDescription: "Allow the extra cauldron that trades two uncommon items for three common items.");
            second = BazaarSpawnHelper.BindFormerUnlockOnly("Common Cauldron 2", PrefabPath,
                -122.77f, -22.82f, -5.53f, .99f, 0f, -.5f,
                exactPose: "-121.639633, -23.8940716, -6.592497, 5.00000143, 156.796082, 0");
            RoR2Application.onLoad += PreparePrefab;

            var harmony = new Harmony("3Questionmarks.3QsVanillaTweaks.Bazaar.CommonCauldron");
            harmony.Patch(AccessTools.Method(typeof(Stage), "Start"),
                postfix: new HarmonyMethod(typeof(CommonCauldron), nameof(OnStageStart)));
            // Enforce one payout of exactly three items per paid purchase, even if the reused
            // Moon prefab calls its payout callback multiple times.
            harmony.Patch(AccessTools.Method(typeof(ShopTerminalBehavior), "DropPickup", new[] { typeof(bool) }),
                prefix: new HarmonyMethod(typeof(CommonCauldron), nameof(BeforeCustomCauldronDrop)));
            harmony.Patch(AccessTools.Method(typeof(PurchaseInteraction), "OnInteractionBegin"),
                transpiler: new HarmonyMethod(typeof(CommonCauldron), nameof(PurchaseIL)));
            Log.Info("Bazaar extra common cauldron registered.");
        }

        private static void PreparePrefab()
        {
            try
            {
                var original = Addressables.LoadAssetAsync<GameObject>(PrefabPath).WaitForCompletion();
                if (!original) throw new InvalidOperationException("The cauldron prefab could not be loaded.");
                prefab = R2API.PrefabAPI.InstantiateClone(original, "3Qs_BazaarGreenToWhiteCauldron", true);
                var purchase = prefab.GetComponent<PurchaseInteraction>();
                var terminal = prefab.GetComponent<ShopTerminalBehavior>();
                purchase.costType = CostTypeIndex.GreenItem;
                purchase.cost = 2;
                terminal.itemTier = ItemTier.Tier1;
                terminal.dropTable = null;
                terminal.dropAmount = 3;
                terminal.debt = 1;
            }
            catch (Exception e)
            {
                prefab = null;
                Log.Warning("Could not prepare the Bazaar common cauldron: " + e.Message);
            }
        }

        // The reused Moon cauldron can invoke its payout handler more than once.
        // Only the first invocation may generate droplets. Using the cauldron's
        // root identifier also protects against separate child terminals.
        private static bool BeforeCustomCauldronDrop(ShopTerminalBehavior __instance)
        {
            if (!NetworkServer.active || __instance == null || !InBazaar()) return true;

            Transform node = __instance.transform;
            Transform cauldron = null;
            while (node != null)
            {
                if (node.gameObject.name == "3Qs_GreenToWhiteCauldron")
                {
                    cauldron = node;
                    break;
                }
                node = node.parent;
            }
            if (cauldron == null) return true;

            int identifier = cauldron.gameObject.GetInstanceID();
            if (!PaidCauldrons.Add(identifier))
            {
                Log.Info("Prevented duplicate common cauldron payout for the current purchase (already dispensed 3 items).");
                return false;
            }

            __instance.dropAmount = 3;
            __instance.debt = 1;
            return true;
        }

        private static void BeginPaidPurchase(PurchaseInteraction purchase)
        {
            if (!NetworkServer.active || !purchase || !InBazaar()) return;
            Transform node = purchase.transform;
            while (node)
            {
                if (node.gameObject.name == "3Qs_GreenToWhiteCauldron")
                {
                    PaidCauldrons.Remove(node.gameObject.GetInstanceID());
                    return;
                }
                node = node.parent;
            }
        }

        private static IEnumerable<CodeInstruction> PurchaseIL(IEnumerable<CodeInstruction> instructions)
        {
            int found = 0;
            foreach (CodeInstruction original in instructions)
            {
                // This event is reached only after payment and item-removal have
                // succeeded. Reset before any purchase listener can start payout.
                if (original.opcode == OpCodes.Ldfld && original.operand is FieldInfo field &&
                    field.DeclaringType == typeof(PurchaseInteraction) && field.Name == "onPurchase")
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(original.labels); original.labels.Clear();
                    load.blocks.AddRange(original.blocks); original.blocks.Clear();
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CommonCauldron), nameof(BeginPaidPurchase)));
                    found++;
                }
                yield return original;
            }
            if (found != 1) throw new InvalidOperationException("Common cauldron paid-purchase anchor changed: " + found);
        }

        private static bool InBazaar()
        {
            SceneDef scene = SceneCatalog.mostRecentSceneDef;
            return scene != null && string.Equals(scene.baseSceneName, "bazaar", StringComparison.OrdinalIgnoreCase);
        }

        private static void OnStageStart(Stage __instance)
        {
            if (!NetworkServer.active || !InBazaar() || spawnedStage == __instance) return;
            spawnedStage = __instance;
            PaidCauldrons.Clear();
            __instance.StartCoroutine(BazaarSpawnHelper.SpawnNextFrame(__instance, SpawnCauldrons));
        }

        private static void SpawnCauldrons()
        {
            if (!prefab) return;
            foreach (var slot in new[] { first, second })
                if (BazaarSpawnHelper.Roll(slot))
                    BazaarSpawnHelper.Spawn(slot, prefab, slot.Name, slot.Position, slot.Forward, ConfigureCauldron);
        }

        private static void ConfigureCauldron(GameObject instance)
        {
            instance.name = "3Qs_GreenToWhiteCauldron";
            var purchase = instance.GetComponent<PurchaseInteraction>();
            var terminal = instance.GetComponent<ShopTerminalBehavior>();
            if (!purchase || !terminal)
                throw new InvalidOperationException("The common cauldron has no shop or purchase component.");
            purchase.NetworkcostType = CostTypeIndex.GreenItem;
            purchase.Networkcost = 2;
            terminal.itemTier = ItemTier.Tier1;
            terminal.dropTable = null;
            terminal.dropAmount = 3;
            terminal.debt = 1;
        }
    }
}
