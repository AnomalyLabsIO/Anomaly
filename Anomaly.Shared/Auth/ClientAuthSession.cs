using System;
using System.Linq;
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
    private readonly object _loginSync = new object();
    private ClientModInventory _modInventory;
    private ClientIntegrityAudit _integrityAudit;
    private byte[] _cachedLoginHash;
    private byte[] _cachedPasswordProof;
    private byte[] _cachedEncryptedLogin;

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

    public void BindAdmission(ClientModInventory inventory, ClientIntegrityAudit audit)
    {
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));
        if (audit == null)
            throw new ArgumentNullException(nameof(audit));
        var frozenInventory = ClientModInventory.Decode(inventory.Encode());
        var frozenAudit = ClientIntegrityAudit.Decode(audit.Encode());
        lock (_loginSync)
        {
            if (_modInventory != null || _cachedEncryptedLogin != null)
                throw new InvalidOperationException("Client admission is already bound. Changed claims require a fresh authentication session.");
            _modInventory = frozenInventory;
            _integrityAudit = frozenAudit;
        }
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
        lock (_loginSync)
            return CreateLoginAttemptCore(handshake, serverIdentifier, passwordChallenge, displayName, passwordProof);
    }

    private ClientLoginAttempt CreateLoginAttemptCore(
        ServerHandshake handshake,
        string serverIdentifier,
        PasswordChallenge passwordChallenge,
        string displayName,
        byte[] passwordProof)
    {
        if (_modInventory == null || _integrityAudit == null)
            throw new InvalidOperationException("Client inventory and integrity audit must be bound before creating a login attempt.");
        var clientHandshake = CreateHandshake();
        var loginTranscriptHash = AuthTranscript.HashLogin(
            clientHandshake,
            handshake,
            serverIdentifier,
            passwordChallenge,
            displayName);
        try
        {
            if (_cachedEncryptedLogin != null)
            {
                if (!_cachedLoginHash.SequenceEqual(loginTranscriptHash)
                    || !SameOptionalBytes(_cachedPasswordProof, passwordProof))
                    throw new InvalidOperationException("A login attempt is already frozen with different connection data.");
                return new ClientLoginAttempt { signature = (byte[])_cachedEncryptedLogin.Clone() };
            }

            var encodedInventory = _modInventory.Encode();
            var encodedAudit = _integrityAudit.Encode();
            var transcript = AuthTranscript.BuildLoginWithAdmission(
                clientHandshake, handshake, serverIdentifier, passwordChallenge, displayName, encodedInventory, encodedAudit);
            var signature = AuthCrypto.SignData(transcript, _identityKeyPair);
            var payload = ClientLoginAttempt.EncodePlainPayload(signature, passwordProof, encodedInventory, encodedAudit);
            using (var keys = DeriveSessionKeys(handshake, serverIdentifier, passwordChallenge))
            {
                try
                {
                    var encryptedPayload = AuthCrypto.AesEncrypt(keys.LoginKey, _nonce, payload, loginTranscriptHash);
                    if (encryptedPayload.Length > ClientLoginAttempt.MaxSize)
                        throw new InvalidOperationException("Encrypted login attempt exceeds the connection size limit.");
                    _cachedLoginHash = (byte[])loginTranscriptHash.Clone();
                    _cachedPasswordProof = passwordProof == null ? null : (byte[])passwordProof.Clone();
                    _cachedEncryptedLogin = (byte[])encryptedPayload.Clone();
                    return new ClientLoginAttempt { signature = encryptedPayload };
                }
                finally
                {
                    Array.Clear(payload, 0, payload.Length);
                }
            }
        }
        finally
        {
            Array.Clear(loginTranscriptHash, 0, loginTranscriptHash.Length);
        }
    }

    private static bool SameOptionalBytes(byte[] left, byte[] right)
    {
        return left == null ? right == null : right != null && left.SequenceEqual(right);
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
