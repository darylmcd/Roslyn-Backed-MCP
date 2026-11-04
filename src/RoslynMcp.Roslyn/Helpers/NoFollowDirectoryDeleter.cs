using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Deletes an owned directory tree without ever traversing a reparse point. Directory junctions,
/// directory symlinks and file symlinks are removed as links; their targets are never enumerated
/// or mutated. <see cref="Directory.Delete(string, bool)"/> with <c>recursive: true</c> is not an
/// acceptable substitute: on .NET 10 the first attempt throws <see cref="UnauthorizedAccessException"/>
/// on a descendant junction (a single-shot caller leaks the root), and it walks by path with no hold
/// on the directories being walked.
/// </summary>
/// <remarks>
/// <para>
/// Windows: the walk is iterative and postorder over a depth-bounded stack of open handles. Every
/// entry is opened once with <c>FILE_FLAG_OPEN_REPARSE_POINT</c> (so a link is opened as the link,
/// never its target), classified from that handle, and mutated through that same handle
/// (<c>FileDispositionInfoEx</c>; the handle also carries the read-only override), so the object
/// that was classified is the object that is deleted. A directory's handle stays open, denying
/// delete/rename sharing, from before its children are enumerated until its own postorder delete.
/// No ancestor of any queued child path can therefore be renamed away and replaced by a junction.
/// Win32 calls use <c>\\?\</c> paths, so depth beyond MAX_PATH works without a long-path manifest.
/// </para>
/// <para>
/// Other platforms: no handle-relative API is used. The walk is path-based
/// (<see cref="File.GetAttributes(string)"/>, then enumerate or delete). A directory swapped to a
/// symlink between classification and enumeration is enumerated through the link, and
/// <see cref="File.SetAttributes(string, FileAttributes)"/> follows a link swapped in after
/// classification. A correct fix needs an fd-based walk (openat/unlinkat with O_NOFOLLOW) through
/// libc interop; that is not implemented here.
/// </para>
/// </remarks>
internal static class NoFollowDirectoryDeleter
{
    private const uint AccessDelete = 0x00010000;
    private const uint FileListDirectory = 0x1;
    private const uint FileReadAttributes = 0x80;
    private const uint FileWriteAttributes = 0x100;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeReadOnly = 0x1;
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint FileAttributeReparsePoint = 0x400;
    private const int FileBasicInfoClass = 0;
    private const int FileDispositionInfoClass = 4;
    private const int FileAttributeTagInfoClass = 9;
    private const int FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1;
    private const uint DispositionIgnoreReadOnly = 0x10;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidParameter = 87;
    private const int ErrorNotSupported = 50;
    private const int ErrorInvalidFunction = 1;

    /// <summary>
    /// Deletes <paramref name="path"/> and everything beneath it. A reparse point at
    /// <paramref name="path"/> itself is removed as a link. No-op when the path does not exist.
    /// Failures propagate to the caller's retry/log path.
    /// </summary>
    /// <param name="beforeEnumerate">
    /// Test seam: invoked with a directory's path after it is classified and held (Windows) and
    /// immediately before its children are enumerated.
    /// </param>
    /// <param name="afterEnumerate">
    /// Test seam: invoked with a directory's path after its children are enumerated and queued,
    /// before any child is processed.
    /// </param>
    /// <param name="beforeMutate">
    /// Test seam: invoked with an entry's path after it is classified and immediately before it is
    /// deleted.
    /// </param>
    internal static void Delete(
        string path,
        CancellationToken ct,
        Action<string>? beforeEnumerate = null,
        ILogger? logger = null,
        Action<string>? afterEnumerate = null,
        Action<string>? beforeMutate = null)
    {
        if (OperatingSystem.IsWindows())
        {
            DeleteWindows(path, ct, beforeEnumerate, afterEnumerate, beforeMutate);
        }
        else
        {
            DeletePortable(path, ct, beforeEnumerate, afterEnumerate, beforeMutate);
        }
    }

    /// <summary>
    /// Open the directory itself, rather than its reparse target, and deny delete sharing for the
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
        var handle = OpenNoFollow(path, FileListDirectory | FileReadAttributes);
        if (handle.IsInvalid)
        {
            logger?.LogDebug("Could not hold an analyzer shadow directory; Win32 error {ErrorCode}.", Marshal.GetLastPInvokeError());
            handle.Dispose();
            return null;
        }

