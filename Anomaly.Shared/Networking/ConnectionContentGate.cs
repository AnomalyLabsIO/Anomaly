using System;
using System.IO;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking;

/// <summary>Pure exact-offer admission state. No native connection or executable code is retained.</summary>
internal sealed class ConnectionContentGate
{
    internal const double TimeoutSeconds = 120;
    private double _deadline;
    private ulong _sessionId;
    private byte[] _catalogHash, _manifestHash;
    private bool _consented, _assetsLoaded, _scriptsLoaded, _serverConfirmed;
    internal bool IsActive { get; private set; }
    internal bool IsFailed { get; private set; }
    internal bool IsReady => IsActive && !IsFailed && _consented && _assetsLoaded && _scriptsLoaded && _serverConfirmed;
    internal bool OwnsOffer(ulong sessionId, byte[] catalogHash, byte[] manifestHash) =>
        IsActive && !IsFailed && _sessionId != 0 && _sessionId == sessionId &&
        CatalogBuilder.HashesEqual(_catalogHash, catalogHash) && CatalogBuilder.HashesEqual(_manifestHash, manifestHash);

    internal void Begin(double now, bool preserveDeadline = false)
    {
        if (double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentOutOfRangeException(nameof(now));
        var previousDeadline = _deadline;
        var keepDeadline = preserveDeadline && IsActive;
        Reset();
        IsActive = true;
        _deadline = keepDeadline ? previousDeadline : now + TimeoutSeconds;
    }

    internal void BindOffer(ulong sessionId, byte[] catalogHash, byte[] manifestHash)
    {
        EnsureActive();
        if (sessionId == 0 || catalogHash?.Length != 32 || manifestHash?.Length != 32)
            throw new InvalidDataException("Incomplete authenticated content offer.");
        if (_sessionId != 0)
        {
            EnsureOffer(sessionId, catalogHash, manifestHash);
            return;
        }
        _sessionId = sessionId;
        _catalogHash = (byte[])catalogHash.Clone();
        _manifestHash = (byte[])manifestHash.Clone();
    }

    internal void Consent(ulong sessionId, byte[] catalogHash, byte[] manifestHash)
    {
        EnsureOffer(sessionId, catalogHash, manifestHash);
        _consented = true;
    }

    internal void Loaded(ulong sessionId, byte[] catalogHash, byte[] manifestHash, bool scriptsReady)
    {
        EnsureOffer(sessionId, catalogHash, manifestHash);
        if (!_consented || !scriptsReady) throw new InvalidOperationException("Content initialization is not authorized or scripts are unavailable.");
        _assetsLoaded = _scriptsLoaded = true;
    }

    internal void Confirm(ulong sessionId, byte[] catalogHash, byte[] manifestHash)
    {
        EnsureOffer(sessionId, catalogHash, manifestHash);
        if (!_consented || !_assetsLoaded || !_scriptsLoaded)
            throw new InvalidOperationException("Server readiness arrived before local content initialization.");
        _serverConfirmed = true;
    }

    internal bool DeadlineExpired(double now) => IsActive && (double.IsNaN(now) || double.IsInfinity(now) || now >= _deadline);
    internal bool HasTimedOut(double now) => !IsReady && DeadlineExpired(now);
    internal void Fail() { IsFailed = true; }
    internal void Reset()
    {
        IsActive = IsFailed = _consented = _assetsLoaded = _scriptsLoaded = _serverConfirmed = false;
        _sessionId = 0;
        _catalogHash = _manifestHash = null;
        _deadline = 0;
    }

    private void EnsureActive()
    {
        if (!IsActive || IsFailed) throw new InvalidOperationException("Content connection scope is inactive or revoked.");
    }

    private void EnsureOffer(ulong sessionId, byte[] catalogHash, byte[] manifestHash)
    {
        EnsureActive();
        if (_sessionId == 0 || _sessionId != sessionId ||
            !CatalogBuilder.HashesEqual(_catalogHash, catalogHash) || !CatalogBuilder.HashesEqual(_manifestHash, manifestHash))
            throw new InvalidDataException("Content bootstrap did not match this connection's exact offer.");
    }
}
