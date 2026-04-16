using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Anomaly.Client.API.Mods;
using MelonLoader;

namespace Anomaly.Client.API.Commands;

/// <summary>
/// Static registry for client-side commands. Mods register their
/// <see cref="ICommand"/> implementations under their mod id; invocations are
/// dispatched by canonical name (<c>modId.commandName</c>) or alias.
/// </summary>
/// <remarks>
/// <para>
/// The primary surface is <see cref="Execute(string)"/>, which parses a raw
/// invocation line (name + args) and dispatches it. UI layers, console hooks,
/// or hotkeys can all route into this one method.
/// </para>
/// <para>
/// Integration with the game's client console is handled by
/// <c>ConsoleTypeCommandPatch</c> in <c>Anomaly.Client.Patches.Command</c>,
/// which intercepts <c>Il2CppGameCore.Console.TypeCommand</c> and routes
/// unprefixed input through <see cref="Execute(string, Action{string})"/>.
/// </para>
/// </remarks>
public static class CommandRegistry
{
    // Command names after the mod prefix: lowercase alphanumeric + underscore.
    private static readonly Regex NamePattern = new Regex(
        @"^[a-z][a-z0-9_]{0,31}$", RegexOptions.Compiled);

    private sealed class Entry
    {
        public string CanonicalName; // "modId.commandName"
        public ICommand Command;
        public string ModId;
    }

    private static readonly Dictionary<string, Entry> _byName = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Entry> _byAlias = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    /// <summary>All registered commands (canonical name -> instance).</summary>
    public static IReadOnlyCollection<ICommand> All =>
        new ReadOnlyCollection<ICommand>(_byName.Values.Select(e => e.Command).ToList());

    /// <summary>Registers a command under <paramref name="modId"/>.</summary>
    /// <remarks>The caller's mod id must be registered (see <see cref="AnomalyClientMods"/>).
    /// The special id "anomaly" is accepted for core commands.</remarks>
    public static void Register(string modId, ICommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        if (string.IsNullOrEmpty(modId)) throw new ArgumentException("modId must be non-empty.", nameof(modId));
        if (string.IsNullOrEmpty(command.Name)) throw new ArgumentException("Command.Name must be non-empty.");
        if (!NamePattern.IsMatch(command.Name))
            throw new ArgumentException($"Command name '{command.Name}' is invalid (lowercase alphanumeric/underscore, no dots).");

        // modId must be registered or the reserved "anomaly" core.
        bool isCore = modId == "anomaly" || modId.StartsWith("anomaly.", StringComparison.Ordinal);
        if (!isCore && !AnomalyClientMods.IsKeyOwnedByRegisteredMod(modId))
            throw new ArgumentException($"CommandRegistry.Register: mod id '{modId}' is not registered.");

        string canonical = $"{modId}.{command.Name}";
        if (_byName.ContainsKey(canonical))
        {
            MelonLogger.Warning($"CommandRegistry: command '{canonical}' already registered; ignoring duplicate.");
            return;
        }

        var entry = new Entry { CanonicalName = canonical, Command = command, ModId = modId };
        _byName[canonical] = entry;

        if (command.Aliases != null)
        {
            foreach (var alias in command.Aliases)
            {
                if (string.IsNullOrEmpty(alias)) continue;
                if (_byAlias.TryGetValue(alias, out var existing))
                {
                    MelonLogger.Warning(
                        $"CommandRegistry: alias '{alias}' for '{canonical}' collides with '{existing.CanonicalName}'; skipping alias.");
                    continue;
                }
                if (_byName.ContainsKey(alias))
                {
                    MelonLogger.Warning(
                        $"CommandRegistry: alias '{alias}' for '{canonical}' collides with a canonical command name; skipping alias.");
                    continue;
                }
                _byAlias[alias] = entry;
            }
        }

        MelonLogger.Msg($"CommandRegistry: Registered '{canonical}'.");
    }

    /// <summary>
    /// Parses and executes <paramref name="line"/>. First whitespace-delimited
    /// token is the command name (canonical or alias), remainder is the args.
    /// Returns true if a command was dispatched, false if no match.
    /// </summary>
    public static bool Execute(string line) => Execute(line, reply: null);

    /// <summary>
    /// Parses and executes <paramref name="line"/>, routing reply output to
    /// <paramref name="reply"/>. If <paramref name="reply"/> is null, replies
    /// go to the MelonLogger.
    /// </summary>
    public static bool Execute(string line, Action<string> reply)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;

        // Split the first token (command name) off the rest.
        int firstSpace = line.IndexOfAny(new[] { ' ', '\t' });
        string name = firstSpace < 0 ? line.Trim() : line.Substring(0, firstSpace).Trim();
        string rest = firstSpace < 0 ? string.Empty : line.Substring(firstSpace + 1);

        if (!_byName.TryGetValue(name, out var entry) &&
            !_byAlias.TryGetValue(name, out entry))
        {
            return false;
        }

        var args = SplitArgs(rest);
        var ctx = new CommandContext(rest, args, reply);
        try
        {
            entry.Command.Execute(ctx);
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"CommandRegistry: '{entry.CanonicalName}' threw: {ex}");
            ctx.Reply($"Error: {ex.Message}");
        }
        return true;
    }

    /// <summary>
    /// Returns the command registered under <paramref name="nameOrAlias"/> or null.
    /// </summary>
    public static ICommand Lookup(string nameOrAlias)
    {
        if (string.IsNullOrEmpty(nameOrAlias)) return null;
        if (_byName.TryGetValue(nameOrAlias, out var entry)) return entry.Command;
        if (_byAlias.TryGetValue(nameOrAlias, out entry)) return entry.Command;
        return null;
    }

    /// <summary>
    /// Splits arg string into tokens, respecting double-quoted segments so
    /// multi-word args can be passed as "hello world".
    /// </summary>
    internal static List<string> SplitArgs(string s)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(s)) return result;

        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in s)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result;
    }
}
