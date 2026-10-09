using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

/// <summary>
/// Resolves filesystem paths in physical component order, including symbolic links and junctions.
/// </summary>
public static class PhysicalPathResolver
{
    private const int MaxLinkResolutionDepth = 64;

    /// <summary>
    /// Resolves every existing path component without lexically collapsing a parent traversal
    /// before an earlier filesystem link has been followed.
    /// </summary>
    public static string Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PublicArgumentException("Provide a non-empty filesystem path.", nameof(path));
        }
        if (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path))
        {
            throw new PublicArgumentException(
                "Drive-relative and root-relative paths are ambiguous; use a fully qualified or ordinary relative path.",
                nameof(path));
        }

        return ResolveCore(
            path,
            new HashSet<string>(FileSystemPath.Comparer),
            linkResolutionDepth: 0);
    }

    internal static string GetLinkTargetPath(string linkPath, string rawLinkTarget)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows mount-point junctions may expose a volume GUID without a path prefix.
            // The NT object-manager spelling is also returned by some filesystem APIs.
            var volumeTarget = rawLinkTarget.StartsWith(@"\??\", StringComparison.Ordinal)
                || rawLinkTarget.StartsWith(@"\\?\", StringComparison.Ordinal)
                ? rawLinkTarget[4..]
                : rawLinkTarget;
            if (volumeTarget.Length >= 45
                && volumeTarget.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(volumeTarget.AsSpan(7, 36), "D", out _)
                && volumeTarget[43] == '}'
                && volumeTarget[44] == '\\')
            {
                var volumeRoot = @"\\?\" + volumeTarget[..45];
                var driveRoot = TryGetDriveRoot(volumeRoot);
                return driveRoot is null
                    ? @"\\?\" + volumeTarget
                    : Path.Join(driveRoot, volumeTarget[45..]);
            }
        }

        if (Path.IsPathFullyQualified(rawLinkTarget))
        {
            return rawLinkTarget;
        }

        // Windows drive-relative (`C:target`) and rooted-but-drive-less (`\target`) targets
        // depend on ambient state. Never reinterpret them as link-parent-relative paths.
        if (Path.IsPathRooted(rawLinkTarget))
        {
            throw new IOException(
                $"Filesystem link '{linkPath}' has an ambiguous partially-qualified target.");
        }

        return Path.Join(Path.GetDirectoryName(linkPath), rawLinkTarget);
    }

    [SupportedOSPlatform("windows")]
    private static string? TryGetDriveRoot(string volumeRoot)
    {
        var paths = new char[256];
        if (!GetVolumePathNamesForVolumeNameW(volumeRoot, paths, (uint)paths.Length, out var requiredLength))
        {
            if (Marshal.GetLastPInvokeError() != 234 || requiredLength <= paths.Length)
            {
                return null;
            }

            paths = new char[requiredLength];
            if (!GetVolumePathNamesForVolumeNameW(volumeRoot, paths, (uint)paths.Length, out _))
            {
                return null;
            }
        }

        // A drive root gives MSBuild and other consumers a stable Win32 spelling. Directory
        // mount points can contain further links, so do not treat one as a physical root.
        return new string(paths)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Length == 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    [SupportedOSPlatform("windows")]
    private static string CanonicalizeDriveRoot(string pathRoot)
    {
        if (pathRoot.Length != 3 || !char.IsAsciiLetter(pathRoot[0])
            || pathRoot[1] != ':' || pathRoot[2] != '\\')
        {
            return pathRoot;
        }

        var volumeName = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(pathRoot, volumeName, (uint)volumeName.Capacity))
        {
            return pathRoot;
        }

        return TryGetDriveRoot(volumeName.ToString()) ?? pathRoot;
    }

    [SupportedOSPlatform("windows")]
    private static string? GetDriveAliasTarget(string pathRoot)
    {
        if (pathRoot.Length != 3 || !char.IsAsciiLetter(pathRoot[0])
            || pathRoot[1] != ':' || pathRoot[2] != '\\')
        {
            return null;
        }

        var target = new char[1024];
        var length = QueryDosDeviceW(pathRoot[..2], target, (uint)target.Length);
        if (length == 0 && Marshal.GetLastPInvokeError() == 122)
        {
            target = new char[32_768];
            length = QueryDosDeviceW(pathRoot[..2], target, (uint)target.Length);
        }

        if (length == 0)
        {
            if (Marshal.GetLastPInvokeError() == 122)
            {
                throw new IOException($"Drive alias mapping for '{pathRoot[..2]}' exceeds the supported path length.");
            }

            return null;
        }

        var first = new string(target, 0, checked((int)length)).Split('\0')[0];
        if (!first.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return null;
        }

        var aliasTarget = first[4..];
        return Path.IsPathFullyQualified(aliasTarget) ? aliasTarget : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNamesForVolumeNameW(
        string volumeName,
        [Out] char[] volumePathNames,
        uint bufferLength,
        out uint returnLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string volumeMountPoint,
        StringBuilder volumeName,
        uint bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint QueryDosDeviceW(string deviceName, [Out] char[] targetPath, uint maxLength);

    private static string ResolveCore(
        string path,
        HashSet<string> activeLinkPaths,
        int linkResolutionDepth)
    {
        if (linkResolutionDepth > MaxLinkResolutionDepth)
        {
            throw new IOException(
                $"Path contains more than {MaxLinkResolutionDepth} nested filesystem links.");
        }

        // Path.GetFullPath would collapse `..` before the operating system resolves an earlier
        // link, changing the physical target of `allowed/link-to-outside/../secret.cs`.
        var absolutePath = Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(Environment.CurrentDirectory, path);
        var pathRoot = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new UnreachableException("A fully qualified path must have a filesystem root.");
        }

        var relativePath = absolutePath[pathRoot.Length..];
        // A DOS drive alias may point at a subdirectory. Its root remains the logical boundary
        // for parent traversal even though the backing path has a parent of its own.
        var aliasRoot = OperatingSystem.IsWindows() && GetDriveAliasTarget(pathRoot) is { } aliasTarget
            ? ResolveCore(aliasTarget, activeLinkPaths, linkResolutionDepth + 1)
            : null;

        if (string.IsNullOrEmpty(relativePath))
        {
            return Path.GetFullPath(aliasRoot ?? (OperatingSystem.IsWindows() ? CanonicalizeDriveRoot(pathRoot) : pathRoot));
        }

        var current = aliasRoot ?? (OperatingSystem.IsWindows() ? CanonicalizeDriveRoot(pathRoot) : pathRoot);
        foreach (var component in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (aliasRoot is null || !FileSystemPath.Comparer.Equals(current, aliasRoot))
                {
                    current = GetPhysicalParent(current);
                }

                continue;
            }

            var next = Path.Combine(current, component);
            var rawLinkTarget = new FileInfo(next).LinkTarget;
            if (rawLinkTarget is null)
            {
                current = next;
                continue;
            }

            var linkKey = Path.GetFullPath(next);
            if (!activeLinkPaths.Add(linkKey))
            {
                throw new IOException($"Filesystem link cycle detected at '{linkKey}'.");
            }

            try
            {
                current = ResolveCore(
                    GetLinkTargetPath(next, rawLinkTarget),
                    activeLinkPaths,
                    linkResolutionDepth + 1);
            }
            finally
            {
                activeLinkPaths.Remove(linkKey);
            }
        }

        return Path.GetFullPath(current);
    }

    private static string GetPhysicalParent(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(parent) ? Path.GetPathRoot(path) ?? path : parent;
    }
}
