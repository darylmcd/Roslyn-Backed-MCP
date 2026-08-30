using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Owns diagnostic query selection, aggregation, totals, and version-scoped caches.
/// </summary>
internal sealed class DiagnosticQueryService
{
    private const int MaxResultCacheEntriesPerWorkspace = 8;

    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ConcurrentDictionary<string, DiagnosticCacheEntry> _diagnosticCache = new();
    private readonly ConcurrentDictionary<string, ResultCacheEntry> _resultCache = new();

    private sealed record DiagnosticCacheEntry(int Version, IReadOnlyList<Diagnostic> Diagnostics);

    private sealed record ResultCacheEntry(
        int Version,
        ConcurrentDictionary<DiagnosticQueryFilters, DiagnosticsResultDto> Results);

    public DiagnosticQueryService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
    }

    public void InvalidateWorkspaceCaches(string workspaceId)
    {
        _resultCache.TryRemove(workspaceId, out _);
        _diagnosticCache.TryRemove(workspaceId, out _);
    }

    public bool TryGetCachedWorkspaceDiagnostics(
        string workspaceId,
        out DiagnosticsResultDto? diagnostics)
    {
        diagnostics = null;
        var version = _workspace.GetCurrentVersion(workspaceId);
        if (!_resultCache.TryGetValue(workspaceId, out var entry) || entry.Version != version)
        {
            return false;
        }

        foreach (var cached in entry.Results)
        {
            var key = cached.Key;
            // Severity changes the returned rows but not aggregate totals. The support-bundle
            // cache-only path consumes totals, so any current full-workspace severity entry is valid.
            if (key.Project is null
                && key.File is null
                && key.DiagnosticId is null)
            {
                diagnostics = cached.Value;
                return true;
            }
        }

        return false;
    }

    public bool TryGetCachedDiagnostics(
        string workspaceId,
        int version,
        out IReadOnlyList<Diagnostic>? diagnostics)
    {
        diagnostics = null;
        if (!_diagnosticCache.TryGetValue(workspaceId, out var cached)
            || cached.Version != version)
        {
            return false;
        }

        diagnostics = cached.Diagnostics;
        return true;
    }

    public void CacheDiagnostics(
        string workspaceId,
        int version,
        IReadOnlyList<Diagnostic> diagnostics) =>
        _diagnosticCache[workspaceId] = new DiagnosticCacheEntry(version, diagnostics);

    public async Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId,
        DiagnosticQueryFilters filters,
        CancellationToken ct)
    {
        // Cache repeated queries by workspace version and the complete filter tuple.
        var version = _workspace.GetCurrentVersion(workspaceId);
        if (_resultCache.TryGetValue(workspaceId, out var entry)
            && entry.Version == version
            && entry.Results.TryGetValue(filters, out var cachedResult))
        {
            return cachedResult;
        }

        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Default to Info so totals align with the returned rows. Hidden diagnostics remain
        // excluded, and the host tool owns page-size bounding.
        DiagnosticSeverity? minSeverity = ParseSeverity(filters.Severity) ?? DiagnosticSeverity.Info;
        var workspaceScope = await BuildWorkspaceScopeAsync(
            workspaceId,
            filters.File,
            minSeverity,
            ct).ConfigureAwait(false);
        var projectResults = await CollectProjectResultsAsync(
            workspaceId,
            solution,
            filters,
            minSeverity,
            ct).ConfigureAwait(false);

        // Only a whole-solution scan is complete enough for detail lookup reuse.
        if (filters.Project is null && filters.File is null)
        {
            CacheDiagnostics(
                workspaceId,
                version,
                projectResults.SelectMany(result => result.Raw).ToList());
        }

        var result = BuildResult(workspaceScope, projectResults);
        StoreResult(workspaceId, version, filters, result);
        return result;
    }

    private async Task<WorkspaceDiagnosticScope> BuildWorkspaceScopeAsync(
        string workspaceId,
        string? fileFilter,
        DiagnosticSeverity? minSeverity,
        CancellationToken ct)
    {
        // Workspace diagnostics are normalized at workspace-load ingress. Apply the file and
        // severity filters independently so aggregate totals remain severity-invariant.
        var rawWorkspace = (await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false))
            .WorkspaceDiagnostics;
        var matchingFile = fileFilter is null
            ? rawWorkspace
            : rawWorkspace.Where(diagnostic =>
                    diagnostic.FilePath is not null
                    && string.Equals(
                        Path.GetFullPath(diagnostic.FilePath),
                        Path.GetFullPath(fileFilter),
                        StringComparison.OrdinalIgnoreCase))
                .ToList();
        return new WorkspaceDiagnosticScope(
            matchingFile,
            FilterDiagnostics(matchingFile, minSeverity));
    }

    private async Task<IReadOnlyList<ProjectDiagnosticResult>> CollectProjectResultsAsync(
        string workspaceId,
        Solution solution,
        DiagnosticQueryFilters filters,
        DiagnosticSeverity? minSeverity,
        CancellationToken ct)
    {
        var projectTasks = solution.Projects
            .Where(project =>
                filters.Project is null
                || string.Equals(project.Name, filters.Project, StringComparison.OrdinalIgnoreCase))
            .Select(project => CollectProjectDiagnosticsAsync(
                workspaceId,
                project,
                filters.File,
                filters.DiagnosticId,
                minSeverity,
                ct));
        return await Task.WhenAll(projectTasks).ConfigureAwait(false);
    }

    private static DiagnosticsResultDto BuildResult(
        WorkspaceDiagnosticScope workspaceScope,
        IReadOnlyList<ProjectDiagnosticResult> projectResults)
    {
        var compilerDiagnostics = projectResults
            .SelectMany(result => result.CompilerFiltered)
            .ToList();
        var analyzerDiagnostics = projectResults
            .SelectMany(result => result.AnalyzerFiltered)
            .ToList();
        var compilerAllDiagnostics = projectResults
            .SelectMany(result => result.CompilerAll)
            .ToList();
        var analyzerAllDiagnostics = projectResults
            .SelectMany(result => result.AnalyzerAll)
            .ToList();
        var compilerErrors = compilerAllDiagnostics.Count(diagnostic =>
            diagnostic.Severity == "Error");
        var analyzerErrors = analyzerAllDiagnostics.Count(diagnostic =>
            diagnostic.Severity == "Error");
        var workspaceErrors = workspaceScope.All.Count(diagnostic =>
            diagnostic.Severity == "Error");
        var totalWarnings = compilerAllDiagnostics.Count(diagnostic =>
                diagnostic.Severity == "Warning")
            + analyzerAllDiagnostics.Count(diagnostic => diagnostic.Severity == "Warning")
            + workspaceScope.All.Count(diagnostic => diagnostic.Severity == "Warning");
        var totalInfo = compilerAllDiagnostics.Count(diagnostic => diagnostic.Severity == "Info")
            + analyzerAllDiagnostics.Count(diagnostic => diagnostic.Severity == "Info")
            + workspaceScope.All.Count(diagnostic => diagnostic.Severity == "Info");

        return new DiagnosticsResultDto(
            workspaceScope.Filtered,
            compilerDiagnostics,
            analyzerDiagnostics,
            TotalErrors: compilerErrors + analyzerErrors + workspaceErrors,
            TotalWarnings: totalWarnings,
            TotalInfo: totalInfo,
            CompilerErrors: compilerErrors,
            AnalyzerErrors: analyzerErrors,
            WorkspaceErrors: workspaceErrors);
    }

    private void StoreResult(
        string workspaceId,
        int version,
        DiagnosticQueryFilters filters,
        DiagnosticsResultDto result)
    {
        var workspaceEntry = _resultCache.AddOrUpdate(
            workspaceId,
            _ => new ResultCacheEntry(
                version,
                new ConcurrentDictionary<DiagnosticQueryFilters, DiagnosticsResultDto>()),
            (_, existing) => existing.Version == version
                ? existing
                : new ResultCacheEntry(
                    version,
                    new ConcurrentDictionary<DiagnosticQueryFilters, DiagnosticsResultDto>()));
        if (workspaceEntry.Results.Count >= MaxResultCacheEntriesPerWorkspace)
        {
            // The cache is intentionally small. Removing an arbitrary entry avoids the extra
            // synchronization and bookkeeping cost of a true LRU for typical 1-3-key sessions.
            var someKey = workspaceEntry.Results.Keys.FirstOrDefault();
            if (someKey is not null)
            {
                workspaceEntry.Results.TryRemove(someKey, out _);
            }
        }

        workspaceEntry.Results[filters] = result;
    }

    private async Task<ProjectDiagnosticResult> CollectProjectDiagnosticsAsync(
        string workspaceId,
        Project project,
        string? fileFilter,
        string? diagnosticIdFilter,
        DiagnosticSeverity? minSeverity,
        CancellationToken ct)
    {
        var compilerAll = new List<DiagnosticDto>();
        var compilerFiltered = new List<DiagnosticDto>();
        var analyzerAll = new List<DiagnosticDto>();
        var analyzerFiltered = new List<DiagnosticDto>();
        var raw = new List<Diagnostic>();

        var snapshot = await _compilationCache
            .GetCompilationSnapshotAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (snapshot is null)
        {
            var miss = new DiagnosticDto(
                "WORKSPACE001",
                $"Could not get compilation for project '{project.Name}'",
                "Error",
                "Workspace",
                project.FilePath,
                null,
                null,
                null,
                null,
                Location: null);
            compilerAll.Add(miss);
            compilerFiltered.Add(miss);
            return new ProjectDiagnosticResult(
                compilerAll,
                compilerFiltered,
                analyzerAll,
                analyzerFiltered,
                raw);
        }

        CollectDiagnostics(
            snapshot.Compilation.GetDiagnostics(ct).Concat(snapshot.GeneratorDiagnostics),
            fileFilter,
            diagnosticIdFilter,
            minSeverity,
            raw,
            compilerAll,
            compilerFiltered);

        // Avoid an analyzer pass for an Error-only query when no loaded descriptor defaults to
        // Error. The effective-severity override limitation remains tracked separately.
        if (minSeverity == DiagnosticSeverity.Error && !ProjectHasErrorDefaultAnalyzer(project))
        {
            return new ProjectDiagnosticResult(
                compilerAll,
                compilerFiltered,
                analyzerAll,
                analyzerFiltered,
                raw);
        }

        var compilationWithAnalyzers = await _compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is not null)
        {
            var analyzerDiagnostics = await compilationWithAnalyzers
                .GetAnalyzerDiagnosticsAsync(ct)
                .ConfigureAwait(false);
            CollectDiagnostics(
                analyzerDiagnostics,
                fileFilter,
                diagnosticIdFilter,
                minSeverity,
                raw,
                analyzerAll,
                analyzerFiltered);
        }

        return new ProjectDiagnosticResult(
            compilerAll,
            compilerFiltered,
            analyzerAll,
            analyzerFiltered,
            raw);
    }

    private static bool ProjectHasErrorDefaultAnalyzer(Project project)
    {
        foreach (var reference in project.AnalyzerReferences)
        {
            foreach (var analyzer in reference.GetAnalyzers(project.Language))
            {
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void CollectDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        string? fileFilter,
        string? diagnosticIdFilter,
        DiagnosticSeverity? minSeverity,
        List<Diagnostic> raw,
        List<DiagnosticDto> all,
        List<DiagnosticDto> filtered)
    {
        foreach (var diagnostic in diagnostics)
        {
            raw.Add(diagnostic);
            if (!MatchesFileFilter(diagnostic, fileFilter)
                || diagnostic.Severity == DiagnosticSeverity.Hidden
                || (diagnosticIdFilter is not null
                    && !string.Equals(
                        diagnostic.Id,
                        diagnosticIdFilter,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var dto = SymbolMapper.ToDiagnosticDto(diagnostic);
            all.Add(dto);
            if (minSeverity is null || diagnostic.Severity >= minSeverity.Value)
            {
                filtered.Add(dto);
            }
        }
    }

    private static bool MatchesFileFilter(Diagnostic diagnostic, string? fileFilter)
    {
        if (fileFilter is null)
        {
            return true;
        }

        if (!diagnostic.Location.IsInSource)
        {
            return false;
        }

        var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
        return string.Equals(
            Path.GetFullPath(diagnosticPath),
            Path.GetFullPath(fileFilter),
            StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticSeverity? ParseSeverity(string? severityFilter) =>
        severityFilter?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null,
        };

    private static List<DiagnosticDto> FilterDiagnostics(
        IReadOnlyList<DiagnosticDto> diagnostics,
        DiagnosticSeverity? minSeverity) =>
        diagnostics
            .Where(diagnostic =>
                !minSeverity.HasValue
                || !Enum.TryParse<DiagnosticSeverity>(
                    diagnostic.Severity,
                    ignoreCase: true,
                    out var severity)
                || severity >= minSeverity.Value)
            .ToList();

    private sealed record ProjectDiagnosticResult(
        List<DiagnosticDto> CompilerAll,
        List<DiagnosticDto> CompilerFiltered,
        List<DiagnosticDto> AnalyzerAll,
        List<DiagnosticDto> AnalyzerFiltered,
        List<Diagnostic> Raw);

    private sealed record WorkspaceDiagnosticScope(
        IReadOnlyList<DiagnosticDto> All,
        IReadOnlyList<DiagnosticDto> Filtered);
}

internal sealed record DiagnosticQueryFilters(
    string? Project,
    string? File,
    string? Severity,
    string? DiagnosticId);
