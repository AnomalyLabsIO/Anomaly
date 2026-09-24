using System;
using Anomaly.Server.Auth;
using CommandSystem;
using LabApi.Loader;

namespace Anomaly.Server.Commands;

[CommandHandler(typeof(GameConsoleCommandHandler))]
internal sealed class PasswordCommand : ICommand
{
    public PasswordCommand() { }

    public string Command => "password";

    public string[] Aliases => Array.Empty<string>();

    public string Description => "Sets, clears, or checks the Anomaly preauth password.";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        if (arguments.Count == 0)
        {
            response = "Usage: password <value> | password status | password clear";
            return false;
        }

        var first = arguments.Array[arguments.Offset];
        if (string.Equals(first, "status", StringComparison.OrdinalIgnoreCase))
        {
            response = $"Anomaly password required: {PasswordStore.IsRequired}; verifier configured: {PasswordStore.HasPassword}; "
                + $"legacy loopback-only mode: {Plugin.Instance?.Config?.EnableLegacyPasswordAuthOnLoopback == true}. "
                + "Public password mode remains disabled until a vetted OPAQUE implementation supports both runtimes.";
            return true;
        }

        if (string.Equals(first, "clear", StringComparison.OrdinalIgnoreCase))
        {
            if (!PasswordStore.ClearPassword(out var clearError))
            {
                response = "Failed to clear Anomaly password: " + clearError;
                return false;
            }

            var disableSaved = TrySetRequirePassword(false, out var disableError);
            response = disableSaved
                ? "Anomaly password cleared and RequirePassword disabled."
                : "Anomaly password cleared and disabled for this runtime, but failed to persist RequirePassword=false: " + disableError;
            return true;
        }

        var password = JoinArguments(arguments);
        var iterations = Plugin.Instance?.Config?.PasswordKdfIterations ?? Anomaly.Shared.Auth.PasswordAuth.DefaultIterations;
        if (!PasswordStore.SetPassword(password, iterations, out var error))
        {
            response = "Failed to save Anomaly password: " + error;
            return false;
        }

        var enabledSaved = TrySetRequirePassword(true, out var enableError);
        var migrationMode = Plugin.Instance?.Config?.EnableLegacyPasswordAuthOnLoopback == true;
        response = enabledSaved
            ? (migrationMode
                ? "Anomaly password saved. Legacy password auth is enabled for loopback clients only; remote clients are rejected."
                : "Anomaly password saved, but public password auth is intentionally disabled. All clients are rejected while RequirePassword remains true unless the loopback-only migration switch is enabled.")
            : "Anomaly password saved for this runtime, but failed to persist RequirePassword=true: " + enableError;
        return true;
    }

    private static bool TrySetRequirePassword(bool required, out string error)
    {
        error = null;
        var plugin = Plugin.Instance;
        if (plugin == null || plugin.Config == null)
        {
            error = "Anomaly plugin config is not initialized.";
            return false;
        }

        plugin.Config.RequirePassword = required;
        if (plugin.TrySaveConfig(plugin.Config, plugin.ConfigFileName))
            return true;

        error = "LabAPI failed to save the Anomaly config file.";
        return false;
    }

    private static string JoinArguments(ArraySegment<string> arguments)
    {
        var values = new string[arguments.Count];
        Array.Copy(arguments.Array, arguments.Offset, values, 0, arguments.Count);
        return string.Join(" ", values);
    }
}
