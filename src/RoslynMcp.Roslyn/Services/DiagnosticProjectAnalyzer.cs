using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Collects compiler, generator, and analyzer diagnostics for one project while preserving the
/// effective analyzer-severity policy used by Error-only query optimization.
/// </summary>
internal sealed class DiagnosticProjectAnalyzer(
    ICompilationCache compilationCache,
    IUnexpectedExceptionReporter? exceptionReporter)
{
    public async Task<DiagnosticProjectAnalysisResult> AnalyzeAsync(
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
            compilerAll.Add(miss);
            compilerFiltered.Add(miss);
            return new DiagnosticProjectAnalysisResult(
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
            return new DiagnosticProjectAnalysisResult(
                compilerAll,
                compilerFiltered,
                analyzerAll,
                analyzerFiltered,
                raw);
        }

        var compilationWithAnalyzers = await compilationCache
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

        return new DiagnosticProjectAnalysisResult(
            compilerAll,
            compilerFiltered,
            analyzerAll,
            analyzerFiltered,
            raw);
    }

    private bool ProjectHasEffectiveErrorAnalyzer(
        Project project,
        Compilation compilation,
        CancellationToken ct)
    {
        foreach (var reference in project.AnalyzerReferences)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadProbeValues(
                    () => reference.GetAnalyzers(project.Language),
                    out ImmutableArray<DiagnosticAnalyzer> analyzers))
            {
                return true;
            }

            foreach (var analyzer in analyzers)
            {
                if (!TryReadProbeValues(
                        () => analyzer.SupportedDiagnostics,
                        out ImmutableArray<DiagnosticDescriptor> descriptors))
                {
                    return true;
                }

                foreach (var descriptor in descriptors)
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

    private bool TryReadProbeValues<T>(
        Func<ImmutableArray<T>> read,
        out ImmutableArray<T> values)
    {
        try
        {
            values = read();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            UnexpectedExceptionReporting.Report(
                exceptionReporter,
                ex,
                UnexpectedExceptionCategory.AnalyzerLoad);
            values = [];
            return false;
        }
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

    internal static ReportDiagnostic GetEffectiveReportDiagnostic(
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
}

internal sealed record DiagnosticProjectAnalysisResult(
    List<DiagnosticDto> CompilerAll,
    List<DiagnosticDto> CompilerFiltered,
    List<DiagnosticDto> AnalyzerAll,
    List<DiagnosticDto> AnalyzerFiltered,
    List<Diagnostic> Raw);
