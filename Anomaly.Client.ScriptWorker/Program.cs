using Anomaly.Client.Scripting.Remote.Protocol;
using MoonSharp.Interpreter;

namespace Anomaly.Client.ScriptWorker;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        // There is deliberately no source-file, command-line probe or unsandboxed mode.
        if (arguments.Length != 0) return 2;
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        return new WorkerActor(input, output).Run();
    }
}

internal sealed class WorkerActor
{
    private readonly Stream _input, _output;
    private readonly WorkerSession?[] _sessions = new WorkerSession?[8];
    private readonly MemoryBudget _memory = new(64L * 1024 * 1024, reclaimable: true);
    private WorkerCatalog? _catalog;
    private long _lastCommand, _nextRequest;
    private RemoteFrame? _active;
    private int _requests;
    private bool _faulted;

    internal WorkerActor(Stream input, Stream output) { _input = input; _output = output; }

    internal int Run()
    {
        try
        {
            Send(new RemoteFrame(FrameKind.Ready, 0, 0, 0, Wire.Record(("version", WireValue.Numeric(RemoteProtocol.Version)))));
            while (true)
            {
                var frame = RemoteProtocol.Read(_input);
                if (frame == null) return 0;
                _active = frame;
                if (frame.Command <= _lastCommand || frame.Request != 0)
                    throw new InvalidDataException("Unexpected command identity.");
                _lastCommand = frame.Command;
                _requests = 0;
                WorkerResult result;
                switch (frame.Kind)
                {
                    case FrameKind.Configure:
                        if (frame.Session != 0 || _catalog != null) throw new InvalidDataException("Unexpected configuration.");
                        _catalog = new WorkerCatalog(frame.Data);
                        result = WorkerResult.Completed;
                        break;
                    case FrameKind.Probe:
                        if (frame.Session != 0 || _catalog != null) throw new InvalidDataException("Unexpected probe.");
                        result = new WorkerResult("Completed", BoundaryProbe.Run(frame.Data));
                        break;
                    case FrameKind.Load:
                        var loadIndex = SessionIndex(frame.Session);
                        if (_catalog == null || _sessions[loadIndex] != null) throw new InvalidDataException("Unexpected session load.");
                        try
                        {
                            var session = new WorkerSession(this, _catalog, _memory);
                            _sessions[loadIndex] = session;
                            result = session.Load(frame.Data);
                        }
                        catch (ScriptExecutionAbortedException exception) { result = WorkerResult.Aborted(exception); }
                        break;
                    case FrameKind.Invoke:
                    case FrameKind.InvokeFunction:
                        var sessionIndex = SessionIndex(frame.Session);
                        var owned = _sessions[sessionIndex] ?? throw new InvalidDataException("Unknown session.");
                        result = owned.Invoke(frame.Data, frame.Kind == FrameKind.InvokeFunction);
                        break;
                    default:
                        throw new InvalidDataException("Unexpected frame direction or kind.");
                }
                Send(new RemoteFrame(FrameKind.Completed, frame.Session, frame.Command, 0, result.ToWire()));
                _active = null;
                if (result.Status != "Completed" || _faulted) return 1;
            }
        }
        catch (Exception exception) when (exception is not StackOverflowException)
        {
            try
            {
                Send(new RemoteFrame(FrameKind.Fault, _active?.Session ?? 0, _active?.Command ?? 0,
                    _active?.Request ?? 0, Wire.Record(("error", WireValue.String(Wire.Error(exception.Message))))));
            }
            catch (Exception) { }
            return 1;
        }
    }

    internal WireValue Request(WorkerSession session, Dictionary<string, WireValue> fields)
    {
        var command = _active ?? throw new InvalidDataException("No active command.");
        var budget = session.Script.ExecutionBudget ?? throw new InvalidDataException("No active budget.");
        budget.Check();
        if (_faulted || ++_requests > 256) return Terminal(session, "Host request limit exceeded.");
        try
        {
            fields.Add("release", session.CollectReleases());
            var request = checked(++_nextRequest);
            Send(new RemoteFrame(FrameKind.Request, command.Session, command.Command, request, WireValue.Object("", fields)));
            RemoteFrame response;
            using (budget.PauseForHost())
                response = RemoteProtocol.Read(_input) ?? throw new EndOfStreamException("Host disconnected during an operation.");
            budget.Check();
            if (response.Kind != FrameKind.Response || response.Session != command.Session ||
                response.Command != command.Command || response.Request != request)
                throw new InvalidDataException("Host response identity mismatch.");
            Wire.Shape(response.Data, "ok", "value", "error");
            var ok = response.Data.Required("ok").RequireBoolean();
            var value = response.Data.Required("value");
            if (!ok)
            {
                var error = response.Data.Optional("error");
                throw new HostOperationException(error.Kind == WireKind.Null ? "The host rejected an operation." : error.RequireString(768));
            }
            return value;
        }
        catch (HostOperationException exception) { throw new ScriptRuntimeException(exception.Message); }
        catch (ScriptExecutionAbortedException) { throw; }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        { return Terminal(session, exception.Message); }
    }

    private WireValue Terminal(WorkerSession session, string reason)
    {
        _faulted = true;
        session.ProtocolError = Wire.Error(reason);
        session.Script.ExecutionBudget!.Cancel();
        throw new ScriptExecutionAbortedException(ExecutionTerminationReason.Cancelled);
    }

    private void Send(RemoteFrame frame) { RemoteProtocol.Write(_output, frame, fromWorker: true); _output.Flush(); }
    private static int SessionIndex(int session) => session is >= 1 and <= 8 ? session - 1 :
        throw new InvalidDataException("Invalid session identity.");
    private sealed class HostOperationException : Exception { internal HostOperationException(string error) : base(error) { } }
}

internal sealed record WorkerResult(string Status, WireValue? Value = null, string? Error = null)
{
    internal static readonly WorkerResult Completed = new("Completed");
    internal static WorkerResult Aborted(ScriptExecutionAbortedException exception) => new(exception.Reason.ToString(), null,
        "Script execution terminated: " + exception.Reason);
    internal WireValue ToWire()
    {
        var fields = new Dictionary<string, WireValue>(StringComparer.Ordinal) { ["status"] = WireValue.String(Status) };
        if (Value != null) fields.Add("value", Value);
        if (Error != null) fields.Add("error", WireValue.String(Wire.Error(Error)));
        return WireValue.Object("", fields);
    }
}

internal static class Wire
{
    internal static WireValue Record(params (string Name, WireValue Value)[] fields) => WireValue.Object("",
        fields.ToDictionary(field => field.Name, field => field.Value, StringComparer.Ordinal));
    internal static Dictionary<string, WireValue> Request(string op) => new(StringComparer.Ordinal) { ["op"] = WireValue.String(op) };
    internal static void Shape(WireValue value, params string[] allowed)
    {
        foreach (var pair in value.RequireObject(16384))
            if (!allowed.Contains(pair.Key, StringComparer.Ordinal)) throw new InvalidDataException("Unexpected object field.");
    }
    internal static string Error(string error)
    {
        // UTF-8 frame bounds apply to bytes, not UTF-16 code units.
        var length = Math.Min(error.Length, 192);
        if (length > 0 && char.IsHighSurrogate(error[length - 1])) length--;
        return error[..length];
    }
}
