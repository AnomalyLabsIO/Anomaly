using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Discrete round-lifecycle phases broadcast from server to client. Mirrors
    /// the set of LabAPI round events available on the server so Anomaly plugins
    /// can hook the same moments from the client.
    /// </summary>
    public enum RoundPhase : byte
    {
        /// <summary>Initial state before any clients have connected.</summary>
        WaitingForPlayers = 0,

        /// <summary>Round is about to start (pre-start setup window).</summary>
        Starting = 1,

        /// <summary>Round is in progress.</summary>
        InProgress = 2,

        /// <summary>Round end conditions have been met; ending sequence is running.</summary>
        Ending = 3,

        /// <summary>Round has ended and is being torn down.</summary>
        Ended = 4,

        /// <summary>Server has finished restarting into a fresh round lobby.</summary>
        Restarted = 5,
    }

    /// <summary>
    /// Server → client. Broadcasts the current <see cref="RoundPhase"/>. Sent by
    /// <c>RoundStateBroadcaster</c> on every LabAPI round event.
    /// </summary>
    public class RoundStateMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:round:state";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public RoundPhase Phase { get; set; }

        public void Serialize(BinaryWriter writer) => writer.Write((byte)Phase);
        public void Deserialize(BinaryReader reader) => Phase = (RoundPhase)reader.ReadByte();
    }
}
