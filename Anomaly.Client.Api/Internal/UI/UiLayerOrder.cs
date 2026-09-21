using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Client.Api.UI;

namespace Anomaly.Client.Api.Internal.UI;

internal static class UiLayerOrder
{
    internal static void Apply(IEnumerable<UiLayer> layers, Action<UiLayer, int> setSiblingIndex)
    {
        int index = 0;
        foreach (UiLayer layer in layers.OrderBy(layer => (int)layer))
            setSiblingIndex(layer, index++);
    }
}
