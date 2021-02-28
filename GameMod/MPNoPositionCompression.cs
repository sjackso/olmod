using System;
using System.Linq;
using Harmony;
using Overload;
using UnityEngine;
using UnityEngine.Networking;

namespace GameMod {
    /// <summary>
    /// This mod disables position compression when the server supports it.
    /// </summary>
    public class MPNoPositionCompression {
        /// <summary>
        /// Determines whether no position compression is enabled for the current game.
        /// </summary>
        static public bool enabled = true;
        static public NewPlayerSnapshotToClientMessage m_new_snapshot_buffer = new NewPlayerSnapshotToClientMessage();
    }

    public class NewPlayerSnapshot
    {
        public NewPlayerSnapshot()
        {
        }

        public NewPlayerSnapshot(NetworkInstanceId net_id, 
                              Vector3 pos, Quaternion rot,
                              Vector3 vel, Vector3 vrot)
        {
            this.m_net_id = net_id;
            this.m_pos = pos;
            this.m_rot = rot;
            this.m_vel = vel;
            this.m_vrot = vrot;
        }

        public NetworkInstanceId m_net_id;

        public Vector3 m_pos;

        public Quaternion m_rot;

        public Vector3 m_vel;

        public Vector3 m_vrot;

        internal PlayerSnapshot ToOldSnapshot() {
            return new PlayerSnapshot() {
                m_net_id = this.m_net_id,
                m_pos = this.m_pos,
                m_rot = this.m_rot
            };
        }
    }

