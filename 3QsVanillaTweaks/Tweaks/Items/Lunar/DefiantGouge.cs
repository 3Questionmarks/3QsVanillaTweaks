using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using A = ThreeQsVanillaTweaks.ReflectionTools;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using Rewards = Tweaks.Interactables.ShrineEncounterRewards;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Lunar
{
    internal static class DefiantGouge
    {
        static R c;
        sealed class ChanceResult
        {
            internal Interactor Buyer;
            internal bool Success;
            internal int Frame;
        }
        static readonly ConditionalWeakTable<ShrineChanceBehavior, ChanceResult> chanceResults = new ConditionalWeakTable<ShrineChanceBehavior, ChanceResult>();
        static readonly System.Reflection.FieldInfo successfulPurchases = A.Field(typeof(ShrineChanceBehavior), "successfulPurchaseCount");
        static readonly string[] rarities = { "Common", "Uncommon", "Legendary", "Boss", "Lunar", "Void Common", "Void Uncommon", "Void Legendary", "Void Boss" };
        internal static void Init()
        {
            U.Ensure(); c = new R("Defiant Gouge", "MonstersOnShrineUse", true, "Lunar");
            c.B("Reward An Item", true, "Drop rewards at the shrine after all summoned enemies die. Adds its own encounter/rewards to Shrines of Combat as well. Vanilla: false.");
            c.B("Chance Shrine Requires Successful Purchase", true, "Only award Gouge items when that Chance Shrine use drops a reward. Failed uses still summon enemies. Vanilla: Gouge never awards items.");
            c.B("Reward Each Player", false, "Drop a copy of each reward per participating player.");
            c.Pair("Monster Credits", 40, 40, "Encounter credits before stage difficulty scaling. Vanilla: 40 per stack.");
            c.N("Extra Item Chance Per Stack (%)", 10, "Extra reward chance per additional item. Every full 100% guarantees another reward.");
            c.N("Enemy Gold Reward (%)", 0, "Gold awarded by summoned enemies, as a percentage of their normal reward. Experience is unchanged. Vanilla: 100%.");
            float[] values = { 55, 35, 1, 0, 9, 0, 0, 0, 0 };
            for (int i = 0; i < rarities.Length; i++) c.N(rarities[i] + " Reward Weight", values[i], "Relative reward weight; zero excludes this rarity.");
            if (c.Enabled.Value)
            {
                c.Suppress("RoR2.GlobalEventManager", "OnInteractionBegin");
                U.Patch(typeof(GlobalEventManager), "OnInteractionBegin", typeof(DefiantGouge), postfix: nameof(Interact));
                U.Harmony.Patch(A.Method(typeof(ShrineChanceBehavior), "AddShrineStack"),
                    prefix: new HarmonyMethod(typeof(DefiantGouge), nameof(BeforeChancePurchase)),
                    postfix: new HarmonyMethod(typeof(DefiantGouge), nameof(AfterChancePurchase)));
            }
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => c.Item ? c.Item.itemIndex : ItemIndex.None,
                (master, stacks, text) => GougeStats(stacks), () => c.Active);
            c.Text(() => "<style=cIsHealth>Using a shrine summons enemies</style>" + (c.F("Monster Credits Per Stack") == 0 ? "." : " <style=cStack>(+" + (c.F("Monster Credits") > 0 ? (c.F("Monster Credits Per Stack") / c.F("Monster Credits") * 100).ToString("0.##") + "% difficulty per stack" : c.F("Monster Credits Per Stack").ToString("0.##") + " monster credits per stack") + ").</style>") + (c.B("Reward An Item") ? " Defeating them all drops an <style=cIsUtility>item</style>" + (c.F("Extra Item Chance Per Stack (%)") == 0 ? "" : ", with a <style=cIsUtility>0%</style> <style=cStack>(+" + c.F("Extra Item Chance Per Stack (%)").ToString("0.##") + "% per stack)</style> chance to drop an additional item") + "." : ""), () => c.B("Reward An Item") ? "Defeat <style=cIsHealth>enemies summoned by shrines</style> for items." : "<style=cIsHealth>Using shrines summons enemies.</style>");
        }
        static string GougeStats(int stacks)
        {
            string result = "";
            foreach (var entry in c.Numbers)
            {
                string key = entry.Key;
                bool chance = key == "Extra Item Chance Per Stack (%)";
                if (key.EndsWith(" Per Stack") || (chance && !c.B("Reward An Item"))) continue;
                float value = chance ? c.F(key) * Mathf.Max(0, stacks - 1) : c.Total(key, stacks);
                if (!chance && key != "Enemy Gold Reward (%)" && (value == 0 || value == -1)) continue;
                result = Tweaks.ModCompatibility.LookingGlassStatRegistry.AddValue(result,
                    chance ? "Extra Item Chance" : key.Replace(" (%)", ""), value, key.EndsWith(" (%)") ? "%" : "", "#E5C962");
            }
            return result;
        }
        static void BeforeChancePurchase(ShrineChanceBehavior __instance, out int __state)
        {
            __state = (int)successfulPurchases.GetValue(__instance);
        }
        static void AfterChancePurchase(ShrineChanceBehavior __instance, Interactor activator, int __state)
        {
            if (!NetworkServer.active) return;
            var result = chanceResults.GetOrCreateValue(__instance);
            result.Buyer = activator;
            result.Frame = Time.frameCount;
            result.Success = (int)successfulPurchases.GetValue(__instance) > __state;
        }
        static void Interact(Interactor interactor, GameObject interactableObject)
        {
            if (!NetworkServer.active || !Run.instance || !interactor || !interactableObject) return;
            var body = interactor.GetComponent<CharacterBody>(); int n = c.Count(body);
            var purchase = interactableObject.GetComponent<PurchaseInteraction>();
            if (n == 0 || !purchase || !purchase.isShrine || purchase.disableSpawnOnInteraction) return;
            bool eligibleReward = true;
            var chanceShrine = interactableObject.GetComponent<ShrineChanceBehavior>();
            if (chanceShrine)
            {
                bool success = chanceResults.TryGetValue(chanceShrine, out var result)
                    && result.Buyer == interactor && result.Frame == Time.frameCount && result.Success;
                chanceResults.Remove(chanceShrine);
                eligibleReward = !c.B("Chance Shrine Requires Successful Purchase") || success;
            }
            var prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/Encounters/MonstersOnShrineUseEncounter");
            if (!prefab) return;
            var obj = UnityEngine.Object.Instantiate(prefab, interactableObject.transform.position, Quaternion.identity);
            var director = obj.GetComponent<CombatDirector>(); var squad = obj.GetComponent<CombatSquad>();
            if (!director || !squad) { UnityEngine.Object.Destroy(obj); return; }
            float credits = c.Total("Monster Credits", n) * (Stage.instance ? Stage.instance.entryDifficultyCoefficient : Run.instance.difficultyCoefficient);
            var card = director.SelectMonsterCardForCombatShrine(credits);
            if (card == null) { UnityEngine.Object.Destroy(obj); return; }
            Rewards.ConfigureGold(director, () => c.Active ? c.F("Enemy Gold Reward (%)") : 100f);
            if (c.B("Reward An Item") && eligibleReward) obj.AddComponent<Encounter>().Setup(director, squad, Rewards.BoundsFor(interactableObject), n);
            director.combatSquad = squad;
            director.shouldSpawnOneWave = true;
            NetworkServer.Spawn(obj);
            director.CombatShrineActivation(interactor, credits, card);
        }
        internal sealed class Encounter : MonoBehaviour
        {
            CombatDirector director; CombatSquad squad; Bounds shrineBounds; int stacks; bool seen, paid;
            internal void Setup(CombatDirector d, CombatSquad s, Bounds p, int n)
            { director = d; squad = s; shrineBounds = p; stacks = n; squad.onMemberAddedServer += Added; squad.onDefeatedServer += Defeated; }
            void Added(CharacterMaster master) { seen = true; }
            void Defeated() { if (director && !director.enabled) Pay(); }
            void FixedUpdate() { if (NetworkServer.active && seen && director && !director.enabled && squad && squad.memberCount == 0) Pay(); }
            void OnDestroy() { if (squad) { squad.onMemberAddedServer -= Added; squad.onDefeatedServer -= Defeated; } }
            void Pay()
            {
                if (paid || !seen || !NetworkServer.active || !Run.instance) return; paid = true;
                float extra = c.F("Extra Item Chance Per Stack (%)") * Mathf.Max(0, stacks - 1);
                int count = 1 + Mathf.FloorToInt(extra / 100) + (Run.instance.treasureRng.nextNormalizedFloat < extra % 100 / 100 ? 1 : 0);
                for (int i = 0; i < count; i++) Drop(shrineBounds);
            }
        }
        static void Drop(Bounds bounds)
        {
            var run = Run.instance;
            List<PickupIndex>[] pools = { run.availableTier1DropList, run.availableTier2DropList, run.availableTier3DropList, run.availableBossDropList, run.availableLunarItemDropList, run.availableVoidTier1DropList, run.availableVoidTier2DropList, run.availableVoidTier3DropList, run.availableVoidBossDropList };
            var choices = new WeightedSelection<List<PickupIndex>>();
            for (int i = 0; i < pools.Length; i++)
            {
                float weight = c.F(rarities[i] + " Reward Weight"); if (weight <= 0) continue;
                var allowed = pools[i].Where(p => { var def = PickupCatalog.GetPickupDef(p); var item = def == null ? null : ItemCatalog.GetItemDef(def.itemIndex); return item && SpawnRequirements.ItemAllowed(item); }).ToList();
                if (allowed.Count > 0) choices.AddChoice(allowed, weight);
            }
            if (choices.Count == 0) return;
            var list = choices.Evaluate(run.treasureRng.nextNormalizedFloat); var pickup = new UniquePickup(list[run.treasureRng.RangeInt(0, list.Count)]);
            int players = c.B("Reward Each Player") ? Mathf.Max(1, run.participatingPlayerCount) : 1;
            float angle = run.treasureRng.nextNormalizedFloat * 360f;
            Rewards.PlayRewardEffect(bounds.center);
            for (int i = 0; i < players; i++) Rewards.Launch(pickup, bounds, i, players, angle);
        }
    }
}
