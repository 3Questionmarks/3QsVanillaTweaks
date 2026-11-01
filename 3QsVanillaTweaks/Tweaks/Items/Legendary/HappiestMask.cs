using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using RoR2.Projectile;
using UnityEngine;
using UnityEngine.Networking;
using C = Config.ConfigManager;
using S = Tweaks.Items.Common.CommonItemSupport;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using N = Tweaks.Items.Uncommon.UncommonNative;
using A = ThreeQsVanillaTweaks.ReflectionTools;

namespace Tweaks.Items.Legendary
{
    internal static class HappiestMask
    {
        static R c;
        static bool warned;
        sealed class Ghost
        {
            internal CharacterMaster Master;
            internal CharacterMaster Owner;
            internal float Expiry;
        }

        sealed class Group
        {
            internal readonly List<Ghost> Ghosts = new List<Ghost>();
        }

        static readonly ConditionalWeakTable<CharacterMaster, Group> groups = new ConditionalWeakTable<CharacterMaster, Group>();
        static readonly ConditionalWeakTable<CharacterMaster, Ghost> ghosts = new ConditionalWeakTable<CharacterMaster, Ghost>();
        internal static void Init()
        {
            c = RareItemSupport.Config("Happiest Mask", "GhostOnKill");
            c.B("Rework", true, "Kills summon an invulnerable ghost of your survivor rather than a random enemy ghost.");
            c.Pair("Ghost Damage (%)", 300, 150, "Ghost damage relative to its normal survivor damage. Vanilla enemy ghosts: 1600%.");
            c.N("Ghost Duration", -1, "Lifetime in seconds. -1 lasts until the stage ends.", -1);
            c.B("Duration Scales With Stacks", false, "Apply the extra ghost duration for additional item stacks.");
            c.N("Extra Ghost Duration Per Stack", 0, "Additional seconds per extra stack when duration scaling is enabled.");
            c.Pair("Maximum Ghosts", 1, 0, "Maximum simultaneous self ghosts.");
            c.B("Kills Attributed To Owner", true, "Credit ghost kills to the owner, including on-kill item effects.");
            c.B("Inherits Items", false, "Copy the owner's items, excluding recursive ally-summoning and item-generation effects.");
            c.Pair("Summon Chance (%)", 100, 0, "Chance to fill an available ghost slot on a kill.");
            c.Suppress("RoR2.GlobalEventManager", "OnCharacterDeath", () => c.B("Rework"));
            if (c.Enabled.Value)
            {
                U.Ensure();
                U.Kill += Kill;
                U.Tick += Tick;
                U.Stats += Stats;
                U.Harmony.Patch(A.Method(typeof(GlobalEventManager), "OnCharacterDeath"), prefix: new HarmonyMethod(typeof(HappiestMask), nameof(Credit)) { priority = Priority.First });
            }

            c.Text(() => c.B("Rework") ? "Killing an enemy summons an <style=cIsUtility>invulnerable ghost of yourself</style> that deals " + c.V("Ghost Damage (%)", "%") + " damage. You can have a maximum of " + c.V("Maximum Ghosts", "", "cIsUtility") + " ghosts." + (c.F("Ghost Duration") < 0 ? " The Ghosts last for the entire stage." : " Ghosts last " + RareItemSupport.Utility(Tweaks.ModCompatibility.LookingGlassStatRegistry.Number(c.F("Ghost Duration")) + "s") + ".") : "Killing enemies has a chance to summon a temporary ghost of the defeated enemy.", () => c.B("Rework") ? "Kills summon an invulnerable ghost of yourself." : "Kills can summon ghosts of defeated enemies.");
        }

        static bool Eligible(ItemIndex i)
        {
            var d = ItemCatalog.GetItemDef(i);
            return d && i != c.Item.itemIndex && !new[]
            {
                "GhostOnKill",
                "MinorConstructOnKill",
                "Squid",
                "RoboBallBuddy",
                "LemurianHarness",
                "ExtraLife",
                "ExtraLifeVoid",
                "Ghost",
                "HealthDecay",
                "GummyCloneIdentifier"
            }.Contains(d.name);
        }

