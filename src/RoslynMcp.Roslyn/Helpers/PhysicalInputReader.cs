using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>Reads only through an opened object whose physical coordinate was verified before content access.</summary>
internal static class PhysicalInputReader
{
    internal static DateTime? ReadFileLastWriteTimeUtcIfPresent(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateRequest(path);
        // Comparison coordinates may omit an extended prefix. Reopen through
        // the original normalized request so literal components keep their meaning.
        var requested = Path.GetFullPath(path);
        var parentPath = Path.GetDirectoryName(requested) ?? throw new IOException("File metadata requires a parent directory.");
        using var parent = PhysicalDirectoryScope.OpenRouteIfPresent(parentPath);
        if (parent is null) return null;
        parent.AssertCurrentPath();
        var result = parent.ReadFileLastWriteTimeUtcIfPresent(Path.GetFileName(requested), ct);
        parent.AssertCurrentPath();
        return result;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static DateTime? ReadFileLastWriteTimeUtcWindowsIfPresent(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateRequest(path);
        using var opened = PhysicalPathResolver.OpenInputWindows(path, metadataOnly: true, FileShare.ReadWrite | FileShare.Delete);
        if (opened.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3) return null;
            throw OpenFailure(path, error, windows: true);
        }
        if (ValidateOpened(path, opened, ct)) return null;
        return File.GetLastWriteTimeUtc(opened);
    }

    internal static PhysicalInputReadLease OpenRead(string path, CancellationToken ct)
    {
        var handle = Acquire(path, FileShare.Read, ct);
        return BindOpenedFile(path, handle, ct);
    }

