using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Security;

/// <summary>
/// Compatibility adapter for clients that still advertise MCP Roots. Configured server roots
/// remain authoritative: values returned here are an additional narrowing boundary only and are
/// never used for query-anchored discovery.
/// </summary>
#pragma warning disable MCP9005
internal static class LegacyClientRootsNarrowingAdapter
{
    internal static async Task<IReadOnlyList<string>?> TryGetNarrowingRootsAsync(
        McpServer? server,
        CancellationToken cancellationToken,
        ILogger? logger)
    {
        if (server is null || !AdvertisesRoots(server))
        {
            return null;
        }

        ListRootsResult response;
        try
        {
            response = await RequestRootsAsync(server, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or McpException)
        {
            logger?.LogWarning(ex, "Client roots lookup failed; rejecting path access");
            throw new ArgumentException(
                "Path validation failed because the client roots narrowing boundary could not be obtained.",
                nameof(server),
                ex);
        }

        var rootPaths = new List<string>(response.Roots.Count);
        foreach (var root in response.Roots)
        {
            if (Uri.TryCreate(root.Uri, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                rootPaths.Add(uri.LocalPath);
            }
        }

        // An advertised-but-empty (or entirely non-file) response deliberately returns an empty
        // list. The validator interprets that as "allow nothing", which is the safe narrowing
        // behavior for malformed or unsupported root schemes.
        return rootPaths;
    }

    // MCP9005 is isolated to this adapter for the compatibility window. Production authority
    // lives in SecurityOptions; this deprecated capability can only further narrow it.
    private static bool AdvertisesRoots(McpServer server) =>
        server.ClientCapabilities?.Roots is not null;

    private static ValueTask<ListRootsResult> RequestRootsAsync(
        McpServer server,
        CancellationToken cancellationToken) =>
        server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken);
}
#pragma warning restore MCP9005
