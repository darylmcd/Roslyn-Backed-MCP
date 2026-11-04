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
/// entry is opened with <c>FILE_FLAG_OPEN_REPARSE_POINT</c> (so a link is opened as the link, never
/// its target), classified from that handle, and mutated through a handle to the same object
/// (<c>FileDispositionInfoEx</c> with POSIX semantics, so an open child does not leave its parent non-empty; the handle also carries the read-only override). Files and links
/// are opened with <c>DELETE | FILE_READ_ATTRIBUTES</c> and full sharing (write-attributes access is acquired only by the legacy read-only-clearing fallback), the same footprint as
/// <see cref="File.Delete(string)"/>, so a holder that tolerates delete but not read does not block
/// the walk. A directory is re-opened with list access and no delete sharing, and the re-open is
/// accepted only if it has the same <c>FILE_ID_INFO</c> (volume serial plus 128-bit file id, as ReFS ids are not 64-bit) as the classified object. That handle stays
/// open, denying delete/rename sharing, from before the directory's children are enumerated until
/// its own postorder delete, so no ancestor of any queued child can be renamed away and replaced
/// by a junction.
/// </para>
/// <para>
/// Path handling: the root is normalized exactly once (<see cref="Path.GetFullPath(string)"/>) into
/// an extended-length <c>\\?\</c> path, so depth beyond MAX_PATH works without a long-path
/// manifest. Child paths are the parent's extended path plus the raw enumerated name, never
/// re-normalized: names with trailing dots or spaces, legal only under <c>\\?\</c>, therefore open
/// exactly the entry that was enumerated. The root's own ancestor chain is resolved by the OS and
/// followed if it contains links: the caller owns that trust boundary (callers pass roots they
/// created under a directory they hold).
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
    private const uint FileShareAll = 0x1 | 0x2 | 0x4;
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
    private const int FileIdInfoClass = 18;
    private const int FileDispositionInfoExClass = 21;
    private const uint DispositionDelete = 0x1;
    private const uint DispositionPosixSemantics = 0x2;
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
        var handle = OpenNoFollow(ToExtendedPath(path), FileListDirectory | FileReadAttributes, FileShareReadWrite);
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
            VisitWindows(path, ToExtendedPath(path), frames, beforeEnumerate, afterEnumerate, beforeMutate);
            while (frames.TryPeek(out var frame))
            {
                ct.ThrowIfCancellationRequested();
                if (frame.Next < frame.Names.Length)
                {
                    var name = frame.Names[frame.Next++];
                    VisitWindows(
                        Path.Combine(frame.Path, name),
                        AppendName(frame.ExtendedPath, name),
                        frames,
                        beforeEnumerate,
                        afterEnumerate,
                        beforeMutate);
                    continue;
                }

                frames.Pop();
                try
                {
                    beforeMutate?.Invoke(frame.Path);
                    DeleteByHandle(frame.Handle, frame.Path, frame.ExtendedPath);
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
        string display,
        string extended,
        Stack<DirectoryFrame> frames,
        Action<string>? beforeEnumerate,
        Action<string>? afterEnumerate,
        Action<string>? beforeMutate)
    {
        // Minimal footprint first: the same access and sharing File.Delete needs, which also
        // classifies the entry without requesting read or list access.
        var handle = OpenNoFollow(extended, AccessDelete | FileReadAttributes, FileShareAll);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (error is ErrorFileNotFound or ErrorPathNotFound)
            {
                // Already gone (a concurrent cleanup, or a dangling root): nothing to delete.
                return;
            }

            throw Failure("open", display, error);
        }

        var retained = false;
        try
        {
            if (!TryReadAttributes(handle, out var attributes))
            {
                throw Failure("inspect", display, Marshal.GetLastPInvokeError());
            }

            var isRealDirectory = (attributes & FileAttributeDirectory) != 0 &&
                                  (attributes & FileAttributeReparsePoint) == 0;
            if (!isRealDirectory)
            {
                // File, file symlink, directory symlink or junction: delete the object we opened.
                beforeMutate?.Invoke(display);
                DeleteByHandle(handle, display, extended);
                return;
            }

            // A directory needs list access and no delete sharing so that it cannot be renamed
            // away while its children are processed. Re-open it, and accept the re-open only if it
            // is the very object that was classified (a swap in between changes the identity).
            if (!TryReadFileId(handle, out var classified))
            {
                throw Failure("identify", display, Marshal.GetLastPInvokeError());
            }

            handle.Dispose();
            handle = OpenNoFollow(
                extended,
                AccessDelete | FileListDirectory | FileReadAttributes,
                FileShareReadWrite);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is ErrorFileNotFound or ErrorPathNotFound)
                {
                    return;
                }

                throw Failure("hold", display, error);
            }

            if (!TryReadAttributes(handle, out var heldAttributes) ||
                !TryReadFileId(handle, out var held) ||
                (heldAttributes & (FileAttributeDirectory | FileAttributeReparsePoint)) != FileAttributeDirectory ||
                !held.Equals(classified))
            {
                throw new IOException($"'{display}' changed while it was being opened for no-follow deletion.");
            }

            beforeEnumerate?.Invoke(display);
            var names = new List<string>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(extended))
            {
                var name = Path.GetFileName(entry);
                if (string.IsNullOrEmpty(name) || name is "." or "..")
                {
                    throw new IOException($"Refusing an unexpected directory entry name under '{display}'.");
                }

                names.Add(name);
            }

            afterEnumerate?.Invoke(display);
            frames.Push(new DirectoryFrame(display, extended, handle, names.ToArray()));
            retained = true;
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
    private static void DeleteByHandle(SafeFileHandle handle, string path, string extended)
    {
        // POSIX semantics unlink the name immediately. A classic disposition stays pending while any
        // other handle (watcher, indexer, scanner) is open with FILE_SHARE_DELETE, which leaves the
        // parent "not empty" and unremovable until that handle closes.
        var ex = new FileDispositionInfoEx { Flags = DispositionDelete | DispositionPosixSemantics | DispositionIgnoreReadOnly };
        if (SetFileInformationByHandle(handle, FileDispositionInfoExClass, ref ex, (uint)Marshal.SizeOf<FileDispositionInfoEx>()))
        {
            return;
        }

        var error = Marshal.GetLastPInvokeError();
        if (error is not (ErrorInvalidParameter or ErrorNotSupported or ErrorInvalidFunction))
        {
            throw Failure("delete", path, error);
        }

        // Pre-1809 / non-NTFS fallback: clear the read-only attribute, then use the classic
        // disposition. FILE_WRITE_ATTRIBUTES is not part of the delete footprint (DeleteFileW does
        // not need it), so it is acquired here only, through a second open that must be the same
        // object as the handle being deleted.
        if (GetFileInformationByHandleEx(handle, FileBasicInfoClass, out FileBasicInfo basic, (uint)Marshal.SizeOf<FileBasicInfo>()) &&
            (basic.FileAttributes & FileAttributeReadOnly) != 0)
        {
            using var attributeHandle = OpenNoFollow(extended, FileReadAttributes | FileWriteAttributes, FileShareAll);
            if (attributeHandle.IsInvalid)
            {
                throw Failure("open for attribute change", path, Marshal.GetLastPInvokeError());
            }

            if (!TryReadFileId(handle, out var expected) ||
                !TryReadFileId(attributeHandle, out var actual) ||
                !actual.Equals(expected))
            {
                throw new IOException($"'{path}' changed while its read-only attribute was being cleared.");
            }

            var remaining = basic.FileAttributes & ~FileAttributeReadOnly;
            var cleared = new FileBasicInfo { FileAttributes = remaining == 0 ? FileAttributeNormal : remaining };
            if (!SetFileInformationByHandle(attributeHandle, FileBasicInfoClass, ref cleared, (uint)Marshal.SizeOf<FileBasicInfo>()))
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
    private static SafeFileHandle OpenNoFollow(string extendedPath, uint desiredAccess, uint shareMode) =>
        CreateFileW(extendedPath, desiredAccess, shareMode, IntPtr.Zero, OpenExisting,
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

    [SupportedOSPlatform("windows")]
    private static bool TryReadFileId(SafeFileHandle handle, out FileIdInfo id) =>
        GetFileInformationByHandleEx(handle, FileIdInfoClass, out id, (uint)Marshal.SizeOf<FileIdInfo>());

    private static string AppendName(string extendedParent, string name) =>
        extendedParent.EndsWith('\\') ? extendedParent + name : extendedParent + '\\' + name;

    /// <summary>
    /// Normalizes <paramref name="path"/> once and returns it as an extended-length path. Called
    /// for the root only: children are built by appending raw enumerated names.
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

    private sealed record DirectoryFrame(string Path, string ExtendedPath, SafeFileHandle Handle, string[] Names)
    {
        public int Next { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    /// <summary>FILE_ID_INFO: 64-bit volume serial plus the 128-bit file id (ReFS ids are 128-bit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo : IEquatable<FileIdInfo>
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;

        public readonly bool Equals(FileIdInfo other) =>
            VolumeSerialNumber == other.VolumeSerialNumber && FileIdLow == other.FileIdLow && FileIdHigh == other.FileIdHigh;

        public override readonly bool Equals(object? obj) => obj is FileIdInfo other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(VolumeSerialNumber, FileIdLow, FileIdHigh);
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
        out FileIdInfo fileInformation, uint bufferSize);

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
