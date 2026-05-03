namespace Anomaly.Client.Api.Internal.Localization;

/// <summary>
/// Compile-time constants for Anomaly's own translation keys (anomaly.* namespace).
/// Internal — third-party mods declare their own keys in their own namespaces.
/// </summary>
internal static class TrKeys
{
    // Auth overlay
    public const string Overlay_Authenticating = "anomaly.overlay.authenticating";
    public const string Overlay_ConnectionAuthenticating = "anomaly.overlay.connection_authenticating";
    public const string Overlay_PasswordRequired = "anomaly.overlay.password_required";
    public const string PasswordPrompt_Title = "anomaly.password_prompt.title";
    public const string PasswordPrompt_Hint = "anomaly.password_prompt.hint";

    // File transfer overlay
    public const string FileTransfer_Preparing = "anomaly.overlay.file_transfer.preparing";
    public const string FileTransfer_Downloading = "anomaly.overlay.file_transfer.downloading";
    public const string FileTransfer_Connecting = "anomaly.overlay.file_transfer.connecting";
    public const string FileTransfer_LoadingLevel = "anomaly.overlay.file_transfer.loading_level";

    // News / main menu announcement
    public const string News_Anomaly_Title = "anomaly.news.anomaly.title";
    public const string News_Anomaly_Body = "anomaly.news.anomaly.body";
    public const string News_Anomaly_Date = "anomaly.news.anomaly.date";

    // Main menu customization
    public const string Menu_Welcome = "anomaly.menu.welcome";
    public const string Menu_AuthConnected = "anomaly.menu.auth_connected";
    public const string Menu_AuthNotSignedIn = "anomaly.menu.auth_not_signed_in";

    // Shared between preloader and main menu version labels
    public const string Version_ModSuffix = "anomaly.version_modded_suffix";
}
