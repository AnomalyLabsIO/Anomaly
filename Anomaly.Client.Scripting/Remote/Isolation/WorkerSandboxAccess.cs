using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Anomaly.Client.Scripting.Remote.Isolation;

// The journal is written before ACL edits. Recovery removes only this unique profile SID,
// never restores a stale DACL snapshot over somebody else's changes.
[SupportedOSPlatform("windows")]
internal sealed class WorkerSandboxAccess : IDisposable
{
    private const string ProfilePrefix = "Anomaly.Lua.";
    private const int MaximumEntries = 128;
    private static readonly object Gate = new();
    private readonly string _journalPath;
    private readonly FileStream _lease;
    private readonly Journal _journal;
    private readonly SecurityIdentifier _identity;
    private bool _disposed;
    internal IntPtr Sid { get; private set; }
    internal string TemporaryDirectory { get; }

    public sealed class Entry
    {
        public string Path { get; set; } = "";
        public bool Directory { get; set; }
        public bool Deny { get; set; }
        public bool Temporary { get; set; }
    }

    public sealed class Journal
    {
        public string Profile { get; set; } = "";
        public int ProcessId { get; set; }
        public List<Entry> Entries { get; set; } = new();
    }

    internal WorkerSandboxAccess(WorkerSandboxOptions options)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Worker profile and ACL isolation requires Windows.");
        lock (Gate)
        {
            using var sharedAclLock = new WorkerSandboxAclLock();
            if (options.ProtectedDirectories == null || options.ProtectedDirectories.Count == 0)
                throw new ArgumentException("Worker isolation requires its protected UserData root.");
            var userDataRoot = FullPath(options.ProtectedDirectories[0]);
            if (!Directory.Exists(userDataRoot)) throw new DirectoryNotFoundException(userDataRoot);
            if ((File.GetAttributes(userDataRoot) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Worker isolation UserData root is a reparse point.");
            var journalParent = Path.Combine(userDataRoot, "WorkerIsolation");
            Directory.CreateDirectory(journalParent);
            if ((File.GetAttributes(journalParent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Worker isolation journal parent is a reparse point.");
            var root = Path.Combine(journalParent, "Profiles");
            Directory.CreateDirectory(root);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Worker isolation journal directory is a reparse point.");
            Recover(root);
            if (Directory.EnumerateFiles(root, ProfilePrefix + "*.json").Take(64).Count() >= 64)
                throw new IOException("Worker isolation ownership inventory is full.");
            WorkerSandboxProtectedPaths.Validate(options.ProtectedDirectories);
            _journal = new Journal { Profile = ProfilePrefix + Guid.NewGuid().ToString("N") };
            _journalPath = Path.Combine(root, _journal.Profile + ".json");
            // ACL inheritance propagation must be able to open this descendant with
            // MAXIMUM_ALLOWED. Recovery's FileShare.None open still cannot coexist
            // with this live ReadWrite handle because Windows share checks are symmetric.
            _lease = new FileStream(_journalPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            try
            {
                Save();
                var hr = WorkerSandboxNative.CreateAppContainerProfile(_journal.Profile, _journal.Profile,
                    "Anomaly isolated Lua worker", IntPtr.Zero, 0, out var sid);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                Sid = sid;
                _identity = new SecurityIdentifier(Sid);
                hr = WorkerSandboxNative.GetAppContainerFolderPath(_identity.Value, out var folder);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                string profileDirectory;
                try { profileDirectory = Marshal.PtrToStringUni(folder) ?? throw new IOException("Missing AppContainer directory."); }
                finally { Marshal.FreeCoTaskMem(folder); }
                TemporaryDirectory = Path.Combine(profileDirectory, "AnomalyLuaTemp");
                Directory.CreateDirectory(TemporaryDirectory);

                var protectedPaths = options.ProtectedDirectories.Select(FullPath).Append(FullPath(root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach (var path in protectedPaths)
                {
                    if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);
                    Add(path, true, deny: true);
                }
                var files = options.ReadableFiles.Append(options.WorkerAssembly).Select(FullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (files.Length > 32 || protectedPaths.Length > 16)
                    throw new InvalidOperationException("Worker permission inventory exceeds its bound.");
                foreach (var file in files)
                {
                    if (!File.Exists(file)) throw new FileNotFoundException("Worker payload is missing.", file);
                    if (protectedPaths.Any(p => IsWithin(file, p)))
                        throw new InvalidOperationException("A worker payload is inside a protected directory.");
                    Add(file, false);
                }
                Add(TemporaryDirectory, true, temporary: true);
                Save();
                foreach (var entry in _journal.Entries) Apply(entry, _identity);
            }
            catch
            {
                Dispose();
                throw;
            }
        }
    }

    internal void RecordProcess(int processId)
    {
        _journal.ProcessId = processId;
        Save();
    }

    internal static string FullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Worker isolation paths must be absolute.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsWithin(string path, string directory) => path.Equals(directory, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);


    private void Add(string path, bool directory, bool deny = false, bool temporary = false)
    {
        if (_journal.Entries.Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
        if (_journal.Entries.Count == MaximumEntries) throw new InvalidOperationException("Worker ACL inventory exceeds its bound.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Worker isolation refuses reparse-point permission targets.");
        var entry = new Entry { Path = path, Directory = directory, Deny = deny, Temporary = temporary };
        if (ReadAcl(entry).GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                .Any(rule => rule.IdentityReference.Equals(_identity)))
            throw new InvalidOperationException("A fresh worker SID already has explicit permissions.");
        _journal.Entries.Add(entry);
    }

    private void Save()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_journal);
        if (bytes.Length > 65536) throw new InvalidOperationException("Worker ACL journal exceeds its bound.");
        // Append committed snapshots so a crash during the PID update cannot destroy
        // the permission inventory that was flushed before any ACL was changed.
        if (_lease.Length + bytes.Length + 1 > 256 * 1024)
            throw new InvalidOperationException("Worker ACL journal exceeds its bound.");
        _lease.Position = _lease.Length;
        _lease.Write(bytes);
        _lease.WriteByte((byte)'\n');
        _lease.Flush(true);
    }

    private static FileSystemSecurity ReadAcl(Entry entry) => ReadAclForPath(entry.Path, entry.Directory, "read");

    internal static FileSystemSecurity ReadAclForPath(string path, bool directory, string stage)
    {
        try
        {
            return directory ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is Win32Exception)
        {
            throw AclFailure(stage, path, exception);
        }
    }

    private static IOException AclFailure(string stage, string path, Exception exception)
    {
        var code = exception is Win32Exception native ? "Win32 " + native.NativeErrorCode : "HRESULT 0x" + exception.HResult.ToString("X8");
        return new IOException($"Worker ACL {stage} failed for '{path}' ({code}): {exception.Message}", exception);
    }

    private static void WriteAcl(Entry entry, FileSystemSecurity acl)
    {
        try { WriteAclCore(entry, acl); }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is Win32Exception)
        {
            throw AclFailure(entry.Deny ? "protected deny write" : entry.Temporary ? "private temp write" : "payload write", entry.Path, exception);
        }
    }

    private static void WriteAclCore(Entry entry, FileSystemSecurity acl)
    {
        if (entry.Directory)
        {
            if (!entry.Deny && !entry.Temporary)
                throw new InvalidOperationException("Worker isolation must not mutate ancestor directory permissions.");
            new DirectoryInfo(entry.Path).SetAccessControl((DirectorySecurity)acl);
        }
        else new FileInfo(entry.Path).SetAccessControl((FileSecurity)acl);
    }

    private static void Apply(Entry entry, SecurityIdentifier identity)
    {
        if (entry.Directory && !entry.Deny && !entry.Temporary)
            throw new InvalidOperationException("Worker launch must not mutate ancestor directory permissions.");
        var acl = ReadAcl(entry);
        if (acl.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(identity)))
            throw new InvalidOperationException("A fresh worker SID already has explicit permissions.");
        acl.AddAccessRule(PrimaryRule(entry, identity));
        if (!entry.Deny && !entry.Temporary) acl.AddAccessRule(ReadOnlyRule(entry, identity));
        WriteAcl(entry, acl);
    }

    private static FileSystemAccessRule PrimaryRule(Entry entry, SecurityIdentifier identity)
    {
        var rights = entry.Deny ? FileSystemRights.FullControl : entry.Temporary ? FileSystemRights.Modify
            : entry.Directory ? FileSystemRights.Traverse | FileSystemRights.ReadAttributes : FileSystemRights.ReadAndExecute;
        var inherit = entry.Deny || entry.Temporary ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        return new FileSystemAccessRule(identity, rights, inherit, PropagationFlags.None,
            entry.Deny ? AccessControlType.Deny : AccessControlType.Allow);
    }

    private static FileSystemAccessRule ReadOnlyRule(Entry entry, SecurityIdentifier identity)
    {
        // ALL_APPLICATION_PACKAGES grants must not turn explicit payload/traversal
        // access into writes, ACL changes, or deletion of an executable child.
        var denied = FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (entry.Directory) denied |= FileSystemRights.DeleteSubdirectoriesAndFiles;
        return new FileSystemAccessRule(identity, denied, InheritanceFlags.None,
            PropagationFlags.None, AccessControlType.Deny);
    }

    private static bool Clean(Journal journal)
    {
        if (!journal.Profile.StartsWith(ProfilePrefix, StringComparison.Ordinal) ||
            !Guid.TryParseExact(journal.Profile.Substring(ProfilePrefix.Length), "N", out _) || journal.Entries.Count > MaximumEntries)
            return false;
        var hr = WorkerSandboxNative.DeriveAppContainerSidFromAppContainerName(journal.Profile, out var sid);
        if (hr != 0) return false;
        var succeeded = true;
        try
        {
            var identity = new SecurityIdentifier(sid);
            foreach (var entry in journal.Entries)
            {
                try
                {
                    if (!Path.IsPathFullyQualified(entry.Path)) { succeeded = false; continue; }
                    if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path)) continue;
                    if ((File.GetAttributes(entry.Path) & FileAttributes.ReparsePoint) != 0) { succeeded = false; continue; }
                    var acl = ReadAcl(entry);
                    if (!acl.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                            .Any(rule => rule.IdentityReference.Equals(identity))) continue;
                    acl.RemoveAccessRuleSpecific(PrimaryRule(entry, identity));
                    if (!entry.Deny && !entry.Temporary)
                        acl.RemoveAccessRuleSpecific(ReadOnlyRule(entry, identity));
                    WriteAcl(entry, acl);
                }
                catch (Exception) { succeeded = false; }
            }
            if (succeeded)
            {
                hr = WorkerSandboxNative.DeleteAppContainerProfile(journal.Profile);
                // An interrupted creation can leave only the journal, not a profile.
                succeeded = hr == 0 || hr == unchecked((int)0x80070002);
            }
            return succeeded;
        }
        finally { WorkerSandboxNative.FreeSid(sid); }
    }

    private static void Recover(string root)
    {
        var files = Directory.EnumerateFiles(root, ProfilePrefix + "*.json").Take(65).ToArray();
        if (files.Length > 64) throw new IOException("Worker isolation recovery inventory is full.");
        foreach (var path in files)
        {
            var remove = false;
            try
            {
                using var lease = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                if (lease.Length > 256 * 1024) continue;
                using var reader = new StreamReader(lease, System.Text.Encoding.UTF8, true, 1024, true);
                var snapshots = reader.ReadToEnd().Split('\n');
                Journal? journal = null;
                for (var index = 0; index < snapshots.Length - 1 && index < 4; index++)
                {
                    if (snapshots[index].Length > 65536) break;
                    var snapshot = JsonSerializer.Deserialize<Journal>(snapshots[index]);
                    if (snapshot == null) break;
                    journal = snapshot;
                }
                if (journal != null && Path.GetFileName(path) == journal.Profile + ".json")
                {
                    // PID reuse can delay recovery, but must never permit cleanup of a live worker.
                    if (journal.ProcessId > 0)
                    {
                        try
                        {
                            using var process = Process.GetProcessById(journal.ProcessId);
                            if (!process.HasExited) continue;
                        }
                        catch (ArgumentException) { }
                    }
                    remove = Clean(journal);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (JsonException) { }
            if (remove) File.Delete(path);
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            var clean = false;
            try
            {
                using var sharedAclLock = new WorkerSandboxAclLock();
                clean = Clean(_journal);
            }
            finally
            {
                // On mutex/ACL failure keep the journal for a later recovery, but
                // never retain native SID memory or the exclusive journal lease.
                if (Sid != IntPtr.Zero) { WorkerSandboxNative.FreeSid(Sid); Sid = IntPtr.Zero; }
                _lease.Dispose();
                if (clean) File.Delete(_journalPath);
            }
        }
    }
}
