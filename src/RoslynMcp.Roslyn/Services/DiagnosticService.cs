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
        string workspaceId, string? projectFilter, string? fileFilter, string? severityFilter, CancellationToken ct)
    {
        var solution = _workspace.GetCurrentSolution(workspaceId);
        // Default to Warning severity when no filter specified to avoid multi-MB Hidden output
        DiagnosticSeverity? minSeverity = ParseSeverity(severityFilter) ?? DiagnosticSeverity.Warning;

        var rawWorkspace = (await _workspace.GetStatusAsync(workspaceId, ct).ConfigureAwait(false)).WorkspaceDiagnostics;
        var workspaceDiagnostics = FilterDiagnostics(
            NormalizeWorkspaceDiagnostics(rawWorkspace),
            fileFilter,
            minSeverity: minSeverity);

        var projects = solution.Projects
            .Where(p => projectFilter is null || string.Equals(p.Name, projectFilter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var projectTasks = projects.Select(async project =>
        {
            var compiler = new List<DiagnosticDto>();
            var analyzer = new List<DiagnosticDto>();
            var raw = new List<Diagnostic>();

            // Use the shared compilation cache so independent tools (and the diagnostic
            // detail cold path) reuse the same warm Compilation across requests.
            var compilation = await _compilationCache.GetCompilationAsync(workspaceId, project, ct).ConfigureAwait(false);
            if (compilation is null)
            {
                compiler.Add(new DiagnosticDto(
                    "WORKSPACE001", $"Could not get compilation for project '{project.Name}'",
                    "Error", "Workspace", project.FilePath, null, null, null, null));
                return (compiler, analyzer, raw);
            }

            foreach (var diagnostic in compilation.GetDiagnostics(ct))
            {
                raw.Add(diagnostic);
                if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                {
                    compiler.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                }
            }

            // CompilationWithAnalyzers is also cached: subsequent diagnostic calls (and
            // related tools that need analyzer diagnostics) reuse the same instance.
            var compilationWithAnalyzers = await _compilationCache
                .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
                .ConfigureAwait(false);
            if (compilationWithAnalyzers is not null)
            {
                var projectAnalyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(ct).ConfigureAwait(false);
                foreach (var diagnostic in projectAnalyzerDiagnostics)
                {
                    raw.Add(diagnostic);
                    if (MatchesFilter(diagnostic, fileFilter, minSeverity))
                    {
                        analyzer.Add(SymbolMapper.ToDiagnosticDto(diagnostic));
                    }
                }
            }

            return (compiler, analyzer, raw);
        });

        var results = await Task.WhenAll(projectTasks).ConfigureAwait(false);
        var compilerDiagnostics = results.SelectMany(r => r.compiler).ToList();
        var analyzerDiagnostics = results.SelectMany(r => r.analyzer).ToList();

        // Cache the unfiltered Diagnostic objects so the detail-lookup path (which is
        // almost always called next) can skip recompilation. Only cache when this call
        // covered the whole solution; partial filters would produce a misleading cache.
        if (projectFilter is null && fileFilter is null)
        {
            var version = _workspace.GetCurrentVersion(workspaceId);
            var allRaw = results.SelectMany(r => r.raw).ToList();
            _diagnosticCache[workspaceId] = new DiagnosticCacheEntry(version, allRaw);
        }

        var compilerErrors = compilerDiagnostics.Count(d => d.Severity == "Error");
        var analyzerErrors = analyzerDiagnostics.Count(d => d.Severity == "Error");
        var workspaceErrors = workspaceDiagnostics.Count(d => d.Severity == "Error");

        return new DiagnosticsResultDto(
            workspaceDiagnostics,
            compilerDiagnostics,
            analyzerDiagnostics,
            TotalErrors: compilerErrors + analyzerErrors + workspaceErrors,
            TotalWarnings: compilerDiagnostics.Count(d => d.Severity == "Warning")
                + analyzerDiagnostics.Count(d => d.Severity == "Warning")
                + workspaceDiagnostics.Count(d => d.Severity == "Warning"),
            TotalInfo: compilerDiagnostics.Count(d => d.Severity == "Info")
                + analyzerDiagnostics.Count(d => d.Severity == "Info")
                + workspaceDiagnostics.Count(d => d.Severity == "Info"),
            CompilerErrors: compilerErrors,
            AnalyzerErrors: analyzerErrors,
            WorkspaceErrors: workspaceErrors);
    }

    /// <summary>
    /// Downgrades known informational SDK/workspace messages that MSBuildWorkspace surfaces as failures.
    /// </summary>
    private static IReadOnlyList<DiagnosticDto> NormalizeWorkspaceDiagnostics(IReadOnlyList<DiagnosticDto> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return diagnostics;
        }

        var list = new List<DiagnosticDto>(diagnostics.Count);
        foreach (var d in diagnostics)
        {
            if (string.Equals(d.Severity, "Error", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Category, "Workspace", StringComparison.OrdinalIgnoreCase) &&
                IsInformationalWorkspaceFailureMessage(d.Message))
            {
                list.Add(d with { Severity = "Warning" });
            }
            else
            {
                list.Add(d);
            }
        }

        return list;
    }

    private static bool IsInformationalWorkspaceFailureMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // SDK package pruning hints (e.g. "PackageReference X will not be pruned...")
        if (message.Contains("pruned", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
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
        SupportedFixes: GetSupportedFixes(diagnostic.Id));

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

    private static bool MatchesFilter(Diagnostic diagnostic, string? fileFilter, DiagnosticSeverity? minSeverity)
    {
        if (minSeverity.HasValue && diagnostic.Severity < minSeverity.Value)
        {
            return false;
        }

        if (fileFilter is not null && diagnostic.Location.IsInSource)
        {
            var diagnosticPath = diagnostic.Location.GetLineSpan().Path;
            if (!string.Equals(Path.GetFullPath(diagnosticPath), Path.GetFullPath(fileFilter), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

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
            "CS0414" =>
            [
                new CodeFixOptionDto(
                    FixId: "remove_unused_field",
                    Title: "Remove unused field",
                    Description: "Field is assigned but never read; remove or use the field.")
            ],
            "CS8600" or "CS8602" or "CS8603" =>
            [
                new CodeFixOptionDto(
                    FixId: "nullable_fix",
                    Title: "Address nullable reference warning",
                    Description: "Add null check, use null-forgiving operator, or adjust annotations.")
            ],
            "CA2234" =>
            [
                new CodeFixOptionDto(
                    FixId: "pass_system_uri",
                    Title: "Pass System.Uri instead of string",
                    Description: "CA2234: use Uri overload for HTTP client calls where applicable.")
            ],
            _ => []
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
