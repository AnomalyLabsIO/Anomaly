using System;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace Anomaly.Shared.Auth;

public class ClientAuthSession
{
    AsymmetricCipherKeyPair identityKeyPair;
    AsymmetricCipherKeyPair sessionKeyPair;
    byte[] nonce;

    public static ClientAuthSession Generate(byte[] identity)
    {
        var env = new ClientAuthSession();
        env.identityKeyPair = AuthCrypto.ReadIdentity(identity);
        env.sessionKeyPair = AuthCrypto.CreateX25519KeyPair();
        env.nonce = new byte[12];
        new SecureRandom().NextBytes(env.nonce);
        return env;
    }

    public ClientHandshake CreateHandshake()
    {
        return new ClientHandshake
        {
            identityPublic = AuthCrypto.WritePublic(identityKeyPair),
            sessionPublic = AuthCrypto.GetX25519Public(sessionKeyPair),
            nonce = nonce
        };
    }

    public byte[] GetSharedSecret(ServerHandshake handshake) => AuthCrypto.ComputeSharedSecret(sessionKeyPair, handshake.sessionPublic);

    public ClientLoginAttempt CreateLoginAttempt(ServerHandshake handshake)
    {
        return CreateLoginAttempt(handshake, null);
    }

    public ClientLoginAttempt CreateLoginAttempt(ServerHandshake handshake, byte[] passwordProof)
    {
        var buf = new byte[32];
        Array.Copy(nonce, 0, buf, 0, 12);
        Array.Copy(handshake.challenge, 0, buf, 12, 20);
        var signature = AuthCrypto.SignData(buf, identityKeyPair);
        var payload = ClientLoginAttempt.EncodePlainPayload(signature, passwordProof);
        var sharedSecret = AuthCrypto.ComputeSharedSecret(sessionKeyPair, handshake.sessionPublic);
        var encryptedPayload = AuthCrypto.AesEncrypt(sharedSecret, nonce, payload);
        return new ClientLoginAttempt { signature = encryptedPayload };
    }

    public byte[] CreatePasswordProof(string password, ServerHandshake handshake, string serverIdentifier, PasswordChallenge challenge)
    {
        if (challenge == null || !challenge.Required)
            return null;

        var verifier = PasswordAuth.DeriveVerifier(password, challenge.Salt, challenge.Iterations);
        try
        {
            return PasswordAuth.CreateProof(verifier, nonce, handshake.challenge, serverIdentifier, challenge.Nonce);
        }
        finally
        {
            Array.Clear(verifier, 0, verifier.Length);
        }
    }
}
