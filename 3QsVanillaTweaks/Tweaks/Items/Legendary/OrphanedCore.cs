using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Items;
using RoR2.Orbs;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class OrphanedCore
    {
        internal enum InheritanceMode
        {
            None,
            Movement,
            All
        }

        static R c;
        static ConfigEntry<InheritanceMode> inheritance;
        sealed class Clock
        {
            internal float Remaining = -1;
        }

        sealed class Copies
        {
            internal readonly Dictionary<ItemIndex, int> Counts = new Dictionary<ItemIndex, int>();
            internal bool Updating;
        }

        static readonly ConditionalWeakTable<FriendUnitController, Clock> clocks = new ConditionalWeakTable<FriendUnitController, Clock>();
        static readonly ConditionalWeakTable<Inventory, Copies> copies = new ConditionalWeakTable<Inventory, Copies>();
        static readonly FieldInfo interactionTimer = A.Field(typeof(FriendUnitController), "timer");
        static readonly FieldInfo overlap = A.Field(typeof(EntityStates.FriendUnit.KineticAura), "attack");
        internal static void Init()
        {
            c = RareItemSupport.Config("Orphaned Core", "PhysicsProjectile");
            c.B("Automatic Cleanse", true, "Replace pet-triggered cleansing with a periodic cleanse. Always includes the owner and also cleanses Buddy and nearby allies. Vanilla: false; pet to cleanse.");
            c.Pair("Cleanse Interval", 30, 0, "Seconds between automatic cleanses before stack cooldown reduction. Vanilla: pet interaction, 15-second recharge.");
            c.N("Cleanse Cooldown Reduction Per Stack (%)", 10, "Multiplicative cooldown reduction per additional item. Default: 30, 27, 24.3 seconds at one, two, three stacks.", 0, 100);
            c.Pair("Damage (%)", 400, 400, "Base ram damage before movement-speed scaling. Vanilla: 400% +400% per stack.");
            c.Pair("Proc Coefficient", 1, 0, "Ram proc coefficient. Vanilla: 1.");
            c.N("Respawn Delay", 30, "Seconds before replacing a lost Buddy. Vanilla: 30.", .1f);
            inheritance = C.Bind(c.Section, "Item Inheritance", InheritanceMode.Movement, "Which owner items Buddy inherits: None, Movement (vanilla), or All. All excludes hidden/internal items and Orphaned Core itself to prevent recursive Buddy spawning. Equipment is not copied.");
            if (c.Enabled.Value)
            {
                U.Patch(typeof(FriendUnitController), "FixedUpdate", typeof(OrphanedCore), postfix: nameof(Tick));
                U.Patch(typeof(FriendUnitController), "PetFriendUnit", typeof(OrphanedCore), prefix: nameof(Pet));
                U.Patch(typeof(FriendUnitController), "ForceInteractibilityUpdate", typeof(OrphanedCore), prefix: nameof(Interact));
                U.Patch(typeof(PhysicsProjectileBehavior), "InheritMovementItems", typeof(OrphanedCore), prefix: nameof(Inherit));
                U.Patch(typeof(EntityStates.FriendUnit.KineticAura), "OnEnter", typeof(OrphanedCore), postfix: nameof(Ram));
                c.Field("EntityStates.FriendUnit.KineticAura", "OnEnter", "chargeDamageCoefficient", RamCoefficient);
                c.Field("EntityStates.FriendUnit.KineticAura", "FixedUpdate", "chargeDamageCoefficient", RamCoefficient);
                c.Literal("RoR2.Items.PhysicsProjectileBehavior", "FixedUpdate", 30, () => c.F("Respawn Delay"));
            }

            RoR2Application.onLoad += () =>
            {
                float native = EntityStates.FriendUnit.FriendUnitLovesYou.cleanseRadius;
                c.Pair("Cleanse Radius", native, 0, "Nearby-ally cleanse radius in metres. The owner is always included by automatic cleansing. Vanilla: " + native + ".");
            };
            C.EffectiveSettingsChanged += Refresh;
            c.ShowStat = key => !key.StartsWith("Cleanse Interval") && !key.StartsWith("Cleanse Cooldown Reduction");
            // Register the actual interval ahead of the generic item stats.
            Tweaks.ModCompatibility.LookingGlassStatRegistry.RegisterItem(() => c.Item ? c.Item.itemIndex : ItemIndex.None, (master, n, text) => "Ram Damage: <style=cIsDamage>" + Number(c.Total("Damage (%)", n)) + "%</style>" + (c.B("Automatic Cleanse") ? "\nAutomatic Cleanse: <style=cIsUtility>" + Number(Interval(n)) + "s</style>" : ""), () => c.Active);
            c.Text(Description, () => c.B("Automatic Cleanse") ? "Gain a buddy that rams enemies and automatically cleanses you." : "Gain a buddy that rams enemies. Pet it to cleanse negative effects.");
        }

        static string Number(float f) => f.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        static string Description()
        {
            string mode = C.Get(inheritance) == InheritanceMode.All ? " Inherits your items." : C.Get(inheritance) == InheritanceMode.Movement ? " Inherits your movement items." : "";
            string cleanse = c.B("Automatic Cleanse") ? " Automatically <style=cIsUtility>cleanses negative effects</style> from you and nearby allies every <style=cIsUtility>" + Number(Interval(1)) + " seconds</style>" + (c.F("Cleanse Cooldown Reduction Per Stack (%)") == 0 ? "" : " <style=cStack>(-" + Number(c.F("Cleanse Cooldown Reduction Per Stack (%)")) + "% time per stack)</style>") + "." : " Pet it to <style=cIsUtility>cleanse negative effects</style>.";
            return "Gain a friendly <style=cIsUtility>Solus unit</style> that rams enemies for " + c.V("Damage (%)", "%") + " damage. Deals more damage the faster it moves." + mode + cleanse;
        }

        static float Interval(int n) => Mathf.Max(.1f, c.Total("Cleanse Interval", Mathf.Max(1, n)) * Mathf.Pow(1 - Mathf.Clamp01(c.F("Cleanse Cooldown Reduction Per Stack (%)") / 100), Mathf.Max(0, n - 1)));
        static CharacterBody Owner(CharacterBody buddy) => buddy && buddy.master && buddy.master.minionOwnership && buddy.master.minionOwnership.ownerMaster ? buddy.master.minionOwnership.ownerMaster.GetBody() : null;
        static int Stacks(CharacterBody buddy) => c.Count(Owner(buddy));
        static bool Automatic => c.Active && c.B("Automatic Cleanse");

        static bool Pet() => !Automatic;
        static bool Interact(FriendUnitController __instance)
        {
            if (!Automatic)
                return true;
            __instance.SetInteractibility(false);
            interactionTimer?.SetValue(__instance, 0f);
            return false;
        }

        static void Tick(FriendUnitController __instance)
        {
            if (!NetworkServer.active)
                return;
            var clock = clocks.GetOrCreateValue(__instance);
            var buddy = __instance.GetComponent<CharacterBody>();
            var owner = Owner(buddy);
            int n = c.Count(owner);
            if (!Automatic || n == 0 || !buddy || !buddy.healthComponent || !buddy.healthComponent.alive || !owner.healthComponent || !owner.healthComponent.alive)
            {
                clock.Remaining = -1;
                return;
            }

            float interval = Interval(n);
            if (clock.Remaining < 0)
                clock.Remaining = interval;
            clock.Remaining = Mathf.Min(clock.Remaining, interval) - Time.fixedDeltaTime;
            if (clock.Remaining > 0)
                return;
            clock.Remaining = interval;
            Cleanse(buddy, owner, n);
        }

        static void Cleanse(CharacterBody buddy, CharacterBody owner, int stacks)
        {
            var targets = new HashSet<CharacterBody>
            {
                owner,
                buddy
            };
            float radius = c.Total("Cleanse Radius", stacks);
            foreach (var member in TeamComponent.GetTeamMembers(buddy.teamComponent.teamIndex))
                if (member && member.body && (member.body.corePosition - buddy.corePosition).sqrMagnitude <= radius * radius)
                    targets.Add(member.body);
            foreach (var target in targets)
            {
                if (!target || !target.mainHurtBox || !target.healthComponent || !target.healthComponent.alive)
                    continue;
                // Reuse the game's cleanse, including its normal debuff/DoT handling.
                OrbManager.instance.AddOrb(new CleanseOrb { origin = buddy.corePosition, target = target.mainHurtBox, baseRemove = EntityStates.FriendUnit.FriendUnitLovesYou.curseStacksToRemove, stacks = stacks });
            }

            GameObject effect = RareItemSupport.StaticPrefab(typeof(EntityStates.FriendUnit.FriendUnitLovesYou), "effectPrefab");
            if (effect)
                EffectManager.SpawnEffect(effect, new EffectData { origin = buddy.corePosition, start = buddy.corePosition, rotation = Quaternion.identity }, true);
        }

        static float RamCoefficient()
        {
            var buddy = N.Body;
            int n = Stacks(buddy);
            // Vanilla supplies +100% Buddy damage per extra core using BoostDamage.
            return n > 0 ? c.Total("Damage (%)", n) / (100f * n) : EntityStates.FriendUnit.KineticAura.chargeDamageCoefficient;
        }

        static void Ram(EntityStates.FriendUnit.KineticAura __instance)
        {
            int n = Stacks(__instance.characterBody);
            if (n == 0)
                return;
            var attack = overlap?.GetValue(__instance) as OverlapAttack;
            if (attack != null)
                attack.procCoefficient = c.Total("Proc Coefficient", n);
        }

        static bool Inherit(PhysicsProjectileBehavior __instance, Inventory inventory)
        {
            if (!c.Active)
                return true;
            if (!NetworkServer.active || !inventory || !__instance.body || !__instance.body.inventory)
                return false;
            var state = copies.GetOrCreateValue(inventory);
            if (state.Updating)
                return false;
            state.Updating = true;
            try
            {
                var owner = __instance.body.inventory;
                var mode = C.Get(inheritance);
                var desired = new Dictionary<ItemIndex, int>();
                if (c.Count(__instance.body) > 0 && mode != InheritanceMode.None)
                {
                    foreach (var index in owner.itemAcquisitionOrder.ToArray())
                    {
                        var item = ItemCatalog.GetItemDef(index);
                        if (!item || item.hidden || index == c.Item.itemIndex || mode == InheritanceMode.Movement && !item.ContainsTag(ItemTag.MobilityRelated))
                            continue;
                        int count = owner.GetItemCountEffective(index);
                        if (count > 0)
                            desired[index] = count;
                    }
                }

                foreach (var index in state.Counts.Keys.Concat(desired.Keys).Distinct().ToArray())
                {
                    state.Counts.TryGetValue(index, out int previous);
                    desired.TryGetValue(index, out int next);
                    int current = inventory.GetItemCountPermanent(index);
                    // Remove only this module's copies, preserving the summon’s native inventory.
                    int baseCount = Mathf.Max(0, current - previous);
                    int target = baseCount + next;
                    if (target > current)
                        inventory.GiveItemPermanent(index, target - current);
                    else if (current > target)
                        inventory.RemoveItemPermanent(index, current - target);
                }

                state.Counts.Clear();
                foreach (var pair in desired)
                    state.Counts.Add(pair.Key, pair.Value);
            }
            finally
            {
                state.Updating = false;
            }

            return false;
        }

        static void Refresh()
        {
            if (c == null || !c.Active || !NetworkServer.active)
                return;
            foreach (var body in CharacterBody.readOnlyInstancesList.ToArray())
            {
                if (!body)
                    continue;
                var behavior = body.GetComponent<PhysicsProjectileBehavior>();
                if (behavior)
                    behavior.UpdateFriendUnitMovementItems();
            }
        }
    }
}
