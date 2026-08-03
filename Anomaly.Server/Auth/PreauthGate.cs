using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Shared.Auth;

namespace Anomaly.Server.Auth;

/// <summary>
/// Cheap, bounded work performed before allocating an X25519 session.
/// Retry cookies prove that the sender can receive traffic at the claimed address.
/// </summary>
internal static class PreauthGate
{
    private const int SecretSize = 32;
    private const int CookieMacSize = 32;
    private const int CookieTimestampSize = 8;
    private const int CookieLifetimeSeconds = 20;
    private const int CookieFutureSkewSeconds = 2;
    private const int PerAddressBurst = 12;
    private const double PerAddressTokensPerSecond = 2.0;
    private const int PerSubnetBurst = 48;
    private const double PerSubnetTokensPerSecond = 8.0;
    private const int GlobalBurst = 256;
    private const double GlobalTokensPerSecond = 64.0;
    private const int MaximumTrackedAddresses = 4096;
    private const int MaximumTrackedSubnets = 1024;
    private const int BucketIdleSeconds = 120;
    private const int MaximumBucketSweepsPerRequest = 8;

    private static readonly object Sync = new object();
    private static readonly Dictionary<string, TokenBucket> AddressBuckets = new Dictionary<string, TokenBucket>();
    private static readonly Queue<string> BucketSweepOrder = new Queue<string>();
    private static readonly Dictionary<string, TokenBucket> SubnetBuckets = new Dictionary<string, TokenBucket>();
    private static readonly Queue<string> SubnetSweepOrder = new Queue<string>();
    private static TokenBucket _globalBucket;
    private static byte[] _cookieSecret;
    private static Func<long> _utcSecondsProvider;

    public static void Initialize()
        => Initialize(null);

    internal static void InitializeForTests(Func<long> utcSecondsProvider)
        => Initialize(utcSecondsProvider ?? throw new ArgumentNullException(nameof(utcSecondsProvider)));

    private static void Initialize(Func<long> utcSecondsProvider)
    {
        lock (Sync)
        {
            ResetLocked();
            _utcSecondsProvider = utcSecondsProvider;
            _cookieSecret = new byte[SecretSize];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(_cookieSecret);
            var now = UtcSeconds();
            _globalBucket = new TokenBucket(GlobalBurst, now);
        }
    }

    public static void Reset()
    {
        lock (Sync)
            ResetLocked();
    }

    public static bool TryAcceptRequest(IPAddress address)
    {
        if (address == null)
            return false;

        lock (Sync)
        {
            if (_cookieSecret == null)
                return false;

            var now = UtcSeconds();
            SweepIdleBuckets(AddressBuckets, BucketSweepOrder, now, MaximumBucketSweepsPerRequest / 2);
            SweepIdleBuckets(SubnetBuckets, SubnetSweepOrder, now, MaximumBucketSweepsPerRequest / 2);

            var addressKey = NormalizeAddress(address);
            if (!AddressBuckets.TryGetValue(addressKey, out var addressBucket))
            {
                if (AddressBuckets.Count >= MaximumTrackedAddresses)
                    return false;
                addressBucket = new TokenBucket(PerAddressBurst, now);
                AddressBuckets.Add(addressKey, addressBucket);
                BucketSweepOrder.Enqueue(addressKey);
            }

            var subnetKey = NormalizeSubnet(address);
            if (!SubnetBuckets.TryGetValue(subnetKey, out var subnetBucket))
            {
                if (SubnetBuckets.Count >= MaximumTrackedSubnets)
                    return false;
                subnetBucket = new TokenBucket(PerSubnetBurst, now);
                SubnetBuckets.Add(subnetKey, subnetBucket);
                SubnetSweepOrder.Enqueue(subnetKey);
            }

            Refill(addressBucket, PerAddressBurst, PerAddressTokensPerSecond, now);
            Refill(subnetBucket, PerSubnetBurst, PerSubnetTokensPerSecond, now);
            Refill(_globalBucket, GlobalBurst, GlobalTokensPerSecond, now);
            addressBucket.LastSeenUtcSeconds = now;
            subnetBucket.LastSeenUtcSeconds = now;
            _globalBucket.LastSeenUtcSeconds = now;

            if (addressBucket.Tokens < 1.0 || subnetBucket.Tokens < 1.0 || _globalBucket.Tokens < 1.0)
                return false;

            addressBucket.Tokens -= 1.0;
            subnetBucket.Tokens -= 1.0;
            _globalBucket.Tokens -= 1.0;
            return true;
        }
    }

