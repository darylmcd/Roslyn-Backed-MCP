using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Tools;

/// <summary>
/// Validates that a requested workspace path falls under one of the root directories
/// sanctioned by the MCP client.
/// </summary>
/// <remarks>
/// If the client does not advertise roots capability, or if the roots list is empty,
/// the path is allowed unconditionally. If the roots request itself fails, the path
/// is also allowed to avoid blocking legitimate operations.
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
    /// <exception cref="System.ArgumentException">Thrown when the path falls outside all client-sanctioned roots.</exception>
    public static async Task ValidatePathAgainstRootsAsync(McpServer server, string path, CancellationToken ct)
    {
        try
        {
            if (server.ClientCapabilities?.Roots is null)
            {
                return;
            }

            var rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), ct).ConfigureAwait(false);
            if (rootsResult.Roots.Count == 0)
            {
                return;
            }

            var fullPath = Path.GetFullPath(path);
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
        catch
        {
            // If roots request fails, allow the operation.
        }
    }
}