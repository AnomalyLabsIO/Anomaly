using System;
using System.Text.RegularExpressions;

namespace Anomaly.Shared.Content
{
    // Mirror of Anomaly.Client.Api.Internal.ModNamespace (net48 / Shared copy — Shared
    // can't reference Client.Api). Anomaly keeps no global mod registry; this only enforces
    // that caller-supplied content keys look like a namespace so two mods can't collide by
    // accident. Keep the regex in sync with the Client.Api original.
    public static class ModNamespace
    {
        private static readonly Regex Pattern = new Regex(
            @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
            RegexOptions.Compiled);

        /// <summary>True if <paramref name="id"/> is a syntactically valid namespace/dotted key.</summary>
        public static bool IsValid(string id) =>
            !string.IsNullOrEmpty(id) && Pattern.IsMatch(id);

        /// <summary>True if <paramref name="id"/> is in the reserved <c>anomaly</c> / <c>anomaly.*</c> space.</summary>
        public static bool IsReserved(string id) =>
            id == "anomaly" || (id != null && id.StartsWith("anomaly.", StringComparison.Ordinal));

        /// <summary>
        /// Validates a content key of the form "<c>modid:name</c>" (the modid part must be a
        /// valid, non-reserved namespace; name must be a single lowercase segment).
        /// </summary>
        public static bool IsValidContentKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1)
                return false;
            string modId = key.Substring(0, colon);
            string name = key.Substring(colon + 1);
            return IsValid(modId) && !IsReserved(modId)
                && Regex.IsMatch(name, @"^[a-z][a-z0-9_]{0,63}$");
        }
    }
}
