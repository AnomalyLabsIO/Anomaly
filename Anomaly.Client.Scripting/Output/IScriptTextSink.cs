namespace Anomaly.Client.Scripting.Output;

/// <summary>Main-thread-only host boundary. Slots are private to one ScriptOutputHost.</summary>
internal interface IScriptTextSink : IDisposable
{
    // A failed Set must leave the slot empty, including after partial native creation.
    bool Set(int slot, string plainText);
    void Clear(int slot);
}
