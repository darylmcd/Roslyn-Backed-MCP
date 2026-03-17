using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Company.RoslynMcp.Host.Stdio.Tools;

internal static class ClientRootPathValidator
{
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
                    if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
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