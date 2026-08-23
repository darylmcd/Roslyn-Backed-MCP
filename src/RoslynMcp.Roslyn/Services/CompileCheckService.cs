using System.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompileCheckService : ICompileCheckService
{
    private readonly IWorkspaceManager _workspace;
    private readonly ILogger<CompileCheckService> _logger;

    public CompileCheckService(IWorkspaceManager workspace, ILogger<CompileCheckService> logger)
    {
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// Per-invocation mutable accumulator threaded through the phase helpers. Keeps
    /// the outer <see cref="CheckAsync"/> signature stable (hot-path) while moving
    /// the diagnostic-collection inner loop into focused helpers.
    /// </summary>
    private sealed class CompileCheckAccumulator
    {
        public List<DiagnosticDto> Diagnostics { get; } = new();
        public int ErrorCount;
        public int WarningCount;
        public int CompletedProjects;
        public bool Cancelled;
    }

    public async Task<CompileCheckDto> CheckAsync(
        string workspaceId,
        CompileCheckOptions options,
        CancellationToken ct)
    {
        var (projectFilter, emitValidation, severityFilter, fileFilter, offset, limit, fileFilters) = options;
        var sw = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);

        var minSeverity = ParseMinimumSeverity(severityFilter);
        var normalizedFileFilters = NormalizeFileFilters(fileFilter, fileFilters);
        var (projectList, fileScopeHint) = ResolveProjectScope(solution, projectFilter, normalizedFileFilters);

        var acc = new CompileCheckAccumulator();

        try
        {
            await CollectDiagnosticsAsync(projectList, emitValidation, minSeverity, normalizedFileFilters, acc, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            acc.Cancelled = true;
            _logger.LogWarning(
                "compile_check cancelled after {Completed}/{Total} projects",
                acc.CompletedProjects, projectList.Count);
        }

        var pagedDiagnostics = acc.Diagnostics.Skip(offset).Take(limit).ToList();
        sw.Stop();

        var hint = BuildHint(projectFilter, projectList.Count, acc, fileScopeHint);

        // compile-check-multi-project-fallback-structured-scope: surface the requested-vs-actual
        // compile scope as structured fields so callers can detect the silent file-filter widening
        // without string-matching restoreHint prose. fileScopeHint (not `hint`) is the fallback
        // signal.
        var requestedScope = ComputeRequestedScope(projectFilter, normalizedFileFilters);
        // projectList.Count > 0 guard: the zero-resolution file-scope arm sets fileScopeHint but
        // compiles nothing, so reporting ActualScope=="solution" there would claim a widening
        // that never happened. Only the multi-project fallback actually widens.
        var actualScope = fileScopeHint is not null && projectList.Count > 0 ? ScopeSolution : requestedScope;

        return new CompileCheckDto(
            // A true Success requires that we actually evaluated at least one project.
            // CompletedProjects==0 with TotalProjects==0 always flips Success false; when
            // the filter matched no projects (projectList.Count==0), BuildHint synthesizes
            // an actionable hint instead of a silent green response.
            Success: acc.ErrorCount == 0 && !acc.Cancelled && acc.CompletedProjects > 0,
            ErrorCount: acc.ErrorCount,
            WarningCount: acc.WarningCount,
            TotalDiagnostics: acc.Diagnostics.Count,
            ReturnedDiagnostics: pagedDiagnostics.Count,
            Offset: offset,
            Limit: limit,
            HasMore: offset + pagedDiagnostics.Count < acc.Diagnostics.Count,
            Diagnostics: pagedDiagnostics,
            ElapsedMs: sw.ElapsedMilliseconds,
            RestoreHint: hint,
            Cancelled: acc.Cancelled,
            CompletedProjects: acc.CompletedProjects,
            TotalProjects: projectList.Count,
            RequestedScope: requestedScope,
            ActualScope: actualScope);
    }

    /// <summary>Scope vocabulary shared by <c>RequestedScope</c>/<c>ActualScope</c>.</summary>
    private const string ScopeFiles = "files";
    private const string ScopeProject = "project";
    private const string ScopeSolution = "solution";

    /// <summary>
    /// Classifies the compile scope the caller asked for. Mirrors
    /// <see cref="ResolveProjectScope"/>'s precedence exactly: a project filter short-circuits
    /// before file filters are considered, so a call supplying both <c>projectName</c> and
    /// <c>files</c> is a <c>"project"</c> request. Takes the already-normalized file set (rather
    /// than the raw options) so whitespace-only file arguments — which
    /// <see cref="NormalizeFileFilters"/> discards — are not misreported as a file scope.
    /// </summary>
    private static string ComputeRequestedScope(
        string? projectFilter,
        IReadOnlySet<string>? normalizedFileFilters)
    {
        if (!string.IsNullOrWhiteSpace(projectFilter)) return ScopeProject;
        if (normalizedFileFilters is not null && normalizedFileFilters.Count > 0) return ScopeFiles;
        return ScopeSolution;
    }

    /// <summary>
    /// Per-project diagnostic-collection loop. Acquires each project's compilation
    /// (or emits it when <paramref name="emitValidation"/> is true) and appends
    /// every non-filtered diagnostic to <paramref name="acc"/>. Throws
    /// <see cref="OperationCanceledException"/> to let the caller set the cancelled
    /// flag with an accurate completed-projects count.
    /// </summary>
    private static async Task CollectDiagnosticsAsync(
        IReadOnlyList<Project> projectList,
        bool emitValidation,
        DiagnosticSeverity? minSeverity,
        IReadOnlySet<string>? normalizedFileFilters,
        CompileCheckAccumulator acc,
        CancellationToken ct)
    {
        foreach (var project in projectList)
        {
            ct.ThrowIfCancellationRequested();

            var compilation = await project.GetCompilationAsync(ct).ConfigureAwait(false);
            if (compilation is null) { acc.CompletedProjects++; continue; }

            var diagnostics = emitValidation
                ? EmitAndGetDiagnostics(compilation, ct)
                : compilation.GetDiagnostics(ct);

            AppendFilteredDiagnostics(diagnostics, minSeverity, normalizedFileFilters, acc);

            acc.CompletedProjects++;
        }
    }

    /// <summary>
    /// Runs a throwaway PE emit to surface diagnostics the simple GetDiagnostics
    /// path can miss (unreferenced metadata at emit time). The MemoryStream sink
    /// is discarded — we only want the diagnostic list.
    /// </summary>
    private static IEnumerable<Diagnostic> EmitAndGetDiagnostics(Compilation compilation, CancellationToken ct)
    {
        var emitOptions = new EmitOptions(metadataOnly: false);
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream, options: emitOptions, cancellationToken: ct);
        return emitResult.Diagnostics;
    }

    /// <summary>
    /// Filters a project's raw diagnostics by severity + file scope and pushes the
    /// survivors onto <paramref name="acc"/>. Hidden diagnostics and
    /// below-minSeverity diagnostics are dropped silently; file-filter misses are
    /// also dropped. Error/Warning counts are updated in the accumulator as a
    /// side-effect so the caller avoids a second pass.
    /// </summary>
    private static void AppendFilteredDiagnostics(
        IEnumerable<Diagnostic> diagnostics,
        DiagnosticSeverity? minSeverity,
        IReadOnlySet<string>? normalizedFileFilters,
        CompileCheckAccumulator acc)
    {
        foreach (var diag in diagnostics)
        {
            var lineSpan = diag.Location.GetMappedLineSpan();
            if (!ShouldReportDiagnostic(diag, minSeverity, normalizedFileFilters, lineSpan)) continue;

            if (diag.Severity == DiagnosticSeverity.Error) acc.ErrorCount++;
            else if (diag.Severity == DiagnosticSeverity.Warning) acc.WarningCount++;

            acc.Diagnostics.Add(BuildDiagnosticDto(diag, lineSpan));
        }
    }

    /// <summary>
    /// Returns true when the diagnostic survives the severity + file-scope filter.
    /// Hidden diagnostics never survive; below-<paramref name="minSeverity"/>
    /// diagnostics are dropped; when <paramref name="normalizedFileFilter"/> is
    /// non-null, the diagnostic must have a non-empty path that resolves to the
    /// same full path (case-insensitive).
    /// </summary>
    private static bool ShouldReportDiagnostic(
        Diagnostic diag,
        DiagnosticSeverity? minSeverity,
        IReadOnlySet<string>? normalizedFileFilters,
        FileLinePositionSpan lineSpan)
    {
        if (diag.Severity == DiagnosticSeverity.Hidden) return false;
        if (minSeverity is not null && diag.Severity < minSeverity) return false;

        if (normalizedFileFilters is null || normalizedFileFilters.Count == 0) return true;

        var diagPath = lineSpan.Path;
        if (string.IsNullOrEmpty(diagPath)) return false;
        return normalizedFileFilters.Contains(Path.GetFullPath(diagPath));
    }

    /// <summary>
    /// Materializes a <see cref="DiagnosticDto"/> from a Roslyn <see cref="Diagnostic"/>
    /// plus its resolved <paramref name="lineSpan"/>. Pulled out of
    /// <see cref="AppendFilteredDiagnostics"/> so the latter stays under cc 10.
    /// </summary>
    private static DiagnosticDto BuildDiagnosticDto(Diagnostic diag, FileLinePositionSpan lineSpan)
    {
        var startLine = lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : (int?)null;
        var startColumn = lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : (int?)null;
        var endLine = lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : (int?)null;
        var endColumn = lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : (int?)null;
        return new DiagnosticDto(
            Id: diag.Id,
            Message: diag.GetMessage(),
            Severity: diag.Severity.ToString(),
            Category: diag.Descriptor.Category,
            FilePath: lineSpan.Path,
            StartLine: startLine,
            StartColumn: startColumn,
            EndLine: endLine,
            EndColumn: endColumn,
            Location: SymbolMapper.ToOptionalLocationDto(
                lineSpan.Path, startLine, startColumn, endLine, endColumn));
    }

    /// <summary>
    /// Combines the zero-projects diagnostic hint (Item #6) with the CS0234-flood
    /// restore hint into a single user-facing message, returning <see langword="null"/>
    /// when neither applies. Mirrors the pre-extraction ordering (zero-projects
    /// first, space, then restore) to preserve the exact wire string that existing
    /// tests assert against.
    /// </summary>
    private static string? BuildHint(
        string? projectFilter,
        int projectCount,
        CompileCheckAccumulator acc,
        string? fileScopeHint)
    {
        // Heuristic: many CS0234 ("type or namespace not found") errors across the solution
        // usually indicate NuGet packages haven't been restored.
        var cs0234Count = acc.Diagnostics.Count(d => d.Id == "CS0234");
        var restoreHint = cs0234Count >= 10
            ? $"Detected {cs0234Count} CS0234 (type or namespace not found) errors. " +
              "This usually means NuGet packages are not restored. " +
              "Run 'dotnet restore' on the solution, then call workspace_reload."
            : null;

        // Item #6: compile-check-zero-projects-claimed-success-after-reload.
        // When the filter (or a mid-reload race) leaves us with zero projects to evaluate,
        // returning Success=true with CompletedProjects=0 lets agents trust a vacuous pass
        // and ship broken code. Fail loud with a structured hint — this mirrors what the
        // SampleSolution audit §9.8 repro produced (success:true, completedProjects:0,
        // staleAction:auto-reloaded) and is the specific shape that gave the false-green.
        // fileScopeHint is null guard: the zero-resolution file-scope arm (added by
        // compile-check-zero-resolution-false-success) now also yields projectCount==0, and it
        // already carries a precise, actionable hint. Emitting the generic "workspace may have
        // completed a reload" text alongside it would mislead. This combination could not occur
        // before that fix, since the file-filter fallback always produced projectCount > 0.
        string? zeroProjectsHint = null;
        if (!acc.Cancelled && projectCount == 0 && fileScopeHint is null)
        {
            zeroProjectsHint = string.IsNullOrWhiteSpace(projectFilter)
                ? "compile_check evaluated 0 projects. The workspace may have completed a reload without re-populating its project list — call workspace_reload explicitly and retry, or verify workspace_load succeeded."
                : $"compile_check evaluated 0 projects: projectFilter '{projectFilter}' did not match any project in the workspace. Project names are matched case-insensitively against Project.Name (e.g. 'MyProject', not 'MyProject.csproj'). Call workspace_status for the current project list.";
        }

        var joined = string.Join(" ", new[] { zeroProjectsHint, restoreHint, fileScopeHint }
            .Where(static hint => !string.IsNullOrWhiteSpace(hint)));
        return string.IsNullOrEmpty(joined) ? null : joined;
    }

    private static IReadOnlySet<string>? NormalizeFileFilters(string? fileFilter, IReadOnlyList<string>? fileFilters)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddNormalized(fileFilter, normalized);
        if (fileFilters is not null)
        {
            foreach (var file in fileFilters)
            {
                AddNormalized(file, normalized);
            }
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static void AddNormalized(string? filePath, HashSet<string> normalized)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        normalized.Add(Path.GetFullPath(filePath));
    }

    private static (IReadOnlyList<Project> Projects, string? ScopeHint) ResolveProjectScope(
        Solution solution,
        string? projectFilter,
        IReadOnlySet<string>? normalizedFileFilters)
    {
        var filteredProjects = ProjectFilterHelper.FilterProjects(solution, projectFilter).ToList();
        if (!string.IsNullOrWhiteSpace(projectFilter) || normalizedFileFilters is null || normalizedFileFilters.Count == 0)
        {
            return (filteredProjects, null);
        }

        var owningProjects = FindOwningProjects(solution, normalizedFileFilters);
        if (owningProjects.Count == 1)
        {
            return ([owningProjects.Single()], null);
        }

        // compile-check-zero-resolution-false-success: a file scope that resolves to NO loaded
        // workspace document must not widen to the full project list. Widening compiled every
        // project and then dropped every returned diagnostic in ShouldReportDiagnostic (none of
        // them can match a path that is not in the workspace), so a solution carrying thousands
        // of real errors reported Success=true/ErrorCount=0. Returning an empty project list
        // drives CompletedProjects==0/TotalProjects==0, which the Success precondition already
        // treats as a failure — the same contract Item #6 established for a projectFilter miss.
        if (owningProjects.Count == 0)
        {
            return ([],
                "compile_check file scope did not resolve to any loaded workspace document; no project was compiled and no diagnostics were evaluated. Verify the supplied file paths exist in the loaded workspace (call workspace_status), or omit the file scope to compile the full solution.");
        }

        // owningProjects.Count > 1: the files genuinely span multiple projects, so widening the
        // compile and filtering the returned diagnostics by path is the correct behaviour.
        return (filteredProjects,
            "compile_check file filter fallback: supplied file scope resolved to multiple projects; compiled the full project scope and filtered returned diagnostics by file path.");
    }

    private static HashSet<Project> FindOwningProjects(Solution solution, IReadOnlySet<string> normalizedFileFilters)
    {
        var owningProjects = new HashSet<Project>();
        var unresolvedFiles = new HashSet<string>(normalizedFileFilters, StringComparer.OrdinalIgnoreCase);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is null)
                {
                    continue;
                }

                var documentPath = Path.GetFullPath(document.FilePath);
                if (normalizedFileFilters.Contains(documentPath))
                {
                    owningProjects.Add(project);
                    unresolvedFiles.Remove(documentPath);
                }
            }
        }

        return unresolvedFiles.Count == 0 ? owningProjects : [];
    }

    private static DiagnosticSeverity? ParseMinimumSeverity(string? severityFilter)
    {
        if (string.IsNullOrWhiteSpace(severityFilter)) return null;
        return severityFilter.Trim().ToLowerInvariant() switch
        {
            "error" => DiagnosticSeverity.Error,
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            "hidden" => DiagnosticSeverity.Hidden,
            _ => null
        };
    }
}
