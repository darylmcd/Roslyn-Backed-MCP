using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Deletes an owned directory tree without ever traversing a reparse point. Directory junctions,
/// directory symlinks and file symlinks are removed as links; their targets are never enumerated
/// or mutated. <see cref="Directory.Delete(string, bool)"/> with <c>recursive: true</c> is not an
/// acceptable substitute: on .NET 10 it throws <see cref="UnauthorizedAccessException"/> on a
/// descendant junction (leaking the root), and it enumerates children by path after a separate
/// existence check.
/// </summary>
/// <remarks>
/// The walk is iterative, postorder, one physical level at a time (no recursive managed descent,
/// so deep owned trees cannot exhaust the stack). On Windows each directory is held open (opened
/// as a reparse point, delete-sharing denied) while its children are enumerated and queued, so a
/// concurrent rename/delete/replace of that directory fails instead of redirecting the walk.
/// </remarks>
internal static class NoFollowDirectoryDeleter
{
    /// <summary>
    /// Deletes <paramref name="path"/> and everything beneath it. A reparse point at
    /// <paramref name="path"/> itself is removed as a link. No-op when the path does not exist.
    /// Failures propagate to the caller's retry/log path.
    /// </summary>
    /// <param name="beforeEnumerate">
    /// Test seam: invoked with a directory's path after it is classified (and, on Windows, held)
    /// and immediately before its children are enumerated.
    /// </param>
    internal static void Delete(
        string path,
        CancellationToken ct,
        Action<string>? beforeEnumerate = null,
        ILogger? logger = null)
    {
        if (!Path.Exists(path))
        {
            return;
        }

        var pending = new Stack<(string Path, bool Enumerated)>();
        pending.Push((path, false));
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(current.Path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(current.Path);
                else File.Delete(current.Path);
            }
            else if ((attributes & FileAttributes.Directory) == 0)
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(current.Path, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(current.Path);
            }
            else if (current.Enumerated)
            {
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(current.Path, attributes & ~FileAttributes.ReadOnly);
                }

                Directory.Delete(current.Path);
            }
            else
            {
                pending.Push((current.Path, true));
                using var hold = TryHoldOwnedDirectory(current.Path, logger);
                if (OperatingSystem.IsWindows() && hold is null)
                {
                    throw new IOException("Could not hold a directory for no-follow deletion; it may be a replaced reparse point.");
                }

                beforeEnumerate?.Invoke(current.Path);
                foreach (var entry in Directory.EnumerateFileSystemEntries(current.Path))
                {
                    pending.Push((entry, false));
                }
            }
        }
    }

    /// <summary>
    /// Open the path itself, rather than its reparse target, and deny delete sharing for the
    /// duration of path-based traversal. A competing rename/delete cannot replace a verified
    /// directory while the caller enumerates children or deletes entries beneath it. Returns null
    /// on non-Windows platforms, or when the path is a reparse point or cannot be opened.
    /// </summary>
    internal static SafeFileHandle? TryHoldOwnedDirectory(string path, ILogger? logger)
    {
        if (!OperatingSystem.IsWindows()) return null;
        return TryHoldOwnedDirectoryWindows(path, logger);
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? TryHoldOwnedDirectoryWindows(string path, ILogger? logger)
    {
        // A zero-access handle never participates in sharing checks, so it would not deny a
        // concurrent delete/rename. List + read-attributes access makes the missing FILE_SHARE_DELETE
        // bite: other opens that need DELETE fail while we hold the directory.
        const uint desiredAccess = 0x1 | 0x80;
        const uint fileFlagBackupSemantics = 0x02000000;
        const uint fileFlagOpenReparsePoint = 0x00200000;
        const uint openExisting = 3;
        const uint fileShareReadWrite = 0x1 | 0x2;
        const uint fileAttributeDirectory = 0x10;
        const uint fileAttributeReparsePoint = 0x400;
        const int fileAttributeTagInfo = 9;

        var handle = CreateFileW(path, desiredAccess, fileShareReadWrite, IntPtr.Zero, openExisting,
            fileFlagBackupSemantics | fileFlagOpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            logger?.LogDebug("Could not hold an analyzer shadow directory; Win32 error {ErrorCode}.", Marshal.GetLastPInvokeError());
            handle.Dispose();
            return null;
        }

        if (!GetFileInformationByHandleEx(handle, fileAttributeTagInfo, out var info,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()) ||
            (info.FileAttributes & (fileAttributeDirectory | fileAttributeReparsePoint)) != fileAttributeDirectory)
        {
            logger?.LogDebug("An analyzer shadow directory is a reparse point or cannot be inspected; skipping it.");
            handle.Dispose();
            return null;
        }

        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass,
        out FileAttributeTagInfo fileInformation, uint bufferSize);
}
