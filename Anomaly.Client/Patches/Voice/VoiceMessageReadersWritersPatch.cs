namespace Anomaly.Client.Patches.Voice;

// Intentionally empty. The Sending dispatch previously lived here (as a
// prefix on VoiceMessageReadersWriters.SerializeVoiceMessage), but that
// location runs during Mirror serialization where cancellation would corrupt
// the NetworkWriter. Sending now fires from VoiceTransceiverPatch's prefix
// on ClientSendData, which is a safe cancellation point.
