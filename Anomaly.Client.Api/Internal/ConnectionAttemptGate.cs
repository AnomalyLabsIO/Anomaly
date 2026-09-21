using System;

namespace Anomaly.Client.Api.Internal;

internal enum ConnectionWriterDisposition
{
    Duplicate,
    NewUserAttempt,
    ExpectedReconnect,
}

/// <summary>
/// Distinguishes an Anomaly-owned reconnect from a new user connection attempt.
/// Expected reconnects are single-use and bound to the current generation and endpoint.
/// </summary>
internal sealed class ConnectionAttemptGate
{
    private string _expectedEndpoint;
    private int _expectedGeneration = -1;
    private string _activeWriterEndpoint;
    private long _activeWriterIdentity;
    private bool _hasActiveWriter;

    internal int Generation { get; private set; }

    internal int BeginUserAttempt()
    {
        CompleteWriterAttempt();
        AdvanceGeneration();
        ClearExpectedReconnect();
        return Generation;
    }

    internal ConnectionWriterDisposition BeginWriterAttempt(string endpoint, long writerIdentity)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("A normalized endpoint is required.", nameof(endpoint));
        if (writerIdentity == 0)
            throw new ArgumentOutOfRangeException(nameof(writerIdentity));

        if (_hasActiveWriter &&
            _activeWriterIdentity == writerIdentity &&
            string.Equals(_activeWriterEndpoint, endpoint, StringComparison.OrdinalIgnoreCase))
        {
            return ConnectionWriterDisposition.Duplicate;
        }

        _hasActiveWriter = true;
        _activeWriterIdentity = writerIdentity;
        _activeWriterEndpoint = endpoint;

        if (TryConsumeExpectedReconnect(endpoint))
            return ConnectionWriterDisposition.ExpectedReconnect;

        AdvanceGeneration();
        ClearExpectedReconnect();
        return ConnectionWriterDisposition.NewUserAttempt;
    }

    internal void CompleteWriterAttempt()
    {
        _hasActiveWriter = false;
        _activeWriterIdentity = 0;
        _activeWriterEndpoint = null;
    }

    internal bool TryScheduleReconnect(string endpoint, int generation)
    {
        if (generation != Generation || string.IsNullOrWhiteSpace(endpoint))
            return false;

        _expectedEndpoint = endpoint;
        _expectedGeneration = generation;
        return true;
    }

    internal bool TryConsumeExpectedReconnect(string endpoint)
    {
        bool matches = _expectedGeneration == Generation &&
                       string.Equals(_expectedEndpoint, endpoint, StringComparison.OrdinalIgnoreCase);
        ClearExpectedReconnect();
        return matches;
    }

    internal bool IsCurrent(int generation) => generation == Generation;

    internal void Invalidate()
    {
        CompleteWriterAttempt();
        AdvanceGeneration();
        ClearExpectedReconnect();
    }

    private void ClearExpectedReconnect()
    {
        _expectedEndpoint = null;
        _expectedGeneration = -1;
    }

    private void AdvanceGeneration()
    {
        if (Generation == int.MaxValue)
            throw new InvalidOperationException("Connection attempt generation is exhausted.");
        Generation++;
    }
}
