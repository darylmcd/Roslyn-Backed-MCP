using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using RoslynMcp.Core.Services;
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

    /// <summary>
    /// Canonicalizes the operator-configured sanctioned roots. A root that cannot be canonicalized
    /// is a server configuration fault, not a caller error: it surfaces as a path-free
    /// <see cref="PublicInvalidOperationException"/> that keeps the underlying cause for server logs.
    /// </summary>
    internal static IReadOnlyList<string> GetCanonicalRoots(SecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return CanonicalizeRoots(
            options.SanctionedRoots,
            static inner => new PublicInvalidOperationException(
                "A configured sanctioned root could not be canonicalized. Check ROSLYNMCP_SANCTIONED_ROOTS on the server.",
                inner));
    }

    /// <summary>
    /// Canonicalizes request-supplied narrowing roots. These come from the caller (or the client's
    /// roots capability), so an unusable value is a caller-argument refusal attributed to
    /// <paramref name="parameterName"/>; the detail stays path-free and the cause stays server-side.
    /// </summary>
    internal static IReadOnlyList<string> GetCanonicalNarrowingRoots(
        IReadOnlyList<string> narrowingRootPaths,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(narrowingRootPaths);

        return CanonicalizeRoots(
            narrowingRootPaths,
            inner => ArgumentErrors.Redacted(
                parameterName,
                "A narrowing root could not be canonicalized.",
                inner));
    }

    private static List<string> CanonicalizeRoots(
        IReadOnlyList<string> rootPaths,
        Func<Exception, Exception> createFailure)
    {
        var roots = new List<string>(rootPaths.Count);
        foreach (var rootPath in rootPaths)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                continue;
            }

            string canonicalRoot;
            try
            {
                canonicalRoot = ResolvePath(rootPath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
            {
                throw createFailure(ex);
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