        if (!TryReadAttributes(handle, out var attributes) ||
            (attributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != FileAttributeDirectory)
        {
            logger?.LogDebug("An analyzer shadow directory is a reparse point or cannot be inspected; skipping it.");
            handle.Dispose();
            return null;
        }

        return handle;
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteWindows(
        string path,
        CancellationToken ct,
        Action<string>? beforeEnumerate,
        Action<string>? afterEnumerate,
        Action<string>? beforeMutate)
    {
        var frames = new Stack<DirectoryFrame>();
        try
        {
            VisitWindows(path, frames, beforeEnumerate, afterEnumerate, beforeMutate);
            while (frames.TryPeek(out var frame))
            {
                ct.ThrowIfCancellationRequested();
                if (frame.Next < frame.Children.Length)
                {
                    VisitWindows(frame.Children[frame.Next++], frames, beforeEnumerate, afterEnumerate, beforeMutate);
                    continue;
                }

                frames.Pop();
                try
                {
                    beforeMutate?.Invoke(frame.Path);
                    DeleteByHandle(frame.Handle, frame.Path);
                }
                finally
                {
                    frame.Handle.Dispose();
                }
            }
        }
        finally
        {
            while (frames.TryPop(out var open))
            {
                open.Handle.Dispose();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VisitWindows(
        string entry,
        Stack<DirectoryFrame> frames,
        Action<string>? beforeEnumerate,
        Action<string>? afterEnumerate,
        Action<string>? beforeMutate)
    {
        var handle = OpenNoFollow(entry, AccessDelete | FileListDirectory | FileReadAttributes | FileWriteAttributes);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                // Already gone (a concurrent cleanup, or a dangling root): nothing to delete.
                return;
            }

            throw Failure("open", entry, error);
        }

        var retained = false;
        try
        {
            if (!TryReadAttributes(handle, out var attributes))
            {
                throw Failure("inspect", entry, Marshal.GetLastPInvokeError());
            }

            if ((attributes & FileAttributeDirectory) != 0 && (attributes & FileAttributeReparsePoint) == 0)
            {
                beforeEnumerate?.Invoke(entry);
                var children = Directory.GetFileSystemEntries(entry);
                afterEnumerate?.Invoke(entry);
                frames.Push(new DirectoryFrame(entry, handle, children));
                retained = true;
                return;
            }

            // File, file symlink, directory symlink or junction: delete the object we opened.
            beforeMutate?.Invoke(entry);
            DeleteByHandle(handle, entry);
        }
        finally
        {
            if (!retained)
            {
                handle.Dispose();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteByHandle(SafeFileHandle handle, string path)
    {
        var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionIgnoreReadOnly };
        if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>()))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error is not (ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction))
        {
            throw Failure("delete", path, error);
        }

        // Pre-1809 / non-NTFS fallback: clear the read-only attribute through the same handle,
        // then use the classic disposition.
        if (GetFileInformationByHandleEx(handle, FileBasicInfoClass, out FileBasicInfo basic, (uint)Marshal.SizeOf<FileBasicInfo>()) &&
            (basic.FileAttributes & FileAttributeReadOnly) != 0)
        {
            var remaining = basic.FileAttributes & ~FileAttributeReadOnly;
            var cleared = new FileBasicInfo { FileAttributes = remaining == 0 ? FileAttributeNormal : remaining };
            if (!SetFileInformationByHandle(handle, FileBasicInfoClass, ref cleared, (uint)Marshal.SizeOf<FileBasicInfo>()))
            {
                throw Failure("clear the read-only attribute of", path, Marshal.GetLastPInvokeError());
            }
        }

        var classic = new FileDispositionInfo { DeleteFile = 1 };
        if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, ref classic, (uint)Marshal.SizeOf<FileDispositionInfo>()))
        {
            throw Failure("delete", path, Marshal.GetLastPInvokeError());
        }
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenNoFollow(string path, uint desiredAccess) =>
        CreateFileW(ToExtendedPath(path), desiredAccess, FileShareReadWrite, IntPtr.Zero, OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint, IntPtr.Zero);

    [SupportedOSPlatform("windows")]
    private static bool TryReadAttributes(SafeFileHandle handle, out uint attributes)
    {
        if (GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, out FileAttributeTagInfo info,
                (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            attributes = info.FileAttributes;
            return true;
        }

        attributes = 0;
        return false;
    }

    /// <summary>
    /// Win32 calls here take <c>\\?\</c> paths: no long-path manifest exists, so a plain path over
    /// MAX_PATH would fail to open.
    /// </summary>
    internal static string ToExtendedPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) return full;
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + full[2..];
        return @"\\?\" + full;
    }

    private static Exception Failure(string step, string path, int error)
    {
        var inner = new Win32Exception(error);
        var message = $"Could not {step} '{path}' for no-follow deletion: {inner.Message}";
        return error == ErrorAccessDenied
            ? new UnauthorizedAccessException(message, inner)
            : new IOException(message, inner);
    }

    private static void DeletePortable(
        string path,
        CancellationToken ct,
        Action<string>? beforeEnumerate,
        Action<string>? afterEnumerate,
        Action<string>? beforeMutate)
    {
        try
        {
            File.GetAttributes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        // One physical level at a time, postorder; reparse points are removed as links.
        var pending = new Stack<(string Path, bool Enumerated)>();
        pending.Push((path, false));
        while (pending.TryPop(out var current))
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(current.Path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                beforeMutate?.Invoke(current.Path);
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(current.Path);
                else File.Delete(current.Path);
            }
            else if ((attributes & FileAttributes.Directory) == 0 || current.Enumerated)
            {
                beforeMutate?.Invoke(current.Path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(current.Path, attributes & ~FileAttributes.ReadOnly);
                }

                if ((attributes & FileAttributes.Directory) == 0) File.Delete(current.Path);
                else Directory.Delete(current.Path);
            }
            else
            {
                pending.Push((current.Path, true));
                beforeEnumerate?.Invoke(current.Path);
                var children = Directory.GetFileSystemEntries(current.Path);
                afterEnumerate?.Invoke(current.Path);
                foreach (var child in children)
                {
                    pending.Push((child, false));
                }
            }
        }
    }

    private sealed record DirectoryFrame(string Path, SafeFileHandle Handle, string[] Children)
    {
        public int Next { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfoEx
    {
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo
    {
        public byte DeleteFile;
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

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int fileInformationClass,
        out FileBasicInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass,
        ref FileBasicInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass,
        ref FileDispositionInfo fileInformation, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int fileInformationClass,
        ref FileDispositionInfoEx fileInformation, uint bufferSize);
}
