using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Validates that a requested workspace path falls under one of the root directories
/// sanctioned by the MCP client.
/// </summary>
/// <remarks>
/// If the client does not advertise roots capability, or if the roots list is empty,
/// the path is allowed unconditionally. If the roots request itself fails (e.g. the
/// client advertises the capability but doesn't fully support it), behavior depends on
/// <see cref="SecurityOptions.PathValidationFailOpen"/>: when false (default) the request
/// is rejected (fail-closed); when true the path is allowed (fail-open). When no
/// <see cref="SecurityOptions"/> is supplied, the fail-closed default applies.
/// Symlinks and junctions are resolved before comparison to prevent traversal attacks.
/// </remarks>
internal static class ClientRootPathValidator
{
    /// <summary>
    /// Verifies that <paramref name="path"/> is located under at least one of the roots
    /// reported by the MCP client.
    /// </summary>
    /// <param name="server">The active MCP server instance used to query client roots.</param>
    /// <param name="path">The file-system path to validate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="securityOptions">Security options controlling fail-open/fail-closed behavior.</param>
    /// <param name="expandSanctionedRoots">
    /// Operator-opt-in flag (default <c>false</c>). When <c>true</c>, the validator additionally
    /// accepts paths under the immediate parent directory of any client-sanctioned root. This
    /// widens the allowlist by exactly one level — enough to permit a disposable sibling worktree
    /// at <c>../&lt;sibling&gt;</c> (e.g. mcp-server-surface-test's audit worktree) without
    /// exposing arbitrary filesystem locations. Higher ancestors (grandparent etc.) remain
    /// disallowed. This flag must only be plumbed through tools whose own parameter is operator-
    /// supplied — never auto-enable on every request.
    /// </param>
    /// <exception cref="System.ArgumentException">Thrown when the path falls outside all client-sanctioned roots.</exception>
    public static async Task ValidatePathAgainstRootsAsync(
        McpServer server,
        string path,
        CancellationToken ct,
        ILogger? logger = null,
        SecurityOptions? securityOptions = null,
        bool expandSanctionedRoots = false)
    {
        var failOpen = securityOptions?.PathValidationFailOpen ?? false;

        if (server is null || server.ClientCapabilities?.Roots is null)
        {
            return;
        }

        try
        {
            await ValidateAgainstAdvertisedRootsAsync(
                server,
                path,
                expandSanctionedRoots,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleRootsLookupFailure(path, failOpen, logger, ex);
        }
    }

    private static async Task ValidateAgainstAdvertisedRootsAsync(
        McpServer server,
        string path,
        bool expandSanctionedRoots,
        CancellationToken ct)
    {
        var rootsResult = await server.RequestRootsAsync(
            new ListRootsRequestParams(),
            ct).ConfigureAwait(false);
        if (rootsResult.Roots.Count == 0)
        {
            return;
        }

        var fullPath = await ResolvePathAsync(path, ct).ConfigureAwait(false);
        var rootPaths = rootsResult.Roots
            .Where(root => root.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            .Select(root => new Uri(root.Uri).LocalPath)
            .ToList();
        if (IsPathUnderAnyRoot(fullPath, rootPaths, expandSanctionedRoots))
        {
            return;
        }

        var widenedNote = expandSanctionedRoots
            ? " (expandSanctionedRoots=true was applied — parent directories of each root were also checked)"
            : string.Empty;
        throw new ArgumentException(
            $"Path '{path}' is not under any client-sanctioned root. " +
            $"Allowed roots: {string.Join(", ", rootsResult.Roots.Select(root => root.Uri))}{widenedNote}");
    }

    private static void HandleRootsLookupFailure(
        string path,
        bool failOpen,
        ILogger? logger,
        Exception exception)
    {
        if (failOpen)
        {
            logger?.LogWarning(
                exception,
                "Roots lookup failed for path '{Path}' — allowing access (fail-open)",
                path);
            return;
        }

        logger?.LogWarning(
            exception,
            "Roots lookup failed for path '{Path}' — rejecting access (fail-closed)",
            path);
        throw new ArgumentException(
            $"Path validation failed for '{path}': roots lookup error and fail-closed mode is enabled. " +
            "Set ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true to allow access when roots lookup fails.");
    }

    internal static Task<string> ResolvePathAsync(string path, CancellationToken ct) =>
        Task.Run(() => ResolvePath(path), ct);

    /// <summary>
    /// Pure path-match helper extracted for unit-testability of the sanctioned-root
    /// allowlist + <c>expandSanctionedRoots</c> widening logic. <paramref name="fullPath"/>
    /// must already be canonicalized via <see cref="ResolvePath"/>; <paramref name="rootPaths"/>
    /// must be the <c>file://</c>-decoded LocalPath strings reported by the client.
    /// </summary>
    /// <param name="fullPath">Canonicalized candidate path.</param>
    /// <param name="rootPaths">Decoded client-sanctioned roots (local paths, not URIs).</param>
    /// <param name="expandSanctionedRoots">When <c>true</c>, also accept paths under the
    /// immediate parent directory of each root (one level of widening only). Drive-root
    /// parents are excluded so a root at <c>C:\Foo</c> does NOT widen to the entire drive.</param>
    /// <returns><c>true</c> when <paramref name="fullPath"/> falls under any root (or, with
    /// widening, any root's parent directory).</returns>
    internal static bool IsPathUnderAnyRoot(string fullPath, IReadOnlyList<string> rootPaths, bool expandSanctionedRoots)
    {
        if (rootPaths.Count == 0)
        {
            return false;
        }

        foreach (var rootPath in EnumerateAllowedRoots(rootPaths, expandSanctionedRoots))
        {
            if (IsPathUnderRoot(fullPath, rootPath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lazily yields each sanctioned root, and — when <paramref name="expandSanctionedRoots"/>
    /// is <c>true</c> — the one-level-widened parent of each root (drive roots excluded).
    /// Extracted from <see cref="IsPathUnderAnyRoot"/> so root-set construction is a pure
    /// iterator rather than an eagerly materialized list.
    /// </summary>
    /// <remarks>
    /// Must be enumerated exactly once per call (the single <c>foreach</c> in
    /// <see cref="IsPathUnderAnyRoot"/> satisfies this) — a caller that enumerates it twice
    /// would silently re-run <see cref="Path.GetDirectoryName(string?)"/> /
    /// <see cref="Path.GetPathRoot(string?)"/> per pass.
    /// </remarks>
    private static IEnumerable<string> EnumerateAllowedRoots(IReadOnlyList<string> rootPaths, bool expandSanctionedRoots)
    {
        foreach (var rootPath in rootPaths)
        {
            yield return rootPath;

            if (expandSanctionedRoots)
            {
                var parent = GetWidenedParent(rootPath);
                if (parent is not null)
                {
                    yield return parent;
                }
            }
        }
    }

    /// <summary>
    /// Widens by exactly one level — the parent directory of <paramref name="rootPath"/>.
    /// This permits sibling worktrees (e.g. parent/main is sanctioned and the worktree is at
    /// parent/.worktrees/foo) without exposing arbitrary grandparent or unrelated filesystem
    /// locations. Returns <c>null</c> when there is no parent, or when the parent is a drive
    /// root (to avoid widening to the entire drive).
    /// </summary>
    private static string? GetWidenedParent(string rootPath)
    {
        var parent = Path.GetDirectoryName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent) && Path.GetPathRoot(parent) != parent)
        {
            return parent;
        }

        return null;
    }

    /// <summary>
    /// Tests whether <paramref name="fullPath"/> equals <paramref name="rootPath"/> exactly,
    /// or falls under it as a separator-bounded child. Both checks are
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> to match Windows path semantics.
    /// </summary>
    private static bool IsPathUnderRoot(string fullPath, string rootPath)
    {
        if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves a path to its canonical form, following symlinks and junctions.
    /// </summary>
    internal static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return ResolveExistingEntry(fullPath)
            ?? ResolveAncestorLink(fullPath)
            ?? fullPath;
    }

    private static string? ResolveExistingEntry(string fullPath)
    {
        if (File.Exists(fullPath))
        {
            var resolved = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            return resolved is null ? null : Path.GetFullPath(resolved.FullName);
        }

        if (Directory.Exists(fullPath))
        {
            var resolved = new DirectoryInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            return resolved is null ? null : Path.GetFullPath(resolved.FullName);
        }

        return null;
    }

    private static string? ResolveAncestorLink(string fullPath)
    {
        var current = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(current))
        {
            if (Path.GetPathRoot(current) == current)
            {
                return null;
            }

            if (Directory.Exists(current))
            {
                var resolved = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is not null)
                {
                    var relativeTail = Path.GetRelativePath(current, fullPath);
                    return Path.GetFullPath(Path.Combine(resolved.FullName, relativeTail));
                }
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}
