// The generated game's public NullableAttribute has IL2CPP array constructors.
// Supply CLR metadata attributes locally so nullable annotations in this diagnostic
// assembly do not bind to those incompatible generated types.
namespace System.Runtime.CompilerServices;

[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class NullableAttribute : Attribute
{
    public readonly byte[] NullableFlags;
    public NullableAttribute(byte flag) => NullableFlags = new[] { flag };
    public NullableAttribute(byte[] flags) => NullableFlags = flags;
}

[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class NullableContextAttribute : Attribute
{
    public readonly byte Flag;
    public NullableContextAttribute(byte flag) => Flag = flag;
}
