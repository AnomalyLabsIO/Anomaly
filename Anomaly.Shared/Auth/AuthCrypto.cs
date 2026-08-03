using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Anomaly.Shared.Auth;

public class AuthCrypto
{
    public static readonly X9ECParameters SingingCurve = ECNamedCurveTable.GetByName("secp256r1");
    private static readonly ECDomainParameters SigningDomain = new ECDomainParameters(SingingCurve);

    public static byte[] CreateIdentity()
    {
        var buf = new byte[97];
        var generator = new ECKeyPairGenerator();
        generator.Init(new ECKeyGenerationParameters(SigningDomain, new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        var privateParams = (ECPrivateKeyParameters)pair.Private;
        var publicParams = (ECPublicKeyParameters)pair.Public;
        var privateBytes = privateParams.D.ToByteArrayUnsigned();
        Array.Copy(privateBytes, 0, buf, 32 - privateBytes.Length, privateBytes.Length);
        Array.Copy(publicParams.Q.GetEncoded(false), 0, buf, 32, 65);
        return buf;
    }

    public static AsymmetricCipherKeyPair ReadIdentity(byte[] buf)
    {
        if (buf == null || buf.Length != 97)
            throw new ArgumentException("Identity must contain a 32-byte private key and 65-byte public key.", nameof(buf));

        var privateKeyBytes = new byte[32];
        Array.Copy(buf, 0, privateKeyBytes, 0, privateKeyBytes.Length);
        var privateValue = new BigInteger(1, privateKeyBytes);
        if (privateValue.SignValue <= 0 || privateValue.CompareTo(SingingCurve.N) >= 0)
            throw new ArgumentException("Identity private key is outside the signing curve range.", nameof(buf));

        var privateKey = new ECPrivateKeyParameters(privateValue, SigningDomain);
        var publicKey = new byte[65];
        Array.Copy(buf, 32, publicKey, 0, publicKey.Length);
        var publicKeyParams = ReadPublic(publicKey);
        var pair = new AsymmetricCipherKeyPair(publicKeyParams, privateKey);

        // A stored identity contains both halves. Verify their correspondence so a
        // truncated or manually edited identity cannot silently sign under one key
        // while advertising another.
        var probe = Encoding.ASCII.GetBytes("anomaly-identity-self-check");
        var signature = SignData(probe, pair);
        if (!VerifyData(probe, signature, publicKeyParams))
            throw new ArgumentException("Identity public and private keys do not match.", nameof(buf));

        return pair;
    }

    public static byte[] WritePublic(AsymmetricCipherKeyPair ecdsa)
    {
        if (!(ecdsa?.Public is ECPublicKeyParameters publicKeyParameters))
            throw new ArgumentException("An ECDSA public key is required.", nameof(ecdsa));
        return publicKeyParameters.Q.GetEncoded(false);
    }

    public static ECPublicKeyParameters ReadPublic(byte[] buf)
    {
        if (buf == null || buf.Length != 65 || buf[0] != 0x04)
            throw new ArgumentException("ECDSA public key must be a 65-byte uncompressed point.", nameof(buf));

        var point = SingingCurve.Curve.DecodePoint(buf);
        if (point == null || point.IsInfinity || !point.IsValid())
            throw new ArgumentException("ECDSA public key is not a valid curve point.", nameof(buf));
        return new ECPublicKeyParameters(point, SigningDomain);
    }

    public static byte[] SignData(byte[] data, AsymmetricCipherKeyPair pair)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));
        var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
        signer.Init(true, pair.Private);
        signer.BlockUpdate(data, 0, data.Length);
        return signer.GenerateSignature();
    }

    public static bool VerifyData(byte[] data, byte[] signature, ECPublicKeyParameters publicKey)
    {
        if (data == null || signature == null || publicKey == null)
            return false;

        try
        {
            var signer = SignerUtilities.GetSigner("SHA-256withECDSA");
            signer.Init(false, publicKey);
            signer.BlockUpdate(data, 0, data.Length);
            return signer.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    public static AsymmetricCipherKeyPair CreateX25519KeyPair()
    {
        var generator = new X25519KeyPairGenerator();
        generator.Init(new X25519KeyGenerationParameters(new SecureRandom()));
        return generator.GenerateKeyPair();
    }

    public static byte[] GetX25519Public(AsymmetricCipherKeyPair pair)
    {
        if (!(pair?.Public is X25519PublicKeyParameters publicKey))
            throw new ArgumentException("An X25519 public key is required.", nameof(pair));
        return publicKey.GetEncoded();
    }

    public static byte[] ComputeSharedSecret(AsymmetricCipherKeyPair pair, byte[] publicKey)
    {
        if (!(pair?.Private is X25519PrivateKeyParameters))
            throw new ArgumentException("An X25519 private key is required.", nameof(pair));
        if (publicKey == null || publicKey.Length != 32)
            throw new ArgumentException("X25519 public key must be 32 bytes.", nameof(publicKey));

        var agreement = new X25519Agreement();
        agreement.Init(pair.Private);
        var peerPublic = new X25519PublicKeyParameters(publicKey);
        var secret = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(peerPublic, secret, 0);

        var anyNonZero = 0;
        for (var i = 0; i < secret.Length; i++)
            anyNonZero |= secret[i];
        if (anyNonZero == 0)
        {
            Array.Clear(secret, 0, secret.Length);
            throw new CryptographicException("X25519 produced the forbidden all-zero shared secret.");
        }

        return secret;
    }

    public static AuthSessionKeys DeriveSessionKeys(
        AsymmetricCipherKeyPair sessionKeyPair,
        byte[] peerSessionPublic,
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
        => DeriveSessionKeysCore(
            sessionKeyPair,
            peerSessionPublic,
            client,
            server,
            serverIdentifier,
            passwordChallenge,
            false);

    /// <summary>
    /// Derives the deprecated bidirectional key only for an explicitly supplied
    /// loopback peer. Production remote transports must use DeriveSessionKeys.
    /// </summary>
    public static AuthSessionKeys DeriveLoopbackMigrationSessionKeys(
        AsymmetricCipherKeyPair sessionKeyPair,
        byte[] peerSessionPublic,
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        IPAddress peerAddress)
    {
        if (peerAddress == null || !IPAddress.IsLoopback(peerAddress))
            throw new InvalidOperationException("Legacy bidirectional transport is restricted to explicit loopback migration tests.");

        return DeriveSessionKeysCore(
            sessionKeyPair,
            peerSessionPublic,
            client,
            server,
            serverIdentifier,
            passwordChallenge,
            true);
    }

    private static AuthSessionKeys DeriveSessionKeysCore(
        AsymmetricCipherKeyPair sessionKeyPair,
        byte[] peerSessionPublic,
        ClientHandshake client,
        ServerHandshake server,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        bool enableLegacyLoopbackMigration)
    {
        var sharedSecret = ComputeSharedSecret(sessionKeyPair, peerSessionPublic);
        var transcriptHash = AuthTranscript.Hash(client, server, serverIdentifier, passwordChallenge);
        try
        {
            return new AuthSessionKeys(
                HkdfSha256(sharedSecret, transcriptHash, "anomaly-v2/client-to-server", 32),
                HkdfSha256(sharedSecret, transcriptHash, "anomaly-v2/server-to-client", 32),
                HkdfSha256(sharedSecret, transcriptHash, "anomaly-v2/login", 32),
                enableLegacyLoopbackMigration
                    ? HkdfSha256(sharedSecret, transcriptHash, "anomaly-v2/legacy-loopback-migration", 32)
                    : null,
                HkdfSha256(sharedSecret, transcriptHash, "anomaly-v2/session-id", 16),
                transcriptHash);
        }
        finally
        {
            Array.Clear(sharedSecret, 0, sharedSecret.Length);
        }
    }

    public static byte[] AesEncrypt(byte[] key, byte[] iv, byte[] data)
        => AesEncrypt(key, iv, data, null);

    public static byte[] AesEncrypt(byte[] key, byte[] iv, byte[] data, byte[] associatedData)
    {
        ValidateAesInputs(key, iv, data);
        var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
        cipher.Init(true, new AeadParameters(new KeyParameter(key), 128, iv, associatedData));
        return cipher.DoFinal(data);
    }

    public static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] data)
        => AesDecrypt(key, iv, data, null);

    public static byte[] AesDecrypt(byte[] key, byte[] iv, byte[] data, byte[] associatedData)
    {
        ValidateAesInputs(key, iv, data);
        var cipher = CipherUtilities.GetCipher("AES/GCM/NoPadding");
        cipher.Init(false, new AeadParameters(new KeyParameter(key), 128, iv, associatedData));
        return cipher.DoFinal(data);
    }

    public static byte[] AesEncryptWithNonce(byte[] key, byte[] data)
    {
        var iv = new byte[12];
        new SecureRandom().NextBytes(iv);
        var bytes = AesEncrypt(key, iv, data);
        var buf = new byte[iv.Length + bytes.Length];
        Array.Copy(iv, 0, buf, 0, iv.Length);
        Array.Copy(bytes, 0, buf, iv.Length, bytes.Length);
        return buf;
    }

    public static byte[] AesDecryptWithNonce(byte[] key, byte[] data)
    {
        if (data == null || data.Length < 28)
            throw new ArgumentException("AES-GCM envelope is too short.", nameof(data));
        var iv = new byte[12];
        var bytes = new byte[data.Length - iv.Length];
        Array.Copy(data, 0, iv, 0, iv.Length);
        Array.Copy(data, iv.Length, bytes, 0, bytes.Length);
        return AesDecrypt(key, iv, bytes);
    }

    private static byte[] HkdfSha256(byte[] inputKey, byte[] salt, string info, int outputLength)
    {
        byte[] pseudoRandomKey;
        using (var extract = new HMACSHA256(salt ?? new byte[32]))
            pseudoRandomKey = extract.ComputeHash(inputKey);

        try
        {
            var infoBytes = Encoding.ASCII.GetBytes(info);
            var output = new byte[outputLength];
            var previous = new byte[0];
            var written = 0;
            byte counter = 1;
            while (written < outputLength)
            {
                byte[] blockInput;
                using (var stream = new System.IO.MemoryStream())
                {
                    stream.Write(previous, 0, previous.Length);
                    stream.Write(infoBytes, 0, infoBytes.Length);
                    stream.WriteByte(counter);
                    blockInput = stream.ToArray();
                }

                byte[] block;
                using (var expand = new HMACSHA256(pseudoRandomKey))
                    block = expand.ComputeHash(blockInput);
                if (previous.Length != 0)
                    Array.Clear(previous, 0, previous.Length);
                previous = block;
                var count = Math.Min(block.Length, outputLength - written);
                Array.Copy(block, 0, output, written, count);
                written += count;
                counter++;
            }

            if (previous.Length != 0)
                Array.Clear(previous, 0, previous.Length);
            return output;
        }
        finally
        {
            Array.Clear(pseudoRandomKey, 0, pseudoRandomKey.Length);
        }
    }

    private static void ValidateAesInputs(byte[] key, byte[] iv, byte[] data)
    {
        if (key == null || key.Length != 32)
            throw new ArgumentException("AES-256 key must be 32 bytes.", nameof(key));
        if (iv == null || iv.Length != 12)
            throw new ArgumentException("AES-GCM nonce must be 12 bytes.", nameof(iv));
        if (data == null)
            throw new ArgumentNullException(nameof(data));
    }
}
