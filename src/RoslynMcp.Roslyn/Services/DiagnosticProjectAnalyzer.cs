using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Collects compiler, generator, and analyzer diagnostics for one project while preserving the
/// effective analyzer-severity policy used by Error-only query optimization.
/// </summary>
internal sealed class DiagnosticProjectAnalyzer(
    ICompilationCache compilationCache,
    IUnexpectedExceptionReporter? exceptionReporter)
{
    private readonly DiagnosticSeverityPolicy _severityPolicy = new(exceptionReporter);

    public async Task<DiagnosticProjectAnalysisResult> AnalyzeAsync(
        string workspaceId,
        Project project,
        string? fileFilter,
        string? diagnosticIdFilter,
        DiagnosticSeverity? minSeverity,
        CancellationToken ct)
    {
        var snapshot = await compilationCache
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
            return new DiagnosticProjectAnalysisResult(
                [miss],
                [miss],
                [],
                [],
                []);
        }

        var compiler = DiagnosticSeverityPolicy.Project(
            snapshot.Compilation.GetDiagnostics(ct).Concat(snapshot.GeneratorDiagnostics),
            fileFilter,
            diagnosticIdFilter,
            minSeverity);

        // Avoid an analyzer pass for an Error-only query only when descriptor defaults,
        // command-line options, and analyzer-config overrides all prove that no loaded analyzer
        // can contribute an Error.
        if (minSeverity == DiagnosticSeverity.Error
            && !_severityPolicy.ProjectHasEffectiveErrorAnalyzer(project, snapshot.Compilation, ct))
        {
            return new DiagnosticProjectAnalysisResult(
                compiler.All,
                compiler.Filtered,
                [],
                [],
                compiler.Raw);
        }

        var analyzer = new DiagnosticProjection([], [], []);
        var compilationWithAnalyzers = await compilationCache
            .GetCompilationWithAnalyzersAsync(workspaceId, project, ct)
            .ConfigureAwait(false);
        if (compilationWithAnalyzers is not null)
        {
            var analyzerDiagnostics = await compilationWithAnalyzers
                .GetAnalyzerDiagnosticsAsync(ct)
                .ConfigureAwait(false);
            analyzer = DiagnosticSeverityPolicy.Project(
                analyzerDiagnostics,
                fileFilter,
                diagnosticIdFilter,
                minSeverity);
        }

        return new DiagnosticProjectAnalysisResult(
            compiler.All,
            compiler.Filtered,
            analyzer.All,
            analyzer.Filtered,
            [.. compiler.Raw, .. analyzer.Raw]);
    }
}

internal sealed record DiagnosticProjectAnalysisResult(
    List<DiagnosticDto> CompilerAll,
    List<DiagnosticDto> CompilerFiltered,
    List<DiagnosticDto> AnalyzerAll,
    List<DiagnosticDto> AnalyzerFiltered,
    List<Diagnostic> Raw);
