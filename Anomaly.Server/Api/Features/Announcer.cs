using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Anomaly.Server.Api.Assets;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Cassie;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Features;

/// <summary>
/// Server-authoritative extension point for adding CASSIE/Announcer vocabulary.
/// Registered words are replayed to clients after their content activation succeeds.
/// </summary>
public static class Announcer
{
    private static readonly Dictionary<string, Word> _words = new Dictionary<string, Word>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CassieLine> _appliedLines = new Dictionary<string, CassieLine>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<AudioClip> _ownedClips = new List<AudioClip>();
    private static readonly FieldInfo AllLinesField = typeof(CassieLineDatabase).GetField(
        "<AllLines>k__BackingField",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static bool _initialized;

    public static IReadOnlyCollection<Word> Words => Array.AsReadOnly(_words.Values.ToArray());

    internal static void Init()
    {
        if (_initialized)
            return;

        _initialized = true;
        ContentGrantGate.Activated += OnActivated;
        CassieTtsAnnouncer.OnSingletonSet += ApplyAll;
        ApplyAll();
    }

    internal static void Disable()
    {
        if (_initialized)
        {
            ContentGrantGate.Activated -= OnActivated;
            CassieTtsAnnouncer.OnSingletonSet -= ApplyAll;
            _initialized = false;
        }

        RemoveAppliedLines();
        _words.Clear();
        _appliedLines.Clear();

        foreach (AudioClip clip in _ownedClips)
        {
            if (clip != null)
                UnityEngine.Object.Destroy(clip);
        }
        _ownedClips.Clear();
    }

    /// <summary>Adds a new word using the indexed clip duration. Stock replacements use game/cassie addresses.</summary>
    public static Word AddWord(string apiName, AssetAddress audioAddress, CassieClipCategory type = CassieClipCategory.Word)
    {
        string normalizedName = NormalizeApiName(apiName);
        if (!AnnouncerVocabularyMessage.IsValidNewApiName(normalizedName))
            throw new ArgumentException("AddWord requires a valid non-stock API name.", nameof(apiName));
        if (audioAddress.IsGameReplacement || !ServerAssetManager.TryGetAsset(audioAddress, out var asset) || asset.Kind != AssetKind.AudioClip)
            throw new ArgumentException("Address must identify an indexed mods/ AudioClip.", nameof(audioAddress));
        if (!Enum.IsDefined(typeof(CassieClipCategory), type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (_words.ContainsKey(normalizedName)) throw new InvalidOperationException("Announcer word is already registered: " + normalizedName);
        if (_words.Count >= AnnouncerVocabularyMessage.MaxWords) throw new InvalidOperationException("Announcer vocabulary capacity exceeded.");
        var word = new Word
        {
            ApiName = normalizedName, AudioAddress = audioAddress, PackIndexHash = (byte[])asset.IndexHash.Clone(),
            DurationSeconds = (float)((double)asset.Entry.SampleFrames / asset.Entry.SampleRate), Type = type
        };
        _words.Add(normalizedName, word);
        TryApplyWord(word);
        if (_initialized) SendToApprovedClients(new[] { word });
        return word;
    }

    private static void OnActivated(uint netId)
    {
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            if (hub.netId == netId) { ReplayActivated(hub); break; }
    }
    private static void ReplayActivated(ReferenceHub hub)
    {
        if (_words.Count == 0)
            return;

        var approved = new List<Word>();
        foreach (Word word in _words.Values)
        {
            if (FileTransferSession.IsAssetApproved(hub, word.AudioAddress, AssetKind.AudioClip, word.PackIndexHash))
                approved.Add(word);
        }
        if (approved.Count == 0)
            return;

        Logger.Info($"Announcer: Replaying {approved.Count} approved custom word(s) to {hub.LoggedNameFromRefHub()}");
        AnomalyMessaging.TrySendToClient(hub, BuildMessage(approved));
    }

    private static void ApplyAll()
    {
        foreach (var word in _words.Values)
            TryApplyWord(word);
    }

    private static bool TryApplyWord(Word word)
    {
        try
        {
            if (!CassieTtsAnnouncer.TryGetDatabase(out CassieLineDatabase db) || db == null || db.AllLines == null)
                return false;

            CassieLine[] lines = db.AllLines;
            foreach (CassieLine line in lines)
            {
                if (line == null || !string.Equals(line.ApiName, word.ApiName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_appliedLines.TryGetValue(word.ApiName, out CassieLine applied) && ReferenceEquals(applied, line))
                    return true;

                Logger.Warn($"Announcer: Cannot register '{word.ApiName}' because the base game already has that word.");
                return false;
            }

            AudioClip placeholderClip = ResolvePlaceholderClip(lines, word);
            if (placeholderClip == null)
            {
                Logger.Warn($"Announcer: Cannot register '{word.ApiName}' because no placeholder AudioClip is available.");
                return false;
            }

            var newLine = new CassieLine
            {
                ApiName = word.ApiName,
                Clip = placeholderClip,
                Duration = word.DurationSeconds,
                Type = word.Type
            };

            var expanded = new CassieLine[lines.Length + 1];
            Array.Copy(lines, expanded, lines.Length);
            expanded[expanded.Length - 1] = newLine;

            if (!SetAllLines(db, expanded))
            {
                Logger.Warn("Announcer: Failed to update CassieLineDatabase.AllLines.");
                return false;
            }

            db.ValidateCache(true);
            _appliedLines[word.ApiName] = newLine;
            Logger.Info($"Announcer: Registered custom word '{word.ApiName}' ({word.DurationSeconds:F2}s).");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Announcer.TryApplyWord('{word?.ApiName}'): {ex}");
            return false;
        }
    }

    private static AudioClip ResolvePlaceholderClip(CassieLine[] lines, Word word)
    {
        CassieLine fallback = null;
        foreach (CassieLine line in lines)
        {
            if (line?.Clip == null)
                continue;

            if (fallback == null)
                fallback = line;

            if (string.Equals(line.ApiName, ".", StringComparison.Ordinal))
                return line.Clip;
        }

        if (fallback?.Clip != null)
            return fallback.Clip;

        int sampleRate = 48000;
        int samples = Math.Max(1, (int)(Math.Max(0.01f, word.DurationSeconds) * sampleRate));
        AudioClip ownedClip = AudioClip.Create(
            "AnomalyAnnouncer:" + word.ApiName,
            samples,
            1,
            sampleRate,
            false);
        _ownedClips.Add(ownedClip);
        return ownedClip;
    }

    private static void RemoveAppliedLines()
    {
        if (_appliedLines.Count == 0 ||
            !CassieTtsAnnouncer.TryGetDatabase(out CassieLineDatabase db) ||
            db == null ||
            db.AllLines == null)
        {
            return;
        }

        CassieLine[] lines = db.AllLines;
        int retainedCount = 0;
        foreach (CassieLine line in lines)
        {
            if (!IsAppliedLine(line))
                retainedCount++;
        }

        if (retainedCount == lines.Length)
            return;

        var retained = new CassieLine[retainedCount];
        int index = 0;
        foreach (CassieLine line in lines)
        {
            if (!IsAppliedLine(line))
                retained[index++] = line;
        }

        if (!SetAllLines(db, retained))
        {
            Logger.Warn("Announcer: Failed to remove custom words from CassieLineDatabase.AllLines.");
            return;
        }

        db.ValidateCache(true);
    }

    private static bool IsAppliedLine(CassieLine candidate)
    {
        foreach (CassieLine applied in _appliedLines.Values)
        {
            if (ReferenceEquals(candidate, applied))
                return true;
        }

        return false;
    }

    private static bool SetAllLines(CassieLineDatabase db, CassieLine[] lines)
    {
        var property = typeof(CassieLineDatabase).GetProperty(
            nameof(CassieLineDatabase.AllLines),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(true);
        if (setter != null)
        {
            setter.Invoke(db, new object[] { lines });
            return true;
        }

        if (AllLinesField == null)
            return false;

        AllLinesField.SetValue(db, lines);
        return true;
    }

    private static AnnouncerVocabularyMessage BuildMessage(IEnumerable<Word> words)
    {
        var message = new AnnouncerVocabularyMessage();
        foreach (var word in words)
        {
            message.Words.Add(new AnnouncerVocabularyMessage.Entry(
                word.ApiName,
                word.AudioAddress.Value,
                word.DurationSeconds,
                (byte)word.Type));
        }
        return message;
    }

    private static void SendToApprovedClients(IEnumerable<Word> words)
    {
        var candidates = new List<Word>(words);
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            try
            {
                var approved = new List<Word>();
                foreach (Word word in candidates)
                {
                    if (FileTransferSession.IsAssetApproved(hub, word.AudioAddress, AssetKind.AudioClip, word.PackIndexHash))
                        approved.Add(word);
                }
                if (approved.Count == 0)
                    continue;

                AnomalyMessaging.TrySendToClient(hub, BuildMessage(approved));
            }
            catch (Exception exception)
            {
                SafeLogRecipientFailure(hub, exception);
            }
        }
    }

    private static void SafeLogRecipientFailure(ReferenceHub hub, Exception exception)
    {
        string name;
        try { name = hub?.LoggedNameFromRefHub() ?? "<null>"; }
        catch { name = "<unavailable>"; }
        try
        {
            Logger.Error(
                $"Announcer: Isolated approved-recipient send failure for {name}: {exception}");
        }
        catch { }
    }

    private static string NormalizeApiName(string apiName)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            throw new ArgumentException("Announcer API name must be non-empty.", nameof(apiName));

        string normalized = apiName.Trim().ToUpperInvariant();
        if (Encoding.UTF8.GetByteCount(normalized) > AnnouncerVocabularyMessage.MaxApiNameBytes)
            throw new ArgumentException(
                $"Announcer API names are limited to {AnnouncerVocabularyMessage.MaxApiNameBytes} UTF-8 bytes.",
                nameof(apiName));
        foreach (char c in normalized)
        {
            if (char.IsWhiteSpace(c))
                throw new ArgumentException("Announcer API names cannot contain whitespace.", nameof(apiName));
        }

        return normalized;
    }

    public sealed class Word
    {
        internal Word() { }
        public string ApiName { get; internal set; }
        public AssetAddress AudioAddress { get; internal set; }
        internal byte[] PackIndexHash;
        public float DurationSeconds { get; internal set; }
        public CassieClipCategory Type { get; internal set; }
    }
}
