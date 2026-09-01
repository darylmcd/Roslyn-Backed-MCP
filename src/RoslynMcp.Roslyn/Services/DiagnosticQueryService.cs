using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Owns diagnostic query selection, aggregation, totals, and version-scoped caches.
/// </summary>
internal sealed class DiagnosticQueryService
{
    private const int MaxResultCacheEntriesPerWorkspace = 8;

    private readonly IWorkspaceManager _workspace;
    private readonly DiagnosticProjectAnalyzer _projectAnalyzer;
    private readonly ConcurrentDictionary<string, DiagnosticCacheEntry> _diagnosticCache = new();
    private readonly ConcurrentDictionary<string, ResultCacheEntry> _resultCache = new();

    private sealed record DiagnosticCacheEntry(int Version, IReadOnlyList<Diagnostic> Diagnostics);

    private sealed record ResultCacheEntry(
        int Version,
        ImmutableDictionary<DiagnosticQueryFilters, DiagnosticsResultDto> Results);

    public DiagnosticQueryService(
        IWorkspaceManager workspace,
        ICompilationCache compilationCache,
        IUnexpectedExceptionReporter? exceptionReporter = null)
    {
        _workspace = workspace;
        _projectAnalyzer = new DiagnosticProjectAnalyzer(compilationCache, exceptionReporter);
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

    internal DiagnosticsResultDto? GetCachedResult(
        string workspaceId,
        int version,
        DiagnosticQueryFilters filters)
    {
        if (!_resultCache.TryGetValue(workspaceId, out var entry)
            || entry.Version != version
            || !entry.Results.TryGetValue(filters, out var cachedResult))
        {
            return null;
        }

        return cachedResult;
    }

    public void CacheDiagnostics(
        string workspaceId,
        int version,
        IReadOnlyList<Diagnostic> diagnostics)
    {
        var candidate = new DiagnosticCacheEntry(version, diagnostics);
        _diagnosticCache.AddOrUpdate(
            workspaceId,
            candidate,
            (_, existing) => existing.Version > version ? existing : candidate);
    }

    public async Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId,
        DiagnosticQueryFilters filters,
        CancellationToken ct)
    {
        // Cache repeated queries by workspace version and the complete filter tuple.
        var version = _workspace.GetCurrentVersion(workspaceId);
        var cachedResult = GetCachedResult(workspaceId, version, filters);
        if (cachedResult is not null)
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

    private async Task<IReadOnlyList<DiagnosticProjectAnalysisResult>> CollectProjectResultsAsync(
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
            .Select(project => _projectAnalyzer.AnalyzeAsync(
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
        IReadOnlyList<DiagnosticProjectAnalysisResult> projectResults)
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
        _resultCache.AddOrUpdate(
            workspaceId,
            _ => CreateResultCacheEntry(version, filters, result),
            (_, existing) => MergeResultCacheEntry(existing, version, filters, result));
    }

    private static ResultCacheEntry CreateResultCacheEntry(
        int version,
        DiagnosticQueryFilters filters,
        DiagnosticsResultDto result) =>
        new(version, ImmutableDictionary<DiagnosticQueryFilters, DiagnosticsResultDto>.Empty
            .Add(filters, result));

    private static ResultCacheEntry MergeResultCacheEntry(
        ResultCacheEntry existing,
        int version,
        DiagnosticQueryFilters filters,
        DiagnosticsResultDto result)
    {
        if (existing.Version > version)
        {
            return existing;
        }

        if (existing.Version < version)
        {
            return CreateResultCacheEntry(version, filters, result);
        }

        var results = existing.Results.SetItem(filters, result);
        if (results.Count > MaxResultCacheEntriesPerWorkspace)
        {
            // The cache is intentionally small. Removing an arbitrary prior entry avoids the
            // bookkeeping cost of a true LRU for typical 1-3-key sessions. Immutable replacement
            // keeps capacity enforcement and insertion inside this workspace-key update.
            results = results.Remove(existing.Results.Keys.First());
        }

        return existing with { Results = results };
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

    private sealed record WorkspaceDiagnosticScope(
        IReadOnlyList<DiagnosticDto> All,
        IReadOnlyList<DiagnosticDto> Filtered);
}

internal sealed record DiagnosticQueryFilters(
    string? Project,
    string? File,
    string? Severity,
    string? DiagnosticId);
