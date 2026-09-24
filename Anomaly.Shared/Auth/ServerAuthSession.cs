using System;
using System.Net;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace Anomaly.Shared.Auth;

public class ServerAuthSession
{
    private AsymmetricCipherKeyPair _identityKeyPair;
    private AsymmetricCipherKeyPair _sessionKeyPair;
    private byte[] _challenge;

    public static ServerAuthSession Generate(byte[] persistentIdentity)
    {
        var env = new ServerAuthSession
        {
            _identityKeyPair = AuthCrypto.ReadIdentity(persistentIdentity),
            _sessionKeyPair = AuthCrypto.CreateX25519KeyPair(),
            _challenge = new byte[ServerHandshake.ChallengeSize],
        };
        new SecureRandom().NextBytes(env._challenge);
        return env;
    }

    public ServerHandshake CreateHandshake(
        ClientHandshake clientHandshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        ServerIdentityRotationRecord identityRotationRecord = null)
    {
        var handshake = new ServerHandshake
        {
            sessionPublic = AuthCrypto.GetX25519Public(_sessionKeyPair),
            challenge = (byte[])_challenge.Clone(),
            identityPublic = AuthCrypto.WritePublic(_identityKeyPair),
            identityRotationRecord = identityRotationRecord?.Clone(),
        };
        var transcript = AuthTranscript.Build(clientHandshake, handshake, serverIdentifier, passwordChallenge);
        handshake.transcriptSignature = AuthCrypto.SignData(transcript, _identityKeyPair);
        return handshake;
    }

    public bool Validate(
        ClientHandshake clientHandshake,
        ServerHandshake serverHandshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName,
        ClientLoginAttempt attempt,
        out byte[] passwordProof)
    {
        return Validate(clientHandshake, serverHandshake, serverIdentifier, passwordChallenge,
            displayName, attempt, out passwordProof, out _);
    }

    public bool Validate(
        ClientHandshake clientHandshake,
        ServerHandshake serverHandshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName,
        ClientLoginAttempt attempt,
        out byte[] passwordProof,
        out ClientModInventory inventory)
    {
        passwordProof = null;
        inventory = null;
        if (attempt?.signature == null || attempt.signature.Length < ClientLoginAttempt.MinSize
            || attempt.signature.Length > ClientLoginAttempt.MaxSize)
            return false;

        byte[] payload;
        using (var keys = DeriveSessionKeys(clientHandshake, serverHandshake, serverIdentifier, passwordChallenge))
        {
            byte[] loginTranscriptHash = null;
            try
            {
                loginTranscriptHash = AuthTranscript.HashLogin(
                    clientHandshake,
                    serverHandshake,
                    serverIdentifier,
                    passwordChallenge,
                    displayName);
                payload = AuthCrypto.AesDecrypt(
                    keys.LoginKey,
                    clientHandshake.nonce,
                    attempt.signature,
                    loginTranscriptHash);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (loginTranscriptHash != null)
                    Array.Clear(loginTranscriptHash, 0, loginTranscriptHash.Length);
            }
        }

        try
        {
            if (!ClientLoginAttempt.TryDecodePlainPayload(payload, out var signature, out passwordProof, out inventory))
            {
                passwordProof = null;
                inventory = null;
                return false;
            }

            var transcript = AuthTranscript.BuildLoginWithModInventory(
                clientHandshake,
                serverHandshake,
                serverIdentifier,
                passwordChallenge,
                displayName,
                inventory.Encode());
            var identityPublic = AuthCrypto.ReadPublic(clientHandshake.identityPublic);
            if (AuthCrypto.VerifyData(transcript, signature, identityPublic))
                return true;
            passwordProof = null;
            inventory = null;
            return false;
        }
        catch
        {
            passwordProof = null;
            inventory = null;
            return false;
        }
        finally
        {
            Array.Clear(payload, 0, payload.Length);
        }
    }

    public AuthSessionKeys DeriveSessionKeys(
        ClientHandshake clientHandshake,
        ServerHandshake serverHandshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
    {
        return AuthCrypto.DeriveSessionKeys(
            _sessionKeyPair,
            clientHandshake.sessionPublic,
            clientHandshake,
            serverHandshake,
            serverIdentifier,
            passwordChallenge);
    }

    public AuthSessionKeys DeriveLoopbackMigrationSessionKeys(
        ClientHandshake clientHandshake,
        ServerHandshake serverHandshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        IPAddress peerAddress)
    {
        return AuthCrypto.DeriveLoopbackMigrationSessionKeys(
            _sessionKeyPair,
            clientHandshake.sessionPublic,
            clientHandshake,
            serverHandshake,
            serverIdentifier,
            passwordChallenge,
            peerAddress);
    }
}
