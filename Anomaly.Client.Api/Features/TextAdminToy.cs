using Il2CppAdminToys;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed TextToy state.</summary>
[ScriptType]
public class TextAdminToy : AdminToy
{
    internal TextAdminToy(TextToy toy) : base(toy) => TextBase = toy;

    public TextToy TextBase { get; }
    /// <summary>Replicated format string, before the game's substitutions.</summary>
    public string TextFormat => TextBase.TextFormat;
    public Vector2 DisplaySize => TextBase.DisplaySize;
}
