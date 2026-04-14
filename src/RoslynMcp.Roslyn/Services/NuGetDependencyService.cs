using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Reports NuGet package references and known vulnerabilities for a workspace. Reads
/// evaluated MSBuild items so references inherited from <c>Directory.Build.props</c>
/// match <c>evaluate_msbuild_items</c> and the real restore graph. Vulnerability scans
/// shell out to <c>dotnet list package --vulnerable --format json</c> via the gated
/// command executor and parse results through
/// <see cref="NuGetVulnerabilityJsonParser"/>. Split out of the legacy
/// <c>DependencyAnalysisService</c> as part of the SRP refactor.
/// </summary>
public sealed class NuGetDependencyService : INuGetDependencyService
{
    private readonly IWorkspaceManager _workspace;
    private readonly IGatedCommandExecutor _executor;
    private readonly IMsBuildEvaluationService _msBuildEvaluation;
    private readonly ILogger<NuGetDependencyService> _logger;
    private readonly ValidationServiceOptions _options;

    /// <summary>
    /// nuget-vuln-scan-caching: vulnerability scans shell out to <c>dotnet list package
    /// --vulnerable</c>, which makes a network call and runs ~11 s on Jellyfin. Package
    /// references rarely change between calls in a session, so cache results keyed on
    /// (workspaceVersion, projectFilter, includeTransitive, lockfileHash). The lockfile hash
    /// catches cases where Directory.Packages.props or per-project lock files changed without
    /// the workspace version bumping (e.g. external edits without workspace_reload).
    /// </summary>
    private readonly ConcurrentDictionary<string, VulnCacheEntry> _vulnCache = new(StringComparer.Ordinal);

    private sealed record VulnCacheEntry(int Version, ConcurrentDictionary<VulnCacheKey, NuGetVulnerabilityScanResultDto> ByKey);

    private sealed record VulnCacheKey(string ProjectFilter, bool IncludeTransitive, string LockfileHash);

    private const int MaxVulnCacheEntriesPerWorkspace = 4;

    public NuGetDependencyService(
        IWorkspaceManager workspace,
        IGatedCommandExecutor executor,
        IMsBuildEvaluationService msBuildEvaluation,
        ILogger<NuGetDependencyService> logger,
        ValidationServiceOptions? options = null)
    {
        _workspace = workspace;
        _executor = executor;
        _msBuildEvaluation = msBuildEvaluation;
        _logger = logger;
        _options = options ?? new ValidationServiceOptions();
        _workspace.WorkspaceClosed += workspaceId => _vulnCache.TryRemove(workspaceId, out _);
    }

