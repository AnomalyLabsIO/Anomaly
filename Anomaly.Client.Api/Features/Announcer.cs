using System.Collections.ObjectModel;
using Anomaly.Client.Api.Assets;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Il2CppCassie;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Client-side wrapper for the in-game CASSIE announcer vocabulary.
/// </summary>
public static class Announcer
{
    private static readonly Dictionary<CassieLine, AssetAddress> BundleWords = new();
    public static CassieLineDatabase LineDatabase => TryGetDatabase(out CassieLineDatabase db) ? db : null;

    public static ReadOnlyCollection<AnnouncerWord> AllLines
    {
        get
        {
            List<AnnouncerWord> words = new();
            var db = LineDatabase;
            var lines = db?.AllLines;
            if (lines == null)
                return words.AsReadOnly();

            for (int i = 0; i < lines.Length; i++)
            {
                CassieLine line = lines[i];
                if (line != null)
                    words.Add(new AnnouncerWord(line));
            }

            return words.AsReadOnly();
        }
    }

    public static string[] CollectionNames
    {
        get
        {
            var lines = AllLines;
            string[] names = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
                names[i] = lines[i].ApiName;
            return names;
        }
    }

    /// <summary>
    /// All <see cref="AnnouncerWord"/>s whose <see cref="AnnouncerWord.Category"/>
    /// matches <paramref name="category"/>. Useful for menus that surface CASSIE
    /// vocabulary grouped by type (Word / Number / Caption / etc.).
    /// </summary>
    public static ReadOnlyCollection<AnnouncerWord> GetByCategory(CassieClipCategory category)
    {
        var all = AllLines;
        List<AnnouncerWord> matches = new();
        for (int i = 0; i < all.Count; i++)
        {
            AnnouncerWord word = all[i];
            if (word != null && word.Category == category)
                matches.Add(word);
        }
        return matches.AsReadOnly();
    }

    public static bool TryGetDatabase(out CassieLineDatabase db)
    {
        return CassieTtsAnnouncer.TryGetDatabase(out db) && db != null;
    }

    public static bool IsValid(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        string normalized = NormalizeApiName(word);
        var db = LineDatabase;
        var lines = db?.AllLines;
        if (lines == null)
            return false;

        for (int i = 0; i < lines.Length; i++)
        {
            CassieLine line = lines[i];
            if (line != null && string.Equals(line.ApiName, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Adds a new word from a typed pack address; duration follows the effective clip.</summary>
    public static bool AddWord(string apiName, AssetAddress audioAddress, CassieClipCategory type = CassieClipCategory.Word)
    {
        if (audioAddress.IsGameReplacement || !AnnouncerVocabularyMessage.IsValidNewApiName(apiName) ||
            BundleWords.Count >= AnnouncerVocabularyMessage.MaxWords ||
            !AnomalyResources.TryGetAudioClip(audioAddress, out var clip) || clip.frequency <= 0)
            return false;
        if (!TryAddWord(apiName, clip, (float)((double)clip.samples / clip.frequency), type, out var line)) return false;
        BundleWords.Add(line, audioAddress);
        return true;
    }

    // Compiled runtime calls these before publishing/releasing an address layer.
    internal static void RefreshBundleWords()
    {
        foreach (var pair in BundleWords.ToArray())
        {
            if (AnomalyResources.TryGetAudioClip(pair.Value, out var clip) && clip.frequency > 0)
            {
                pair.Key.Clip = clip;
                pair.Key.Duration = (float)((double)clip.samples / clip.frequency);
            }
            else
            {
                RemoveWords(new[] { pair.Key });
                BundleWords.Remove(pair.Key);
            }
        }
    }

    internal static void ClearBundleWords()
    {
        RemoveWords(BundleWords.Keys.ToArray());
        BundleWords.Clear();
    }

    internal static bool AddServerWord(string apiName, AudioClip clip, float durationSeconds, CassieClipCategory type, out CassieLine line)
    {
        return TryAddWord(apiName, clip, durationSeconds, type, out line);
    }

    internal static void RemoveWords(IEnumerable<CassieLine> words)
    {
        if (words == null || !TryGetDatabase(out CassieLineDatabase db) || db.AllLines == null)
            return;

        HashSet<IntPtr> removePointers = new();
        foreach (CassieLine word in words)
        {
            if (word == null)
                continue;

            try
            {
                removePointers.Add(word.Pointer);
            }
            catch
            {
                // The backing IL2CPP object can be collected during scene teardown.
            }
        }

        if (removePointers.Count == 0)
            return;

        var current = db.AllLines;
        List<CassieLine> kept = new(current.Length);
        for (int i = 0; i < current.Length; i++)
        {
            CassieLine line = current[i];
            if (line == null)
                continue;

            try
            {
                if (removePointers.Contains(line.Pointer))
                    continue;
            }
            catch
            {
                continue;
            }

            kept.Add(line);
        }

        if (kept.Count == current.Length)
            return;

        db.AllLines = new Il2CppReferenceArray<CassieLine>(kept.ToArray());
        db.ValidateCache(true);
    }

    private static bool TryAddWord(string apiName, AudioClip clip, float durationSeconds, CassieClipCategory type, out CassieLine line)
    {
        line = null;

        if (clip == null || durationSeconds <= 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
            return false;
        if (!System.Enum.IsDefined(typeof(CassieClipCategory), type))
            return false;
        if (!TryGetDatabase(out CassieLineDatabase db) || db.AllLines == null)
            return false;

        string normalizedName;
        try
        {
            normalizedName = NormalizeApiName(apiName);
        }
        catch
        {
            return false;
        }

        var current = db.AllLines;
        for (int i = 0; i < current.Length; i++)
        {
            CassieLine existing = current[i];
            if (existing != null && string.Equals(existing.ApiName, normalizedName, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        line = new CassieLine
        {
            ApiName = normalizedName,
            Clip = clip,
            Duration = durationSeconds,
            Type = type
        };

        CassieLine[] expanded = new CassieLine[current.Length + 1];
        for (int i = 0; i < current.Length; i++)
            expanded[i] = current[i];
        expanded[expanded.Length - 1] = line;

        db.AllLines = new Il2CppReferenceArray<CassieLine>(expanded);
        db.ValidateCache(true);
        return true;
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
}
