using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>
/// Canonical server-owned path boundary shared by path validation and solution discovery.
/// </summary>
internal static class ConfiguredRootBoundary
{
    private const int MaxLinkResolutionDepth = 64;

    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    internal static SecurityOptions ResolveOptions(McpServer? server, SecurityOptions? explicitOptions) =>
        explicitOptions
        ?? server?.Services?.GetService<SecurityOptions>()
        ?? new SecurityOptions();

    internal static IReadOnlyList<string> GetCanonicalRoots(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var roots = new List<string>(options.SanctionedRoots.Count);
        foreach (var configuredRoot in options.SanctionedRoots)
        {
            if (string.IsNullOrWhiteSpace(configuredRoot))
            {
                continue;
            }

            string canonicalRoot;
            try
            {
                canonicalRoot = ResolvePath(configuredRoot);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
            {
                throw new ArgumentException(
                    $"Configured sanctioned root '{configuredRoot}' could not be canonicalized.",
                    nameof(options),
                    ex);
            }

            if (!roots.Any(root => string.Equals(root, canonicalRoot, FileSystemPathComparison)))
            {
                roots.Add(canonicalRoot);
            }
        }

        return roots;
    }

    internal static bool IsPathAllowed(
        string canonicalPath,
        IReadOnlyList<string> canonicalConfiguredRoots,
        bool expandSanctionedRoots,
        IReadOnlyList<string>? canonicalNarrowingRoots = null)
    {
        if (!IsPathUnderAnyRoot(canonicalPath, canonicalConfiguredRoots, expandSanctionedRoots))
        {
            return false;
        }

        return canonicalNarrowingRoots is null
               || IsPathUnderAnyRoot(canonicalPath, canonicalNarrowingRoots, expandSanctionedRoots: false);
    }

    internal static bool IsPathUnderAnyRoot(
        string canonicalPath,
        IReadOnlyList<string> canonicalRoots,
        bool expandSanctionedRoots)
    {
        if (canonicalRoots.Count == 0)
        {
            return false;
        }

        foreach (var root in EnumerateAllowedRoots(canonicalRoots, expandSanctionedRoots))
        {
            if (IsPathUnderRoot(canonicalPath, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves every existing path component, rather than only the leaf entry, so an existing
    /// regular file below a symlink or junction cannot retain its deceptive logical path.
    /// </summary>
    internal static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) && !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Drive-relative and root-relative paths are ambiguous; use a fully qualified or ordinary relative path.",
                nameof(path));
        }

        return ResolvePathCore(
            path,
            new HashSet<string>(FileSystemPathComparer),
            linkResolutionDepth: 0);
    }

    private static string ResolvePathCore(
        string path,
        HashSet<string> activeLinkPaths,
        int linkResolutionDepth)
    {
        if (linkResolutionDepth > MaxLinkResolutionDepth)
        {
            throw new IOException(
                $"Path contains more than {MaxLinkResolutionDepth} nested filesystem links.");
        }

        // Do not call Path.GetFullPath until after walking the components. It lexically collapses
        // `..` before the operating system resolves an earlier symlink, which changes the physical
        // target of paths such as `allowed/link-to-outside/../secret.cs`.
        var absolutePath = Path.IsPathFullyQualified(path)
            ? path
            : Path.Combine(Environment.CurrentDirectory, path);
        var pathRoot = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(pathRoot))
        {
            throw new ArgumentException("Path could not be resolved to a filesystem root.", nameof(path));
        }

        var relativePath = absolutePath[pathRoot.Length..];
        if (string.IsNullOrEmpty(relativePath))
        {
            return Path.GetFullPath(pathRoot);
        }

        var current = pathRoot;
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
                current = GetPhysicalParent(current);
                continue;
            }

            var next = Path.Combine(current, component);
            var rawLinkTarget = TryGetRawLinkTarget(next);
            if (rawLinkTarget is null)
            {
                current = next;
                continue;
            }

            // LinkTarget preserves the immediate target exactly as stored. Do not use
            // ResolveLinkTarget(false).FullName here: FullName lexically collapses `..` before
            // this walker can resolve a preceding linked component in the target itself.
            // Re-walk every introduced component so nested aliases retain filesystem ordering.
            // The active set protects indirect cycles while still allowing a legitimate path to
            // encounter the same link again after an intervening physical parent traversal.
            var linkKey = Path.GetFullPath(next);
            if (!activeLinkPaths.Add(linkKey))
            {
                throw new IOException($"Filesystem link cycle detected at '{linkKey}'.");
            }

            try
            {
                var linkTargetPath = GetLinkTargetPath(next, rawLinkTarget);
                current = ResolvePathCore(
                    linkTargetPath,
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

    private static string? TryGetRawLinkTarget(string path) =>
        // LinkTarget inspects the directory entry rather than following its target, so it also
        // detects a dangling link that a later create/write operation would otherwise follow.
        // FileInfo works for both file and directory links on supported .NET platforms; the
        // directory/file distinction affects link creation, not immediate-target inspection.
        new FileInfo(path).LinkTarget;

    internal static string GetLinkTargetPath(string linkPath, string rawLinkTarget)
    {
        if (Path.IsPathFullyQualified(rawLinkTarget))
        {
            return rawLinkTarget;
        }

        // Windows drive-relative (`C:target`) and rooted-but-drive-less (`\target`) targets depend
        // on ambient per-drive/current-directory state. Never reinterpret those ambiguous stored
        // targets as ordinary link-parent-relative paths at a security boundary.
        if (Path.IsPathRooted(rawLinkTarget))
        {
            throw new IOException(
                $"Filesystem link '{linkPath}' has an ambiguous partially-qualified target.");
        }

        return Path.Join(Path.GetDirectoryName(linkPath), rawLinkTarget);
    }

    private static IEnumerable<string> EnumerateAllowedRoots(
        IReadOnlyList<string> canonicalRoots,
        bool expandSanctionedRoots)
    {
        foreach (var root in canonicalRoots)
        {
            yield return root;

            if (!expandSanctionedRoots)
            {
                continue;
            }

            var parent = GetWidenedParent(root);
            if (parent is not null)
            {
                yield return parent;
            }
        }
    }

    private static string? GetWidenedParent(string root)
    {
        var parent = Path.GetDirectoryName(
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.IsNullOrEmpty(parent) && Path.GetPathRoot(parent) != parent
            ? parent
            : null;
    }

    private static bool IsPathUnderRoot(string canonicalPath, string canonicalRoot)
    {
        if (string.Equals(canonicalPath, canonicalRoot, FileSystemPathComparison))
        {
            return true;
        }

        var normalizedRoot = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalPath.StartsWith(normalizedRoot, FileSystemPathComparison);
    }
}
