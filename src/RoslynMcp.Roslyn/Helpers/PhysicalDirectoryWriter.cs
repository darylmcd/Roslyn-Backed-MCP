using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Reads and replaces entries only relative to an already-issued physical directory object.</summary>
internal static class PhysicalDirectoryWriter
{
    internal static void EnsureDirectories(PhysicalDirectoryScope root, string relative, CancellationToken ct)
    {
        DirectoryRoute? route = null;
        Exception? primary = null;
        try { route = Resolve(root, Components(relative, directory: true), create: true, ct); }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Owned directory route release failed.", primary, route); }
    }

    internal static Task WriteFileAsync(PhysicalDirectoryScope root, string relative, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        => WriteAsync(root, relative, bytes, null, null, ct);

    internal static Task CopyFromStreamAsync(PhysicalDirectoryScope root, string relative, string sourcePath, FileStream source, CancellationToken ct)
        => WriteAsync(root, relative, ReadOnlyMemory<byte>.Empty, sourcePath, source, ct);

    private static async Task WriteAsync(PhysicalDirectoryScope root, string relative, ReadOnlyMemory<byte> bytes, string? sourcePath, FileStream? source, CancellationToken ct)
    {
        var components = Components(relative, directory: false);
        DirectoryRoute? route = null;
        FileStream? output = null;
        Exception? primary = null;
        try
        {
            route = Resolve(root, components[..^1], create: true, ct)
                ?? throw new IOException("The owned write parent is unavailable.");
            if (OperatingSystem.IsWindows() && source is null)
            {
                await WriteWindowsOverlayAsync(route.Directory, components[^1], bytes, ct).ConfigureAwait(false);
                return;
            }
            var mode = !OperatingSystem.IsWindows() && source is null
                ? route.Directory.ReadFileModeIfPresent(components[^1], ct) : null;
            route.Directory.TryDeleteFile(components[^1], ct);
            // Remove only the bound old entry, then create exclusively. A racing
            // actor entry causes refusal; no existing inode is ever truncated.
            var fresh = route.Directory.CreateFreshFile(components[^1], ct);
            try { output = new FileStream(fresh, FileAccess.Write, bufferSize: 4096, isAsync: false); }
            catch { fresh.Dispose(); throw; }
            if (source is not null)
            {
                if (OperatingSystem.IsWindows())
                    PhysicalDirectoryLease.CopyWindows(sourcePath ?? throw new IOException("The copy source has no issued physical coordinate."), source.SafeFileHandle,
                        System.IO.Path.Combine(route.Directory.Path, components[^1]), output.SafeFileHandle, ct);
                else
                {
                    await source.CopyToAsync(output, 81920, ct).ConfigureAwait(false);
                    await output.FlushAsync(ct).ConfigureAwait(false);
                    PhysicalDirectoryScope.CopyUnixMetadata(source.SafeFileHandle, output.SafeFileHandle, ct);
                }
            }
            else
            {
                await output.WriteAsync(bytes, ct).ConfigureAwait(false);
                await output.FlushAsync(ct).ConfigureAwait(false);
                if (!OperatingSystem.IsWindows() && mode is { } permissions)
                    File.SetUnixFileMode(output.SafeFileHandle, permissions);
            }
            ct.ThrowIfCancellationRequested();
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Owned write resource release failed.", primary, output, route); }
    }

    [SupportedOSPlatform("windows")]
    private static async Task WriteWindowsOverlayAsync(PhysicalDirectoryScope directory, string name, ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        var temporary = ".owned-write-" + Guid.NewGuid().ToString("N");
        var temporaryPath = System.IO.Path.Combine(directory.Path, temporary);
        SafeFileHandle? original = null;
        SafeFileHandle? originalWitness = null;
        SafeFileHandle? preparedWitness = null;
        FileStream? output = null;
        Exception? primary = null;
        try
        {
            original = directory.OpenReadFileIfPresent(name, ct);
            DateTime? creation = original is null ? null : File.GetCreationTimeUtc(original);
            DateTime? access = original is null ? null : File.GetLastAccessTimeUtc(original);
            var fresh = directory.CreateFreshFile(temporary, ct);
            try { output = new FileStream(fresh, FileAccess.Write, bufferSize: 4096, isAsync: false); }
            catch { fresh.Dispose(); throw; }
            if (original is not null)
            {
                PhysicalDirectoryLease.CopyWindows(System.IO.Path.Combine(directory.Path, name), original,
                    temporaryPath, output.SafeFileHandle, ct);
                originalWitness = PhysicalDirectoryLease.CaptureFileWitness(System.IO.Path.Combine(directory.Path, name), original, ct);
            }
            // Only our exclusively-created, continuously pinned inode is written.
            // Native cloning retains alternate streams and existing attributes.
            output.SetLength(0);
            await output.WriteAsync(bytes, ct).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            if (creation is { } created) File.SetCreationTimeUtc(output.SafeFileHandle, created);
            if (access is { } accessed) File.SetLastAccessTimeUtc(output.SafeFileHandle, accessed);
            preparedWitness = PhysicalDirectoryLease.CaptureFileWitness(temporaryPath, output.SafeFileHandle, ct);
            output.Dispose();
            output = null;
            original?.Dispose();
            original = null;
            // The live witnesses bridge the sharing handoff. Replaced identities
            // refuse before the old entry is deleted; publication never replaces
            // a raced destination name and performs no pathname content write.
            directory.PublishPreparedFile(temporary, preparedWitness, name, originalWitness, ct);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Owned overlay resource release failed.", primary,
                output, original, preparedWitness, originalWitness);
        }
    }

    internal static async Task<byte[]?> ReadFileIfPresentAsync(PhysicalDirectoryScope root, string relative, CancellationToken ct)
    {
        var components = Components(relative, directory: false);
        DirectoryRoute? route = null;
        FileStream? input = null;
        Exception? primary = null;
        try
        {
            route = Resolve(root, components[..^1], create: false, ct);
            if (route is null) return null;
            var file = route.Directory.OpenReadFileIfPresent(components[^1], ct);
            if (file is null) return null;
            try { input = new FileStream(file, FileAccess.Read, bufferSize: 4096, isAsync: false); }
            catch { file.Dispose(); throw; }
            return await StreamContentBytes.ReadAllAsync(input, ct).ConfigureAwait(false);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Owned read resource release failed.", primary, input, route); }
    }

    internal static void DeleteFileIfPresent(PhysicalDirectoryScope root, string relative, CancellationToken ct)
    {
        var components = Components(relative, directory: false);
        DirectoryRoute? route = null;
        Exception? primary = null;
        try
        {
            route = Resolve(root, components[..^1], create: false, ct);
            route?.Directory.TryDeleteFile(components[^1], ct);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Owned delete route release failed.", primary, route); }
    }

    private static string[] Components(string relative, bool directory)
    {
        if (directory && relative is "" or ".") return [];
        if (System.IO.Path.IsPathRooted(relative)) throw new IOException("An owned operand must be root-relative.");
        var components = relative.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
        foreach (var component in components) PhysicalDirectoryScope.ValidateName(component);
        return components;
    }

    private static DirectoryRoute? Resolve(PhysicalDirectoryScope root, string[] components, bool create, CancellationToken ct)
    {
        var held = new List<PhysicalDirectoryScope>();
        var current = root;
        try
        {
            ct.ThrowIfCancellationRequested();
            foreach (var component in components)
            {
                ct.ThrowIfCancellationRequested();
                var child = current.OpenDirectoryForAccess(component, create, PhysicalDirectoryCreationMode.PrivateOwned, ct);
                if (child is null)
                {
                    OwnedResourceCleanup.Dispose("Missing owned route release failed.", null, held.AsEnumerable().Reverse().ToArray());
                    return null;
                }
                held.Add(child);
                current = child;
            }
            return new DirectoryRoute(current, held);
        }
        catch (Exception failure)
        {
            OwnedResourceCleanup.DisposePreservingFailure("Owned directory acquisition cleanup failed.", failure, held.AsEnumerable().Reverse().ToArray());
            throw;
        }
    }

    private sealed class DirectoryRoute(PhysicalDirectoryScope directory, IReadOnlyList<PhysicalDirectoryScope> held) : IDisposable
    {
        internal PhysicalDirectoryScope Directory { get; } = directory;
        public void Dispose() => OwnedResourceCleanup.Dispose("Owned directory route release failed.", null, held.Reverse().ToArray());
    }
}
