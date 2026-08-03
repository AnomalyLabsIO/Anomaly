using System;
using System.Net;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

namespace Anomaly.Shared.Auth;

public class ClientAuthSession
{
    private AsymmetricCipherKeyPair _identityKeyPair;
    private AsymmetricCipherKeyPair _sessionKeyPair;
    private byte[] _nonce;
    private byte _gameMajor;
    private byte _gameMinor;
    private byte _gameRevision;
    private bool _gameBackwardCompatibility;
    private byte _gameBackwardRevision;
    private string _connectionContext;
    private ClientIdentityRotationChain _identityRotationChain;

    public static ClientAuthSession Generate(byte[] identity)
    {
        var env = new ClientAuthSession
        {
            _identityKeyPair = AuthCrypto.ReadIdentity(identity),
            _sessionKeyPair = AuthCrypto.CreateX25519KeyPair(),
            _nonce = new byte[ClientHandshake.NonceSize],
        };
        new SecureRandom().NextBytes(env._nonce);
        return env;
    }

    public void BindConnectionContext(
        byte gameMajor,
        byte gameMinor,
        byte gameRevision,
        bool gameBackwardCompatibility,
        byte gameBackwardRevision,
        string connectionContext)
    {
        if (gameMajor == 0)
            throw new ArgumentOutOfRangeException(nameof(gameMajor));
        if (!AuthConnectionContext.IsNormalizedEndpoint(connectionContext))
            throw new ArgumentException(
                "Connection context must be a normalized host:port endpoint.",
                nameof(connectionContext));

        _gameMajor = gameMajor;
        _gameMinor = gameMinor;
        _gameRevision = gameRevision;
        _gameBackwardCompatibility = gameBackwardCompatibility;
        _gameBackwardRevision = gameBackwardCompatibility ? gameBackwardRevision : (byte)0;
        _connectionContext = connectionContext;
    }

    public void BindIdentityRotationChain(ClientIdentityRotationChain rotationChain)
    {
        if (rotationChain == null)
        {
            _identityRotationChain = null;
            return;
        }

        byte[] currentPublic = AuthCrypto.WritePublic(_identityKeyPair);
        if (!rotationChain.TryValidate(currentPublic, out _, out var error))
            throw new ArgumentException(
                "Client identity rotation chain does not authorize the active identity: " + error,
                nameof(rotationChain));
        _identityRotationChain = rotationChain.Clone();
    }

    public ClientHandshake CreateHandshake(byte[] retryCookie = null)
    {
        return new ClientHandshake
        {
            identityPublic = AuthCrypto.WritePublic(_identityKeyPair),
            sessionPublic = AuthCrypto.GetX25519Public(_sessionKeyPair),
            nonce = (byte[])_nonce.Clone(),
            gameMajor = _gameMajor,
            gameMinor = _gameMinor,
            gameRevision = _gameRevision,
            gameBackwardCompatibility = _gameBackwardCompatibility,
            gameBackwardRevision = _gameBackwardRevision,
            connectionContext = _connectionContext,
            identityRotationChain = _identityRotationChain?.Clone(),
            retryCookie = retryCookie == null ? new byte[0] : (byte[])retryCookie.Clone(),
        };
    }

    public bool ValidateServerHandshake(
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
    {
        if (handshake == null || handshake.transcriptSignature == null)
            return false;

        try
        {
            var transcript = AuthTranscript.Build(CreateHandshake(), handshake, serverIdentifier, passwordChallenge);
            var serverIdentity = AuthCrypto.ReadPublic(handshake.identityPublic);
            return AuthCrypto.VerifyData(transcript, handshake.transcriptSignature, serverIdentity);
        }
        catch
        {
            return false;
        }
    }

    public ClientLoginAttempt CreateLoginAttempt(
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName,
        byte[] passwordProof)
    {
        var clientHandshake = CreateHandshake();
        var transcript = AuthTranscript.BuildLogin(
            clientHandshake,
            handshake,
            serverIdentifier,
            passwordChallenge,
            displayName);
        var loginTranscriptHash = AuthTranscript.HashLogin(
            clientHandshake,
            handshake,
            serverIdentifier,
            passwordChallenge,
            displayName);
        var signature = AuthCrypto.SignData(transcript, _identityKeyPair);
        var payload = ClientLoginAttempt.EncodePlainPayload(signature, passwordProof);

        try
        {
            using (var keys = DeriveSessionKeys(handshake, serverIdentifier, passwordChallenge))
            {
                var encryptedPayload = AuthCrypto.AesEncrypt(keys.LoginKey, _nonce, payload, loginTranscriptHash);
                return new ClientLoginAttempt { signature = encryptedPayload };
            }
        }
        finally
        {
            Array.Clear(loginTranscriptHash, 0, loginTranscriptHash.Length);
            Array.Clear(payload, 0, payload.Length);
        }
    }

    public AuthSessionKeys DeriveSessionKeys(
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge)
    {
        return AuthCrypto.DeriveSessionKeys(
            _sessionKeyPair,
            handshake.sessionPublic,
            CreateHandshake(),
            handshake,
            serverIdentifier,
            passwordChallenge);
    }

    public AuthSessionKeys DeriveLoopbackMigrationSessionKeys(
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        IPAddress peerAddress)
    {
        return AuthCrypto.DeriveLoopbackMigrationSessionKeys(
            _sessionKeyPair,
            handshake.sessionPublic,
            CreateHandshake(),
            handshake,
            serverIdentifier,
            passwordChallenge,
            peerAddress);
    }

    public byte[] CreatePasswordProof(
        string password,
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge challenge)
    {
        if (challenge == null || !challenge.Required)
            return null;

        var verifier = PasswordAuth.DeriveVerifier(password, challenge.Salt, challenge.Iterations);
        try
        {
            return PasswordAuth.CreateProof(verifier, _nonce, handshake.challenge, serverIdentifier, challenge.Nonce);
        }
        finally
        {
            Array.Clear(verifier, 0, verifier.Length);
        }
    }
}
