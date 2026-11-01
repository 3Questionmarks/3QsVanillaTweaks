using System;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

namespace Tweaks.Interactables
{
    internal static class ShrineEncounterRewards
    {
        static GameObject rewardEffect;
        internal static void ConfigureGold(CombatDirector director, Func<float> percent)
        {
            if (!NetworkServer.active || !director) return;
            var rewards = director.GetComponent<GoldRewards>();
            if (!rewards) rewards = director.gameObject.AddComponent<GoldRewards>();
            rewards.Configure(director, percent);
        }

        internal sealed class GoldRewards : MonoBehaviour
        {
            CombatDirector director;
            Func<float> percent;
            internal void Configure(CombatDirector value, Func<float> multiplier)
            {
                percent = multiplier;
                if (director) return;
                director = value;
                if (director.onSpawnedServer == null) director.onSpawnedServer = new CombatDirector.OnSpawnedServer();
                director.onSpawnedServer.AddListener(Spawned);
            }
            void Spawned(GameObject masterObject)
            {
                if (!NetworkServer.active || !masterObject) return;
                var master = masterObject.GetComponent<CharacterMaster>();
                var body = master ? master.GetBody() : masterObject.GetComponent<CharacterBody>();
                var reward = body ? body.GetComponent<DeathRewards>() : null;
                if (reward)
                    reward.goldReward = (uint)Math.Min(uint.MaxValue, Math.Floor(reward.goldReward * (double)Mathf.Max(0f, percent()) / 100d));
            }
            void OnDestroy()
            {
                if (director && director.onSpawnedServer != null) director.onSpawnedServer.RemoveListener(Spawned);
            }
        }

        internal static Bounds BoundsFor(GameObject shrine)
        {
            var bounds = new Bounds(shrine.transform.position + Vector3.up, new Vector3(2f, 2f, 2f));
            foreach (var collider in shrine.GetComponentsInChildren<Collider>())
                if (collider.enabled && !collider.isTrigger) bounds.Encapsulate(collider.bounds);
            return bounds;
        }

        internal static void Launch(UniquePickup pickup, Bounds bounds, int index, int count, float angle)
        {
            Vector3 direction = Quaternion.AngleAxis(angle + index * 360f / Mathf.Max(1, count), Vector3.up) * Vector3.forward;
            float clearance = new Vector2(bounds.extents.x, bounds.extents.z).magnitude + 1f;
            Vector3 origin = bounds.center + direction * clearance;
            origin.y = bounds.max.y + 1f;
            Vector3 velocity = direction * 15f + Vector3.up * 12f;
            PickupDropletController.CreatePickupDroplet(new GenericPickupController.CreatePickupInfo
                { pickup = pickup, position = origin, rotation = Quaternion.identity }, origin, velocity);
        }

        internal static void PlayRewardEffect(Vector3 position)
        {
            if (!rewardEffect)
            {
                var prefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/ShrineChance/ShrineChance.prefab").WaitForCompletion();
                var shrine = prefab ? prefab.GetComponent<ShrineChanceBehavior>() : null;
                if (shrine) rewardEffect = shrine.effectPrefabShrineRewardNormal;
            }
            if (rewardEffect) EffectManager.SpawnEffect(rewardEffect,
                new EffectData { origin = position, rotation = Quaternion.identity, scale = 1f, color = Color.white }, true);
        }
    }
}
