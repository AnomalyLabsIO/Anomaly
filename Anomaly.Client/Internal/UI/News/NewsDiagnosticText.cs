using Anomaly.Client.Api.Localization;

namespace Anomaly.Client.Internal.UI.News;

internal static class NewsDiagnosticText
{
    internal const string Prefix = "anomaly.news.diagnostics.";

    internal static string Get(string key, params object[] arguments) => Tr.Get(Prefix + key, arguments);
}