    internal static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct)
    {
        await using var input = OpenRead(path, ct);
        return await StreamContentBytes.ReadAllAsync(input.Stream, ct).ConfigureAwait(false);
    }

    internal static async Task<FileContentDigest> ReadDigestAsync(string path, CancellationToken ct)
    {
        await using var input = OpenRead(path, ct);
        return FileContentDigest.FromHash(await StreamContentHash.ComputeSha256Async(input.Stream, ct).ConfigureAwait(false));
    }

    internal static byte[] ReadAllBytes(string path)
    {
        using var input = OpenRead(path, CancellationToken.None);
        return StreamContentBytes.ReadAll(input.Stream);
    }

    internal static async Task<PhysicalInputIdentity> CaptureIdentityAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var handle = Acquire(path, FileShare.ReadWrite | FileShare.Delete, ct);
            try
            {
                var directory = ValidateOpened(path, handle, ct);
                if (directory) return new(path, true, []);
                await using var stream = CreateReadLease(handle);
                return new(path, true, [.. await StreamContentHash.ComputeSha256Async(stream.Stream, ct).ConfigureAwait(false)]);
            }
            finally { handle.Dispose(); }
        }
        catch (FileNotFoundException) { return new(path, false, []); }
        catch (DirectoryNotFoundException) { return new(path, false, []); }
    }

    // Takes ownership even on failure. No readable stream escapes before held-object validation.
    internal static PhysicalInputReadLease BindOpenedFile(string requestedPath, SafeFileHandle opened, CancellationToken ct)
    {
        try
        {
            if (ValidateOpened(requestedPath, opened, ct))
                throw new UnauthorizedAccessException("A directory cannot be read as file content.");
            return CreateReadLease(opened);
        }
        catch { opened.Dispose(); throw; }
    }

    private static PhysicalInputReadLease CreateReadLease(SafeFileHandle handle)
    {
        var locked = false;
        try
        {
            if (!OperatingSystem.IsWindows()) locked = PhysicalDirectoryScope.LockInputReadUnix(handle);
            return new PhysicalInputReadLease(new FileStream(handle, FileAccess.Read, 1, isAsync: handle.IsAsync), locked);
        }
        catch (Exception primary)
        {
            try { if (locked) PhysicalDirectoryScope.UnlockInputReadUnix(handle); }
            catch (Exception cleanup) { primary.Data["PhysicalInputReadCleanupFailure"] = cleanup; }
            finally { handle.Dispose(); }
            throw;
        }
    }
    private static bool ValidateOpened(string requestedPath, SafeFileHandle handle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var lexical = ValidateRequest(requestedPath);
        bool directory;
        if (OperatingSystem.IsWindows())
        {
            if (!PhysicalPathResolver.IsDiskHandleWindows(handle))
                throw new IOException("A physical input requires a regular file or directory.");
            var attributes = File.GetAttributes(handle);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw WorkspaceManager.PhysicalPathRequired();
            directory = (attributes & FileAttributes.Directory) != 0;
        }
        else
        {
            var kind = PhysicalDirectoryScope.ReadInputKindUnix(handle);
            directory = kind == 0x4000;
            if (!directory && kind != 0x8000) throw new IOException("A physical input requires a regular file or directory.");
        }
        // UNC has no volume GUID; permit only the actual UNC namespace fallback.
        var unc = OperatingSystem.IsWindows() && lexical.StartsWith(@"\\", StringComparison.Ordinal)
            && !lexical.StartsWith(@"\\?\", StringComparison.Ordinal) && !lexical.StartsWith(@"\\.\", StringComparison.Ordinal);
        var physical = Path.TrimEndingDirectorySeparator(PhysicalHandlePath.Get(handle, unc));
        if (!FileSystemPath.Comparer.Equals(lexical, physical)) throw WorkspaceManager.PhysicalPathRequired();
        ct.ThrowIfCancellationRequested();
        return directory;
    }

    private static string ValidateRequest(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path)) throw new PublicInvalidOperationException("Workspace physical paths must be fully qualified.");
        // Preserve component-order admission for spellings a native open may collapse.
        var components = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (components.Any(component => component is "." or ".." || component.EndsWith('.') || char.IsWhiteSpace(component[^1])))
            WorkspaceManager.EnsurePhysicalPath(path);
        // Normalize before removing an extended namespace prefix. Its literal
        // components must never pass through ordinary Win32 normalization.
        var requested = Path.GetFullPath(path);
        var coordinate = OperatingSystem.IsWindows() ? WindowsFilesystemCoordinate(requested) : requested;
        return Path.TrimEndingDirectorySeparator(coordinate);
    }

    private static string WindowsFilesystemCoordinate(string path)
    {
        var normalized = path.Replace('/', '\\');
        var extended = normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal);
        if (extended)
        {
            var tail = normalized[4..];
            if (tail.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase)) normalized = @"\\" + tail[4..];
            else if (tail.Length >= 3 && char.IsAsciiLetter(tail[0]) && tail[1] == ':' && tail[2] == '\\') normalized = tail;
            else if (tail.Length >= 45 && tail.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(tail.AsSpan(7, 36), "D", out _) && tail[43] == '}' && tail[44] == '\\')
                normalized = PhysicalPathResolver.GetLinkTargetPath(path, @"\\?\" + tail);
            else throw WorkspaceManager.PhysicalPathRequired();
        }
        if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = normalized[2..].Split('\\');
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1])
                || parts[1].Equals("pipe", StringComparison.OrdinalIgnoreCase)) throw WorkspaceManager.PhysicalPathRequired();
        }
        // Win32 interprets DOS device basenames even beneath an ordinary drive directory.
        var relative = normalized[(Path.GetPathRoot(normalized)?.Length ?? 0)..];
        foreach (var part in (extended ? string.Empty : relative).Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = part.Split('.', ':')[0].TrimEnd(' ');
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                || stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)
                || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                    && (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³')))
                throw WorkspaceManager.PhysicalPathRequired();
        }
        return normalized;
    }
    private static SafeFileHandle Acquire(string path, FileShare share, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateRequest(path);
        if (OperatingSystem.IsWindows())
        {
            var opened = PhysicalPathResolver.OpenInputWindows(path, metadataOnly: false, share);
            if (!opened.IsInvalid) return opened;
            var error = Marshal.GetLastPInvokeError();
            opened.Dispose();
            if (error == 5)
            {
                // Metadata-only directory observation must not require file-content rights.
                var metadata = PhysicalPathResolver.OpenInputWindows(path, metadataOnly: true, share);
                if (!metadata.IsInvalid)
                {
                    try
                    {
                        if (ValidateOpened(path, metadata, ct)) return metadata;
                    }
                    catch { metadata.Dispose(); throw; }
                    metadata.Dispose();
                }
                else metadata.Dispose();
            }
            throw OpenFailure(path, error, windows: true);
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Physical input acquisition requires Windows, Linux or macOS.");
        var handle = PhysicalDirectoryScope.OpenInputHandleUnix(path, out var code);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw OpenFailure(path, code, windows: false);
    }

    private static Exception OpenFailure(string path, int code, bool windows)
    {
        if (code == 2 || (windows ? code == 3 : code == 20))
        {
            // An absent leaf must not disguise a linked ancestor. No content is read here.
            WorkspaceManager.EnsurePhysicalPath(path);
            var missingLeaf = code == 2 && (windows || Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(path))));
            return missingLeaf ? new FileNotFoundException("The physical input does not exist.")
                : new DirectoryNotFoundException("A physical input parent does not exist.");
        }
        if (!windows && code == (OperatingSystem.IsMacOS() ? 62 : 40)) return WorkspaceManager.PhysicalPathRequired();
        if (windows ? code == 5 : code == 13) return new UnauthorizedAccessException("The physical input cannot be opened for reading.");
        return new IOException("The physical input could not be opened (native error " + code + ").",
            unchecked((int)(0x80070000u | (uint)code)));
    }
}
