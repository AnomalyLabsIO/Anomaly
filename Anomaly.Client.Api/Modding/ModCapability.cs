using System;

namespace Anomaly.Client.Api.Modding;

/// <summary>
/// Capabilities declared by a module for review and framework-facade policy.
/// These flags are not an operating-system sandbox; approved in-process code
/// retains the full trust of the game process.
/// </summary>
[Flags]
public enum ModCapability
{
    None = 0,
    Commands = 1 << 0,
    Configuration = 1 << 1,
    Persistence = 1 << 2,
    Scheduling = 1 << 3,
    Input = 1 << 4,
    Ui = 1 << 5,
    Networking = 1 << 6,
    FileSystem = 1 << 7,
    NativeInterop = 1 << 8,
    ExternalNetwork = 1 << 9,
}
