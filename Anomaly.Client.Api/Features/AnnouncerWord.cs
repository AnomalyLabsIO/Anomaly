using Il2CppCassie;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public sealed class AnnouncerWord
{
    internal AnnouncerWord(CassieLine line)
    {
        Base = line;
    }

    public CassieLine Base { get; }

    public string ApiName => Base?.ApiName;

    public AudioClip Clip => Base?.Clip;

    public float Duration => Base?.Duration ?? 0f;

    public CassieClipCategory Type => Base?.Type ?? CassieClipCategory.Word;

    /// <summary>
    /// The category of this announcer line — alias of <see cref="Type"/>, kept
    /// for naming symmetry with <see cref="Announcer.GetByCategory(CassieClipCategory)"/>.
    /// </summary>
    public CassieClipCategory Category => Type;
}
