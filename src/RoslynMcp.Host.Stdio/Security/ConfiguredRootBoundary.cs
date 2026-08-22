using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>
/// Canonical server-owned path boundary shared by path validation and solution discovery.
/// </summary>
internal static class ConfiguredRootBoundary
{
    private static readonly StringComparison FileSystemPathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Compares two already-canonical filesystem paths using this boundary's own platform-aware
    /// comparison. Exposed so security checks outside this type cannot drift into a hardcoded
    /// comparison that is more permissive than the boundary it defends — on a case-sensitive
    /// filesystem a hardcoded <see cref="StringComparison.OrdinalIgnoreCase"/> would treat two
    /// genuinely distinct files as identical.
    /// </summary>
    internal static bool PathsEqual(string left, string right) =>
        string.Equals(left, right, FileSystemPathComparison);

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
        => PhysicalPathResolver.Resolve(path);

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
