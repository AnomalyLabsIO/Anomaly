using System;
using Anomaly.Client.Api.Commands;
using HarmonyLib;
using Il2Cpp;
using Il2CppGameCore;
using MelonLoader;
using UnityEngine;

namespace Anomaly.Client.Patches.Command;

/// <summary>
/// Harmony prefix on <see cref="Il2CppGameCore.Console.TypeCommand"/> that lets
/// Anomaly's <see cref="CommandRegistry"/> handle local, client-only commands
/// before the game's default <c>ConsoleCommandHandler</c> sees them.
/// </summary>
/// <remarks>
/// <para><b>Scope — strictly local commands.</b> Only plain (unprefixed) input
/// is considered. Commands starting with:</para>
/// <list type="bullet">
///   <item><c>.</c> — dot commands, dispatched to the server by the game's own
///     path;</item>
///   <item><c>/</c> — remote admin queries, handled by the base-game
///     RemoteAdmin / LabAPI server API;</item>
///   <item><c>@</c> — admin chat, again server-side.</item>
/// </list>
/// <para>…are <b>never</b> intercepted. Server-authoritative features belong on
/// the server (LabAPI commands + RemoteAdmin). This patch exists to control
/// local features only (HUD toggles, debug overlays, client tweaks).</para>
///
/// <para><b>Dispatch order.</b> For an unprefixed command the prefix:</para>
/// <list type="number">
///   <item>Extracts the first whitespace-delimited token as the command name.</item>
///   <item>Looks it up in Anomaly's <see cref="CommandRegistry"/> (canonical
///     <c>mod.command</c> or alias).</item>
///   <item>If unknown, returns <c>true</c> — the game's own handler gets its
///     usual shot at the input (including the standard "Command X does not
///     exist!" reply).</item>
///   <item>If known, executes via the registry, routing reply lines back to
///     the console (through <see cref="CommandSender.Print"/> when we have a
///     sender, else directly via <see cref="Console.AddLog"/>), sets the method's
///     return value to the last reply, and returns <c>false</c> to skip the
///     original.</item>
/// </list>
///
/// <para><b>Response plumbing.</b> The game's <c>TypeCommand</c> uses the
/// supplied <see cref="CommandSender"/> (or <c>ConsoleCommandSender.Ccs</c> if
/// null) and writes replies via <c>sender.Print(text, ConsoleColor)</c>, which
/// ultimately land in <c>Console.AddLog</c>. We mirror that: when invoked from
/// the user pressing Enter in the console (<c>ProceedButton</c>), <paramref name="sender"/>
/// is null, so we write via <c>Console.AddLog</c> directly. When a caller passes
/// a sender, we delegate to <c>sender.Print</c> so nested
/// <c>CommandSender</c> subclasses continue to work.</para>
///
/// <para>We also echo the typed line with <c>">>> "</c> to match the native
/// behavior (the game does <c>Debug.Log(">>> " + cmd)</c> immediately on entry,
/// but that goes to the Unity log rather than the in-game console).</para>
/// </remarks>
[HarmonyPatch(typeof(Il2CppGameCore.Console), nameof(Il2CppGameCore.Console.TypeCommand))]
public static class ConsoleTypeCommandPatch
{
    private static readonly char[] WhitespaceSplit = { ' ', '\t' };

    [HarmonyPrefix]
    public static bool Prefix(string cmd, CommandSender sender, ref string __result)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cmd)) return true;

            string trimmed = cmd.TrimStart();
            if (trimmed.Length == 0) return true;

            // Server-bound prefixes: defer to the game's native dispatch.
            char first = trimmed[0];
            if (first == '.' || first == '/' || first == '@') return true;

            // First token is the command name (canonical or alias).
            int spaceIdx = trimmed.IndexOfAny(WhitespaceSplit);
            string name = spaceIdx < 0 ? trimmed : trimmed.Substring(0, spaceIdx);

            if (CommandRegistry.Lookup(name) == null)
                return true; // not ours — let the game handle (or reject) it.

            // Echo the typed line to the in-game console so history matches.
            Il2CppGameCore.Console.AddLog(">>> " + cmd, Color.gray);

            string lastReply = string.Empty;
            bool handled = CommandRegistry.Execute(
                trimmed,
                reply: line =>
                {
                    var text = line ?? string.Empty;
                    if (sender != null) sender.Print(text);
                    else Il2CppGameCore.Console.AddLog(text, Color.green);
                    lastReply = text;
                });

            // If a lookup succeeded we always return a non-null __result — the
            // method is documented to return the response string, and null is
            // reserved by the game for early-out paths (e.g. not-connected).
            __result = handled ? lastReply : string.Empty;
            return false;
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ConsoleTypeCommandPatch: {ex}");
            return true; // safety: fall back to the game on unexpected error.
        }
    }
}
