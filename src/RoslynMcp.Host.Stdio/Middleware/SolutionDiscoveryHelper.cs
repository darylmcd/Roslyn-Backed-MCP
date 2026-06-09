using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// workspace-auto-load-on-demand: discovers the single solution/project a read-only tool call
/// implies, so <see cref="StructuredCallToolFilter"/> can auto-load it when <c>workspaceId</c>
/// is omitted and no workspace is loaded. Two strategies, file-anchored first:
/// <list type="number">
///   <item><b>File-anchored</b> — if the call carries a <c>filePath</c>-like argument, walk up
///   from that file's directory to the nearest <c>.slnx</c>/<c>.sln</c> (preferred) or
///   <c>.csproj</c>.</item>
///   <item><b>Query-anchored</b> — otherwise, ask the client for its declared roots
///   (<c>roots/list</c>, mirroring <see cref="ClientRootPathValidator"/>) and scan each root's
///   top level plus one level down for solution files.</item>
/// </list>
/// Discovery is deliberately conservative: it reports <see cref="DiscoveryStatus.Unique"/> only
/// when exactly one candidate is found, <see cref="DiscoveryStatus.Ambiguous"/> when two or more
/// are found (the caller fast-fails listing them rather than guessing), and
/// <see cref="DiscoveryStatus.None"/> when nothing is found or no roots are declared (the caller
/// falls back to the binder/elicitation path). It never assumes the process CWD.
/// </summary>
internal static class SolutionDiscoveryHelper
{
    // Argument keys that carry a source-file path, in preference order. `filePath` is the
    // dominant convention across the read-only tool surface; `path`/`file` are rarer variants.
    private static readonly string[] FilePathArgumentKeys = ["filePath", "path", "file"];

    // Solution files preferred over projects; .slnx before .sln when both somehow coexist.
    private static readonly string[] SolutionExtensions = [".slnx", ".sln"];
    private static readonly string[] ProjectExtensions = [".csproj"];

    internal enum DiscoveryStatus
    {
        /// <summary>No candidate found (or no roots declared) — fall back to binder/elicitation.</summary>
        None,
        /// <summary>Exactly one candidate — safe to auto-load.</summary>
        Unique,
        /// <summary>Two or more candidates — fast-fail listing them rather than guessing.</summary>
        Ambiguous,
    }

    internal readonly record struct DiscoveryResult(
        DiscoveryStatus Status,
        string? UniquePath,
        IReadOnlyList<string> Candidates)
    {
        internal static DiscoveryResult None { get; } =
            new(DiscoveryStatus.None, null, []);

        internal static DiscoveryResult Unique(string path) =>
            new(DiscoveryStatus.Unique, path, [path]);

        internal static DiscoveryResult FromCandidates(IReadOnlyList<string> candidates) => candidates.Count switch
        {
            0 => None,
            1 => Unique(candidates[0]),
            _ => new DiscoveryResult(DiscoveryStatus.Ambiguous, null, candidates),
        };
    }

    /// <summary>
    /// Runs file-anchored discovery first, then query-anchored discovery if the former finds
    /// nothing. Returns the first non-<see cref="DiscoveryStatus.None"/> result.
    /// </summary>
    internal static async Task<DiscoveryResult> TryDiscoverAsync(
        IDictionary<string, JsonElement>? arguments,
        McpServer? server,
        CancellationToken cancellationToken)
    {
        var fileAnchored = TryDiscoverFromFilePath(arguments);
        if (fileAnchored.Status != DiscoveryStatus.None)
        {
            return fileAnchored;
        }

        return await TryDiscoverFromRootsAsync(server, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// File-anchored discovery: walk up from the directory of the call's <c>filePath</c>-like
    /// argument, preferring a solution file at each level, falling back to a project file.
    /// </summary>
    internal static DiscoveryResult TryDiscoverFromFilePath(IDictionary<string, JsonElement>? arguments)
    {
        var filePath = ExtractFilePath(arguments);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return DiscoveryResult.None;
        }

        string? directory;
        try
        {
            directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return DiscoveryResult.None;
        }

        while (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            var solutions = EnumerateByExtensions(directory, SolutionExtensions);
            if (solutions.Count > 0)
            {
                return DiscoveryResult.FromCandidates(solutions);
            }

            var projects = EnumerateByExtensions(directory, ProjectExtensions);
            if (projects.Count > 0)
            {
                return DiscoveryResult.FromCandidates(projects);
            }

            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            directory = parent;
        }

        return DiscoveryResult.None;
    }

    /// <summary>
    /// Query-anchored discovery: fetch the client's declared roots and scan them. Returns
    /// <see cref="DiscoveryStatus.None"/> when the client declares no roots capability, returns
    /// no roots, or the roots request fails (advertised-but-unsupported clients).
    /// </summary>
    private static async Task<DiscoveryResult> TryDiscoverFromRootsAsync(
        McpServer? server, CancellationToken cancellationToken)
    {
        if (server is null || server.ClientCapabilities?.Roots is null)
        {
            return DiscoveryResult.None;
        }

        IReadOnlyList<string> rootDirectories;
        try
        {
            var rootsResult = await server.RequestRootsAsync(new ListRootsRequestParams(), cancellationToken)
                .ConfigureAwait(false);
            rootDirectories = rootsResult.Roots
                .Where(root => root.Uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                .Select(TryDecodeLocalPath)
                .Where(path => path is not null)
                .Select(path => path!)
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Advertised-but-unsupported roots, transport hiccup, etc. — treat as zero candidates.
            return DiscoveryResult.None;
        }

        return ScanDirectoriesForSolutions(rootDirectories, cancellationToken);
    }

    /// <summary>
    /// Pure, testable scan: for each root directory, collect solution files at the top level and
    /// one level down. Distinct, case-insensitive. Bounded (one level of recursion only) so the
    /// cold-path scan cannot walk an arbitrarily deep tree.
    /// </summary>
    internal static DiscoveryResult ScanDirectoriesForSolutions(
        IReadOnlyList<string> rootDirectories, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        foreach (var rootDirectory in rootDirectories)
        {
            if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                continue;
            }

            found.AddRange(EnumerateByExtensions(rootDirectory, SolutionExtensions));

            IEnumerable<string> subdirectories;
            try
            {
                subdirectories = Directory.EnumerateDirectories(rootDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                found.AddRange(EnumerateByExtensions(subdirectory, SolutionExtensions));
            }
        }

        var distinct = found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return DiscoveryResult.FromCandidates(distinct);
    }

    private static IReadOnlyList<string> EnumerateByExtensions(string directory, string[] extensions)
    {
        var matches = new List<string>();
        foreach (var extension in extensions)
        {
            try
            {
                matches.AddRange(Directory.EnumerateFiles(directory, "*" + extension));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable directory — skip it; discovery is best-effort.
            }
        }

        return matches;
    }

    private static string? ExtractFilePath(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        foreach (var key in FilePathArgumentKeys)
        {
            if (arguments.TryGetValue(key, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var candidate = value.GetString();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string? TryDecodeLocalPath(Root root)
    {
        try
        {
            return new Uri(root.Uri).LocalPath;
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}
