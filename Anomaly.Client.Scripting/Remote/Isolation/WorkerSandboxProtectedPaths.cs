using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Anomaly.Client.Scripting.Remote.Isolation;

[SupportedOSPlatform("windows")]
internal static class WorkerSandboxProtectedPaths
{
    internal static void Validate(IReadOnlyList<string> roots)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Worker protected-file validation requires Windows.");
        if (roots.Count > 16) throw new InvalidOperationException("Worker protected directory inventory exceeds its bound.");
        var clock = Stopwatch.StartNew();
        var count = 0;
        var allPackages = new SecurityIdentifier("S-1-15-2-1");
        var restrictedPackages = new SecurityIdentifier("S-1-15-2-2");
        const FileSystemRights unsafeRights = FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        var pending = new Stack<(IEnumerator<string> Entries, int Depth)>();
        try
        {
            foreach (var suppliedRoot in roots)
            {
                var root = WorkerSandboxAccess.FullPath(suppliedRoot);
                if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Worker protected directory is a reparse point.");
                pending.Push((Directory.EnumerateFileSystemEntries(root).GetEnumerator(), 1));
                while (pending.Count != 0)
                {
                    if (clock.Elapsed > TimeSpan.FromSeconds(5))
                        throw new TimeoutException("Worker protected-file ACL validation exceeded its work deadline.");
                    var current = pending.Peek();
                    if (!current.Entries.MoveNext()) { current.Entries.Dispose(); pending.Pop(); continue; }
                    if (++count > 65536) throw new InvalidOperationException("Worker protected-file inventory exceeds its bound.");
                    if (current.Depth > 64) throw new InvalidOperationException("Worker protected-file inventory exceeds its depth bound.");
                    var path = current.Entries.Current;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Worker protected-file inventory contains a reparse point.");
                    var directory = (attributes & FileAttributes.Directory) != 0;
                    var acl = WorkerSandboxAccess.ReadAclForPath(path, directory, "protected descendant scan");
                    // Explicit package grants precede an ancestor's inherited deny. A protected
                    // DACL can also preserve inherited grants while blocking our new root deny.
                    // Protected owner-only DACLs need no modification and remain acceptable.
                    foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, acl.AreAccessRulesProtected, typeof(SecurityIdentifier)))
                    {
                        if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & unsafeRights) != 0 &&
                            (rule.IdentityReference.Equals(allPackages) || rule.IdentityReference.Equals(restrictedPackages)))
                            throw new InvalidOperationException("A protected descendant grants ambient application-package file access.");
                    }
                    if (directory)
                    {
                        pending.Push((Directory.EnumerateFileSystemEntries(path).GetEnumerator(), current.Depth + 1));
                    }
                }
            }
            if (clock.Elapsed > TimeSpan.FromSeconds(5))
                throw new TimeoutException("Worker protected-file ACL validation exceeded its work deadline.");
        }
        finally
        {
            while (pending.Count != 0) pending.Pop().Entries.Dispose();
        }
    }
}
