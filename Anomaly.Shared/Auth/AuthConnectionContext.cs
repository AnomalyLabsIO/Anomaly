using System;
using System.Globalization;
using System.Net;

namespace Anomaly.Shared.Auth;

/// <summary>
/// Canonical endpoint label bound into the protocol-v2 signed transcript. The
/// server signs the exact target the client intended to reach; TOFU storage uses
/// the same normalization so textual aliases do not silently create new pins.
/// </summary>
public static class AuthConnectionContext
{
    public static string NormalizeEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Endpoint host cannot be empty.", nameof(host));
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Endpoint port must be in [1, 65535].");

        host = host.Trim();
        if (host.Length >= 2 && host[0] == '[' && host[host.Length - 1] == ']')
            host = host.Substring(1, host.Length - 2);

        string canonicalHost;
        if (IPAddress.TryParse(host, out var address))
        {
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            canonicalHost = address.ToString().ToLowerInvariant();
        }
        else
        {
            host = host.TrimEnd('.');
            if (host.Length == 0)
                throw new ArgumentException("Endpoint host cannot be empty.", nameof(host));
            foreach (char character in host)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character) || character == '/' || character == '\\')
                    throw new ArgumentException("Endpoint host contained an invalid character.", nameof(host));
            }

            canonicalHost = new IdnMapping().GetAscii(host).ToLowerInvariant();
            if (Uri.CheckHostName(canonicalHost) != UriHostNameType.Dns)
                throw new ArgumentException("Endpoint host was not a valid DNS name or IP address.", nameof(host));
        }

        return canonicalHost.IndexOf(':') >= 0
            ? "[" + canonicalHost + "]:" + port.ToString(CultureInfo.InvariantCulture)
            : canonicalHost + ":" + port.ToString(CultureInfo.InvariantCulture);
    }

    public static bool IsNormalizedEndpoint(string value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        try
        {
            string host;
            string portText;
            if (value[0] == '[')
            {
                int delimiter = value.IndexOf("]:", StringComparison.Ordinal);
                if (delimiter <= 1)
                    return false;
                host = value.Substring(1, delimiter - 1);
                portText = value.Substring(delimiter + 2);
            }
            else
            {
                int delimiter = value.LastIndexOf(':');
                if (delimiter <= 0 || value.IndexOf(':') != delimiter)
                    return false;
                host = value.Substring(0, delimiter);
                portText = value.Substring(delimiter + 1);
            }

            if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out int port))
                return false;
            return string.Equals(value, NormalizeEndpoint(host, port), StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
