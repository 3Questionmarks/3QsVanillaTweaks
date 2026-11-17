using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace Tweaks.Bazaar
{
    internal static partial class BazaarAlternateMode
    {
        private const short StateMessageId = 188;
        private const short CrystalBreakMessageId = 191;
        private static NetworkClient registeredClient;
        private static readonly Dictionary<NetworkInstanceId, InteractionState> clientStates =
            new Dictionary<NetworkInstanceId, InteractionState>();
        private static readonly Dictionary<GameObject, bool> movedObjects = new Dictionary<GameObject, bool>();
        private static readonly List<NetworkInstanceId> appliedPoses = new List<NetworkInstanceId>();
        private static readonly Dictionary<NetworkInstanceId, InteractionState> pendingPoses =
            new Dictionary<NetworkInstanceId, InteractionState>();

        internal static void PublishSceneObject(GameObject target, bool enabled)
        {
            movedObjects[target] = enabled;
            var state = SceneState(target, enabled);
            if (!state.Id.IsEmpty()) NetworkServer.SendToAll(StateMessageId, state);
        }

        private static InteractionState SceneState(GameObject target, bool enabled) => new InteractionState
        {
            Id = FindNetId(target.transform),
            IsSceneObject = true,
            Enabled = enabled,
            Pose = new Pose(target.transform.position, target.transform.rotation)
        };

        private static NetworkInstanceId FindNetId(Transform node)
        {
            for (; node; node = node.parent)
            {
                var identity = node.GetComponent<NetworkIdentity>();
                if (identity && !identity.netId.IsEmpty()) return identity.netId;
            }
            return default;
        }

        private static IEnumerable<InteractionState> GateStates(Gate gate)
        {
            var allowTarget = new List<NetworkInstanceId>();
            var allowCrystal = new List<NetworkInstanceId>();
            foreach (var user in NetworkUser.readOnlyInstancesList)
            {
                if (!user) continue;
                if (IsUnlocked(gate.Slot, user)) allowTarget.Add(user.netId);
                if (CanBuy(gate, user)) allowCrystal.Add(user.netId);
            }
            var targetId = FindNetId(gate.Target.transform);
            if (!targetId.IsEmpty())
                yield return new InteractionState { Id = targetId, CrystalId = gate.Crystal.netId, Allowed = allowTarget };
            yield return new InteractionState { Id = gate.Crystal.netId, Allowed = allowCrystal, CrystalSize = gate.CrystalSize };
        }

        private static void PublishGate(Gate gate)
        {
            foreach (var state in GateStates(gate))
                if (!state.Id.IsEmpty()) NetworkServer.SendToAll(StateMessageId, state);
        }

        private static void PublishCrystalBreak(Gate gate, List<NetworkUser> viewers)
        {
            var local = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;
            var message = new CrystalBreakMessage { Id = gate.Crystal.netId };
            foreach (var user in viewers)
            {
                if (!user || CanBuy(gate, user)) continue;
                if (user == local) gate.Crystal.GetComponent<CrystalVisual>().PlayBreak();
                else if (!message.Id.IsEmpty()) user.connectionToClient?.Send(CrystalBreakMessageId, message);
            }
        }

        private static void SendToLoadedPlayer(NetworkUser user)
        {
            if (!NetworkServer.active || !BazaarSpawnHelper.InBazaar() || !user || user.connectionToClient == null) return;
            // Recompute permissions after a join or reconnect so the new network ID receives its own access.
            foreach (var gate in targets.Values)
                if (gate.Target && gate.Crystal) PublishGate(gate);
            foreach (var pair in movedObjects)
            {
                if (!pair.Key) continue;
                var state = SceneState(pair.Key, pair.Value);
                if (!state.Id.IsEmpty()) user.connectionToClient.Send(StateMessageId, state);
            }
        }

        private static void UpdateNetwork()
        {
            var manager = NetworkManager.singleton;
            var client = manager ? manager.client : null;
            if (client != registeredClient)
            {
                registeredClient = client;
                client?.RegisterHandler(StateMessageId, ReceiveState);
                client?.RegisterHandler(CrystalBreakMessageId, ReceiveCrystalBreak);
                clientStates.Clear();
                pendingPoses.Clear();
            }
            if (!NetworkClient.active || NetworkServer.active || pendingPoses.Count == 0) return;
            foreach (var pair in pendingPoses)
                if (TryApplyPose(pair.Value)) appliedPoses.Add(pair.Key);
            foreach (var id in appliedPoses) pendingPoses.Remove(id);
            appliedPoses.Clear();
        }

        private static void ReceiveCrystalBreak(NetworkMessage message)
        {
            if (NetworkServer.active || !BazaarSpawnHelper.InBazaar()) return;
            var state = message.ReadMessage<CrystalBreakMessage>();
            var crystal = ClientScene.FindLocalObject(state.Id);
            if (crystal) crystal.GetComponent<CrystalVisual>()?.PlayBreak();
        }

        private sealed class CrystalBreakMessage : MessageBase
        {
            internal NetworkInstanceId Id;

            public override void Serialize(NetworkWriter writer) => writer.Write(Id);

            public override void Deserialize(NetworkReader reader) => Id = reader.ReadNetworkId();
        }

        private static void ReceiveState(NetworkMessage message)
        {
            if (NetworkServer.active) return;
            var state = message.ReadMessage<InteractionState>();
            if (state.Id.IsEmpty()) return;
            if (!state.IsSceneObject)
            {
                clientStates[state.Id] = state;
                RefreshSeers();
            }
            else if (!TryApplyPose(state)) pendingPoses[state.Id] = state;
            else pendingPoses.Remove(state.Id);
        }

        private static bool TryApplyPose(InteractionState state)
        {
            var target = ClientScene.FindLocalObject(state.Id);
            if (!target) return false;
            target.transform.SetPositionAndRotation(state.Pose.position, state.Pose.rotation);
            if (target.activeSelf != state.Enabled) target.SetActive(state.Enabled);
            Physics.SyncTransforms();
            return true;
        }

        private static bool TryGetState(Component interaction, out InteractionState state) =>
            clientStates.TryGetValue(FindNetId(interaction.transform), out state);

        private sealed class InteractionState : MessageBase
        {
            internal NetworkInstanceId Id, CrystalId;
            internal bool IsSceneObject, Enabled;
            internal Pose Pose;
            internal Vector3 CrystalSize;
            internal List<NetworkInstanceId> Allowed;

            internal bool Allows(NetworkUser user) => user && Allowed.Contains(user.netId);

            public override void Serialize(NetworkWriter writer)
            {
                writer.Write(Id);
                writer.Write(IsSceneObject);
                if (IsSceneObject)
                {
                    writer.Write(Enabled);
                    writer.Write(Pose.position);
                    writer.Write(Pose.rotation);
                    return;
                }
                writer.Write(CrystalSize);
                writer.Write(CrystalId);
                writer.Write((ushort)Allowed.Count);
                foreach (var user in Allowed) writer.Write(user);
            }

            public override void Deserialize(NetworkReader reader)
            {
                Id = reader.ReadNetworkId();
                IsSceneObject = reader.ReadBoolean();
                if (IsSceneObject)
                {
                    Enabled = reader.ReadBoolean();
                    Pose = new Pose(reader.ReadVector3(), reader.ReadQuaternion());
                    return;
                }
                CrystalSize = reader.ReadVector3();
                CrystalId = reader.ReadNetworkId();
                int count = reader.ReadUInt16();
                Allowed = new List<NetworkInstanceId>(count);
                for (int i = 0; i < count; i++) Allowed.Add(reader.ReadNetworkId());
            }
        }
    }
}
