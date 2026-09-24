using System;

namespace Anomaly.AssetValidation;

public sealed class BundleValidationException : Exception {
    internal BundleValidationException(string reason) : base(reason) { }
}

internal static class Guard {
    internal static void Require(bool valid, string reason) {
        if (!valid) throw new BundleValidationException(reason);
    }
}
