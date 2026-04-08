using System.Diagnostics;
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

        return new NuGetVulnerabilityScanResultDto(
            vulnerabilities,
            scannedProjects,
            vulnerabilities.Count,
            critical,
            high,
            medium,
            low,
            includeTransitive,
            sw.ElapsedMilliseconds);
    }
}
