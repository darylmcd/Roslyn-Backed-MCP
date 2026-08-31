using System.Collections.Concurrent;
using System.Collections.Immutable;
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
        ImmutableDictionary<DiagnosticQueryFilters, DiagnosticsResultDto> Results);

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

        // Avoid an analyzer pass for an Error-only query only when descriptor defaults,
        // command-line options, and analyzer-config overrides all prove that no loaded analyzer
        // can contribute an Error.
        if (minSeverity == DiagnosticSeverity.Error
            && !ProjectHasEffectiveErrorAnalyzer(project, snapshot.Compilation, ct))
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

    private static bool ProjectHasEffectiveErrorAnalyzer(
        Project project,
        Compilation compilation,
        CancellationToken ct)
    {
        foreach (var reference in project.AnalyzerReferences)
        {
            foreach (var analyzer in reference.GetAnalyzers(project.Language))
            {
                foreach (var descriptor in analyzer.SupportedDiagnostics)
                {
                    ct.ThrowIfCancellationRequested();
                    if (CanReportAsError(descriptor, compilation, ct))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool CanReportAsError(
        DiagnosticDescriptor descriptor,
        Compilation compilation,
        CancellationToken ct)
    {
        var options = compilation.Options;
        if (GetEffectiveReportDiagnostic(descriptor, options, syntaxTree: null, ct)
            == ReportDiagnostic.Error)
        {
            return true;
        }

        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            ct.ThrowIfCancellationRequested();
            if (GetEffectiveReportDiagnostic(descriptor, options, syntaxTree, ct)
                == ReportDiagnostic.Error)
            {
                return true;
            }
        }

        return false;
    }

    private static ReportDiagnostic GetEffectiveReportDiagnostic(
        DiagnosticDescriptor descriptor,
        CompilationOptions options,
        SyntaxTree? syntaxTree,
        CancellationToken ct)
    {
        var treeOptions = options.SyntaxTreeOptionsProvider;
        if (syntaxTree is not null
            && treeOptions is not null
            && treeOptions.TryGetDiagnosticValue(syntaxTree, descriptor.Id, ct, out var treeSeverity)
            && treeSeverity != ReportDiagnostic.Default)
        {
            return treeSeverity;
        }

        if (treeOptions is not null
            && treeOptions.TryGetGlobalDiagnosticValue(descriptor.Id, ct, out var globalSeverity)
            && globalSeverity != ReportDiagnostic.Default)
        {
            return globalSeverity;
        }

        if (options.SpecificDiagnosticOptions.TryGetValue(descriptor.Id, out var specificSeverity)
            && specificSeverity != ReportDiagnostic.Default)
        {
            return specificSeverity;
        }

        if (!descriptor.IsEnabledByDefault)
        {
            return ReportDiagnostic.Suppress;
        }

        return descriptor.DefaultSeverity switch
        {
            DiagnosticSeverity.Error => ReportDiagnostic.Error,
            DiagnosticSeverity.Warning when options.GeneralDiagnosticOption == ReportDiagnostic.Error =>
                ReportDiagnostic.Error,
            DiagnosticSeverity.Warning => ReportDiagnostic.Warn,
            DiagnosticSeverity.Info => ReportDiagnostic.Info,
            DiagnosticSeverity.Hidden => ReportDiagnostic.Hidden,
            _ => ReportDiagnostic.Default,
        };
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
