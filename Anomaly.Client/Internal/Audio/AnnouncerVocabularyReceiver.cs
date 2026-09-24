using System;
using System.Collections.Generic;
using Anomaly.Client.Api.Assets;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Il2CppCassie;
using MelonLoader;
using AnnouncerApi = Anomaly.Client.Api.Features.Announcer;

namespace Anomaly.Client.Internal.Audio;

internal static class AnnouncerVocabularyReceiver
{
    private static readonly Dictionary<string, CassieLine> _serverLines = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TransactionalInitializer Initializer = new();

    public static void Init()
    {
        Initializer.Initialize(new InitializationStep(
            () => AnomalyMessaging.MessageReceived += OnMessage,
            () => AnomalyMessaging.MessageReceived -= OnMessage));
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
        if (!ContentCatalogClient.IsActivated)
        {
            AnomalyMessaging.FailClosedTransport("Announcer vocabulary arrived before content activation.");
            return;
        }
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
        if (!AnnouncerVocabularyMessage.IsValidNewApiName(word.ApiName) || !AssetAddress.TryParse(word.AudioAddress, out var address) ||
            !AssetBundleManager.IsRemoteAsset(address, AssetKind.AudioClip))
            return false;
        if (word.DurationSeconds <= 0f ||
            word.DurationSeconds > AnnouncerVocabularyMessage.MaxDurationSeconds ||
            float.IsNaN(word.DurationSeconds) || float.IsInfinity(word.DurationSeconds))
            return false;
        if (!System.Enum.IsDefined(typeof(CassieClipCategory), (CassieClipCategory)word.Type))
            return false;

        if (!AnomalyResources.TryGetAudioClip(address, out var clip))
        {
            MelonLogger.Warning($"Announcer: Audio for server word '{word.ApiName}' is not in an active pack.");
            return false;
        }
        if (clip.samples <= 0 || clip.frequency <= 0 || (double)clip.samples / clip.frequency > AnnouncerVocabularyMessage.MaxDurationSeconds ||
            Math.Abs((double)clip.samples / clip.frequency - word.DurationSeconds) > 1d / clip.frequency)
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
