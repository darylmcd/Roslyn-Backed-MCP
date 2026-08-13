using System.Collections.Concurrent;
using System.Text.Json;
using ModelContextProtocol.Server;
using RoslynMcp.Host.Stdio.Security;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Host.Stdio.Middleware;

/// <summary>
/// workspace-auto-load-on-demand: discovers the single solution/project a read-only tool call
/// implies, so <see cref="StructuredCallToolFilter"/> can auto-load it when <c>workspaceId</c>
/// is omitted and no workspace is loaded. Two strategies, file-anchored first:
/// <list type="number">
///   <item><b>File-anchored</b> — if the call carries a <c>filePath</c>-like argument, walk up
///   from that file's directory to the nearest <c>.slnx</c>/<c>.sln</c> (preferred) or
///   <c>.csproj</c>.</item>
///   <item><b>Query-anchored</b> — otherwise, scan each server-configured sanctioned root's top
///   level plus one level down for solution files.</item>
/// </list>
/// Discovery is deliberately conservative: it reports <see cref="DiscoveryStatus.Unique"/> only
/// when exactly one candidate is found, <see cref="DiscoveryStatus.Ambiguous"/> when two or more
/// are found (the caller fast-fails listing them rather than guessing), and
/// <see cref="DiscoveryStatus.None"/> when nothing is found or no roots are configured (the caller
/// falls back to the binder/elicitation path). It never assumes the process CWD.
/// </summary>
internal static class SolutionDiscoveryHelper
{
    private static readonly StringComparer FileSystemPathComparer =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    // Argument keys that carry a source-file path, in preference order. `filePath` is the
    // dominant convention across the read-only tool surface; `path`/`file` are rarer variants.
    private static readonly string[] FilePathArgumentKeys = ["filePath", "path", "file"];

    // Solution files preferred over projects; .slnx before .sln when both somehow coexist.
    private static readonly string[] SolutionExtensions = [".slnx", ".sln"];
    private static readonly string[] ProjectExtensions = [".csproj"];

    // solutiondiscoveryhelper-hotpath-perf: the file-anchored walk-up is bounded to this many
    // ancestor hops from the anchor file's directory so a pathological deep-nested filePath cannot
    // trigger a full walk to the filesystem root on the read-only dispatch hot path. Mirrors the
    // bounded (one-level-down) shape already used by ScanDirectoriesForSolutions.
    private const int MaxAncestorLevels = 8;

