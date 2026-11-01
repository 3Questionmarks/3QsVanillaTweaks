using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using HarmonyLib;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Boss
{
    internal static class BossItemSupport
    {
        internal static R Config(string name, string item, bool enabled = true)
        {
            U.Ensure();
            return new R(name, item, enabled, "04 Boss");
        }

        internal static float HealingMaximum(HealthComponent hc)
        {
            // Reuse the shield-aware healing policy when that module is enabled.
            var method = A.Method(typeof(Tweaks.Others.ShieldMechanics), "HealingHealth");
            return method != null ? (float)method.Invoke(null, new object[] { hc }) : hc.fullHealth;
        }

        internal static void Cores() => U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(CoresIL)));
        static IEnumerable<CodeInstruction> CoresIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int local = -1;
            int anchor = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "TeamSizeDamageBonus");
            for (int j = anchor + 1; anchor >= 0 && j < Math.Min(anchor + 10, code.Count); j++)
                if (Local(code[j], true) >= 0)
                {
                    local = Local(code[j], true);
                    break;
                }

            bool found = false;
            for (int j = code.Count - 1; j >= 0; j--)
                if (code[j].LoadsConstant(1f) && code.Skip(Math.Max(0, j - 5)).Take(Math.Min(j, 5)).Any(i => Local(i, false) == local))
                {
                    code.InsertRange(j + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(CoresDamage))) });
                    found = true;
                    break;
                }

            if (!found)
                throw new InvalidOperationException("Empathy Cores damage coefficient not found.");
            return code;
        }

        static float CoresDamage(float vanilla, CharacterBody b)
        {
            var c = EmpathyCores.c;
            int n = b.inventory ? b.inventory.GetItemCountEffective(RoR2Content.Items.TeamSizeDamageBonus) : 0;
            return c.Active && n > 0 ? c.Total("Damage Per Ally (%)", n) / (100 * n) : vanilla;
        }

        internal static void OptionalPearl() => CapacityBonuses();
        static bool capacityInstalled;
        internal static void CapacityBonuses()
        {
            if (capacityInstalled) return;
            capacityInstalled = true;
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(CapacityIL)));
        }
        static void CapacityStats(CharacterBody body)
        {
            if (!body.inventory) return;
            int shiny = body.inventory.GetItemCountEffective(RoR2Content.Items.ShinyPearl);
            int pearls = body.inventory.GetItemCountEffective(RoR2Content.Items.Pearl);
            var irradiant = IrradiantPearl.c;
            var pearl = global::Tweaks.Items.Boss.Pearl.c;
            bool changeShiny = irradiant != null && irradiant.Active && shiny > 0;
            bool changePearl = pearl != null && pearl.Active && pearls > 0;
            if (changeShiny || changePearl)
            {
                float native = 1 + .1f * (shiny + pearls);
                float wanted = 1 + (changeShiny ? irradiant.Total("Stat Increase (%)", shiny) / 100 : .1f * shiny)
                    + (changePearl ? pearl.Total("Maximum Health (%)", pearls) / 100 : .1f * pearls);
                U.Scale(body, "maxHealth", 100 * (wanted / native - 1));
            }
            var knurl = TitanicKnurl.c;
            int count = knurl == null ? 0 : knurl.Count(body);
            if (count > 0) U.Scale(body, "maxHealth", knurl.Total("Maximum Health (%)", count));
        }
        // Run before barrier/hull capacities and the native health reconciliation,
        // so every calculation uses the final maximum health for this frame.
        static IEnumerable<CodeInstruction> CapacityIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList(); int found = 0;
            for (int i = 6; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo setter) || setter.DeclaringType != typeof(CharacterBody) || setter.Name != "set_maxBarrier") continue;
                if (code[i - 1].opcode != OpCodes.Add || !(code[i - 2].operand is MethodInfo shield) || shield.Name != "get_maxShield" || !(code[i - 4].operand is MethodInfo health) || health.Name != "get_maxHealth" || code[i - 3].opcode != OpCodes.Ldarg_0 || code[i - 5].opcode != OpCodes.Ldarg_0 || code[i - 6].opcode != OpCodes.Ldarg_0) continue;
                int start = i - 6;
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(code[start].labels); code[start].labels.Clear();
                load.blocks.AddRange(code[start].blocks); code[start].blocks.Clear();
                code.InsertRange(start, new[] { load, new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(CapacityStats))) });
                found++; i += 2;
            }
            if (found != 1) throw new InvalidOperationException("Boss item capacity anchor changed: " + found);
            return code;
        }

        static bool summons;
        internal static void Summons()
        {
            if (summons)
                return;
            summons = true;
            U.Harmony.Patch(A.Method(typeof(ProjectileManager), "FireProjectile", new[] { typeof(FireProjectileInfo) }), prefix: new HarmonyMethod(typeof(BossItemSupport), nameof(ConstructChance)));
            U.Patch(typeof(CharacterMaster), "GetDeployableSameSlotLimit", typeof(BossItemSupport), postfix: nameof(Limit));
            U.Patch(typeof(CharacterMaster), "IsDeployableLimited", typeof(BossItemSupport), postfix: nameof(Limited));
            U.Patch(typeof(CharacterMaster), "AddDeployable", typeof(BossItemSupport), prefix: nameof(Deploy));
            U.Patch(typeof(RoR2.Items.BeetleGlandBodyBehavior), "FixedUpdate", typeof(BossItemSupport), prefix: nameof(GuardTick), postfix: nameof(GuardEnd));
        }

        static bool ConstructChance(FireProjectileInfo fireProjectileInfo)
        {
            var c = DefenseNucleus.c;
            if (c == null || !fireProjectileInfo.projectilePrefab || !fireProjectileInfo.projectilePrefab.name.StartsWith("MinorConstructOnKillProjectile", StringComparison.Ordinal))
                return true;
            var body = fireProjectileInfo.owner ? fireProjectileInfo.owner.GetComponent<CharacterBody>() : null;
            int n = c.Count(body);
            return n == 0 || U.Roll(c.Total("Summon Chance (%)", n), body);
        }

        static R SummonConfig(DeployableSlot slot) => slot == DeployableSlot.MinorConstructOnKill ? DefenseNucleus.c : slot == DeployableSlot.BeetleGuardAlly ? QueensGland.c : null;
        static int OwnerCount(R c, CharacterMaster m) => c != null && c.Active && m && m.inventory ? m.inventory.GetItemCountEffective(c.Item) : 0;
        static void Limit(CharacterMaster __instance, DeployableSlot slot, ref int __result)
        {
            var c = SummonConfig(slot);
            int n = OwnerCount(c, __instance);
            if (n == 0)
                return;
            int swarms = RunArtifactManager.instance && RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.swarmsArtifactDef) && slot == DeployableSlot.BeetleGuardAlly ? 2 : 1;
            __result = Mathf.Max(0, Mathf.RoundToInt(c.Total(slot == DeployableSlot.BeetleGuardAlly ? "Maximum Guards" : "Maximum Constructs", n))) * swarms;
        }

        static void Limited(CharacterMaster __instance, DeployableSlot slot, ref bool __result)
        {
            if (slot == DeployableSlot.MinorConstructOnKill && OwnerCount(DefenseNucleus.c, __instance) > 0 && DefenseNucleus.c.B("Replace Oldest") && __instance.GetDeployableSameSlotLimit(slot) > 0)
                __result = false;
        }

        static void SetBoost(Inventory inv, ItemDef item, float percent)
        {
            int old = inv.GetItemCountPermanent(item.itemIndex), desired = Mathf.Max(0, Mathf.RoundToInt(percent / 10f));
            if (desired > old)
                inv.GiveItemPermanent(item, desired - old);
            else if (old > desired)
                inv.RemoveItemPermanent(item, old - desired);
        }

        static void Deploy(CharacterMaster __instance, Deployable deployable, DeployableSlot slot)
        {
            if (!NetworkServer.active || !deployable)
                return;
            var c = SummonConfig(slot);
            int n = OwnerCount(c, __instance);
            if (n == 0)
                return;
            var m = deployable.GetComponent<CharacterMaster>();
            if (!m || !m.inventory)
                return;
            SetBoost(m.inventory, RoR2Content.Items.BoostDamage, c.Total("Damage Bonus (%)", n));
            SetBoost(m.inventory, RoR2Content.Items.BoostHp, c.Total("Health Bonus (%)", n));
            var body = m.GetBody();
            if (slot == DeployableSlot.MinorConstructOnKill)
            {
                if (body && c.B("Mechanical"))
                    body.bodyFlags |= CharacterBody.BodyFlags.Mechanical;
                m.onBodyStart += b =>
                {
                    if (c.Active && c.B("Mechanical"))
                        b.bodyFlags |= CharacterBody.BodyFlags.Mechanical;
                };
                if (deployable.onUndeploy == null)
                    deployable.onUndeploy = new UnityEngine.Events.UnityEvent();
                deployable.onUndeploy.AddListener(() =>
                {
                    if (m)
                        m.TrueKill();
                });
            }

            if (body)
            {
                body.RecalculateStats();
                if (body.healthComponent)
                    body.healthComponent.Networkhealth = body.maxHealth;
            }
        }

        static readonly FieldInfo guardTimer = A.Field(typeof(RoR2.Items.BeetleGlandBodyBehavior), "guardResummonCooldown");
        struct GuardState
        {
            internal int Stack, Count;
        }

        static void GuardTick(RoR2.Items.BeetleGlandBodyBehavior __instance, out GuardState __state)
        {
            __state = new GuardState
            {
                Stack = __instance.stack,
                Count = __instance.body && __instance.body.master ? __instance.body.master.GetDeployableCount(DeployableSlot.BeetleGuardAlly) : 0
            };
            var c = QueensGland.c;
            if (c == null || !c.Active || !__instance.body || !__instance.body.master)
                return;
            __instance.stack = __instance.body.master.GetDeployableSameSlotLimit(DeployableSlot.BeetleGuardAlly);
        }

        static void GuardEnd(RoR2.Items.BeetleGlandBodyBehavior __instance, GuardState __state)
        {
            __instance.stack = __state.Stack;
            if (QueensGland.c != null && QueensGland.c.Active && guardTimer != null)
            {
                if (__instance.body && __instance.body.master && __instance.body.master.GetDeployableCount(DeployableSlot.BeetleGuardAlly) > __state.Count)
                    guardTimer.SetValue(__instance, QueensGland.c.F("Respawn Delay"));
            }
        }

        internal static void Titan()
        {
            var target = A.Method(typeof(GoldTitanManager), "TryStartChannelingTitansServer");
            U.Harmony.Patch(target, transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(TitanIL)));
        }

        static IEnumerable<CodeInstruction> TitanIL(IEnumerable<CodeInstruction> input)
        {
            bool found = false;
            foreach (var i in input)
            {
                yield return i;
                if (i.opcode == OpCodes.Ldc_R4 && i.LoadsConstant(.5f))
                {
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(TitanExponent)));
                    found = true;
                }

                if (i.opcode == OpCodes.Call && i.operand is MethodInfo m && m.DeclaringType == typeof(Mathf) && m.Name == "Pow")
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4, found ? 1 : 0);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(TitanPower)));
                }
            }
        }

        // Pow(count, exponent) becomes linear scaling; preserve False Son's preceding multiplier.
        static float TitanExponent(float vanilla) => HalcyonSeed.c.Active ? 1 : vanilla;
        static float TitanPower(float value, int damage)
        {
            var c = HalcyonSeed.c;
            if (!c.Active)
                return value;
            return 1 + c.Total(damage == 1 ? "Damage Bonus (%)" : "Health Bonus (%)", Mathf.Max(1, Mathf.RoundToInt(value))) / 100f;
        }

        internal static void Urn()
        {
            U.Patch(typeof(RoR2.Items.SiphonOnLowHealthItemBodyBehavior), "FixedUpdate", typeof(BossItemSupport), postfix: nameof(UrnTick));
            U.Patch(typeof(SiphonNearbyController), "FixedUpdate", typeof(BossItemSupport), prefix: nameof(UrnController));
            U.Harmony.Patch(A.Method(typeof(SiphonNearbyController), "Tick"), prefix: new HarmonyMethod(typeof(BossItemSupport), nameof(UrnBefore)), finalizer: new HarmonyMethod(typeof(BossItemSupport), nameof(UrnAfter)), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(UrnIL)));
            U.Patch(typeof(HealthComponent), "Heal", typeof(BossItemSupport), prefix: nameof(UrnHealing));
        }

        static R UrnConfig(SiphonNearbyController ctrl, out int n)
        {
            var attachment = ctrl.GetComponent<NetworkedBodyAttachment>();
            // Other game effects share this controller; only alter the item's own attachment.
            var c = MiredUrn.c;
            n = c != null && ctrl.name.StartsWith("SiphonNearbyBodyAttachment", StringComparison.Ordinal) && attachment ? c.Count(attachment.attachedBody) : 0;
            return c;
        }

        static void UrnTick(RoR2.Items.SiphonOnLowHealthItemBodyBehavior __instance)
        {
            var field = A.Field(__instance.GetType(), "siphonNearbyController");
            var ctrl = field?.GetValue(__instance) as SiphonNearbyController;
            int n = MiredUrn.c.Count(__instance.body);
            if (ctrl && n > 0)
                ctrl.NetworkmaxTargets = __instance.body.healthComponent.alive ? Mathf.Max(0, Mathf.RoundToInt(MiredUrn.c.Total("Maximum Targets", n))) : 0;
        }

        static void UrnController(SiphonNearbyController __instance)
        {
            var c = UrnConfig(__instance, out int n);
            if (n == 0)
                return;
            float interval = Mathf.Max(.01f, c.Total("Damage Interval", n));
            __instance.tickRate = 1 / interval;
            __instance.damagePerSecondCoefficient = c.Total("Damage Per Tick (%)", n) / (100 * interval);
            __instance.Networkradius = c.Total("Radius", n);
        }

        static float UrnProc(float vanilla, SiphonNearbyController ctrl)
        {
            var c = UrnConfig(ctrl, out int n);
            return n > 0 ? c.Total("Proc Coefficient", n) : vanilla;
        }

        [ThreadStatic]
        static SiphonNearbyController healingUrn;
        static void UrnBefore(SiphonNearbyController __instance, out SiphonNearbyController __state)
        {
            __state = healingUrn;
            healingUrn = __instance;
        }

        static Exception UrnAfter(SiphonNearbyController __state, Exception __exception)
        {
            healingUrn = __state;
            return __exception;
        }

        static void UrnHealing(HealthComponent __instance, ref float amount)
        {
            if (!healingUrn)
                return;
            var attachment = healingUrn.GetComponent<NetworkedBodyAttachment>();
            if (attachment && attachment.attachedBody && attachment.attachedBody.healthComponent == __instance)
                amount = UrnHeal(amount, healingUrn);
        }

        static float UrnHeal(float amount, SiphonNearbyController ctrl)
        {
            var c = UrnConfig(ctrl, out int n);
            return n > 0 ? amount * c.Total("Healing From Damage (%)", n) / 100 : amount;
        }

        static IEnumerable<CodeInstruction> UrnIL(IEnumerable<CodeInstruction> input)
        {
            foreach (var i in input)
            {
                if (i.opcode == OpCodes.Stfld && i.operand is FieldInfo f && f.Name == "procCoefficient")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(UrnProc)));
                }

                yield return i;
            // Heal(amount, default mask) has the amount stored in a local; modify it at its store instead below.
            }
        }

        internal static void Molten() => U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(MoltenIL)));
        static IEnumerable<CodeInstruction> MoltenIL(IEnumerable<CodeInstruction> input)
        {
            bool active = false, found = false;
            foreach (var i in input)
            {
                if (i.operand is FieldInfo f && f.Name == "FireballsOnHit")
                    active = true;
                yield return i;
                if (active && !found && i.opcode != OpCodes.Ldc_R4 && i.LoadsConstant(3))
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(MoltenCount)));
                    found = true;
                }
            }

            if (!found)
                throw new InvalidOperationException("Molten Perforator fireball count not found.");
        }

        static int MoltenCount(int vanilla, DamageInfo info)
        {
            var c = MoltenPerforator.c;
            int n = info.attacker ? c.Count(info.attacker.GetComponent<CharacterBody>()) : 0;
            return n > 0 ? Mathf.Max(1, Mathf.RoundToInt(c.Total("Fireballs", n))) : vanilla;
        }

        internal static void Spleen()
        {
            U.Patch(typeof(DelayBlast), "FixedUpdate", typeof(BossItemSupport), prefix: nameof(SpleenStart));
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(SpleenCritIL)));
            U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "ProcessHitEnemy"), prefix: new HarmonyMethod(typeof(BossItemSupport), nameof(SpleenBefore)), finalizer: new HarmonyMethod(typeof(BossItemSupport), nameof(SpleenAfter)));
            U.Harmony.Patch(A.Method(typeof(DotController), "InflictDot", new[] { typeof(InflictDotInfo).MakeByRefType() }), prefix: new HarmonyMethod(typeof(BossItemSupport), nameof(SpleenDot)));
        }

        [ThreadStatic]
        static DamageInfo spleenHit;
        static void SpleenBefore(DamageInfo damageInfo, out DamageInfo __state)
        {
            __state = spleenHit;
            spleenHit = damageInfo;
        }

        static Exception SpleenAfter(DamageInfo __state, Exception __exception)
        {
            spleenHit = __state;
            return __exception;
        }

        static bool SpleenDot(ref InflictDotInfo inflictDotInfo)
        {
            var hit = spleenHit;
            var c = Shatterspleen.c;
            if (hit == null || !hit.crit || inflictDotInfo.dotIndex != DotController.DotIndex.Bleed || inflictDotInfo.attackerObject != hit.attacker || (ulong)(hit.damageType & DamageType.BleedOnHit) != 0)
                return true;
            var body = hit.attacker ? hit.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(body);
            if (n == 0)
                return true;
            if (!U.Roll(c.Total("Bleed Chance On Critical Hit (%)", n), body))
                return false;
            inflictDotInfo.duration = c.Total("Bleed Duration", n) * hit.procCoefficient;
            inflictDotInfo.damageMultiplier *= c.Total("Bleed Damage Per Second (%)", n) / 80;
            return inflictDotInfo.duration > 0 && inflictDotInfo.damageMultiplier > 0;
        }

        static IEnumerable<CodeInstruction> SpleenCritIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int local = -1;
            int anchor = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "BleedOnHitAndExplode");
            for (int j = anchor + 1; anchor >= 0 && j < Math.Min(anchor + 10, code.Count); j++)
                if (Local(code[j], true) >= 0)
                {
                    local = Local(code[j], true);
                    break;
                }

            bool found = false;
            for (int j = 0; j < code.Count; j++)
            {
                if (Local(code[j], false) != local)
                    continue;
                int at = code.FindIndex(j + 1, Math.Min(10, code.Count - j - 1), i => i.LoadsConstant(5f));
                if (at < 0)
                    continue;
                code.InsertRange(at + 1, new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(SpleenCrit))) });
                found = true;
                break;
            }

            if (!found)
                throw new InvalidOperationException("Shatterspleen critical chance not found.");
            return code;
        }

        static float SpleenCrit(float vanilla, CharacterBody b)
        {
            var c = Shatterspleen.c;
            int n = c.Count(b);
            return n > 0 ? c.Total("Critical Chance (%)", n) : vanilla;
        }

        static void SpleenStart(DelayBlast __instance)
        {
            var c = Shatterspleen.c;
            var owner = __instance.attacker ? __instance.attacker.GetComponent<CharacterBody>() : null;
            int n = c.Count(owner);
            if (n > 0 && __instance.name.StartsWith("BleedOnHitAndExplodeDelay", StringComparison.Ordinal))
                __instance.procCoefficient = c.Total("Proc Coefficient", n);
        }

        internal static void Pearl()
        {
            CapacityBonuses();
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(PearlIL)));
            // Run after our shared stat callbacks so banner/item armour and regen
            // are included in the actual-stat multipliers.
            U.Harmony.Patch(A.Method(typeof(CharacterBody), "RecalculateStats"), postfix: new HarmonyMethod(typeof(BossItemSupport), nameof(PearlAfter)) { priority = Priority.Last });
        }

        static int Local(CodeInstruction i, bool store)
        {
            if (i.opcode == (store ? OpCodes.Stloc_0 : OpCodes.Ldloc_0))
                return 0;
            if (i.opcode == (store ? OpCodes.Stloc_1 : OpCodes.Ldloc_1))
                return 1;
            if (i.opcode == (store ? OpCodes.Stloc_2 : OpCodes.Ldloc_2))
                return 2;
            if (i.opcode == (store ? OpCodes.Stloc_3 : OpCodes.Ldloc_3))
                return 3;
            if (i.opcode != (store ? OpCodes.Stloc : OpCodes.Ldloc) && i.opcode != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S))
                return -1;
            return i.operand is LocalBuilder b ? b.LocalIndex : i.operand is LocalVariableInfo v ? v.LocalIndex : Convert.ToInt32(i.operand);
        }

        static IEnumerable<CodeInstruction> PearlIL(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            int local = -1;
            int anchor = code.FindIndex(i => i.operand is FieldInfo f && f.Name == "ShinyPearl");
            for (int j = anchor + 1; anchor >= 0 && j < Math.Min(anchor + 10, code.Count); j++)
                if (Local(code[j], true) >= 0)
                {
                    local = Local(code[j], true);
                    break;
                }

            if (local < 0)
                throw new InvalidOperationException("Irradiant Pearl item-count local not found.");
            for (int j = code.Count - 2; j >= 0; j--)
            {
                if (Local(code[j], false) != local || code[j + 1].opcode != OpCodes.Conv_R4)
                    continue;
                int mode = j >= 1 && code[j - 1].LoadsConstant(.1f) ? 2 : j + 6 < code.Count && code[j + 2].LoadsConstant(.1f) && code[j + 5].opcode == OpCodes.Mul ? 1 : 0;
                code.InsertRange(j + 2, new[] { new CodeInstruction(OpCodes.Ldc_I4, mode), new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(PearlCount))) });
            }

            return code;
        }

        static float PearlCount(float count, int mode)
        {
            var c = IrradiantPearl.c;
            if (!c.Active)
                return count;
            if (mode == 1 && c.B("Percentage Regeneration") || mode == 2 && c.B("Multiply Actual Armour"))
                return 0;
            return c.Total("Stat Increase (%)", Mathf.RoundToInt(count)) / 10f;
        }

        static void PearlAfter(CharacterBody __instance)
        {
            PearlStats(__instance);
        }

        static void PearlStats(CharacterBody b)
        {
            var c = IrradiantPearl.c;
            int n = c.Count(b);
            if (n == 0)
                return;
            float extra = c.Total("Stat Increase (%)", n) / 100;
            if (c.B("Multiply Actual Armour"))
                U.Scale(b, "armor", extra * 100);
            if (c.B("Percentage Regeneration") && (b.regen > 0 || c.B("Multiply Negative Regeneration")))
                U.Scale(b, "regen", extra * 100);
        }

        internal static void Mycelium()
        {
            U.Patch(typeof(ProjectileController), "Start", typeof(BossItemSupport), prefix: nameof(MushProjectile));
            U.Patch(typeof(ProjectileDotZone), "Start", typeof(BossItemSupport), prefix: nameof(MushZone));
            RoR2Application.onLoad += ConfigureMush;
        }

        static string mushRootName;
        static readonly Dictionary<string, bool> mushPrefabs = new Dictionary<string, bool>();
        static readonly Dictionary<string, string> mushLabels = new Dictionary<string, string>();
        static readonly Dictionary<string, int> mushLabelCounts = new Dictionary<string, int>();
        static string MushLabel(GameObject prefab, bool cloud)
        {
            if (mushLabels.TryGetValue(prefab.name, out string label)) return label;
            string basic = cloud ? "Spore Cloud" : prefab.name == mushRootName ? "Landing Projectile" : "Spore Projectile";
            mushLabelCounts.TryGetValue(basic, out int number);
            mushLabelCounts[basic] = ++number;
            label = basic + (number == 1 ? "" : " " + number) + " - ";
            mushLabels[prefab.name] = label;
            return label;
        }
        static void ConfigureMush()
        {
            var c = MyceliumCap.c;
            if (!c.Active)
                return;
            Type state = typeof(EntityStates.EntityState).Assembly.GetType("EntityStates.MushShot.MushShotFall");
            if (state == null)
            {
                ThreeQsVanillaTweaks.Log.Warning("Mycelium Cap: landing state not found.");
                return;
            }

            U.Harmony.Patch(A.Method(state, "DoStompExplosionAuthority"), transpiler: new HarmonyMethod(typeof(BossItemSupport), nameof(MushCountIL)));
            object wrapped = A.Field(state, "mushShotSplatPrefab").GetValue(null);
            var conversion = wrapped.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == typeof(GameObject));
            var prefab = wrapped as GameObject ?? conversion?.Invoke(null, new[] { wrapped }) as GameObject;
            mushRootName = prefab ? prefab.name : null;
            DiscoverMush(prefab, new HashSet<GameObject>());
            ThreeQsVanillaTweaks.Log.Info("Mycelium Cap: configured " + mushPrefabs.Count + " projectile prefabs.");
        }

        static IEnumerable<CodeInstruction> MushCountIL(IEnumerable<CodeInstruction> input)
        {
            foreach (var i in input)
            {
                yield return i;
                if (i.opcode == OpCodes.Ldsfld && i.operand is FieldInfo f && f.Name == "projectileCount")
                {
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call, A.Method(typeof(BossItemSupport), nameof(MushCount)));
                }
            }
        }

        static int MushCount(int vanilla, EntityStates.EntityState state)
        {
            var c = MyceliumCap.c;
            var b = A.Field(state.GetType(), "body")?.GetValue(state) as CharacterBody;
            int n = c.Count(b);
            return n > 0 ? Mathf.Max(1, Mathf.RoundToInt(c.Total("Spore Projectiles", n))) : vanilla;
        }

        static void DiscoverMush(GameObject prefab, HashSet<GameObject> visited)
        {
            if (!prefab || !visited.Add(prefab))
                return;
            bool cloud = prefab.GetComponent<ProjectileDotZone>();
            if (prefab.GetComponent<ProjectileController>())
            {
                mushPrefabs[prefab.name] = cloud;
                BindMushValues(prefab, cloud);
            }

            foreach (var component in prefab.GetComponents<Component>())
            {
                if (!component || !(component is MonoBehaviour))
                    continue;
                foreach (var field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 || field.Name.IndexOf("ghost", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;
                    if (field.FieldType == typeof(GameObject))
                        DiscoverMush(field.GetValue(component) as GameObject, visited);
                }
            }
        }

        static void MushZone(ProjectileDotZone __instance)
        {
            var pc = __instance.GetComponent<ProjectileController>();
            if (pc)
                ApplyMush(pc, false);
        }

        static void BindMushValues(GameObject prefab, bool cloud)
        {
            var c = MyceliumCap.c;
            string prefix = MushLabel(prefab, cloud);
            var zone = prefab.GetComponent<ProjectileDotZone>();
            var simple = prefab.GetComponent<ProjectileSimple>();
            var explosion = prefab.GetComponent<ProjectileImpactExplosion>();
            if (zone)
            {
                c.Pair(prefix + "Damage Multiplier (%)", zone.damageCoefficient * 100, 0, "Damage multiplier per cloud tick. Vanilla: " + zone.damageCoefficient * 100 + "%.");
                c.Pair(prefix + "Interval", 1 / Mathf.Max(.01f, zone.resetFrequency), 0, "Cloud tick interval in seconds, read from the vanilla prefab.");
                c.Pair(prefix + "Cloud Lifetime", zone.lifetime, 0, "Cloud lifetime in seconds. Vanilla: " + zone.lifetime + ".");
            }

            if (simple)
            {
                c.Pair(prefix + "Speed", simple.desiredForwardSpeed, 0, "Projectile speed, read from the vanilla prefab.");
                c.Pair(prefix + "Projectile Lifetime", simple.lifetime, 0, "Projectile lifetime, read from the vanilla prefab.");
            }

            if (explosion)
            {
                c.Pair(prefix + "Explosion Radius", explosion.blastRadius, 0, "Explosion radius in metres, read from the vanilla prefab.");
                c.Pair(prefix + "Explosion Damage Multiplier (%)", explosion.blastDamageCoefficient * 100, 0, "Explosion damage multiplier, read from the vanilla prefab.");
            }

            var sphere = prefab.GetComponentInChildren<SphereCollider>();
            if (zone && sphere)
                c.Pair(prefix + "Radius", sphere.radius, 0, "Local cloud hitbox radius, read from the vanilla prefab.");
        }

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ProjectileController, object> scaledMush = new System.Runtime.CompilerServices.ConditionalWeakTable<ProjectileController, object>();
        static void MushProjectile(ProjectileController __instance)
        {
            ApplyMush(__instance, true);
        }

        static void ApplyMush(ProjectileController __instance, bool scaleDamage)
        {
            var c = MyceliumCap.c;
            var owner = __instance.owner ? __instance.owner.GetComponent<CharacterBody>() : null;
            int n = c.Count(owner);
            if (n == 0)
                return;
            string name = __instance.name.Replace("(Clone)", "");
            if (!mushPrefabs.TryGetValue(name, out bool cloud))
                return;
            __instance.procCoefficient = c.Total(cloud ? "Cloud Proc Coefficient" : "Spore Proc Coefficient", n);
            if (!mushLabels.TryGetValue(name, out string prefix)) return;
            var zone = __instance.GetComponent<ProjectileDotZone>();
            if (zone && c.Numbers.ContainsKey(prefix + "Interval"))
            {
                zone.overlapProcCoefficient = 1;
                zone.damageCoefficient = c.Total(prefix + "Damage Multiplier (%)", n) / 100;
                zone.resetFrequency = 1 / Mathf.Max(.01f, c.Total(prefix + "Interval", n));
                zone.lifetime = c.Total(prefix + "Cloud Lifetime", n);
                var sphere = zone.GetComponentInChildren<SphereCollider>();
                if (sphere && c.Numbers.ContainsKey(prefix + "Radius"))
                    sphere.radius = c.Total(prefix + "Radius", n);
            }

            var simple = __instance.GetComponent<ProjectileSimple>();
            if (simple && c.Numbers.ContainsKey(prefix + "Speed"))
            {
                simple.desiredForwardSpeed = c.Total(prefix + "Speed", n);
                simple.lifetime = c.Total(prefix + "Projectile Lifetime", n);
            }

            var explosion = __instance.GetComponent<ProjectileImpactExplosion>();
            if (explosion && c.Numbers.ContainsKey(prefix + "Explosion Radius"))
            {
                explosion.blastRadius = c.Total(prefix + "Explosion Radius", n);
                explosion.blastDamageCoefficient = c.Total(prefix + "Explosion Damage Multiplier (%)", n) / 100;
            }

            if (name == mushRootName && scaleDamage && !scaledMush.TryGetValue(__instance, out _))
            {
                scaledMush.Add(__instance, new object ());
                var damage = __instance.GetComponent<ProjectileDamage>();
                if (damage)
                    damage.damage *= c.Total("Landing Damage (%)", n) / (1000 + 200f * (n - 1));
            }
        }
    }
}
