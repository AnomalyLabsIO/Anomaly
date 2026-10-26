using System.Globalization;
using System.Text;
using Anomaly.Client.Api.Scripting;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Interop;

namespace Anomaly.Client.Scripting.Output;

/// <summary>Host-created plain-text output capability, revoked with its session.</summary>
[ScriptType("ScriptOutput")]
internal sealed class ScriptOutput : IDisposable
{
    internal const int MaxTextBytes = 1024;
    internal const int MaxLines = 8;
    internal const int MaxSourceCharacters = 48;
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly ScriptOutputHost _host;
    private readonly Script _owner;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private string _prefix = string.Empty;
    private ScriptOutputHost.RateWindow _logs, _displays;
    private string? _displayText;
    private bool _disposed;

    internal ScriptOutput(ScriptOutputHost host, Script owner, string sourceIdentity)
    {
        _host = host;
        _owner = owner;
        SetSourceIdentity(sourceIdentity);
    }

    internal void SetSourceIdentity(string sourceIdentity)
    {
        CheckOwner(_owner);
        if (_disposed) throw new ObjectDisposedException(nameof(ScriptOutput));
        using var scope = _owner.MemoryBudget?.EnterScope();
        var length = Math.Min(sourceIdentity?.Length ?? 0, MaxSourceCharacters);
        using var reservation = MemoryBudget.ReserveString(10 + Math.Max(1, length));
        _prefix = string.Create(10 + Math.Max(1, length), (sourceIdentity, length), static (span, state) =>
        {
            "[Script:".AsSpan().CopyTo(span);
            if (state.length == 0) span[8] = '_';
            for (var i = 0; i < state.length; i++)
            {
                var c = state.sourceIdentity![i];
                span[8 + i] = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_' or '/' ? c : '_';
            }
            span[^2] = ']';
            span[^1] = ' ';
        });
        reservation.Bind(_prefix);
    }

    internal int Slot { get; set; } = -1;
    internal double ExpiresAt { get; private set; }

    /// <summary>Logs one bounded line with a host-controlled source prefix; false on rejection.</summary>
    [ScriptVisible]
    public bool Log(string text)
    {
        if (!Available || !_host.HasLog || !IsValidText(text, display: false)) return false;
        var now = _host.Now();
        if (!_host.TryRate(false, ref _logs, now)) return false;
        using var scope = _owner.MemoryBudget?.EnterScope();
        using var reservation = MemoryBudget.ReserveString(_prefix.Length + text.Length);
        var line = string.Concat(_prefix, text);
        reservation.Bind(line);
        return _host.WriteLog(line);
    }

    /// <summary>Shows literal text for 0.1 through 10 seconds; false on rejection or unavailable UI.</summary>
    [ScriptVisible]
    public bool ShowText(string text, double durationSeconds)
    {
        if (!Available || !_host.HasDisplay || !double.IsFinite(durationSeconds) || durationSeconds < 0.1 ||
            durationSeconds > 10 || !IsValidText(text, display: true)) return false;
        var now = _host.Now();
        // Expired owners do not occupy scarce UI slots until the next frame.
        _host.Tick();
        if (!_host.HasSlot(this) || !_host.TryRate(true, ref _displays, now)) return false;
        using var scope = _owner.MemoryBudget?.EnterScope();
        using var storage = MemoryBudget.Reserve(ScriptOutputHost.DisplayStorageBytes + 32L + text.Length * 2L);
        var copy = new string(text.AsSpan());
        storage.Bind(copy);
        if (!_host.Show(this, copy)) return false;
        _displayText = copy;
        ExpiresAt = now + durationSeconds;
        return true;
    }

    /// <summary>Clears only this session's text, even when other sessions are displaying text.</summary>
    [ScriptVisible]
    public void ClearText()
    {
        if (Available) _host.Clear(this);
    }

    internal void ForgetDisplay() { _displayText = null; ExpiresAt = 0; }
    private bool Available => !_disposed && _host.IsAvailable;

    internal void InstallGlobal()
    {
        CheckOwner(_owner);
        if (_disposed || !_host.IsAvailable) throw new ObjectDisposedException(nameof(ScriptOutput));
        using var scope = _owner.MemoryBudget?.EnterScope();
        if (!_owner.Globals.Get("Output").IsNil()) throw new InvalidOperationException("Output is already installed.");
        var descriptor = MemoryBudget.Allocate(256, () => new Descriptor(this));
        _owner.Globals.Set("Output", UserData.Create(this, descriptor));
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Script output must be disposed on its runtime's owning thread.");
        if (!_host.IsAvailable)
        {
            _disposed = true;
            ForgetDisplay();
            return;
        }
        _disposed = true;
        _host.Clear(this);
    }