    public async Task<NuGetDependencyResultDto> GetNuGetDependenciesAsync(
        string workspaceId, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projectDtos = new List<NuGetProjectDto>();
        var packageMap = new Dictionary<(string Id, string Version), List<string>>();
        var packagesPropsPath = MsBuildMetadataHelper.FindDirectoryPackagesProps(_workspace.GetStatus(workspaceId).LoadedPath);

        foreach (var project in solution.Projects)
        {
            if (ct.IsCancellationRequested) break;
            if (project.FilePath is null) continue;

            var packages = new List<NuGetPackageReferenceDto>();

            try
            {
                // BUG-N7: Use evaluated MSBuild items so PackageReference from Directory.Build.props
                // / targets / imports matches `evaluate_msbuild_items` and the real restore graph.
                var evaluated = await _msBuildEvaluation.EvaluateItemsAsync(
                    workspaceId, project.Name, "PackageReference", ct).ConfigureAwait(false);

                foreach (var item in evaluated.Items)
                {
                    var id = item.Include;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    item.Metadata.TryGetValue("Version", out var version);
                    version ??= "centrally-managed";

                    string? resolvedCentral = null;
                    if (string.Equals(version, "centrally-managed", StringComparison.OrdinalIgnoreCase) &&
                        packagesPropsPath is not null)
                    {
                        resolvedCentral = MsBuildMetadataHelper.TryGetCentralPackageVersion(packagesPropsPath, id);
                    }

                    packages.Add(new NuGetPackageReferenceDto(id, version, resolvedCentral));

                    var key = (id, version);
                    if (!packageMap.TryGetValue(key, out var users))
                    {
                        users = [];
                        packageMap[key] = users;
                    }

                    users.Add(project.Name);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Failed to enumerate PackageReference items for {Project} via MSBuild", project.Name);
            }

            projectDtos.Add(new NuGetProjectDto(project.Name, project.FilePath, packages));
        }

        var packageDtos = packageMap.Select(kvp =>
        {
            var displayVersion = kvp.Key.Version;
            if (string.Equals(kvp.Key.Version, "centrally-managed", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = projectDtos
                    .SelectMany(p => p.PackageReferences)
                    .Where(pr => string.Equals(pr.PackageId, kvp.Key.Id, StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(pr.Version, "centrally-managed", StringComparison.OrdinalIgnoreCase))
                    .Select(pr => pr.ResolvedCentralVersion)
                    .FirstOrDefault(rv => !string.IsNullOrEmpty(rv));
                if (resolved is not null)
                    displayVersion = resolved;
            }

            return new NuGetPackageDto(kvp.Key.Id, displayVersion, kvp.Value);
        }).ToList();

        return new NuGetDependencyResultDto(packageDtos, projectDtos);
    }

    public async Task<NuGetVulnerabilityScanResultDto> ScanNuGetVulnerabilitiesAsync(
        string workspaceId,
        string? projectFilter,
        bool includeTransitive,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var status = await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(status.LoadedPath))
        {
            throw new InvalidOperationException($"Workspace '{workspaceId}' is not loaded.");
        }

        var targetPath = string.IsNullOrWhiteSpace(projectFilter)
            ? status.LoadedPath
            : _executor.ResolveProject(workspaceId, projectFilter).FilePath;

        // nuget-vuln-scan-caching: build the cache key from workspaceVersion + lockfile hash
        // so external edits to Directory.Packages.props or packages.lock.json invalidate
        // cleanly even if the workspace version didn't tick.
        var version = _workspace.GetCurrentVersion(workspaceId);
        var lockfileHash = ComputeLockfileHash(status.LoadedPath, projectFilter);
        var cacheKey = new VulnCacheKey(projectFilter ?? "<all>", includeTransitive, lockfileHash);

        var entry = _vulnCache.AddOrUpdate(
            workspaceId,
            _ => new VulnCacheEntry(version, new ConcurrentDictionary<VulnCacheKey, NuGetVulnerabilityScanResultDto>()),
            (_, existing) => existing.Version == version
                ? existing
                : new VulnCacheEntry(version, new ConcurrentDictionary<VulnCacheKey, NuGetVulnerabilityScanResultDto>()));

        if (entry.ByKey.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var args = new List<string> { "list", targetPath, "package", "--vulnerable", "--format", "json" };
        if (includeTransitive)
        {
            args.Add("--include-transitive");
        }

        var execution = await _executor.ExecuteAsync(
            workspaceId,
            targetPath,
            args,
            _options.VulnerabilityScanTimeout,
            ct).ConfigureAwait(false);

        sw.Stop();

        if (!execution.Succeeded)
        {
            var err = string.IsNullOrWhiteSpace(execution.StdErr) ? execution.StdOut : execution.StdErr;
            throw new InvalidOperationException(
                $"dotnet list package --vulnerable failed (exit {execution.ExitCode}). {err.Trim()}");
        }

        var vulnerabilities = NuGetVulnerabilityJsonParser.Parse(execution.StdOut, out var scannedProjects);
        var critical = vulnerabilities.Count(v => string.Equals(v.Severity, "Critical", StringComparison.OrdinalIgnoreCase));
        var high = vulnerabilities.Count(v => string.Equals(v.Severity, "High", StringComparison.OrdinalIgnoreCase));
        var medium = vulnerabilities.Count(v => string.Equals(v.Severity, "Medium", StringComparison.OrdinalIgnoreCase));
        var low = vulnerabilities.Count(v => string.Equals(v.Severity, "Low", StringComparison.OrdinalIgnoreCase));

        var result = new NuGetVulnerabilityScanResultDto(
            vulnerabilities,
            scannedProjects,
            vulnerabilities.Count,
            critical,
            high,
            medium,
            low,
            includeTransitive,
            sw.ElapsedMilliseconds);

        // nuget-vuln-scan-caching: store under the existing per-workspace entry. Cap at 4
        // entries so memory stays bounded for chatty (projectFilter, includeTransitive) combos.
        if (entry.ByKey.Count >= MaxVulnCacheEntriesPerWorkspace)
        {
            var someKey = entry.ByKey.Keys.FirstOrDefault();
            if (someKey is not null) entry.ByKey.TryRemove(someKey, out _);
        }
        entry.ByKey[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// Hashes the lockfile-equivalent inputs (Directory.Packages.props and per-project
    /// packages.lock.json) so cache entries invalidate when package references change without
    /// a workspace version bump. Conservative — when the relevant files don't exist or can't
    /// be read, returns "<unknown>" which still keys the cache (just less precisely).
    /// </summary>
    private static string ComputeLockfileHash(string solutionOrProjectPath, string? projectFilter)
    {
        try
        {
            var rootDir = Path.GetDirectoryName(solutionOrProjectPath);
            if (string.IsNullOrEmpty(rootDir)) return "<unknown>";

            var inputs = new List<string>();

            // Directory.Packages.props (CPM) — solution-level
            var packagesProps = Path.Combine(rootDir, "Directory.Packages.props");
            if (File.Exists(packagesProps))
            {
                inputs.Add(File.ReadAllText(packagesProps));
            }

            // Per-project packages.lock.json files. For project filter, narrow to that subtree;
            // otherwise scan the solution root for all lock files.
            var lockFileSearchRoot = string.IsNullOrWhiteSpace(projectFilter) ? rootDir : rootDir;
            foreach (var lockFile in Directory.EnumerateFiles(lockFileSearchRoot, "packages.lock.json", SearchOption.AllDirectories))
            {
                inputs.Add(lockFile);
                inputs.Add(File.ReadAllText(lockFile));
            }

            if (inputs.Count == 0) return "<no-lockfiles>";

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u0000", inputs)));
            return Convert.ToHexString(bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "<unreadable>";
        }
    }
}