    // solutiondiscoveryhelper-hotpath-perf: short-TTL memoization of the query-anchored root scan.
    // A burst of workspaceId-omitted read-only dispatches (no workspace loaded) would otherwise
    // re-walk every configured root's top level + one level down on every call. Keyed by the
    // sorted root-directory set using platform path casing; scoped to the cold no-workspace path
    // only, where a short staleness window (a solution created inside the TTL is not seen until
    // expiry) is low-stakes because the caller falls back to binder/elicitation regardless.
    private static readonly TimeSpan RootScanCacheTtl = TimeSpan.FromSeconds(10);
    private static readonly ConcurrentDictionary<string, (DiscoveryResult Result, DateTime ExpiresUtc)> s_rootScanCache =
        new(StringComparer.Ordinal);

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
        CancellationToken cancellationToken,
        SecurityOptions? securityOptions = null)
    {
        var options = ConfiguredRootBoundary.ResolveOptions(server, securityOptions);

        // solutiondiscoveryhelper-hotpath-perf: off-load the synchronous ancestor-walk directory I/O
        // to the thread pool so it does not block the calling async continuation's thread. .NET has
        // no true async Directory API, so Task.Run off-loading plus the walk's own depth cap is the
        // correct "off the hot synchronous path" fix. The method's own behavior/signature is
        // unchanged — only how its single caller invokes it.
        var fileAnchored = await Task.Run(
            () => TryDiscoverFromFilePathWithinBoundary(arguments, options),
            cancellationToken)
            .ConfigureAwait(false);
        if (fileAnchored.Status != DiscoveryStatus.None)
        {
            return fileAnchored;
        }

        return await Task.Run(
            () => TryDiscoverFromConfiguredRoots(options, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// File-anchored discovery: walk up from the directory of the call's <c>filePath</c>-like
    /// argument, preferring a solution file at each level, falling back to a project file.
    /// </summary>
    internal static DiscoveryResult TryDiscoverFromFilePath(IDictionary<string, JsonElement>? arguments)
    {
        var filePath = ExtractFilePath(arguments);
        return TryDiscoverFromFilePath(filePath, canonicalBoundaryRoots: null);
    }

    private static DiscoveryResult TryDiscoverFromFilePathWithinBoundary(
        IDictionary<string, JsonElement>? arguments,
        SecurityOptions options)
    {
        var filePath = ExtractFilePath(arguments);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return DiscoveryResult.None;
        }

        var configuredRoots = ConfiguredRootBoundary.GetCanonicalRoots(options);
        if (configuredRoots.Count == 0)
        {
            return options.PathValidationFailOpen
                ? TryDiscoverFromFilePath(filePath, canonicalBoundaryRoots: null)
                : DiscoveryResult.None;
        }

        string canonicalFilePath;
        try
        {
            canonicalFilePath = ConfiguredRootBoundary.ResolvePath(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                   or UnauthorizedAccessException)
        {
            return DiscoveryResult.None;
        }

        if (!ConfiguredRootBoundary.IsPathUnderAnyRoot(
                canonicalFilePath,
                configuredRoots,
                expandSanctionedRoots: false))
        {
            return DiscoveryResult.None;
        }

        return TryDiscoverFromFilePath(canonicalFilePath, configuredRoots);
    }

    private static DiscoveryResult TryDiscoverFromFilePath(
        string? filePath,
        IReadOnlyList<string>? canonicalBoundaryRoots)
    {
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

        var levelsWalked = 0;
        while (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            if (canonicalBoundaryRoots is not null
                && !ConfiguredRootBoundary.IsPathUnderAnyRoot(
                    directory,
                    canonicalBoundaryRoots,
                    expandSanctionedRoots: false))
            {
                break;
            }

            var solutions = EnumerateByExtensions(
                directory,
                SolutionExtensions,
                canonicalBoundaryRoots);
            if (solutions.Count > 0)
            {
                return DiscoveryResult.FromCandidates(solutions);
            }

            var projects = EnumerateByExtensions(
                directory,
                ProjectExtensions,
                canonicalBoundaryRoots);
            if (projects.Count > 0)
            {
                return DiscoveryResult.FromCandidates(projects);
            }

            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
            {
                break;
            }

            // solutiondiscoveryhelper-hotpath-perf: bound the ancestor walk-up so a deeply-nested
            // anchor file cannot trigger a walk all the way to the filesystem root.
            if (++levelsWalked >= MaxAncestorLevels)
            {
                break;
            }

            directory = parent;
        }

        return DiscoveryResult.None;
    }

    /// <summary>
    /// Query-anchored discovery: scan the canonical server-configured sanctioned roots. Returns
    /// <see cref="DiscoveryStatus.None"/> when no roots are configured or no candidate exists.
    /// </summary>
    private static DiscoveryResult TryDiscoverFromConfiguredRoots(
        SecurityOptions options,
        CancellationToken cancellationToken)
    {
        var rootDirectories = ConfiguredRootBoundary.GetCanonicalRoots(options);
        return ScanDirectoriesForSolutionsCached(rootDirectories, cancellationToken);
    }

    /// <summary>
    /// solutiondiscoveryhelper-hotpath-perf: short-TTL memoized wrapper around
    /// <see cref="ScanDirectoriesForSolutions"/>. A burst of workspaceId-omitted read-only
    /// dispatches within <see cref="RootScanCacheTtl"/> reuses the last scan for the same declared
    /// root set instead of re-walking every root's tree. <see cref="DiscoveryStatus.None"/> results
    /// are cached too (that is the burst case). Internal for direct coverage of the cache-hit /
    /// staleness-window behavior.
    /// </summary>
    internal static DiscoveryResult ScanDirectoriesForSolutionsCached(
        IReadOnlyList<string> rootDirectories, CancellationToken cancellationToken)
    {
        if (rootDirectories.Count == 0)
        {
            return DiscoveryResult.None;
        }

        var cacheKey = string.Join('\0', rootDirectories.OrderBy(d => d, FileSystemPathComparer));
        var nowUtc = DateTime.UtcNow;

        if (s_rootScanCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresUtc > nowUtc)
        {
            return cached.Result;
        }

        var result = ScanDirectoriesForSolutions(rootDirectories, cancellationToken);
        s_rootScanCache[cacheKey] = (result, nowUtc.Add(RootScanCacheTtl));
        return result;
    }

    /// <summary>
    /// Pure, testable scan: for each root directory, collect solution files at the top level and
    /// one level down. Distinct using the platform filesystem comparison. Bounded (one level of
    /// recursion only) so the
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

            found.AddRange(EnumerateByExtensions(
                rootDirectory,
                SolutionExtensions,
                rootDirectories));

            string[] subdirectories;
            try
            {
                // Materialize inside the guarded block: EnumerateDirectories is lazy and can
                // otherwise throw during foreach, outside this best-effort discovery boundary.
                subdirectories = Directory.GetDirectories(rootDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subdirectory in subdirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsLinkedDirectory(subdirectory))
                {
                    // Never follow a symlink/junction child out of the configured physical root.
                    // Configured roots themselves were canonicalized before this bounded scan.
                    continue;
                }

                found.AddRange(EnumerateByExtensions(
                    subdirectory,
                    SolutionExtensions,
                    rootDirectories));
            }
        }

        var distinct = found
            .Distinct(FileSystemPathComparer)
            .OrderBy(path => path, FileSystemPathComparer)
            .ToArray();
        return DiscoveryResult.FromCandidates(distinct);
    }

    private static bool IsLinkedDirectory(string path)
    {
        try
        {
            return (new DirectoryInfo(path).Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable child cannot contribute a safe discovery candidate.
            return true;
        }
    }

    private static IReadOnlyList<string> EnumerateByExtensions(
        string directory,
        string[] extensions,
        IReadOnlyList<string>? canonicalBoundaryRoots = null)
    {
        var matches = new List<string>();
        foreach (var extension in extensions)
        {
            try
            {
                foreach (var match in Directory.EnumerateFiles(directory, "*" + extension))
                {
                    if (canonicalBoundaryRoots is null)
                    {
                        matches.Add(match);
                        continue;
                    }

                    var canonicalMatch = ConfiguredRootBoundary.ResolvePath(match);
                    if (ConfiguredRootBoundary.IsPathUnderAnyRoot(
                            canonicalMatch,
                            canonicalBoundaryRoots,
                            expandSanctionedRoots: false))
                    {
                        matches.Add(canonicalMatch);
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException
                                       or UnauthorizedAccessException)
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

}
