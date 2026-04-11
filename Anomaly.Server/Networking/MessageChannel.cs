namespace Anomaly.Server.Networking;

public enum MessageChannel
{
    ReliableOrdered = 0, // Most game messages
    Unreliable = 1, // Voice/Audio
    Sequenced = 2,
    ReliableSequenced = 3,
    ReliableUnordered = 4 // Shutdown/restart messages
}