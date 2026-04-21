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
        var (projectFilter, emitValidation, severityFilter, fileFilter, offset, limit) = options;
        var sw = Stopwatch.StartNew();
        var solution = _workspace.GetCurrentSolution(workspaceId);
        var projectList = ProjectFilterHelper.FilterProjects(solution, projectFilter).ToList();

        var minSeverity = ParseMinimumSeverity(severityFilter);
        var normalizedFileFilter = string.IsNullOrWhiteSpace(fileFilter)
            ? null
            : Path.GetFullPath(fileFilter);

        var acc = new CompileCheckAccumulator();

        try
        {
            await CollectDiagnosticsAsync(projectList, emitValidation, minSeverity, normalizedFileFilter, acc, ct)
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

        var hint = BuildHint(projectFilter, projectList.Count, acc);

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
            TotalProjects: projectList.Count);
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
        string? normalizedFileFilter,
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

            AppendFilteredDiagnostics(diagnostics, minSeverity, normalizedFileFilter, acc);

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
        string? normalizedFileFilter,
        CompileCheckAccumulator acc)
    {
        foreach (var diag in diagnostics)
        {
            var lineSpan = diag.Location.GetMappedLineSpan();
            if (!ShouldReportDiagnostic(diag, minSeverity, normalizedFileFilter, lineSpan)) continue;

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
        string? normalizedFileFilter,
        FileLinePositionSpan lineSpan)
    {
        if (diag.Severity == DiagnosticSeverity.Hidden) return false;
        if (minSeverity is not null && diag.Severity < minSeverity) return false;

        if (normalizedFileFilter is null) return true;

        var diagPath = lineSpan.Path;
        if (string.IsNullOrEmpty(diagPath)) return false;
        return Path.GetFullPath(diagPath).Equals(normalizedFileFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Materializes a <see cref="DiagnosticDto"/> from a Roslyn <see cref="Diagnostic"/>
    /// plus its resolved <paramref name="lineSpan"/>. Pulled out of
    /// <see cref="AppendFilteredDiagnostics"/> so the latter stays under cc 10.
    /// </summary>
    private static DiagnosticDto BuildDiagnosticDto(Diagnostic diag, FileLinePositionSpan lineSpan)
    {
        return new DiagnosticDto(
            Id: diag.Id,
            Message: diag.GetMessage(),
            Severity: diag.Severity.ToString(),
            Category: diag.Descriptor.Category,
            FilePath: lineSpan.Path,
            StartLine: lineSpan.IsValid ? lineSpan.StartLinePosition.Line + 1 : null,
            StartColumn: lineSpan.IsValid ? lineSpan.StartLinePosition.Character + 1 : null,
            EndLine: lineSpan.IsValid ? lineSpan.EndLinePosition.Line + 1 : null,
            EndColumn: lineSpan.IsValid ? lineSpan.EndLinePosition.Character + 1 : null);
    }

    /// <summary>
    /// Combines the zero-projects diagnostic hint (Item #6) with the CS0234-flood
    /// restore hint into a single user-facing message, returning <see langword="null"/>
    /// when neither applies. Mirrors the pre-extraction ordering (zero-projects
    /// first, space, then restore) to preserve the exact wire string that existing
    /// tests assert against.
    /// </summary>
    private static string? BuildHint(string? projectFilter, int projectCount, CompileCheckAccumulator acc)
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
        string? zeroProjectsHint = null;
        if (!acc.Cancelled && projectCount == 0)
        {
            zeroProjectsHint = projectFilter is null
                ? "compile_check evaluated 0 projects. The workspace may have completed a reload without re-populating its project list — call workspace_reload explicitly and retry, or verify workspace_load succeeded."
                : $"compile_check evaluated 0 projects: projectFilter '{projectFilter}' did not match any project in the workspace. Project names are matched case-insensitively against Project.Name (e.g. 'MyProject', not 'MyProject.csproj'). Call workspace_status for the current project list.";
        }

        return zeroProjectsHint is not null && restoreHint is not null
            ? zeroProjectsHint + " " + restoreHint
            : zeroProjectsHint ?? restoreHint;
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
