using AccessTools = ThreeQsVanillaTweaks.ReflectionTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using ThreeQsVanillaTweaks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;
using ModConfig = Config.ConfigManager;

namespace Tweaks.Bazaar
{
    /// <summary>
    /// Adds a 2-uncommon -> 3-common cauldron at a configurable world-space
    /// position and facing direction in the Bazaar. Reuses the vanilla red-to-white cauldron network prefab.
    /// </summary>
    internal static class CommonCauldron
    {
        private const string Section = "Bazaar - 04 Common Cauldron";
        private const string PrefabPath = "RoR2/Base/LunarCauldrons/LunarCauldron, RedToWhite Variant.prefab";
        private static ConfigEntry<bool> enabled;
        private static ConfigEntry<float> spawnChance;
        private static ConfigEntry<float> positionX;
        private static ConfigEntry<float> positionY;
        private static ConfigEntry<float> positionZ;
        private static ConfigEntry<float> forwardX;
        private static ConfigEntry<float> forwardY;
        private static ConfigEntry<float> forwardZ;
        private static BazaarSpawnHelper.GroundSettings ground;
        private static Stage spawnedStage;
        private static GameObject prefab;
        private static bool warned;
        private static readonly HashSet<int> PaidCauldrons = new HashSet<int>();

        internal static void Init()
        {
            enabled = ModConfig.BindBool(Section, "Add Green-to-White Cauldron", true,
                "Spawn an additional cauldron in the Bazaar that trades 2 uncommon (green) items or scrap for 3 random common (white) items.");
            spawnChance = ModConfig.Bind(Section, "Spawn Chance (%)", 50f,
                "Chance (0-100%) to spawn the extra cauldron on each Bazaar visit.");
            positionX = ModConfig.Bind(Section, "Position X", -141f,
                "Absolute world-space X position of the additional Bazaar cauldron.");
            positionY = ModConfig.Bind(Section, "Position Y", -25.8f,
                "Absolute world-space Y position (height) of the additional Bazaar cauldron.");
            positionZ = ModConfig.Bind(Section, "Position Z", -16.7f,
                "Absolute world-space Z position of the additional Bazaar cauldron.");
            forwardX = ModConfig.Bind(Section, "Forward X", 0.99f,
                "X component of the direction the cauldron faces.");
            forwardY = ModConfig.Bind(Section, "Forward Y", 0f,
                "Y component of the direction the cauldron faces.");
            forwardZ = ModConfig.Bind(Section, "Forward Z", -0.5f,
                "Z component of the direction the cauldron faces.");

            ground = BazaarSpawnHelper.BindGround(Section);

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
            if (!ModConfig.Get(enabled) || !InBazaar()) return;
            if (NetworkServer.active && spawnedStage != __instance)
            {
                spawnedStage = __instance;
                PaidCauldrons.Clear();
                float chance = Mathf.Clamp(ModConfig.Get(spawnChance), 0f, 100f);
                if (UnityEngine.Random.value * 100f < chance)
                    __instance.StartCoroutine(SpawnWhenReady(__instance));
            }
            // The prefab is a vanilla registered network prefab. On remote clients its
            // original cost type is restored when it is spawned; match and correct it.
            if (NetworkClient.active && !NetworkServer.active)
                __instance.StartCoroutine(ConfigureClientInstance(__instance));
        }

        private static IEnumerator ConfigureClientInstance(Stage stage)
        {
            for (int attempt = 0; attempt < 120; attempt++)
            {
                if (stage == null || !InBazaar()) yield break;
                foreach (PurchaseInteraction purchase in UnityEngine.Object.FindObjectsOfType<PurchaseInteraction>())
                {
                    if (purchase == null || purchase.costType != CostTypeIndex.RedItem || purchase.cost != 1) continue;
                    // Bazaar has no natural red-to-white cauldron, so this is our spawn.
                    ShopTerminalBehavior terminal = purchase.GetComponent<ShopTerminalBehavior>();
                    if (terminal == null || terminal.dropAmount != 3) continue;
                    purchase.costType = CostTypeIndex.GreenItem;
                    purchase.cost = 2;
                    terminal.itemTier = ItemTier.Tier1;
                    terminal.dropTable = null;
                    terminal.dropAmount = 3;
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
            }
        }

        private static IEnumerator SpawnWhenReady(Stage stage)
        {
            yield return null;
            if (stage == null || !NetworkServer.active || !InBazaar()) yield break;
            try
            {
                if (prefab == null)
                    prefab = Addressables.LoadAssetAsync<GameObject>(PrefabPath).WaitForCompletion();
                if (prefab == null)
                    throw new InvalidOperationException("Prefab not found: " + PrefabPath);

                Vector3 position = new Vector3(ModConfig.Get(positionX), ModConfig.Get(positionY), ModConfig.Get(positionZ));
                Vector3 forward = new Vector3(ModConfig.Get(forwardX), ModConfig.Get(forwardY), ModConfig.Get(forwardZ));
                ground.GetPlacement("Common Cauldron", position, forward, out position, out Quaternion rotation);
                GameObject instance = UnityEngine.Object.Instantiate(prefab, position, rotation);
                instance.name = "3Qs_GreenToWhiteCauldron";
                PurchaseInteraction purchase = instance.GetComponent<PurchaseInteraction>();
                ShopTerminalBehavior terminal = instance.GetComponent<ShopTerminalBehavior>();
                if (purchase == null || terminal == null)
                {
                    UnityEngine.Object.Destroy(instance);
                    throw new InvalidOperationException("Vanilla red-to-white cauldron prefab lacks PurchaseInteraction or ShopTerminalBehavior.");
                }
                purchase.costType = CostTypeIndex.GreenItem;
                purchase.cost = 2;
                terminal.itemTier = ItemTier.Tier1;
                terminal.dropTable = null;
                terminal.dropAmount = 3;
                terminal.debt = 1;
                NetworkServer.Spawn(instance);
                Log.Info("Spawned extra Bazaar 2-green-to-3-white cauldron at " + position);
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("Common cauldron spawning failed: " + e);
                }
            }
            yield break;
        }
    }
}
