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
/// <see cref="SecurityOptions.PathValidationFailOpen"/>: when true (default) the path is
/// allowed; when false the request is rejected.
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
    /// <exception cref="System.ArgumentException">Thrown when the path falls outside all client-sanctioned roots.</exception>
    public static async Task ValidatePathAgainstRootsAsync(
        McpServer server, string path, CancellationToken ct, ILogger? logger = null, SecurityOptions? securityOptions = null)
    {
        var failOpen = securityOptions?.PathValidationFailOpen ?? true;

        try
        {
            if (server is null || server.ClientCapabilities?.Roots is null)
            {
                return;
            }

            var rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), ct).ConfigureAwait(false);
            if (rootsResult.Roots.Count == 0)
            {
                return;
            }

            var fullPath = ResolvePath(path);
            foreach (var root in rootsResult.Roots)
            {
                if (root.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var rootPath = new Uri(root.Uri).LocalPath;
                    if (string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    var normalizedRoot = rootPath.EndsWith(Path.DirectorySeparatorChar)
                        ? rootPath
                        : rootPath + Path.DirectorySeparatorChar;
                    if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            throw new ArgumentException(
                $"Path '{path}' is not under any client-sanctioned root. " +
                $"Allowed roots: {string.Join(", ", rootsResult.Roots.Select(r => r.Uri))}");
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Roots lookup can fail when the client advertises the capability but
            // doesn't fully support it (common with Claude Code and other clients).
            if (failOpen)
            {
                logger?.LogWarning(ex, "Roots lookup failed for path '{Path}' — allowing access (fail-open)", path);
            }
            else
            {
                logger?.LogWarning(ex, "Roots lookup failed for path '{Path}' — rejecting access (fail-closed)", path);
                throw new ArgumentException(
                    $"Path validation failed for '{path}': roots lookup error and fail-closed mode is enabled. " +
                    $"Set ROSLYNMCP_PATH_VALIDATION_FAIL_OPEN=true to allow access when roots lookup fails.");
            }
        }
    }

    /// <summary>
    /// Resolves a path to its canonical form, following symlinks and junctions.
    /// </summary>
    internal static string ResolvePath(string path)
    {
        var fullPath = Path.GetFullPath(path);

        // Resolve symlinks/junctions when the target exists on disk.
        // For files, check if the file itself or any directory component is a symlink.
        if (File.Exists(fullPath))
        {
            var resolved = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                return Path.GetFullPath(resolved.FullName);
            }
        }
        else if (Directory.Exists(fullPath))
        {
            var resolved = new DirectoryInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                return Path.GetFullPath(resolved.FullName);
            }
        }

        // Also check parent directories for symlinks (e.g., /allowed/link/subdir/file.cs
        // where "link" is a symlink but file.cs doesn't exist yet)
        var current = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                var resolved = new DirectoryInfo(current).ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is not null)
                {
                    // Rebase the remaining path on the resolved directory
                    var relativeTail = Path.GetRelativePath(current, fullPath);
                    return Path.GetFullPath(Path.Combine(resolved.FullName, relativeTail));
                }
            }

            current = Path.GetDirectoryName(current);
        }

        return fullPath;
    }
}