        static void Kill(DamageReport r)
        {
            var b = r.attackerBody;
            int n = c.Count(b);
            if (n == 0 || !c.B("Rework") || !b.master || ghosts.TryGetValue(b.master, out var ignored) || !U.Roll(c.Total("Summon Chance (%)", n), b))
                return;
            var group = groups.GetOrCreateValue(b.master);
            group.Ghosts.RemoveAll(g => !g.Master || !g.Master.GetBody());
            int max = Mathf.Max(0, Mathf.FloorToInt(c.Total("Maximum Ghosts", n)));
            if (group.Ghosts.Count >= max)
                return;
            var ai = MasterCatalog.allAiMasters.FirstOrDefault(m => m && m.bodyPrefab && m.bodyPrefab.GetComponent<CharacterBody>() && m.bodyPrefab.GetComponent<CharacterBody>().bodyIndex == b.bodyIndex);
            if (!ai)
            {
                if (!warned)
                {
                    warned = true;
                    ThreeQsVanillaTweaks.Log.Warning("Happiest Mask: no AI master exists for " + b.name + "; cannot create a self ghost for this survivor.");
                }

                return;
            }

            var summon = new MasterSummon
            {
                masterPrefab = ai.gameObject,
                position = b.footPosition + Vector3.right * 2,
                rotation = b.transform.rotation,
                summonerBodyObject = b.gameObject,
                teamIndexOverride = b.teamComponent.teamIndex,
                ignoreTeamMemberLimit = true,
                loadout = b.master.loadout,
                inventoryToCopy = c.B("Inherits Items") ? b.inventory : null,
                inventoryItemCopyFilter = Eligible
            };
            summon.preSpawnSetupCallback = m =>
            {
                m.inventory.GiveItemPermanent(RoR2Content.Items.Ghost);
            };
            var master = summon.Perform();
            if (!master)
                return;
            var body = master.GetBody();
            if (!body)
            {
                UnityEngine.Object.Destroy(master.gameObject);
                return;
            }

            float duration = c.F("Ghost Duration");
            if (duration >= 0 && c.B("Duration Scales With Stacks"))
                duration += (n - 1) * c.F("Extra Ghost Duration Per Stack");
            var ghost = new Ghost
            {
                Master = master,
                Owner = b.master,
                Expiry = duration < 0 ? float.PositiveInfinity : Time.fixedTime + duration
            };
            ghosts.Add(master, ghost);
            group.Ghosts.Add(ghost);
            body.healthComponent.godMode = true;
            body.AddBuff(RoR2Content.Buffs.HiddenInvincibility);
            body.RecalculateStats();
        }

        static void Stats(CharacterBody b)
        {
            if (!c.Active || !b.master || !ghosts.TryGetValue(b.master, out var g) || !g.Owner)
                return;
            int n = g.Owner.inventory ? g.Owner.inventory.GetItemCountEffective(c.Item) : 0;
            if (n > 0)
                U.Scale(b, "damage", c.Total("Ghost Damage (%)", n) - 100f);
        }

        static void Tick(CharacterBody b)
        {
            if (!b.master)
                return;
            if (ghosts.TryGetValue(b.master, out var g))
            {
                if (!c.Active || !c.B("Rework") || !g.Owner || !g.Owner.inventory || g.Owner.inventory.GetItemCountEffective(c.Item) == 0 || Time.fixedTime >= g.Expiry)
                {
                    ghosts.Remove(b.master);
                    UnityEngine.Object.Destroy(b.master.gameObject);
                    UnityEngine.Object.Destroy(b.gameObject);
                }

                return;
            }

            if (groups.TryGetValue(b.master, out var group))
            {
                int n = c.Count(b);
                int max = n > 0 ? Mathf.Max(0, Mathf.FloorToInt(c.Total("Maximum Ghosts", n))) : 0;
                group.Ghosts.RemoveAll(x => !x.Master || !x.Master.GetBody());
                for (int i = group.Ghosts.Count - 1; i >= max; i--)
                {
                    var x = group.Ghosts[i];
                    if (x.Master)
                    {
                        var body = x.Master.GetBody();
                        ghosts.Remove(x.Master);
                        UnityEngine.Object.Destroy(x.Master.gameObject);
                        if (body)
                            UnityEngine.Object.Destroy(body.gameObject);
                    }

                    group.Ghosts.RemoveAt(i);
                }
            }
        }

        static void Credit(ref DamageReport damageReport)
        {
            if (!c.Active || !c.B("Rework") || !c.B("Kills Attributed To Owner") || !damageReport.attackerMaster || !ghosts.TryGetValue(damageReport.attackerMaster, out var g) || !g.Owner)
                return;
            var owner = g.Owner.GetBody();
            if (!owner)
                return;
            var info = (DamageInfo)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(damageReport.damageInfo, null);
            info.attacker = owner.gameObject;
            damageReport = new DamageReport(info, damageReport.victim, damageReport.damageDealt, damageReport.combinedHealthBeforeDamage);
        }
    }
}
