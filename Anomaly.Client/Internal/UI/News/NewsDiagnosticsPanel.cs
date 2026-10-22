using Anomaly.Client.Components.News;
using Il2Cpp;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Anomaly.Client.Internal.UI.News;

internal static class NewsDiagnosticsPanel
{
    internal static void Install(NewsLoader loader)
    {
        if (loader == null || loader.ArticleText == null) return;
        if (loader.transform.Find("Anomaly.NewsDiagnostics") != null) return;
        var root = AnomalyUiStyle.MakeUi("Anomaly.NewsDiagnostics");
        root.SetActive(false);
        root.transform.SetParent(loader.transform, false);
        try
        {
            var presenter = root.AddComponent<NewsDiagnosticsPresenter>();
            presenter.Initialize(loader);
            root.SetActive(true);
            presenter.Commit();
        }
        catch
        {
            root.SetActive(false);
            Object.Destroy(root);
            throw;
        }
    }
}
