using System.Collections.Concurrent;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class DiagnosticService : IDiagnosticService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ICompilationCache _compilationCache;
    private readonly ILogger<DiagnosticService> _logger;

    /// <summary>
    /// Cache of full per-project diagnostic lists, keyed by workspaceId. The detail-lookup
    /// path (<see cref="GetDiagnosticDetailsAsync"/>) is almost always called immediately
    /// after <see cref="GetDiagnosticsAsync"/>, so we keep the warm Diagnostic objects
    /// around to skip recompiling and re-running analyzers. Invalidates on workspace
    /// version bump.
    /// </summary>
    private readonly ConcurrentDictionary<string, DiagnosticCacheEntry> _diagnosticCache = new();

    private sealed record DiagnosticCacheEntry(int Version, IReadOnlyList<Diagnostic> Diagnostics);

    public DiagnosticService(IWorkspaceManager workspace, ICompilationCache compilationCache, ILogger<DiagnosticService> logger)
    {
        _workspace = workspace;
        _compilationCache = compilationCache;
        _logger = logger;
    }

    public async Task<DiagnosticsResultDto> GetDiagnosticsAsync(
        string workspaceId, string? projectFilter, string? fileFilter, string? severityFilter, string? diagnosticIdFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Default to Info when no filter so totals align with the first page (Warning hid Info-only
        // solutions). Hidden remains excluded in CollectDiagnostics. Payload size is capped by
        // project_diagnostics limit (default 200), not by raising the severity floor.
        DiagnosticSeverity? minSeverity = ParseSeverity(severityFilter) ?? DiagnosticSeverity.Info;

        // Workspace diagnostics already arrive normalized via WorkspaceDiagnosticSeverityClassifier
        // (applied in WorkspaceManager.LoadIntoSessionAsync at ingress).
        var rawWorkspace = (await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false)).WorkspaceDiagnostics;
        // Apply file filter to workspace diagnostics, but build BOTH a filtered list (for the
        // returned WorkspaceDiagnostics array) and an unfiltered count basis (for TotalXxx).
        var workspaceMatchingFile = fileFilter is null
            ? rawWorkspace
            : rawWorkspace.Where(d => d.FilePath is not null &&
                string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(fileFilter), StringComparison.OrdinalIgnoreCase))
                .ToList();
        var workspaceDiagnostics = FilterDiagnostics(workspaceMatchingFile, fileFilter: null, minSeverity: minSeverity);

        var projects = solution.Projects
            .Where(p => projectFilter is null || string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var projectTasks = projects.Select(project =>
            CollectProjectDiagnosticsAsync(workspaceId, project, fileFilter, diagnosticIdFilter, minSeverity, ct));

        var results = await Task.WhenAll(projectTasks).ConfigureAwait(false);
        var compilerDiagnostics = results.SelectMany(r => r.compilerFiltered).ToList();
        var analyzerDiagnostics = results.SelectMany(r => r.analyzerFiltered).ToList();
        var compilerAllDiagnostics = results.SelectMany(r => r.compilerAll).ToList();
        var analyzerAllDiagnostics = results.SelectMany(r => r.analyzerAll).ToList();

        // Cache the unfiltered Diagnostic objects so the detail-lookup path (which is
        // almost always called next) can skip recompilation. Only cache when this call
        // covered the whole solution; partial filters would produce a misleading cache.
        if (projectFilter is null && fileFilter is null)
        {
            var version = _workspace.GetCurrentVersion(workspaceId);
            var allRaw = results.SelectMany(r => r.raw).ToList();
            _diagnosticCache[workspaceId] = new DiagnosticCacheEntry(version, allRaw);
        }

        // Counts are over the file-scoped (but severity-unfiltered) result so callers can see
        // the full shape of the queried scope even when severityFilter narrows the returned set.
        // BUG fix (project-diagnostics-filter-totals): previously these counts double-applied
        // the severity filter, so `severity=Error` reported `totalWarnings=0` even when the
        // same workspace had hundreds of warnings.
        var compilerErrors = compilerAllDiagnostics.Count(d => d.Severity == "Error");
        var analyzerErrors = analyzerAllDiagnostics.Count(d => d.Severity == "Error");
        var workspaceErrors = workspaceMatchingFile.Count(d => d.Severity == "Error");

        var totalWarnings = compilerAllDiagnostics.Count(d => d.Severity == "Warning")
            + analyzerAllDiagnostics.Count(d => d.Severity == "Warning")
            + workspaceMatchingFile.Count(d => d.Severity == "Warning");
        var totalInfo = compilerAllDiagnostics.Count(d => d.Severity == "Info")
            + analyzerAllDiagnostics.Count(d => d.Severity == "Info")
            + workspaceMatchingFile.Count(d => d.Severity == "Info");

        return new DiagnosticsResultDto(
            workspaceDiagnostics,
            compilerDiagnostics,
            analyzerDiagnostics,
            TotalErrors: compilerErrors + analyzerErrors + workspaceErrors,
            TotalWarnings: totalWarnings,
            TotalInfo: totalInfo,
            CompilerErrors: compilerErrors,
            AnalyzerErrors: analyzerErrors,
            WorkspaceErrors: workspaceErrors);
    }

    private async Task<(List<DiagnosticDto> compilerAll, List<DiagnosticDto> compilerFiltered,
        List<DiagnosticDto> analyzerAll, List<DiagnosticDto> analyzerFiltered, List<Diagnostic> raw)>
        CollectProjectDiagnosticsAsync(
            string workspaceId, Project project, string? fileFilter, string? diagnosticIdFilter,
            DiagnosticSeverity? minSeverity, CancellationToken ct)
    {
        var compilerAll = new List<DiagnosticDto>();
        var compilerFiltered = new List<DiagnosticDto>();
        var analyzerAll = new List<DiagnosticDto>();
        var analyzerFiltered = new List<DiagnosticDto>();
        var raw = new List<Diagnostic>();

        var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
        if (compilation is null)
        {
            var miss = new DiagnosticDto(
                "WORKSPACE001", $"Could not get compilation for project '{project.Name}'",
                "Error", "Workspace", project.FilePath, null, null, null, null);
            compilerAll.Add(miss);
            compilerFiltered.Add(miss);
            return (compilerAll, compilerFiltered, analyzerAll, analyzerFiltered, raw);
        }

        CollectDiagnostics(compilation.GetDiagnostics(ct), fileFilter, diagnosticIdFilter, minSeverity,
            raw, compilerAll, compilerFiltered);

        var compilationWithAnalyzers = await _compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is not null)
        {
            var analyzerDiags = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
            CollectDiagnostics(analyzerDiags, fileFilter, diagnosticIdFilter, minSeverity,
                raw, analyzerAll, analyzerFiltered);
        }

        return (compilerAll, compilerFiltered, analyzerAll, analyzerFiltered, raw);
    }

    private static void CollectDiagnostics(
        IEnumerable<Diagnostic> diagnostics, string? fileFilter, string? diagnosticIdFilter,
        DiagnosticSeverity? minSeverity,
        List<Diagnostic> raw, List<DiagnosticDto> all, List<DiagnosticDto> filtered)
    {
        foreach (var diagnostic in diagnostics)
        {
            raw.Add(diagnostic);
            if (!MatchesFileFilter(diagnostic, fileFilter)) continue;
            if (diagnostic.Severity == DiagnosticSeverity.Hidden) continue;
            if (diagnosticIdFilter is not null &&
                !string.Equals(diagnostic.Id, diagnosticIdFilter, StringComparison.OrdinalIgnoreCase)) continue;

            var dto = SymbolMapper.ToDiagnosticDto(diagnostic);
            all.Add(dto);
            if (minSeverity is null || diagnostic.Severity >= minSeverity.Value)
                filtered.Add(dto);
        }
    }

    /// <summary>
    /// Returns true when the diagnostic's source path matches the optional file filter.
    /// In-memory diagnostics with no source location pass through unconditionally.
    /// </summary>
    private static bool MatchesFileFilter(Diagnostic diagnostic, string? fileFilter)
    {
        if (fileFilter is null) return true;
        if (!diagnostic.Location.IsInSource) return false;

        var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
        return string.Equals(Path.GetFullPath(diagnosticPath), Path.GetFullPath(fileFilter), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<DiagnosticDetailsDto?> GetDiagnosticDetailsAsync(
        string workspaceId,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct)
    {
        // Fast path: most callers invoke this immediately after GetDiagnosticsAsync, so the
        // exact Diagnostic they want is already in the cache. Skip the recompile entirely.
        var version = _workspace.GetCurrentVersion(workspaceId);
        if (_diagnosticCache.TryGetValue(workspaceId, out var cached) && cached.Version == version)
        {
            foreach (var d in cached.Diagnostics)
            {
                if (DiagnosticMatchesLocation(d, diagnosticId, filePath, line, column))
                {
                    return BuildDetailsDto(d);
                }
            }
        }

        // Cold path: cache miss (no prior list call, or workspace was reloaded). Re-run
        // the search across projects in parallel and warm the cache for next time.
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var diagnostic = await FindDiagnosticAsync(workspaceId, solution, version, diagnosticId, filePath, line, column, ct).ConfigureAwait(false);
        return diagnostic is null ? null : BuildDetailsDto(diagnostic);
    }

    private static DiagnosticDetailsDto BuildDetailsDto(Diagnostic diagnostic) => new(
        Diagnostic: SymbolMapper.ToDiagnosticDto(diagnostic),
        Description: BuildDiagnosticDescription(diagnostic),
        HelpLinkUri: BuildHelpLink(diagnostic.Id),
        SupportedFixes: GetSupportedFixes(diagnostic.Id),
        GuidanceMessage: GetFixGuidance(diagnostic.Id));

    private static string BuildDiagnosticDescription(Diagnostic diagnostic)
    {
        var fromDesc = diagnostic.Descriptor.Description.ToString();
        if (!string.IsNullOrWhiteSpace(fromDesc))
            return fromDesc;

        var fromFormat = diagnostic.Descriptor.MessageFormat.ToString();
        if (!string.IsNullOrWhiteSpace(fromFormat))
            return fromFormat;

        return diagnostic.GetMessage();
    }

    private static DiagnosticSeverity? ParseSeverity(string? severityFilter) =>
        severityFilter?.ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null
        };

    /// <summary>
    /// Returns the input list narrowed to diagnostics whose severity is at or above
    /// <paramref name="minSeverity"/>. Used by the post-ingress filter on the workspace
    /// diagnostic feed; the file filter is applied earlier so this method only handles
    /// severity narrowing.
    /// </summary>
    private static IReadOnlyList<DiagnosticDto> FilterDiagnostics(
        IReadOnlyList<DiagnosticDto> diagnostics,
        string? fileFilter,
        DiagnosticSeverity? minSeverity)
    {
        return diagnostics
            .Where(diagnostic =>
            {
                if (minSeverity.HasValue &&
                    Enum.TryParse<DiagnosticSeverity>(diagnostic.Severity, ignoreCase: true, out var severity) &&
                    severity < minSeverity.Value)
                {
                    return false;
                }

                if (fileFilter is not null && diagnostic.FilePath is not null)
                {
                    return string.Equals(
                        Path.GetFullPath(diagnostic.FilePath),
                        Path.GetFullPath(fileFilter),
                        StringComparison.OrdinalIgnoreCase);
                }

                return true;
            })
            .ToList();
    }

    private static string BuildHelpLink(string diagnosticId) =>
        $"https://learn.microsoft.com/dotnet/csharp/language-reference/compiler-messages/{diagnosticId.ToLowerInvariant()}";

    // dr-code-fix-preview-vs-diagnostic-details-curated-gap: Only advertise
    // diagnostics that code_fix_preview (PreviewCodeFixAsync) actually handles.
    // Previously this map included CS0414, CS8600/8602/8603, CA2234 which were
    // rejected at apply time — a contract violation surfaced by deep-review audits.
    private static IReadOnlyList<CodeFixOptionDto> GetSupportedFixes(string diagnosticId) =>
        diagnosticId.ToUpperInvariant() switch
        {
            "CS8019" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_using",
                    Title: "Remove unused using",
                    Description: "Deletes the using directive flagged as unnecessary.")
            ],
            "IDE0005" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_using",
                    Title: "Remove unnecessary using directive",
                    Description: "IDE-style unused using removal (requires a matching code fix provider in the workspace).")
            ],
            _ => []
        };

    /// <summary>
    /// Returns a guidance message for diagnostics that do not have curated code_fix_preview
    /// support but can be addressed via get_code_actions + preview_code_action.
    /// </summary>
    private static string? GetFixGuidance(string diagnosticId) =>
        diagnosticId.ToUpperInvariant() switch
        {
            "CS0414" or "CS8600" or "CS8602" or "CS8603" or "CA2234" or "CA1852" =>
                "This diagnostic does not have a curated code_fix_preview fix. " +
                "Use get_code_actions at the diagnostic location, then preview_code_action to apply a Roslyn-provided fix.",
            _ => null
        };

    private static bool DiagnosticMatchesLocation(Diagnostic diagnostic, string diagnosticId, string filePath, int line, int column)
    {
        if (!string.Equals(diagnostic.Id, diagnosticId, StringComparison.OrdinalIgnoreCase) ||
            !diagnostic.Location.IsInSource)
            return false;

        var lineSpan = diagnostic.Location.GetLineSpan();
        return string.Equals(Path.GetFullPath(lineSpan.Path), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase) &&
               lineSpan.StartLinePosition.Line + 1 == line &&
               lineSpan.StartLinePosition.Character + 1 == column;
    }

    private async Task<Diagnostic?> FindDiagnosticAsync(
        string workspaceId,
        Solution solution,
        int version,
        string diagnosticId,
        string filePath,
        int line,
        int column,
        CancellationToken ct)
    {
        // Walk all projects in parallel — each one is an independent compilation+analyzer
        // pass, and there's no reason to serialize them. Roslyn's compilation and analyzer
        // APIs are thread-safe over immutable Project/Compilation objects.
        var projectTasks = solution.Projects.Select(project => CollectProjectDiagnosticsAsync(workspaceId, project, ct));
        var perProjectResults = await Task.WhenAll(projectTasks).ConfigureAwait(false);

        // Warm the cache so a follow-up details lookup (or a project_diagnostics call that
        // hits an unrelated diagnostic on the same workspace) hits the fast path.
        var allDiagnostics = perProjectResults.SelectMany(d => d).ToList();
        _diagnosticCache[workspaceId] = new DiagnosticCacheEntry(version, allDiagnostics);

        foreach (var diagnostic in allDiagnostics)
        {
            if (DiagnosticMatchesLocation(diagnostic, diagnosticId, filePath, line, column))
                return diagnostic;
        }

        return null;
    }

    private async Task<IReadOnlyList<Diagnostic>> CollectProjectDiagnosticsAsync(string workspaceId, Project project, CancellationToken ct)
    {
        var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
        if (compilation is null) return [];

        var collected = new List<Diagnostic>();
        collected.AddRange(compilation.GetDiagnostics(ct));

        var compilationWithAnalyzers = await _compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is null) return collected;

        var analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync(ct).ConfigureAwait(false);
        collected.AddRange(analyzerDiagnostics);
        return collected;
    }
}
