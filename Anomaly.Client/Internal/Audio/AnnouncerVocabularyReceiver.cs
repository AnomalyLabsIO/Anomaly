using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppCassie;
using MelonLoader;
using AnnouncerApi = Anomaly.Client.Api.Features.Announcer;

namespace Anomaly.Client.Internal.Audio;

internal static class AnnouncerVocabularyReceiver
{
    private static readonly Dictionary<string, CassieLine> _serverLines = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized;

    public static void Init()
    {
        if (_initialized)
            return;

        _initialized = true;
        AnomalyMessaging.MessageReceived += OnMessage;
    }

    public static void Clear()
    {
        if (_serverLines.Count == 0)
            return;

        AnnouncerApi.RemoveWords(_serverLines.Values);
        _serverLines.Clear();
    }

    private static void OnMessage(IAnomalyMessage msg)
    {
        if (msg is AnnouncerVocabularyMessage vocabulary)
            HandleVocabulary(vocabulary);
    }

    private static void HandleVocabulary(AnnouncerVocabularyMessage vocabulary)
    {
        int applied = 0;
        foreach (var word in vocabulary.Words)
        {
            if (!TryApply(word))
                continue;

            applied++;
        }

        if (applied > 0)
            MelonLogger.Msg($"Announcer: Registered {applied} server custom word(s).");
    }

    private static bool TryApply(AnnouncerVocabularyMessage.Entry word)
    {
        if (string.IsNullOrWhiteSpace(word.ApiName) || word.AudioHash == null || word.AudioHash.Length != 32)
            return false;
        if (word.DurationSeconds <= 0f ||
            word.DurationSeconds > AnnouncerVocabularyMessage.MaxDurationSeconds ||
            float.IsNaN(word.DurationSeconds) || float.IsInfinity(word.DurationSeconds))
            return false;
        if (!System.Enum.IsDefined(typeof(CassieClipCategory), (CassieClipCategory)word.Type))
            return false;

        if (!AnomalyResources.TryGetAudioClip(word.AudioHash, out var clip))
        {
            MelonLogger.Warning($"Announcer: Audio for server word '{word.ApiName}' is not cached or failed to decode.");
            return false;
        }
        if (clip.length <= 0f || clip.length > AnnouncerVocabularyMessage.MaxDurationSeconds ||
            Math.Abs(clip.length - word.DurationSeconds) > Math.Max(0.5f, clip.length * 0.1f))
        {
            MelonLogger.Warning(
                $"Announcer: Rejected server word '{word.ApiName}' because its declared duration " +
                "did not match the bounded decoded clip.");
            return false;
        }

        if (!_serverLines.ContainsKey(word.ApiName) &&
            _serverLines.Count >= AnnouncerVocabularyMessage.MaxWords)
        {
            MelonLogger.Warning(
                $"Announcer: Rejected '{word.ApiName}' because the server vocabulary reached " +
                $"{AnnouncerVocabularyMessage.MaxWords} entries.");
            return false;
        }

        if (_serverLines.TryGetValue(word.ApiName, out CassieLine existing))
        {
            AnnouncerApi.RemoveWords(new[] { existing });
            _serverLines.Remove(word.ApiName);
        }

        if (!AnnouncerApi.AddServerWord(word.ApiName, clip, word.DurationSeconds, (CassieClipCategory)word.Type, out CassieLine line))
        {
            MelonLogger.Warning($"Announcer: Failed to register server word '{word.ApiName}'.");
            return false;
        }

        _serverLines[line.ApiName] = line;
        return true;
    }
}
