using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Anomaly.Shared.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Anomaly.Client.Auth;

public struct PlayerAuth
{
    internal const string DpapiProtection = "dpapi-current-user-v1";

    [JsonProperty("identity")]
    public string Identity { get; set; }

    [JsonProperty("identity_protection", NullValueHandling = NullValueHandling.Ignore)]
    public string IdentityProtection { get; set; }

    [JsonProperty("identity_public", NullValueHandling = NullValueHandling.Ignore)]
    public string IdentityPublic { get; set; }

    [JsonProperty("username")]
    public string Username { get; set; }

    [JsonProperty("identity_rotation_chain", NullValueHandling = NullValueHandling.Ignore)]
    public string IdentityRotationChain { get; set; }

    [JsonExtensionData]
    public IDictionary<string, JToken> ExtensionData { get; set; }

    public void SetIdentity(byte[] identity)
        => SetIdentity(identity, WindowsDataProtection.Protect);

    internal void SetIdentity(byte[] identity, Func<byte[], byte[]> protect)
    {
        if (identity == null || identity.Length != 97)
            throw new ArgumentException("Anomaly identity must be 97 bytes.", nameof(identity));
        if (protect == null)
            throw new ArgumentNullException(nameof(protect));

        byte[] identityPublic = null;
        byte[] protectedIdentity = null;
        try
        {
            identityPublic = AuthCrypto.WritePublic(AuthCrypto.ReadIdentity(identity));
            protectedIdentity = protect(identity);
            if (protectedIdentity == null || protectedIdentity.Length == 0)
                throw new InvalidOperationException("Identity protection returned an empty result.");

            Identity = Convert.ToBase64String(protectedIdentity);
            IdentityProtection = DpapiProtection;
            IdentityPublic = Convert.ToBase64String(identityPublic);
        }
        finally
        {
            if (identityPublic != null)
                Array.Clear(identityPublic, 0, identityPublic.Length);
            if (protectedIdentity != null)
                Array.Clear(protectedIdentity, 0, protectedIdentity.Length);
        }
    }

    public byte[] GetIdentity()
        => GetIdentity(WindowsDataProtection.Unprotect);

    internal byte[] GetIdentity(Func<byte[], byte[]> unprotect)
    {
        if (unprotect == null)
            throw new ArgumentNullException(nameof(unprotect));

        var stored = Convert.FromBase64String(Identity);
        if (string.IsNullOrEmpty(IdentityProtection))
        {
            if (stored.Length != 97)
            {
                Array.Clear(stored, 0, stored.Length);
                throw new InvalidOperationException("Legacy Anomaly identity is not 97 bytes.");
            }
            return stored; // legacy plaintext; AuthHandler migrates it immediately
        }
        if (!string.Equals(IdentityProtection, DpapiProtection, StringComparison.Ordinal))
        {
            Array.Clear(stored, 0, stored.Length);
            throw new InvalidOperationException("Unsupported Anomaly identity protection scheme: " + IdentityProtection);
        }

        try
        {
            var identity = unprotect(stored);
            if (identity != null && identity.Length == 97)
                return identity;

            if (identity != null)
                Array.Clear(identity, 0, identity.Length);
            throw new InvalidOperationException("Protected Anomaly identity is not 97 bytes.");
        }
        finally
        {
            Array.Clear(stored, 0, stored.Length);
        }
    }

    public void SetIdentityRotationChain(ClientIdentityRotationChain chain)
    {
        IdentityRotationChain = chain == null
            ? null
            : Convert.ToBase64String(chain.Encode());
    }

    public ClientIdentityRotationChain GetIdentityRotationChain()
    {
        if (string.IsNullOrWhiteSpace(IdentityRotationChain))
            return null;
        return ClientIdentityRotationChain.Decode(Convert.FromBase64String(IdentityRotationChain));
    }
}

/// <summary>Windows DPAPI current-user wrapper with UI explicitly forbidden.</summary>
internal static class WindowsDataProtection
{
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(byte[] value)
        => Transform(value, protect: true);

    public static byte[] Unprotect(byte[] value)
        => Transform(value, protect: false);

    private static byte[] Transform(byte[] value, bool protect)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new PlatformNotSupportedException("Anomaly client identities require the Windows DPAPI current-user keystore.");
        if (value == null || value.Length == 0)
            throw new ArgumentException("DPAPI input cannot be empty.", nameof(value));

        var input = new DataBlob();
        var output = new DataBlob();
        try
        {
            input.Size = value.Length;
            input.Data = Marshal.AllocHGlobal(value.Length);
            Marshal.Copy(value, 0, input.Data, value.Length);

            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!success)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (output.Size <= 0 || output.Data == IntPtr.Zero)
                throw new InvalidOperationException("DPAPI returned an empty result.");

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero)
            {
                for (var i = 0; i < input.Size; i++)
                    Marshal.WriteByte(input.Data, i, 0);
                Marshal.FreeHGlobal(input.Data);
            }
            if (output.Data != IntPtr.Zero)
            {
                for (var i = 0; i < output.Size; i++)
                    Marshal.WriteByte(output.Data, i, 0);
                LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
