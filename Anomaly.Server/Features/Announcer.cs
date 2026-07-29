using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Anomaly.Server.Assets;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Cassie;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Features;

/// <summary>
/// Server-authoritative extension point for adding CASSIE/Announcer vocabulary.
/// Registered words are replayed to clients after their asset transfer completes.
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

    public static IReadOnlyCollection<Word> Words => _words.Values;

    public static void Init()
    {
        if (_initialized)
            return;

        _initialized = true;
        FileTransferSession.SessionCompleted += OnSessionCompleted;
        CassieTtsAnnouncer.OnSingletonSet += ApplyAll;
        ApplyAll();
    }

    public static void Disable()
    {
        if (_initialized)
        {
            FileTransferSession.SessionCompleted -= OnSessionCompleted;
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

    public static Word AddWord(string apiName, string assetPath, float durationSeconds, CassieClipCategory type = CassieClipCategory.Word)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            throw new ArgumentException("Asset path must be non-empty.", nameof(assetPath));

        byte[] hash = AssetRegistry.GetHashByPath(assetPath);
        if (hash == null)
            throw new FileNotFoundException($"No registered server asset exists at '{assetPath}'.", assetPath);

        if (AssetRegistry.TryGetKind(hash, out AssetKind kind) && kind != AssetKind.AudioClip)
            throw new ArgumentException($"Asset '{assetPath}' is '{kind}', not '{AssetKind.AudioClip}'.", nameof(assetPath));

        return AddWord(apiName, hash, durationSeconds, type, assetPath);
    }

    public static Word AddWord(string apiName, byte[] audioHash, float durationSeconds, CassieClipCategory type = CassieClipCategory.Word)
    {
        return AddWord(apiName, audioHash, durationSeconds, type, null);
    }

    private static Word AddWord(string apiName, byte[] audioHash, float durationSeconds, CassieClipCategory type, string assetPath)
    {
        string normalizedName = NormalizeApiName(apiName);
        if (audioHash == null || audioHash.Length != 32)
            throw new ArgumentException("Audio hash must be 32 bytes.", nameof(audioHash));
        if (durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration must be a positive finite value.");
        if (!Enum.IsDefined(typeof(CassieClipCategory), type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (_words.ContainsKey(normalizedName))
            throw new InvalidOperationException($"Announcer word '{normalizedName}' is already registered.");

        var word = new Word
        {
            ApiName = normalizedName,
            AssetPath = assetPath,
            AudioHash = CloneHash(audioHash),
            DurationSeconds = durationSeconds,
            Type = type
        };

        _words[normalizedName] = word;
        TryApplyWord(word);

        if (_initialized)
            AnomalyMessaging.SendToAll(BuildMessage(new[] { word }));

        return word;
    }

    private static void OnSessionCompleted(ReferenceHub hub)
    {
        if (_words.Count == 0)
            return;

        Logger.Info($"Announcer: Replaying {_words.Count} custom word(s) to {hub.LoggedNameFromRefHub()}");
        AnomalyMessaging.SendToClient(hub, BuildMessage(_words.Values));
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
                CloneHash(word.AudioHash),
                word.DurationSeconds,
                (byte)word.Type));
        }
        return message;
    }

    private static string NormalizeApiName(string apiName)
    {
        if (string.IsNullOrWhiteSpace(apiName))
            throw new ArgumentException("Announcer API name must be non-empty.", nameof(apiName));

        string normalized = apiName.Trim().ToUpperInvariant();
        foreach (char c in normalized)
        {
            if (char.IsWhiteSpace(c))
                throw new ArgumentException("Announcer API names cannot contain whitespace.", nameof(apiName));
        }

        return normalized;
    }

    private static byte[] CloneHash(byte[] hash)
    {
        if (hash == null)
            return null;

        var clone = new byte[hash.Length];
        Array.Copy(hash, clone, hash.Length);
        return clone;
    }

    public sealed class Word
    {
        public string ApiName { get; internal set; }
        public string AssetPath { get; internal set; }
        public byte[] AudioHash { get; internal set; }
        public float DurationSeconds { get; internal set; }
        public CassieClipCategory Type { get; internal set; }
    }
}
