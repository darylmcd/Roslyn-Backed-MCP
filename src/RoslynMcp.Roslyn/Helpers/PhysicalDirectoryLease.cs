using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Holds an existing physical Windows directory against rename/deletion; grants no file-content authority.</summary>
internal interface IWindowsDirectoryPin : IDisposable
{
    (ulong Volume, ulong Low, ulong High) Identity { get; }
}

[SupportedOSPlatform("windows")]
internal sealed class PhysicalDirectoryLease : IWindowsDirectoryPin
{
    private readonly SafeFileHandle _handle;
    private PhysicalDirectoryLease(SafeFileHandle handle) => _handle = handle;
    public (ulong Volume, ulong Low, ulong High) Identity => ReadIdentity(_handle);

    [SupportedOSPlatform("windows")]
    internal static IWindowsDirectoryPin OpenRouteWindows(string path) => OpenRouteWindowsCore(path, allowMissing: false)
        ?? throw new IOException("The physical directory route is missing.");

    [SupportedOSPlatform("windows")]
    internal static IWindowsDirectoryPin? OpenRouteWindowsIfPresent(string path) => OpenRouteWindowsCore(path, allowMissing: true);

    [SupportedOSPlatform("windows")]
    private static IWindowsDirectoryPin? OpenRouteWindowsCore(string path, bool allowMissing)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        // Keep the actual Win32-selected object while deriving its canonical
        // native components; ordinary and extended spellings need not select
        // the same object for trailing dots, spaces or short names.
        using var selected = OpenWindowsCore(full, allowMissing);
        if (selected is null) return null;
        if (!PhysicalPathResolver.TryGetCanonicalWindowsHandlePath(selected._handle, allowUnc: true, out var canonical))
            throw new IOException("The physical directory has no verifiable native Windows coordinate.");
        var root = Path.GetPathRoot(canonical) ?? throw new IOException("A physical route has no filesystem root.");
        // The handle returned an absolute native coordinate. Ordinary path
        // normalization would reinterpret literal trailing dots or spaces.
        if (!canonical.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The native directory coordinate escaped its filesystem root.");
        var relative = canonical[root.Length..];
        var names = relative.Length == 0 ? [] : relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var held = new List<PhysicalDirectoryLease>();
        try
        {
            var current = OpenWindowsCore(root, allowMissing: false)
                ?? throw new IOException("The selected native directory root is no longer available.");
            held.Add(current);
            foreach (var name in names)
            {
                // Every ancestor remains pinned. Open only the next component
                // relative to its exact held parent, without resolving prefixes again.
                var handle = OpenNativeChild(current._handle, name, 0x1 | 0x80 | 0x100000,
                    1, 0x1, false, 0x1 | 0x2, CancellationToken.None)
                    ?? throw new IOException("The selected native directory route is no longer available.");
                current = RequireDirectory(handle);
                held.Add(current);
            }
            if (current.Identity != selected.Identity)
                throw new IOException("The native directory route changed the selected physical object.");
            // Direct route consumers need the same original-coordinate closure
            // as scopes. Every actual prefix is pinned before this fresh open.
            using var published = OpenWindowsCore(full, allowMissing: false)
                ?? throw new IOException("The published physical directory route is missing.");
            if (published.Identity != selected.Identity)
                throw new IOException("The physical directory no longer occupies its published route.");
            return new Route(held);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Physical directory route acquisition cleanup failed.", failure, held.AsEnumerable().Reverse().ToArray());
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static PhysicalDirectoryLease RequireDirectory(SafeFileHandle opened)
    {
        try
        {
            var kind = File.GetAttributes(opened);
            if ((kind & FileAttributes.Directory) == 0 || (kind & FileAttributes.ReparsePoint) != 0)
                throw new IOException("An owned directory route cannot follow a reparse point.");
            return new PhysicalDirectoryLease(opened);
        }
        catch { opened.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle? OpenNativeChild(SafeFileHandle parentHandle, string name, uint access, uint disposition, uint options, bool allowMissing, uint share, CancellationToken ct)
    {
        PhysicalDirectoryScope.ValidateName(name);
        ct.ThrowIfCancellationRequested();
        var length = checked((ushort)(name.Length * 2));
        var text = Marshal.StringToHGlobalUni(name);
        var unicodePointer = IntPtr.Zero;
        var parentHeld = false;
        SafeFileHandle? created = null;
        try
        {
            var unicode = new NativeUnicodeString { Length = length, MaximumLength = length, Buffer = text };
            unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<NativeUnicodeString>());
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            parentHandle.DangerousAddRef(ref parentHeld);
            var attributes = new NativeObjectAttributes
            {
                Length = (uint)Marshal.SizeOf<NativeObjectAttributes>(),
                RootDirectory = parentHandle.DangerousGetHandle(),
                ObjectName = unicodePointer,
                Attributes = 0x40 // OBJ_CASE_INSENSITIVE; one validated relative component.
            };
            var status = NtCreateFile(out created, access, ref attributes,
                out var result, IntPtr.Zero, 0, share, disposition,
                options | 0x20 | 0x00200000, IntPtr.Zero, 0);
            if (allowMissing && (status == unchecked((int)0xC0000034) || status == unchecked((int)0xC000003A))) return null;
            if (status != 0 || created.IsInvalid || (disposition == 2 && result.Information != (nuint)2))
                throw new IOException("A relative owned filesystem object could not be acquired safely.");
            ct.ThrowIfCancellationRequested();
            var returned = created;
            created = null;
            return returned;
        }
        finally
        {
            created?.Dispose();
            if (parentHeld) parentHandle.DangerousRelease();
            if (unicodePointer != IntPtr.Zero) Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    private sealed class Route(IReadOnlyList<PhysicalDirectoryLease> held) : IWindowsDirectoryPin
    {
        public (ulong Volume, ulong Low, ulong High) Identity => held[^1].Identity;
        public void Dispose() => OwnedResourceCleanup.Dispose("Physical directory route release failed.", null, held.Reverse().ToArray());
    }

    [SupportedOSPlatform("windows")]
    internal static PhysicalDirectoryLease OpenWindows(string path) => OpenWindowsCore(path, allowMissing: false)
        ?? throw new IOException("The physical directory is missing.");

    [SupportedOSPlatform("windows")]
    private static PhysicalDirectoryLease? OpenWindowsCore(string path, bool allowMissing)
    {
        EnsurePhysicalAcquisitionPath(path);
        // Attribute-only opens do not participate in Windows read/write/delete
        // sharing checks. Directory-list access makes no-delete sharing effective.
        const uint directoryRead = 0x1 | 0x80; // FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES
        const uint openExisting = 3;
        const uint backupSemantics = 0x02000000;
        const uint openReparsePoint = 0x00200000;
        var handle = CreateFileW(path, directoryRead, FileShare.Read | FileShare.Write, IntPtr.Zero,
            openExisting, backupSemantics | openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            handle.Dispose();
            if (allowMissing && code is 2 or 3) return null;
            throw new IOException("A physical directory could not be held safely (native error " + code + ").");
        }
        try
        {
            EnsurePhysicalAcquisitionPath(path);
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new PublicInvalidOperationException("The held input is not a physical directory.");
            return new(handle);
        }
        catch { handle.Dispose(); throw; }
    }

    internal static void EnsurePhysicalAcquisitionPath(string path)
    {
        try { WorkspaceManager.EnsurePhysicalPath(path); }
        catch (InvalidOperationException failure) when (failure.GetType() == typeof(InvalidOperationException))
        {
            // Namespace rejection is an unavailable filesystem acquisition here;
            // typed public argument refusals retain their original classification.
            throw new IOException("An owned filesystem namespace cannot be acquired through a link.");
        }
    }

    // Issued while the original no-delete namespace pin is still held.
    // The effective LIST/no-share-delete pin prevents replacement while this
    // share-delete witness is acquired and compared to the still-live pin.
    // No pathname witness acquisition after releasing the pin is permitted.
    [SupportedOSPlatform("windows")]
    internal static DeletionWitness CaptureDeletionWitness(string path)
    {
        using var original = OpenWindows(path);
        var witness = CreateFileW(path, 0x80, FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (witness.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            witness.Dispose();
            throw new IOException("The physical directory ownership witness could not be retained (native error " + code + ").");
        }
        try
        {
            if (ReadIdentity(original._handle) != ReadIdentity(witness))
                throw new IOException("The retained physical directory witness changed object identity.");
            var attributes = File.GetAttributes(witness);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The retained witness is not the original physical directory.");
            return new DeletionWitness(witness);
        }
        catch { witness.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    internal static (ulong Volume, ulong Low, ulong High) ReadIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 18, out var information, 24))
            throw new IOException("The held physical directory identity could not be read.");
        return (information.Volume, information.Low, information.High);
    }

    [SupportedOSPlatform("windows")]
    internal sealed class DeletionWitness : IDisposable
    {
        private readonly SafeFileHandle _witness;
        internal DeletionWitness(SafeFileHandle witness) => _witness = witness;
        internal (ulong Volume, ulong Low, ulong High) Identity => ReadIdentity(_witness);
        internal PhysicalDirectoryLease CreateChild(string name)
        {
            var created = OpenNativeChild(_witness, name, 0x1 | 0x80 | 0x100000, 2, 0x1, false, 0x1 | 0x2, CancellationToken.None)
                ?? throw new IOException("A new directory has no owned handle.");
            return RequireDirectory(created);
        }
        internal PhysicalDirectoryLease? OpenDirectory(string name, bool create, CancellationToken ct)
        {
            var opened = OpenNativeChild(_witness, name, 0x1 | 0x80 | 0x100000, create ? 3u : 1u, 0x1, !create, 0x1 | 0x2, ct);
            return opened is null ? null : RequireDirectory(opened);
        }

        internal SafeFileHandle CreateFreshFile(string name, CancellationToken ct)
            => OpenNativeChild(_witness, name, 0x3 | 0x80 | 0x100 | 0x100000, 2, 0x40, false, 0x1 | 0x2, ct)
                ?? throw new IOException("A fresh owned file has no physical handle.");
        internal SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct)
        {
            var opened = OpenNativeChild(_witness, name, 0x1 | 0x80 | 0x100000, 1, 0x40, true, 0x1 | 0x2, ct);
            if (opened is null) return null;
            try
            {
                var kind = File.GetAttributes(opened);
                if ((kind & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new IOException("An owned read requires a regular physical file.");
                return opened;
            }
            catch { opened.Dispose(); throw; }
        }
        internal SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct)
            => RequireTransitionFile(OpenNativeChild(_witness, name, 0x1 | (writable ? 0x2u : 0u) | 0x80 | 0x10000 | 0x100000, 1, 0x40, true, 0x1, ct));

        internal SafeFileHandle CreateTransitionFile(string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // Creation transfers cleanup ownership. Return its handle before a
            // cancellation observation can hide that successful namespace effect.
            return OpenNativeChild(_witness, name, 0x3 | 0x80 | 0x10000 | 0x100000, 2, 0x40, false, 0x1, CancellationToken.None)
                ?? throw new IOException("A fresh transition file has no physical handle.");
        }

        private static SafeFileHandle? RequireTransitionFile(SafeFileHandle? file)
        {
            if (file is null) return null;
            try
            {
                if ((File.GetAttributes(file) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                    throw new IOException("A source transition requires a regular physical file.");
                return file;
            }
            catch { file.Dispose(); throw; }
        }

        internal bool CurrentFileMatches(string name, SafeFileHandle witness)
        {
            using var current = OpenNativeChild(_witness, name, 0x80 | 0x100000, 1, 0, true, 0x1 | 0x2 | 0x4, CancellationToken.None);
            return current is not null && ReadIdentity(current) == ReadIdentity(witness);
        }

        internal void MoveFileNoReplace(string name, SafeFileHandle witness, DeletionWitness destination, string destinationName, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!CurrentFileMatches(name, witness)) throw new IOException("The transition file no longer occupies its held name.");
            destination.RenameFile(witness, destinationName, ct);
        }

        internal void ReclaimAdmittedTransitionFile(string name, SafeFileHandle witness, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!CurrentFileMatches(name, witness)) throw new IOException("The transition file no longer occupies its held name.");
            uint disposition = 0x1 | 0x10;
            if (!SetFileInformationByHandle(witness, 21, ref disposition, 4))
                throw new IOException("The held transition file could not be deleted (native error " + Marshal.GetLastPInvokeError() + ").");
            // Caller owns the DELETE handle and records the successful effect before closing it.
        }

        internal BoundRemoval? BindNonDirectoryEntryIfPresent(string name, CancellationToken ct)
        {
            var opened = OpenNativeChild(_witness, name, 0x10000 | 0x80 | 0x100000, 1, 0, true, 0x1 | 0x2, ct);
            if (opened is null) return null;
            return RequireNonDirectoryRemoval(opened, ct);
        }


        internal void PublishPreparedFile(string temporary, SafeFileHandle preparedWitness, string name, SafeFileHandle? replacedWitness, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var prepared = OpenNativeChild(_witness, temporary, 0x10000 | 0x80 | 0x100000, 1, 0x40, false, 0x1 | 0x2, ct)
                ?? throw new IOException("The prepared owned file is unavailable.");
            RequireSameFile(prepared, preparedWitness);
            SafeFileHandle? replaced = null;
            try
            {
                if (replacedWitness is not null)
                {
                    replaced = OpenNativeChild(_witness, name, 0x10000 | 0x80 | 0x100000, 1, 0x40, false, 0x1 | 0x2, ct)
                        ?? throw new IOException("The replaced owned file is unavailable.");
                    RequireSameFile(replaced, replacedWitness);
                }
                // All relevant DELETE handles now bind the expected still-live
                // objects. No pathname write occurs after releasing data pins.
                preparedWitness.Dispose();
                replacedWitness?.Dispose();
                if (replaced is not null)
                {
                    using var removal = RequireNonDirectoryRemoval(replaced, ct);
                    replaced = null;
                    removal.Delete(ct);
                }
                RenameFile(prepared, name, ct);
            }
            finally { replaced?.Dispose(); }
        }
        private void RenameFile(SafeFileHandle file, string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var nameBytes = checked(name.Length * 2);
            var header = checked((int)Marshal.OffsetOf<NativeRenameInformation>(nameof(NativeRenameInformation.FileName)));
            var length = checked(header + nameBytes);
            var information = Marshal.AllocHGlobal(length);
            var parentHeld = false;
            try
            {
                _witness.DangerousAddRef(ref parentHeld);
                Marshal.WriteInt32(information, 0); // ReplaceIfExists = FALSE
                Marshal.WriteIntPtr(information, checked((int)Marshal.OffsetOf<NativeRenameInformation>(nameof(NativeRenameInformation.RootDirectory))), _witness.DangerousGetHandle());
                Marshal.WriteInt32(information, checked((int)Marshal.OffsetOf<NativeRenameInformation>(nameof(NativeRenameInformation.FileNameLength))), nameBytes);
                Marshal.Copy(name.ToCharArray(), 0, IntPtr.Add(information, header), name.Length);
                var result = NtSetInformationFile(file, out _, information, checked((uint)length), 10); // FileRenameInformation
                if (result < 0) throw new IOException("The prepared owned file could not be published (native status " + result + ").");
                // Return the successful effect before cancellation can obscure its receipt.
            }
            finally
            {
                if (parentHeld) _witness.DangerousRelease();
                Marshal.FreeHGlobal(information);
            }
        }

        internal BoundRemoval BindIssuedName(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var target = OpenRemovalTarget(path);
            try
            {
                if (ReadIdentity(target) != Identity)
                    throw new IOException("A cleanup child no longer occupies its owned name.");
                var attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The cleanup target is not the original physical directory.");
                ct.ThrowIfCancellationRequested();
                return new BoundRemoval(target, this);
            }
            catch { target.Dispose(); throw; }
        }
        public void Dispose() => _witness.Dispose();
    }

    [SupportedOSPlatform("windows")]
    internal static SafeFileHandle CaptureFileWitness(string path, SafeFileHandle original, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var witness = CreateFileW(path, 0x80, FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (witness.IsInvalid)
        {
            var code = Marshal.GetLastPInvokeError();
            witness.Dispose();
            throw new IOException("The prepared file witness is unavailable (native error " + code + ").");
        }
        try { RequireSameFile(original, witness); ct.ThrowIfCancellationRequested(); return witness; }
        catch { witness.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static void RequireSameFile(SafeFileHandle left, SafeFileHandle right)
    {
        if (ReadIdentity(left) != ReadIdentity(right)) throw new IOException("An owned file no longer occupies its bound name.");
        if ((File.GetAttributes(right) & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
            throw new IOException("The copied entry must remain a regular physical file.");
    }

    [SupportedOSPlatform("windows")]
    internal static void CopyWindows(string sourcePath, SafeFileHandle source, string destinationPath, SafeFileHandle destination, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? throw new IOException("The copy source has no physical parent.");
        using var route = PhysicalDirectoryScope.OpenRoute(sourceDirectory);
        using var sourcePin = CreateFileW(sourcePath, 0x81, FileShare.Read, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
        if (sourcePin.IsInvalid) throw new IOException("The approved copy source cannot be pinned against replacement.");
        RequireSameFile(source, sourcePin);
        using var sourceWitness = CaptureFileWitness(sourcePath, sourcePin, ct);
        using var destinationWitness = CaptureFileWitness(destinationPath, destination, ct);
        // Source data pins plus the complete route bind the native path to the
        // already-approved source. The destination is our exclusively-created
        // inode held with effective no-delete sharing through native File.Copy.
        File.Copy(sourcePath, destinationPath, overwrite: true);
        RequireSameFile(source, sourceWitness);
        RequireSameFile(destination, destinationWitness);
        ct.ThrowIfCancellationRequested();
    }

    [SupportedOSPlatform("windows")]
    private static BoundRemoval RequireNonDirectoryRemoval(SafeFileHandle target, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(target);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                throw new IOException("A file or link cleanup entry was replaced by an unowned physical directory.");
            ct.ThrowIfCancellationRequested();
            return new BoundRemoval(target, null);
        }
        catch { target.Dispose(); throw; }
    }

    [SupportedOSPlatform("windows")]
    private static SafeFileHandle OpenRemovalTarget(string path)
    {
        var target = CreateFileW(path, 0x10000 | 0x80,
            FileShare.Read | FileShare.Write, IntPtr.Zero, 3,
            0x02000000 | 0x00200000, IntPtr.Zero);
        if (!target.IsInvalid) return target;
        var code = Marshal.GetLastPInvokeError();
        target.Dispose();
        throw new IOException("The cleanup entry could not be held for object-bound deletion (native error " + code + ").");
    }

    [SupportedOSPlatform("windows")]
    internal sealed class BoundRemoval(SafeFileHandle target, DeletionWitness? owner) : IDisposable
    {
        private bool _deleted;
        internal void Delete(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_deleted) return;
            // Namespace/type/identity were checked on THIS DELETE handle.
            // No share-delete means another actor cannot rename/replace it.
            // Readonly is inode-wide metadata: clearing it would mutate every
            // hardlink, including links outside this owned cleanup namespace.
            // Ignore it for THIS bound deletion without changing shared attributes.
            const uint deleteAndIgnoreReadOnly = 0x1 | 0x10;
            var disposition = deleteAndIgnoreReadOnly;
            if (!SetFileInformationByHandle(target, 21, ref disposition, 4))
                throw new IOException("The owned cleanup entry could not be marked for deletion (native error " + Marshal.GetLastPInvokeError() + ").");
            // DELETE target stays alive while the original witness closes.
            owner?.Dispose();
            _deleted = true;
        }
        public void Dispose() => target.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref uint information, uint size);

    public void Dispose() => _handle.Dispose();

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct DirectoryInformation
    {
        [FieldOffset(0)] internal ulong Volume;
        [FieldOffset(8)] internal ulong Low;
        [FieldOffset(16)] internal ulong High;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out DirectoryInformation information, uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeRenameInformation
    {
        internal uint ReplaceIfExists;
        internal IntPtr RootDirectory;
        internal uint FileNameLength;
        internal char FileName;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtSetInformationFile(SafeFileHandle file, out NativeIoStatus status, IntPtr information, uint length, int informationClass);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeUnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeObjectAttributes
    {
        internal uint Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeIoStatus
    {
        internal IntPtr Status;
        internal nuint Information;
    }

    [DllImport("ntdll.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access,
        ref NativeObjectAttributes attributes, out NativeIoStatus status, IntPtr allocationSize,
        uint fileAttributes, uint sharing, uint disposition, uint options, IntPtr extendedAttributes, uint extendedAttributesLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, FileShare shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);
}
