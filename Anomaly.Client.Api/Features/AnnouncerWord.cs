using Il2CppCassie;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public sealed class AnnouncerWord
{
    internal AnnouncerWord(CassieLine line)
    {
        Base = line;
    }

    public CassieLine Base { get; }

    [ScriptVisible]
    public string ApiName => Base?.ApiName;

    public AudioClip Clip => Base?.Clip;

    [ScriptVisible]
    public float Duration => Base?.Duration ?? 0f;

    [ScriptVisible]
    public CassieClipCategory Type => Base?.Type ?? CassieClipCategory.Word;

    /// <summary>
    /// The category of this announcer line — alias of <see cref="Type"/>, kept
    /// for naming symmetry with <see cref="Announcer.GetByCategory(CassieClipCategory)"/>.
    /// </summary>
    public CassieClipCategory Category => Type;
}
