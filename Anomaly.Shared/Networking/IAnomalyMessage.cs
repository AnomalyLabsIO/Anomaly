using System.IO;

namespace Anomaly.Shared.Networking
{
    public interface IAnomalyMessage
    {
        /// <summary>
        /// Namespaced message name (e.g., "anomaly:blendshapes").
        /// Used for registration and hashed for the wire format.
        /// </summary>
        string MessageName { get; }

        /// <summary>
        /// Mirror transport channel to use when sending.
        /// 0 = ReliableOrdered, 1 = Unreliable, 2 = Sequenced, 3 = ReliableSequenced, 4 = ReliableUnordered.
        /// </summary>
        MessageChannel TransportChannel { get; }

        void Serialize(BinaryWriter writer);

        void Deserialize(BinaryReader reader);
    }
}
