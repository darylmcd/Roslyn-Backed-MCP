using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

internal enum PhysicalDirectoryCreationMode { PrivateOwned, SourceDefault }
internal enum PhysicalDirectoryEntryKind { File, Directory, Link, Other }

/// <summary>A bounded directory namespace: Windows pins paths; Unix operations remain descriptor-relative.</summary>
internal abstract class PhysicalDirectoryScope(string path) : IDisposable
{
    internal string Path { get; } = path;
    internal abstract IReadOnlyList<string> Entries();
    internal abstract PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct);
    internal abstract DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct);
    internal abstract PhysicalDirectoryScope? OpenChild(string name);
    internal abstract PhysicalDirectoryScope CreateOwnedChildScope(string name);
    internal abstract bool SameIdentity(PhysicalDirectoryScope other);
    internal abstract bool SameFilesystem(PhysicalDirectoryScope other);
    internal abstract bool IsLink(string name);
    internal abstract IDisposable? LockOwner(string name);
    internal abstract DateTime LastActivityUtc { get; }
    internal abstract bool TryDeleteFile(string name, CancellationToken ct);
    internal abstract PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct);
    internal abstract SafeFileHandle CreateFreshFile(string name, CancellationToken ct);
    internal abstract SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct);
    internal abstract UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct);
    internal abstract void PublishPreparedFile(string temporary, SafeFileHandle preparedWitness, string name, SafeFileHandle? replacedWitness, CancellationToken ct);
    internal abstract SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct);
    internal abstract SafeFileHandle CreateTransitionFile(string name, CancellationToken ct);
    internal abstract bool CurrentFileMatches(string name, SafeFileHandle witness);
    internal abstract void MoveFileNoReplace(string name, SafeFileHandle? witness, PhysicalDirectoryScope destination, string destinationName, CancellationToken ct);
    internal abstract void ReclaimAdmittedTransitionFile(string name, SafeFileHandle witness, CancellationToken ct);
    internal abstract void ReleaseNamespacePin();
    internal abstract PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct);
    public abstract void Dispose();

    internal static bool SameFile(SafeFileHandle left, SafeFileHandle right) => OperatingSystem.IsWindows()
        ? PhysicalDirectoryLease.ReadIdentity(left) == PhysicalDirectoryLease.ReadIdentity(right)
        : UnixDirectory.ReadIdentity(left) == UnixDirectory.ReadIdentity(right);

    internal static SafeFileHandle OpenInputHandleUnix(string path, out int error) => UnixDirectory.OpenInputHandle(path, out error);
    internal static int ReadInputKindUnix(SafeFileHandle handle) => UnixDirectory.ReadMode(handle) & 0xF000;
    internal static bool LockInputReadUnix(SafeFileHandle handle) => UnixDirectory.LockInputRead(handle);
    internal static void UnlockInputReadUnix(SafeFileHandle handle) => UnixDirectory.UnlockInputRead(handle);

    internal static PhysicalDirectoryScope OpenRoute(string path)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (OperatingSystem.IsWindows()) return new WindowsDirectory(full, PhysicalDirectoryLease.OpenRouteWindows(full));
        return UnixDirectory.OpenUnixRoute(full, allowMissing: false)
            ?? throw new IOException("The physical directory route is missing.");
    }

    internal static PhysicalDirectoryScope? OpenRouteIfPresent(string path)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        if (!OperatingSystem.IsWindows()) return UnixDirectory.OpenUnixRoute(full, allowMissing: true);
        var pin = PhysicalDirectoryLease.OpenRouteWindowsIfPresent(full);
        return pin is null ? null : new WindowsDirectory(full, pin);
    }

    internal void AssertCurrentPath()
    {
        using var current = OpenRoute(Path);
        if (!SameIdentity(current))
            throw new IOException("The physical directory no longer occupies its published route.");
    }

    internal static OwnedRoot CreateOwnedRoot(string path)
    {
        var full = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var parentPath = System.IO.Path.GetDirectoryName(full) ?? throw new IOException("Cleanup cannot own a filesystem root.");
        return CreateOwnedChild(OpenRoute(parentPath), System.IO.Path.GetFileName(full));
    }

    // Borrows the authorized anchor; returns the SAME full-route pin checked against
    // its descriptor-relative final parent. Shared components are never cleanup-owned.
    internal static PhysicalDirectoryScope OpenOrCreateDescendant(
        PhysicalDirectoryScope anchor, IReadOnlyList<string> names, CancellationToken ct)
    {
        foreach (var name in names) ValidateName(name);
        var held = new List<PhysicalDirectoryScope>();
        PhysicalDirectoryScope? published = null;
        Exception? primary = null;
        try
        {
            anchor.AssertCurrentPath();
            var current = anchor;
            foreach (var name in names)
            {
                ct.ThrowIfCancellationRequested();
                current = current.OpenDirectoryForAccess(name, true, PhysicalDirectoryCreationMode.PrivateOwned, ct)
                    ?? throw new IOException("The shared artifact parent could not be acquired.");
                held.Add(current);
            }
            published = OpenRoute(current.Path);
            if (!current.SameIdentity(published))
                throw new IOException("The shared artifact parent changed during acquisition.");
            anchor.AssertCurrentPath();
            var intermediates = held.AsEnumerable().Reverse().Cast<IDisposable?>().ToArray();
            held.Clear();
            OwnedResourceCleanup.Dispose("Shared artifact intermediate release failed.", null, intermediates);
            var result = published;
            published = null;
            return result;
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Shared artifact parent acquisition release failed.", primary,
                held.AsEnumerable().Reverse().Cast<IDisposable?>().Append(published).ToArray());
        }
    }

    // Transfers the already-held parent to the returned owner, or releases it on failure.
    internal static OwnedRoot CreateOwnedChild(PhysicalDirectoryScope parent, string name)
    {
        PhysicalDirectoryScope? child = null;
        try
        {
            child = parent.CreateOwnedChildScope(name);
            using (var current = parent.OpenChild(name))
            {
                if (current is null || !child.SameIdentity(current))
                    throw new IOException("The acquired cleanup directory no longer occupies its issued name.");
            }
            // POSIX has no atomic mkdir-and-return-descriptor operation. Ownership
            // starts with this captured EMPTY object, not an unproved mkdir creator.
            // Refusal never cleans entries found before this admission boundary.
            if (child.Entries().Count != 0)
                throw new IOException("A fresh cleanup directory already contains unowned entries.");
            return new(parent, child, name);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.Dispose("Fresh cleanup ownership acquisition failed.", failure, child, parent);
            throw;
        }
    }

    internal sealed class OwnedRoot(PhysicalDirectoryScope parent, PhysicalDirectoryScope root, string name) : IDisposable
    {
        private readonly PhysicalDirectoryScope _root = root;
        private bool _contentsCleaned;
        private bool _cleanupStarted;
        private bool _disposed;
        private PhysicalDirectoryCleanupOutcome? _outcome;
        internal string Path => _root.Path;
        internal void AssertCurrentPath(CancellationToken ct)
        {
            RequireActive(ct);
            using var current = parent.OpenChild(name);
            if (current is null || !_root.SameIdentity(current))
                throw new IOException("The owned directory no longer occupies its issued name.");
            _root.AssertCurrentPath();
        }

        internal void AssertPhysicalPaths(IReadOnlyList<string> relativePaths, CancellationToken ct)
        {
            // Every frame is local to one side of the current-policy callback.
            // Only directory handles are shared; each requested entry is probed anew.
            ct.ThrowIfCancellationRequested();
            var paths = relativePaths.Select(PhysicalPathComponents).ToArray();
            AssertCurrentPath(ct);
            var directories = new Dictionary<string, PhysicalDirectoryScope>(FileSystemPath.Comparer)
                { [string.Empty] = _root };
            var edges = new List<(PhysicalDirectoryScope Parent, string Name, PhysicalDirectoryScope Child)>();
            Exception? primary = null;
            try
            {
                foreach (var components in paths)
                {
                    var directory = _root;
                    var prefix = string.Empty;
                    for (var index = 0; index < components.Length; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var component = components[index];
                        var kind = directory.GetEntryKind(component, ct);
                        if (kind is null) break;
                        if (kind is PhysicalDirectoryEntryKind.Link or PhysicalDirectoryEntryKind.Other)
                            throw Services.WorkspaceManager.PhysicalPathRequired();
                        if (index == components.Length - 1) break;
                        if (kind != PhysicalDirectoryEntryKind.Directory)
                            throw new IOException("An owned path ancestor is not a physical directory.");
                        prefix = prefix.Length == 0 ? component : System.IO.Path.Combine(prefix, component);
                        if (!directories.TryGetValue(prefix, out var child))
                        {
                            child = directory.OpenChild(component)
                                ?? throw new IOException("An owned path changed during physical admission.");
                            edges.Add((directory, component, child));
                            directories.Add(prefix, child);
                        }
                        directory = child;
                    }
                }
                AssertCurrentPath(ct);
                // A POSIX descriptor can outlive its name. Rebind every held edge
                // through its actual parent before publishing the mapped inventory.
                foreach (var edge in edges)
                {
                    ct.ThrowIfCancellationRequested();
                    if (edge.Parent.GetEntryKind(edge.Name, ct) != PhysicalDirectoryEntryKind.Directory)
                        throw Services.WorkspaceManager.PhysicalPathRequired();
                    PhysicalDirectoryScope? current = null;
                    Exception? bindingFailure = null;
                    try
                    {
                        current = edge.Parent.OpenChild(edge.Name);
                        if (current is null || !edge.Child.SameIdentity(current))
                            throw new IOException("An owned path changed during physical admission.");
                    }
                    catch (Exception failure) { bindingFailure = failure; throw; }
                    finally
                    {
                        OwnedResourceCleanup.DisposePreservingFailure("Owned physical path binding release failed.", bindingFailure, current);
                    }
                }
                AssertCurrentPath(ct);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                OwnedResourceCleanup.DisposePreservingFailure("Owned physical path batch release failed.", primary,
                    edges.AsEnumerable().Reverse().Select(edge => edge.Child).ToArray());
            }
        }

        internal void AssertPhysicalPath(string relative, CancellationToken ct)
        {
            AssertCurrentPath(ct);
            var components = PhysicalPathComponents(relative);
            if (components.Length == 0) return;
            var held = new List<PhysicalDirectoryScope>();
            Exception? primary = null;
            try
            {
                var directory = _root;
                for (var index = 0; index < components.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    directory.AssertCurrentPath();
                    var kind = directory.GetEntryKind(components[index], ct);
                    if (kind is null) break;
                    if (kind is PhysicalDirectoryEntryKind.Link or PhysicalDirectoryEntryKind.Other)
                        throw Services.WorkspaceManager.PhysicalPathRequired();
                    if (index == components.Length - 1) break;
                    if (kind != PhysicalDirectoryEntryKind.Directory)
                        throw new IOException("An owned path ancestor is not a physical directory.");
                    var child = directory.OpenChild(components[index])
                        ?? throw new IOException("An owned path changed during physical admission.");
                    held.Add(child);
                    directory = child;
                }
                AssertCurrentPath(ct);
            }
            catch (Exception failure) { primary = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Owned physical path route release failed.", primary, held.AsEnumerable().Reverse().ToArray()); }
        }
        private static string[] PhysicalPathComponents(string relative)
        {
            if (relative is "" or ".") return [];
            if (System.IO.Path.IsPathRooted(relative)) throw new IOException("An owned path must be root-relative.");
            var components = relative.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
            foreach (var component in components) ValidateName(component);
            return components;
        }

        internal IReadOnlyList<string> EnumerateRelativeFiles(CancellationToken ct) =>
            EnumerateRelativeEntries(ct).Where(entry => entry.Kind == PhysicalDirectoryEntryKind.File)
                .Select(entry => entry.RelativePath).ToArray();

        internal IReadOnlyList<(string RelativePath, PhysicalDirectoryEntryKind Kind)> EnumerateRelativeEntries(CancellationToken ct)
        {
            RequireActive(ct);
            AssertCurrentPath(ct);
            var result = new List<(string RelativePath, PhysicalDirectoryEntryKind Kind)>();
            Visit(_root, string.Empty);
            AssertCurrentPath(ct);
            return result;

            void Visit(PhysicalDirectoryScope directory, string relative)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var name in directory.Entries())
                {
                    ct.ThrowIfCancellationRequested();
                    var kind = directory.GetEntryKind(name, ct)
                        ?? throw new IOException("An owned result entry disappeared during enumeration.");
                    if (kind is not (PhysicalDirectoryEntryKind.File or PhysicalDirectoryEntryKind.Directory))
                        throw new IOException("Owned result enumeration requires physical regular files and directories.");
                    var childRelative = relative.Length == 0 ? name : System.IO.Path.Combine(relative, name);
                    result.Add((childRelative, kind));
                    if (kind == PhysicalDirectoryEntryKind.Directory)
                    {
                        using var child = directory.OpenChild(name)
                            ?? throw new IOException("An owned result directory changed during enumeration.");
                        Visit(child, childRelative);
                    }
                }
            }
        }

        internal void EnsureDirectories(string relativeDirectory, CancellationToken ct)
        {
            RequireActive(ct);
            PhysicalDirectoryWriter.EnsureDirectories(_root, relativeDirectory, ct);
        }
        internal Task WriteFileAsync(string relativeFile, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        {
            RequireActive(ct);
            return PhysicalDirectoryWriter.WriteFileAsync(_root, relativeFile, bytes, ct);
        }
        internal Task CopyFromStreamAsync(string relativeFile, string sourcePath, FileStream source, CancellationToken ct)
        {
            RequireActive(ct);
            return PhysicalDirectoryWriter.CopyFromStreamAsync(_root, relativeFile, sourcePath, source, ct);
        }
        internal Task<byte[]?> ReadFileIfPresentAsync(string relativeFile, CancellationToken ct)
        {
            RequireActive(ct);
            return PhysicalDirectoryWriter.ReadFileIfPresentAsync(_root, relativeFile, ct);
        }
        internal void DeleteFileIfPresent(string relativeFile, CancellationToken ct)
        {
            RequireActive(ct);
            PhysicalDirectoryWriter.DeleteFileIfPresent(_root, relativeFile, ct);
        }
        private void RequireActive(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_cleanupStarted || _disposed) throw new IOException("The owned namespace no longer grants writer authority.");
        }

        internal PhysicalDirectoryCleanupOutcome Delete(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_outcome is { } completed) return completed;
            if (_disposed) throw new ObjectDisposedException(nameof(OwnedRoot));
            _cleanupStarted = true;
            if (!_contentsCleaned)
            {
                _root.DeleteChildren(ct);
                _root.ReleaseNamespacePin();
                _contentsCleaned = true;
            }
            var result = parent.RemoveHeldChild(name, _root, ct);
            _outcome = result;
            _root.Dispose();
            return result;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OwnedResourceCleanup.Dispose("Owned cleanup namespace release failed.", null, _root, parent);
        }
    }

    internal static void CopyUnixMetadata(SafeFileHandle source, SafeFileHandle target, CancellationToken ct)
        => UnixDirectory.CopyMetadata(source, target, ct);

    internal bool HasReclaimableContent(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var name in Entries())
        {
            ct.ThrowIfCancellationRequested();
            using var child = OpenChild(name);
            // Files and no-follow links are reclaimable entries, even if empty.
            if (child is null || child.HasReclaimableContent(ct)) return true;
        }
        return false;
    }

    internal PhysicalDirectoryCleanupOutcome DeleteChildren(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var outcome = PhysicalDirectoryCleanupOutcome.Removed;
        foreach (var name in Entries())
        {
            ct.ThrowIfCancellationRequested();
            if (DeleteChild(name, directoriesOnly: false, ct) == PhysicalDirectoryCleanupOutcome.Retained)
                outcome = PhysicalDirectoryCleanupOutcome.Retained;
        }
        return outcome;
    }

    internal PhysicalDirectoryCleanupOutcome DeleteChild(string name, bool directoriesOnly, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var child = OpenChild(name);
        if (child is null)
        {
            if (directoriesOnly && !IsLink(name)) return PhysicalDirectoryCleanupOutcome.Retained;
            TryDeleteFile(name, ct);
            return PhysicalDirectoryCleanupOutcome.Removed;
        }
        child.DeleteChildren(ct);
        return RemoveHeldChild(name, child, ct);
    }

    internal static void ValidateName(string name)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/') || name.Contains('\0')
            || OperatingSystem.IsWindows() && (name.Contains('\\') || name.Contains(':')))
            throw new IOException("An owned directory entry has an invalid relative identity.");
    }

    [SupportedOSPlatform("windows")]
    private sealed class WindowsDirectory : PhysicalDirectoryScope
    {
        private readonly IDisposable _pin;
        private readonly PhysicalDirectoryLease.DeletionWitness _witness;
        private readonly (ulong Volume, ulong Low, ulong High) _identity;
        private bool _pinReleased;
        internal WindowsDirectory(string path, IWindowsDirectoryPin pin) : base(path)
        {
            PhysicalDirectoryLease.DeletionWitness? witness = null;
            try
            {
                witness = PhysicalDirectoryLease.CaptureDeletionWitness(path);
                if (pin.Identity != witness.Identity)
                    throw new IOException("The directory witness differs from its held namespace pin.");
                _identity = witness.Identity;
                _witness = witness;
                _pin = pin;
            }
            catch (Exception failure)
            {
                OwnedResourceCleanup.Dispose("Physical directory ownership capture failed.", failure, witness, pin);
                throw;
            }
        }
        internal override void ReleaseNamespacePin()
        {
            if (_pinReleased) return;
            _pin.Dispose();
            _pinReleased = true;
        }
        internal override PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            if (child is not WindowsDirectory original)
                throw new IOException("Cleanup requires an original physical Windows directory witness.");
            original.ReleaseNamespacePin();
            using var removal = original._witness.BindIssuedName(System.IO.Path.Combine(Path, name), ct);
            removal.Delete(ct);
            return PhysicalDirectoryCleanupOutcome.Removed;
        }
        internal override bool SameIdentity(PhysicalDirectoryScope other) => other is WindowsDirectory windows && _identity == windows._identity;
        internal override bool SameFilesystem(PhysicalDirectoryScope other) => other is WindowsDirectory windows && _identity.Volume == windows._identity.Volume;
        internal override bool IsLink(string name) { ValidateName(name); return (File.GetAttributes(System.IO.Path.Combine(Path, name)) & FileAttributes.ReparsePoint) != 0; }
        internal override PhysicalDirectoryScope CreateOwnedChildScope(string name)
        {
            ValidateName(name);
            var pin = _witness.CreateChild(name);
            return new WindowsDirectory(System.IO.Path.Combine(Path, name), pin);
        }
        internal override PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try { attributes = File.GetAttributes(System.IO.Path.Combine(Path, name)); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            return (attributes & FileAttributes.ReparsePoint) != 0 ? PhysicalDirectoryEntryKind.Link
                : (attributes & FileAttributes.Directory) != 0 ? PhysicalDirectoryEntryKind.Directory
                : (attributes & FileAttributes.Device) != 0 ? PhysicalDirectoryEntryKind.Other : PhysicalDirectoryEntryKind.File;
        }
        internal override DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct)
        {
            ValidateName(name);
            return PhysicalInputReader.ReadFileLastWriteTimeUtcWindowsIfPresent(System.IO.Path.Combine(Path, name), ct);
        }
        internal override IReadOnlyList<string> Entries() => Directory.EnumerateFileSystemEntries(Path).Select(System.IO.Path.GetFileName)
            .Select(name => name ?? throw new IOException("An owned directory entry has no name.")).ToArray();
        internal override PhysicalDirectoryScope? OpenChild(string name)
        {
            ValidateName(name);
            var child = System.IO.Path.Combine(Path, name);
            var attributes = File.GetAttributes(child);
            if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) return null;
            return new WindowsDirectory(child, PhysicalDirectoryLease.OpenWindows(child));
        }
        internal override IDisposable? LockOwner(string name)
        {
            ValidateName(name);
            var child = System.IO.Path.Combine(Path, name);
            PhysicalDirectoryLease.EnsurePhysicalAcquisitionPath(child);
            try { return new FileStream(child, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (FileNotFoundException) { return null; }
        }
        internal override DateTime LastActivityUtc => new[] { Directory.GetCreationTimeUtc(Path), Directory.GetLastWriteTimeUtc(Path) }.Max();
        internal override bool TryDeleteFile(string name, CancellationToken ct)
        {
            ValidateName(name);
            using var removal = _witness.BindNonDirectoryEntryIfPresent(name, ct);
            if (removal is null) return false;
            removal.Delete(ct);
            return true;
        }
        internal override PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct)
        {
            ValidateName(name);
            var lease = _witness.OpenDirectory(name, create, ct);
            return lease is null ? null : new WindowsDirectory(System.IO.Path.Combine(Path, name), lease);
        }
        internal override UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            return null;
        }
        internal override void PublishPreparedFile(string temporary, SafeFileHandle preparedWitness, string name, SafeFileHandle? replacedWitness, CancellationToken ct)
        {
            ValidateName(temporary);
            ValidateName(name);
            _witness.PublishPreparedFile(temporary, preparedWitness, name, replacedWitness, ct);
        }
        internal override SafeFileHandle CreateFreshFile(string name, CancellationToken ct)
        {
            ValidateName(name);
            return _witness.CreateFreshFile(name, ct);
        }
        internal override SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct)
        {
            ValidateName(name);
            return _witness.OpenReadFileIfPresent(name, ct);
        }
        internal override SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct)
        { ValidateName(name); return _witness.OpenTransitionFileIfPresent(name, writable, ct); }
        internal override SafeFileHandle CreateTransitionFile(string name, CancellationToken ct)
        { ValidateName(name); return _witness.CreateTransitionFile(name, ct); }
        internal override bool CurrentFileMatches(string name, SafeFileHandle witness)
        { ValidateName(name); return _witness.CurrentFileMatches(name, witness); }
        internal override void MoveFileNoReplace(string name, SafeFileHandle? witness, PhysicalDirectoryScope destination, string destinationName, CancellationToken ct)
        {
            ValidateName(name);
            ValidateName(destinationName);
            if (witness is null || destination is not WindowsDirectory target)
                throw new IOException("A Windows transition requires held source and destination objects.");
            _witness.MoveFileNoReplace(name, witness, target._witness, destinationName, ct);
        }
        internal override void ReclaimAdmittedTransitionFile(string name, SafeFileHandle witness, CancellationToken ct)
        { ValidateName(name); _witness.ReclaimAdmittedTransitionFile(name, witness, ct); }
        public override void Dispose() => OwnedResourceCleanup.Dispose("Physical directory ownership release failed.", null,
            OwnedResourceCleanup.FromAction(ReleaseNamespacePin), _witness);
    }

    private sealed class UnixDirectory(string path, SafeFileHandle handle) : PhysicalDirectoryScope(path)
    {
        private readonly (ulong Device, ulong Inode) _identity = CaptureIdentity(handle);
        private static (ulong Device, ulong Inode) CaptureIdentity(SafeFileHandle handle)
        {
            try { return ReadIdentity(handle); }
            catch { handle.Dispose(); throw; }
        }
        internal override PhysicalDirectoryScope CreateOwnedChildScope(string name)
        {
            ValidateName(name);
            if (MakeDirectoryAt(handle, name, 0x1C0) != 0) throw Failure(Marshal.GetLastPInvokeError()); // 0700
            return OpenChild(name) ?? throw new IOException("The created directory cannot be held safely.");
        }
        internal override void ReleaseNamespacePin() { }
        internal override PhysicalDirectoryCleanupOutcome RemoveHeldChild(string name, PhysicalDirectoryScope child, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            using var current = OpenChild(name);
            if (current is null || !child.SameIdentity(current))
                throw new IOException("A cleanup child no longer occupies its owned name.");
            // POSIX cannot atomically unlink an expected directory inode. Preserve
            // final names instead of authorizing whichever entry occupies them later.
            return PhysicalDirectoryCleanupOutcome.Retained;
        }

        internal override bool SameIdentity(PhysicalDirectoryScope other) => other is UnixDirectory unix && _identity == unix._identity;
        internal override bool SameFilesystem(PhysicalDirectoryScope other) => other is UnixDirectory unix && _identity.Device == unix._identity.Device;
        internal override bool IsLink(string name) => (GetEntryKind(name, CancellationToken.None)
            ?? throw new IOException("The inspected physical directory entry is missing.")) == PhysicalDirectoryEntryKind.Link;
        internal override PhysicalDirectoryEntryKind? GetEntryKind(string name, CancellationToken ct)
        {
            var mode = ReadEntryModeIfPresent(name, ct);
            return mode is null ? null : (mode.Value & 0xF000) switch
            {
                0x4000 => PhysicalDirectoryEntryKind.Directory,
                0x8000 => PhysicalDirectoryEntryKind.File,
                0xA000 => PhysicalDirectoryEntryKind.Link,
                _ => PhysicalDirectoryEntryKind.Other
            };
        }
        internal override DateTime? ReadFileLastWriteTimeUtcIfPresent(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            ushort mode;
            long seconds;
            long nanoseconds;
            int code;
            if (Mac)
            {
                code = RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? FileStatusAtMac64(handle, name, out var status, 0x20) : FileStatusAtMac(handle, name, out status, 0x20);
                mode = status.Mode; seconds = status.ModifiedSeconds; nanoseconds = status.ModifiedNanoseconds;
            }
            else
            {
                code = FileStatusLinux(handle, name, 0x100, 0x41, out var status); // NOFOLLOW, TYPE | MTIME
                if (code == 0 && (status.Mask & 0x41) != 0x41) throw new IOException("Physical input metadata is unavailable.");
                mode = status.Mode; seconds = status.Modified.Seconds; nanoseconds = status.Modified.Nanoseconds;
            }
            if (code != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is 2 or 20) return null;
                throw Failure(error);
            }
            if ((mode & 0xF000) == 0x4000) return null;
            if ((mode & 0xF000) != 0x8000) throw new IOException("Physical file metadata requires a regular non-linked file.");
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.AddTicks(nanoseconds / 100);
        }
        internal static (ulong Device, ulong Inode) ReadIdentity(SafeFileHandle handle)
        {
            if (Mac)
            {
                var code = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? FileStatusMac64(handle, out var status) : FileStatusMac(handle, out status);
                if (code != 0) throw Failure(Marshal.GetLastPInvokeError());
                return (status.Device, status.Inode);
            }
            if (FileStatusLinux(handle, "", 0x1000, 0x100, out var linux) != 0) throw Failure(Marshal.GetLastPInvokeError()); // AT_EMPTY_PATH, STATX_INO
            if ((linux.Mask & 0x100) == 0) throw new IOException("The owned directory identity is unavailable.");
            return (((ulong)linux.DeviceMajor << 32) | linux.DeviceMinor, linux.Inode);
        }
        // Values/dirent ABI: Linux UAPI asm-generic/fcntl.h and arm64/asm/fcntl.h;
        // Apple xnu bsd/sys/fcntl.h, dirent.h and cdefs.h. No process-CWD operations.
        private static bool Mac => OperatingSystem.IsMacOS();
        private static int NoFollow => Mac ? 0x100 : RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 ? 1 << 15 : 1 << 17;
        private static int DirectoryFlag => Mac ? 0x100000 : RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64 ? 1 << 14 : 1 << 16;
        private static int CloseOnExec => Mac ? 0x1000000 : 1 << 19;
        private static int DirectoryFlags => NoFollow | DirectoryFlag | CloseOnExec;
        private static IOException Failure(int code) => new("An owned directory operation failed (native error " + code + ").");

        internal static PhysicalDirectoryScope? OpenUnixRoute(string path, bool allowMissing)
        {
            if (!OperatingSystem.IsLinux() && !Mac) throw new PlatformNotSupportedException("Owned directory operations require Windows, Linux or macOS directory primitives.");
            // The independently configured application scratch prefix can use an OS alias
            // (for example macOS /var). Only this bootstrap may follow links. Every
            // caller-controlled suffix remains descriptor-relative and O_NOFOLLOW.
            var temporary = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()));
            var usesTemporary = FileSystemPath.Comparer.Equals(path, temporary) || FileSystemPath.IsStrictDescendant(temporary, path);
            var bootstrap = usesTemporary ? temporary : "/";
            var relative = System.IO.Path.GetRelativePath(bootstrap, path);
            var descriptor = Open(bootstrap, usesTemporary ? DirectoryFlag | CloseOnExec : DirectoryFlags);
            if (descriptor < 0) throw Failure(Marshal.GetLastPInvokeError());
            UnixDirectory current = new(bootstrap, new SafeFileHandle((IntPtr)descriptor, ownsHandle: true));
            try
            {
                foreach (var component in relative == "." ? Array.Empty<string>() : relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    var next = current.OpenChild(component) as UnixDirectory;
                    if (next is null)
                    {
                        if (allowMissing && current.GetEntryKind(component, CancellationToken.None) is null)
                        {
                            current.Dispose();
                            return null;
                        }
                        throw new IOException("An owned directory route is not a physical directory.");
                    }
                    current.Dispose();
                    current = next;
                }
                return current;
            }
            catch { current.Dispose(); throw; }
        }

        internal override PhysicalDirectoryScope? OpenChild(string name)
        {
            ValidateName(name);
            var descriptor = OpenAt(handle, name, DirectoryFlags, 0);
            if (descriptor >= 0) return new UnixDirectory(System.IO.Path.Combine(Path, name), new SafeFileHandle((IntPtr)descriptor, ownsHandle: true));
            var code = Marshal.GetLastPInvokeError();
            if (code == 2 || code == 20 || code == (Mac ? 62 : 40)) return null; // absent, non-directory or symlink
            throw Failure(code);
        }

        internal override IReadOnlyList<string> Entries()
        {
            // Opening '.' creates an independent enumeration offset; dup would share it.
            var descriptor = OpenAt(handle, ".", DirectoryFlags, 0);
            if (descriptor < 0) throw Failure(Marshal.GetLastPInvokeError());
            var directory = Mac && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? FdOpenDirMac64(descriptor) : FdOpenDir(descriptor);
            if (directory == IntPtr.Zero)
            {
                var code = Marshal.GetLastPInvokeError();
                using var unused = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
                throw Failure(code);
            }
            Exception? primary = null;
            try
            {
                var result = new List<string>();
                while (true)
                {
                    var entry = !Mac ? ReadDir64(directory) : RuntimeInformation.ProcessArchitecture == Architecture.X64
                        ? ReadDirMac64(directory) : ReadDirMac(directory);
                    if (entry == IntPtr.Zero)
                    {
                        var code = Marshal.GetLastPInvokeError();
                        if (code != 0) throw Failure(code);
                        return result;
                    }
                    var offset = Mac ? 21 : 19;
                    var recordLength = unchecked((ushort)Marshal.ReadInt16(entry, 16));
                    if (recordLength <= offset) throw new IOException("An owned directory entry has an invalid native record.");
                    var bytes = new List<byte>();
                    var terminated = false;
                    for (var index = offset; index < recordLength; index++)
                    {
                        var value = Marshal.ReadByte(entry, index);
                        if (value == 0) { terminated = true; break; }
                        bytes.Add(value);
                    }
                    if (!terminated) throw new IOException("An owned directory entry has no terminated name.");
                    string name;
                    try { name = new UTF8Encoding(false, true).GetString(bytes.ToArray()); }
                    catch (DecoderFallbackException) { throw new IOException("An owned directory entry cannot be represented exactly."); }
                    if (name is "." or "..") continue;
                    ValidateName(name);
                    result.Add(name);
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                if (CloseDir(directory) != 0)
                {
                    if (primary is null) throw Failure(Marshal.GetLastPInvokeError());
                    OwnedResourceCleanup.AttachOutcomes(primary, ["OwnedDirectoryClose:IOException"]);
                }
            }
        }

        internal override IDisposable? LockOwner(string name)
        {
            ValidateName(name);
            var descriptor = OpenAt(handle, name, 2 | NoFollow | CloseOnExec | (Mac ? 4 : 1 << 11), 0);
            if (descriptor < 0)
            {
                var code = Marshal.GetLastPInvokeError();
                if (code == 2) return null;
                throw Failure(code);
            }
            var owner = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            if (Flock(owner, 2 | 4) == 0) return owner; // LOCK_EX | LOCK_NB; release on close
            var error = Marshal.GetLastPInvokeError();
            owner.Dispose();
            throw Failure(error);
        }
        internal override DateTime LastActivityUtc => new[] { File.GetCreationTimeUtc(handle), File.GetLastWriteTimeUtc(handle) }.Max();
        internal override bool TryDeleteFile(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            if (UnlinkAt(handle, name, 0) == 0) return true;
            var code = Marshal.GetLastPInvokeError();
            if (code == 2) return false;
            throw Failure(code);
        }
        internal override PhysicalDirectoryScope? OpenDirectoryForAccess(string name, bool create, PhysicalDirectoryCreationMode creationMode, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            var descriptor = OpenAt(handle, name, DirectoryFlags, 0);
            if (descriptor < 0)
            {
                var code = Marshal.GetLastPInvokeError();
                if (code != 2) throw Failure(code);
                if (!create) return null;
                if (MakeDirectoryAt(handle, name, creationMode switch
                {
                    PhysicalDirectoryCreationMode.PrivateOwned => 0x1C0,
                    PhysicalDirectoryCreationMode.SourceDefault => 0x1FF,
                    _ => throw new ArgumentOutOfRangeException(nameof(creationMode))
                }) != 0 && Marshal.GetLastPInvokeError() != 17)
                    throw Failure(Marshal.GetLastPInvokeError());
                descriptor = OpenAt(handle, name, DirectoryFlags, 0);
                if (descriptor < 0) throw Failure(Marshal.GetLastPInvokeError());
            }
            var opened = new UnixDirectory(System.IO.Path.Combine(Path, name), new SafeFileHandle((IntPtr)descriptor, true));
            try { ct.ThrowIfCancellationRequested(); return opened; }
            catch { opened.Dispose(); throw; }
        }
        private ushort? ReadEntryModeIfPresent(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            ushort mode;
            if (Mac)
            {
                var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? FileStatusAtMac64(handle, name, out var status, 0x20) : FileStatusAtMac(handle, name, out status, 0x20);
                if (result != 0)
                {
                    var code = Marshal.GetLastPInvokeError();
                    if (code == 2) return null;
                    throw Failure(code);
                }
                mode = status.Mode;
            }
            else
            {
                if (FileStatusLinux(handle, name, 0x100, 3, out var status) != 0)
                {
                    var code = Marshal.GetLastPInvokeError();
                    if (code == 2) return null;
                    throw Failure(code);
                }
                if ((status.Mask & 3) != 3) throw new IOException("The existing owned entry mode is unavailable.");
                mode = status.Mode;
            }
            return mode;
        }
        internal override UnixFileMode? ReadFileModeIfPresent(string name, CancellationToken ct)
        {
            var mode = ReadEntryModeIfPresent(name, ct);
            if (mode is null) return null;
            if ((mode.Value & 0xF000) == 0x4000) throw new IOException("An owned file write cannot replace a physical directory.");
            return (mode.Value & 0xF000) == 0x8000 ? (UnixFileMode)(mode.Value & 0x1FF) : null;
        }
        internal override void PublishPreparedFile(string temporary, SafeFileHandle preparedWitness, string name, SafeFileHandle? replacedWitness, CancellationToken ct)
            => throw new PlatformNotSupportedException("Descriptor-relative fresh-file publication uses a Windows rename handle.");

        internal override SafeFileHandle? OpenTransitionFileIfPresent(string name, bool writable, CancellationToken ct)
            => OpenRegularFileIfPresent(name, writable, ct);

        internal override SafeFileHandle CreateTransitionFile(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            var flags = 2 | NoFollow | CloseOnExec | (Mac ? 0x200 | 0x800 : 0x40 | 0x80);
            var descriptor = OpenAt(handle, name, flags, 0x1B6);
            if (descriptor < 0) throw Failure(Marshal.GetLastPInvokeError());
            // Return successful acquisition before cancellation can obscure ownership.
            return new SafeFileHandle((IntPtr)descriptor, true);
        }

        internal override bool CurrentFileMatches(string name, SafeFileHandle witness)
        {
            ValidateName(name);
            (ulong Device, ulong Inode) current;
            if (Mac)
            {
                var result = RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? FileStatusAtMac64(handle, name, out var status, 0x20) : FileStatusAtMac(handle, name, out status, 0x20);
                if (result != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 2) return false;
                    throw Failure(error);
                }
                current = (status.Device, status.Inode);
            }
            else
            {
                if (FileStatusLinux(handle, name, 0x100, 0x100, out var status) != 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 2) return false;
                    throw Failure(error);
                }
                if ((status.Mask & 0x100) == 0) throw new IOException("A transition entry identity is unavailable.");
                current = (((ulong)status.DeviceMajor << 32) | status.DeviceMinor, status.Inode);
            }
            return current == ReadIdentity(witness);
        }

        internal override void MoveFileNoReplace(string name, SafeFileHandle? witness, PhysicalDirectoryScope destination, string destinationName, CancellationToken ct)
        {
            ValidateName(name);
            ValidateName(destinationName);
            ct.ThrowIfCancellationRequested();
            if (destination is not UnixDirectory target) throw new IOException("A native transition requires a same-platform destination.");
            // Capture the actual namespace entry. The caller records this effect,
            // then compares it with the witness; a mismatch must remain recoverable.
            var result = Mac ? RenameExclusiveMac(handle, name, target.Handle, destinationName, 0x4)
                : RenameNoReplaceLinux(handle, name, target.Handle, destinationName, 0x1);
            if (result != 0) throw Failure(Marshal.GetLastPInvokeError());
            // No cancellation or post-transfer validation before the caller's receipt.
        }
        private SafeFileHandle Handle => handle;

        internal override void ReclaimAdmittedTransitionFile(string name, SafeFileHandle witness, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            // Only identity/byte-validated content admitted into the private owned
            // namespace may reach this API. This is the ADR 0029 owned-content
            // reclaim contract, not an atomic expected-inode unlink on source paths.
            if (!CurrentFileMatches(name, witness)) throw new IOException("An admitted transition entry changed before reclamation.");
            if (UnlinkAt(handle, name, 0) != 0) throw Failure(Marshal.GetLastPInvokeError());
            // Record successful reclaim before any cancellation observation.
        }

        internal static void CopyMetadata(SafeFileHandle source, SafeFileHandle target, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Mac)
            {
                // COPYFILE_ACL | COPYFILE_STAT | COPYFILE_XATTR. Data has already
                // been streamed; preserve the same metadata as native File.Copy.
                if (CopyFileMetadataMac(source, target, IntPtr.Zero, 0x7) != 0) throw Failure(Marshal.GetLastPInvokeError());
            }
            else
            {
                const uint metadata = 1 | 2 | 0x20 | 0x40; // TYPE, MODE, ATIME, MTIME
                if (FileStatusLinux(source, "", 0x1000, metadata, out var status) != 0) throw Failure(Marshal.GetLastPInvokeError());
                if ((status.Mask & metadata) != metadata) throw new IOException("The copied source metadata is unavailable.");
                var times = new[]
                {
                    new NativeTime { Seconds = checked((nint)status.Access.Seconds), Nanoseconds = checked((nint)status.Access.Nanoseconds) },
                    new NativeTime { Seconds = checked((nint)status.Modified.Seconds), Nanoseconds = checked((nint)status.Modified.Nanoseconds) }
                };
                int result;
                do { ct.ThrowIfCancellationRequested(); result = SetTimes(target, times); } while (result != 0 && Marshal.GetLastPInvokeError() == 4);
                if (result != 0 && Marshal.GetLastPInvokeError() != 1) throw Failure(Marshal.GetLastPInvokeError());
                do { ct.ThrowIfCancellationRequested(); result = SetMode(target, (uint)(status.Mode & 0x1FF)); } while (result != 0 && Marshal.GetLastPInvokeError() == 4);
                if (result != 0 && Marshal.GetLastPInvokeError() != 1) throw Failure(Marshal.GetLastPInvokeError());
                // Match File.Copy's EPERM handling for filesystems without POSIX
                // ownership; no metadata operation targets a pre-existing inode.
            }
            ct.ThrowIfCancellationRequested();
        }
        internal override SafeFileHandle CreateFreshFile(string name, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            var flags = 1 | NoFollow | CloseOnExec | (Mac ? 0x200 | 0x800 : 0x40 | 0x80); // WRONLY, CREAT, EXCL
            var descriptor = OpenAt(handle, name, flags, 0x1B6); // 0666, filtered by the process umask
            if (descriptor < 0) throw Failure(Marshal.GetLastPInvokeError());
            var opened = new SafeFileHandle((IntPtr)descriptor, true);
            try { ct.ThrowIfCancellationRequested(); return opened; }
            catch { opened.Dispose(); throw; }
        }
        internal override SafeFileHandle? OpenReadFileIfPresent(string name, CancellationToken ct)
            => OpenRegularFileIfPresent(name, writable: false, ct);

        private SafeFileHandle? OpenRegularFileIfPresent(string name, bool writable, CancellationToken ct)
        {
            ValidateName(name);
            ct.ThrowIfCancellationRequested();
            var descriptor = OpenAt(handle, name, (writable ? 2 : 0) | NoFollow | CloseOnExec | (Mac ? 4 : 0x800), 0); // RDWR or RDONLY, NONBLOCK
            if (descriptor < 0)
            {
                var code = Marshal.GetLastPInvokeError();
                if (code == 2) return null;
                throw Failure(code);
            }
            var opened = new SafeFileHandle((IntPtr)descriptor, true);
            try
            {
                var mode = ReadMode(opened);
                if ((mode & 0xF000) != 0x8000) throw new IOException("An owned read requires a regular physical file.");
                ct.ThrowIfCancellationRequested();
                return opened;
            }
            catch { opened.Dispose(); throw; }
        }
        public override void Dispose() => handle.Dispose();

        internal static SafeFileHandle OpenInputHandle(string path, out int error)
        {
            if (!OperatingSystem.IsLinux() && !Mac) throw new PlatformNotSupportedException("Physical inputs require Linux or macOS native file primitives.");
            // No configured scratch-prefix alias exception. The leaf never follows
            // a link, and mandatory held-path validation rejects aliased ancestors.
            var descriptor = Open(path, NoFollow | CloseOnExec | (Mac ? 4 : 0x800));
            error = descriptor < 0 ? Marshal.GetLastPInvokeError() : 0;
            return new SafeFileHandle((IntPtr)descriptor, true);
        }

        private static readonly bool InputLockingDisabled = ReadInputLockingDisabled();

        private static bool ReadInputLockingDisabled()
        {
            // Match the runtime's read-sharing switch, including environment precedence.
            var value = Environment.GetEnvironmentVariable("DOTNET_SYSTEM_IO_DISABLEFILELOCKING");
            if (value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return AppContext.TryGetSwitch("System.IO.DisableFileLocking", out var disabled) && disabled;
        }

        internal static bool LockInputRead(SafeFileHandle file)
        {
            if (InputLockingDisabled) return false;
            if (Flock(file, 1 | 4) == 0) return true; // LOCK_SH | LOCK_NB
            var error = Marshal.GetLastPInvokeError();
            if (error == (Mac ? 35 : 11)) throw new IOException("The physical input is exclusively locked.");
            // Like FileStream, unsupported advisory locking does not deny an otherwise valid read.
            return false;
        }

        internal static void UnlockInputRead(SafeFileHandle file)
        {
            // Best effort before close, matching FileStream: interrupted close must not retain a lock.
            _ = Flock(file, 8); // LOCK_UN
        }
        internal static ushort ReadMode(SafeFileHandle file)
        {
            ushort mode;
            if (Mac)
            {
                var code = RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? FileStatusMac64(file, out var status) : FileStatusMac(file, out status);
                if (code != 0) throw Failure(Marshal.GetLastPInvokeError());
                mode = status.Mode;
            }
            else
            {
                if (FileStatusLinux(file, "", 0x1000, 1, out var status) != 0 || (status.Mask & 1) == 0)
                    throw Failure(Marshal.GetLastPInvokeError());
                mode = status.Mode;
            }
            return mode;
        }

        [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "openat", SetLastError = true)] private static extern int OpenAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "renameat2", SetLastError = true)]
        private static extern int RenameNoReplaceLinux(SafeFileHandle sourceDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName,
            SafeFileHandle destinationDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, uint flags);
        [DllImport("libc", EntryPoint = "renameatx_np", SetLastError = true)]
        private static extern int RenameExclusiveMac(SafeFileHandle sourceDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string sourceName,
            SafeFileHandle destinationDirectory, [MarshalAs(UnmanagedType.LPUTF8Str)] string destinationName, uint flags);
        [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)] private static extern int UnlinkAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
        [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] private static extern int MakeDirectoryAt(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, uint mode);
        // Linux UAPI statx has an architecture-independent 256-byte layout. Darwin
        // stat64 is 144 bytes; x64 selects the public INODE64 symbol explicitly.
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct LinuxStatus
        {
            [FieldOffset(0)] internal uint Mask;
            [FieldOffset(28)] internal ushort Mode;
            [FieldOffset(32)] internal ulong Inode;
            [FieldOffset(64)] internal LinuxTime Access;
            [FieldOffset(112)] internal LinuxTime Modified;
            [FieldOffset(136)] internal uint DeviceMajor;
            [FieldOffset(140)] internal uint DeviceMinor;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct LinuxTime
        {
            internal long Seconds;
            internal uint Nanoseconds;
            internal int Reserved;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTime
        {
            internal nint Seconds;
            internal nint Nanoseconds;
        }
        [DllImport("libc", EntryPoint = "futimens", SetLastError = true)] private static extern int SetTimes(SafeFileHandle file, [In] NativeTime[] times);
        [DllImport("libc", EntryPoint = "fchmod", SetLastError = true)] private static extern int SetMode(SafeFileHandle file, uint mode);
        [DllImport("libc", EntryPoint = "fcopyfile", SetLastError = true)] private static extern int CopyFileMetadataMac(SafeFileHandle source, SafeFileHandle target, IntPtr state, uint flags);
        [StructLayout(LayoutKind.Explicit, Size = 144)]
        private struct MacStatus
        {
            [FieldOffset(0)] internal uint Device;
            [FieldOffset(4)] internal ushort Mode;
            [FieldOffset(8)] internal ulong Inode;
            [FieldOffset(48)] internal long ModifiedSeconds;
            [FieldOffset(56)] internal long ModifiedNanoseconds;
        }
        [DllImport("libc", EntryPoint = "statx", SetLastError = true)] private static extern int FileStatusLinux(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxStatus status);
        [DllImport("libc", EntryPoint = "fstat$INODE64", SetLastError = true)] private static extern int FileStatusMac64(SafeFileHandle file, out MacStatus status);
        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)] private static extern int FileStatusMac(SafeFileHandle file, out MacStatus status);
        [DllImport("libc", EntryPoint = "fstatat$INODE64", SetLastError = true)] private static extern int FileStatusAtMac64(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out MacStatus status, int flags);
        [DllImport("libc", EntryPoint = "fstatat", SetLastError = true)] private static extern int FileStatusAtMac(SafeFileHandle directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out MacStatus status, int flags);
        [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int Flock(SafeFileHandle file, int operation);
        [DllImport("libc", EntryPoint = "fdopendir", SetLastError = true)] private static extern IntPtr FdOpenDir(int directory);
        [DllImport("libc", EntryPoint = "fdopendir$INODE64", SetLastError = true)] private static extern IntPtr FdOpenDirMac64(int directory);
        [DllImport("libc", EntryPoint = "closedir", SetLastError = true)] private static extern int CloseDir(IntPtr directory);
        [DllImport("libc", EntryPoint = "readdir64", SetLastError = true)] private static extern IntPtr ReadDir64(IntPtr directory);
        [DllImport("libc", EntryPoint = "readdir$INODE64", SetLastError = true)] private static extern IntPtr ReadDirMac64(IntPtr directory);
        [DllImport("libc", EntryPoint = "readdir", SetLastError = true)] private static extern IntPtr ReadDirMac(IntPtr directory);
    }
}