    /// <summary>
    /// A class that handles the new way to serialize and deserialize player snapshots without compression.
    /// </summary>
    public class NewPlayerSnapshotToClientMessage : MessageBase {
        /// <summary>
        /// Serialize the snapshot without compression.
        /// </summary>
        /// <param name="writer"></param>
        public override void Serialize(NetworkWriter writer) {
            writer.Write(NetworkMatch.m_match_elapsed_seconds);
            writer.Write((byte)m_num_snapshots);
            for (int i = 0; i < m_num_snapshots; i++) {
                writer.Write(m_snapshots[i].m_net_id);
                writer.Write(m_snapshots[i].m_pos.x);
                writer.Write(m_snapshots[i].m_pos.y);
                writer.Write(m_snapshots[i].m_pos.z);
                writer.Write(m_snapshots[i].m_rot);
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.x));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.y));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vel.z));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.x));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.y));
                writer.Write(HalfHelper.Compress(m_snapshots[i].m_vrot.z));
            }
        }

        /// <summary>
        /// Deserialize the snapshot without compression.
        /// </summary>
        /// <param name="reader"></param>
        public override void Deserialize(NetworkReader reader) {
            m_timestamp = reader.ReadSingle();
            m_num_snapshots = (int)reader.ReadByte();
            for (int i = 0; i < m_num_snapshots; i++) {
                NetworkInstanceId net_id = reader.ReadNetworkId();
                Vector3 pos = default(Vector3);
                pos.x = reader.ReadSingle();
                pos.y = reader.ReadSingle();
                pos.z = reader.ReadSingle();
                Quaternion rot = reader.ReadQuaternion();
                Vector3 vel = default(Vector3);
                vel.x = HalfHelper.Decompress(reader.ReadUInt16());
                vel.y = HalfHelper.Decompress(reader.ReadUInt16());
                vel.z = HalfHelper.Decompress(reader.ReadUInt16());
                Vector3 vrot = default(Vector3);
                vrot.x = HalfHelper.Decompress(reader.ReadUInt16());
                vrot.y = HalfHelper.Decompress(reader.ReadUInt16());
                vrot.z = HalfHelper.Decompress(reader.ReadUInt16());
                m_snapshots[i] = new NewPlayerSnapshot(net_id, pos, rot,
                                                       vel, vrot);
            }
        }

        public float m_timestamp;
        public int m_num_snapshots;
        public NewPlayerSnapshot[] m_snapshots = Enumerable.Range(1, 16).Select(x => new NewPlayerSnapshot()).ToArray();

        internal PlayerSnapshotToClientMessage ToOldSnapshotMessage() {
            return new PlayerSnapshotToClientMessage() {
                m_num_snapshots = this.m_num_snapshots,
                m_snapshots = this.m_snapshots.Select(x => x.ToOldSnapshot()).ToArray()
            };
        }

        /// <summary>
        /// Create a new player snapshot message from an old player snapshot message.
        /// </summary>
        /// <param name="v"></param>
        /*public static explicit operator NewPlayerSnapshotToClientMessage(PlayerSnapshotToClientMessage v) {
            return new NewPlayerSnapshotToClientMessage {
                m_num_snapshots = v.m_num_snapshots,
                //m_snapshots = v.m_snapshots
            };
        }

        /// <summary>
        /// Create an old player snapshot message from a new player snapshot message.
        /// </summary>
        /// <returns></returns>
        public PlayerSnapshotToClientMessage ToPlayerSnapshotToClientMessage() {
            return new PlayerSnapshotToClientMessage {
                m_num_snapshots = m_num_snapshots,
                //m_snapshots = m_snapshots
            };
        }*/
    }

    /// <summary>
    /// Handle the new player snapshot message.
    /// </summary>
    [HarmonyPatch(typeof(Client), "RegisterHandlers")]
    public class MPNoPositionCompression_RegisterHandlers {

        // when using the new protocol, this object replaces
        // Client.m_PendingPlayerSnapshotMessages
        //public static Queue<NewPlayerSnapshotToClientMessage> m_PendingPlayerSnapshotMessages = new Queue<NewPlayerSnapshotToClientMessage>();

        public static void OnNewPlayerSnapshotToClient(NetworkMessage msg) {
            if (NetworkMatch.GetMatchState() == MatchState.PREGAME || NetworkMatch.InGameplay()) {
                NewPlayerSnapshotToClientMessage item = msg.ReadMessage<NewPlayerSnapshotToClientMessage>();
                MPClientShipReckoning.AddNewPlayerSnapshot(item);
            }
        }

        private static void Postfix() {
            if (Client.GetClient() == null) {
                return;
            }

            Client.GetClient().RegisterHandler(MessageTypes.MsgNewPlayerSnapshotToClient,
                                               OnNewPlayerSnapshotToClient);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    [HarmonyPatch(typeof(Server), "SendSnapshotsToPlayer")]
    public class MPNoPositionCompression_SendSnapshotsToPlayer{

        public static NewPlayerSnapshotToClientMessage m_snapshot_buffer = new NewPlayerSnapshotToClientMessage();
        public static bool Prefix(Player send_to_player){
            m_snapshot_buffer.m_num_snapshots = 0;
            foreach (Player player in Overload.NetworkManager.m_Players)
            {
                if (!(player == null) && !player.m_spectator && !(player == send_to_player))
                {
                    NewPlayerSnapshot playerSnapshot = m_snapshot_buffer.m_snapshots[m_snapshot_buffer.m_num_snapshots++];
                    playerSnapshot.m_net_id = player.netId;
                    playerSnapshot.m_pos = player.transform.position;
                    playerSnapshot.m_rot = player.transform.rotation;
                    playerSnapshot.m_vel = player.c_player_ship.c_rigidbody.velocity;
                    playerSnapshot.m_vrot = player.c_player_ship.c_rigidbody.angularVelocity;
                }
            }
            if (m_snapshot_buffer.m_num_snapshots > 0)
            {
                if (!MPNoPositionCompression.enabled || !MPTweaks.ClientHasTweak(send_to_player.connectionToClient.connectionId, "nocompress")) {
                    send_to_player.connectionToClient.SendByChannel(64, m_snapshot_buffer.ToOldSnapshotMessage(), 1);
                    return false;
                }

                send_to_player.connectionToClient.SendByChannel(MessageTypes.MsgNewPlayerSnapshotToClient, m_snapshot_buffer, 1);
            }
            return false;
        }

    }
}
