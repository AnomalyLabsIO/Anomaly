using System.Collections.Generic;

namespace Anomaly.Client.API.Commands;

/// <summary>
/// A registerable client-side command. Commands are scoped to the registering
/// mod; the canonical invocation name is <c>&lt;modId&gt;.&lt;Name&gt;</c>,
/// with short <see cref="Aliases"/> available for convenience (collision-checked
/// across the registry).
/// </summary>
/// <remarks>
/// Anomaly's client command registry is intended for local/debug actions and
/// user-triggered local toggles. Server-authoritative actions (e.g. "give me
/// this custom item") should be invoked via a LabAPI server command on the
/// server, not through this registry.
/// </remarks>
public interface ICommand
{
    /// <summary>
    /// Short name, appended after the mod id: full invocation is <c>modId.Name</c>.
    /// Must be lowercase alphanumeric with underscores; no dots.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Optional alternate names usable without the mod prefix. Collision-checked
    /// against all other registered commands. Return an empty list if none.
    /// </summary>
    IReadOnlyList<string> Aliases { get; }

    /// <summary>
    /// One-line description shown in help output. Localization is the responsibility
    /// of the implementation (e.g. resolve through <c>Tr.Get</c>).
    /// </summary>
    string Description { get; }

    /// <summary>Executes the command. Must not throw — catch and report via <see cref="CommandContext.Reply"/>.</summary>
    void Execute(CommandContext context);
}
