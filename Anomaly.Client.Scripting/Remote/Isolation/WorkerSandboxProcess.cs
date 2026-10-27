using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Anomaly.Client.Scripting.Remote.Isolation;

internal sealed class WorkerSandboxProcess : IDisposable
{
    private static readonly object CleanupGate = new();
    private static readonly HashSet<Task> CleanupTasks = new();
    private readonly SafeFileHandle _process;
    private readonly SafeFileHandle _job;
    private readonly WorkerSandboxAccess _access;
    private readonly SafeFileHandle[] _pipes;
    private readonly object _leaseGate = new();
    private readonly List<IDisposable> _leases = new();
    private int _disposed;
    private int? _exitCode;
    public Stream Input { get; }
    public Stream Output { get; }
    public Stream Error { get; }
    public int ProcessId { get; }
    public WorkerIsolationEvidence Evidence { get; }

    private WorkerSandboxProcess(SafeFileHandle process, SafeFileHandle job, WorkerSandboxAccess access,
        int processId, Stream input, Stream output, Stream error)
    {
        _process = process;
        _job = job;
        _access = access;
        ProcessId = processId;
        Input = input;
        Output = output;
        Error = error;
        _pipes = new[] { ((FileStream)input).SafeFileHandle, ((FileStream)output).SafeFileHandle, ((FileStream)error).SafeFileHandle };
        Evidence = new WorkerIsolationEvidence(job);
    }

    public bool HasExited => ExitCode.HasValue;
    public int? ExitCode
    {
        get
        {
            if (_exitCode.HasValue) return _exitCode;
            if (_process.IsClosed) return null;
            try
            {
                var wait = WorkerSandboxNative.WaitForSingleObject(_process, 0);
                if (wait == 0x102) return null;
                if (wait != 0) throw LastError();
                Check(WorkerSandboxNative.GetExitCodeProcess(_process, out var code));
                _exitCode = unchecked((int)code);
                return _exitCode;
            }
            catch (ObjectDisposedException) { return _exitCode; }
        }
    }

