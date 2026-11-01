using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;
using R = Tweaks.Items.Uncommon.UncommonConfig;
using U = Tweaks.Items.Uncommon.UncommonItemSupport;
namespace Tweaks.Items.Void
{
    internal static class VoidsentFlame
    {
        static R c; static GameObject prefab;
        sealed class Target { internal readonly HashSet<CharacterMaster> Seen = new HashSet<CharacterMaster>(); internal readonly Dictionary<CharacterMaster,float> Last = new Dictionary<CharacterMaster,float>(); }
        static readonly ConditionalWeakTable<HealthComponent,Target> targets = new ConditionalWeakTable<HealthComponent,Target>();
        internal static void Init()
        {
            U.Ensure(); c = new R("Voidsent Flame", "ExplodeOnDeathVoid", true, "Void");
            c.Pair("Damage (%)", 320, 160, "Explosion base damage. Vanilla: 260% +156% per stack.");
            c.Pair("Proc Coefficient", 0, 0, "Explosion proc coefficient. Vanilla: 1.");
            c.Pair("Radius", 12, 2.4f, "Explosion radius in metres. Vanilla: 12 +2.4 per stack.");
            c.Pair("Trigger Chance (%)", 100, 0, "Chance on a qualifying hit. Vanilla: guaranteed on a qualifying full-health hit.");
            c.Pair("Health Threshold (%)", 90, 0, "Target combined-health threshold before the hit. 100% requires full health and shield, matching vanilla. Ignored in first-hit mode.");
            c.B("Activate On First Hit", true, "Trigger on the holder's first damaging hit against each target, ignoring its health threshold. Only hits with nonzero proc coefficient qualify.");
            c.N("Explosion Delay", .2f, "Seconds before the explosion. Vanilla: 0.2.");
            c.N("Target Trigger Cooldown", .25f, "Minimum seconds between activations against the same target by the same holder in health-threshold mode. Vanilla: 0.25 seconds, shared by holders.");
            c.Suppress("RoR2.HealthComponent", "TakeDamageProcess");
            if (c.Enabled.Value) { U.BeforeDamage += Damage; RoR2Application.onLoad += () => prefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/ExplodeOnDeathVoidExplosion"); }
            c.ShowStat = key => !c.B("Activate On First Hit") || !key.StartsWith("Health Threshold");
            c.Text(() => "Hitting an enemy " + (c.B("Activate On First Hit") ? "for the first time" : c.F("Health Threshold (%)") == 100 && c.F("Health Threshold (%) Per Stack") == 0 ? "at <style=cIsHealing>full health</style>" : "above " + c.V("Health Threshold (%)", "%", "cIsHealing") + " health") + " triggers a <style=cIsDamage>lava pillar</style> in a " + c.V("Radius", "m", "cIsUtility") + " radius for " + c.V("Damage (%)", "%") + " base damage. <style=cIsVoid>Corrupts all Will-o'-the-wisps</style>.", () => (c.B("Activate On First Hit") ? "Your first hit against each enemy" : "Hits against healthy enemies") + " triggers a lava pillar. <style=cIsVoid>Corrupts all Will-o'-the-wisps</style>.");
        }
        internal static bool AboveThreshold(float current, float maximum, float threshold) => threshold >= 100 ? current >= maximum : current > maximum * threshold / 100;
        static void Damage(HealthComponent victim, DamageInfo info)
        {
            var attacker = info.attacker ? info.attacker.GetComponent<CharacterBody>() : null; int n = c.Count(attacker);
            if (n == 0 || !attacker.master || !victim.alive || info.damage <= 0 || info.procCoefficient <= 0 || info.procChainMask.HasProc(ProcType.Behemoth) || !prefab) return;
            var state = targets.GetOrCreateValue(victim);
            if (c.B("Activate On First Hit")) { if (!state.Seen.Add(attacker.master)) return; }
            else
            {
                float threshold = c.Total("Health Threshold (%)", n);
                if (!AboveThreshold(victim.combinedHealth, victim.fullCombinedHealth, threshold)) return;
                if (state.Last.TryGetValue(attacker.master, out float last) && Time.fixedTime - last < c.F("Target Trigger Cooldown")) return;
            }
            if (!U.Roll(c.Total("Trigger Chance (%)", n), attacker)) return;
            state.Last[attacker.master] = Time.fixedTime;
            Vector3 position = victim.body ? victim.body.corePosition : info.position;
            var instance = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
            var blast = instance.GetComponent<DelayBlast>();
            if (!blast) { UnityEngine.Object.Destroy(instance); return; }
            blast.position = position; blast.baseDamage = attacker.damage * c.Total("Damage (%)", n) / 100;
            blast.baseForce = 1000; blast.radius = c.Total("Radius", n); blast.attacker = attacker.gameObject; blast.inflictor = instance;
            blast.crit = attacker.RollCrit(); blast.maxTimer = c.F("Explosion Delay"); blast.timerStagger = 0; blast.procCoefficient = c.Total("Proc Coefficient", n);
            var chain = info.procChainMask; chain.AddProc(ProcType.Behemoth); blast.procChainMask = chain;
            blast.damageColorIndex = DamageColorIndex.Void; blast.falloffModel = BlastAttack.FalloffModel.SweetSpot; blast.attackerFiltering = AttackerFiltering.NeverHitSelf;
            var filter = instance.GetComponent<TeamFilter>(); if (filter) filter.teamIndex = attacker.teamComponent.teamIndex;
            NetworkServer.Spawn(instance);
        }
    }
}
