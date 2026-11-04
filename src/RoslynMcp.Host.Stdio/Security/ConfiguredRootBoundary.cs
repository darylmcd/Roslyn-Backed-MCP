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

        foreach (var root in canonicalRoots)
        {
            if (IsPathUnderRoot(canonicalPath, root)
                || (expandSanctionedRoots && IsPathUnderWidenedBoundary(canonicalPath, root)))
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

    /// <summary>
    /// One-level sibling-worktree widening for a configured root. An ordinary parent directory is
    /// accepted whole. A parent that is a filesystem root (<c>D:\</c>, a UNC share root, or
    /// <c>/</c>) is never accepted whole: only siblings named <c>&lt;rootName&gt;-&lt;non-empty suffix&gt;</c>
    /// are. A root that is itself a filesystem root has no parent and is not widened (so <c>/</c>
    /// never yields the prefix <c>-</c>). Paths are already physically resolved, so a prefixed
    /// sibling that is a link to elsewhere is judged by its target.
    /// </summary>
    private static bool IsPathUnderWidenedBoundary(string canonicalPath, string canonicalRoot)
    {
        var trimmedRoot = canonicalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmedRoot);
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        if (Path.GetPathRoot(parent) != parent)
        {
            return IsPathUnderRoot(canonicalPath, parent);
        }

        var rootName = Path.GetFileName(trimmedRoot);
        return rootName.Length > 0 && IsPathUnderPrefixedSibling(canonicalPath, parent, rootName + "-");
    }

    private static bool IsPathUnderPrefixedSibling(string canonicalPath, string filesystemRoot, string siblingPrefix)
    {
        var normalizedRoot = filesystemRoot.EndsWith(Path.DirectorySeparatorChar)
            ? filesystemRoot
            : filesystemRoot + Path.DirectorySeparatorChar;
        if (!canonicalPath.StartsWith(normalizedRoot, FileSystemPathComparison))
        {
            return false;
        }

        var remainder = canonicalPath.AsSpan(normalizedRoot.Length);
        var separatorIndex = remainder.IndexOf(Path.DirectorySeparatorChar);
        var firstSegment = separatorIndex < 0 ? remainder : remainder[..separatorIndex];
        return firstSegment.Length > siblingPrefix.Length
               && firstSegment.StartsWith(siblingPrefix.AsSpan(), FileSystemPathComparison);
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