    // The caller runs Start on its launch task, never on the game thread.
    public static WorkerSandboxProcess Start(WorkerSandboxOptions options)
    {
        if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
            throw new PlatformNotSupportedException("Remote Lua requires Windows x64 AppContainer isolation.");
        ArgumentNullException.ThrowIfNull(options);
        var executable = WorkerSandboxAccess.FullPath(options.DotnetExecutable);
        var assembly = WorkerSandboxAccess.FullPath(options.WorkerAssembly);
        if (executable.Contains('"') || assembly.Contains('"')) throw new ArgumentException("Invalid worker launch path.");
        if (!File.Exists(executable)) throw new FileNotFoundException("The trusted runtime is missing.", executable);
        WorkerSandboxAccess? access = null;
        SafeFileHandle? job = null, process = null, thread = null;
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null, errorRead = null, errorWrite = null;
        Stream? input = null, output = null, error = null;
        IntPtr attributes = IntPtr.Zero, handles = IntPtr.Zero, capabilities = IntPtr.Zero, environment = IntPtr.Zero, packagePolicy = IntPtr.Zero;
        var initialized = false;
        var transferred = false;
        try
        {
            access = new WorkerSandboxAccess(options);
            job = ConfigureJob();
            var security = new WorkerSandboxNative.SecurityAttributes
            {
                Length = Marshal.SizeOf<WorkerSandboxNative.SecurityAttributes>(), InheritHandle = true
            };
            // Each kernel pipe has a fixed buffer; stderr is never mixed with protocol output.
            Check(WorkerSandboxNative.CreatePipe(out inputRead, out inputWrite, ref security, 4096));
            Check(WorkerSandboxNative.CreatePipe(out outputRead, out outputWrite, ref security, 4096));
            Check(WorkerSandboxNative.CreatePipe(out errorRead, out errorWrite, ref security, 4096));
            Check(WorkerSandboxNative.SetHandleInformation(inputWrite, 1, 0));
            Check(WorkerSandboxNative.SetHandleInformation(outputRead, 1, 0));
            Check(WorkerSandboxNative.SetHandleInformation(errorRead, 1, 0));

            UIntPtr size = UIntPtr.Zero;
            WorkerSandboxNative.InitializeProcThreadAttributeList(IntPtr.Zero, 3, 0, ref size);
            if (size == UIntPtr.Zero) throw LastError("InitializeProcThreadAttributeList sizing");
            attributes = Marshal.AllocHGlobal(checked((int)size.ToUInt64()));
            Check(WorkerSandboxNative.InitializeProcThreadAttributeList(attributes, 3, 0, ref size));
            initialized = true;
            handles = Marshal.AllocHGlobal(IntPtr.Size * 3);
            Marshal.WriteIntPtr(handles, 0, inputRead.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size, outputWrite.DangerousGetHandle());
            Marshal.WriteIntPtr(handles, IntPtr.Size * 2, errorWrite.DangerousGetHandle());
            Check(WorkerSandboxNative.UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x20002,
                handles, (UIntPtr)(IntPtr.Size * 3), IntPtr.Zero, IntPtr.Zero));
            capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<WorkerSandboxNative.SecurityCapabilities>());
            Marshal.StructureToPtr(new WorkerSandboxNative.SecurityCapabilities { AppContainerSid = access.Sid }, capabilities, false);
            Check(WorkerSandboxNative.UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x20009,
                capabilities, (UIntPtr)Marshal.SizeOf<WorkerSandboxNative.SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero));
            // A regular AppContainer still accepts ALL_APPLICATION_PACKAGES grants,
            // including explicit/protected descendant ACLs beneath a denied UserData root.
            // LPAC opts out of that ambient authority; unsupported systems fail closed.
            packagePolicy = Marshal.AllocHGlobal(sizeof(uint));
            Marshal.WriteInt32(packagePolicy, 1);
            Check(WorkerSandboxNative.UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x2000F,
                packagePolicy, (UIntPtr)sizeof(uint), IntPtr.Zero, IntPtr.Zero));
            environment = Marshal.StringToHGlobalUni(CreateEnvironment(access.TemporaryDirectory));
            var startup = new WorkerSandboxNative.StartupInfoEx
            {
                StartupInfo = new WorkerSandboxNative.StartupInfo
                {
                    Size = Marshal.SizeOf<WorkerSandboxNative.StartupInfoEx>(), Flags = 0x100,
                    StandardInput = inputRead.DangerousGetHandle(), StandardOutput = outputWrite.DangerousGetHandle(),
                    StandardError = errorWrite.DangerousGetHandle()
                },
                AttributeList = attributes
            };
            // No user code can execute until job assignment AND all independent queries pass.
            Check(WorkerSandboxNative.CreateProcess(executable, new StringBuilder("\"" + executable + "\" \"" + assembly + "\""),
                IntPtr.Zero, IntPtr.Zero, true, 0x4 | 0x400 | 0x80000 | 0x08000000,
                environment, access.TemporaryDirectory, ref startup, out var created));
            process = new SafeFileHandle(created.Process, true);
            thread = new SafeFileHandle(created.Thread, true);
            Check(WorkerSandboxNative.AssignProcessToJobObject(job, process));
            VerifyJob(job, process);
            VerifyToken(process, access.Sid);
            access.RecordProcess(checked((int)created.ProcessId));

            inputRead.Dispose(); inputRead = null;
            outputWrite.Dispose(); outputWrite = null;
            errorWrite.Dispose(); errorWrite = null;
            input = new FileStream(inputWrite, FileAccess.Write, 1, false); inputWrite = null;
            output = new FileStream(outputRead, FileAccess.Read, 1, false); outputRead = null;
            error = new FileStream(errorRead, FileAccess.Read, 1, false); errorRead = null;
            var worker = new WorkerSandboxProcess(process, job, access, checked((int)created.ProcessId), input, output, error);
            if (WorkerSandboxNative.ResumeThread(thread) != 1)
                throw new InvalidOperationException("The isolated worker did not have exactly one suspend count.");
            transferred = true;
            return worker;
        }
        finally
        {
            thread?.Dispose();
            if (initialized) WorkerSandboxNative.DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (handles != IntPtr.Zero) Marshal.FreeHGlobal(handles);
            if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
            if (packagePolicy != IntPtr.Zero) Marshal.FreeHGlobal(packagePolicy);
            if (environment != IntPtr.Zero) Marshal.FreeHGlobal(environment);
            inputRead?.Dispose(); inputWrite?.Dispose(); outputRead?.Dispose(); outputWrite?.Dispose(); errorRead?.Dispose(); errorWrite?.Dispose();
            if (!transferred)
            {
                if (process != null && !process.IsInvalid) WorkerSandboxNative.TerminateProcess(process, 1);
                if (job != null && !job.IsInvalid) WorkerSandboxNative.TerminateJobObject(job, 1);
                job?.Dispose();
                var failedProcess = process;
                var failedAccess = access;
                var failedInput = input;
                var failedOutput = output;
                var failedError = error;
                ScheduleCleanup(() =>
                {
                    try
                    {
                        if (failedProcess != null && !failedProcess.IsInvalid)
                            WorkerSandboxNative.WaitForSingleObject(failedProcess, uint.MaxValue);
                        try { failedInput?.Dispose(); }
                        finally
                        {
                            try { failedOutput?.Dispose(); }
                            finally
                            {
                                try { failedError?.Dispose(); }
                                finally { DisposeAccess(failedAccess); }
                            }
                        }
                    }
                    finally { failedProcess?.Dispose(); }
                });
            }
        }
    }

    private static SafeFileHandle ConfigureJob()
    {
        var job = WorkerSandboxNative.CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); throw LastError(); }
        try
        {
            var limits = new WorkerSandboxNative.ExtendedLimitInformation
            {
                BasicLimitInformation = new WorkerSandboxNative.BasicLimitInformation
                { LimitFlags = WorkerSandboxNative.JobFlags, ActiveProcessLimit = 1 },
                ProcessMemoryLimit = (UIntPtr)WorkerSandboxNative.MemoryLimit,
                JobMemoryLimit = (UIntPtr)WorkerSandboxNative.MemoryLimit
            };
            Check(WorkerSandboxNative.SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<WorkerSandboxNative.ExtendedLimitInformation>()));
            var cpu = new WorkerSandboxNative.CpuRateInformation { ControlFlags = WorkerSandboxNative.CpuFlags, CpuRate = 1000 };
            Check(WorkerSandboxNative.SetInformationJobObject(job, 15, ref cpu, (uint)Marshal.SizeOf<WorkerSandboxNative.CpuRateInformation>()));
            var ui = WorkerSandboxNative.UiFlags;
            Check(WorkerSandboxNative.SetInformationJobObject(job, 4, ref ui, sizeof(uint)));
            return job;
        }
        catch { job.Dispose(); throw; }
    }

    private static void VerifyJob(SafeFileHandle job, SafeFileHandle process)
    {
        Check(WorkerSandboxNative.IsProcessInJob(process, job, out var assigned));
        Check(WorkerSandboxNative.QueryInformationJobObject(job, 9, out WorkerSandboxNative.ExtendedLimitInformation limits,
            (uint)Marshal.SizeOf<WorkerSandboxNative.ExtendedLimitInformation>(), IntPtr.Zero));
        Check(WorkerSandboxNative.QueryInformationJobObject(job, 15, out WorkerSandboxNative.CpuRateInformation cpu,
            (uint)Marshal.SizeOf<WorkerSandboxNative.CpuRateInformation>(), IntPtr.Zero));
        Check(WorkerSandboxNative.QueryInformationJobObject(job, 4, out uint ui, sizeof(uint), IntPtr.Zero));
        if (!assigned || limits.BasicLimitInformation.LimitFlags != WorkerSandboxNative.JobFlags ||
            limits.BasicLimitInformation.ActiveProcessLimit != 1 || limits.ProcessMemoryLimit.ToUInt64() != WorkerSandboxNative.MemoryLimit ||
            limits.JobMemoryLimit.ToUInt64() != WorkerSandboxNative.MemoryLimit || cpu.ControlFlags != WorkerSandboxNative.CpuFlags ||
            cpu.CpuRate != 1000 || ui != WorkerSandboxNative.UiFlags)
            throw new InvalidOperationException("The worker job did not retain the required hard isolation limits.");
    }

    private static void VerifyToken(SafeFileHandle process, IntPtr expectedSid)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Worker token verification requires Windows.");
        Check(WorkerSandboxNative.OpenProcessToken(process, 0x8 | 0x2, out var token));
        using (token)
        {
            var buffer = Marshal.AllocHGlobal(4096);
            try
            {
                Check(WorkerSandboxNative.GetTokenInformation(token, 29, buffer, 4096, out _));
                if (Marshal.ReadInt32(buffer) != 1) throw new InvalidOperationException("Worker token is not an AppContainer.");
                Check(WorkerSandboxNative.GetTokenInformation(token, 31, buffer, 4096, out _));
                if (!WorkerSandboxNative.EqualSid(Marshal.ReadIntPtr(buffer), expectedSid))
                    throw new InvalidOperationException("Worker token belongs to a different AppContainer.");
                Check(WorkerSandboxNative.GetTokenInformation(token, 30, buffer, 4096, out _));
                if (Marshal.ReadInt32(buffer) != 0) throw new InvalidOperationException("Worker token unexpectedly has capabilities.");
                WorkerSandboxTokenVerifier.VerifyLeastPrivilege(token, expectedSid);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        VerifyNoLoopbackExemption(expectedSid);
    }

    private static void VerifyNoLoopbackExemption(IntPtr expectedSid)
    {
        var error = WorkerSandboxNative.NetworkIsolationGetAppContainerConfig(out var count, out var entries);
        var stride = Marshal.SizeOf<WorkerSandboxNative.SidAndAttributes>();
        try
        {
            if (error != 0) throw new Win32Exception(checked((int)error));
            if (count != 0 && entries == IntPtr.Zero)
                throw new IOException("The network isolation configuration returned no SID array.");
            for (uint index = 0; index < count; index++)
            {
                var entry = Marshal.PtrToStructure<WorkerSandboxNative.SidAndAttributes>(
                    IntPtr.Add(entries, checked((int)(index * (uint)stride))));
                if (entry.Sid == IntPtr.Zero || WorkerSandboxNative.EqualSid(entry.Sid, expectedSid))
                    throw new InvalidOperationException("The worker AppContainer has a loopback exemption or invalid isolation configuration.");
            }
        }
        finally
        {
            // FirewallAPI allocates each SID AND the outer array on the process heap.
            // LocalFree/FreeSid are not interchangeable with this API's ownership.
            if (entries != IntPtr.Zero)
            {
                var heap = WorkerSandboxNative.GetProcessHeap();
                for (uint index = 0; index < count; index++)
                {
                    var entry = Marshal.PtrToStructure<WorkerSandboxNative.SidAndAttributes>(
                        IntPtr.Add(entries, checked((int)(index * (uint)stride))));
                    if (entry.Sid != IntPtr.Zero) WorkerSandboxNative.HeapFree(heap, 0, entry.Sid);
                }
                WorkerSandboxNative.HeapFree(heap, 0, entries);
            }
        }
    }

    private static string CreateEnvironment(string temporaryDirectory)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SystemRoot"] = windows, ["WINDIR"] = windows,
            ["TEMP"] = temporaryDirectory, ["TMP"] = temporaryDirectory,
            ["APPDATA"] = temporaryDirectory, ["LOCALAPPDATA"] = temporaryDirectory, ["USERPROFILE"] = temporaryDirectory,
            ["DOTNET_EnableDiagnostics"] = "0", ["COMPlus_EnableDiagnostics"] = "0",
            ["DOTNET_GCHeapHardLimit"] = "0x8000000", ["DOTNET_MULTILEVEL_LOOKUP"] = "0",
            ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1"
        };
        return string.Join("\0", values.Select(pair => pair.Key + "=" + pair.Value)) + "\0\0";
    }

    public void Kill()
    {
        if (_job.IsClosed) return;
        try
        {
            if (!WorkerSandboxNative.TerminateJobObject(_job, 1) && !HasExited)
                throw LastError();
        }
        catch (ObjectDisposedException) { }
    }

    internal void Retain(IDisposable lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lock (_leaseGate)
        {
            if (Volatile.Read(ref _disposed) == 0) { _leases.Add(lease); return; }
        }
        lease.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // No process/IO wait or profile/ACL operation is performed on this thread.
        try { Kill(); }
        catch { _job.Dispose(); }
        finally
        {
            foreach (var pipe in _pipes)
            {
                try { WorkerSandboxNative.CancelIoEx(pipe, IntPtr.Zero); }
                catch (ObjectDisposedException) { }
                pipe.Dispose();
            }
            ScheduleCleanup(() =>
            {
                try
                {
                    WorkerSandboxNative.WaitForSingleObject(_process, uint.MaxValue);
                    Check(WorkerSandboxNative.GetExitCodeProcess(_process, out var code));
                    _exitCode = unchecked((int)code);
                    _ = Evidence.PeakProcessMemoryBytes;
                }
                finally
                {
                    _job.Dispose();
                    try { Input.Dispose(); }
                    finally
                    {
                        try { Output.Dispose(); }
                        finally
                        {
                            try { Error.Dispose(); }
                            finally
                            {
                                _process.Dispose();
                                try { DisposeAccess(_access); }
                                finally
                                {
                                    lock (_leaseGate)
                                    {
                                        foreach (var lease in _leases)
                                        {
                                            try { lease.Dispose(); }
                                            catch (Exception) { /* Other owned leases must still be released. */ }
                                        }
                                        _leases.Clear();
                                    }
                                }
                            }
                        }
                    }
                }
            });
        }
    }

    private static void ScheduleCleanup(Action cleanup)
    {
        var task = Task.Run(cleanup);
        lock (CleanupGate) CleanupTasks.Add(task);
        _ = task.ContinueWith(completed =>
        {
            _ = completed.Exception;
            lock (CleanupGate) CleanupTasks.Remove(completed);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal static async Task DrainCleanupAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (CleanupGate)
            {
                if (CleanupTasks.Count == 0) return;
                pending = CleanupTasks.ToArray();
            }
            try { await Task.WhenAll(pending).ConfigureAwait(false); }
            catch (Exception) { /* Failed cleanup retains its bounded recovery journal. */ }
        }
    }

    private static void DisposeAccess(WorkerSandboxAccess? access)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Worker profile and ACL cleanup requires Windows.");
        access?.Dispose();
    }

    private static Win32Exception LastError(string operation = "Worker sandbox native operation")
    {
        var error = Marshal.GetLastWin32Error();
        return new Win32Exception(error, $"{operation} failed (Win32 {error}).");
    }

    private static void Check(bool succeeded, [CallerArgumentExpression("succeeded")] string operation = "")
    {
        if (!succeeded) throw LastError(operation);
    }
}