    private void CheckOwner(Script script)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || !ReferenceEquals(script, _owner))
            throw new ScriptRuntimeException("Output belongs to another session or thread.");
        script.ExecutionBudget?.Check();
        script.MemoryBudget?.Check();
    }

    private static bool IsValidText(string? text, bool display)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxTextBytes) return false;
        var lines = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (display && c == '\n') { if (++lines > MaxLines) return false; continue; }
            var category = CharUnicodeInfo.GetUnicodeCategory(text, i);
            if (char.IsControl(c) || category is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                return false;
        }
        try { return Utf8.GetByteCount(text) <= MaxTextBytes; }
        catch (EncoderFallbackException) { return false; }
    }

    // Unlike the general object binder, this primitive-only boundary validates
    // borrowed Lua strings and duration numbers before any copy or rate charge.
    private sealed class Descriptor : IUserDataDescriptor
    {
        private readonly ScriptOutput _output;
        private DynValue? _log, _show, _clear;
        internal Descriptor(ScriptOutput output) { _output = output; }
        public string Name => "ScriptOutput";
        public Type Type => typeof(ScriptOutput);
        public DynValue Index(Script script, object obj, DynValue index, bool isDirectIndexing)
        {
            Check(script, obj);
            if (index.Type != DataType.String || index.String is not ("Log" or "ShowText" or "ClearText")) return DynValue.Nil;
            ref var cached = ref (index.String == "Log" ? ref _log : ref (index.String == "ShowText" ? ref _show : ref _clear));
            if (cached != null) return cached;
            using var scope = script.MemoryBudget?.EnterScope();
            var operation = MemoryBudget.Allocate(128, () => new Operation(this, index.String));
            var callback = MemoryBudget.Allocate(96, () => new Func<ScriptExecutionContext, CallbackArguments, DynValue>(operation.Invoke));
            return cached = DynValue.NewCallback(callback, index.String == "Log" ? "Output.Log" :
                index.String == "ShowText" ? "Output.ShowText" : "Output.ClearText");
        }
        public bool SetIndex(Script script, object obj, DynValue index, DynValue value, bool isDirectIndexing)
        { Check(script, obj); throw new ScriptRuntimeException("Output is read-only."); }
        public string AsString(object obj) { Check(_output._owner, obj); return "[script Output]"; }
        public DynValue MetaIndex(Script script, object obj, string metaname) { Check(script, obj); return null!; }
        public bool IsTypeCompatible(Type type, object obj) => false;
        private void Check(Script script, object obj)
        {
            _output.CheckOwner(script);
            if (!ReferenceEquals(obj, _output)) throw new ScriptRuntimeException("Invalid output receiver.");
        }

        private sealed class Operation
        {
            private readonly Descriptor _descriptor;
            private readonly string _name;
            internal Operation(Descriptor descriptor, string name) { _descriptor = descriptor; _name = name; }
            internal DynValue Invoke(ScriptExecutionContext context, CallbackArguments arguments)
            {
                var output = _descriptor._output;
                _descriptor.Check(context.GetScript(), output);
                var offset = arguments.IsMethodCall ? 1 : 0;
                var count = _name == "ClearText" ? 0 : _name == "Log" ? 1 : 2;
                if (arguments.Count != count + offset || (offset == 1 && (arguments[0].Type != DataType.UserData ||
                    !ReferenceEquals(arguments[0].UserData.Descriptor, _descriptor) ||
                    !ReferenceEquals(arguments[0].UserData.Object, output)))) return DynValue.False;
                if (_name == "ClearText") { output.ClearText(); return DynValue.Nil; }
                if (arguments[offset].Type != DataType.String) return DynValue.False;
                if (_name == "Log") return output.Log(arguments[offset].String) ? DynValue.True : DynValue.False;
                if (arguments[offset + 1].Type != DataType.Number) return DynValue.False;
                return output.ShowText(arguments[offset].String, arguments[offset + 1].Number) ? DynValue.True : DynValue.False;
            }
        }
    }
}
