namespace Anomaly.Client.Internal.Localization;

/// <summary>
/// Compile-time constants for Anomaly's own translation keys (anomaly.* namespace).
/// Internal — third-party mods declare their own keys in their own namespaces.
/// </summary>
internal static class TrKeys
{
    // Auth overlay
    public const string Overlay_Authenticating = "anomaly.overlay.authenticating";

    // File transfer overlay
    public const string FileTransfer_Preparing = "anomaly.overlay.file_transfer.preparing";
    public const string FileTransfer_Downloading = "anomaly.overlay.file_transfer.downloading";

    // News / main menu announcement
    public const string News_Anomaly_Title = "anomaly.news.anomaly.title";
    public const string News_Anomaly_Body = "anomaly.news.anomaly.body";
    public const string News_Anomaly_Date = "anomaly.news.anomaly.date";
}