    public static byte[] CreateRetryCookie(IPAddress address, ClientHandshake handshake)
    {
        if (address == null)
            throw new ArgumentNullException(nameof(address));
        if (handshake == null)
            throw new ArgumentNullException(nameof(handshake));

        lock (Sync)
        {
            if (_cookieSecret == null)
                throw new InvalidOperationException("Preauth gate is not initialized.");

            var issuedAt = UtcSeconds();
            var mac = ComputeCookieMac(address, handshake, issuedAt);
            var cookie = new byte[ClientHandshake.RetryCookieSize];
            WriteInt64BigEndian(cookie, 0, issuedAt);
            Array.Copy(mac, 0, cookie, CookieTimestampSize, CookieMacSize);
            return cookie;
        }
    }

    public static bool ValidateRetryCookie(IPAddress address, ClientHandshake handshake)
    {
        if (address == null || handshake?.retryCookie == null
            || handshake.retryCookie.Length != ClientHandshake.RetryCookieSize)
            return false;

        lock (Sync)
        {
            if (_cookieSecret == null)
                return false;

            var issuedAt = ReadInt64BigEndian(handshake.retryCookie, 0);
            var age = UtcSeconds() - issuedAt;
            if (age < -CookieFutureSkewSeconds || age > CookieLifetimeSeconds)
                return false;

            var expected = ComputeCookieMac(address, handshake, issuedAt);
            var actual = new byte[CookieMacSize];
            Array.Copy(handshake.retryCookie, CookieTimestampSize, actual, 0, actual.Length);
            return PasswordAuth.FixedTimeEquals(expected, actual);
        }
    }

    private static byte[] ComputeCookieMac(IPAddress address, ClientHandshake handshake, long issuedAt)
    {
        var addressBytes = NormalizeAddressBytes(address);
        var binding = handshake.EncodeCookieBinding();
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(Encoding.ASCII.GetBytes("ANOMALY-RETRY-V2"));
            writer.Write(issuedAt);
            writer.Write((byte)addressBytes.Length);
            writer.Write(addressBytes);
            writer.Write(binding);
            writer.Flush();
            using (var hmac = new HMACSHA256(_cookieSecret))
                return hmac.ComputeHash(stream.ToArray());
        }
    }

    private static void Refill(TokenBucket bucket, int capacity, double refillPerSecond, long now)
    {
        var elapsed = Math.Max(0, now - bucket.LastRefillUtcSeconds);
        if (elapsed != 0)
        {
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * refillPerSecond);
            bucket.LastRefillUtcSeconds = now;
        }
    }

    private static void SweepIdleBuckets(
        Dictionary<string, TokenBucket> buckets,
        Queue<string> sweepOrder,
        long now,
        int maximumSweeps)
    {
        var count = Math.Min(maximumSweeps, sweepOrder.Count);
        for (var i = 0; i < count; i++)
        {
            var key = sweepOrder.Dequeue();
            if (!buckets.TryGetValue(key, out var bucket))
                continue;
            if (now - bucket.LastSeenUtcSeconds > BucketIdleSeconds)
                buckets.Remove(key);
            else
                sweepOrder.Enqueue(key);
        }
    }

    private static string NormalizeAddress(IPAddress address)
        => new IPAddress(NormalizeAddressBytes(address)).ToString();

    internal static string NormalizeSubnet(IPAddress address)
    {
        var bytes = NormalizeAddressBytes(address);
        if (bytes.Length == 4)
        {
            bytes[3] = 0;
            return "v4/24:" + new IPAddress(bytes);
        }

        for (var i = 8; i < bytes.Length; i++)
            bytes[i] = 0;
        return "v6/64:" + new IPAddress(bytes);
    }

    internal static byte[] NormalizeAddressBytes(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        return address.GetAddressBytes();
    }

    internal static bool AddressesEqual(IPAddress left, IPAddress right)
        => left != null && right != null
            && PasswordAuth.FixedTimeEquals(NormalizeAddressBytes(left), NormalizeAddressBytes(right));

    private static long UtcSeconds()
        => _utcSecondsProvider?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static void ResetLocked()
    {
        if (_cookieSecret != null)
            Array.Clear(_cookieSecret, 0, _cookieSecret.Length);
        _cookieSecret = null;
        _globalBucket = null;
        _utcSecondsProvider = null;
        AddressBuckets.Clear();
        BucketSweepOrder.Clear();
        SubnetBuckets.Clear();
        SubnetSweepOrder.Clear();
    }

    private static void WriteInt64BigEndian(byte[] target, int offset, long value)
    {
        for (var i = 7; i >= 0; i--)
        {
            target[offset + i] = (byte)value;
            value >>= 8;
        }
    }

    private static long ReadInt64BigEndian(byte[] source, int offset)
    {
        long value = 0;
        for (var i = 0; i < 8; i++)
            value = (value << 8) | source[offset + i];
        return value;
    }

    private sealed class TokenBucket
    {
        public TokenBucket(double tokens, long now)
        {
            Tokens = tokens;
            LastRefillUtcSeconds = now;
            LastSeenUtcSeconds = now;
        }

        public double Tokens;
        public long LastRefillUtcSeconds;
        public long LastSeenUtcSeconds;
    }
}
